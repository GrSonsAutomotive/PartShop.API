using Microsoft.Extensions.Options;
using Site_2024.Web.Api.Configurations;
using Site_2024.Web.Api.Extensions;
using Site_2024.Web.Api.Interfaces;
using Site_2024.Models.Domain.RefundRequests;
using Site_2024.Web.Api.Models;
using System.Data;
using System.Data.SqlClient;
using System.Globalization;
using System.Text;
using System.Text.Json;
using StaticFileOptions = Site_2024.Web.Api.Configurations.StaticFileOptions;

namespace Site_2024.Web.Api.Services
{
    public class ShopifyOrderService : IShopifyOrderService
    {
        private readonly HttpClient _httpClient;
        private readonly IShopifyTokenService _tokenService;
        private readonly ShopifySettings _settings;
        private readonly IDataProvider _data;
        private readonly StaticFileOptions _staticFileOptions;
        private readonly ILogger<ShopifyOrderService> _logger;

        public ShopifyOrderService(
            HttpClient httpClient,
            IShopifyTokenService tokenService,
            IOptions<ShopifySettings> settings,
            IDataProvider data,
            IOptions<StaticFileOptions> staticFileOptions,
            ILogger<ShopifyOrderService> logger)
        {
            _httpClient = httpClient;
            _tokenService = tokenService;
            _settings = settings.Value;
            _data = data;
            _staticFileOptions = staticFileOptions.Value;
            _logger = logger;
        }

        public async Task<List<ShopifyOrderSummary>> GetRecentOrdersAsync(int first, string? view)
        {
            first = first <= 0 ? 25 : Math.Min(first, 50);

            string? orderQuery = BuildOrderQuery(view);

            string query = @"
query GetRecentOrders($first: Int!, $query: String) {
  orders(first: $first, reverse: true, sortKey: CREATED_AT, query: $query) {
    nodes {
      id
      name
      createdAt
      email
      displayFinancialStatus
      displayFulfillmentStatus
      customer {
        displayName
        email
      }
      currentTotalPriceSet {
        shopMoney {
          amount
          currencyCode
        }
      }
      lineItems(first: 50) {
        nodes {
          id
          title
          quantity
          sku
          originalUnitPriceSet {
            shopMoney {
              amount
              currencyCode
            }
          }
          variant {
            id
            sku
            image {
              url
            }
            product {
              id
            }
          }
        }
      }
    }
  }
}";

            var variables = new
            {
                first,
                query = orderQuery
            };

            using JsonDocument doc = await SendGraphQlAsync(query, variables);
            ThrowIfTopLevelErrors(doc);

            JsonElement nodes = doc.RootElement
                .GetProperty("data")
                .GetProperty("orders")
                .GetProperty("nodes");

            List<ShopifyOrderSummary> orders = new List<ShopifyOrderSummary>();

            foreach (JsonElement node in nodes.EnumerateArray())
            {
                ShopifyOrderSummary order = MapOrder(node);

                foreach (ShopifyOrderLineItemSummary item in order.LineItems)
                {
                    if (item.ShopifyVariantId.HasValue)
                    {
                        item.LocalPart = GetLocalPartMatchByVariantId(item.ShopifyVariantId.Value);
                    }
                }

                orders.Add(order);
            }

            return orders;
        }


        public async Task<ShopifyReturnOrderLookupResult?>
            GetOrderForReturnAsync(
                string orderNumber,
                string? expectedEmail)
        {
            string normalizedOrderName =
                NormalizeOrderName(orderNumber);

            if (string.IsNullOrWhiteSpace(normalizedOrderName))
            {
                throw new InvalidOperationException(
                    "Order number is required.");
            }

            string query = @"
query GetReturnOrder($query: String!) {
  orders(
    first: 10,
    reverse: true,
    sortKey: PROCESSED_AT,
    query: $query
  ) {
    nodes {
      id
      name
      createdAt
      email
      displayFinancialStatus
      displayFulfillmentStatus
      shippingAddress {
        countryCodeV2
      }
      fulfillments(first: 20) {
        deliveredAt
      }
      customer {
        displayName
        email
      }
      currentTotalPriceSet {
        shopMoney {
          amount
          currencyCode
        }
      }
      lineItems(first: 100) {
        nodes {
          id
          title
          quantity
          sku
          originalUnitPriceSet {
            shopMoney {
              amount
              currencyCode
            }
          }
          variant {
            id
            sku
            image {
              url
            }
            product {
              id
            }
          }
        }
      }
    }
  }
}";

            string searchQuery =
                $"name:{QuoteSearchValue(normalizedOrderName)}";

            using JsonDocument doc =
                await SendGraphQlAsync(
                    query,
                    new
                    {
                        query = searchQuery
                    });

            ThrowIfTopLevelErrors(doc);

            JsonElement nodes = doc.RootElement
                .GetProperty("data")
                .GetProperty("orders")
                .GetProperty("nodes");

            ShopifyOrderSummary? matchedOrder = null;

            foreach (JsonElement node in nodes.EnumerateArray())
            {
                ShopifyOrderSummary candidate =
                    MapOrder(node);

                if (!string.Equals(
                        NormalizeOrderName(candidate.Name),
                        normalizedOrderName,
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                foreach (
                    ShopifyOrderLineItemSummary item
                    in candidate.LineItems)
                {
                    if (item.ShopifyVariantId.HasValue)
                    {
                        item.LocalPart =
                            GetLocalPartMatchByVariantId(
                                item.ShopifyVariantId.Value);
                    }
                }

                matchedOrder = candidate;
                break;
            }

            if (matchedOrder == null)
            {
                return null;
            }

            string requestedEmail =
                (expectedEmail ?? string.Empty).Trim();

            bool emailMatches =
                !string.IsNullOrWhiteSpace(requestedEmail)
                &&
                !string.IsNullOrWhiteSpace(
                    matchedOrder.CustomerEmail)
                &&
                string.Equals(
                    requestedEmail,
                    matchedOrder.CustomerEmail.Trim(),
                    StringComparison.OrdinalIgnoreCase);

            return new ShopifyReturnOrderLookupResult
            {
                Order = matchedOrder,
                RequestedEmail =
                    string.IsNullOrWhiteSpace(requestedEmail)
                        ? null
                        : requestedEmail,
                CustomerEmailMatches = emailMatches
            };
        }

        public async Task<ShopifyRefundPreviewResult> GetRefundPreviewAsync(
            RefundRequest refundRequest,
            ShopifyRefundPreviewOptions options)
        {
            if (refundRequest == null)
            {
                throw new ArgumentNullException(nameof(refundRequest));
            }

            options ??= new ShopifyRefundPreviewOptions();

            ValidateRefundPreviewRequest(refundRequest, options);

            long shopifyOrderId = refundRequest.ShopifyOrderId!.Value;
            string orderGid = BuildShopifyGid("Order", shopifyOrderId);

            List<RefundRequestItem> inspectedItems = refundRequest.Items
                .Where(item => (item.QuantityReceived ?? 0) > 0)
                .ToList();

            ShopifyRefundableOrderState orderState =
                await LoadRefundableOrderStateAsync(orderGid);

            if (!orderState.Exists)
            {
                throw new InvalidOperationException(
                    $"Shopify order {shopifyOrderId} was not found.");
            }

            if (!orderState.Refundable)
            {
                throw new InvalidOperationException(
                    "Shopify reports that this order is not refundable.");
            }

            Dictionary<long, RefundRequestItem> localItemsByLineId =
                inspectedItems.ToDictionary(
                    item => item.ShopifyLineItemId!.Value,
                    item => item);

            List<object> refundLineItems = new List<object>();

            foreach (RefundRequestItem item in inspectedItems)
            {
                long lineItemId = item.ShopifyLineItemId!.Value;

                if (!orderState.LineItems.TryGetValue(
                        lineItemId,
                        out ShopifyRefundableLineState? shopifyLine))
                {
                    throw new InvalidOperationException(
                        $"Refund request item {item.Id} is linked to Shopify line item {lineItemId}, but that line item is not present on order {orderState.Name}.");
                }

                int quantityReceived = item.QuantityReceived ?? 0;

                if (quantityReceived > shopifyLine.RefundableQuantity)
                {
                    throw new InvalidOperationException(
                        $"{shopifyLine.Title} has only {shopifyLine.RefundableQuantity} unit(s) remaining refundable in Shopify, but the completed inspection received {quantityReceived}.");
                }

                refundLineItems.Add(new
                {
                    lineItemId = shopifyLine.Gid,
                    quantity = quantityReceived,
                    restockType = "NO_RESTOCK"
                });
            }

            using JsonDocument previewDocument =
                await LoadSuggestedRefundAsync(
                    orderGid,
                    refundLineItems);

            ThrowIfTopLevelErrors(previewDocument);

            JsonElement orderNode = previewDocument.RootElement
                .GetProperty("data")
                .GetProperty("order");

            if (orderNode.ValueKind == JsonValueKind.Null)
            {
                throw new InvalidOperationException(
                    $"Shopify order {shopifyOrderId} was not found while calculating the refund preview.");
            }

            JsonElement itemsOnly = GetRequiredObject(
                orderNode,
                "itemsOnly",
                "Shopify did not return an item refund suggestion.");

            JsonElement itemsAndShipping = GetRequiredObject(
                orderNode,
                "itemsAndShipping",
                "Shopify did not return a shipping refund suggestion.");

            string currencyCode =
                GetMoneyCurrencyCode(
                    itemsOnly,
                    "amountSet",
                    preferPresentment: true)
                ?? orderState.PresentmentCurrencyCode
                ?? orderState.ShopCurrencyCode
                ?? string.Empty;

            decimal subtotalBeforeDiscount =
                GetMoneyAmount(
                    itemsOnly,
                    "subtotalSet",
                    preferPresentment: true);

            decimal merchandiseRefund =
                GetMoneyAmount(
                    itemsOnly,
                    "discountedSubtotalSet",
                    preferPresentment: true);

            decimal cartDiscount =
                GetMoneyAmount(
                    itemsOnly,
                    "totalCartDiscountAmountSet",
                    preferPresentment: true);

            decimal taxRefund =
                GetMoneyAmount(
                    itemsOnly,
                    "totalTaxSet",
                    preferPresentment: true);

            decimal dutiesRefund =
                GetMoneyAmount(
                    itemsOnly,
                    "totalDutiesSet",
                    preferPresentment: true);

            decimal suggestedItemRefund =
                GetMoneyAmount(
                    itemsOnly,
                    "amountSet",
                    preferPresentment: true);

            if (dutiesRefund > 0.01m)
            {
                throw new InvalidOperationException(
                    "Shopify returned refundable duties. Duties are not included in the approved Site_2024 final-refund calculation and require manual review.");
            }

            decimal itemBreakdownTotal =
                merchandiseRefund + taxRefund;

            if (Math.Abs(itemBreakdownTotal - suggestedItemRefund) > 0.01m)
            {
                throw new InvalidOperationException(
                    "Shopify's item refund total does not match its merchandise, tax, and duty breakdown. Manual reconciliation is required before preparing this refund.");
            }

            JsonElement shipping = GetRequiredObject(
                itemsAndShipping,
                "shipping",
                "Shopify did not return a shipping refund breakdown.");

            decimal shippingBaseRefundable =
                GetMoneyAmount(
                    shipping,
                    "amountSet",
                    preferPresentment: true);

            decimal shippingTaxRefundable =
                GetMoneyAmount(
                    shipping,
                    "taxSet",
                    preferPresentment: true);

            decimal itemsAndShippingAmount =
                GetMoneyAmount(
                    itemsAndShipping,
                    "amountSet",
                    preferPresentment: true);

            decimal shippingRefundable = Math.Max(
                0m,
                itemsAndShippingAmount - suggestedItemRefund);

            decimal shippingBreakdownTotal =
                shippingBaseRefundable + shippingTaxRefundable;

            if (Math.Abs(shippingBreakdownTotal - shippingRefundable) > 0.01m)
            {
                throw new InvalidOperationException(
                    "Shopify's shipping refund total does not match its shipping and shipping-tax breakdown. Manual reconciliation is required before preparing this refund.");
            }

            bool originalShippingAllowed =
                refundRequest.SellerError == true;

            decimal originalShippingRefund =
                options.IncludeOriginalShippingRefund
                && originalShippingAllowed
                    ? shippingRefundable
                    : 0m;

            decimal buyerPaidLabelDeduction =
                string.Equals(
                    refundRequest.ReturnShippingPayer,
                    "Buyer",
                    StringComparison.OrdinalIgnoreCase)
                && refundRequest.ReturnLabelCost.GetValueOrDefault() > 0m
                    ? MoneyRound(
                        refundRequest.ReturnLabelCost.GetValueOrDefault())
                    : 0m;

            decimal additionalDeduction =
                MoneyRound(options.AdditionalDeductionAmount);

            decimal maximumRefundable =
                GetMoneyAmount(
                    itemsAndShipping,
                    "maximumRefundableSet",
                    preferPresentment: true);

            decimal finalRefund = MoneyRound(
                merchandiseRefund
                + taxRefund
                + originalShippingRefund
                - buyerPaidLabelDeduction
                - additionalDeduction);

            if (finalRefund < 0m)
            {
                throw new InvalidOperationException(
                    $"The final refund cannot be below 0.00 {currencyCode}. Reduce the deductions before preparing the refund.");
            }

            if (finalRefund > maximumRefundable)
            {
                throw new InvalidOperationException(
                    $"The calculated refund of {finalRefund:0.00} {currencyCode} exceeds Shopify's remaining refundable amount of {maximumRefundable:0.00} {currencyCode}.");
            }

            ShopifyRefundPreviewResult result =
                new ShopifyRefundPreviewResult
                {
                    RefundRequestId = refundRequest.Id,
                    ShopifyOrderId = shopifyOrderId,
                    ShopifyOrderGid = orderState.Gid,
                    OrderName = orderState.Name,
                    OrderIsRefundable = orderState.Refundable,
                    CurrencyCode = currencyCode,
                    ShopCurrencyCode =
                        orderState.ShopCurrencyCode
                        ?? currencyCode,
                    SellerError = refundRequest.SellerError == true,
                    IsInternational =
                        refundRequest.IsInternational == true,
                    ReturnShippingPayer =
                        refundRequest.ReturnShippingPayer,
                    OriginalShippingRequested =
                        options.IncludeOriginalShippingRefund,
                    OriginalShippingAllowed =
                        originalShippingAllowed,
                    MerchandiseSubtotalBeforeDiscountAmount =
                        MoneyRound(subtotalBeforeDiscount),
                    MerchandiseDiscountAmount = MoneyRound(
                        Math.Max(
                            0m,
                            subtotalBeforeDiscount
                            - merchandiseRefund)),
                    CartDiscountAmount = MoneyRound(cartDiscount),
                    MerchandiseRefundAmount =
                        MoneyRound(merchandiseRefund),
                    TaxRefundAmount = MoneyRound(taxRefund),
                    ShopifySuggestedItemRefundAmount =
                        MoneyRound(suggestedItemRefund),
                    ShopifyShippingBaseRefundableAmount =
                        MoneyRound(shippingBaseRefundable),
                    ShopifyShippingTaxRefundableAmount =
                        MoneyRound(shippingTaxRefundable),
                    ShopifyShippingRefundableAmount =
                        MoneyRound(shippingRefundable),
                    OriginalShippingRefundAmount =
                        MoneyRound(originalShippingRefund),
                    BuyerPaidLabelDeductionAmount =
                        buyerPaidLabelDeduction,
                    AdditionalDeductionAmount =
                        additionalDeduction,
                    AdditionalDeductionReason =
                        string.IsNullOrWhiteSpace(
                            options.AdditionalDeductionReason)
                            ? null
                            : options.AdditionalDeductionReason.Trim(),
                    ShopifyMaximumRefundableAmount =
                        MoneyRound(maximumRefundable),
                    FinalRefundAmount = finalRefund,
                    PreviewedAtUtc = DateTime.UtcNow,
                    ShopifyPreviewJson =
                        previewDocument.RootElement.GetRawText()
                };

            MapRefundPreviewLineItems(
                result,
                itemsOnly,
                localItemsByLineId,
                orderState);

            JsonElement selectedSuggestion =
                options.IncludeOriginalShippingRefund
                && originalShippingAllowed
                    ? itemsAndShipping
                    : itemsOnly;

            MapSuggestedTransactions(
                result,
                selectedSuggestion);

            return result;
        }

        public async Task<ShopifyOrderSyncResult> SyncRecentPaidOrdersAsync(int first, int userId)
        {
            ShopifyOrderSyncResult result = new ShopifyOrderSyncResult();
            List<ShopifyOrderSummary> orders = await GetRecentOrdersAsync(first, "awaitingShipment");

            result.OrdersChecked = orders.Count;

            foreach (ShopifyOrderSummary order in orders)
            {
                bool isPaid = IsPaidFinancialStatus(order.DisplayFinancialStatus);

                foreach (ShopifyOrderLineItemSummary item in order.LineItems)
                {
                    result.LineItemsChecked++;

                    ShopifyOrderSyncLineItemResult row = new ShopifyOrderSyncLineItemResult
                    {
                        OrderName = order.Name,
                        ShopifyOrderId = order.ShopifyOrderId,
                        ShopifyLineItemId = item.ShopifyLineItemId,
                        ShopifyVariantId = item.ShopifyVariantId,
                        PartId = item.LocalPart?.PartId,
                        PartName = item.LocalPart?.PartName,
                        QuantityPurchased = item.Quantity
                    };

                    if (!isPaid)
                    {
                        row.Message = $"Skipped because financial status is {order.DisplayFinancialStatus ?? "Unknown"}.";
                        result.SkippedCount++;
                        result.Items.Add(row);
                        continue;
                    }

                    if (item.LocalPart == null)
                    {
                        row.Message = "Skipped because no local part matched this Shopify variant.";
                        result.SkippedCount++;
                        result.Items.Add(row);
                        continue;
                    }

                    if (item.LocalPart.ShopifyOrderId.HasValue && item.LocalPart.ShopifyOrderId.Value == order.ShopifyOrderId)
                    {
                        row.WasAlreadySynced = true;
                        row.Message = "Already synced to this Shopify order.";
                        result.AlreadySyncedCount++;
                        result.Items.Add(row);
                        continue;
                    }

                    if (item.LocalPart.ShopifyOrderId.HasValue && item.LocalPart.ShopifyOrderId.Value != order.ShopifyOrderId)
                    {
                        row.Message = $"Skipped because local part is already attached to Shopify order {item.LocalPart.ShopifyOrderId.Value}.";
                        result.SkippedCount++;
                        result.Items.Add(row);
                        continue;
                    }

                    try
                    {
                        bool wasAlreadySynced = MarkLocalPartSoldFromOrder(
                            item.LocalPart.PartId,
                            order.ShopifyOrderId,
                            item.Quantity,
                            userId);

                        row.WasAlreadySynced = wasAlreadySynced;
                        row.WasSynced = !wasAlreadySynced;
                        row.Message = wasAlreadySynced
                            ? "Already synced to this Shopify order."
                            : "Marked local part sold/unavailable.";

                        if (wasAlreadySynced)
                        {
                            result.AlreadySyncedCount++;
                        }
                        else
                        {
                            result.PartsMarkedSold++;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex,
                            "Failed to sync Shopify order {ShopifyOrderId} line item {ShopifyLineItemId} to local part {PartId}.",
                            order.ShopifyOrderId,
                            item.ShopifyLineItemId,
                            item.LocalPart.PartId);

                        row.Message = ex.Message;
                        result.SkippedCount++;
                    }

                    result.Items.Add(row);
                }
            }

            return result;
        }


        private static void ValidateRefundPreviewRequest(
            RefundRequest refundRequest,
            ShopifyRefundPreviewOptions options)
        {
            if (!refundRequest.ShopifyOrderId.HasValue
                || refundRequest.ShopifyOrderId.Value <= 0)
            {
                throw new InvalidOperationException(
                    "The refund request is not linked to a Shopify order.");
            }

            if (!string.Equals(
                    refundRequest.InspectionStatus,
                    "Completed",
                    StringComparison.OrdinalIgnoreCase)
                || !refundRequest.InspectionCompletedAt.HasValue
                || !refundRequest.ReadyForRefundAt.HasValue)
            {
                throw new InvalidOperationException(
                    "The item-receipt inspection must be completed before loading a final refund preview.");
            }

            if (!refundRequest.SellerError.HasValue)
            {
                throw new InvalidOperationException(
                    "The return decision must specify whether seller error occurred before loading a final refund preview.");
            }

            if (options.AdditionalDeductionAmount < 0m)
            {
                throw new InvalidOperationException(
                    "The additional deduction cannot be negative.");
            }

            if (options.AdditionalDeductionAmount > 0m
                && string.IsNullOrWhiteSpace(
                    options.AdditionalDeductionReason))
            {
                throw new InvalidOperationException(
                    "An additional deduction requires a written reason.");
            }

            if (options.IncludeOriginalShippingRefund
                && refundRequest.SellerError != true)
            {
                throw new InvalidOperationException(
                    "Original outbound shipping can only be refunded when seller error is marked Yes.");
            }

            List<RefundRequestItem> inspectedItems = refundRequest.Items
                .Where(item => (item.QuantityReceived ?? 0) > 0)
                .ToList();

            if (inspectedItems.Count == 0)
            {
                throw new InvalidOperationException(
                    "No inspected, received quantities are available to refund.");
            }

            List<long> duplicateLineItemIds = inspectedItems
                .Where(item => item.ShopifyLineItemId.HasValue)
                .GroupBy(item => item.ShopifyLineItemId!.Value)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key)
                .ToList();

            if (duplicateLineItemIds.Count > 0)
            {
                throw new InvalidOperationException(
                    "The refund request contains duplicate Shopify line-item matches. Correct the matching before preparing the refund.");
            }

            foreach (RefundRequestItem item in inspectedItems)
            {
                if (!item.ShopifyLineItemId.HasValue
                    || item.ShopifyLineItemId.Value <= 0)
                {
                    throw new InvalidOperationException(
                        $"Refund request item {item.Id} has received quantity but is not matched to a Shopify line item.");
                }

                if (!item.InspectionCompletedAt.HasValue)
                {
                    throw new InvalidOperationException(
                        $"Refund request item {item.Id} does not have a completed inspection.");
                }

                int quantityReceived = item.QuantityReceived ?? 0;
                int restockQuantity = item.RestockQuantity ?? 0;
                int holdQuantity = item.HoldQuantity ?? 0;
                int damagedQuantity = item.DamagedQuantity ?? 0;

                if (restockQuantity < 0
                    || holdQuantity < 0
                    || damagedQuantity < 0
                    || restockQuantity
                        + holdQuantity
                        + damagedQuantity
                        != quantityReceived)
                {
                    throw new InvalidOperationException(
                        $"Refund request item {item.Id} has an invalid inspection allocation. Restock, hold, and damaged quantities must equal the quantity received.");
                }

                if (item.QuantityPurchased.HasValue
                    && quantityReceived
                        > item.QuantityPurchased.Value)
                {
                    throw new InvalidOperationException(
                        $"Refund request item {item.Id} received quantity exceeds the quantity purchased.");
                }
            }
        }

        private async Task<ShopifyRefundableOrderState>
            LoadRefundableOrderStateAsync(string orderGid)
        {
            string query = @"
query GetRefundableOrder($orderId: ID!) {
  order(id: $orderId) {
    id
    name
    refundable
    currencyCode
    presentmentCurrencyCode
    lineItems(first: 100) {
      nodes {
        id
        title
        sku
        quantity
        refundableQuantity
        restockable
      }
    }
  }
}";

            using JsonDocument document =
                await SendGraphQlAsync(
                    query,
                    new
                    {
                        orderId = orderGid
                    });

            ThrowIfTopLevelErrors(document);

            JsonElement orderNode = document.RootElement
                .GetProperty("data")
                .GetProperty("order");

            if (orderNode.ValueKind == JsonValueKind.Null)
            {
                return new ShopifyRefundableOrderState
                {
                    Exists = false
                };
            }

            ShopifyRefundableOrderState state =
                new ShopifyRefundableOrderState
                {
                    Exists = true,
                    Gid = GetString(orderNode, "id")
                        ?? orderGid,
                    Name = GetString(orderNode, "name")
                        ?? string.Empty,
                    Refundable = GetBoolean(
                        orderNode,
                        "refundable"),
                    ShopCurrencyCode = GetString(
                        orderNode,
                        "currencyCode"),
                    PresentmentCurrencyCode = GetString(
                        orderNode,
                        "presentmentCurrencyCode")
                };

            if (orderNode.TryGetProperty(
                    "lineItems",
                    out JsonElement lineItems)
                && lineItems.TryGetProperty(
                    "nodes",
                    out JsonElement nodes))
            {
                foreach (JsonElement node in nodes.EnumerateArray())
                {
                    string gid = GetString(node, "id")
                        ?? string.Empty;
                    long numericId = ExtractNumericId(gid);

                    if (numericId <= 0)
                    {
                        continue;
                    }

                    state.LineItems[numericId] =
                        new ShopifyRefundableLineState
                        {
                            Gid = gid,
                            NumericId = numericId,
                            Title = GetString(node, "title")
                                ?? string.Empty,
                            Sku = GetString(node, "sku"),
                            Quantity = GetInt32(
                                node,
                                "quantity"),
                            RefundableQuantity = GetInt32(
                                node,
                                "refundableQuantity"),
                            Restockable = GetBoolean(
                                node,
                                "restockable")
                        };
                }
            }

            return state;
        }

        private async Task<JsonDocument> LoadSuggestedRefundAsync(
            string orderGid,
            List<object> refundLineItems)
        {
            string query = @"
query GetSuggestedRefund(
  $orderId: ID!,
  $refundLineItems: [RefundLineItemInput!]!
) {
  order(id: $orderId) {
    id
    name
    refundable
    currencyCode
    presentmentCurrencyCode

    itemsOnly: suggestedRefund(
      refundLineItems: $refundLineItems,
      refundShipping: false
    ) {
      amountSet {
        shopMoney { amount currencyCode }
        presentmentMoney { amount currencyCode }
      }
      subtotalSet {
        shopMoney { amount currencyCode }
        presentmentMoney { amount currencyCode }
      }
      discountedSubtotalSet {
        shopMoney { amount currencyCode }
        presentmentMoney { amount currencyCode }
      }
      totalCartDiscountAmountSet {
        shopMoney { amount currencyCode }
        presentmentMoney { amount currencyCode }
      }
      totalTaxSet {
        shopMoney { amount currencyCode }
        presentmentMoney { amount currencyCode }
      }
      totalDutiesSet {
        shopMoney { amount currencyCode }
        presentmentMoney { amount currencyCode }
      }
      maximumRefundableSet {
        shopMoney { amount currencyCode }
        presentmentMoney { amount currencyCode }
      }
      refundLineItems {
        quantity
        subtotalSet {
          shopMoney { amount currencyCode }
          presentmentMoney { amount currencyCode }
        }
        totalTaxSet {
          shopMoney { amount currencyCode }
          presentmentMoney { amount currencyCode }
        }
        lineItem {
          id
          title
          sku
          quantity
          refundableQuantity
        }
      }
      shipping {
        amountSet {
          shopMoney { amount currencyCode }
          presentmentMoney { amount currencyCode }
        }
        maximumRefundableSet {
          shopMoney { amount currencyCode }
          presentmentMoney { amount currencyCode }
        }
        taxSet {
          shopMoney { amount currencyCode }
          presentmentMoney { amount currencyCode }
        }
      }
      suggestedTransactions {
        kind
        gateway
        formattedGateway
        accountNumber
        amountSet {
          shopMoney { amount currencyCode }
          presentmentMoney { amount currencyCode }
        }
        maximumRefundableSet {
          shopMoney { amount currencyCode }
          presentmentMoney { amount currencyCode }
        }
        parentTransaction {
          id
          status
          kind
          gateway
        }
      }
    }

    itemsAndShipping: suggestedRefund(
      refundLineItems: $refundLineItems,
      refundShipping: true
    ) {
      amountSet {
        shopMoney { amount currencyCode }
        presentmentMoney { amount currencyCode }
      }
      subtotalSet {
        shopMoney { amount currencyCode }
        presentmentMoney { amount currencyCode }
      }
      discountedSubtotalSet {
        shopMoney { amount currencyCode }
        presentmentMoney { amount currencyCode }
      }
      totalCartDiscountAmountSet {
        shopMoney { amount currencyCode }
        presentmentMoney { amount currencyCode }
      }
      totalTaxSet {
        shopMoney { amount currencyCode }
        presentmentMoney { amount currencyCode }
      }
      totalDutiesSet {
        shopMoney { amount currencyCode }
        presentmentMoney { amount currencyCode }
      }
      maximumRefundableSet {
        shopMoney { amount currencyCode }
        presentmentMoney { amount currencyCode }
      }
      refundLineItems {
        quantity
        subtotalSet {
          shopMoney { amount currencyCode }
          presentmentMoney { amount currencyCode }
        }
        totalTaxSet {
          shopMoney { amount currencyCode }
          presentmentMoney { amount currencyCode }
        }
        lineItem {
          id
          title
          sku
          quantity
          refundableQuantity
        }
      }
      shipping {
        amountSet {
          shopMoney { amount currencyCode }
          presentmentMoney { amount currencyCode }
        }
        maximumRefundableSet {
          shopMoney { amount currencyCode }
          presentmentMoney { amount currencyCode }
        }
        taxSet {
          shopMoney { amount currencyCode }
          presentmentMoney { amount currencyCode }
        }
      }
      suggestedTransactions {
        kind
        gateway
        formattedGateway
        accountNumber
        amountSet {
          shopMoney { amount currencyCode }
          presentmentMoney { amount currencyCode }
        }
        maximumRefundableSet {
          shopMoney { amount currencyCode }
          presentmentMoney { amount currencyCode }
        }
        parentTransaction {
          id
          status
          kind
          gateway
        }
      }
    }
  }
}";

            return await SendGraphQlAsync(
                query,
                new
                {
                    orderId = orderGid,
                    refundLineItems
                });
        }

        private static void MapRefundPreviewLineItems(
            ShopifyRefundPreviewResult result,
            JsonElement suggestedRefund,
            Dictionary<long, RefundRequestItem> localItemsByLineId,
            ShopifyRefundableOrderState orderState)
        {
            if (!suggestedRefund.TryGetProperty(
                    "refundLineItems",
                    out JsonElement refundLineItems)
                || refundLineItems.ValueKind
                    != JsonValueKind.Array)
            {
                throw new InvalidOperationException(
                    "Shopify did not return the suggested refund line items.");
            }

            HashSet<long> mappedLineItemIds = new HashSet<long>();

            foreach (JsonElement refundLineItem
                in refundLineItems.EnumerateArray())
            {
                JsonElement lineItem = GetRequiredObject(
                    refundLineItem,
                    "lineItem",
                    "Shopify returned a refund line without its source line item.");

                string lineItemGid =
                    GetString(lineItem, "id")
                    ?? string.Empty;

                long lineItemId = ExtractNumericId(lineItemGid);

                if (!localItemsByLineId.TryGetValue(
                        lineItemId,
                        out RefundRequestItem? localItem))
                {
                    throw new InvalidOperationException(
                        $"Shopify returned unexpected refund line item {lineItemId}.");
                }

                if (!orderState.LineItems.TryGetValue(
                        lineItemId,
                        out ShopifyRefundableLineState? orderLine))
                {
                    throw new InvalidOperationException(
                        $"Shopify line item {lineItemId} could not be reconciled with the order snapshot.");
                }

                int quantityToRefund =
                    GetInt32(refundLineItem, "quantity");

                int expectedQuantity =
                    localItem.QuantityReceived ?? 0;

                if (quantityToRefund != expectedQuantity)
                {
                    throw new InvalidOperationException(
                        $"Shopify suggested quantity {quantityToRefund} for {orderLine.Title}, but the completed inspection received {expectedQuantity}.");
                }

                decimal subtotal = GetMoneyAmount(
                    refundLineItem,
                    "subtotalSet",
                    preferPresentment: true);

                decimal tax = GetMoneyAmount(
                    refundLineItem,
                    "totalTaxSet",
                    preferPresentment: true);

                result.Items.Add(
                    new ShopifyRefundPreviewLineItem
                    {
                        RefundRequestItemId = localItem.Id,
                        PartId = localItem.PartId,
                        PartName =
                            localItem.PartName
                            ?? localItem.ProductTitle,
                        PartNumber =
                            localItem.PartNumber
                            ?? localItem.Sku,
                        ShopifyLineItemId = lineItemId,
                        ShopifyLineItemGid = lineItemGid,
                        ShopifyTitle =
                            GetString(lineItem, "title")
                            ?? orderLine.Title,
                        ShopifySku =
                            GetString(lineItem, "sku")
                            ?? orderLine.Sku,
                        QuantityPurchased =
                            orderLine.Quantity,
                        ShopifyRefundableQuantity =
                            orderLine.RefundableQuantity,
                        QuantityReceived = expectedQuantity,
                        QuantityToRefund = quantityToRefund,
                        RestockQuantity =
                            localItem.RestockQuantity ?? 0,
                        HoldQuantity =
                            localItem.HoldQuantity ?? 0,
                        DamagedQuantity =
                            localItem.DamagedQuantity ?? 0,
                        ShopifySubtotalAmount =
                            MoneyRound(subtotal),
                        ShopifyTaxAmount =
                            MoneyRound(tax),
                        ShopifyTotalAmount =
                            MoneyRound(subtotal + tax),
                        CurrencyCode = result.CurrencyCode
                    });

                mappedLineItemIds.Add(lineItemId);
            }

            if (mappedLineItemIds.Count
                != localItemsByLineId.Count)
            {
                throw new InvalidOperationException(
                    "Shopify did not return every inspected line item in the refund suggestion.");
            }
        }

        private static void MapSuggestedTransactions(
            ShopifyRefundPreviewResult result,
            JsonElement suggestedRefund)
        {
            if (!suggestedRefund.TryGetProperty(
                    "suggestedTransactions",
                    out JsonElement transactions)
                || transactions.ValueKind
                    != JsonValueKind.Array)
            {
                return;
            }

            foreach (JsonElement transaction
                in transactions.EnumerateArray())
            {
                decimal? maximumRefundable =
                    TryGetMoneyAmount(
                        transaction,
                        "maximumRefundableSet",
                        preferPresentment: true);

                string? parentGid = null;
                long? parentId = null;
                string? parentStatus = null;
                string? parentKind = null;
                string? parentGateway = null;

                if (transaction.TryGetProperty(
                        "parentTransaction",
                        out JsonElement parent)
                    && parent.ValueKind
                        == JsonValueKind.Object)
                {
                    parentGid = GetString(parent, "id");
                    long parsedParentId =
                        ExtractNumericId(parentGid);
                    parentId = parsedParentId > 0
                        ? parsedParentId
                        : null;
                    parentStatus = GetString(parent, "status");
                    parentKind = GetString(parent, "kind");
                    parentGateway = GetString(parent, "gateway");
                }

                result.SuggestedTransactions.Add(
                    new ShopifyRefundSuggestedTransaction
                    {
                        Kind = GetString(transaction, "kind")
                            ?? string.Empty,
                        Gateway = GetString(
                            transaction,
                            "gateway"),
                        FormattedGateway = GetString(
                            transaction,
                            "formattedGateway"),
                        AccountNumber = GetString(
                            transaction,
                            "accountNumber"),
                        Amount = MoneyRound(
                            GetMoneyAmount(
                                transaction,
                                "amountSet",
                                preferPresentment: true)),
                        MaximumRefundableAmount =
                            maximumRefundable.HasValue
                                ? MoneyRound(
                                    maximumRefundable.Value)
                                : null,
                        CurrencyCode =
                            GetMoneyCurrencyCode(
                                transaction,
                                "amountSet",
                                preferPresentment: true)
                            ?? result.CurrencyCode,
                        ParentTransactionId = parentId,
                        ParentTransactionGid = parentGid,
                        ParentTransactionStatus = parentStatus,
                        ParentTransactionKind = parentKind,
                        ParentTransactionGateway = parentGateway
                    });
            }
        }

        private static JsonElement GetRequiredObject(
            JsonElement parent,
            string propertyName,
            string errorMessage)
        {
            if (!parent.TryGetProperty(
                    propertyName,
                    out JsonElement value)
                || value.ValueKind
                    != JsonValueKind.Object)
            {
                throw new InvalidOperationException(errorMessage);
            }

            return value;
        }

        private static decimal GetMoneyAmount(
            JsonElement parent,
            string propertyName,
            bool preferPresentment)
        {
            decimal? value = TryGetMoneyAmount(
                parent,
                propertyName,
                preferPresentment);

            return value ?? 0m;
        }

        private static decimal? TryGetMoneyAmount(
            JsonElement parent,
            string propertyName,
            bool preferPresentment)
        {
            if (!parent.TryGetProperty(
                    propertyName,
                    out JsonElement moneyBag)
                || moneyBag.ValueKind
                    != JsonValueKind.Object)
            {
                return null;
            }

            string firstProperty = preferPresentment
                ? "presentmentMoney"
                : "shopMoney";

            string secondProperty = preferPresentment
                ? "shopMoney"
                : "presentmentMoney";

            JsonElement money;

            if (!moneyBag.TryGetProperty(
                    firstProperty,
                    out money)
                || money.ValueKind
                    != JsonValueKind.Object)
            {
                if (!moneyBag.TryGetProperty(
                        secondProperty,
                        out money)
                    || money.ValueKind
                        != JsonValueKind.Object)
                {
                    return null;
                }
            }

            string? rawAmount = GetString(money, "amount");

            return decimal.TryParse(
                rawAmount,
                NumberStyles.Number,
                CultureInfo.InvariantCulture,
                out decimal amount)
                    ? amount
                    : null;
        }

        private static string? GetMoneyCurrencyCode(
            JsonElement parent,
            string propertyName,
            bool preferPresentment)
        {
            if (!parent.TryGetProperty(
                    propertyName,
                    out JsonElement moneyBag)
                || moneyBag.ValueKind
                    != JsonValueKind.Object)
            {
                return null;
            }

            string firstProperty = preferPresentment
                ? "presentmentMoney"
                : "shopMoney";

            string secondProperty = preferPresentment
                ? "shopMoney"
                : "presentmentMoney";

            if (moneyBag.TryGetProperty(
                    firstProperty,
                    out JsonElement first)
                && first.ValueKind
                    == JsonValueKind.Object)
            {
                string? currency =
                    GetString(first, "currencyCode");

                if (!string.IsNullOrWhiteSpace(currency))
                {
                    return currency;
                }
            }

            if (moneyBag.TryGetProperty(
                    secondProperty,
                    out JsonElement second)
                && second.ValueKind
                    == JsonValueKind.Object)
            {
                return GetString(second, "currencyCode");
            }

            return null;
        }

        private static bool GetBoolean(
            JsonElement element,
            string propertyName)
        {
            return element.TryGetProperty(
                    propertyName,
                    out JsonElement value)
                && value.ValueKind
                    == JsonValueKind.True;
        }

        private static decimal MoneyRound(decimal amount)
        {
            return decimal.Round(
                amount,
                2,
                MidpointRounding.AwayFromZero);
        }

        private static string BuildShopifyGid(
            string resourceType,
            long numericId)
        {
            return $"gid://shopify/{resourceType}/{numericId}";
        }

        private sealed class ShopifyRefundableOrderState
        {
            public bool Exists { get; set; }
            public string Gid { get; set; } = string.Empty;
            public string Name { get; set; } = string.Empty;
            public bool Refundable { get; set; }
            public string? ShopCurrencyCode { get; set; }
            public string? PresentmentCurrencyCode { get; set; }

            public Dictionary<long, ShopifyRefundableLineState>
                LineItems { get; set; } =
                    new Dictionary<long, ShopifyRefundableLineState>();
        }

        private sealed class ShopifyRefundableLineState
        {
            public string Gid { get; set; } = string.Empty;
            public long NumericId { get; set; }
            public string Title { get; set; } = string.Empty;
            public string? Sku { get; set; }
            public int Quantity { get; set; }
            public int RefundableQuantity { get; set; }
            public bool Restockable { get; set; }
        }

        private static string NormalizeOrderName(
            string? orderNumber)
        {
            string value =
                (orderNumber ?? string.Empty).Trim();

            while (value.StartsWith("#"))
            {
                value = value.Substring(1).TrimStart();
            }

            return value;
        }

        private static string QuoteSearchValue(
            string value)
        {
            string escaped = value
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"");

            return $"\"{escaped}\"";
        }

        private static bool IsPaidFinancialStatus(string? status)
        {
            if (string.IsNullOrWhiteSpace(status))
            {
                return false;
            }

            string normalized = status.Trim().ToUpperInvariant();
            return normalized == "PAID" || normalized == "PARTIALLY_PAID";
        }

        private bool MarkLocalPartSoldFromOrder(int partId, long shopifyOrderId, int quantityPurchased, int userId)
        {
            bool wasAlreadySynced = false;
            const string procName = "[dbo].[Parts_MarkSoldFromShopifyOrder]";

            _data.ExecuteCmd(procName,
                inputParamMapper: delegate (SqlParameterCollection col)
                {
                    col.AddWithValue("@PartId", partId);
                    col.AddWithValue("@ShopifyOrderId", shopifyOrderId);
                    col.AddWithValue("@QuantityPurchased", quantityPurchased <= 0 ? 1 : quantityPurchased);
                    col.AddWithValue("@LastMovedBy", userId);
                },
                singleRecordMapper: delegate (IDataReader reader, short set)
                {
                    int i = 0;
                    i++; // Id
                    i++; // ShopifyOrderId
                    i++; // SoldOnUtc
                    i++; // Quantity
                    i++; // AvailableId
                    wasAlreadySynced = reader.GetSafeBool(i++);
                });

            return wasAlreadySynced;
        }

        private ShopifyOrderSummary MapOrder(JsonElement node)
        {
            string orderGid = GetString(node, "id") ?? string.Empty;

            ShopifyOrderSummary order = new ShopifyOrderSummary
            {
                ShopifyOrderGid = orderGid,
                ShopifyOrderId = ExtractNumericId(orderGid),
                Name = GetString(node, "name") ?? string.Empty,
                OrderNumber = ExtractOrderNumberFromName(GetString(node, "name")),
                CreatedAt = GetDateTime(node, "createdAt"),
                CustomerEmail = GetString(node, "email"),
                DisplayFinancialStatus = GetString(node, "displayFinancialStatus"),
                DisplayFulfillmentStatus = GetString(node, "displayFulfillmentStatus")
            };

            if (node.TryGetProperty("customer", out JsonElement customer)
                && customer.ValueKind != JsonValueKind.Null)
            {
                order.CustomerDisplayName = GetString(customer, "displayName");
                order.CustomerEmail = GetString(customer, "email") ?? order.CustomerEmail;
            }

            if (node.TryGetProperty("currentTotalPriceSet", out JsonElement totalSet)
                && totalSet.TryGetProperty("shopMoney", out JsonElement shopMoney))
            {
                order.TotalPrice = GetDecimal(shopMoney, "amount");
                order.CurrencyCode = GetString(shopMoney, "currencyCode");
            }

            if (node.TryGetProperty("shippingAddress", out JsonElement shippingAddress)
                && shippingAddress.ValueKind != JsonValueKind.Null)
            {
                order.DestinationCountryCode =
                    GetString(shippingAddress, "countryCodeV2");

                order.IsInternational =
                    !string.IsNullOrWhiteSpace(order.DestinationCountryCode)
                    && !string.Equals(
                        order.DestinationCountryCode,
                        "US",
                        StringComparison.OrdinalIgnoreCase);
            }

            if (node.TryGetProperty("fulfillments", out JsonElement fulfillments)
                && fulfillments.ValueKind == JsonValueKind.Array)
            {
                DateTime? latestDeliveredAt = null;

                foreach (JsonElement fulfillment in fulfillments.EnumerateArray())
                {
                    DateTime? deliveredAt =
                        GetDateTimeNullable(fulfillment, "deliveredAt");

                    if (deliveredAt.HasValue
                        && (!latestDeliveredAt.HasValue
                            || deliveredAt.Value > latestDeliveredAt.Value))
                    {
                        latestDeliveredAt = deliveredAt;
                    }
                }

                order.DeliveredAt = latestDeliveredAt;
            }

            if (node.TryGetProperty("lineItems", out JsonElement lineItems)
                && lineItems.TryGetProperty("nodes", out JsonElement lineItemNodes))
            {
                foreach (JsonElement lineItemNode in lineItemNodes.EnumerateArray())
                {
                    order.LineItems.Add(MapLineItem(lineItemNode));
                }
            }

            return order;
        }

        private ShopifyOrderLineItemSummary MapLineItem(JsonElement node)
        {
            string lineItemGid = GetString(node, "id") ?? string.Empty;

            ShopifyOrderLineItemSummary item = new ShopifyOrderLineItemSummary
            {
                ShopifyLineItemGid = lineItemGid,
                ShopifyLineItemId = ExtractNumericId(lineItemGid),
                Title = GetString(node, "title") ?? string.Empty,
                Sku = GetString(node, "sku"),
                Quantity = GetInt32(node, "quantity")
            };

            if (node.TryGetProperty("originalUnitPriceSet", out JsonElement priceSet)
                && priceSet.TryGetProperty("shopMoney", out JsonElement shopMoney))
            {
                item.UnitPrice = GetDecimal(shopMoney, "amount");
                item.CurrencyCode = GetString(shopMoney, "currencyCode");
            }

            if (node.TryGetProperty("variant", out JsonElement variant)
                && variant.ValueKind != JsonValueKind.Null)
            {
                string? variantGid = GetString(variant, "id");
                item.ShopifyVariantGid = variantGid;
                item.ShopifyVariantId = string.IsNullOrWhiteSpace(variantGid) ? null : ExtractNumericId(variantGid);

                item.Sku = GetString(variant, "sku") ?? item.Sku;

                if (variant.TryGetProperty("image", out JsonElement image)
                    && image.ValueKind != JsonValueKind.Null)
                {
                    item.ShopifyImageUrl = GetString(image, "url");
                }

                if (variant.TryGetProperty("product", out JsonElement product)
                    && product.ValueKind != JsonValueKind.Null)
                {
                    string? productGid = GetString(product, "id");
                    item.ShopifyProductGid = productGid;
                    item.ShopifyProductId = string.IsNullOrWhiteSpace(productGid) ? null : ExtractNumericId(productGid);
                }
            }

            return item;
        }

        private ShopifyLocalPartMatch? GetLocalPartMatchByVariantId(long shopifyVariantId)
        {
            ShopifyLocalPartMatch? match = null;
            string? legacyImagePath = null;
            const string procName = "[dbo].[Parts_GetOrderMatchByShopifyVariantId]";

            _data.ExecuteCmd(procName,
                inputParamMapper: delegate (SqlParameterCollection col)
                {
                    col.AddWithValue("@ShopifyVariantId", shopifyVariantId);
                },
                singleRecordMapper: delegate (IDataReader reader, short set)
                {
                    int i = 0;

                    int partId = reader.GetSafeInt32(i++);
                    string partName = reader.GetSafeString(i++);
                    string partNumber = reader.GetSafeString(i++);
                    legacyImagePath = reader.GetSafeString(i++);

                    match = new ShopifyLocalPartMatch
                    {
                        PartId = partId,
                        PartName = partName,
                        PartNumber = partNumber,
                        ImageUrl = null,
                        ImageUrls = new List<string>(),
                        AvailableId = reader.GetSafeInt32(i++),
                        AvailableStatus = reader.GetSafeString(i++),
                        SiteName = reader.GetSafeString(i++),
                        AreaName = reader.GetSafeString(i++),
                        AisleName = reader.GetSafeString(i++),
                        ShelfName = reader.GetSafeString(i++),
                        SectionName = reader.GetSafeString(i++),
                        BoxName = reader.GetSafeString(i++),
                        OtherBox = reader.GetSafeString(i++),
                        ShopifyVariantId = reader.GetSafeInt64(i++),
                        ShopifyOrderId = reader.GetSafeInt64Nullable(i++),
                        SoldOnUtc = reader.GetSafeDateTimeNullable(i++),
                        Quantity = reader.GetSafeInt32(i++),
                        ConditionId = reader.GetSafeInt32Nullable(i++),
                        ConditionName = reader.GetSafeString(i++)
                    };
                });

            if (match != null)
            {
                match.ImageUrls = GetLocalPartImageUrls(
                    match.PartId,
                    legacyImagePath);

                match.ImageUrl = match.ImageUrls.FirstOrDefault();
            }

            return match;
        }

        private List<string> GetLocalPartImageUrls(
            int partId,
            string? legacyImagePath)
        {
            List<(string Url, bool IsPrimary, int SortOrder)> images =
                new();

            const string procName =
                "[dbo].[PartImages_SelectByPartId]";

            try
            {
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

                            index++; // Id
                            index++; // PartId

                            string url =
                                reader.GetSafeString(index++);

                            bool isPrimary =
                                reader.GetSafeBool(index++);

                            int sortOrder =
                                reader.GetSafeInt32(index++);

                            if (!string.IsNullOrWhiteSpace(url))
                            {
                                images.Add(
                                    (
                                        url,
                                        isPrimary,
                                        sortOrder
                                    ));
                            }
                        });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Unable to load PartImages for order item PartId {PartId}. Falling back to Parts.Image.",
                    partId);
            }

            if (!string.IsNullOrWhiteSpace(legacyImagePath))
            {
                images.Add(
                    (
                        legacyImagePath,
                        images.Count == 0,
                        int.MaxValue
                    ));
            }

            return images
                .OrderByDescending(image => image.IsPrimary)
                .ThenBy(image => image.SortOrder)
                .Select(image => BuildPublicImageUrl(image.Url))
                .Where(url => !string.IsNullOrWhiteSpace(url))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private string BuildPublicImageUrl(string? imagePath)
        {
            string value = (imagePath ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            if (Uri.TryCreate(
                    value,
                    UriKind.Absolute,
                    out Uri? absoluteUri)
                &&
                (
                    absoluteUri.Scheme == Uri.UriSchemeHttp
                    ||
                    absoluteUri.Scheme == Uri.UriSchemeHttps
                ))
            {
                return absoluteUri.ToString();
            }

            string baseUrl = ResolvePublicApiBaseUrl();

            return $"{baseUrl.TrimEnd('/')}/{value.TrimStart('/')}";
        }

        private string ResolvePublicApiBaseUrl()
        {
            if (!string.IsNullOrWhiteSpace(
                    _settings.PublicApiBaseUrl))
            {
                return _settings.PublicApiBaseUrl.Trim();
            }

            if (!string.IsNullOrWhiteSpace(
                    _staticFileOptions.ImageBaseUrl)
                &&
                !_staticFileOptions.ImageBaseUrl.Contains(
                    "yourdomain.com",
                    StringComparison.OrdinalIgnoreCase))
            {
                return _staticFileOptions.ImageBaseUrl.Trim();
            }

            if (Uri.TryCreate(
                    _settings.RedirectUri,
                    UriKind.Absolute,
                    out Uri? redirectUri))
            {
                return redirectUri.GetLeftPart(
                    UriPartial.Authority);
            }

            throw new InvalidOperationException(
                "A public API URL is required to display Site_2024 order photos. Configure ShopifySettings:PublicApiBaseUrl.");
        }

        private async Task<JsonDocument> SendGraphQlAsync(string query, object variables)
        {
            string shopDomain = NormalizeShopDomain(_settings.ShopDomain);
            string endpoint = $"https://{shopDomain}/admin/api/{_settings.ApiVersion}/graphql.json";
            string token = await _tokenService.GetAccessTokenAsync();

            var body = new
            {
                query,
                variables
            };

            string json = JsonSerializer.Serialize(body);

            using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, endpoint);
            request.Headers.Add("X-Shopify-Access-Token", token);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");

            using HttpResponseMessage response = await _httpClient.SendAsync(request);
            string responseText = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Shopify orders GraphQL request failed. Status: {StatusCode}. Body: {Body}",
                    response.StatusCode,
                    responseText);

                throw new ApplicationException(
                    $"Shopify orders GraphQL request failed: {(int)response.StatusCode} {response.ReasonPhrase}. Body: {responseText}");
            }

            return JsonDocument.Parse(responseText);
        }

        private static string? BuildOrderQuery(string? view)
        {
            if (string.IsNullOrWhiteSpace(view))
            {
                return null;
            }

            switch (view.Trim().ToLowerInvariant())
            {
                case "awaitingshipment":
                case "awaiting-shipment":
                    return "status:open fulfillment_status:unfulfilled";

                case "fulfilled":
                    return "fulfillment_status:fulfilled";

                case "open":
                    return "status:open";

                case "all":
                default:
                    return null;
            }
        }

        private static void ThrowIfTopLevelErrors(JsonDocument doc)
        {
            if (doc.RootElement.TryGetProperty("errors", out JsonElement topErrors))
            {
                throw new ApplicationException($"Shopify GraphQL error: {topErrors}");
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

        private static int ExtractOrderNumberFromName(string? orderName)
        {
            if (string.IsNullOrWhiteSpace(orderName))
            {
                return 0;
            }

            StringBuilder digits = new StringBuilder();

            foreach (char c in orderName)
            {
                if (char.IsDigit(c))
                {
                    digits.Append(c);
                }
            }

            return int.TryParse(digits.ToString(), out int orderNumber) ? orderNumber : 0;
        }

        private static long ExtractNumericId(string? gid)
        {
            if (string.IsNullOrWhiteSpace(gid))
            {
                return 0;
            }

            string idString = gid.Split('/').Last();
            return long.TryParse(idString, out long id) ? id : 0;
        }

        private static string? GetString(JsonElement element, string propertyName)
        {
            if (!element.TryGetProperty(propertyName, out JsonElement value)
                || value.ValueKind == JsonValueKind.Null
                || value.ValueKind == JsonValueKind.Undefined)
            {
                return null;
            }

            return value.GetString();
        }

        private static int GetInt32(JsonElement element, string propertyName)
        {
            if (!element.TryGetProperty(propertyName, out JsonElement value)
                || value.ValueKind == JsonValueKind.Null
                || value.ValueKind == JsonValueKind.Undefined)
            {
                return 0;
            }

            return value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int result)
                ? result
                : 0;
        }

        private static decimal GetDecimal(JsonElement element, string propertyName)
        {
            string? raw = GetString(element, propertyName);
            return decimal.TryParse(raw, out decimal result) ? result : 0m;
        }

        private static DateTime GetDateTime(JsonElement element, string propertyName)
        {
            string? raw = GetString(element, propertyName);
            return DateTime.TryParse(raw, out DateTime result)
                ? result
                : DateTime.MinValue;
        }

        private static DateTime? GetDateTimeNullable(
            JsonElement element,
            string propertyName)
        {
            string? raw = GetString(element, propertyName);

            return DateTime.TryParse(raw, out DateTime result)
                ? result
                : null;
        }
    }
}
