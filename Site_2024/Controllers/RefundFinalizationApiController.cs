using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Site_2024.Models.Domain.RefundRequests;
using Site_2024.Models.Requests.RefundRequests;
using Site_2024.Web.Api.Interfaces;
using Site_2024.Web.Api.Models;
using Site_2024.Web.Api.Models.Shopify;
using Site_2024.Web.Api.Models.User;
using Site_2024.Web.Api.Responses;
using Site_2024.Web.Api.Services;
using System.Data.SqlClient;
using System.Text.Json;

namespace Site_2024.Web.Api.Controllers
{
    [Route("api/refunds/{refundRequestId:int}/finalization")]
    [ApiController]
    [Authorize(Policy = "AdminAction")]
    public class RefundFinalizationApiController : BaseApiController
    {
        private const decimal MoneyTolerance = 0.01m;

        private readonly IRefundRequestService _refundRequestService;
        private readonly IShopifyOrderService _shopifyOrderService;
        private readonly IRefundFinalizationService _finalizationService;
        private readonly IShopifyRefundService _shopifyRefundService;
        private readonly IShopifyAdminService _shopifyAdminService;
        private readonly IPartService _partService;
        private readonly ISmtpEmailService _emailService;
        private readonly IAuthenticationService<IUserAuthData> _authService;

        public RefundFinalizationApiController(
            IRefundRequestService refundRequestService,
            IShopifyOrderService shopifyOrderService,
            IRefundFinalizationService finalizationService,
            IShopifyRefundService shopifyRefundService,
            IShopifyAdminService shopifyAdminService,
            IPartService partService,
            ISmtpEmailService emailService,
            IAuthenticationService<IUserAuthData> authService,
            ILogger<RefundFinalizationApiController> logger)
            : base(logger)
        {
            _refundRequestService = refundRequestService;
            _shopifyOrderService = shopifyOrderService;
            _finalizationService = finalizationService;
            _shopifyRefundService = shopifyRefundService;
            _shopifyAdminService = shopifyAdminService;
            _partService = partService;
            _emailService = emailService;
            _authService = authService;
        }

        [HttpGet]
        public ActionResult<ItemResponse<RefundFinalization>> Get(
            int refundRequestId)
        {
            try
            {
                RefundFinalization? finalization =
                    _finalizationService.GetByRefundRequestId(
                        refundRequestId);

                if (finalization == null)
                {
                    return StatusCode(
                        StatusCodes.Status404NotFound,
                        new ErrorResponse(
                            "No prepared final refund exists for this return request."));
                }

                return StatusCode(
                    StatusCodes.Status200OK,
                    new ItemResponse<RefundFinalization>
                    {
                        Item = finalization
                    });
            }
            catch (Exception ex)
            {
                Logger.LogError(
                    ex,
                    "Failed to load refund finalization for refund request {RefundRequestId}.",
                    refundRequestId);

                return StatusCode(
                    StatusCodes.Status500InternalServerError,
                    new ErrorResponse(
                        "Unable to load the prepared final refund."));
            }
        }

        [HttpPost("prepare")]
        public async Task<ActionResult<ItemResponse<RefundFinalization>>> Prepare(
            int refundRequestId,
            [FromBody] ShopifyRefundPreviewOptions? model)
        {
            try
            {
                IUserAuthData user = RequireCurrentUser();
                RefundRequest refundRequest =
                    RequireRefundReadyForFinalization(refundRequestId);

                ShopifyRefundPreviewOptions options =
                    model ?? new ShopifyRefundPreviewOptions();

                ShopifyRefundPreviewResult preview =
                    await _shopifyOrderService.GetRefundPreviewAsync(
                        refundRequest,
                        options);

                RefundFinalization finalization =
                    _finalizationService.PrepareFromPreview(
                        preview,
                        user.Id);

                return StatusCode(
                    StatusCodes.Status200OK,
                    new ItemResponse<RefundFinalization>
                    {
                        Item = finalization
                    });
            }
            catch (InvalidOperationException ex)
            {
                return StatusCode(
                    StatusCodes.Status400BadRequest,
                    new ErrorResponse(ex.Message));
            }
            catch (SqlException ex)
            {
                Logger.LogWarning(
                    ex,
                    "SQL rejected refund preparation for refund request {RefundRequestId}.",
                    refundRequestId);

                return StatusCode(
                    StatusCodes.Status400BadRequest,
                    new ErrorResponse(GetFirstSqlMessageLine(ex)));
            }
            catch (Exception ex)
            {
                Logger.LogError(
                    ex,
                    "Failed to prepare refund finalization for refund request {RefundRequestId}.",
                    refundRequestId);

                return StatusCode(
                    StatusCodes.Status500InternalServerError,
                    new ErrorResponse(
                        "Unable to prepare the final refund calculation."));
            }
        }

        [HttpPost("confirm")]
        [Authorize(Policy = "RefundCommit")]
        public async Task<ActionResult<ItemResponse<RefundFinalization>>> Confirm(
            int refundRequestId,
            [FromBody] RefundFinalizationConfirmRequest model)
        {
            IUserAuthData? user = null;

            try
            {
                user = RequireCurrentUser();
                ValidateFinalConfirmation(model);

                RefundRequest refundRequest =
                    RequireRefundReadyForFinalization(refundRequestId);

                RefundFinalization saved =
                    _finalizationService.GetByRefundRequestId(refundRequestId)
                    ?? throw new InvalidOperationException(
                        "Prepare the final refund calculation before confirming it.");

                if (saved.ShopifySucceededAt.HasValue)
                {
                    RefundFinalization completed =
                        await ContinueInventoryCommitAsync(
                            refundRequest,
                            saved,
                            user);

                    return OkFinalization(completed);
                }

                if (!string.IsNullOrWhiteSpace(saved.ShopifyRefundGid))
                {
                    RefundFinalization reconciled =
                        await ReconcileSavedShopifyRefundAsync(
                            refundRequest,
                            saved,
                            user);

                    return OkFinalization(reconciled);
                }

                // Before the first dispatch only, verify that Shopify's current
                // refundable values still match the immutable prepared snapshot.
                // After an ambiguous network failure, the exact same mutation and
                // persisted idempotency key must be retried without changing input.
                if (!saved.ShopifyRequestStartedAt.HasValue)
                {
                    ShopifyRefundPreviewResult freshPreview =
                        await LoadFreshPreparedPreviewAsync(
                            refundRequest,
                            saved);

                    EnsurePreparedCalculationIsStillCurrent(
                        saved,
                        freshPreview);
                }

                RefundFinalization processing =
                    _finalizationService.BeginProcessing(
                        refundRequestId,
                        user.Id);

                ShopifyRefundExecutionResult shopifyResult =
                    await _shopifyRefundService.CreateRefundAsync(
                        processing,
                        () => _finalizationService.MarkShopifyRequestStarted(
                            refundRequestId,
                            user.Id));

                bool financiallySuccessful =
                    IsExpectedSuccessfulRefund(
                        processing,
                        shopifyResult);

                string? reconciliationMessage = financiallySuccessful
                    ? null
                    : BuildShopifyReconciliationMessage(
                        processing,
                        shopifyResult);

                _finalizationService.MarkShopifyResult(
                    refundRequestId,
                    shopifyResult,
                    financiallySuccessful,
                    reconciliationMessage,
                    user.Id);

                if (!financiallySuccessful)
                {
                    return StatusCode(
                        StatusCodes.Status409Conflict,
                        new ErrorResponse(reconciliationMessage!));
                }

                RefundFinalization confirmed =
                    _finalizationService.GetByRefundRequestId(refundRequestId)
                    ?? throw new InvalidOperationException(
                        "Shopify confirmed the refund, but the saved result could not be reloaded.");

                RefundFinalization result =
                    await ContinueInventoryCommitAsync(
                        refundRequest,
                        confirmed,
                        user);

                return OkFinalization(result);
            }
            catch (InvalidOperationException ex)
            {
                bool refundAlreadySucceeded =
                    TryMarkInventoryReconciliationIfRefundSucceeded(
                        refundRequestId,
                        ex.Message,
                        user?.Id);

                if (!refundAlreadySucceeded)
                {
                    TryMarkDispatchFailure(
                        refundRequestId,
                        ex.Message,
                        user?.Id);
                }

                RefundFinalization? state =
                    TryGetFinalization(refundRequestId);

                int statusCode = refundAlreadySucceeded
                    || state?.ShopifyRequestStartedAt.HasValue == true
                    || !string.IsNullOrWhiteSpace(state?.ShopifyRefundGid)
                        ? StatusCodes.Status409Conflict
                        : StatusCodes.Status400BadRequest;

                string message = refundAlreadySucceeded
                    ? $"Shopify confirmed the refund, but inventory reconciliation is required: {ex.Message}"
                    : ex.Message;

                return StatusCode(
                    statusCode,
                    new ErrorResponse(message));
            }
            catch (SqlException ex)
            {
                string sqlMessage = GetFirstSqlMessageLine(ex);
                bool refundAlreadySucceeded =
                    TryMarkInventoryReconciliationIfRefundSucceeded(
                        refundRequestId,
                        sqlMessage,
                        user?.Id);

                if (!refundAlreadySucceeded)
                {
                    TryMarkDispatchFailure(
                        refundRequestId,
                        sqlMessage,
                        user?.Id);
                }

                Logger.LogWarning(
                    ex,
                    "SQL rejected final refund commit for refund request {RefundRequestId}.",
                    refundRequestId);

                string message = refundAlreadySucceeded
                    ? $"Shopify confirmed the refund, but local inventory reconciliation is required: {sqlMessage}"
                    : sqlMessage;

                return StatusCode(
                    StatusCodes.Status409Conflict,
                    new ErrorResponse(message));
            }
            catch (Exception ex)
            {
                Logger.LogError(
                    ex,
                    "Final refund commit failed for refund request {RefundRequestId}.",
                    refundRequestId);

                bool refundAlreadySucceeded =
                    TryMarkInventoryReconciliationIfRefundSucceeded(
                        refundRequestId,
                        ex.Message,
                        user?.Id);

                if (!refundAlreadySucceeded)
                {
                    TryMarkDispatchFailure(
                        refundRequestId,
                        ex.Message,
                        user?.Id);
                }

                return StatusCode(
                    refundAlreadySucceeded
                        ? StatusCodes.Status409Conflict
                        : StatusCodes.Status500InternalServerError,
                    new ErrorResponse(
                        refundAlreadySucceeded
                            ? "Shopify confirmed the refund, but inventory reconciliation is required. The refund will not be repeated."
                            : "The final refund attempt failed. Reload the return before retrying. The same Shopify idempotency key will be reused."));
            }
        }

        [HttpPost("retry-inventory")]
        [Authorize(Policy = "RefundCommit")]
        public async Task<ActionResult<ItemResponse<RefundFinalization>>> RetryInventory(
            int refundRequestId)
        {
            IUserAuthData? user = null;

            try
            {
                user = RequireCurrentUser();

                RefundRequest refundRequest =
                    _refundRequestService.GetById(refundRequestId)
                    ?? throw new InvalidOperationException(
                        "Return request not found.");

                RefundFinalization finalization =
                    _finalizationService.GetByRefundRequestId(refundRequestId)
                    ?? throw new InvalidOperationException(
                        "No final refund exists for this return request.");

                if (string.IsNullOrWhiteSpace(
                        finalization.ShopifyRefundGid))
                {
                    throw new InvalidOperationException(
                        "Inventory cannot be retried because a Shopify refund ID has not been saved.");
                }

                ShopifyRefundExecutionResult status =
                    await _shopifyRefundService.GetRefundStatusAsync(
                        finalization.ShopifyRefundGid);

                if (!IsExpectedSuccessfulRefund(finalization, status))
                {
                    string message = BuildShopifyReconciliationMessage(
                        finalization,
                        status);

                    _finalizationService.MarkShopifyResult(
                        refundRequestId,
                        status,
                        false,
                        message,
                        user.Id);

                    return StatusCode(
                        StatusCodes.Status409Conflict,
                        new ErrorResponse(message));
                }

                _finalizationService.MarkShopifyResult(
                    refundRequestId,
                    status,
                    true,
                    null,
                    user.Id);

                RefundFinalization confirmed =
                    _finalizationService.GetByRefundRequestId(refundRequestId)
                    ?? throw new InvalidOperationException(
                        "The reconciled Shopify refund could not be reloaded.");

                RefundFinalization completed =
                    await ContinueInventoryCommitAsync(
                        refundRequest,
                        confirmed,
                        user);

                return OkFinalization(completed);
            }
            catch (InvalidOperationException ex)
            {
                return StatusCode(
                    StatusCodes.Status409Conflict,
                    new ErrorResponse(ex.Message));
            }
            catch (Exception ex)
            {
                Logger.LogError(
                    ex,
                    "Inventory reconciliation retry failed for refund request {RefundRequestId}.",
                    refundRequestId);

                TryMarkReconciliationRequired(
                    refundRequestId,
                    ex.Message,
                    user?.Id);

                return StatusCode(
                    StatusCodes.Status500InternalServerError,
                    new ErrorResponse(
                        "The Shopify refund was not repeated, but inventory reconciliation failed again."));
            }
        }

        [HttpPost("retry-email")]
        public ActionResult<ItemResponse<RefundFinalization>> RetryCompletionEmail(
            int refundRequestId)
        {
            IUserAuthData? user = null;

            try
            {
                user = RequireCurrentUser();

                RefundRequest refundRequest =
                    _refundRequestService.GetById(refundRequestId)
                    ?? throw new InvalidOperationException(
                        "Return request not found.");

                RefundFinalization finalization =
                    _finalizationService.GetByRefundRequestId(refundRequestId)
                    ?? throw new InvalidOperationException(
                        "No final refund exists for this return request.");

                if (!finalization.ShopifySucceededAt.HasValue
                    || (!string.Equals(
                            finalization.InventoryStatus,
                            "Completed",
                            StringComparison.OrdinalIgnoreCase)
                        && !string.Equals(
                            finalization.InventoryStatus,
                            "NotRequired",
                            StringComparison.OrdinalIgnoreCase)))
                {
                    throw new InvalidOperationException(
                        "The completion email can only be sent after Shopify refund success and inventory completion.");
                }

                SendCompletionEmail(
                    refundRequest,
                    finalization,
                    user.Id);

                RefundFinalization updated =
                    _finalizationService.GetByRefundRequestId(refundRequestId)
                    ?? finalization;

                return OkFinalization(updated);
            }
            catch (InvalidOperationException ex)
            {
                return StatusCode(
                    StatusCodes.Status400BadRequest,
                    new ErrorResponse(ex.Message));
            }
            catch (Exception ex)
            {
                Logger.LogError(
                    ex,
                    "Refund completion email retry failed for refund request {RefundRequestId}.",
                    refundRequestId);

                TryMarkEmailFailure(
                    refundRequestId,
                    ex.Message,
                    user?.Id);

                return StatusCode(
                    StatusCodes.Status500InternalServerError,
                    new ErrorResponse(
                        "The refund remains complete, but the customer completion email could not be sent."));
            }
        }

        private IUserAuthData RequireCurrentUser()
        {
            IUserAuthData user = _authService.GetCurrentUser();

            if (user == null || user.Id <= 0)
            {
                throw new InvalidOperationException(
                    "You must be logged in to manage the final refund.");
            }

            return user;
        }

        private RefundRequest RequireRefundReadyForFinalization(
            int refundRequestId)
        {
            RefundRequest refund =
                _refundRequestService.GetById(refundRequestId)
                ?? throw new InvalidOperationException(
                    "Return request not found.");

            string status =
                refund.Status
                ?? refund.StatusName
                ?? string.Empty;

            if (!string.Equals(
                    status,
                    "Approved",
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "Only an Approved return can be finalized.");
            }

            if (!string.Equals(
                    refund.InspectionStatus,
                    "Completed",
                    StringComparison.OrdinalIgnoreCase)
                || !refund.InspectionCompletedAt.HasValue
                || !refund.ReadyForRefundAt.HasValue)
            {
                throw new InvalidOperationException(
                    "Item receipt and inspection must be completed before the final refund.");
            }

            if (!refund.ShopifyOrderId.HasValue
                || refund.ShopifyOrderId.Value <= 0)
            {
                throw new InvalidOperationException(
                    "The return request is not matched to a Shopify order.");
            }

            List<RefundRequestItem> receivedItems = refund.Items
                .Where(item =>
                    (item.QuantityReceived ?? 0) > 0)
                .ToList();

            if (receivedItems.Count == 0)
            {
                throw new InvalidOperationException(
                    "The completed inspection does not contain a received quantity to refund.");
            }

            foreach (RefundRequestItem item in receivedItems)
            {
                if (!item.ShopifyLineItemId.HasValue
                    || item.ShopifyLineItemId.Value <= 0)
                {
                    throw new InvalidOperationException(
                        "Every received quantity must remain matched to a Shopify order line before refunding.");
                }

                int received = item.QuantityReceived ?? 0;
                int restock = item.RestockQuantity ?? 0;
                int hold = item.HoldQuantity ?? 0;
                int damaged = item.DamagedQuantity ?? 0;

                if (restock < 0
                    || hold < 0
                    || damaged < 0
                    || restock + hold + damaged != received)
                {
                    throw new InvalidOperationException(
                        "The saved inspection buckets no longer equal the received quantity.");
                }

                if (restock > 0 && !item.PartId.HasValue)
                {
                    throw new InvalidOperationException(
                        "A Shopify-only item cannot be automatically restocked without a local Site_2024 part match.");
                }
            }

            return refund;
        }

        private async Task<ShopifyRefundPreviewResult>
            LoadFreshPreparedPreviewAsync(
                RefundRequest refundRequest,
                RefundFinalization prepared)
        {
            ShopifyRefundPreviewOptions options =
                new ShopifyRefundPreviewOptions
                {
                    IncludeOriginalShippingRefund =
                        prepared.OriginalShippingRefundAmount > 0m,
                    AdditionalDeductionAmount =
                        prepared.AdditionalDeductionAmount,
                    AdditionalDeductionReason =
                        prepared.AdditionalDeductionReason
                };

            return await _shopifyOrderService.GetRefundPreviewAsync(
                refundRequest,
                options);
        }

        private static void EnsurePreparedCalculationIsStillCurrent(
            RefundFinalization prepared,
            ShopifyRefundPreviewResult fresh)
        {
            if (!string.Equals(
                    prepared.CurrencyCode,
                    fresh.CurrencyCode,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "The Shopify refund currency changed. Prepare the calculation again.");
            }

            if (Math.Abs(
                    prepared.MerchandiseRefundAmount
                    - fresh.MerchandiseRefundAmount) > MoneyTolerance
                || Math.Abs(
                    prepared.TaxRefundAmount
                    - fresh.TaxRefundAmount) > MoneyTolerance
                || Math.Abs(
                    prepared.OriginalShippingRefundAmount
                    - fresh.OriginalShippingRefundAmount) > MoneyTolerance
                || Math.Abs(
                    prepared.BuyerPaidLabelDeductionAmount
                    - fresh.BuyerPaidLabelDeductionAmount) > MoneyTolerance
                || Math.Abs(
                    prepared.AdditionalDeductionAmount
                    - fresh.AdditionalDeductionAmount) > MoneyTolerance
                || Math.Abs(
                    prepared.FinalRefundAmount
                    - fresh.FinalRefundAmount) > MoneyTolerance
                || prepared.FinalRefundAmount
                    - fresh.ShopifyMaximumRefundableAmount > MoneyTolerance)
            {
                throw new InvalidOperationException(
                    "Shopify's refundable values changed after preparation. Reload and prepare the calculation again before moving money.");
            }

            Dictionary<long, ShopifyRefundPreviewLineItem> freshItems =
                fresh.Items.ToDictionary(
                    item => item.ShopifyLineItemId,
                    item => item);

            foreach (RefundFinalizationItem preparedItem in prepared.Items)
            {
                if (!freshItems.TryGetValue(
                        preparedItem.ShopifyLineItemId,
                        out ShopifyRefundPreviewLineItem? freshItem)
                    || freshItem.QuantityToRefund
                        != preparedItem.RefundQuantity
                    || freshItem.ShopifyRefundableQuantity
                        < preparedItem.RefundQuantity
                    || Math.Abs(
                        freshItem.ShopifySubtotalAmount
                        - preparedItem.MerchandiseRefundAmount) > MoneyTolerance
                    || Math.Abs(
                        freshItem.ShopifyTaxAmount
                        - preparedItem.TaxRefundAmount) > MoneyTolerance)
                {
                    throw new InvalidOperationException(
                        "One or more Shopify line items no longer match the prepared refundable quantity or amount. Prepare the calculation again.");
                }
            }
        }

        private async Task<RefundFinalization>
            ReconcileSavedShopifyRefundAsync(
                RefundRequest refundRequest,
                RefundFinalization finalization,
                IUserAuthData user)
        {
            ShopifyRefundExecutionResult status =
                await _shopifyRefundService.GetRefundStatusAsync(
                    finalization.ShopifyRefundGid!);

            bool success = IsExpectedSuccessfulRefund(
                finalization,
                status);

            string? message = success
                ? null
                : BuildShopifyReconciliationMessage(
                    finalization,
                    status);

            _finalizationService.MarkShopifyResult(
                refundRequest.Id,
                status,
                success,
                message,
                user.Id);

            if (!success)
            {
                throw new InvalidOperationException(message!);
            }

            RefundFinalization confirmed =
                _finalizationService.GetByRefundRequestId(refundRequest.Id)
                ?? throw new InvalidOperationException(
                    "The reconciled Shopify refund could not be reloaded.");

            return await ContinueInventoryCommitAsync(
                refundRequest,
                confirmed,
                user);
        }

        private async Task<RefundFinalization>
            ContinueInventoryCommitAsync(
                RefundRequest refundRequest,
                RefundFinalization finalization,
                IUserAuthData user)
        {
            if (!finalization.ShopifySucceededAt.HasValue
                || !finalization.ActualRefundedAmount.HasValue
                || Math.Abs(
                    finalization.ActualRefundedAmount.Value
                    - finalization.FinalRefundAmount) > MoneyTolerance)
            {
                throw new InvalidOperationException(
                    "Inventory cannot be committed until Shopify confirms the exact prepared refund amount.");
            }

            RefundFinalization localState =
                _finalizationService.ApplyLocalInventory(
                    refundRequest.Id,
                    user.Id);

            List<string> errors = new List<string>();

            foreach (RefundFinalizationItem item in localState.Items
                .Where(item =>
                    item.RestockQuantitySnapshot > 0
                    && !string.Equals(
                        item.ShopifyInventoryStatus,
                        "Completed",
                        StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(
                        item.ShopifyInventoryStatus,
                        "NotRequired",
                        StringComparison.OrdinalIgnoreCase)))
            {
                try
                {
                    if (!item.PartId.HasValue
                        || !item.LocalQuantityBefore.HasValue
                        || !item.LocalQuantityAfter.HasValue)
                    {
                        throw new InvalidOperationException(
                            "The local inventory commit is missing the part or quantity snapshot required for Shopify synchronization.");
                    }

                    Part part = _partService.GetPartById(
                        item.PartId.Value)
                        ?? throw new InvalidOperationException(
                            $"Part {item.PartId.Value} was not found after local inventory commit.");

                    if (part.Quantity != item.LocalQuantityAfter.Value)
                    {
                        throw new InvalidOperationException(
                            $"Part {part.Id} quantity changed after the refund restock snapshot ({item.LocalQuantityAfter.Value} expected, {part.Quantity} current). The saved refund inventory adjustment was not allowed to overwrite a newer local quantity.");
                    }

                    if (!part.ShopifyInventoryItemId.HasValue
                        || part.ShopifyInventoryItemId.Value <= 0)
                    {
                        throw new InvalidOperationException(
                            $"Part {part.Id} does not have a Shopify inventory item ID.");
                    }

                    string inventoryKey =
                        string.IsNullOrWhiteSpace(
                            item.ShopifyInventoryIdempotencyKey)
                            ? $"site-2024-refund-inventory-{localState.Id}-{item.Id}"
                            : item.ShopifyInventoryIdempotencyKey;

                    string referenceUri =
                        $"gid://site-2024/RefundFinalization/{localState.Id}/Item/{item.Id}";

                    ShopifyInventoryQuantityCommitResult inventoryResult =
                        await _shopifyAdminService.SetInventoryQuantityForRefundAsync(
                            part.ShopifyInventoryItemId.Value,
                            item.LocalQuantityBefore.Value,
                            item.LocalQuantityAfter.Value,
                            inventoryKey,
                            referenceUri);

                    _finalizationService.MarkInventoryItemResult(
                        refundRequest.Id,
                        item.Id,
                        true,
                        null,
                        JsonSerializer.Serialize(inventoryResult),
                        user.Id);
                }
                catch (Exception ex)
                {
                    string itemName =
                        item.PartName
                        ?? $"Item {item.Id}";

                    errors.Add($"{itemName}: {ex.Message}");

                    _finalizationService.MarkInventoryItemResult(
                        refundRequest.Id,
                        item.Id,
                        false,
                        ex.Message,
                        null,
                        user.Id);
                }
            }

            if (errors.Count > 0)
            {
                string errorMessage = string.Join(" | ", errors);

                _finalizationService.MarkReconciliationRequired(
                    refundRequest.Id,
                    errorMessage,
                    user.Id);

                throw new InvalidOperationException(
                    "Shopify received the refund exactly once, but one or more inventory quantities could not be synchronized. Use Retry Inventory Only after reviewing the error details.");
            }

            _finalizationService.CompleteInventory(
                refundRequest.Id,
                user.Id);

            RefundFinalization completed =
                _finalizationService.GetByRefundRequestId(refundRequest.Id)
                ?? throw new InvalidOperationException(
                    "The final refund completed but could not be reloaded.");

            if (!string.Equals(
                    completed.CompletionEmailStatus,
                    "Sent",
                    StringComparison.OrdinalIgnoreCase))
            {
                SendCompletionEmail(
                    refundRequest,
                    completed,
                    user.Id);

                completed =
                    _finalizationService.GetByRefundRequestId(refundRequest.Id)
                    ?? completed;
            }

            return completed;
        }

        private void SendCompletionEmail(
            RefundRequest refundRequest,
            RefundFinalization finalization,
            int userId)
        {
            try
            {
                _emailService.SendReturnCompletionEmail(
                    refundRequest,
                    finalization);

                _finalizationService.MarkEmailResult(
                    refundRequest.Id,
                    true,
                    null,
                    userId);
            }
            catch (Exception ex)
            {
                Logger.LogError(
                    ex,
                    "Refund completion email failed for refund request {RefundRequestId}.",
                    refundRequest.Id);

                _finalizationService.MarkEmailResult(
                    refundRequest.Id,
                    false,
                    ex.Message,
                    userId);
            }
        }

        private static bool IsExpectedSuccessfulRefund(
            RefundFinalization finalization,
            ShopifyRefundExecutionResult result)
        {
            return result.IsFinanciallySuccessful
                && string.Equals(
                    result.CurrencyCode,
                    finalization.CurrencyCode,
                    StringComparison.OrdinalIgnoreCase)
                && Math.Abs(
                    result.ActualRefundedAmount
                    - finalization.FinalRefundAmount) <= MoneyTolerance;
        }

        private static string BuildShopifyReconciliationMessage(
            RefundFinalization finalization,
            ShopifyRefundExecutionResult result)
        {
            string statuses = result.Transactions.Count == 0
                ? "no payment transaction returned"
                : string.Join(
                    ", ",
                    result.Transactions.Select(transaction =>
                        transaction.Status ?? "Unknown"));

            return
                $"Shopify created or returned refund {result.ShopifyRefundGid}, but the payment transaction is not confirmed as SUCCESS or the amount/currency differs from the prepared {finalization.FinalRefundAmount:0.00} {finalization.CurrencyCode} refund. Transaction status: {statuses}. Do not issue another refund; reconcile this Shopify refund before inventory is committed.";
        }

        private static void ValidateFinalConfirmation(
            RefundFinalizationConfirmRequest model)
        {
            if (model == null
                || !model.ConfirmMoneyMovement
                || !string.Equals(
                    model.ConfirmationText?.Trim(),
                    "REFUND",
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Final confirmation is required. Check the money-movement confirmation and type REFUND exactly.");
            }
        }

        private ActionResult<ItemResponse<RefundFinalization>>
            OkFinalization(RefundFinalization finalization)
        {
            return StatusCode(
                StatusCodes.Status200OK,
                new ItemResponse<RefundFinalization>
                {
                    Item = finalization
                });
        }

        private RefundFinalization? TryGetFinalization(
            int refundRequestId)
        {
            try
            {
                return _finalizationService.GetByRefundRequestId(
                    refundRequestId);
            }
            catch
            {
                return null;
            }
        }

        private void TryMarkDispatchFailure(
            int refundRequestId,
            string errorMessage,
            int? userId)
        {
            try
            {
                RefundFinalization? state =
                    _finalizationService.GetByRefundRequestId(
                        refundRequestId);

                if (state == null
                    || state.ShopifySucceededAt.HasValue
                    || !string.IsNullOrWhiteSpace(
                        state.ShopifyRefundGid))
                {
                    return;
                }

                // Do not change a purely Prepared record when validation failed
                // before the money-moving attempt entered Processing.
                if (!string.Equals(
                        state.Status,
                        "Processing",
                        StringComparison.OrdinalIgnoreCase)
                    && !state.ShopifyRequestStartedAt.HasValue)
                {
                    return;
                }

                _finalizationService.MarkFailed(
                    refundRequestId,
                    errorMessage,
                    userId);
            }
            catch (Exception markException)
            {
                Logger.LogError(
                    markException,
                    "Unable to persist final refund failure state for refund request {RefundRequestId}.",
                    refundRequestId);
            }
        }

        private bool TryMarkInventoryReconciliationIfRefundSucceeded(
            int refundRequestId,
            string errorMessage,
            int? userId)
        {
            RefundFinalization? state =
                TryGetFinalization(refundRequestId);

            if (state?.ShopifySucceededAt.HasValue != true)
            {
                return false;
            }

            TryMarkReconciliationRequired(
                refundRequestId,
                errorMessage,
                userId);

            return true;
        }

        private void TryMarkReconciliationRequired(
            int refundRequestId,
            string errorMessage,
            int? userId)
        {
            try
            {
                _finalizationService.MarkReconciliationRequired(
                    refundRequestId,
                    errorMessage,
                    userId);
            }
            catch (Exception markException)
            {
                Logger.LogError(
                    markException,
                    "Unable to persist inventory reconciliation state for refund request {RefundRequestId}.",
                    refundRequestId);
            }
        }

        private void TryMarkEmailFailure(
            int refundRequestId,
            string errorMessage,
            int? userId)
        {
            try
            {
                _finalizationService.MarkEmailResult(
                    refundRequestId,
                    false,
                    errorMessage,
                    userId);
            }
            catch (Exception markException)
            {
                Logger.LogError(
                    markException,
                    "Unable to persist refund completion email failure for refund request {RefundRequestId}.",
                    refundRequestId);
            }
        }

        private static string GetFirstSqlMessageLine(
            SqlException exception)
        {
            string? message = exception.Message
                .Split(
                    new[] { '\r', '\n' },
                    StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault();

            return string.IsNullOrWhiteSpace(message)
                ? "The final refund operation was rejected by the database."
                : message.Trim();
        }
    }
}
