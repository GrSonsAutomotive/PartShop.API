using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Site_2024.Models.Domain.RefundRequests;
using Site_2024.Models.Requests.RefundRequests;
using Site_2024.Web.Api.Constructors;
using Site_2024.Web.Api.Interfaces;
using Site_2024.Web.Api.Models;
using Site_2024.Web.Api.Models.User;
using Site_2024.Web.Api.Responses;
using Site_2024.Web.Api.Services;

namespace Site_2024.Web.Api.Controllers
{
    [Route("api/refunds")]
    [ApiController]
    public class RefundRequestsApiController : BaseApiController
    {
        private readonly IRefundRequestService _service;
        private readonly IShopifyOrderService _shopifyOrderService;
        private readonly IAuthenticationService<IUserAuthData> _authService;
        private readonly IWebHostEnvironment _webHostEnvironment;
        private readonly IConfiguration _configuration;
        private readonly ISmtpEmailService _emailService;
        private readonly IEmailDeliveryLogService _emailDeliveryLogService;

        public RefundRequestsApiController(
            IRefundRequestService service,
            IShopifyOrderService shopifyOrderService,
            IAuthenticationService<IUserAuthData> authService,
            IWebHostEnvironment webHostEnvironment,
            IConfiguration configuration,
            ISmtpEmailService emailService,
            IEmailDeliveryLogService emailDeliveryLogService,
            ILogger<RefundRequestsApiController> logger)
            : base(logger)
        {
            _service = service;
            _shopifyOrderService = shopifyOrderService;
            _authService = authService;
            _webHostEnvironment = webHostEnvironment;
            _configuration = configuration;
            _emailService = emailService;
            _emailDeliveryLogService = emailDeliveryLogService;
        }

        [HttpGet("reasons")]
        [AllowAnonymous]
        public ActionResult<ItemResponse<List<ReturnReason>>> GetReasons()
        {
            int code = 200;
            BaseResponse response = null;

            try
            {
                List<ReturnReason> list = _service.GetReasons();
                response = new ItemResponse<List<ReturnReason>> { Item = list };
            }
            catch (Exception ex)
            {
                code = 500;
                response = new ErrorResponse(ex.Message);
                Logger.LogError(ex.ToString());
            }

            return StatusCode(code, response);
        }

        [HttpGet("statuses")]
        [Authorize(Policy = "AdminAction")]
        public ActionResult<ItemResponse<List<ReturnStatus>>> GetStatuses()
        {
            int code = 200;
            BaseResponse response = null;

            try
            {
                List<ReturnStatus> list = _service.GetStatuses();
                response = new ItemResponse<List<ReturnStatus>> { Item = list };
            }
            catch (Exception ex)
            {
                code = 500;
                response = new ErrorResponse(ex.Message);
                Logger.LogError(ex.ToString());
            }

            return StatusCode(code, response);
        }

        [HttpPost]
        [Authorize(Policy = "AdminAction")]
        public ActionResult<ItemResponse<int>> Create(RefundRequestAddRequest model)
        {
            int code = 201;
            BaseResponse response = null;

            try
            {
                var user = _authService.GetCurrentUser();
                int id = _service.Add(model, user.Id);

                response = new ItemResponse<int> { Item = id };
            }
            catch (InvalidOperationException ex)
            {
                code = 400;
                response = new ErrorResponse(ex.Message);
            }
            catch (Exception ex)
            {
                code = 500;
                response = new ErrorResponse(
                    "Unable to create the refund request.");
                Logger.LogError(ex.ToString());
            }

            return StatusCode(code, response);
        }

        [HttpPost("customer-submit")]
        [AllowAnonymous]
        [Consumes("multipart/form-data")]
        public ActionResult<ItemResponse<int>> CustomerSubmit(
            [FromForm] RefundRequestCustomerSubmitRequest model)
        {
            int code = 201;
            BaseResponse response = null;

            try
            {
                if (model == null)
                {
                    return BadRequest(
                        new ErrorResponse(
                            "Return request payload is required."));
                }

                if (model.ClientSubmissionId == null
                    || model.ClientSubmissionId == Guid.Empty)
                {
                    return BadRequest(
                        new ErrorResponse(
                            "A valid return submission id is required."));
                }

                List<ReturnReason> reasons = _service.GetReasons();
                ReturnReason selectedReason = reasons.FirstOrDefault(
                    r => r.Id == model.ReturnReasonId);

                if (selectedReason == null)
                {
                    return BadRequest(
                        new ErrorResponse(
                            "Please select a valid return reason."));
                }

                if (selectedReason.RequiresNotes
                    && string.IsNullOrWhiteSpace(model.Notes))
                {
                    return BadRequest(
                        new ErrorResponse(
                            "This return reason requires a written description."));
                }

                if (selectedReason.RequiresPhotos
                    && (model.Photos == null
                        || model.Photos.Count == 0))
                {
                    return BadRequest(
                        new ErrorResponse(
                            "This return reason requires at least one proof photo."));
                }

                RefundRequestAddRequest addRequest =
                    new RefundRequestAddRequest
                    {
                        ClientSubmissionId = model.ClientSubmissionId,
                        PartId = null,
                        ShopifyOrderId = null,
                        OrderNumber = model.OrderNumber,
                        CustomerEmail = model.CustomerEmail,
                        RequestedPartName = model.RequestedPartName,
                        RequestedQuantity = model.RequestedQuantity,
                        ReturnReasonId = model.ReturnReasonId,
                        Reason = selectedReason.Name
                            ?? "Customer Return Request",
                        Notes = model.Notes,
                        Items = new List<RefundRequestItemAddRequest>()
                    };

                RefundRequestCreateResult createResult =
                    _service.AddWithResult(addRequest, null);

                if (createResult.WasCreated
                    && model.Photos != null
                    && model.Photos.Count > 0)
                {
                    SaveCustomerPhotos(
                        createResult.Id,
                        model.Photos);
                }

                RefundRequest refundRequest =
                    _service.GetById(createResult.Id)
                    ?? throw new InvalidOperationException(
                        "The saved return request could not be reloaded.");

                TrySendSubmissionEmails(refundRequest);

                response = new ItemResponse<int>
                {
                    Item = createResult.Id
                };
            }
            catch (InvalidOperationException ex)
            {
                code = 400;
                response = new ErrorResponse(ex.Message);
            }
            catch (Exception ex)
            {
                code = 500;
                response = new ErrorResponse(
                    "Unable to submit the return request.");
                Logger.LogError(ex.ToString());
            }

            return StatusCode(code, response);
        }

        [HttpGet("{id:int}")]
        [Authorize(Policy = "AdminAction")]
        public ActionResult<ItemResponse<RefundRequest>> GetById(int id)
        {
            int code = 200;
            BaseResponse response = null;

            try
            {
                RefundRequest refundRequest = _service.GetById(id);

                if (refundRequest == null)
                {
                    code = 404;
                    response = new ErrorResponse("Refund request not found.");
                }
                else
                {
                    response = new ItemResponse<RefundRequest> { Item = refundRequest };
                }
            }
            catch (Exception ex)
            {
                code = 500;
                response = new ErrorResponse(ex.Message);
                Logger.LogError(ex.ToString());
            }

            return StatusCode(code, response);
        }

        [HttpGet("paginate")]
        [Authorize(Policy = "AdminAction")]
        public ActionResult<ItemResponse<Paged<RefundRequest>>> GetPaginated(
            int pageIndex,
            int pageSize,
            [FromQuery] RefundRequestSearchRequest model)
        {
            int code = 200;
            BaseResponse response = null;

            try
            {
                Paged<RefundRequest> paged = _service.GetPaginated(pageIndex, pageSize, model);

                if (paged == null)
                {
                    code = 404;
                    response = new ErrorResponse("No refund requests found.");
                }
                else
                {
                    response = new ItemResponse<Paged<RefundRequest>> { Item = paged };
                }
            }
            catch (Exception ex)
            {
                code = 500;
                response = new ErrorResponse(ex.Message);
                Logger.LogError(ex.ToString());
            }

            return StatusCode(code, response);
        }


        [HttpGet("{id:int}/shopify-order")]
        [Authorize(Policy = "AdminAction")]
        public async Task<
            ActionResult<
                ItemResponse<ShopifyReturnOrderLookupResult>>>
            GetShopifyOrderForRefund(int id)
        {
            int code = 200;
            BaseResponse response = null;

            try
            {
                RefundRequest refundRequest =
                    _service.GetById(id);

                if (refundRequest == null)
                {
                    code = 404;
                    response =
                        new ErrorResponse(
                            "Refund request not found.");
                }
                else if (string.IsNullOrWhiteSpace(
                    refundRequest.OrderNumber))
                {
                    code = 400;
                    response =
                        new ErrorResponse(
                            "This request does not have an order number.");
                }
                else
                {
                    ShopifyReturnOrderLookupResult? result =
                        await _shopifyOrderService
                            .GetOrderForReturnAsync(
                                refundRequest.OrderNumber,
                                refundRequest.CustomerEmail);

                    if (result == null)
                    {
                        code = 404;
                        response =
                            new ErrorResponse(
                                "No Shopify order matched the saved order number.");
                    }
                    else
                    {
                        response =
                            new ItemResponse<
                                ShopifyReturnOrderLookupResult>
                            {
                                Item = result
                            };
                    }
                }
            }
            catch (InvalidOperationException ex)
            {
                code = 400;
                response =
                    new ErrorResponse(ex.Message);
            }
            catch (Exception ex)
            {
                code = 500;
                response =
                    new ErrorResponse(
                        "Unable to load the Shopify order.");

                Logger.LogError(ex.ToString());
            }

            return StatusCode(code, response);
        }

        [HttpPost("{id:int}/match-shopify-items")]
        [Authorize(Policy = "AdminAction")]
        public async Task<
            ActionResult<ItemResponse<RefundRequest>>>
            MatchShopifyItems(
                int id,
                RefundRequestMatchShopifyItemsRequest model)
        {
            int code = 200;
            BaseResponse response = null;

            try
            {
                RefundRequest refundRequest =
                    _service.GetById(id);

                if (refundRequest == null)
                {
                    code = 404;
                    return StatusCode(
                        code,
                        new ErrorResponse(
                            "Refund request not found."));
                }

                string status =
                    refundRequest.Status
                    ?? refundRequest.StatusName
                    ?? string.Empty;

                if (!string.Equals(
                        status,
                        "Requested",
                        StringComparison.OrdinalIgnoreCase))
                {
                    code = 400;
                    return StatusCode(
                        code,
                        new ErrorResponse(
                            "Order items can only be matched while the request is in Requested status."));
                }

                if (string.IsNullOrWhiteSpace(
                    refundRequest.OrderNumber))
                {
                    code = 400;
                    return StatusCode(
                        code,
                        new ErrorResponse(
                            "This request does not have an order number."));
                }

                if (model == null
                    ||
                    model.Items == null
                    ||
                    model.Items.Count == 0)
                {
                    code = 400;
                    return StatusCode(
                        code,
                        new ErrorResponse(
                            "Select at least one Shopify order item."));
                }

                ShopifyReturnOrderLookupResult? lookup =
                    await _shopifyOrderService
                        .GetOrderForReturnAsync(
                            refundRequest.OrderNumber,
                            refundRequest.CustomerEmail);

                if (lookup == null)
                {
                    code = 404;
                    return StatusCode(
                        code,
                        new ErrorResponse(
                            "The Shopify order could not be found."));
                }

                List<RefundRequestShopifyItemSelectionRequest>
                    distinctSelections =
                        model.Items
                            .GroupBy(
                                item =>
                                    item.ShopifyLineItemId)
                            .Select(group => group.First())
                            .ToList();

                foreach (
                    RefundRequestShopifyItemSelectionRequest selection
                    in distinctSelections)
                {
                    if (!long.TryParse(
                            selection.ShopifyLineItemId,
                            out long lineItemId)
                        ||
                        lineItemId <= 0)
                    {
                        throw new InvalidOperationException(
                            "A selected Shopify line item is invalid.");
                    }

                    ShopifyOrderLineItemSummary? lineItem =
                        lookup.Order.LineItems
                            .FirstOrDefault(
                                item =>
                                    item.ShopifyLineItemId ==
                                    lineItemId);

                    if (lineItem == null)
                    {
                        throw new InvalidOperationException(
                            $"Shopify line item {lineItemId} is not part of this order.");
                    }

                    if (selection.Quantity <= 0
                        ||
                        selection.Quantity >
                            lineItem.Quantity)
                    {
                        throw new InvalidOperationException(
                            $"Return quantity for {lineItem.Title} must be between 1 and {lineItem.Quantity}.");
                    }
                }

                _service.ReplaceMatchedShopifyItems(
                    id,
                    lookup.Order,
                    distinctSelections);

                RefundRequest refreshed =
                    _service.GetById(id);

                response =
                    new ItemResponse<RefundRequest>
                    {
                        Item = refreshed
                    };
            }
            catch (InvalidOperationException ex)
            {
                code = 400;
                response =
                    new ErrorResponse(ex.Message);
            }
            catch (Exception ex)
            {
                code = 500;
                response =
                    new ErrorResponse(
                        "Unable to match Shopify order items.");

                Logger.LogError(ex.ToString());
            }

            return StatusCode(code, response);
        }

        [HttpGet("{id:int}/eligibility")]
        [Authorize(Policy = "AdminAction")]
        public async Task<
            ActionResult<ItemResponse<ReturnEligibilityEvaluation>>>
            GetEligibility(int id)
        {
            int code = 200;
            BaseResponse response = null;

            try
            {
                RefundRequest refundRequest =
                    _service.GetById(id);

                if (refundRequest == null)
                {
                    return StatusCode(
                        404,
                        new ErrorResponse(
                            "Refund request not found."));
                }

                ShopifyReturnOrderLookupResult? lookup =
                    await LoadReturnOrderAsync(refundRequest);

                ReturnEligibilityEvaluation eligibility =
                    BuildEligibility(refundRequest, lookup);

                response =
                    new ItemResponse<ReturnEligibilityEvaluation>
                    {
                        Item = eligibility
                    };
            }
            catch (InvalidOperationException ex)
            {
                code = 400;
                response = new ErrorResponse(ex.Message);
            }
            catch (Exception ex)
            {
                code = 500;
                response =
                    new ErrorResponse(
                        "Unable to evaluate return eligibility.");

                Logger.LogError(ex.ToString());
            }

            return StatusCode(code, response);
        }

        [HttpPost("{id:int}/decision")]
        [Authorize(Policy = "AdminAction")]
        public async Task<
            ActionResult<ItemResponse<RefundRequest>>>
            ApplyDecision(
                int id,
                RefundRequestDecisionRequest model)
        {
            int code = 200;
            BaseResponse response = null;

            try
            {
                RefundRequest refundRequest =
                    _service.GetById(id);

                if (refundRequest == null)
                {
                    return StatusCode(
                        404,
                        new ErrorResponse(
                            "Refund request not found."));
                }

                string currentStatus =
                    refundRequest.Status
                    ?? refundRequest.StatusName
                    ?? string.Empty;

                if (!string.Equals(
                        currentStatus,
                        "Requested",
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        "Only Requested returns can be approved or denied.");
                }

                bool isApproval = string.Equals(
                    model.Decision,
                    "Approve",
                    StringComparison.OrdinalIgnoreCase);

                bool isDenial = string.Equals(
                    model.Decision,
                    "Deny",
                    StringComparison.OrdinalIgnoreCase);

                if (!isApproval && !isDenial)
                {
                    throw new InvalidOperationException(
                        "Decision must be Approve or Deny.");
                }

                model.Decision =
                    isApproval ? "Approve" : "Deny";

                ReturnEligibilityEvaluation eligibility;

                if (isApproval)
                {
                    ShopifyReturnOrderLookupResult? lookup =
                        await LoadReturnOrderAsync(refundRequest);

                    eligibility =
                        BuildEligibility(refundRequest, lookup);

                    ValidateApproval(
                        model,
                        eligibility,
                        User.IsInRole("AdminHigh"));
                }
                else
                {
                    if (string.IsNullOrWhiteSpace(
                        model.DenialReason))
                    {
                        throw new InvalidOperationException(
                            "A denial reason is required.");
                    }

                    // An administrator must be able to deny a malformed or
                    // unverifiable request even when Shopify lookup fails.
                    eligibility = BuildDenialSnapshot(
                        refundRequest,
                        model.DenialReason);

                    model.ReturnShippingPayer = null;
                    model.SellerError = null;
                    model.CustomerInstructions = null;
                    model.UsePolicyOverride = false;
                    model.PolicyOverrideReason = null;
                }

                IUserAuthData user =
                    _authService.GetCurrentUser();

                _service.ApplyDecision(
                    id,
                    model,
                    eligibility,
                    user.Id);

                RefundRequest refreshed =
                    _service.GetById(id);

                if (refreshed != null)
                {
                    refreshed = TrySendDecisionEmail(refreshed);
                }

                response =
                    new ItemResponse<RefundRequest>
                    {
                        Item = refreshed
                    };
            }
            catch (InvalidOperationException ex)
            {
                code = 400;
                response = new ErrorResponse(ex.Message);
            }
            catch (Exception ex)
            {
                code = 500;
                response =
                    new ErrorResponse(
                        "Unable to save the return decision.");

                Logger.LogError(ex.ToString());
            }

            return StatusCode(code, response);
        }

        [HttpPost("{id:int}/decision-email")]
        [Authorize(Policy = "AdminAction")]
        public ActionResult<ItemResponse<RefundRequest>>
            SendDecisionEmail(int id)
        {
            int code = 200;
            BaseResponse response = null;

            try
            {
                RefundRequest refundRequest =
                    _service.GetById(id);

                if (refundRequest == null)
                {
                    return NotFound(
                        new ErrorResponse(
                            "Refund request not found."));
                }

                string status =
                    refundRequest.Status
                    ?? refundRequest.StatusName
                    ?? string.Empty;

                if (!string.Equals(
                        status,
                        "Approved",
                        StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(
                        status,
                        "Denied",
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        "Decision emails can only be sent for Approved or Denied requests.");
                }

                RefundRequest refreshed =
                    TrySendDecisionEmail(refundRequest);

                response = new ItemResponse<RefundRequest>
                {
                    Item = refreshed
                };
            }
            catch (InvalidOperationException ex)
            {
                code = 400;
                response = new ErrorResponse(ex.Message);
            }
            catch (Exception ex)
            {
                code = 500;
                response = new ErrorResponse(
                    "Unable to send the return decision email.");
                Logger.LogError(ex.ToString());
            }

            return StatusCode(code, response);
        }

        [HttpPost("{id:int}/return-label")]
        [Authorize(Policy = "AdminAction")]
        [RequestSizeLimit(10 * 1024 * 1024)]
        public async Task<
            ActionResult<ItemResponse<RefundRequest>>>
            SaveReturnLabel(
                int id,
                [FromForm] RefundRequestReturnLabelRequest model)
        {
            int code = 200;
            BaseResponse response = null;
            string? savedFullPath = null;
            bool labelRecordSaved = false;

            try
            {
                RefundRequest refundRequest =
                    _service.GetById(id);

                ValidateApprovedReturn(refundRequest);

                string shippingPayer =
                    (refundRequest!.ReturnShippingPayer
                        ?? string.Empty).Trim();

                bool sellerPaid =
                    string.Equals(
                        shippingPayer,
                        "Seller",
                        StringComparison.OrdinalIgnoreCase);

                bool buyerPaid =
                    string.Equals(
                        shippingPayer,
                        "Buyer",
                        StringComparison.OrdinalIgnoreCase);

                if (!sellerPaid && !buyerPaid)
                {
                    throw new InvalidOperationException(
                        "A Pirate Ship PDF label can only be saved for a buyer-paid or seller-paid return.");
                }

                if (buyerPaid
                    && (!model.LabelCost.HasValue
                        || model.LabelCost.Value <= 0))
                {
                    throw new InvalidOperationException(
                        "A buyer-paid Pirate Ship label requires a positive label cost so it can be documented for deduction from the final refund.");
                }

                ValidateReturnLabelPdf(model.LabelPdf);

                if (string.IsNullOrWhiteSpace(model.Carrier))
                {
                    throw new InvalidOperationException(
                        "Carrier is required.");
                }

                if (string.IsNullOrWhiteSpace(
                    model.TrackingNumber))
                {
                    throw new InvalidOperationException(
                        "Tracking number is required.");
                }

                string labelFolder =
                    Path.Combine(
                        GetReturnLabelStorageRoot(),
                        id.ToString());

                Directory.CreateDirectory(labelFolder);

                string storedFileName =
                    $"{Guid.NewGuid():N}.pdf";

                savedFullPath =
                    Path.Combine(
                        labelFolder,
                        storedFileName);

                await using (
                    FileStream outputStream =
                        new FileStream(
                            savedFullPath,
                            FileMode.CreateNew,
                            FileAccess.Write,
                            FileShare.None))
                {
                    await model.LabelPdf
                        .CopyToAsync(outputStream);
                }

                string relativePath =
                    Path.Combine(
                        id.ToString(),
                        storedFileName)
                    .Replace(
                        Path.DirectorySeparatorChar,
                        '/');

                string originalFileName =
                    Path.GetFileName(
                        model.LabelPdf.FileName);

                IUserAuthData user =
                    _authService.GetCurrentUser();

                _service.SaveReturnLabel(
                    id,
                    relativePath,
                    originalFileName,
                    "application/pdf",
                    model,
                    user.Id);

                labelRecordSaved = true;

                RefundRequest refreshed =
                    _service.GetById(id)
                    ?? throw new InvalidOperationException(
                        "The return label was saved, but the request could not be reloaded.");

                refreshed =
                    TrySendReturnLabelEmail(refreshed);

                TryDeleteReplacedReturnLabel(
                    refundRequest.ReturnLabelFilePath,
                    relativePath);

                response =
                    new ItemResponse<RefundRequest>
                    {
                        Item = refreshed
                    };
            }
            catch (InvalidOperationException ex)
            {
                if (!labelRecordSaved
                    && !string.IsNullOrWhiteSpace(savedFullPath))
                {
                    TryDeleteFile(savedFullPath);
                }

                code = 400;
                response =
                    new ErrorResponse(ex.Message);
            }
            catch (Exception ex)
            {
                if (!labelRecordSaved
                    && !string.IsNullOrWhiteSpace(savedFullPath))
                {
                    TryDeleteFile(savedFullPath);
                }

                code = 500;
                response =
                    new ErrorResponse(
                        "Unable to save the Pirate Ship PDF return label.");

                Logger.LogError(ex.ToString());
            }

            return StatusCode(code, response);
        }

        [HttpPost("{id:int}/return-label-email")]
        [Authorize(Policy = "AdminAction")]
        public ActionResult<ItemResponse<RefundRequest>>
            SendReturnLabelEmail(int id)
        {
            int code = 200;
            BaseResponse response = null;

            try
            {
                RefundRequest refundRequest = _service.GetById(id);
                ValidateApprovedReturn(refundRequest);

                RefundRequest refreshed =
                    TrySendReturnLabelEmail(refundRequest!);

                response = new ItemResponse<RefundRequest>
                {
                    Item = refreshed
                };
            }
            catch (InvalidOperationException ex)
            {
                code = 400;
                response = new ErrorResponse(ex.Message);
            }
            catch (Exception ex)
            {
                code = 500;
                response = new ErrorResponse(
                    "Unable to send the Pirate Ship return label email.");
                Logger.LogError(ex.ToString());
            }

            return StatusCode(code, response);
        }

        [HttpPost("{id:int}/return-tracking")]
        [Authorize(Policy = "AdminAction")]
        public ActionResult<ItemResponse<RefundRequest>>
            UpdateReturnTracking(
                int id,
                RefundRequestReturnTrackingRequest model)
        {
            int code = 200;
            BaseResponse response = null;

            try
            {
                RefundRequest refundRequest = _service.GetById(id);
                ValidateApprovedReturn(refundRequest);

                IUserAuthData user = _authService.GetCurrentUser();
                _service.UpdateReturnTracking(id, model, user.Id);

                response = new ItemResponse<RefundRequest>
                {
                    Item = _service.GetById(id)
                };
            }
            catch (InvalidOperationException ex)
            {
                code = 400;
                response = new ErrorResponse(ex.Message);
            }
            catch (Exception ex)
            {
                code = 500;
                response = new ErrorResponse(
                    "Unable to save the return tracking information.");
                Logger.LogError(ex.ToString());
            }

            return StatusCode(code, response);
        }

        [HttpPost("{id:int}/return-delivered")]
        [Authorize(Policy = "AdminAction")]
        public ActionResult<ItemResponse<RefundRequest>>
            MarkReturnDelivered(
                int id,
                RefundRequestReturnDeliveredRequest model)
        {
            int code = 200;
            BaseResponse response = null;

            try
            {
                RefundRequest refundRequest = _service.GetById(id);
                ValidateApprovedReturn(refundRequest);

                IUserAuthData user = _authService.GetCurrentUser();
                _service.MarkReturnDelivered(id, model, user.Id);

                response = new ItemResponse<RefundRequest>
                {
                    Item = _service.GetById(id)
                };
            }
            catch (InvalidOperationException ex)
            {
                code = 400;
                response = new ErrorResponse(ex.Message);
            }
            catch (Exception ex)
            {
                code = 500;
                response = new ErrorResponse(
                    "Unable to mark the return as carrier delivered.");
                Logger.LogError(ex.ToString());
            }

            return StatusCode(code, response);
        }

        [HttpPost("{id:int}/item-received")]
        [Authorize(Policy = "AdminAction")]
        public ActionResult<ItemResponse<RefundRequest>>
            MarkItemReceived(
                int id,
                RefundRequestMarkReceivedRequest model)
        {
            int code = 200;
            BaseResponse response = null;

            try
            {
                RefundRequest refundRequest = _service.GetById(id);
                ValidateApprovedReturn(refundRequest);

                if (refundRequest.Items == null
                    || refundRequest.Items.Count == 0)
                {
                    throw new InvalidOperationException(
                        "Match at least one Shopify order item before receiving the return.");
                }

                if (refundRequest.ItemReceivedAt.HasValue)
                {
                    throw new InvalidOperationException(
                        "This return has already been marked received.");
                }

                IUserAuthData user = _authService.GetCurrentUser();
                _service.MarkItemReceived(id, model, user.Id);

                response = new ItemResponse<RefundRequest>
                {
                    Item = _service.GetById(id)
                };
            }
            catch (InvalidOperationException ex)
            {
                code = 400;
                response = new ErrorResponse(ex.Message);
            }
            catch (Exception ex)
            {
                code = 500;
                response = new ErrorResponse(
                    "Unable to mark the returned item as received.");
                Logger.LogError(ex.ToString());
            }

            return StatusCode(code, response);
        }

        [HttpPost("{id:int}/inspection")]
        [Authorize(Policy = "AdminAction")]
        public ActionResult<ItemResponse<RefundRequest>>
            CompleteInspection(
                int id,
                RefundRequestCompleteInspectionRequest model)
        {
            int code = 200;
            BaseResponse response = null;

            try
            {
                RefundRequest refundRequest = _service.GetById(id);
                ValidateApprovedReturn(refundRequest);

                if (!refundRequest.ItemReceivedAt.HasValue)
                {
                    throw new InvalidOperationException(
                        "Mark the returned item as received before completing inspection.");
                }

                if (string.Equals(
                        refundRequest.InspectionStatus,
                        "Completed",
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        "Inspection has already been completed for this return.");
                }

                ValidateInspectionRequest(refundRequest, model);

                IUserAuthData user = _authService.GetCurrentUser();
                _service.CompleteInspection(id, model, user.Id);

                response = new ItemResponse<RefundRequest>
                {
                    Item = _service.GetById(id)
                };
            }
            catch (InvalidOperationException ex)
            {
                code = 400;
                response = new ErrorResponse(ex.Message);
            }
            catch (Exception ex)
            {
                code = 500;
                response = new ErrorResponse(
                    "Unable to complete the return inspection.");
                Logger.LogError(ex.ToString());
            }

            return StatusCode(code, response);
        }

        [HttpPost("{id:int}/refund-preview")]
        [Authorize(Policy = "AdminAction")]
        public async Task<
            ActionResult<ItemResponse<ShopifyRefundPreviewResult>>>
            GetRefundPreview(
                int id,
                [FromBody] ShopifyRefundPreviewOptions? model)
        {
            int code = 200;
            BaseResponse response = null;

            try
            {
                RefundRequest? refundRequest =
                    _service.GetById(id);

                if (refundRequest == null)
                {
                    return StatusCode(
                        404,
                        new ErrorResponse(
                            "Refund request not found."));
                }

                ShopifyRefundPreviewOptions options =
                    model ?? new ShopifyRefundPreviewOptions();

                ShopifyRefundPreviewResult result =
                    await _shopifyOrderService
                        .GetRefundPreviewAsync(
                            refundRequest,
                            options);

                response =
                    new ItemResponse<ShopifyRefundPreviewResult>
                    {
                        Item = result
                    };
            }
            catch (InvalidOperationException ex)
            {
                code = 400;
                response = new ErrorResponse(ex.Message);
            }
            catch (Exception ex)
            {
                code = 500;
                response = new ErrorResponse(
                    "Unable to calculate the Shopify refund preview.");

                Logger.LogError(
                    ex,
                    "Shopify refund preview failed for refund request {RefundRequestId}.",
                    id);
            }

            return StatusCode(code, response);
        }

        [HttpPatch("{id:int}/status")]
        [Authorize(Policy = "AdminAction")]
        public ActionResult<SuccessResponse> UpdateStatus(
            int id,
            RefundRequestUpdateStatusRequest model)
        {
            int code = 200;
            BaseResponse response = null;

            try
            {
                IUserAuthData user = _authService.GetCurrentUser();
                bool statusChanged =
                    _service.UpdateStatus(id, model, user.Id);

                if (statusChanged)
                {
                    RefundRequest refreshed =
                        _service.GetById(id)
                        ?? throw new InvalidOperationException(
                            "The updated return request could not be reloaded.");

                    TrySendStatusEmail(refreshed);
                }

                response = new SuccessResponse();
            }
            catch (InvalidOperationException ex)
            {
                code = 400;
                response = new ErrorResponse(ex.Message);
            }
            catch (Exception ex)
            {
                code = 500;
                response = new ErrorResponse(
                    "Unable to update the return status.");
                Logger.LogError(ex.ToString());
            }

            return StatusCode(code, response);
        }

        private RefundRequest TrySendReturnLabelEmail(
            RefundRequest refundRequest)
        {
            string labelVersion =
                refundRequest.ReturnLabelCreatedAt?.Ticks.ToString()
                ?? refundRequest.ReturnLabelFilePath
                ?? "current";
            string messageKey =
                $"return-label:{refundRequest.Id}:{labelVersion}";

            try
            {
                bool shouldSend = _emailDeliveryLogService.TryBegin(
                    messageKey,
                    "ReturnLabel",
                    "RefundRequest",
                    refundRequest.Id,
                    refundRequest.CustomerEmail ?? string.Empty);

                if (shouldSend)
                {
                    _emailService.SendReturnLabelEmail(refundRequest);
                    _emailDeliveryLogService.MarkSent(messageKey);
                    _service.MarkReturnLabelEmailResult(
                        refundRequest.Id,
                        true,
                        null);
                }
            }
            catch (Exception emailException)
            {
                TryMarkEmailLogFailed(messageKey, emailException);
                _service.MarkReturnLabelEmailResult(
                    refundRequest.Id,
                    false,
                    emailException.Message);

                Logger.LogError(
                    emailException,
                    "The return label was saved, but its customer email failed for refund request {RefundRequestId}.",
                    refundRequest.Id);
            }

            return _service.GetById(refundRequest.Id)
                ?? refundRequest;
        }

        private static void ValidateReturnLabelPdf(
            IFormFile? file)
        {
            const long maxFileSize =
                10L * 1024L * 1024L;

            if (file == null || file.Length <= 0)
            {
                throw new InvalidOperationException(
                    "Select the Pirate Ship PDF return label.");
            }

            if (file.Length > maxFileSize)
            {
                throw new InvalidOperationException(
                    "The return-label PDF must be 10 MB or smaller.");
            }

            string extension =
                Path.GetExtension(file.FileName);

            if (!string.Equals(
                    extension,
                    ".pdf",
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "The return label must be a PDF file.");
            }

            using Stream stream =
                file.OpenReadStream();

            byte[] signature = new byte[5];
            int bytesRead =
                stream.Read(
                    signature,
                    0,
                    signature.Length);

            string header =
                System.Text.Encoding.ASCII.GetString(
                    signature,
                    0,
                    bytesRead);

            if (!header.StartsWith(
                    "%PDF-",
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "The selected file is not a valid PDF.");
            }
        }

        private void TryDeleteReplacedReturnLabel(
            string? oldRelativePath,
            string newRelativePath)
        {
            if (string.IsNullOrWhiteSpace(oldRelativePath)
                || string.Equals(
                    oldRelativePath,
                    newRelativePath,
                    StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            string oldPath =
                oldRelativePath
                    .Replace(
                        '/',
                        Path.DirectorySeparatorChar)
                    .Replace(
                        '\\',
                        Path.DirectorySeparatorChar);

            string storageRoot =
                GetReturnLabelStorageRoot();

            string fullPath =
                Path.GetFullPath(
                    Path.Combine(
                        storageRoot,
                        oldPath));

            string allowedRoot =
                Path.GetFullPath(storageRoot)
                .TrimEnd(
                    Path.DirectorySeparatorChar)
                + Path.DirectorySeparatorChar;

            if (fullPath.StartsWith(
                    allowedRoot,
                    StringComparison.OrdinalIgnoreCase))
            {
                TryDeleteFile(fullPath);
            }
        }

        private string GetReturnLabelStorageRoot()
        {
            string? home =
                Environment.GetEnvironmentVariable(
                    "HOME");

            if (!string.IsNullOrWhiteSpace(home))
            {
                return Path.Combine(
                    home,
                    "data",
                    "Site_2024",
                    "ReturnLabels");
            }

            return Path.Combine(
                _webHostEnvironment.ContentRootPath,
                "App_Data",
                "ReturnLabels");
        }

        private static void TryDeleteFile(
            string fullPath)
        {
            try
            {
                if (System.IO.File.Exists(fullPath))
                {
                    System.IO.File.Delete(fullPath);
                }
            }
            catch
            {
                // Do not fail the saved return workflow
                // because an obsolete local file could not be deleted.
            }
        }

        private static void ValidateInspectionRequest(
            RefundRequest refundRequest,
            RefundRequestCompleteInspectionRequest model)
        {
            if (model == null)
            {
                throw new InvalidOperationException(
                    "Inspection details are required.");
            }

            if (string.IsNullOrWhiteSpace(model.InspectionSummary))
            {
                throw new InvalidOperationException(
                    "Enter an overall inspection summary.");
            }

            if (model.Items == null || model.Items.Count == 0)
            {
                throw new InvalidOperationException(
                    "Inspect every matched return item.");
            }

            if (refundRequest.Items == null
                || model.Items.Count != refundRequest.Items.Count)
            {
                throw new InvalidOperationException(
                    "Every matched return item must be included in the inspection.");
            }

            if (model.Items
                .GroupBy(item => item.RefundRequestItemId)
                .Any(group => group.Count() > 1))
            {
                throw new InvalidOperationException(
                    "The same return item cannot be inspected more than once.");
            }

            foreach (
                RefundRequestItemInspectionRequest inspected
                in model.Items)
            {
                RefundRequestItem? item =
                    refundRequest.Items.FirstOrDefault(
                        current =>
                            current.Id ==
                            inspected.RefundRequestItemId);

                if (item == null)
                {
                    throw new InvalidOperationException(
                        "One of the inspected items does not belong to this return request.");
                }

                int approvedQuantity =
                    Math.Max(1, item.Quantity);

                if (inspected.QuantityReceived < 0
                    || inspected.QuantityReceived > approvedQuantity)
                {
                    throw new InvalidOperationException(
                        $"Quantity received for {item.PartName ?? item.ProductTitle ?? "the item"} must be between 0 and {approvedQuantity}.");
                }

                int restockQuantity =
                    inspected.RestockQuantity;
                int holdQuantity =
                    inspected.HoldQuantity;
                int damagedQuantity =
                    inspected.DamagedQuantity;

                if (restockQuantity < 0
                    || holdQuantity < 0
                    || damagedQuantity < 0)
                {
                    throw new InvalidOperationException(
                        "Inventory allocation quantities cannot be negative.");
                }

                if (restockQuantity
                        + holdQuantity
                        + damagedQuantity
                    != inspected.QuantityReceived)
                {
                    throw new InvalidOperationException(
                        $"Restock, hold, and damaged quantities for {item.PartName ?? item.ProductTitle ?? "the item"} must equal the quantity received.");
                }

                if (restockQuantity > 0
                    && !item.PartId.HasValue)
                {
                    throw new InvalidOperationException(
                        "A Shopify-only item cannot be selected for automatic restocking until it is matched to a Site_2024 part.");
                }

                bool hasIssue =
                    !inspected.IsSameItem
                    || !inspected.IsComplete
                    || inspected.IsAltered
                    || inspected.HasNewDamage
                    || inspected.QuantityReceived < approvedQuantity
                    || holdQuantity > 0
                    || damagedQuantity > 0;

                if (hasIssue
                    && string.IsNullOrWhiteSpace(
                        inspected.InspectionNotes))
                {
                    throw new InvalidOperationException(
                        $"Add inspection notes for {item.PartName ?? item.ProductTitle ?? "the item"} because an issue, held quantity, damaged quantity, or missing quantity was recorded.");
                }
            }
        }

        private static void ValidateApprovedReturn(
            RefundRequest? refundRequest)
        {
            if (refundRequest == null)
            {
                throw new InvalidOperationException(
                    "Refund request not found.");
            }

            string status =
                refundRequest.Status
                ?? refundRequest.StatusName
                ?? string.Empty;

            if (!string.Equals(
                status,
                "Approved",
                StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "Return shipping can only be managed for an Approved request.");
            }
        }

        private RefundRequest TrySendDecisionEmail(
            RefundRequest refundRequest)
        {
            string status =
                refundRequest.Status
                ?? refundRequest.StatusName
                ?? "Decision";
            string messageKey =
                $"return-decision:{refundRequest.Id}:{status.ToLowerInvariant()}";

            try
            {
                bool shouldSend = _emailDeliveryLogService.TryBegin(
                    messageKey,
                    "ReturnDecision",
                    "RefundRequest",
                    refundRequest.Id,
                    refundRequest.CustomerEmail ?? string.Empty);

                if (shouldSend)
                {
                    _emailService.SendReturnDecisionEmail(refundRequest);
                    _emailDeliveryLogService.MarkSent(messageKey);
                    _service.MarkDecisionEmailResult(
                        refundRequest.Id,
                        true,
                        null);
                }
            }
            catch (Exception emailException)
            {
                TryMarkEmailLogFailed(messageKey, emailException);
                _service.MarkDecisionEmailResult(
                    refundRequest.Id,
                    false,
                    emailException.Message);

                Logger.LogError(
                    emailException,
                    "The return decision was saved, but its customer email failed for refund request {RefundRequestId}.",
                    refundRequest.Id);
            }

            return _service.GetById(refundRequest.Id)
                ?? refundRequest;
        }

        private async Task<ShopifyReturnOrderLookupResult?>
            LoadReturnOrderAsync(RefundRequest refundRequest)
        {
            if (string.IsNullOrWhiteSpace(
                refundRequest.OrderNumber))
            {
                throw new InvalidOperationException(
                    "This request does not have an order number.");
            }

            ShopifyReturnOrderLookupResult? lookup =
                await _shopifyOrderService.GetOrderForReturnAsync(
                    refundRequest.OrderNumber,
                    refundRequest.CustomerEmail);

            if (lookup == null)
            {
                throw new InvalidOperationException(
                    "The Shopify order could not be found.");
            }

            return lookup;
        }

        private ReturnEligibilityEvaluation BuildEligibility(
            RefundRequest refundRequest,
            ShopifyReturnOrderLookupResult? lookup)
        {
            ReturnEligibilityEvaluation result =
                new ReturnEligibilityEvaluation
                {
                    RefundRequestId = refundRequest.Id,
                    CustomerEmailMatches =
                        lookup?.CustomerEmailMatches == true,
                    DeliveredAt = lookup?.Order.DeliveredAt,
                    DestinationCountryCode =
                        lookup?.Order.DestinationCountryCode,
                    IsInternational =
                        lookup?.Order.IsInternational == true
                };

            List<long> selectedLineItemIds =
                refundRequest.Items
                    .Where(item =>
                        item.ShopifyLineItemId.HasValue)
                    .Select(item =>
                        item.ShopifyLineItemId!.Value)
                    .Distinct()
                    .ToList();

            result.HasMatchedItems =
                selectedLineItemIds.Count > 0;

            AddEligibilityIssue(
                result,
                !result.HasMatchedItems,
                "NO_MATCHED_ITEMS",
                "Blocking",
                "No Shopify order items are matched to this request.",
                true);

            AddEligibilityIssue(
                result,
                !result.CustomerEmailMatches,
                "EMAIL_MISMATCH",
                "Warning",
                "The customer email does not match the Shopify order.",
                true);

            if (result.DeliveredAt.HasValue)
            {
                result.ReturnWindowEndsAt =
                    result.DeliveredAt.Value.AddDays(30);

                result.IsWithinReturnWindow =
                    DateTime.UtcNow <=
                    result.ReturnWindowEndsAt.Value.ToUniversalTime();

                AddEligibilityIssue(
                    result,
                    !result.IsWithinReturnWindow,
                    "RETURN_WINDOW_EXPIRED",
                    "Blocking",
                    "The 30-day return window has expired.",
                    true);
            }
            else
            {
                result.IsWithinReturnWindow = false;

                AddEligibilityIssue(
                    result,
                    true,
                    "DELIVERY_DATE_MISSING",
                    "ManualReview",
                    "Shopify does not have a confirmed delivery date. Manual review is required.",
                    true);
            }

            List<ShopifyOrderLineItemSummary> selectedOrderItems =
                lookup?.Order.LineItems
                    .Where(item =>
                        selectedLineItemIds.Contains(
                            item.ShopifyLineItemId))
                    .ToList()
                ?? new List<ShopifyOrderLineItemSummary>();

            AddEligibilityIssue(
                result,
                selectedOrderItems.Count !=
                    selectedLineItemIds.Count,
                "ORDER_ITEM_NOT_FOUND",
                "Blocking",
                "At least one matched Shopify line item is no longer present on the live order.",
                true);

            result.HasPartsNotWorkingItems =
                selectedOrderItems.Any(item =>
                    item.LocalPart?.IsPartsNotWorking == true)
                || refundRequest.Items.Any(item =>
                    item.IsPartsNotWorking);

            AddEligibilityIssue(
                result,
                result.HasPartsNotWorkingItems,
                "PARTS_NOT_WORKING",
                "Blocking",
                "At least one selected item is listed as Parts / Not Working and is normally final sale.",
                true);

            result.HasUnknownConditionItems =
                selectedOrderItems.Any(item =>
                    item.LocalPart == null
                    || string.IsNullOrWhiteSpace(
                        item.LocalPart.ConditionName));

            AddEligibilityIssue(
                result,
                result.HasUnknownConditionItems,
                "CONDITION_UNKNOWN",
                "ManualReview",
                "At least one selected order item has no verified Site_2024 condition.",
                true);

            List<RefundRequestDuplicateConflict> conflicts =
                _service.GetDuplicateConflicts(
                    refundRequest.Id);

            result.DuplicateRequestCount =
                conflicts
                    .Select(conflict =>
                        conflict.RefundRequestId)
                    .Distinct()
                    .Count();

            AddEligibilityIssue(
                result,
                result.DuplicateRequestCount > 0,
                "DUPLICATE_REQUEST",
                "Blocking",
                $"{result.DuplicateRequestCount} other return request(s) already use one or more selected Shopify order items.",
                true);

            AddEligibilityIssue(
                result,
                string.IsNullOrWhiteSpace(
                    result.DestinationCountryCode),
                "DESTINATION_UNKNOWN",
                "ManualReview",
                "The order destination country is unavailable. Manual review is required before assigning return shipping.",
                true);

            AddEligibilityIssue(
                result,
                result.IsInternational,
                "INTERNATIONAL_RETURN",
                "Information",
                "International return postage is buyer-paid and is not reimbursed.",
                false);

            result.RequiresPolicyOverride =
                result.Issues.Any(issue =>
                    issue.RequiresOverride);

            result.CanApproveWithoutOverride =
                result.HasMatchedItems
                && !result.RequiresPolicyOverride;

            result.EligibilityStatus =
                result.CanApproveWithoutOverride
                    ? "Eligible"
                    : result.Issues.Any(issue =>
                        string.Equals(
                            issue.Severity,
                            "Blocking",
                            StringComparison.OrdinalIgnoreCase))
                        ? "Ineligible"
                        : "ManualReview";

            result.Summary = result.Issues.Count == 0
                ? "Eligible under the current return rules."
                : string.Join(
                    " | ",
                    result.Issues.Select(issue =>
                        issue.Message));

            return result;
        }

        private static ReturnEligibilityEvaluation
            BuildDenialSnapshot(
                RefundRequest refundRequest,
                string denialReason)
        {
            return new ReturnEligibilityEvaluation
            {
                RefundRequestId = refundRequest.Id,
                HasMatchedItems = refundRequest.Items.Any(item =>
                    item.ShopifyLineItemId.HasValue),
                CustomerEmailMatches =
                    refundRequest.CustomerEmailMatched == true,
                DeliveredAt =
                    refundRequest.ShopifyDeliveredAt,
                ReturnWindowEndsAt =
                    refundRequest.ReturnWindowEndsAt,
                IsWithinReturnWindow =
                    refundRequest.ReturnWindowEndsAt.HasValue
                    && DateTime.UtcNow <=
                        refundRequest.ReturnWindowEndsAt.Value
                            .ToUniversalTime(),
                IsInternational =
                    refundRequest.IsInternational == true,
                DestinationCountryCode =
                    refundRequest.DestinationCountryCode,
                RequiresPolicyOverride = false,
                CanApproveWithoutOverride = false,
                EligibilityStatus =
                    refundRequest.EligibilityStatus
                    ?? "Denied",
                Summary =
                    refundRequest.EligibilitySummary
                    ?? $"Request denied by administrator: {denialReason.Trim()}"
            };
        }

        private static void AddEligibilityIssue(
            ReturnEligibilityEvaluation result,
            bool shouldAdd,
            string code,
            string severity,
            string message,
            bool requiresOverride)
        {
            if (!shouldAdd)
            {
                return;
            }

            result.Issues.Add(
                new ReturnEligibilityIssue
                {
                    Code = code,
                    Severity = severity,
                    Message = message,
                    RequiresOverride = requiresOverride
                });
        }

        private static void ValidateApproval(
            RefundRequestDecisionRequest model,
            ReturnEligibilityEvaluation eligibility,
            bool isAdminHigh)
        {
            string payer =
                (model.ReturnShippingPayer ?? string.Empty)
                    .Trim();

            if (payer != "Buyer"
                && payer != "Seller"
                && payer != "NoLabel")
            {
                throw new InvalidOperationException(
                    "Choose Buyer, Seller, or No Label for return shipping.");
            }

            if (!model.SellerError.HasValue)
            {
                throw new InvalidOperationException(
                    "Specify whether the return was caused by seller error.");
            }

            if (string.IsNullOrWhiteSpace(
                model.CustomerInstructions))
            {
                throw new InvalidOperationException(
                    "Customer return instructions are required for approval.");
            }

            if (eligibility.IsInternational
                && payer != "Buyer"
                && !model.UsePolicyOverride)
            {
                throw new InvalidOperationException(
                    "International return postage must be buyer-paid unless an Admin High override is documented.");
            }

            if (eligibility.RequiresPolicyOverride
                && !model.UsePolicyOverride)
            {
                throw new InvalidOperationException(
                    "This request has policy conflicts. An Admin High override and reason are required to approve it.");
            }

            if (model.UsePolicyOverride)
            {
                if (!isAdminHigh)
                {
                    throw new InvalidOperationException(
                        "Only an Admin High user can override return policy rules.");
                }

                if (string.IsNullOrWhiteSpace(
                    model.PolicyOverrideReason))
                {
                    throw new InvalidOperationException(
                        "A policy override reason is required.");
                }
            }
        }

        private void SaveCustomerPhotos(int refundRequestId, List<IFormFile> photos)
        {
            string[] allowed = { ".jpg", ".jpeg", ".png", ".webp" };
            const long maxBytes = 5 * 1024 * 1024;

            string uploadRoot =
                _configuration["UploadStorage:RootPath"]
                ?? Path.Combine(_webHostEnvironment.WebRootPath, "uploads");

            string uploadsFolder = Path.Combine(uploadRoot, "returns");

            Directory.CreateDirectory(uploadsFolder);

            for (int i = 0; i < photos.Count; i++)
            {
                IFormFile photo = photos[i];

                if (photo == null || photo.Length == 0)
                {
                    throw new Exception("One of the proof photos is empty.");
                }

                string ext = Path.GetExtension(photo.FileName).ToLowerInvariant();
                if (!allowed.Contains(ext))
                {
                    throw new Exception($"Invalid proof photo type: {ext}. Allowed: jpg, jpeg, png, webp.");
                }

                if (photo.Length > maxBytes)
                {
                    throw new Exception("Proof photo too large. Max size is 5MB.");
                }

                string fileName = $"{Guid.NewGuid()}{ext}";
                string filePath = Path.Combine(uploadsFolder, fileName);

                using (FileStream stream = new FileStream(filePath, FileMode.Create))
                {
                    photo.CopyTo(stream);
                }

                _service.AddPhoto(refundRequestId, new RefundRequestPhotoAddRequest
                {
                    Url = $"/uploads/returns/{fileName}",
                    OriginalFileName = photo.FileName,
                    ContentType = photo.ContentType,
                    SortOrder = i
                });
            }
        }
        private void TrySendSubmissionEmails(RefundRequest refundRequest)
        {
            string businessKey =
                $"return-submitted-business:{refundRequest.Id}";
            string customerKey =
                $"return-submitted-customer:{refundRequest.Id}";

            TrySendLoggedReturnEmail(
                businessKey,
                "ReturnSubmissionBusinessNotification",
                refundRequest,
                _emailService.GetContactRecipientEmail("returns"),
                () => _emailService.SendReturnSubmissionBusinessEmail(
                    refundRequest));

            TrySendLoggedReturnEmail(
                customerKey,
                "ReturnSubmissionCustomerConfirmation",
                refundRequest,
                refundRequest.CustomerEmail ?? string.Empty,
                () => _emailService.SendReturnSubmissionCustomerEmail(
                    refundRequest));
        }

        private void TrySendStatusEmail(RefundRequest refundRequest)
        {
            string status =
                refundRequest.Status
                ?? refundRequest.StatusName
                ?? "Unknown";
            string version = refundRequest.DateModified.Ticks.ToString();
            string messageKey =
                $"return-status:{refundRequest.Id}:{status.ToLowerInvariant()}:{version}";

            TrySendLoggedReturnEmail(
                messageKey,
                "ReturnStatusChange",
                refundRequest,
                refundRequest.CustomerEmail ?? string.Empty,
                () => _emailService.SendReturnStatusEmail(refundRequest));
        }

        private void TrySendLoggedReturnEmail(
            string messageKey,
            string messageType,
            RefundRequest refundRequest,
            string recipient,
            Action sendAction)
        {
            try
            {
                bool shouldSend = _emailDeliveryLogService.TryBegin(
                    messageKey,
                    messageType,
                    "RefundRequest",
                    refundRequest.Id,
                    recipient);

                if (!shouldSend)
                {
                    return;
                }

                sendAction();
                _emailDeliveryLogService.MarkSent(messageKey);
            }
            catch (Exception emailException)
            {
                TryMarkEmailLogFailed(messageKey, emailException);
                Logger.LogError(
                    emailException,
                    "{MessageType} email failed for refund request {RefundRequestId}.",
                    messageType,
                    refundRequest.Id);
            }
        }

        private void TryMarkEmailLogFailed(
            string messageKey,
            Exception emailException)
        {
            try
            {
                _emailDeliveryLogService.MarkFailed(
                    messageKey,
                    emailException.Message);
            }
            catch (Exception logException)
            {
                Logger.LogError(
                    logException,
                    "Email failure could not be recorded for message {MessageKey}.",
                    messageKey);
            }
        }


    }
}
