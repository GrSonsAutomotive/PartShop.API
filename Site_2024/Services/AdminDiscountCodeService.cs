using Site_2024.Web.Api.Constructors;
using Site_2024.Web.Api.Extensions;
using Site_2024.Web.Api.Interfaces;
using Site_2024.Web.Api.Models;
using Site_2024.Web.Api.Requests;
using System.Data;
using System.Data.SqlClient;
using System.Text.Json;

namespace Site_2024.Web.Api.Services
{
    public class AdminDiscountCodeService : IAdminDiscountCodeService
    {
        private readonly IDataProvider _data;

        public AdminDiscountCodeService(IDataProvider data)
        {
            _data = data;
        }

        public int Add(AdminDiscountCodeAddRequest model, int? userId)
        {
            ValidateCollectionRules(model);

            int id = 0;
            const string procName = "[dbo].[AdminDiscountCodes_Insert]";

            _data.ExecuteCmd(procName,
                inputParamMapper: delegate (SqlParameterCollection col)
                {
                    AddCommonParams(model, col);
                    col.AddWithValue("@CreatedByUserId", userId.HasValue ? userId.Value : DBNull.Value);

                    SqlParameter idOut = new("@Id", SqlDbType.Int)
                    {
                        Direction = ParameterDirection.Output,
                        Value = 0
                    };

                    col.Add(idOut);
                },
                singleRecordMapper: delegate (IDataReader reader, short set)
                {
                    if (set == 0)
                    {
                        id = Convert.ToInt32(reader["Id"]);
                    }
                });

            return id;
        }

        public AdminDiscountCode? GetById(int id)
        {
            AdminDiscountCode? discount = null;
            const string procName = "[dbo].[AdminDiscountCodes_GetById]";

            _data.ExecuteCmd(procName,
                inputParamMapper: col => col.AddWithValue("@Id", id),
                singleRecordMapper: delegate (IDataReader reader, short set)
                {
                    discount = MapSingleDiscount(reader);
                });

            if (discount != null &&
                string.Equals(discount.AppliesToType, "CollectionRule", StringComparison.OrdinalIgnoreCase))
            {
                discount.Rules = GetRulesByDiscountId(id);
            }

            return discount;
        }

        public PublicSaleBanner? GetActiveSiteBanner(DateTime utcNow)
        {
            PublicSaleBanner? banner = null;
            const string procName = "[dbo].[AdminDiscountCodes_GetActiveSiteBanner]";

            _data.ExecuteCmd(procName,
                inputParamMapper: col => col.AddWithValue("@UtcNow", utcNow),
                singleRecordMapper: delegate (IDataReader reader, short set)
                {
                    int index = 0;
                    banner = new PublicSaleBanner
                    {
                        Id = reader.GetInt32(index++),
                        Code = reader.GetSafeString(index++),
                        Headline = reader.GetSafeString(index++),
                        Message = reader.GetSafeString(index++),
                        LinkText = reader.GetSafeString(index++),
                        LinkUrl = reader.GetSafeString(index++),
                        StartsAtUtc = reader.GetSafeDateTimeNullable(index++),
                        EndsAtUtc = reader.GetSafeDateTimeNullable(index++)
                    };
                });

            return banner;
        }

        public List<AdminDiscountCodeRule> GetRulesByDiscountId(int discountId)
        {
            List<AdminDiscountCodeRule> rules = new();
            const string procName = "[dbo].[AdminDiscountCodeRules_GetByDiscountId]";

            _data.ExecuteCmd(procName,
                inputParamMapper: col => col.AddWithValue("@AdminDiscountCodeId", discountId),
                singleRecordMapper: delegate (IDataReader reader, short set)
                {
                    int index = 0;
                    rules.Add(new AdminDiscountCodeRule
                    {
                        Id = reader.GetInt32(index++),
                        AdminDiscountCodeId = reader.GetInt32(index++),
                        RuleType = reader.GetSafeString(index++),
                        SourceId = reader.GetSafeInt32Nullable(index++),
                        RuleValue = reader.GetSafeString(index++),
                        ShopifyTag = reader.GetSafeString(index++),
                        RuleOperator = reader.GetSafeString(index++),
                        SortOrder = reader.GetInt32(index++),
                        DateCreated = reader.GetDateTime(index++),
                        DateModified = reader.GetDateTime(index++)
                    });
                });

            return rules;
        }

        public Paged<AdminDiscountCode>? GetPaginated(
            int pageIndex,
            int pageSize,
            AdminDiscountCodeSearchRequest model)
        {
            Paged<AdminDiscountCode>? paged = null;
            List<AdminDiscountCode>? list = null;
            int totalCount = 0;

            const string procName = "[dbo].[AdminDiscountCodes_GetPaginated]";

            _data.ExecuteCmd(procName,
                inputParamMapper: delegate (SqlParameterCollection col)
                {
                    col.AddWithValue("@PageIndex", pageIndex);
                    col.AddWithValue("@PageSize", pageSize);
                    col.AddWithValue("@Status", string.IsNullOrWhiteSpace(model?.Status) ? DBNull.Value : model.Status);
                    col.AddWithValue("@Code", string.IsNullOrWhiteSpace(model?.Code) ? DBNull.Value : model.Code);
                    col.AddWithValue("@CustomerEmail", string.IsNullOrWhiteSpace(model?.CustomerEmail) ? DBNull.Value : model.CustomerEmail);
                },
                singleRecordMapper: delegate (IDataReader reader, short set)
                {
                    AdminDiscountCode discount = MapSingleDiscount(reader);

                    if (totalCount == 0 && reader["TotalCount"] != DBNull.Value)
                    {
                        totalCount = Convert.ToInt32(reader["TotalCount"]);
                    }

                    list ??= new List<AdminDiscountCode>();
                    list.Add(discount);
                });

            if (list != null)
            {
                paged = new Paged<AdminDiscountCode>(list, pageIndex, pageSize, totalCount);
            }

            return paged;
        }

        public void MarkShopifyCreated(int id, AdminDiscountCodeShopifyCreatedRequest model)
        {
            const string procName = "[dbo].[AdminDiscountCodes_MarkShopifyCreated]";

            _data.ExecuteNonQuery(procName,
                inputParamMapper: delegate (SqlParameterCollection col)
                {
                    col.AddWithValue("@Id", id);
                    col.AddWithValue("@ShopifyDiscountGid", model.ShopifyDiscountGid);
                });
        }

        public void MarkCollectionCreated(
            int id,
            string shopifyCollectionGid,
            string? shopifyCollectionHandle)
        {
            const string procName = "[dbo].[AdminDiscountCodes_MarkCollectionCreated]";

            _data.ExecuteNonQuery(procName,
                inputParamMapper: delegate (SqlParameterCollection col)
                {
                    col.AddWithValue("@Id", id);
                    col.AddWithValue("@ShopifyCollectionGid", shopifyCollectionGid);
                    col.AddWithValue("@ShopifyCollectionHandle",
                        string.IsNullOrWhiteSpace(shopifyCollectionHandle)
                            ? DBNull.Value
                            : shopifyCollectionHandle);
                });
        }

        public void MarkCollectionSync(int id, string status, string? error = null)
        {
            const string procName = "[dbo].[AdminDiscountCodes_MarkCollectionSync]";

            _data.ExecuteNonQuery(procName,
                inputParamMapper: delegate (SqlParameterCollection col)
                {
                    col.AddWithValue("@Id", id);
                    col.AddWithValue("@SyncStatus", status);
                    col.AddWithValue("@SyncError", string.IsNullOrWhiteSpace(error) ? DBNull.Value : error);
                });
        }

        public void Deactivate(int id, AdminDiscountCodeDeactivateRequest model, int? userId)
        {
            const string procName = "[dbo].[AdminDiscountCodes_Deactivate]";

            _data.ExecuteNonQuery(procName,
                inputParamMapper: delegate (SqlParameterCollection col)
                {
                    col.AddWithValue("@Id", id);
                    col.AddWithValue("@DeactivatedByUserId", userId.HasValue ? userId.Value : DBNull.Value);
                    col.AddWithValue("@AdminNotes", string.IsNullOrWhiteSpace(model?.AdminNotes) ? DBNull.Value : model.AdminNotes);
                });
        }

        public void MarkError(int id, string adminNotes)
        {
            const string procName = "[dbo].[AdminDiscountCodes_MarkError]";

            _data.ExecuteNonQuery(procName,
                inputParamMapper: delegate (SqlParameterCollection col)
                {
                    col.AddWithValue("@Id", id);
                    col.AddWithValue("@AdminNotes", string.IsNullOrWhiteSpace(adminNotes)
                        ? DBNull.Value
                        : adminNotes);
                });
        }

        private static void AddCommonParams(
            AdminDiscountCodeAddRequest model,
            SqlParameterCollection col)
        {
            string? rulesJson = BuildRulesJson(model);

            col.AddWithValue("@Code", model.Code);
            col.AddWithValue("@Title", string.IsNullOrWhiteSpace(model.Title) ? DBNull.Value : model.Title);
            col.AddWithValue("@DiscountType", model.DiscountType);
            col.AddWithValue("@DiscountValue", model.DiscountValue);
            col.AddWithValue("@AppliesToType", model.AppliesToType);

            col.AddWithValue("@PartId", model.PartId.HasValue ? model.PartId.Value : DBNull.Value);
            col.AddWithValue("@ShopifyProductId", model.ShopifyProductId.HasValue ? model.ShopifyProductId.Value : DBNull.Value);
            col.AddWithValue("@ShopifyVariantId", model.ShopifyVariantId.HasValue ? model.ShopifyVariantId.Value : DBNull.Value);

            col.AddWithValue("@CustomerEmail", string.IsNullOrWhiteSpace(model.CustomerEmail) ? DBNull.Value : model.CustomerEmail);
            col.AddWithValue("@StartsAtUtc", model.StartsAtUtc.HasValue ? model.StartsAtUtc.Value : DBNull.Value);
            col.AddWithValue("@EndsAtUtc", model.EndsAtUtc.HasValue ? model.EndsAtUtc.Value : DBNull.Value);
            col.AddWithValue("@UsageLimit", model.UsageLimit <= 0 ? 1 : model.UsageLimit);
            col.AddWithValue("@OncePerCustomer", model.OncePerCustomer);
            col.AddWithValue("@AdminNotes", string.IsNullOrWhiteSpace(model.AdminNotes) ? DBNull.Value : model.AdminNotes);

            col.AddWithValue("@ShowSiteBanner", model.ShowSiteBanner);
            col.AddWithValue("@BannerHeadline", string.IsNullOrWhiteSpace(model.BannerHeadline) ? DBNull.Value : model.BannerHeadline.Trim());
            col.AddWithValue("@BannerMessage", string.IsNullOrWhiteSpace(model.BannerMessage) ? DBNull.Value : model.BannerMessage.Trim());
            col.AddWithValue("@BannerLinkText", string.IsNullOrWhiteSpace(model.BannerLinkText) ? DBNull.Value : model.BannerLinkText.Trim());
            col.AddWithValue("@BannerLinkUrl", string.IsNullOrWhiteSpace(model.BannerLinkUrl) ? DBNull.Value : model.BannerLinkUrl.Trim());
            col.AddWithValue("@BannerPriority", Math.Max(0, model.BannerPriority));

            col.AddWithValue("@MatchAllRules", model.MatchAllRules);
            col.AddWithValue("@AutoMaintainEligibility", model.AutoMaintainEligibility);
            col.AddWithValue("@RulesJson", string.IsNullOrWhiteSpace(rulesJson) ? DBNull.Value : rulesJson);
        }

        private static string? BuildRulesJson(AdminDiscountCodeAddRequest model)
        {
            if (!string.Equals(model.AppliesToType, "CollectionRule", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var rules = model.Rules
                .Select((rule, index) => new
                {
                    ruleType = rule.RuleType.Trim(),
                    sourceId = rule.SourceId,
                    ruleValue = rule.RuleValue.Trim(),
                    shopifyTag = ShopifyManagedTagBuilder.BuildManagedTag(rule.RuleType, rule.RuleValue),
                    sortOrder = rule.SortOrder == 0 ? index : rule.SortOrder
                })
                .ToArray();

            return JsonSerializer.Serialize(rules);
        }

        private static void ValidateCollectionRules(AdminDiscountCodeAddRequest model)
        {
            ValidateBanner(model);

            if (!string.Equals(model.AppliesToType, "CollectionRule", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (model.Rules == null || model.Rules.Count == 0)
            {
                throw new ApplicationException("At least one category, condition, make, model, or custom-tag rule is required.");
            }

            string[] allowedRuleTypes = { "Category", "Condition", "Make", "Model", "CustomTag" };

            foreach (AdminDiscountCodeRuleAddRequest rule in model.Rules)
            {
                if (!allowedRuleTypes.Contains(rule.RuleType, StringComparer.OrdinalIgnoreCase))
                {
                    throw new ApplicationException(
                        $"Unsupported collection rule type '{rule.RuleType}'.");
                }

                if (string.IsNullOrWhiteSpace(rule.RuleValue))
                {
                    throw new ApplicationException("Every collection rule requires a value.");
                }
            }

            string[] tags = model.Rules
                .Select(rule => ShopifyManagedTagBuilder.BuildManagedTag(rule.RuleType, rule.RuleValue))
                .ToArray();

            if (tags.Distinct(StringComparer.OrdinalIgnoreCase).Count() != tags.Length)
            {
                throw new ApplicationException("Collection rules cannot produce duplicate Shopify tags.");
            }
        }

        private static void ValidateBanner(AdminDiscountCodeAddRequest model)
        {
            if (!model.ShowSiteBanner)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(model.BannerMessage))
            {
                throw new ApplicationException("A website banner message is required when the sale banner is enabled.");
            }

            string? linkUrl = model.BannerLinkUrl?.Trim();
            string? linkText = model.BannerLinkText?.Trim();

            if (!string.IsNullOrWhiteSpace(linkText) && string.IsNullOrWhiteSpace(linkUrl))
            {
                throw new ApplicationException("BannerLinkUrl is required when BannerLinkText is provided.");
            }

            if (!string.IsNullOrWhiteSpace(linkUrl) &&
                linkUrl.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase))
            {
                throw new ApplicationException("The banner link URL is not allowed.");
            }
        }

        private static AdminDiscountCode MapSingleDiscount(IDataReader reader)
        {
            AdminDiscountCode discount = new();
            int index = 0;

            discount.Id = reader.GetInt32(index++);
            discount.Code = reader.GetSafeString(index++);
            discount.Title = reader.GetSafeString(index++);
            discount.DiscountType = reader.GetSafeString(index++);
            discount.DiscountValue = reader.GetDecimal(index++);
            discount.AppliesToType = reader.GetSafeString(index++);

            discount.PartId = reader.GetSafeInt32Nullable(index++);
            discount.PartName = reader.GetSafeString(index++);
            discount.PartNumber = reader.GetSafeString(index++);
            discount.ShopifyProductId = reader.GetSafeInt64Nullable(index++);
            discount.ShopifyVariantId = reader.GetSafeInt64Nullable(index++);
            discount.CustomerEmail = reader.GetSafeString(index++);
            discount.StartsAtUtc = reader.GetSafeDateTimeNullable(index++);
            discount.EndsAtUtc = reader.GetSafeDateTimeNullable(index++);
            discount.UsageLimit = reader.GetInt32(index++);
            discount.OncePerCustomer = reader.GetBoolean(index++);
            discount.ShopifyDiscountGid = reader.GetSafeString(index++);

            discount.ShopifyCollectionGid = reader.GetSafeString(index++);
            discount.ShopifyCollectionHandle = reader.GetSafeString(index++);
            discount.MatchAllRules = reader.GetBoolean(index++);
            discount.AutoMaintainEligibility = reader.GetBoolean(index++);
            discount.LastCollectionSyncUtc = reader.GetSafeDateTimeNullable(index++);
            discount.LastCollectionSyncStatus = reader.GetSafeString(index++);
            discount.LastCollectionSyncError = reader.GetSafeString(index++);
            discount.RuleCount = reader.GetInt32(index++);
            discount.RuleSummary = reader.GetSafeString(index++);

            discount.Status = reader.GetSafeString(index++);
            discount.UsageCount = reader.GetInt32(index++);
            discount.AdminNotes = reader.GetSafeString(index++);

            discount.ShowSiteBanner = reader.GetBoolean(index++);
            discount.BannerHeadline = reader.GetSafeString(index++);
            discount.BannerMessage = reader.GetSafeString(index++);
            discount.BannerLinkText = reader.GetSafeString(index++);
            discount.BannerLinkUrl = reader.GetSafeString(index++);
            discount.BannerPriority = reader.GetInt32(index++);

            discount.CreatedByUserId = reader.GetSafeInt32Nullable(index++);
            discount.CreatedByName = reader.GetSafeString(index++);
            discount.DeactivatedByUserId = reader.GetSafeInt32Nullable(index++);
            discount.DeactivatedByName = reader.GetSafeString(index++);
            discount.DateCreated = reader.GetDateTime(index++);
            discount.DateModified = reader.GetDateTime(index++);
            discount.DeactivatedDateUtc = reader.GetSafeDateTimeNullable(index++);

            if (reader.FieldCount > index && reader["TotalCount"] != DBNull.Value)
            {
                discount.TotalCount = Convert.ToInt32(reader["TotalCount"]);
            }

            return discount;
        }
    }
}
