using Microsoft.Extensions.Options;
using Site_2024.Web.Api.Models;
using Site_2024.Web.Api.Models.Shopify;
using System.Text.Json;

namespace Site_2024.Web.Api.Services
{
    public class ShopifyTokenService : IShopifyTokenService
    {
        private readonly HttpClient _httpClient;
        private readonly ShopifySettings _settings;
        private readonly ILogger<ShopifyTokenService> _logger;

        private readonly SemaphoreSlim _lock = new SemaphoreSlim(1, 1);

        private string? _cachedAccessToken;
        private DateTime _expiresAtUtc = DateTime.MinValue;

        public ShopifyTokenService(
            HttpClient httpClient,
            IOptions<ShopifySettings> settings,
            ILogger<ShopifyTokenService> logger)
        {
            _httpClient = httpClient;
            _settings = settings.Value;
            _logger = logger;
        }

        public async Task<string> GetAccessTokenAsync()
        {
            // DEV PARITY SAFETY: All Shopify Admin API clients use this token.
            // Fail closed before returning a cached token or making any HTTP call.
            const string expectedDevStore = "gr-sons-shipping-dev.myshopify.com";
            string configuredShop = NormalizeShopDomain(_settings.ShopDomain ?? string.Empty);
            if (!string.Equals(configuredShop, expectedDevStore, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    "DEV Shopify isolation blocked this request: the configured Shopify store " +
                    "is not the isolated development store. No Shopify call was made.");
            if (!string.IsNullOrWhiteSpace(_cachedAccessToken)
                && DateTime.UtcNow < _expiresAtUtc.AddMinutes(-5))
            {
                return _cachedAccessToken;
            }

            await _lock.WaitAsync();

            try
            {
                if (!string.IsNullOrWhiteSpace(_cachedAccessToken)
                    && DateTime.UtcNow < _expiresAtUtc.AddMinutes(-5))
                {
                    return _cachedAccessToken;
                }

                ValidateSettings();

                string shopDomain = NormalizeShopDomain(_settings.ShopDomain);
                string tokenUrl = $"https://{shopDomain}/admin/oauth/access_token";

                using FormUrlEncodedContent content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "client_credentials",
                    ["client_id"] = _settings.ClientId,
                    ["client_secret"] = _settings.ClientSecret
                });

                using HttpResponseMessage response = await _httpClient.PostAsync(tokenUrl, content);

                string responseBody = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogError(
                        "Shopify token request failed. Status: {StatusCode}. Body: {Body}",
                        response.StatusCode,
                        responseBody);

                    throw new ApplicationException(
                        $"Shopify token request failed: {(int)response.StatusCode} {response.ReasonPhrase}. Body: {responseBody}");
                }

                ShopifyAccessTokenResponse? tokenResponse =
                    JsonSerializer.Deserialize<ShopifyAccessTokenResponse>(responseBody);

                if (tokenResponse == null || string.IsNullOrWhiteSpace(tokenResponse.AccessToken))
                {
                    throw new ApplicationException("Shopify token response did not include an access token.");
                }

                _cachedAccessToken = tokenResponse.AccessToken;
                _expiresAtUtc = DateTime.UtcNow.AddSeconds(tokenResponse.ExpiresIn);

                return _cachedAccessToken;
            }
            finally
            {
                _lock.Release();
            }
        }

        private void ValidateSettings()
        {
            if (string.IsNullOrWhiteSpace(_settings.ShopDomain))
            {
                throw new ApplicationException("ShopifySettings:ShopDomain is missing.");
            }

            if (string.IsNullOrWhiteSpace(_settings.ClientId))
            {
                throw new ApplicationException("ShopifySettings:ClientId is missing.");
            }

            if (string.IsNullOrWhiteSpace(_settings.ClientSecret))
            {
                throw new ApplicationException("ShopifySettings:ClientSecret is missing.");
            }
        }

        private static string NormalizeShopDomain(string shopDomain)
        {
            shopDomain = shopDomain
                .Replace("https://", "", StringComparison.OrdinalIgnoreCase)
                .Replace("http://", "", StringComparison.OrdinalIgnoreCase)
                .Trim()
                .TrimEnd('/');

            if (!shopDomain.EndsWith(".myshopify.com", StringComparison.OrdinalIgnoreCase))
            {
                shopDomain = $"{shopDomain}.myshopify.com";
            }

            return shopDomain;
        }
    }
}