using System.Data;
using System.Data.SqlClient;
using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Options;
using Site_2024.Web.Api.Controllers;
using Site_2024.Web.Api.Interfaces;
using Site_2024.Web.Api.Services;

// Isolated HTTP host: no production Program, configuration, database or Shopify clients.
if (args.Contains("--sql"))
{
    await SqlIntegration.Run();
    return;
}
var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = Array.Empty<string>() });
builder.Logging.ClearProviders();
builder.WebHost.UseUrls("http://127.0.0.1:0");
builder.Services.AddAuthentication("Test").AddScheme<AuthenticationSchemeOptions, TestAuthentication>("Test", _ => { });
builder.Services.AddAuthorization(o => o.AddReferenceDeletionPolicy());
builder.Services.AddControllers().AddApplicationPart(typeof(ReferenceDeletionController).Assembly);
var data = new FakeProvider();
builder.Services.AddSingleton<IDataProvider>(data);
builder.Services.AddScoped<ReferenceDeletionService>();
await using var app = builder.Build();
ProductionRouteChecks.Verify(app.Services);
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
await app.StartAsync();
var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
using var client = new HttpClient { BaseAddress = new Uri(address) };
var passed = 0;
async Task Check(string method, string url, string? role, HttpStatusCode expected)
{
    using var request = new HttpRequestMessage(new HttpMethod(method), url);
    if (role != null) request.Headers.Add("Test-Role", role);
    var before = data.Calls;
    using var response = await client.SendAsync(request);
    if (response.StatusCode != expected) throw new Exception($"{method} {url}: expected {expected}, got {response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
    if (expected is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized && data.Calls != before)
        throw new Exception("Unauthorized request reached data provider.");
    Console.WriteLine($"PASS {role ?? "anonymous"} {method} {url} => {(int)expected}");
    passed++;
}
foreach (var path in new[] { "catagories/1", "makes/1", "models/1", "makes/manufacturer?company=Zeta" })
{
    await Check("DELETE", "/api/" + path, "AdminLow", HttpStatusCode.Forbidden);
    await Check("DELETE", "/api/" + path, null, HttpStatusCode.Unauthorized);
    await Check("DELETE", "/api/" + path, "AdminHigh", HttpStatusCode.OK);
}
foreach (var path in new[] { "catagories/1/delete-check", "makes/1/delete-check",
    "models/1/delete-check", "makes/manufacturer/delete-check?company=Zeta" })
{
    await Check("GET", "/api/" + path, null, HttpStatusCode.Unauthorized);
    await Check("GET", "/api/" + path, "AdminLow", HttpStatusCode.Forbidden);
    await Check("GET", "/api/" + path, "AdminHigh", HttpStatusCode.OK);
    if (data.Delete) throw new Exception("Preview requested a write.");
}
if (data.Company != "Zeta" || data.Kind != "Manufacturer") throw new Exception("Manufacturer identity lost.");
data.Blocked = true;
foreach (var path in new[] { "catagories/1", "makes/1", "models/1", "makes/manufacturer?company=Zeta" })
    await Check("DELETE", "/api/" + path, "AdminHigh", HttpStatusCode.Conflict);
data.Exists = false;
await Check("DELETE", "/api/models/1", "AdminHigh", HttpStatusCode.NotFound);
await Check("GET", "/api/models/1/delete-check", "AdminHigh", HttpStatusCode.NotFound);
await Check("DELETE", "/api/makes/manufacturer?company=%20", "AdminHigh", HttpStatusCode.BadRequest);
Console.WriteLine($"{passed} API checks passed. SQL behavior requires the separate disposable-database tests.");
await app.StopAsync();

sealed class TestAuthentication : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public TestAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : base(options, logger, encoder) { }
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var role = Request.Headers["Test-Role"].ToString();
        if (role.Length == 0) return Task.FromResult(AuthenticateResult.NoResult());
        var identity = new ClaimsIdentity(new[] { new Claim(ClaimTypes.Role, role) }, Scheme.Name);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
    }
}

sealed class FakeProvider : IDataProvider
{
    public bool Exists = true, Blocked, Delete;
    public string? Kind, Company;
    public int Calls;
    public void ExecuteCmd(string storedProc, Action<SqlParameterCollection> inputParamMapper,
        Action<IDataReader, short> singleRecordMapper, Action<SqlParameterCollection> returnParameters = null!,
        Action<SqlCommand> cmdModifier = null!)
    {
        if (storedProc != "dbo.ReferenceData_DeleteSafe") throw new Exception("Unexpected procedure.");
        using var command = new SqlCommand();
        inputParamMapper(command.Parameters);
        Delete = (bool)command.Parameters["@Delete"].Value;
        Kind = (string)command.Parameters["@Kind"].Value;
        Company = command.Parameters["@Company"].Value as string;
        Calls++;
        var table = new DataTable();
        foreach (var type in new[] { typeof(bool), typeof(int), typeof(int), typeof(int), typeof(int), typeof(int), typeof(int), typeof(bool) })
            table.Columns.Add("Column" + table.Columns.Count, type);
        table.Rows.Add(Exists, 0, 0, 0, Kind == "Model" && Blocked ? 2 : 0, Blocked ? 1 : 0,
            Kind == "Manufacturer" ? 2 : 1, Exists && !Blocked && Delete);
        using var reader = table.CreateDataReader();
        reader.Read();
        singleRecordMapper(reader, 0);
    }
    public int ExecuteNonQuery(string storedProc, Action<SqlParameterCollection> inputParamMapper,
        Action<SqlParameterCollection> returnParameters = null!) => throw new Exception("Unexpected mutation path.");
}
