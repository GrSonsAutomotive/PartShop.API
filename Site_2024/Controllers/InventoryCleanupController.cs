using System.Data;
using System.Data.SqlClient;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Site_2024.Web.Api.Interfaces;
using Site_2024.Web.Api.Models;
using Site_2024.Web.Api.Models.User;
using Site_2024.Web.Api.Services;

namespace Site_2024.Web.Api.Controllers;

// No stock is deleted from Shopify by this endpoint. Linked parts are deliberately
// ineligible until a resumable cross-system Shopify cleanup workflow is installed.
[ApiController]
[Route("api/admin/inventory-cleanup")]
[Authorize(Policy = "PartsDelete")]
public sealed class InventoryCleanupController : ControllerBase
{
    private const int MinimumDeletionAgeDays = 90;
    private readonly string _connectionString;
    private readonly IConfiguration _configuration;
    private readonly IWebHostEnvironment _environment;
    private readonly ILogger<InventoryCleanupController> _logger;
    private readonly IAuthenticationService<IUserAuthData> _authentication;
    private readonly ShopifyShippingJobStore _shippingJobs;
    private readonly ShopifySettings _shippingSettings;

    public InventoryCleanupController(
        IConfiguration configuration,
        IWebHostEnvironment environment,
        ILogger<InventoryCleanupController> logger,
        IAuthenticationService<IUserAuthData> authentication,
        ShopifyShippingJobStore shippingJobs,
        IOptions<ShopifySettings> shippingSettings)
    {
        _configuration = configuration;
        _environment = environment;
        _logger = logger;
        _authentication = authentication;
        _shippingJobs = shippingJobs;
        _shippingSettings = shippingSettings.Value;
        _connectionString = configuration.GetConnectionString("connMSSQL")
            ?? throw new InvalidOperationException("Missing connMSSQL database connection.");
    }

    public sealed class DeleteConfirmation
    {
        public int ConfirmPartId { get; set; }
    }

    [HttpGet]
    public async Task<IActionResult> Search(
        [FromQuery] string? query = null,
        [FromQuery] int olderThanDays = 90,
        [FromQuery] int pageIndex = 0,
        [FromQuery] int pageSize = 20)
    {
        if (olderThanDays < 1 || olderThanDays > 3650 || pageIndex < 0 ||
            pageSize < 1 || pageSize > 100 || query?.Length > 200)
            return BadRequest(new { message = "Invalid search options." });

        const string sql = @"
SELECT p.id, p.name, p.partnumber, p.datemodified,
       (SELECT COUNT(1) FROM dbo.PartImages pi WHERE pi.PartId=p.id) AS PhotoCount,
       CASE WHEN p.ShopifyProductId IS NOT NULL OR p.ShopifyVariantId IS NOT NULL
                   OR p.ShopifyInventoryItemId IS NOT NULL THEN CAST(1 AS bit)
            ELSE CAST(0 AS bit) END AS ShopifyLinked,
       COUNT(1) OVER() AS TotalCount,
       DATEDIFF(DAY, p.datemodified, SYSUTCDATETIME()) AS DaysSinceModified,
       p.UnavailableSinceUtc, DATEDIFF(DAY, p.UnavailableSinceUtc, SYSUTCDATETIME()) AS DaysUnavailable
FROM dbo.Parts p
JOIN dbo.Available a ON a.id=p.availableId
WHERE LOWER(LTRIM(RTRIM(a.status)))=N'unavailable'
  AND p.UnavailableSinceUtc<=DATEADD(DAY,-@OlderThanDays,SYSUTCDATETIME())
  AND (@Query=N'' OR p.name LIKE N'%'+@Query+N'%'
                     OR p.partnumber LIKE N'%'+@Query+N'%'
                     OR CONVERT(nvarchar(20),p.id)=@Query)
ORDER BY p.datemodified ASC, p.id ASC
OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;";

        var rows = new List<object>();
        var count = 0;
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@OlderThanDays", SqlDbType.Int).Value = olderThanDays;
        command.Parameters.Add("@Offset", SqlDbType.Int).Value = pageIndex * pageSize;
        command.Parameters.Add("@PageSize", SqlDbType.Int).Value = pageSize;
        command.Parameters.Add("@Query", SqlDbType.NVarChar, 200).Value = (query ?? "").Trim();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            count = reader.GetInt32(6);
            rows.Add(new {
                id = reader.GetInt32(0),
                name = reader.GetString(1),
                partNumber = reader.IsDBNull(2) ? null : reader.GetString(2),
                lastModified = reader.GetDateTime(3),
                photoCount = reader.GetInt32(4),
                shopifyLinked = reader.GetBoolean(5),
                daysSinceModified = reader.GetInt32(7),
                unavailableSince = reader.IsDBNull(8) ? (DateTime?)null : reader.GetDateTime(8),
                daysUnavailable = reader.IsDBNull(9) ? (int?)null : reader.GetInt32(9)
            });
        }
        return Ok(new { item = new { rows, totalCount = count, pageIndex, pageSize,
                                     minimumDeletionAgeDays = MinimumDeletionAgeDays } });
    }

    private async Task<(bool Found, bool CanDelete, string Reason,
                        DateTime LastModified, int DaysSinceModified,
                        DateTime? UnavailableSince, int DaysUnavailable,
                        bool ShopifyLinked, bool HasHistory)> GetEligibilityAsync(int id)
    {
        const string sql = @"
SELECT p.datemodified, a.status,
 CASE WHEN p.ShopifyProductId IS NOT NULL OR p.ShopifyVariantId IS NOT NULL
                OR p.ShopifyInventoryItemId IS NOT NULL THEN CAST(1 AS bit)
      ELSE CAST(0 AS bit) END AS ShopifyLinked,
 CASE WHEN EXISTS(SELECT 1 FROM dbo.PartSales WHERE PartId=p.id)
        OR EXISTS(SELECT 1 FROM dbo.RefundRequests WHERE PartId=p.id)
        OR EXISTS(SELECT 1 FROM dbo.RefundRequestItems WHERE PartId=p.id)
        OR EXISTS(SELECT 1 FROM dbo.RefundFinalizationItems WHERE PartId=p.id)
        OR EXISTS(SELECT 1 FROM dbo.RefundInventoryDispositionActions WHERE PartId=p.id)
        OR EXISTS(SELECT 1 FROM dbo.RefundInventoryDispositionItems WHERE PartId=p.id)
      THEN CAST(1 AS bit) ELSE CAST(0 AS bit) END AS HasHistory,
 CASE WHEN EXISTS (SELECT 1 FROM dbo.AdminDiscountCodes WHERE PartId=p.id)
      THEN CAST(1 AS bit) ELSE CAST(0 AS bit) END AS HasDiscount,
 DATEDIFF(DAY,p.datemodified,SYSUTCDATETIME()) AS DaysSinceModified,
 p.UnavailableSinceUtc, DATEDIFF(DAY,p.UnavailableSinceUtc,SYSUTCDATETIME()) AS DaysUnavailable
FROM dbo.Parts p JOIN dbo.Available a ON a.id=p.availableId
WHERE p.id=@PartId;";
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@PartId", SqlDbType.Int).Value = id;
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            return (false, false, "Part not found.", default, 0, null, 0, false, false);
        var date = reader.GetDateTime(0);
        var status = reader.GetString(1);
        var linked = reader.GetBoolean(2);
        var history = reader.GetBoolean(3);
        var discount = reader.GetBoolean(4);
        var days = reader.GetInt32(5);
        DateTime? unavailableSince = reader.IsDBNull(6) ? null : reader.GetDateTime(6);
        var daysUnavailable = reader.IsDBNull(7) ? 0 : reader.GetInt32(7);

        var reason = !string.Equals(status.Trim(), "Unavailable", StringComparison.OrdinalIgnoreCase)
            ? "Only unavailable parts can be deleted."
            : unavailableSince == null || unavailableSince > DateTime.UtcNow.AddDays(-MinimumDeletionAgeDays)
            ? $"Must remain unavailable for at least {MinimumDeletionAgeDays} days."
            : history
            ? "Sales or return history exists. Preserve this listing for accounting and refunds."
            : discount
            ? "A discount references this listing. Resolve the discount first."
            : linked
            ? "Shopify-linked part: coordinated Shopify deletion is not enabled yet."
            : "";
        return (true, reason.Length == 0, reason, date, days, unavailableSince, daysUnavailable, linked, history);
    }

    [HttpGet("{id:int}/preview")]
    public async Task<IActionResult> Preview(int id)
    {
        if (id <= 0) return BadRequest(new { message = "Invalid part ID." });
        var item = await GetEligibilityAsync(id);
        if (!item.Found) return NotFound(new { message = item.Reason });
        if (_shippingSettings.AsyncShippingJobsSchemaReady && await _shippingJobs.HasOpenAsync(id))
            return Ok(new { item = new { partId = id, canDelete = false,
              reason = "A shipping update is pending or requires attention.",
              item.LastModified, item.DaysSinceModified, item.UnavailableSince, item.DaysUnavailable,
              item.ShopifyLinked, item.HasHistory,
              minimumDeletionAgeDays = MinimumDeletionAgeDays } });
        return Ok(new { item = new { partId = id, item.CanDelete, item.Reason,
          item.LastModified, item.DaysSinceModified, item.UnavailableSince, item.DaysUnavailable,
          item.ShopifyLinked, item.HasHistory,
          minimumDeletionAgeDays = MinimumDeletionAgeDays } });
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, [FromBody] DeleteConfirmation? confirmation)
    {
        if (id <= 0 || confirmation?.ConfirmPartId != id)
            return BadRequest(new { message = "Type the exact Part ID to confirm deletion." });

        var user = _authentication.GetCurrentUser();
        if (user == null) return Unauthorized(new { message = "Sign in to delete parts." });
        var check = await GetEligibilityAsync(id);
        if (!check.Found) return NotFound(new { message = check.Reason });
        if (!check.CanDelete) return Conflict(new { message = check.Reason });
        if (_shippingSettings.AsyncShippingJobsSchemaReady && await _shippingJobs.HasOpenAsync(id))
            return Conflict(new { message = "A shipping update is still pending." });

        // The stored procedure repeats every precondition under a SQL row lock.
        // It archives the part/audit and queues photographs inside the same transaction.
        try
        {
            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();
            await using var command = new SqlCommand("dbo.InventoryCleanup_Delete", connection)
            {
                CommandType = CommandType.StoredProcedure,
                CommandTimeout = 60
            };
            command.Parameters.Add("@PartId", SqlDbType.Int).Value = id;
            command.Parameters.Add("@DeletedByUserId", SqlDbType.Int).Value = user.Id;
            command.Parameters.Add("@MinimumAgeDays", SqlDbType.Int).Value = MinimumDeletionAgeDays;
            await command.ExecuteNonQueryAsync();
            _logger.LogWarning("Inventory cleanup: Admin {UserId} deleted PartId {PartId}", user.Id, id);
        }
        catch (SqlException ex) when (ex.Number is >= 51001 and <= 51007)
        {
            return Conflict(new { message = ex.Message });
        }
        // A separate queue retains file paths even if filesystem deletion fails.
        var cleanup = await PurgePhotosAsync(id);
        return Ok(new { item = new { partId = id, deleted = true,
            photosDeleted = cleanup.Deleted, photoCleanupPending = cleanup.Pending,
            message = cleanup.Pending > 0
                ? "Listing deleted. Some photos still need cleanup; retry from Inventory Cleanup."
                : "Listing and queued photographs deleted." } });
    }

    [HttpPost("photos/retry")]
    public async Task<IActionResult> RetryPhotos()
    {
        var result = await PurgePhotosAsync(null);
        return Ok(new { item = new { result.Deleted, result.Pending } });
    }

    private async Task<(int Deleted, int Pending)> PurgePhotosAsync(int? partId)
    {
        var deleted = 0;
        var pending = 0;
        string root = _configuration["UploadStorage:RootPath"]
            ?? Path.Combine(_environment.WebRootPath
                ?? Path.Combine(_environment.ContentRootPath, "wwwroot"), "uploads");
        string itemsFolder = Path.GetFullPath(Path.Combine(root, "items"));
        var records = new List<(long Id, string Url)>();
        await using (var connection = new SqlConnection(_connectionString))
        {
            await connection.OpenAsync();
            string sql = @"SELECT TOP (100) Id, Url FROM dbo.InventoryCleanupPhotoQueue
WHERE Status IN (N'Pending',N'Failed')
  AND (@PartId IS NULL OR PartId=@PartId)
ORDER BY CASE WHEN Status=N'Pending' THEN 0 ELSE 1 END, Id";
            await using var cmd = new SqlCommand(sql, connection);
            cmd.Parameters.Add("@PartId", SqlDbType.Int).Value = (object?)partId ?? DBNull.Value;
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                records.Add((reader.GetInt64(0), reader.GetString(1)));
        }

        foreach (var record in records)
        {
            var status = "Deleted";
            string? error = null;
            try
            {
                const string prefix = "/uploads/items/";
                if (!record.Url.StartsWith(prefix, StringComparison.Ordinal) ||
                    record.Url.Substring(prefix.Length).Contains('/') ||
                    record.Url.Substring(prefix.Length).Contains('\\'))
                    throw new ArgumentException("Unsupported photo path. Manual inspection required.");
                var name = record.Url.Substring(prefix.Length);
                if (!Regex.IsMatch(name, @"\A[0-9a-fA-F-]{36}\.(jpg|jpeg|png|webp)\z",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                    throw new ArgumentException("Unexpected photo filename. Manual inspection required.");
                var path = Path.GetFullPath(Path.Combine(itemsFolder, name));
                if (!string.Equals(Path.GetDirectoryName(path), itemsFolder,
                    StringComparison.OrdinalIgnoreCase))
                    throw new ArgumentException("Photo path escaped the upload directory.");
                if (System.IO.File.Exists(path)) System.IO.File.Delete(path);
                deleted++;
            }
            catch (ArgumentException ex)
            {
                status = "ManualReview";
                error = ex.Message;
                pending++;
            }
            catch (Exception ex)
            {
                status = "Failed";
                error = ex.Message.Length > 1800 ? ex.Message[..1800] : ex.Message;
                pending++;
                _logger.LogError(ex, "Inventory photo purge failed for queue Id {Id}", record.Id);
            }

            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();
            await using var update = new SqlCommand(@"
UPDATE dbo.InventoryCleanupPhotoQueue
SET Status=@Status, Attempts=Attempts+1, LastError=@Error, DateModifiedUtc=SYSUTCDATETIME()
WHERE Id=@Id AND Status IN (N'Pending',N'Failed')", connection);
            update.Parameters.Add("@Id", SqlDbType.BigInt).Value = record.Id;
            update.Parameters.Add("@Status", SqlDbType.NVarChar, 20).Value = status;
            update.Parameters.Add("@Error", SqlDbType.NVarChar, 2000).Value = (object?)error ?? DBNull.Value;
            await update.ExecuteNonQueryAsync();
        }
        // Include files not yet attempted plus failures and manual-review items.
        await using (var connection = new SqlConnection(_connectionString))
        {
            await connection.OpenAsync();
            await using var count = new SqlCommand(@"SELECT COUNT(1)
FROM dbo.InventoryCleanupPhotoQueue
WHERE Status<>N'Deleted' AND (@PartId IS NULL OR PartId=@PartId)", connection);
            count.Parameters.Add("@PartId", SqlDbType.Int).Value = (object?)partId ?? DBNull.Value;
            pending = Convert.ToInt32(await count.ExecuteScalarAsync());
        }
        return (deleted, pending);
    }
}
