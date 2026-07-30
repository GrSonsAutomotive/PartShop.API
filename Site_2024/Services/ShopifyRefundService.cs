using Microsoft.Extensions.Options;
using Site_2024.Models.Domain.RefundRequests;
using Site_2024.Web.Api.Models;
using Site_2024.Web.Api.Models.Shopify;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Site_2024.Web.Api.Services
{
    public class ShopifyRefundService : IShopifyRefundService
    {
        private const decimal MoneyTolerance = 0.01m;

        private static readonly JsonSerializerOptions JsonOptions =
            new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };

        private readonly HttpClient _httpClient;
        private readonly IShopifyTokenService _tokenService;
        private readonly ShopifySettings _settings;
        private readonly ILogger<ShopifyRefundService> _logger;

        public ShopifyRefundService(
            HttpClient httpClient,
            IShopifyTokenService tokenService,
            IOptions<ShopifySettings> settings,
            ILogger<ShopifyRefundService> logger)
        {
            _httpClient = httpClient;
            _tokenService = tokenService;
            _settings = settings.Value;
            _logger = logger;
        }

        public async Task<ShopifyRefundExecutionResult> CreateRefundAsync(
            RefundFinalization finalization,
            Action markDispatchStarted)
        {
            ValidateFinalization(finalization);

            List<ShopifyPreparedSuggestedTransaction> suggestions =
                ReadPreparedTransactions(finalization.PreparedCalculationJson);

            List<object> transactions = BuildTransactions(
                finalization,
                suggestions);

            Dictionary<string, object?> input =
                new Dictionary<string, object?>
                {
                    ["orderId"] = ResolveOrderGid(finalization),
                    ["currency"] = finalization.CurrencyCode,
                    ["notify"] = false,
                    ["allowOverRefunding"] = false,
                    ["note"] =
                        $"Site_2024 return request #{finalization.RefundRequestId}",
                    ["refundLineItems"] = finalization.Items.Select(item => new
                    {
                        lineItemId =
                            $"gid://shopify/LineItem/{item.ShopifyLineItemId}",
                        quantity = item.RefundQuantity,
                        restockType = "NO_RESTOCK"
                    }).ToArray(),
                    ["transactions"] = transactions
                };

            if (finalization.OriginalShippingRefundAmount > 0m)
            {
                input["shipping"] = new
                {
                    fullRefund = true
                };
            }

            if (finalization.BuyerPaidLabelDeductionAmount > 0m
                || finalization.AdditionalDeductionAmount > 0m)
            {
                input["discrepancyReason"] = "OTHER";
            }

            string mutation = @"
mutation CommitSiteReturnRefund(
  $input: RefundInput!,
  $idempotencyKey: String!
) {
  refundCreate(input: $input)
    @idempotent(key: $idempotencyKey) {
    refund {
      id
      legacyResourceId
      totalRefundedSet {
        presentmentMoney { amount currencyCode }
        shopMoney { amount currencyCode }
      }
      transactions(first: 20) {
        nodes {
          id
          status
          kind
          gateway
          amountSet {
            presentmentMoney { amount currencyCode }
          }
        }
      }
    }
    userErrors {
      field
      message
    }
  }
}";

            var variables = new
            {
                input,
                idempotencyKey = finalization.IdempotencyKey
            };

            // After this marker is persisted, any retry must reuse this exact
            // mutation input and the same persisted Shopify idempotency key.
            markDispatchStarted?.Invoke();

            using JsonDocument doc =
                await SendGraphQlAsync(mutation, variables);

            ThrowIfTopLevelErrors(doc, "Shopify refundCreate");

            JsonElement payload = doc.RootElement
                .GetProperty("data")
                .GetProperty("refundCreate");

            ThrowIfUserErrors(payload, "Shopify refundCreate");

            JsonElement refund = payload.GetProperty("refund");

            if (refund.ValueKind == JsonValueKind.Null)
            {
                throw new InvalidOperationException(
                    "Shopify did not return a refund record.");
            }

            ShopifyRefundExecutionResult result = MapRefundResult(refund);
            result.RawResponseJson = doc.RootElement.GetRawText();

            return result;
        }

        public async Task<ShopifyRefundExecutionResult> GetRefundStatusAsync(
            string shopifyRefundGid)
        {
            if (string.IsNullOrWhiteSpace(shopifyRefundGid))
            {
                throw new InvalidOperationException(
                    "A Shopify refund ID is required for reconciliation.");
            }

            string query = @"
query GetSiteReturnRefundStatus($id: ID!) {
  refund(id: $id) {
    id
    legacyResourceId
    totalRefundedSet {
      presentmentMoney { amount currencyCode }
      shopMoney { amount currencyCode }
    }
    transactions(first: 20) {
      nodes {
        id
        status
        kind
        gateway
        amountSet {
          presentmentMoney { amount currencyCode }
        }
      }
    }
  }
}";

            using JsonDocument doc = await SendGraphQlAsync(
                query,
                new { id = shopifyRefundGid.Trim() });

            ThrowIfTopLevelErrors(doc, "Shopify refund status");

            JsonElement refund = doc.RootElement
                .GetProperty("data")
                .GetProperty("refund");

            if (refund.ValueKind == JsonValueKind.Null)
            {
                throw new InvalidOperationException(
                    "Shopify could not find the saved refund during reconciliation.");
            }

            ShopifyRefundExecutionResult result = MapRefundResult(refund);
            result.RawResponseJson = doc.RootElement.GetRawText();
            return result;
        }

        private static void ValidateFinalization(
            RefundFinalization finalization)
        {
            if (finalization == null)
            {
                throw new ArgumentNullException(nameof(finalization));
            }

            if (finalization.RefundRequestId <= 0
                || finalization.ShopifyOrderId <= 0
                || string.IsNullOrWhiteSpace(finalization.CurrencyCode)
                || string.IsNullOrWhiteSpace(finalization.IdempotencyKey))
            {
                throw new InvalidOperationException(
                    "The prepared final refund is missing required Shopify data.");
            }

            if (finalization.Items == null
                || finalization.Items.Count == 0
                || finalization.Items.Any(item =>
                    item.ShopifyLineItemId <= 0
                    || item.RefundQuantity <= 0
                    || item.RefundQuantity > item.QuantityReceivedSnapshot
                    || item.RefundQuantity
                        > item.ShopifyRefundableQuantitySnapshot))
            {
                throw new InvalidOperationException(
                    "The prepared final refund contains invalid line-item quantities.");
            }

            if (finalization.FinalRefundAmount < 0m
                || finalization.FinalRefundAmount
                    > finalization.ShopifyMaximumRefundableAmount)
            {
                throw new InvalidOperationException(
                    "The prepared refund amount is outside Shopify's refundable limit.");
            }
        }

        private static string ResolveOrderGid(
            RefundFinalization finalization)
        {
            return string.IsNullOrWhiteSpace(finalization.ShopifyOrderGid)
                ? $"gid://shopify/Order/{finalization.ShopifyOrderId}"
                : finalization.ShopifyOrderGid.Trim();
        }

        private static List<ShopifyPreparedSuggestedTransaction>
            ReadPreparedTransactions(string? preparedCalculationJson)
        {
            if (string.IsNullOrWhiteSpace(preparedCalculationJson))
            {
                throw new InvalidOperationException(
                    "The prepared Shopify transaction snapshot is missing. Prepare the refund again before dispatch.");
            }

            try
            {
                using JsonDocument doc =
                    JsonDocument.Parse(preparedCalculationJson);

                if (!doc.RootElement.TryGetProperty(
                        "suggestedTransactions",
                        out JsonElement element)
                    || element.ValueKind != JsonValueKind.Array)
                {
                    throw new InvalidOperationException(
                        "The prepared Shopify transaction snapshot is missing.");
                }

                return JsonSerializer.Deserialize<
                        List<ShopifyPreparedSuggestedTransaction>>(
                        element.GetRawText(),
                        JsonOptions)
                    ?? new List<ShopifyPreparedSuggestedTransaction>();
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException(
                    "The prepared Shopify transaction snapshot could not be read.",
                    ex);
            }
        }

        private static List<object> BuildTransactions(
            RefundFinalization finalization,
            IReadOnlyCollection<ShopifyPreparedSuggestedTransaction> suggestions)
        {
            if (finalization.FinalRefundAmount == 0m)
            {
                return new List<object>();
            }

            List<ShopifyPreparedSuggestedTransaction> usable = suggestions
                .Where(item =>
                    !string.IsNullOrWhiteSpace(item.ParentTransactionGid)
                    && !string.IsNullOrWhiteSpace(
                        item.Gateway ?? item.ParentTransactionGateway))
                .ToList();

            if (usable.Count == 0)
            {
                throw new InvalidOperationException(
                    "Shopify did not provide an original payment transaction for this refund. Reload the preview before confirming.");
            }

            decimal remaining = MoneyRound(finalization.FinalRefundAmount);
            List<object> transactions = new List<object>();
            string orderGid = ResolveOrderGid(finalization);

            foreach (ShopifyPreparedSuggestedTransaction suggestion in usable)
            {
                decimal capacity = suggestion.MaximumRefundableAmount
                    ?? suggestion.Amount;

                capacity = Math.Max(0m, MoneyRound(capacity));

                if (capacity <= 0m || remaining <= MoneyTolerance)
                {
                    continue;
                }

                decimal amount = MoneyRound(Math.Min(remaining, capacity));

                transactions.Add(new
                {
                    orderId = orderGid,
                    parentId = suggestion.ParentTransactionGid,
                    kind = "REFUND",
                    gateway =
                        suggestion.Gateway
                        ?? suggestion.ParentTransactionGateway,
                    amount = amount.ToString(
                        "0.00",
                        CultureInfo.InvariantCulture)
                });

                remaining = MoneyRound(remaining - amount);
            }

            if (remaining > MoneyTolerance)
            {
                throw new InvalidOperationException(
                    "Shopify's prepared payment transactions no longer cover the final refund amount. Reload and prepare the calculation again.");
            }

            return transactions;
        }

        private static ShopifyRefundExecutionResult MapRefundResult(
            JsonElement refund)
        {
            string refundGid =
                refund.GetProperty("id").GetString()
                ?? string.Empty;

            JsonElement money = refund
                .GetProperty("totalRefundedSet")
                .GetProperty("presentmentMoney");

            ShopifyRefundExecutionResult result =
                new ShopifyRefundExecutionResult
                {
                    ShopifyRefundGid = refundGid,
                    ShopifyRefundId = ReadLegacyId(refund, refundGid),
                    ActualRefundedAmount = ReadDecimal(money, "amount"),
                    CurrencyCode =
                        GetOptionalString(money, "currencyCode")
                        ?? string.Empty
                };

            JsonElement nodes = refund
                .GetProperty("transactions")
                .GetProperty("nodes");

            foreach (JsonElement node in nodes.EnumerateArray())
            {
                string? transactionGid =
                    GetOptionalString(node, "id");

                JsonElement transactionMoney = node
                    .GetProperty("amountSet")
                    .GetProperty("presentmentMoney");

                result.Transactions.Add(
                    new ShopifyRefundTransactionResult
                    {
                        ShopifyTransactionGid = transactionGid,
                        ShopifyTransactionId = ReadOptionalLegacyId(
                            node,
                            transactionGid),
                        Status = GetOptionalString(node, "status"),
                        Kind = GetOptionalString(node, "kind"),
                        Gateway = GetOptionalString(node, "gateway"),
                        Amount = ReadDecimal(
                            transactionMoney,
                            "amount"),
                        CurrencyCode =
                            GetOptionalString(
                                transactionMoney,
                                "currencyCode")
                            ?? result.CurrencyCode
                    });
            }

            return result;
        }

        private async Task<JsonDocument> SendGraphQlAsync(
            string query,
            object variables)
        {
            string shopDomain = NormalizeShopDomain(_settings.ShopDomain);
            string endpoint =
                $"https://{shopDomain}/admin/api/{_settings.ApiVersion}/graphql.json";
            string token = await _tokenService.GetAccessTokenAsync();

            string json = JsonSerializer.Serialize(new
            {
                query,
                variables
            });

            using HttpRequestMessage request =
                new HttpRequestMessage(HttpMethod.Post, endpoint);

            request.Headers.Add("X-Shopify-Access-Token", token);
            request.Content =
                new StringContent(json, Encoding.UTF8, "application/json");

            using HttpResponseMessage response =
                await _httpClient.SendAsync(request);

            string responseText =
                await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError(
                    "Shopify refund GraphQL request failed. Status: {StatusCode}. Body: {Body}",
                    response.StatusCode,
                    responseText);

                throw new ApplicationException(
                    $"Shopify GraphQL request failed: {(int)response.StatusCode} {response.ReasonPhrase}. Body: {responseText}");
            }

            return JsonDocument.Parse(responseText);
        }

        private static void ThrowIfTopLevelErrors(
            JsonDocument doc,
            string operation)
        {
            if (!doc.RootElement.TryGetProperty(
                    "errors",
                    out JsonElement errors)
                || errors.ValueKind != JsonValueKind.Array
                || errors.GetArrayLength() == 0)
            {
                return;
            }

            string messages = string.Join(
                "; ",
                errors.EnumerateArray().Select(error =>
                    GetOptionalString(error, "message")
                    ?? error.ToString()));

            throw new ApplicationException(
                $"{operation} failed: {messages}");
        }

        private static void ThrowIfUserErrors(
            JsonElement payload,
            string operation)
        {
            if (!payload.TryGetProperty(
                    "userErrors",
                    out JsonElement errors)
                || errors.ValueKind != JsonValueKind.Array
                || errors.GetArrayLength() == 0)
            {
                return;
            }

            string messages = string.Join(
                "; ",
                errors.EnumerateArray().Select(error =>
                {
                    string field = error.TryGetProperty(
                            "field",
                            out JsonElement fieldElement)
                        ? fieldElement.ToString()
                        : string.Empty;

                    string message =
                        GetOptionalString(error, "message")
                        ?? "Unknown Shopify error.";

                    return string.IsNullOrWhiteSpace(field)
                        ? message
                        : $"{field}: {message}";
                }));

            throw new ApplicationException(
                $"{operation} failed: {messages}");
        }

        private static string NormalizeShopDomain(string shopDomain)
        {
            shopDomain = (shopDomain ?? string.Empty)
                .Replace(
                    "https://",
                    string.Empty,
                    StringComparison.OrdinalIgnoreCase)
                .Replace(
                    "http://",
                    string.Empty,
                    StringComparison.OrdinalIgnoreCase)
                .Trim()
                .TrimEnd('/');

            if (!shopDomain.EndsWith(
                    ".myshopify.com",
                    StringComparison.OrdinalIgnoreCase))
            {
                shopDomain = $"{shopDomain}.myshopify.com";
            }

            return shopDomain;
        }

        private static decimal ReadDecimal(
            JsonElement element,
            string propertyName)
        {
            JsonElement value = element.GetProperty(propertyName);

            if (value.ValueKind == JsonValueKind.Number
                && value.TryGetDecimal(out decimal numeric))
            {
                return numeric;
            }

            string? text = value.GetString();

            return decimal.TryParse(
                text,
                NumberStyles.Number,
                CultureInfo.InvariantCulture,
                out decimal parsed)
                    ? parsed
                    : 0m;
        }

        private static string? GetOptionalString(
            JsonElement element,
            string propertyName)
        {
            if (!element.TryGetProperty(
                    propertyName,
                    out JsonElement value)
                || value.ValueKind == JsonValueKind.Null)
            {
                return null;
            }

            return value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : value.ToString();
        }

        private static long ReadLegacyId(
            JsonElement element,
            string gid)
        {
            long? value = ReadOptionalLegacyId(element, gid);

            if (!value.HasValue)
            {
                throw new InvalidOperationException(
                    $"Shopify returned an invalid resource ID: {gid}");
            }

            return value.Value;
        }

        private static long? ReadOptionalLegacyId(
            JsonElement element,
            string? gid)
        {
            if (element.TryGetProperty(
                    "legacyResourceId",
                    out JsonElement legacy)
                && legacy.ValueKind != JsonValueKind.Null)
            {
                if (legacy.ValueKind == JsonValueKind.Number
                    && legacy.TryGetInt64(out long numeric))
                {
                    return numeric;
                }

                if (long.TryParse(legacy.ToString(), out long parsed))
                {
                    return parsed;
                }
            }

            if (!string.IsNullOrWhiteSpace(gid)
                && long.TryParse(gid.Split('/').Last(), out long fromGid))
            {
                return fromGid;
            }

            return null;
        }

        private static decimal MoneyRound(decimal value)
        {
            return Math.Round(
                value,
                2,
                MidpointRounding.AwayFromZero);
        }
    }
}
