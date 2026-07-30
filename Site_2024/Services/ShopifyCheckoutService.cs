using Microsoft.Extensions.Options;
using Site_2024.Web.Api.Extensions;
using Site_2024.Web.Api.Interfaces;
using Site_2024.Web.Api.Models;
using System.Data;
using System.Data.SqlClient;
using System.Text;

namespace Site_2024.Web.Api.Services
{
    public class ShopifyCheckoutService : IShopifyCheckoutService
    {
        private readonly IDataProvider _data;
        private readonly ShopifySettings _shopifySettings;
        private readonly ILogger<ShopifyCheckoutService> _logger;

        public ShopifyCheckoutService(
            IDataProvider data,
            IOptions<ShopifySettings> shopifySettings,
            ILogger<ShopifyCheckoutService> logger)
        {
            _data = data;
            _shopifySettings = shopifySettings.Value;
            _logger = logger;
        }

        public ShopifyCheckoutResult CreateCheckoutUrl(
            ShopifyCheckoutRequest request)
        {
            if (request == null ||
                request.Items == null ||
                request.Items.Count == 0)
            {
                throw new InvalidOperationException(
                    "At least one checkout item is required.");
            }

            string shopBaseUrl =
                NormalizeShopDomain(_shopifySettings.ShopDomain);

            List<ShopifyCheckoutLineItemResult> lineItems = new();
            List<string> errors = new();

            foreach (
                ShopifyCheckoutLineItemRequest requestItem
                in request.Items)
            {
                if (requestItem == null || requestItem.PartId <= 0)
                {
                    errors.Add("Invalid cart item.");
                    continue;
                }

                int requestedQuantity =
                    requestItem.Quantity <= 0
                        ? 1
                        : requestItem.Quantity;

                ShopifyCheckoutPart? part =
                    GetCheckoutPart(requestItem.PartId);

                if (part == null)
                {
                    errors.Add(
                        $"Part #{requestItem.PartId} was not found.");
                    continue;
                }

                if (!IsAvailable(part.AvailableStatus))
                {
                    errors.Add(
                        $"{part.PartName} is no longer available.");
                    continue;
                }

                if (!part.AllowsOnlineCheckout)
                {
                    errors.Add(
                        $"{part.PartName} requires a custom shipping quote and cannot be purchased through Shopify. Please contact GR & Sons directly.");
                    continue;
                }

                if (!part.ShopifyVariantId.HasValue ||
                    part.ShopifyVariantId.Value <= 0)
                {
                    errors.Add(
                        $"{part.PartName} is not ready for Shopify checkout yet.");
                    continue;
                }

                if (part.Quantity <= 0)
                {
                    errors.Add(
                        $"{part.PartName} is out of stock.");
                    continue;
                }

                if (requestedQuantity > part.Quantity)
                {
                    errors.Add(
                        $"Only {part.Quantity} of {part.PartName} is available.");
                    continue;
                }

                lineItems.Add(
                    new ShopifyCheckoutLineItemResult
                    {
                        PartId = part.PartId,
                        PartName = part.PartName,
                        PartNumber = part.PartNumber,
                        Quantity = requestedQuantity,
                        ShopifyVariantId =
                            part.ShopifyVariantId.Value,
                        Price = part.Price
                    });
            }

            if (errors.Count > 0)
            {
                throw new InvalidOperationException(
                    string.Join(" ", errors));
            }

            if (lineItems.Count == 0)
            {
                throw new InvalidOperationException(
                    "No valid checkout items were found.");
            }

            Guid checkoutToken = Guid.NewGuid();
            string checkoutTokenText =
                checkoutToken.ToString("N");

            string variantPairs = string.Join(
                ",",
                lineItems.Select(
                    item =>
                        $"{item.ShopifyVariantId}:{item.Quantity}"));

            string partIds = string.Join(
                ",",
                lineItems.Select(item => item.PartId));

            InsertCheckoutSession(
                checkoutToken,
                partIds,
                DateTime.UtcNow.AddDays(30));

            StringBuilder url = new();
            url.Append(shopBaseUrl.TrimEnd('/'));
            url.Append("/cart/");
            url.Append(variantPairs);

            List<string> query = new()
            {
                "utm_source=site_2024",
                "utm_medium=site_checkout",
                $"attributes[site_2024_part_ids]={Uri.EscapeDataString(partIds)}",
                $"attributes[site_2024_checkout_token]={Uri.EscapeDataString(checkoutTokenText)}"
            };

            url.Append('?');
            url.Append(string.Join("&", query));

            _logger.LogInformation(
                "Created Shopify checkout {CheckoutToken} for local parts {PartIds} using Shopify variants {VariantIds}.",
                checkoutTokenText,
                partIds,
                string.Join(
                    ",",
                    lineItems.Select(
                        item => item.ShopifyVariantId)));

            return new ShopifyCheckoutResult
            {
                CheckoutUrl = url.ToString(),
                CheckoutToken = checkoutTokenText,
                Items = lineItems
            };
        }

        public ShopifyCheckoutStatusResult? GetCheckoutStatus(
            string checkoutToken)
        {
            if (!TryParseCheckoutToken(
                checkoutToken,
                out Guid parsedToken))
            {
                throw new InvalidOperationException(
                    "Checkout token is invalid.");
            }

            ShopifyCheckoutStatusResult? result = null;
            const string procName =
                "[dbo].[ShopifyCheckoutSessions_GetByToken]";

            _data.ExecuteCmd(
                procName,
                inputParamMapper:
                    delegate (SqlParameterCollection col)
                    {
                        col.AddWithValue(
                            "@CheckoutToken",
                            parsedToken);
                    },
                singleRecordMapper:
                    delegate (IDataReader reader, short set)
                    {
                        int index = 0;
                        Guid token =
                            reader.GetSafeGuid(index++);

                        string status =
                            reader.GetSafeString(index++);

                        result =
                            new ShopifyCheckoutStatusResult
                            {
                                CheckoutToken =
                                    token.ToString("N"),
                                Status = status,
                                IsCompleted =
                                    string.Equals(
                                        status,
                                        "Paid",
                                        StringComparison
                                            .OrdinalIgnoreCase),
                                ShopifyOrderId =
                                    reader.GetSafeInt64Nullable(
                                        index++),
                                OrderName =
                                    reader.GetSafeString(index++),
                                CreatedAtUtc =
                                    reader.GetSafeDateTime(
                                        index++),
                                CompletedAtUtc =
                                    reader.GetSafeDateTimeNullable(
                                        index++),
                                ExpiresAtUtc =
                                    reader.GetSafeDateTime(
                                        index++)
                            };
                    });

            return result;
        }

        private void InsertCheckoutSession(
            Guid checkoutToken,
            string partIds,
            DateTime expiresAtUtc)
        {
            const string procName =
                "[dbo].[ShopifyCheckoutSessions_Insert]";

            _data.ExecuteCmd(
                procName,
                inputParamMapper:
                    delegate (SqlParameterCollection col)
                    {
                        col.AddWithValue(
                            "@CheckoutToken",
                            checkoutToken);

                        col.AddWithValue(
                            "@PartIds",
                            string.IsNullOrWhiteSpace(partIds)
                                ? DBNull.Value
                                : partIds);

                        col.AddWithValue(
                            "@ExpiresAtUtc",
                            expiresAtUtc);
                    },
                singleRecordMapper:
                    delegate (IDataReader reader, short set)
                    {
                        // The procedure returns the inserted row so
                        // database failures surface before redirect.
                    });
        }

        private ShopifyCheckoutPart? GetCheckoutPart(
            int partId)
        {
            const string procName =
                "[dbo].[Parts_Checkout_GetById]";

            ShopifyCheckoutPart? part = null;

            _data.ExecuteCmd(
                procName,
                inputParamMapper:
                    delegate (SqlParameterCollection col)
                    {
                        col.AddWithValue("@PartId", partId);
                    },
                singleRecordMapper:
                    delegate (IDataReader reader, short set)
                    {
                        int index = 0;

                        part = new ShopifyCheckoutPart
                        {
                            PartId =
                                reader.GetSafeInt32(index++),
                            PartName =
                                reader.GetSafeString(index++),
                            PartNumber =
                                reader.GetSafeString(index++),
                            Price =
                                reader.GetSafeDecimal(index++),
                            Quantity =
                                reader.GetSafeInt32(index++),
                            AvailableId =
                                reader.GetSafeInt32(index++),
                            AvailableStatus =
                                reader.GetSafeString(index++),
                            ShippingPolicyId =
                                reader.GetSafeInt32Nullable(index++),
                            ShippingPolicyName =
                                reader.GetSafeString(index++),
                            AllowsOnlineCheckout =
                                reader.GetSafeBool(index++),
                            ShopifyProductId =
                                reader.GetSafeInt64Nullable(
                                    index++),
                            ShopifyVariantId =
                                reader.GetSafeInt64Nullable(
                                    index++),
                            ShopifyOrderId =
                                reader.GetSafeInt64Nullable(
                                    index++),
                            SoldOnUtc =
                                reader.GetSafeDateTimeNullable(
                                    index++)
                        };
                    });

            return part;
        }

        private static bool IsAvailable(
            string? availableStatus)
        {
            return string.Equals(
                availableStatus?.Trim(),
                "Available",
                StringComparison.OrdinalIgnoreCase);
        }

        private static bool TryParseCheckoutToken(
            string? checkoutToken,
            out Guid parsedToken)
        {
            string token =
                (checkoutToken ?? string.Empty).Trim();

            return Guid.TryParseExact(
                       token,
                       "N",
                       out parsedToken)
                   ||
                   Guid.TryParse(
                       token,
                       out parsedToken);
        }

        private static string NormalizeShopDomain(
            string shopDomain)
        {
            shopDomain =
                (shopDomain ?? string.Empty)
                    .Trim()
                    .TrimEnd('/');

            if (string.IsNullOrWhiteSpace(shopDomain))
            {
                throw new InvalidOperationException(
                    "Shopify shop domain is missing from ShopifySettings:ShopDomain.");
            }

            if (!shopDomain.StartsWith(
                    "https://",
                    StringComparison.OrdinalIgnoreCase)
                &&
                !shopDomain.StartsWith(
                    "http://",
                    StringComparison.OrdinalIgnoreCase))
            {
                shopDomain = $"https://{shopDomain}";
            }

            return shopDomain;
        }
    }
}
