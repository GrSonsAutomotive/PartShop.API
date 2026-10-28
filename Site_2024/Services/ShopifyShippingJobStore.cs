using Microsoft.Data.SqlClient;
using Site_2024.Web.Api.Models;
using System.Data;

namespace Site_2024.Web.Api.Services
{
    // SQL is the source of truth for progress, not an in-memory task or HTTP request.
    // The filtered index guarantees one unfinished/attention-required job per part.
    public sealed class ShopifyShippingJobStore
    {
        private readonly string _connectionString;

        public ShopifyShippingJobStore(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("connMSSQL")
                ?? throw new InvalidOperationException("Missing SQL connection.");
        }

        private SqlConnection Connection() => new(_connectionString);

        private static ShopifyShippingReassignmentJob Map(SqlDataReader r) => new()
        {
            JobId = r.GetGuid(r.GetOrdinal("JobId")),
            PartId = r.GetInt32(r.GetOrdinal("PartId")),
            ShopifyProductId = r.GetInt64(r.GetOrdinal("ShopifyProductId")),
            FromPolicyId = r.GetInt32(r.GetOrdinal("FromPolicyId")),
            ToPolicyId = r.GetInt32(r.GetOrdinal("ToPolicyId")),
            RequestedByUserId = r.GetInt32(r.GetOrdinal("RequestedByUserId")),
            OriginalPublishedOnOnlineStore = r.GetBoolean(r.GetOrdinal("OriginalPublishedOnOnlineStore")),
            Status = r.GetString(r.GetOrdinal("Status")),
            IsOpen = r.GetBoolean(r.GetOrdinal("IsOpen")),
            CheckCount = r.GetInt32(r.GetOrdinal("CheckCount")),
            FailureCount = r.GetInt32(r.GetOrdinal("FailureCount")),
            OldCollectionContainsProduct = r.IsDBNull(r.GetOrdinal("OldCollectionContainsProduct"))
                ? null : r.GetBoolean(r.GetOrdinal("OldCollectionContainsProduct")),
            NewCollectionContainsProduct = r.IsDBNull(r.GetOrdinal("NewCollectionContainsProduct"))
                ? null : r.GetBoolean(r.GetOrdinal("NewCollectionContainsProduct")),
            NextCheckUtc = r.GetDateTime(r.GetOrdinal("NextCheckUtc")),
            CreatedUtc = r.GetDateTime(r.GetOrdinal("CreatedUtc")),
            UpdatedUtc = r.GetDateTime(r.GetOrdinal("UpdatedUtc")),
            CompletedUtc = r.IsDBNull(r.GetOrdinal("CompletedUtc"))
                ? null : r.GetDateTime(r.GetOrdinal("CompletedUtc")),
            LastError = r.IsDBNull(r.GetOrdinal("LastError"))
                ? null : r.GetString(r.GetOrdinal("LastError")),
            LeaseOwner = r.IsDBNull(r.GetOrdinal("LeaseOwner"))
                ? null : r.GetGuid(r.GetOrdinal("LeaseOwner"))
        };

        private const string Columns = @"JobId, PartId, ShopifyProductId,
            FromPolicyId, ToPolicyId, RequestedByUserId, OriginalPublishedOnOnlineStore,
            Status, IsOpen, CheckCount, FailureCount, OldCollectionContainsProduct,
            NewCollectionContainsProduct, NextCheckUtc, LeaseOwner,
            LastError, CreatedUtc, UpdatedUtc, CompletedUtc";

        public async Task<ShopifyShippingReassignmentJob?> GetLatestAsync(int partId,
            CancellationToken ct = default)
        {
            await using var conn = Connection();
            await conn.OpenAsync(ct);
            await using var cmd = new SqlCommand($@"SELECT TOP (1) {Columns}
                FROM dbo.ShopifyShippingReassignmentJobs
                WHERE PartId=@part ORDER BY CreatedUtc DESC", conn);
            cmd.Parameters.Add("@part", SqlDbType.Int).Value = partId;
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            return await reader.ReadAsync(ct) ? Map(reader) : null;
        }

        public async Task<bool> HasOpenAsync(int partId, CancellationToken ct = default)
        {
            await using var conn = Connection();
            await conn.OpenAsync(ct);
            await using var cmd = new SqlCommand(@"SELECT COUNT(1) FROM dbo.ShopifyShippingReassignmentJobs
                WHERE PartId=@part AND IsOpen=1", conn);
            cmd.Parameters.Add("@part", SqlDbType.Int).Value = partId;
            return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct)) > 0;
        }

        public async Task<Guid> EnqueueAsync(int partId, long productId, int fromPolicy,
            int toPolicy, int userId, CancellationToken ct = default)
        {
            Guid jobId = Guid.NewGuid();
            await using var conn = Connection();
            await conn.OpenAsync(ct);
            // Check the Parts row under a serializable lock, then enqueue.
            // The unique filtered index also prevents concurrent duplicate jobs.
            const string sql = @"
SET XACT_ABORT ON;
BEGIN TRAN;
DECLARE @dbPolicy INT, @dbProduct BIGINT, @dbAvailability INT, @dbQuantity INT;
DECLARE @AvailableId INT, @UnavailableId INT;
SELECT @AvailableId=Id FROM dbo.Available
WHERE Id=1 AND LOWER(LTRIM(RTRIM([Status])))=N'available';
SELECT @UnavailableId=MIN(Id) FROM dbo.Available
WHERE LOWER(LTRIM(RTRIM([Status])))=N'unavailable'
  AND IsCustomerVisible=0 AND IsPurchasable=0;
IF @AvailableId IS NULL OR @UnavailableId IS NULL
    THROW 51132, 'Available/Unavailable status mapping is not configured safely.', 1;
SELECT @dbPolicy=ShippingPolicyId, @dbProduct=ShopifyProductId,
       @dbAvailability=AvailableId, @dbQuantity=Quantity
FROM dbo.Parts WITH (UPDLOCK, HOLDLOCK) WHERE Id=@PartId;
IF @dbPolicy IS NULL OR @dbPolicy<>@FromPolicy OR @dbProduct<>@ProductId
    THROW 51130, 'Part changed during shipping request; refresh before retrying.', 1;
IF @dbAvailability<>@AvailableId OR @dbQuantity<=0
    THROW 51133, 'Part must be available and in stock before queueing a shipping update.', 1;
IF EXISTS (SELECT 1 FROM dbo.ShopifyShippingReassignmentJobs WITH (UPDLOCK, HOLDLOCK)
    WHERE PartId=@PartId AND IsOpen=1)
    THROW 51131, 'Shipping update already pending or needs attention.', 1;
-- A failed/duplicate job insert rolls back the availability change too.
UPDATE dbo.Parts
SET AvailableId=@UnavailableId, DateModified=SYSUTCDATETIME(), LastMovedBy=@UserId
WHERE Id=@PartId AND AvailableId=@AvailableId;
IF @@ROWCOUNT<>1
    THROW 51134, 'Failed to make part unavailable while queueing shipping update.', 1;
INSERT dbo.ShopifyShippingReassignmentJobs
(JobId, PartId, ShopifyProductId, FromPolicyId, ToPolicyId,
 RequestedByUserId, OriginalPublishedOnOnlineStore)
VALUES (@JobId, @PartId, @ProductId, @FromPolicy, @ToPolicy, @UserId, 1);
COMMIT;";
            await using var cmd = new SqlCommand(sql, conn) { CommandTimeout = 30 };
            cmd.Parameters.Add("@JobId", SqlDbType.UniqueIdentifier).Value = jobId;
            cmd.Parameters.Add("@PartId", SqlDbType.Int).Value = partId;
            cmd.Parameters.Add("@ProductId", SqlDbType.BigInt).Value = productId;
            cmd.Parameters.Add("@FromPolicy", SqlDbType.Int).Value = fromPolicy;
            cmd.Parameters.Add("@ToPolicy", SqlDbType.Int).Value = toPolicy;
            cmd.Parameters.Add("@UserId", SqlDbType.Int).Value = userId;
            await cmd.ExecuteNonQueryAsync(ct);
            return jobId;
        }

        public async Task<ShopifyShippingReassignmentJob?> ClaimAsync(Guid owner,
            CancellationToken ct = default)
        {
            await using var conn = Connection();
            await conn.OpenAsync(ct);
            // SQL lock + lease protects work across IIS/Azure restarts and scaled instances.
            // More than one worker cannot claim the same job within this lease.
            string sql = $@"
;WITH n AS (
 SELECT TOP (1) * FROM dbo.ShopifyShippingReassignmentJobs WITH (UPDLOCK, READPAST, ROWLOCK, READCOMMITTEDLOCK)
 WHERE IsOpen=1 AND Status IN (N'Queued', N'WaitingForShopify', N'SqlUpdated', N'Publishing')
   AND NextCheckUtc<=SYSUTCDATETIME()
   AND (LeaseUntilUtc IS NULL OR LeaseUntilUtc<SYSUTCDATETIME())
 ORDER BY NextCheckUtc, CreatedUtc
)
UPDATE n SET LeaseOwner=@owner, LeaseUntilUtc=DATEADD(MINUTE, 10, SYSUTCDATETIME()),
 UpdatedUtc=SYSUTCDATETIME()
OUTPUT inserted.{Columns.Replace(", ", ", inserted.").Replace("\n            ", "\n            inserted.")};";
            await using var cmd = new SqlCommand(sql, conn) { CommandTimeout = 30 };
            cmd.Parameters.Add("@owner", SqlDbType.UniqueIdentifier).Value = owner;
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            return await reader.ReadAsync(ct) ? Map(reader) : null;
        }

        // Renew an exclusive lease during slow Shopify API operations.
        // An expired or stolen lease cannot be resurrected.
        public async Task<bool> RenewLeaseAsync(Guid jobId, Guid owner,
            CancellationToken ct = default)
        {
            await using var conn = Connection();
            await conn.OpenAsync(ct);
            const string sql = @"
UPDATE dbo.ShopifyShippingReassignmentJobs
SET LeaseUntilUtc=DATEADD(MINUTE, 10, SYSUTCDATETIME()),
    UpdatedUtc=SYSUTCDATETIME()
WHERE JobId=@id AND LeaseOwner=@owner AND IsOpen=1
  AND LeaseUntilUtc>SYSUTCDATETIME();";
            await using var cmd = new SqlCommand(sql, conn) { CommandTimeout = 20 };
            cmd.Parameters.Add("@id", SqlDbType.UniqueIdentifier).Value = jobId;
            cmd.Parameters.Add("@owner", SqlDbType.UniqueIdentifier).Value = owner;
            return await cmd.ExecuteNonQueryAsync(ct) == 1;
        }

        public async Task<bool> UpdateAsync(ShopifyShippingReassignmentJob job, Guid owner,
            string status, bool complete, TimeSpan delay, bool? inOld = null,
            bool? inNew = null, string? error = null, bool countCheck = false,
            bool countFailure = false, CancellationToken ct = default)
        {
            await using var conn = Connection();
            await conn.OpenAsync(ct);
            const string sql = @"
UPDATE dbo.ShopifyShippingReassignmentJobs
SET Status=@status, IsOpen=@open,
    CheckCount=CheckCount+@countCheck, FailureCount=FailureCount+@countFailure,
    OldCollectionContainsProduct=COALESCE(@inOld,OldCollectionContainsProduct),
    NewCollectionContainsProduct=COALESCE(@inNew,NewCollectionContainsProduct),
    NextCheckUtc=DATEADD(SECOND,@delay,SYSUTCDATETIME()),
    LeaseOwner=NULL, LeaseUntilUtc=NULL,
    LastError=@error, UpdatedUtc=SYSUTCDATETIME(),
    CompletedUtc=CASE WHEN @complete=1 THEN SYSUTCDATETIME() ELSE CompletedUtc END
WHERE JobId=@id AND LeaseOwner=@owner AND LeaseUntilUtc>SYSUTCDATETIME();";
            await using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.Add("@status", SqlDbType.NVarChar, 40).Value = status;
            cmd.Parameters.Add("@open", SqlDbType.Bit).Value = !complete;
            cmd.Parameters.Add("@countCheck", SqlDbType.Int).Value = countCheck ? 1 : 0;
            cmd.Parameters.Add("@countFailure", SqlDbType.Int).Value = countFailure ? 1 : 0;
            cmd.Parameters.Add("@inOld", SqlDbType.Bit).Value = (object?)inOld ?? DBNull.Value;
            cmd.Parameters.Add("@inNew", SqlDbType.Bit).Value = (object?)inNew ?? DBNull.Value;
            cmd.Parameters.Add("@delay", SqlDbType.Int).Value = (int)delay.TotalSeconds;
            cmd.Parameters.Add("@error", SqlDbType.NVarChar, 1500).Value =
                (object?)error?[..Math.Min(error.Length, 1500)] ?? DBNull.Value;
            cmd.Parameters.Add("@complete", SqlDbType.Bit).Value = complete;
            cmd.Parameters.Add("@id", SqlDbType.UniqueIdentifier).Value = job.JobId;
            cmd.Parameters.Add("@owner", SqlDbType.UniqueIdentifier).Value = owner;
            return await cmd.ExecuteNonQueryAsync(ct) == 1;
        }
        // Availability is restored ONLY when Shopify is verified and the job completes.
        // A single transaction prevents job completion without restoring availability.
        // Failed or interrupted jobs keep the listing unavailable.
        public async Task<bool> CompleteAsync(ShopifyShippingReassignmentJob job,
            Guid owner, CancellationToken ct = default)
        {
            await using var conn = Connection();
            await conn.OpenAsync(ct);
            const string sql = @"
SET XACT_ABORT ON;
BEGIN TRAN;
DECLARE @AvailableId INT, @UnavailableId INT, @dbPolicy INT, @dbAvailability INT;
SELECT @AvailableId=Id FROM dbo.Available
WHERE Id=1 AND LOWER(LTRIM(RTRIM([Status])))=N'available';
SELECT @UnavailableId=MIN(Id) FROM dbo.Available
WHERE LOWER(LTRIM(RTRIM([Status])))=N'unavailable'
  AND IsCustomerVisible=0 AND IsPurchasable=0;
IF @AvailableId IS NULL OR @UnavailableId IS NULL
    THROW 51135, 'Available/Unavailable status mapping changed during shipping update.', 1;
-- Match the part-row locking order used by EnqueueAsync.
SELECT @dbPolicy=ShippingPolicyId, @dbAvailability=AvailableId
FROM dbo.Parts WITH (UPDLOCK, HOLDLOCK) WHERE Id=@PartId;
IF @dbPolicy<>@ToPolicyId OR @dbAvailability<>@UnavailableId
    THROW 51136, 'Part shipping or availability state changed before completion.', 1;
-- Zero-stock products remain unavailable even after the shipping job completes.
UPDATE dbo.Parts
SET AvailableId=CASE WHEN Quantity>0 THEN @AvailableId ELSE @UnavailableId END,
    DateModified=SYSUTCDATETIME(), LastMovedBy=@UserId
WHERE Id=@PartId AND ShippingPolicyId=@ToPolicyId AND AvailableId=@UnavailableId;
IF @@ROWCOUNT<>1
    THROW 51137, 'Could not restore part availability.', 1;
UPDATE dbo.ShopifyShippingReassignmentJobs
SET Status=N'Completed', IsOpen=0, CheckCount=CheckCount+1,
    OldCollectionContainsProduct=0, NewCollectionContainsProduct=1,
    NextCheckUtc=SYSUTCDATETIME(), LeaseOwner=NULL, LeaseUntilUtc=NULL,
    LastError=NULL, UpdatedUtc=SYSUTCDATETIME(), CompletedUtc=SYSUTCDATETIME()
WHERE JobId=@JobId AND PartId=@PartId AND ToPolicyId=@ToPolicyId
  AND IsOpen=1 AND LeaseOwner=@Owner AND LeaseUntilUtc>SYSUTCDATETIME();
IF @@ROWCOUNT<>1
    THROW 51138, 'Worker lost SQL lease before committing shipping job and availability.', 1;
COMMIT;
SELECT CAST(1 AS BIT);";
            await using var cmd = new SqlCommand(sql, conn) { CommandTimeout = 30 };
            cmd.Parameters.Add("@JobId", SqlDbType.UniqueIdentifier).Value = job.JobId;
            cmd.Parameters.Add("@PartId", SqlDbType.Int).Value = job.PartId;
            cmd.Parameters.Add("@ToPolicyId", SqlDbType.Int).Value = job.ToPolicyId;
            cmd.Parameters.Add("@UserId", SqlDbType.Int).Value = job.RequestedByUserId;
            cmd.Parameters.Add("@Owner", SqlDbType.UniqueIdentifier).Value = owner;
            return Convert.ToBoolean(await cmd.ExecuteScalarAsync(ct));
        }

    }
}
