using System.Data;
using System.Data.SqlClient;
using Microsoft.Extensions.Options;
using Site_2024.Web.Api.Models;

namespace Site_2024.Web.Api.Services;

// SQL app lock serializes requests, persistent journal survives API restarts.
// A mutation whose response was lost is never assumed to have succeeded.
public sealed class InventoryCleanupShopifyCoordinator
{
    private const string DevShop = "gr-sons-shipping-dev.myshopify.com";
    private readonly IShopifyAdminService _shopify;
    private readonly ShopifySettings _settings;
    private readonly string _connectionString;
    private readonly ILogger<InventoryCleanupShopifyCoordinator> _logger;

    public InventoryCleanupShopifyCoordinator(
        IShopifyAdminService shopify,
        IOptions<ShopifySettings> options,
        IConfiguration configuration,
        ILogger<InventoryCleanupShopifyCoordinator> logger)
    {
        _shopify = shopify;
        _settings = options.Value;
        _connectionString = configuration.GetConnectionString("connMSSQL")
            ?? throw new InvalidOperationException("Missing connMSSQL connection string.");
        _logger = logger;
    }

    public sealed record PreviewResult(
        bool CanDelete, string Reason, long? ShopifyProductId,
        string? ShopDomain, string? ShopifyProductStatus, string? OperationState);

    public sealed record DeleteResult(bool Deleted, string Message);

    private sealed record PartLink(long? ProductId, string? OperationState,
                                   string? OperationShopDomain, long? OperationProductId);

    private bool ConfigGateOpen =>
        _settings.AllowInventoryCleanupShopifyDeleteWrites
        && !string.IsNullOrWhiteSpace(_settings.ShopDomain)
        && string.Equals(_settings.ShopDomain.Trim(),
            _settings.InventoryCleanupAuthorizedShopDomain.Trim(), StringComparison.OrdinalIgnoreCase)
        && (string.Equals(_settings.ShopDomain.Trim(), DevShop, StringComparison.OrdinalIgnoreCase)
            || _settings.AllowProductionInventoryCleanupShopifyDeletes);

    private async Task<string> CheckStoreAsync()
    {
        if (!ConfigGateOpen)
            throw new InvalidOperationException(
                "Coordinated Shopify deletion is disabled or this store is not explicitly authorized.");
        string actual = await _shopify.GetConnectedShopDomainAsync();
        if (!_settings.IsInventoryCleanupShopAllowed(actual))
            throw new InvalidOperationException(
                "Shopify connected-store identity did not match the independently authorized store.");
        return actual;
    }

    private async Task<PartLink?> ReadLinkAsync(SqlConnection connection, int partId)
    {
        await using var command = new SqlCommand(@"
SELECT p.ShopifyProductId, o.State, o.ShopDomain, o.ShopifyProductId
FROM dbo.Parts p LEFT JOIN dbo.InventoryCleanupShopifyOperations o ON o.PartId=p.id
WHERE p.id=@PartId;", connection);
        command.Parameters.Add("@PartId", SqlDbType.Int).Value = partId;
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;
        return new PartLink(
            reader.IsDBNull(0) ? null : reader.GetInt64(0),
            reader.IsDBNull(1) ? null : reader.GetString(1),
            reader.IsDBNull(2) ? null : reader.GetString(2),
            reader.IsDBNull(3) ? null : reader.GetInt64(3));
    }

    private static string? GetMismatch(ShopifyCleanupProductSnapshot snapshot, int partId, long productId)
    {
        if (!snapshot.Exists) return null; // Already deleted externally: no mutation will be sent.
        if (!string.Equals(snapshot.ProductGid,
                $"gid://shopify/Product/{productId}", StringComparison.Ordinal))
            return "Shopify returned a different product ID.";
        if (!string.Equals(snapshot.Status, "DRAFT", StringComparison.OrdinalIgnoreCase))
            return "Shopify product must be DRAFT (unpublished) before permanent deletion.";
        if (!snapshot.Tags.Contains("Site_2024", StringComparer.OrdinalIgnoreCase)
            || !snapshot.Tags.Contains($"SitePartId_{partId}", StringComparer.OrdinalIgnoreCase))
            return "Shopify product ownership tags do not match this local part.";
        return null;
    }

    public async Task<PreviewResult> PreviewAsync(int partId)
    {
        if (!ConfigGateOpen)
            return new(false,
                "Shopify deletion is disabled until the isolated store and write gate are approved.",
                null, null, null, null);
        string actual = await CheckStoreAsync();
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();
        PartLink? part = await ReadLinkAsync(connection, partId);
        if (part?.ProductId is not long id || id <= 0)
            return new(false, "Part lacks a valid Shopify product link.", null, actual, null, null);
        if (part.OperationState != null &&
            (part.OperationProductId != id ||
             !string.Equals(part.OperationShopDomain, actual, StringComparison.OrdinalIgnoreCase)))
            return new(false, "Existing operation belongs to a different product/store. Manual reconciliation required.",
                id, actual, null, part.OperationState);
        if (part.OperationState == "Completed")
            return new(false, "A completed operation exists while the local part remains. Manual review required.",
                id, actual, null, part.OperationState);

        if (part.OperationState == "ShopifyDeleted")
            return new(true, "Shopify deletion confirmed; finishing local cleanup is available.",
                id, actual, "Deleted", part.OperationState);

        ShopifyCleanupProductSnapshot snapshot = await _shopify.GetCleanupProductSnapshotAsync(id);
        string? mismatch = GetMismatch(snapshot, partId, id);
        return new(mismatch == null,
            mismatch ?? (snapshot.Exists
                ? "Verified matching draft Shopify product. Both records will be deleted."
                : "Shopify product is already absent; the recorded link can be reconciled."),
            id, actual, snapshot.Exists ? snapshot.Status : "Missing", part.OperationState);
    }

    private static async Task ExecAsync(SqlConnection connection, string procedure,
        int partId, long productId, string shopDomain, int? userId = null)
    {
        await using var command = new SqlCommand(procedure, connection) {
            CommandType = CommandType.StoredProcedure, CommandTimeout = 90
        };
        command.Parameters.Add("@PartId", SqlDbType.Int).Value = partId;
        command.Parameters.Add("@ShopDomain", SqlDbType.NVarChar, 255).Value = shopDomain;
        command.Parameters.Add("@ShopifyProductId", SqlDbType.BigInt).Value = productId;
        if (userId.HasValue)
            command.Parameters.Add("@UserId", SqlDbType.Int).Value = userId.Value;
        await command.ExecuteNonQueryAsync();
    }

    private async Task RecordErrorAsync(SqlConnection connection, int partId, string shopDomain, Exception ex)
    {
        try
        {
            await using var command = new SqlCommand("dbo.InventoryCleanupShopify_RecordError", connection) {
                CommandType = CommandType.StoredProcedure
            };
            command.Parameters.Add("@PartId", SqlDbType.Int).Value = partId;
            command.Parameters.Add("@ShopDomain", SqlDbType.NVarChar, 255).Value = shopDomain;
            // Do not persist authentication tokens or entire API response bodies.
            command.Parameters.Add("@Message", SqlDbType.NVarChar, 2000).Value =
                "Cleanup interrupted. Reconcile the connected Shopify product before retrying. " +
                ex.GetType().Name;
            await command.ExecuteNonQueryAsync();
        }
        catch (Exception logEx)
        {
            _logger.LogWarning(logEx, "Unable to persist Inventory Cleanup failure for PartId {PartId}", partId);
        }
    }

    public async Task<DeleteResult> DeleteAsync(int partId, int adminUserId)
    {
        // Explicit gate and connected-identity read are required for EACH request.
        string actual = await CheckStoreAsync();

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();
        // Session-level SQL lock held for the Shopify HTTP request. Subsequent
        // requests for the same part must wait; a process crash releases the lock.
        await using var lockCommand = new SqlCommand(@"
DECLARE @r int;
EXEC @r=sys.sp_getapplock @Resource=@resource, @LockMode='Exclusive',
    @LockOwner='Session', @LockTimeout=3000;
SELECT @r;", connection);
        lockCommand.Parameters.Add("@resource", SqlDbType.NVarChar, 255).Value =
            $"inventory-cleanup-shopify-part-{partId}";
        int lockResult = Convert.ToInt32(await lockCommand.ExecuteScalarAsync());
        if (lockResult < 0)
            return new(false, "Another deletion attempt is running for this part. Retry after it finishes.");

        try
        {
            PartLink? part = await ReadLinkAsync(connection, partId);
            if (part?.ProductId is not long productId || productId <= 0)
                return new(false, "Part no longer has a valid Shopify product link.");

            // All local eligibility rules are rechecked under a SQL UPDLOCK
            // before persisting the operation, and again during finalization.
            await ExecAsync(connection, "dbo.InventoryCleanupShopify_Begin",
                partId, productId, actual, adminUserId);

            part = await ReadLinkAsync(connection, partId);
            if (part?.OperationState == "ShopifyDeleted")
            {
                await ExecAsync(connection, "dbo.InventoryCleanupShopify_Finalize",
                    partId, productId, actual, adminUserId);
                return new(true, "Shopify deletion was previously confirmed; local cleanup completed.");
            }

            ShopifyCleanupProductSnapshot snapshot = await _shopify.GetCleanupProductSnapshotAsync(productId);
            string? mismatch = GetMismatch(snapshot, partId, productId);
            if (mismatch != null) return new(false, mismatch);

            try
            {
                if (snapshot.Exists)
                {
                    // A failed/ambiguous previous request is verified by reading
                    // the exact product before ever calling productDelete again.
                    await ExecAsync(connection, "dbo.InventoryCleanupShopify_MarkAttempt",
                        partId, productId, actual);
                    await _shopify.DeleteProductForInventoryCleanupAsync(productId);
                }
                await ExecAsync(connection, "dbo.InventoryCleanupShopify_MarkDeleted",
                    partId, productId, actual);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Shopify cleanup attempt interrupted for part {PartId}: {Type}",
                    partId, ex.GetType().Name);
                await RecordErrorAsync(connection, partId, actual, ex);
                return new(false,
                    "Shopify deletion outcome requires verification. No local record was removed. Retry will recheck Shopify.");
            }

            try
            {
                await ExecAsync(connection, "dbo.InventoryCleanupShopify_Finalize",
                    partId, productId, actual, adminUserId);
                return new(true, "Shopify product and local listing were deleted. Photo cleanup is queued.");
            }
            catch (Exception ex)
            {
                await RecordErrorAsync(connection, partId, actual, ex);
                _logger.LogError(ex, "Shopify deleted but SQL finalization failed for part {PartId}", partId);
                return new(false,
                    "Shopify deletion confirmed, but local cleanup is pending. Retry to finish local cleanup.");
            }
        }
        finally
        {
            await using var release = new SqlCommand(
                "EXEC sys.sp_releaseapplock @Resource=@resource, @LockOwner='Session';", connection);
            release.Parameters.Add("@resource", SqlDbType.NVarChar, 255).Value =
                $"inventory-cleanup-shopify-part-{partId}";
            await release.ExecuteNonQueryAsync();
        }
    }
}
