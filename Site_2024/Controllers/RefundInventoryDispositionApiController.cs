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
    [Route("api/refunds/{refundRequestId:int}/inventory-dispositions")]
    [ApiController]
    [Authorize(Policy = "AdminAction")]
    public class RefundInventoryDispositionApiController : BaseApiController
    {
        private static readonly HashSet<string> SupportedActions =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "ReleaseToInventory",
                "MoveHoldToDamaged",
                "WriteOffDamaged",
                "RetainForParts",
                "DisposeDamaged"
            };

        private readonly IRefundInventoryDispositionService _dispositionService;
        private readonly IShopifyAdminService _shopifyAdminService;
        private readonly IPartService _partService;
        private readonly IAuthenticationService<IUserAuthData> _authService;

        public RefundInventoryDispositionApiController(
            IRefundInventoryDispositionService dispositionService,
            IShopifyAdminService shopifyAdminService,
            IPartService partService,
            IAuthenticationService<IUserAuthData> authService,
            ILogger<RefundInventoryDispositionApiController> logger)
            : base(logger)
        {
            _dispositionService = dispositionService;
            _shopifyAdminService = shopifyAdminService;
            _partService = partService;
            _authService = authService;
        }

        [HttpGet]
        public ActionResult<ItemResponse<RefundInventoryDisposition>> Get(
            int refundRequestId)
        {
            try
            {
                IUserAuthData user = RequireCurrentUser();

                _dispositionService.InitializeByRefundRequestId(
                    refundRequestId,
                    user.Id);

                RefundInventoryDisposition disposition =
                    RequireDisposition(refundRequestId);

                return StatusCode(
                    StatusCodes.Status200OK,
                    new ItemResponse<RefundInventoryDisposition>
                    {
                        Item = disposition
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
                return StatusCode(
                    StatusCodes.Status400BadRequest,
                    new ErrorResponse(GetFirstSqlMessageLine(ex)));
            }
            catch (Exception ex)
            {
                Logger.LogError(
                    ex,
                    "Failed to load held/damaged inventory for refund request {RefundRequestId}.",
                    refundRequestId);

                return StatusCode(
                    StatusCodes.Status500InternalServerError,
                    new ErrorResponse(
                        "Unable to load the held and damaged inventory resolution state."));
            }
        }

        [HttpPost("items/{dispositionItemId:int}/actions")]
        [Authorize(Policy = "InventoryDispositionCommit")]
        public async Task<ActionResult<ItemResponse<RefundInventoryDisposition>>> ExecuteAction(
            int refundRequestId,
            int dispositionItemId,
            [FromBody] RefundInventoryDispositionActionRequest model)
        {
            try
            {
                IUserAuthData user = RequireCurrentUser();
                ValidateActionRequest(model);

                _dispositionService.InitializeByRefundRequestId(
                    refundRequestId,
                    user.Id);

                RefundInventoryDisposition current =
                    RequireDisposition(refundRequestId);

                if (!current.Items.Any(item => item.Id == dispositionItemId))
                {
                    return StatusCode(
                        StatusCodes.Status404NotFound,
                        new ErrorResponse(
                            "The held/damaged inventory item does not belong to this return request."));
                }

                RefundInventoryDispositionAction action =
                    _dispositionService.PrepareAction(
                        dispositionItemId,
                        model.ActionType,
                        model.Quantity,
                        model.Reason,
                        model.IdempotencyKey,
                        user.Id);

                if (action.RefundRequestId != refundRequestId)
                {
                    throw new InvalidOperationException(
                        "The prepared resolution action does not belong to this return request.");
                }

                await ResumeActionAsync(action, user.Id);

                return OkDisposition(refundRequestId);
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
                    "SQL rejected held/damaged inventory action for refund request {RefundRequestId}.",
                    refundRequestId);

                return StatusCode(
                    StatusCodes.Status400BadRequest,
                    new ErrorResponse(GetFirstSqlMessageLine(ex)));
            }
            catch (Exception ex)
            {
                Logger.LogError(
                    ex,
                    "Held/damaged inventory action failed for refund request {RefundRequestId}.",
                    refundRequestId);

                return StatusCode(
                    StatusCodes.Status500InternalServerError,
                    new ErrorResponse(
                        "Unable to complete the held/damaged inventory action. Reload the return before retrying."));
            }
        }

        [HttpPost("actions/{actionId:long}/retry")]
        [Authorize(Policy = "InventoryDispositionCommit")]
        public async Task<ActionResult<ItemResponse<RefundInventoryDisposition>>> RetryAction(
            int refundRequestId,
            long actionId)
        {
            try
            {
                IUserAuthData user = RequireCurrentUser();

                RefundInventoryDispositionAction action =
                    _dispositionService.GetActionById(actionId)
                    ?? throw new InvalidOperationException(
                        "The held/damaged resolution action was not found.");

                if (action.RefundRequestId != refundRequestId)
                {
                    return StatusCode(
                        StatusCodes.Status404NotFound,
                        new ErrorResponse(
                            "The held/damaged resolution action does not belong to this return request."));
                }

                await ResumeActionAsync(action, user.Id);

                return OkDisposition(refundRequestId);
            }
            catch (InvalidOperationException ex)
            {
                return StatusCode(
                    StatusCodes.Status400BadRequest,
                    new ErrorResponse(ex.Message));
            }
            catch (SqlException ex)
            {
                return StatusCode(
                    StatusCodes.Status400BadRequest,
                    new ErrorResponse(GetFirstSqlMessageLine(ex)));
            }
            catch (Exception ex)
            {
                Logger.LogError(
                    ex,
                    "Held/damaged inventory retry failed for action {ActionId}.",
                    actionId);

                return StatusCode(
                    StatusCodes.Status500InternalServerError,
                    new ErrorResponse(
                        "Unable to retry the held/damaged inventory action."));
            }
        }

        private async Task<RefundInventoryDispositionAction> ResumeActionAsync(
            RefundInventoryDispositionAction action,
            int userId)
        {
            if (!string.Equals(
                    action.LocalInventoryStatus,
                    "Completed",
                    StringComparison.OrdinalIgnoreCase)
                && !string.Equals(
                    action.Status,
                    "Completed",
                    StringComparison.OrdinalIgnoreCase))
            {
                action = _dispositionService.ApplyLocal(
                    action.Id,
                    userId);
            }

            if (!string.Equals(
                    action.ActionType,
                    "ReleaseToInventory",
                    StringComparison.OrdinalIgnoreCase)
                || string.Equals(
                    action.ShopifyInventoryStatus,
                    "Completed",
                    StringComparison.OrdinalIgnoreCase))
            {
                return action;
            }

            return await SynchronizeReleasedInventoryAsync(
                action,
                userId);
        }

        private async Task<RefundInventoryDispositionAction>
            SynchronizeReleasedInventoryAsync(
                RefundInventoryDispositionAction action,
                int userId)
        {
            try
            {
                if (!string.Equals(
                        action.LocalInventoryStatus,
                        "Completed",
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        "Local inventory must be committed before Shopify can be synchronized.");
                }

                if (!action.PartId.HasValue
                    || !action.LocalQuantityBefore.HasValue
                    || !action.LocalQuantityAfter.HasValue)
                {
                    throw new InvalidOperationException(
                        "The held inventory release is missing its saved local quantity snapshot.");
                }

                Part part = _partService.GetPartById(action.PartId.Value)
                    ?? throw new InvalidOperationException(
                        $"Part {action.PartId.Value} was not found after local inventory release.");

                if (part.Quantity != action.LocalQuantityAfter.Value)
                {
                    throw new InvalidOperationException(
                        $"Part {part.Id} quantity changed after the held inventory release snapshot ({action.LocalQuantityAfter.Value} expected, {part.Quantity} current). The action was not allowed to overwrite a newer local quantity.");
                }

                if (!part.ShopifyInventoryItemId.HasValue
                    || part.ShopifyInventoryItemId.Value <= 0)
                {
                    throw new InvalidOperationException(
                        $"Part {part.Id} does not have a Shopify inventory item ID.");
                }

                string inventoryKey =
                    string.IsNullOrWhiteSpace(
                        action.ShopifyInventoryIdempotencyKey)
                        ? $"site-2024-return-disposition-{action.DispositionItemId}-{action.Id}"
                        : action.ShopifyInventoryIdempotencyKey;

                string referenceUri =
                    $"gid://site-2024/RefundInventoryDispositionAction/{action.Id}";

                ShopifyInventoryQuantityCommitResult result =
                    await _shopifyAdminService.SetInventoryQuantityForRefundAsync(
                        part.ShopifyInventoryItemId.Value,
                        action.LocalQuantityBefore.Value,
                        action.LocalQuantityAfter.Value,
                        inventoryKey,
                        referenceUri);

                return _dispositionService.MarkShopifyResult(
                    action.Id,
                    true,
                    null,
                    JsonSerializer.Serialize(result),
                    userId);
            }
            catch (Exception ex)
            {
                Logger.LogError(
                    ex,
                    "Shopify inventory synchronization failed for held inventory release action {ActionId}.",
                    action.Id);

                return _dispositionService.MarkShopifyResult(
                    action.Id,
                    false,
                    ex.Message,
                    null,
                    userId);
            }
        }

        private ActionResult<ItemResponse<RefundInventoryDisposition>>
            OkDisposition(int refundRequestId)
        {
            RefundInventoryDisposition disposition =
                RequireDisposition(refundRequestId);

            return StatusCode(
                StatusCodes.Status200OK,
                new ItemResponse<RefundInventoryDisposition>
                {
                    Item = disposition
                });
        }

        private RefundInventoryDisposition RequireDisposition(
            int refundRequestId)
        {
            return _dispositionService.GetByRefundRequestId(refundRequestId)
                ?? throw new InvalidOperationException(
                    "The completed refund does not have a held/damaged inventory state.");
        }

        private IUserAuthData RequireCurrentUser()
        {
            IUserAuthData user = _authService.GetCurrentUser();

            if (user == null || user.Id <= 0)
            {
                throw new InvalidOperationException(
                    "You must be logged in to manage held and damaged inventory.");
            }

            return user;
        }

        private static void ValidateActionRequest(
            RefundInventoryDispositionActionRequest model)
        {
            if (model == null)
            {
                throw new InvalidOperationException(
                    "A held/damaged resolution action is required.");
            }

            string actionType = model.ActionType?.Trim() ?? string.Empty;

            if (!SupportedActions.Contains(actionType))
            {
                throw new InvalidOperationException(
                    "The selected held/damaged resolution action is not supported.");
            }

            if (model.Quantity <= 0)
            {
                throw new InvalidOperationException(
                    "Resolution quantity must be greater than zero.");
            }

            if (string.IsNullOrWhiteSpace(model.Reason))
            {
                throw new InvalidOperationException(
                    "A documented resolution reason is required.");
            }

            if (string.IsNullOrWhiteSpace(model.IdempotencyKey))
            {
                throw new InvalidOperationException(
                    "A resolution idempotency key is required.");
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
                ? "The held/damaged inventory operation was rejected by the database."
                : message.Trim();
        }
    }
}
