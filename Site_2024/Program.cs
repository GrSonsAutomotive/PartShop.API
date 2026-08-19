using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Site_2024.Web.Api.Data;
using Site_2024.Web.Api.Interfaces;
using Site_2024.Web.Api.Middleware;
using Site_2024.Web.Api.Models;
using Site_2024.Web.Api.Models.User;
using Site_2024.Web.Api.Services;

// Application files live under the deployed wwwroot.
// Runtime uploads MUST live outside that deployment tree in Azure so that
// deployments cannot delete customer/product photos.
var contentRootPath = Directory.GetCurrentDirectory();
var webRootPath = Path.Combine(contentRootPath, "wwwroot");

var homePath = Environment.GetEnvironmentVariable("HOME");
var azureSiteName = Environment.GetEnvironmentVariable("WEBSITE_SITE_NAME");

bool isAzureAppService =
    !string.IsNullOrWhiteSpace(azureSiteName) &&
    !string.IsNullOrWhiteSpace(homePath);

// Azure:
//   %HOME%\data\Site_2024\Uploads
//
// Local development:
//   <project>\wwwroot\uploads
var uploadRootPath = isAzureAppService
    ? Path.Combine(homePath!, "data", "Site_2024", "Uploads")
    : Path.Combine(webRootPath, "uploads");

Directory.CreateDirectory(webRootPath);
Directory.CreateDirectory(uploadRootPath);
Directory.CreateDirectory(Path.Combine(uploadRootPath, "items"));
Directory.CreateDirectory(Path.Combine(uploadRootPath, "returns"));

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = contentRootPath,
    WebRootPath = webRootPath
});

// Make the resolved upload path available to controllers.
builder.Configuration["UploadStorage:RootPath"] = uploadRootPath;

builder.Configuration
    .SetBasePath(contentRootPath)
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
    .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true)
    .AddEnvironmentVariables();


// Add services to the container.
builder.Services.AddControllers()
    .ConfigureApiBehaviorOptions(options =>
    {
        // Force model validation errors into our BaseResponse shape (instead of ProblemDetails)
        options.InvalidModelStateResponseFactory = context =>
        {
            var errors = context.ModelState
                .SelectMany(kvp => kvp.Value.Errors.Select(e => string.IsNullOrWhiteSpace(e.ErrorMessage)
                    ? "Invalid request."
                    : e.ErrorMessage))
                .ToList();

            return new BadRequestObjectResult(new ErrorResponse(errors));
        };
    });
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
// Add logging
builder.Logging.AddConsole();
builder.Logging.AddDebug();

// Configure CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("CorsPolicy", policy =>
        policy
            .WithOrigins(
                "http://localhost:3000",
                "https://red-pond-0503f431e.2.azurestaticapps.net",
                "https://red-pond-0503f431e-1.westus2.2.azurestaticapps.net",
                "https://grsonsautomotive.com",
                "https://www.grsonsautomotive.com",
                "https://victorious-tree-058c9371e.7.azurestaticapps.net"
            )
            .AllowAnyMethod()
            .AllowAnyHeader()
            .AllowCredentials()
    );
});



// Configure Authentication with Cookie Authentication
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/api/user/login";
        options.Cookie.SameSite = SameSiteMode.None;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        options.ExpireTimeSpan = TimeSpan.FromMinutes(60);
        options.SlidingExpiration = true;

        options.Events = new CookieAuthenticationEvents
        {
            OnRedirectToLogin = ctx =>
            {
                ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return Task.CompletedTask;
            },
            OnRedirectToAccessDenied = ctx =>
            {
                ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
                return Task.CompletedTask;
            }
        };
    });



builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("PartsWrite", p => p.RequireRole("AdminLow", "AdminHigh"));
    options.AddPolicy("PartsDelete", p => p.RequireRole("AdminHigh"));
    options.AddPolicy("AdminAction", p => p.RequireRole("AdminLow", "AdminHigh")); // broad internal review access
    options.AddPolicy("RefundCommit", p => p.RequireRole("AdminHigh"));
    options.AddPolicy("InventoryDispositionCommit", p => p.RequireRole("AdminHigh"));
    options.AddPolicy("UserAdmin", p => p.RequireRole("AdminHigh"));
});


// Configure DbContext and other services
string connString = builder.Configuration.GetConnectionString("connMSSQL");
builder.Services.AddDbContext<ApplicationDbContext>(options => options.UseSqlServer(connString));
builder.Services.AddScoped<IDataProvider>(_ => new DataProvider(connString));

builder.Services.AddScoped<IPartService, PartService>();
builder.Services.AddScoped<IPartImageService, PartImageService>();
builder.Services.AddScoped<IAvailableService, AvailableService>();
builder.Services.AddScoped<IModelService, ModelService>();
builder.Services.AddScoped<IMakeService, MakeService>();
builder.Services.AddScoped<ICatagoryService, CatagoryService>();
builder.Services.AddScoped<ILocationService, LocationService>();
builder.Services.AddScoped<ISiteService, SiteService>();
builder.Services.AddScoped<IShelfService, ShelfService>();
builder.Services.AddScoped<ISectionService, SectionService>();
builder.Services.AddScoped<IBoxService, BoxService>();
builder.Services.AddScoped<IAreaService, AreaService>();
builder.Services.AddScoped<IAisleService, AisleService>();
builder.Services.AddScoped<IAuditService, AuditService>();
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<IConditionService, ConditionService>();
builder.Services.AddScoped<IShippingPoliciesService, ShippingPoliciesService>();
builder.Services.AddScoped<IRefundRequestService, RefundRequestService>();
builder.Services.AddScoped<IRefundFinalizationService, RefundFinalizationService>();
builder.Services.AddScoped<IRefundInventoryDispositionService, RefundInventoryDispositionService>();
builder.Services.AddScoped<IAdminDiscountCodeService, AdminDiscountCodeService>();

builder.Services.Configure<ContactEmailSettings>(
    builder.Configuration.GetSection("ContactEmailSettings"));

builder.Services.AddScoped<ISmtpEmailService, SmtpEmailService>();
builder.Services.AddScoped<IEmailDeliveryLogService, EmailDeliveryLogService>();

builder.Services.Configure<ShopifySettings>(
    builder.Configuration.GetSection("ShopifySettings"));

builder.Services.AddHttpClient<IShopifyTokenService, ShopifyTokenService>();
builder.Services.AddHttpClient<IShopifyAdminService, ShopifyAdminService>();
builder.Services.AddScoped<IShopifyPartSyncService, ShopifyPartSyncService>();
builder.Services.AddHttpClient<IShopifyOrderService, ShopifyOrderService>();
builder.Services.AddHttpClient<IShopifyRefundService, ShopifyRefundService>();
builder.Services.AddScoped<IShopifyWebhookService, ShopifyWebhookService>();
builder.Services.AddHttpClient<IShopifyWebhookSubscriptionService, ShopifyWebhookSubscriptionService>();
builder.Services.AddScoped<IShopifyCheckoutService, ShopifyCheckoutService>();

builder.Services.AddSingleton<IHttpContextAccessor, HttpContextAccessor>();
builder.Services.AddSingleton<IAuthenticationService<IUserAuthData>, AuthenticationService>();
builder.Services.Configure<StaticFileOptions>(
    builder.Configuration.GetSection("StaticFileOptions"));

var app = builder.Build();

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();

    // Enable CORS in development
}


app.Logger.LogInformation(
    "Static files configured from {WebRootPath}. Uploads configured from {UploadRootPath}",
    webRootPath,
    uploadRootPath);

// Global exception handling (returns our standard BaseResponse shape)
app.UseMiddleware<ApiExceptionMiddleware>();

// IMPORTANT:
// /uploads is served from persistent storage FIRST.
// This means product/return photos never depend on deployed wwwroot content.
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(uploadRootPath),
    RequestPath = "/uploads"
});

// Normal application static files, if any.
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(webRootPath),
    RequestPath = ""
});



app.UseRouting();
app.UseCors("CorsPolicy");
//app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.Run();
