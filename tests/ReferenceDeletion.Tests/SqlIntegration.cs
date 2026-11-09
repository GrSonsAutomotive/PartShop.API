using System.Data.SqlClient;
using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Site_2024.Web.Api.Controllers;
using Site_2024.Web.Api.Interfaces;
using Site_2024.Web.Api.Services;

// Real SQL tests, opt-in --sql only. No appsettings, secrets, remote servers or
// existing databases. Create and drop only the unique database owned by this run.
internal static class SqlIntegration
{
    private static string connectionString = "";
    private static string database = "";
    private static string owner = "";
    private static int passed;
    private static readonly string[] Tables = { "Catagory", "Model", "Make", "Parts", "PartCategories", "PartFitments", "PartSales", "AdminDiscountCodes", "AdminDiscountCodeRules" };

    public static async Task Run()
    {
        var root = Directory.GetCurrentDirectory();
        var migration = Path.Combine(root, "database/migrations/20261107_01_ReferenceDataSafeDeletion.sql");
        if (!File.Exists(migration)) throw new Exception("Run --sql from the API repository root.");
        owner = Guid.NewGuid().ToString("N");
        database = "ReferenceDeletionTests_" + owner;
        var masterString = new SqlConnectionStringBuilder
        {
            DataSource = "lpc:localhost", InitialCatalog = "master", IntegratedSecurity = true,
            Encrypt = false, TrustServerCertificate = true, ConnectTimeout = 5, Pooling = false
        }.ConnectionString;
        await using var master = new SqlConnection(masterString);
        await master.OpenAsync();
        var machine = Convert.ToString(await Scalar(master, "SELECT CONVERT(nvarchar(128),SERVERPROPERTY('MachineName'))"));
        if (!string.Equals(machine, Environment.MachineName, StringComparison.OrdinalIgnoreCase))
            throw new Exception("Only the local machine SQL Server is allowed.");
        Console.WriteLine($"LOCAL SQL {machine}; disposable database {database}");
        var created = false;
        try
        {
            if (await Scalar(master, $"SELECT DB_ID(N'{database}')") is not DBNull)
                throw new Exception("Refusing to reuse an existing database.");
            await Exec(master, $"CREATE DATABASE [{database}]");
            created = true;
            connectionString = new SqlConnectionStringBuilder(masterString) { InitialCatalog = database }.ConnectionString;
            await using (var db = await Open())
            {
                await Exec(db, $"EXEC sys.sp_addextendedproperty @name=N'ReferenceDeletionTestOwner',@value=N'{owner}'");
                await Exec(db, File.ReadAllText(Path.Combine(root, "tests/reference-deletion-schema.sql")));
                await Apply(db, migration);
                await ProductionReleaseScripts(db, root);
            }
            foreach (var rcsi in new[] { false, true })
            {
                Console.WriteLine($"MODE READ_COMMITTED_SNAPSHOT={(rcsi ? "ON" : "OFF")}");
                await VerifyOwner();
                await Exec(master, $"ALTER DATABASE [{database}] SET READ_COMMITTED_SNAPSHOT {(rcsi ? "ON" : "OFF")} WITH ROLLBACK IMMEDIATE");
                await using (var db = await Open())
                {
                    db.InfoMessage += (_, e) =>
                    {
                        Console.WriteLine(e.Message);
                        passed += e.Message.Split('\n').Count(line => line.TrimStart().StartsWith("PASS SQL:"));
                    };
                    await Exec(db, File.ReadAllText(Path.Combine(root, "tests/reference-deletion.sql")));
                    await Seed(db);
                    var before = await Snapshot(db);
                    var definitions = await ModuleDefinitions(db);
                    await Apply(db, migration);
                    await Apply(db, migration);
                    Assert(before == await Snapshot(db), "repeated migration preserves seeded data and schema metadata");
                    var afterDefinitions = await ModuleDefinitions(db);
                    if (definitions != afterDefinitions)
                    {
                        var index = Enumerable.Range(0, Math.Min(definitions.Length, afterDefinitions.Length))
                            .FirstOrDefault(i => definitions[i] != afterDefinitions[i]);
                        throw new Exception("Module definition differs at " + index + ": "
                            + System.Text.Json.JsonSerializer.Serialize(definitions.Substring(index, Math.Min(160, definitions.Length-index)))
                            + " versus " + System.Text.Json.JsonSerializer.Serialize(afterDefinitions.Substring(index, Math.Min(160, afterDefinitions.Length-index))));
                    }
                    Pass("repeated migration preserves module definitions (excluding outer blank lines from SQLCMD includes)");
                }
                await Concurrency();
                await ManufacturerRollback();
                await HttpChecks();
                await using (var db = await Open())
                {
                    await Exec(db, "DELETE dbo.AdminDiscountCodeRules; DELETE dbo.AdminDiscountCodes; DELETE dbo.PartSales; DELETE dbo.PartCategories; DELETE dbo.PartFitments; DELETE dbo.Parts; DELETE dbo.Make; DELETE dbo.Model; DELETE dbo.Catagory;");
                }
            }
            Console.WriteLine($"PASS {passed} real SQL/API integration checks across both isolation modes.");
        }
        finally
        {
            if (created)
            {
                // Never compute a cleanup target from user input. Verify both unique
                // name and marker before dropping anything, including on test failure.
                await VerifyOwner();
                await Exec(master, $"ALTER DATABASE [{database}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{database}]");
                Console.WriteLine($"DROPPED owned disposable database {database}");
            }
        }
    }

    private static async Task VerifyOwner()
    {
        if (database != "ReferenceDeletionTests_" + owner || !Regex.IsMatch(owner, "^[a-f0-9]{32}$"))
            throw new Exception("Invalid cleanup target.");
        await using var db = await Open();
        if (!Equals(await Scalar(db, "SELECT CONVERT(nvarchar(32),value) FROM sys.extended_properties WHERE class=0 AND name=N'ReferenceDeletionTestOwner'"), owner))
            throw new Exception("Database ownership marker missing. Cleanup refused.");
    }

    private static async Task ProductionReleaseScripts(SqlConnection db, string root)
    {
        // These scripts are never run against an application database by this test.
        // First prove their original production-name guards reject this owned DB.
        await VerifyOwner();
        var directory = Path.Combine(root, "database/release");
        var scripts = new[]
        {
            ("ReferenceDeletion_Production_Preflight_READ_ONLY.sql", 50510),
            ("ReferenceDeletion_Production_PostInstall_READ_ONLY.sql", 50520),
            ("ReferenceDeletion_Production_ApiPermission_READ_ONLY.sql", 50530)
        };
        foreach (var (file, error) in scripts)
        {
            try { await Exec(db, File.ReadAllText(Path.Combine(directory, file))); throw new Exception("Production guard did not reject disposable DB."); }
            catch (SqlException ex) when (ex.Number == error) { Pass(file + ": original production-name guard rejects test DB"); }
        }

        // Simulate SQLCMD includes/approval only in memory, replacing the exact
        // production guard with this verified disposable database's generated name.
        // The checked-in production scripts remain unchanged and on HOLD.
        var wrapper = File.ReadAllText(Path.Combine(directory, "ReferenceDeletion_Production_Install.sql"));
        foreach (var relative in new[] {
            @"database\release\ReferenceDeletion_Production_Preflight_READ_ONLY.sql",
            @"database\migrations\20261107_01_ReferenceDataSafeDeletion.sql",
            @"database\release\ReferenceDeletion_Production_PostInstall_READ_ONLY.sql" })
            wrapper = wrapper.Replace(":r .\\" + relative, File.ReadAllText(Path.Combine(root, relative)));
        wrapper = Regex.Replace(wrapper, @"^:.*$", "", RegexOptions.Multiline)
            .Replace("N'Site_2024'", "N'" + database + "'");
        async Task Batches(string script)
        {
            foreach (var batch in Regex.Split(script, @"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase))
                if (!string.IsNullOrWhiteSpace(batch)) await Exec(db, batch);
        }
        try { await Batches(wrapper.Replace("$(ReferenceDeletionApproved)", "NO")); throw new Exception("Unapproved installation was allowed."); }
        catch (SqlException ex) when (ex.Number == 50541) { Pass("production installation defaults to HOLD without approval"); }
        Assert(Convert.ToInt32(await Scalar(db, "SELECT @@TRANCOUNT")) == 0, "HOLD guard opens no transaction");

        var before = await Snapshot(db);
        await Batches(wrapper.Replace("$(ReferenceDeletionApproved)", "YES"));
        Assert(Convert.ToInt32(await Scalar(db, "SELECT @@TRANCOUNT")) == 0 && before == await Snapshot(db),
            "approved installation simulation commits all six modules without changing fixture data/schema identities");
        var user = Convert.ToString(await Scalar(db, "SELECT USER_NAME()"))!.Replace("'", "''");
        var permission = File.ReadAllText(Path.Combine(directory, scripts[2].Item1))
            .Replace("N'Site_2024'", "N'" + database + "'")
            .Replace("sysname=N'REPLACE_WITH_ACTUAL_API_DATABASE_USER';", "sysname=N'" + user + "';");
        await Exec(db, permission);
        Pass("API-identity permission script compiles and verifies the disposable test identity");
        await Exec(db, "SET XACT_ABORT OFF");
    }

    private static async Task Seed(SqlConnection db) => await Exec(db, """
        INSERT dbo.Catagory VALUES(1,N'Brakes',50),(100,N'PrimaryRace',10),(101,N'AdditionalRace',20),(102,N'DiscountRace',30),(103,N'RuleUpdateRace',31),(104,N'PartUpdateRace',32),(200,N'ReverseRace',40),(203,N'NarrowTarget',41),(204,N'Unrelated',42);
        INSERT dbo.Model VALUES(1,N'Shared'),(2,N'Other'),(102,N'DiscountModelRace'),(103,N'AssociationRace');
        INSERT dbo.Make VALUES(10,N'Zeta',1,10,20),(11,N'Alpha',1,20,10),(100,N'PrimaryMakeRace',2,30,10),
          (101,N'FitmentRace',2,40,10),(102,N'DiscountMakeRace',2,50,10),
          (103,N'ManufacturerRace',1,60,10),(104,N'ManufacturerRace',2,60,20),
          (107,N'ManufacturerInsertRace',2,70,10),(201,N'ReverseFitmentRace',2,80,10),
          (108,N'FitmentUpdateRace',2,85,10),(300,N'AtomicManufacturer',1,90,10),(301,N'AtomicManufacturer',2,90,20);
        INSERT dbo.Parts VALUES(1,1,10,7);
        INSERT dbo.PartSales VALUES(1,1,2);
        INSERT dbo.AdminDiscountCodes VALUES(1,N'Active',NULL);
        INSERT dbo.AdminDiscountCodeRules(Id,RuleType,SourceId,RuleValue,ShopifyTag) VALUES(200,N'CustomTag',NULL,N'Unrelated',N'Unrelated');
        INSERT dbo.PartFitments VALUES(200,1,10);
        """);

    private static async Task Concurrency()
    {
        // Each writer is a normal SQL transaction, not an application-lock participant.
        await WriterFirst("primary inventory category", "INSERT dbo.Parts VALUES(100,100,10,1)", "Category", 100);
        await WriterFirst("additional category", "INSERT dbo.PartCategories VALUES(101,1,101)", "Category", 101);
        await WriterFirst("primary inventory make", "INSERT dbo.Parts VALUES(101,1,100,1)", "Make", 100);
        await WriterFirst("additional fitment", "INSERT dbo.PartFitments VALUES(101,1,101)", "Make", 101);
        await WriterFirst("category discount", "INSERT dbo.AdminDiscountCodeRules(Id,RuleType,SourceId,RuleValue,ShopifyTag) VALUES(102,N'Category',102,N'DiscountRace',N'Category_DiscountRace')", "Category", 102);
        await WriterFirst("model discount", "INSERT dbo.AdminDiscountCodeRules(Id,RuleType,SourceId,RuleValue,ShopifyTag) VALUES(103,N'Model',102,N'DiscountModelRace',N'Model_DiscountModelRace')", "Model", 102);
        await WriterFirst("make discount", "INSERT dbo.AdminDiscountCodeRules(Id,RuleType,SourceId,RuleValue,ShopifyTag) VALUES(104,N'Make',102,N'DiscountMakeRace',N'Make_DiscountMakeRace')", "Make", 102);
        await WriterFirst("manufacturer discount", "INSERT dbo.AdminDiscountCodeRules(Id,RuleType,SourceId,RuleValue,ShopifyTag) VALUES(105,N'Make',104,N'ManufacturerRace',N'Make_ManufacturerRace')", "Manufacturer", null, "ManufacturerRace");
        await WriterFirst("global model association", "INSERT dbo.Make VALUES(105,N'NewCompany',103,100,10)", "Model", 103);
        await WriterFirst("manufacturer phantom association", "INSERT dbo.Make VALUES(106,N'ManufacturerInsertRace',2,70,20)", "Manufacturer", null, "ManufacturerInsertRace", true);
        await WriterFirst("discount rule retarget update", "UPDATE dbo.AdminDiscountCodeRules SET RuleType=N'Category',SourceId=103,RuleValue=N'RuleUpdateRace',ShopifyTag=N'Category_RuleUpdateRace' WHERE Id=200", "Category", 103);
        await WriterFirst("fitment retarget update", "UPDATE dbo.PartFitments SET MakeId=108 WHERE Id=200", "Make", 108);
        await WriterFirst("inventory retarget update", "UPDATE dbo.Parts SET catagoryId=104 WHERE id=1", "Category", 104);
        await using (var db = await Open()) await Exec(db, "UPDATE dbo.Parts SET catagoryId=1 WHERE id=1");
        await DeleteFirst("Category", 200, "INSERT dbo.Parts VALUES(200,200,10,1)");
        await DeleteFirst("Make", 201, "INSERT dbo.PartFitments VALUES(201,1,201)");
        await using (var deleter = await Open())
        await using (var unrelated = await Open())
        {
            using var transaction = deleter.BeginTransaction();
            if (!(await Delete(deleter, "Category", 203, null, transaction)).Deleted) throw new Exception("Narrow target was blocked.");
            await Exec(unrelated, "UPDATE dbo.Catagory SET DisplayOrder=DisplayOrder+1 WHERE id=204");
            transaction.Commit();
            Pass("unrelated category write proceeds while target deletion transaction is open");
        }
    }

    private static async Task WriterFirst(string label, string sql, string kind, int? id, string? company = null, bool expectedDeleted = false)
    {
        await using var writer = await Open();
        await using var deleter = await Open();
        using var transaction = writer.BeginTransaction();
        await Exec(writer, sql, transaction);
        var writerId = Convert.ToInt32(await Scalar(writer, "SELECT @@SPID", transaction));
        var deleterId = Convert.ToInt32(await Scalar(deleter, "SELECT @@SPID"));
        var pending = Delete(deleter, kind, id, company);
        await WaitBlocked(writer, deleterId, writerId, transaction);
        transaction.Commit();
        var result = await pending;
        Assert(result.Exists && result.Deleted == expectedDeleted && (expectedDeleted ? result.TargetRows == 2 : !result.CanDelete),
            $"writer-first {label}: waits, then {(expectedDeleted ? "includes both committed target rows" : "blocks deletion")}");
        if (kind == "Manufacturer")
            Assert(Convert.ToInt32(await Scalar(writer, $"SELECT COUNT(*) FROM dbo.Make WHERE company=N'{company}'")) == (expectedDeleted ? 0 : 2),
                $"{label}: manufacturer deletion is all or nothing");
    }

    private static async Task DeleteFirst(string kind, int id, string insert)
    {
        await using var deleter = await Open();
        await using var writer = await Open();
        using var transaction = deleter.BeginTransaction();
        var result = await Delete(deleter, kind, id, null, transaction);
        if (!result.Deleted) throw new Exception("Reverse-order deletion unexpectedly blocked.");
        var writerId = Convert.ToInt32(await Scalar(writer, "SELECT @@SPID"));
        var deleterId = Convert.ToInt32(await Scalar(deleter, "SELECT @@SPID", transaction));
        var pending = Exec(writer, insert);
        await WaitBlocked(deleter, writerId, deleterId, transaction);
        transaction.Commit();
        try { await pending; throw new Exception("Writer inserted a dangling inventory reference."); }
        catch (SqlException e) when (e.Number == 547) { Pass($"delete-first {kind}: writer waits, then FK rejects dangling reference"); }
    }

    private static async Task ManufacturerRollback()
    {
        await using var db = await Open();
        var before = await Snapshot(db);
        await Exec(db, "CREATE TRIGGER dbo.TestManufacturerFailure ON dbo.Make AFTER DELETE AS BEGIN IF EXISTS(SELECT 1 FROM deleted WHERE company=N'AtomicManufacturer') THROW 50400,'Injected manufacturer failure',1; END");
        try
        {
            try { await Delete(db, "Manufacturer", null, "AtomicManufacturer"); throw new Exception("Injected error was ignored."); }
            catch (SqlException e) when (e.Number == 50400) { }
        }
        finally { await Exec(db, "DROP TRIGGER dbo.TestManufacturerFailure"); }
        Assert(before == await Snapshot(db), "manufacturer exception rolls back all associations; inventory, models, ordering and rules unchanged");
        var inventory = await Inventory(db);
        var result = await Delete(db, "Manufacturer", null, "AtomicManufacturer");
        Assert(result.Deleted && result.TargetRows == 2, "manufacturer success removes all target rows atomically");
        Assert(Convert.ToInt32(await Scalar(db, "SELECT COUNT(*) FROM dbo.Model WHERE id IN(1,2)")) == 2, "manufacturer success preserves both shared global models");
        Assert(inventory == await Inventory(db), "successful manufacturer deletion preserves inventory, fitments and sales");
    }

    private static async Task HttpChecks()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = Array.Empty<string>() });
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddAuthentication("Test").AddScheme<AuthenticationSchemeOptions, TestAuthentication>("Test", _ => { });
        builder.Services.AddAuthorization(o => o.AddReferenceDeletionPolicy());
        builder.Services.AddControllers().AddApplicationPart(typeof(ReferenceDeletionController).Assembly);
        builder.Services.AddSingleton<IDataProvider>(new DataProvider(connectionString));
        builder.Services.AddScoped<ReferenceDeletionService>();
        await using var app = builder.Build();
        ProductionRouteChecks.Verify(app.Services);
        app.UseAuthentication(); app.UseAuthorization(); app.MapControllers();
        await app.StartAsync();
        var url = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        using var client = new HttpClient { BaseAddress = new Uri(url), Timeout = TimeSpan.FromSeconds(25) };
        client.DefaultRequestHeaders.Add("Test-Role", "AdminHigh");
        foreach (var path in new[] { "catagories/9999", "makes/9999", "models/9999", "makes/manufacturer?company=Missing" })
        {
            using var response = await client.DeleteAsync("/api/" + path);
            Assert(response.StatusCode == HttpStatusCode.NotFound, $"real SQL HTTP DELETE {path} => 404");
        }
        foreach (var path in new[] { "catagories/1", "makes/10", "models/1", "makes/manufacturer?company=Zeta" })
        {
            using var response = await client.DeleteAsync("/api/" + path);
            Assert(response.StatusCode == HttpStatusCode.Conflict, $"real SQL HTTP DELETE {path} => 409");
        }
        foreach (var path in new[] { "catagories/9999/delete-check", "makes/9999/delete-check", "models/9999/delete-check", "makes/manufacturer/delete-check?company=Missing" })
        {
            using var response = await client.GetAsync("/api/" + path);
            Assert(response.StatusCode == HttpStatusCode.NotFound, $"real SQL HTTP preview {path} => 404");
        }
        await using (var db = await Open())
        {
            await Exec(db, "INSERT dbo.Catagory VALUES(900,N'HttpUnused',999); INSERT dbo.Model VALUES(900,N'HttpUnused'); INSERT dbo.Make VALUES(900,N'HttpAssociation',2,999,999),(901,N'HttpManufacturer',1,998,10),(902,N'HttpManufacturer',2,998,20)");
            var inventory = await Inventory(db);
            foreach (var path in new[] { "catagories/900", "makes/900", "models/900", "makes/manufacturer?company=HttpManufacturer" })
            {
                using var response = await client.DeleteAsync("/api/" + path);
                Assert(response.StatusCode == HttpStatusCode.OK, $"real SQL HTTP DELETE {path} => 200");
            }
            Assert(inventory == await Inventory(db), "all successful HTTP deletions preserve inventory and sales history");
        }
        await using var blocker = await Open();
        var before = await Snapshot(blocker);
        using (var transaction = blocker.BeginTransaction())
        {
            await Exec(blocker, "UPDATE dbo.Parts SET Quantity=Quantity+1 WHERE id=1", transaction);
            var blockerId = Convert.ToInt32(await Scalar(blocker, "SELECT @@SPID", transaction));
            var pending = client.DeleteAsync("/api/catagories/1");
            await WaitBlocked(blocker, null, blockerId, transaction);
            // API owns category update lock and is waiting on our Parts write.
            // This update closes the cycle; deletion's LOW deadlock priority loses.
            await Exec(blocker, "UPDATE dbo.Catagory SET DisplayOrder=DisplayOrder+1 WHERE id=1", transaction);
            using var response = await pending;
            Assert(response.StatusCode == HttpStatusCode.ServiceUnavailable, "actual SQL deadlock => HTTP 503");
            var body = await response.Content.ReadAsStringAsync();
            Assert(response.Headers.RetryAfter?.Delta == TimeSpan.FromSeconds(1)
                && body.Contains("ReferenceDataBusy") && !body.Contains("\"item\""), "deadlock response identifies retryable rollback without stale dependency counts");
            transaction.Rollback();
        }
        Assert(before == await Snapshot(blocker), "deadlock leaves all data and ordering unchanged");
        using (var response = await client.DeleteAsync("/api/catagories/1"))
            Assert(response.StatusCode == HttpStatusCode.Conflict, "fresh request after deadlock rechecks dependencies => 409");
        await app.StopAsync();
    }

    private static async Task WaitBlocked(SqlConnection observer, int? waiter, int blocker, SqlTransaction transaction)
    {
        var until = DateTime.UtcNow.AddSeconds(8);
        while (DateTime.UtcNow < until)
        {
            var count = Convert.ToInt32(await Scalar(observer, $"SELECT COUNT(*) FROM sys.dm_exec_requests WHERE database_id=DB_ID() AND blocking_session_id={blocker}" + (waiter.HasValue ? $" AND session_id={waiter}" : ""), transaction));
            if (count > 0) return;
            await Task.Delay(40);
        }
        throw new Exception("Expected blocking was not observed in SQL Server DMVs.");
    }

    private static async Task<ReferenceDeletionResult> Delete(SqlConnection db, string kind, int? id, string? company = null, SqlTransaction? transaction = null)
    {
        using var command = new SqlCommand("dbo.ReferenceData_DeleteSafe", db, transaction) { CommandType = System.Data.CommandType.StoredProcedure, CommandTimeout = 20 };
        command.Parameters.AddWithValue("@Kind", kind);
        command.Parameters.AddWithValue("@Id", (object?)id ?? DBNull.Value);
        command.Parameters.AddWithValue("@Company", (object?)company ?? DBNull.Value);
        command.Parameters.AddWithValue("@Delete", true);
        using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) throw new Exception("No SQL result.");
        return new ReferenceDeletionResult { Exists=reader.GetBoolean(0), PrimaryParts=reader.GetInt32(1), Categories=reader.GetInt32(2), Fitments=reader.GetInt32(3), Makes=reader.GetInt32(4), DiscountRules=reader.GetInt32(5), TargetRows=reader.GetInt32(6), Deleted=reader.GetBoolean(7) };
    }

    private static async Task<string> Inventory(SqlConnection db)
    {
        var result = "";
        foreach (var table in new[] { "Parts", "PartCategories", "PartFitments", "PartSales" })
            result += await Scalar(db, $"SELECT * FROM dbo.[{table}] ORDER BY id FOR XML RAW,BINARY BASE64");
        return result;
    }
    private static async Task<string> Snapshot(SqlConnection db)
    {
        var result = "";
        foreach (var table in Tables)
            result += await Scalar(db, $"SELECT * FROM dbo.[{table}] ORDER BY id FOR XML RAW,BINARY BASE64");
        result += await Scalar(db, "SELECT object_id,name,type FROM sys.objects WHERE is_ms_shipped=0 ORDER BY object_id FOR XML RAW");
        result += await Scalar(db, "SELECT object_id,index_id,name FROM sys.indexes WHERE object_id IN(SELECT object_id FROM sys.tables) ORDER BY object_id,index_id FOR XML RAW");
        return result;
    }
    private static async Task<string> ModuleDefinitions(SqlConnection db)
    {
        var xml = Convert.ToString(await Scalar(db,
            "SELECT definition FROM sys.sql_modules ORDER BY object_id FOR XML PATH('module'), ROOT('modules')"))!;
        // SQLCMD include boundaries add outer blank lines to module definitions.
        // Compare every other character, including all internal whitespace/comments.
        return string.Join("\u001e", System.Xml.Linq.XDocument.Parse(xml)
            .Descendants("definition").Select(d => d.Value.Trim('\r', '\n')));
    }

    private static async Task Apply(SqlConnection db, string path)
    {
        foreach (var batch in Regex.Split(File.ReadAllText(path), @"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase))
            if (!string.IsNullOrWhiteSpace(batch)) await Exec(db, batch);
    }
    private static async Task<SqlConnection> Open()
    {
        var db = new SqlConnection(connectionString);
        await db.OpenAsync();
        return db;
    }
    private static async Task Exec(SqlConnection db, string sql, SqlTransaction? transaction = null)
    {
        using var command = new SqlCommand(sql, db, transaction) { CommandTimeout = 20 };
        await command.ExecuteNonQueryAsync();
    }
    private static async Task<object?> Scalar(SqlConnection db, string sql, SqlTransaction? transaction = null)
    {
        using var command = new SqlCommand(sql, db, transaction) { CommandTimeout = 20 };
        if (sql.Contains("FOR XML", StringComparison.OrdinalIgnoreCase))
        {
            // FOR XML without TYPE streams its text across result rows. ExecuteScalar
            // returns only the first chunk, which can hide later inventory/module drift.
            using var reader = await command.ExecuteReaderAsync();
            var xml = new System.Text.StringBuilder();
            while (await reader.ReadAsync()) xml.Append(reader.GetString(0));
            return xml.ToString();
        }
        return await command.ExecuteScalarAsync();
    }
    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new Exception("FAIL " + message);
        Pass(message);
    }
    private static void Pass(string message) { passed++; Console.WriteLine("PASS " + message); }
}
