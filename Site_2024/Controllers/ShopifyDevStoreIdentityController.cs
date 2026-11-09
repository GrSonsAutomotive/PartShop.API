using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Site_2024.Web.Api.Models;
using Site_2024.Web.Api.Services;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Site_2024.Web.Api.Controllers
{
    // Identity preflight only: never writes to Shopify, changes a shop domain
    // or exposes credentials. This controller is for the DEV API branch.
    [ApiController]
    [Route("api/shipping-migration/legacy")]
    public sealed class ShopifyDevStoreIdentityController : ControllerBase
    {
        private const string ExpectedDev = "gr-sons-shipping-dev.myshopify.com";
        private readonly ShopifySettings _shopify;
        private readonly IShopifyTokenService _token;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<ShopifyDevStoreIdentityController> _logger;

        public ShopifyDevStoreIdentityController(
            IOptions<ShopifySettings> settings,
            IShopifyTokenService token,
            IHttpClientFactory httpClientFactory,
            ILogger<ShopifyDevStoreIdentityController> logger)
        {
            _shopify = settings.Value;
            _token = token;
            _httpClientFactory = httpClientFactory;
            _logger = logger;
        }

        [HttpGet("shop-identity")]
        [Authorize(Policy = "PartsDelete")]
        public async Task<IActionResult> GetIdentity(CancellationToken ct)
        {
            string configured = (_shopify.ShopDomain ?? "").Trim().ToLowerInvariant();
            bool safeDomain = configured.Length > ".myshopify.com".Length &&
                configured.EndsWith(".myshopify.com", StringComparison.Ordinal) &&
                configured.All(c => char.IsAsciiLetterOrDigit(c) || c == '.' || c == '-');

            // Do not request access tokens or make HTTP calls for malformed
            // configuration or an unexpected domain.
            if (!safeDomain)
                return StatusCode(503, new
                {
                    readOnly = true,
                    isolatedDevStoreVerified = false,
                    message = "Configured Shopify domain is invalid. No writes permitted."
                });

            try
            {
                // Read-only Shopify GraphQL query. The token service resolves
                // its token against the CONFIGURED store, which may be PROD.
                // This is safe because the request only asks for shop identity.
                string accessToken = await _token.GetAccessTokenAsync();
                using var request = new HttpRequestMessage(HttpMethod.Post,
                    $"https://{configured}/admin/api/2026-07/graphql.json");
                request.Headers.Add("X-Shopify-Access-Token", accessToken);
                request.Content = new StringContent(
                    JsonSerializer.Serialize(new
                    {
                        query = "query SiteDevShopIdentity { shop { myshopifyDomain } }"
                    }), Encoding.UTF8, "application/json");

                using HttpClient client = _httpClientFactory.CreateClient();
                using HttpResponseMessage reply = await client.SendAsync(request, ct);
                if (!reply.IsSuccessStatusCode)
                    throw new InvalidOperationException(
                        $"Shopify identity read failed with HTTP {(int)reply.StatusCode}");

                await using var stream = await reply.Content.ReadAsStreamAsync(ct);
                using JsonDocument doc = await JsonDocument.ParseAsync(stream,
                    cancellationToken: ct);
                JsonElement root = doc.RootElement;
                if (root.TryGetProperty("errors", out _))
                    throw new InvalidOperationException("Shopify identity GraphQL errors");

                string actual = (root.GetProperty("data").GetProperty("shop")
                    .GetProperty("myshopifyDomain").GetString() ?? "")
                    .Trim().ToLowerInvariant();
                bool matches = actual == configured;
                bool isolated = matches && actual == ExpectedDev;

                return Ok(new
                {
                    readOnly = true,
                    configuredShopDomain = configured,
                    actualShopDomain = actual,
                    configuredMatchesActual = matches,
                    isolatedDevStoreVerified = isolated,
                    devRehearsalWritesEnabled = false,
                    message = isolated
                        ? "Verified isolated Shopify DEV store; no writes performed."
                        : "NOT the expected isolated DEV Shopify store. Do not run source mutations."
                });
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Shopify DEV identity preflight failed: {FailureType}",
                    ex.GetType().Name);
                return StatusCode(502, new
                {
                    readOnly = true,
                    isolatedDevStoreVerified = false,
                    message = "Unable to authenticate and verify Shopify DEV store identity. No writes permitted."
                });
            }
        }
    }
}
