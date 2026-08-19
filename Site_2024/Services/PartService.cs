using Site_2024.Web.Api.Interfaces;
using Site_2024.Web.Api.Models;
using System.Data;
using System.Linq;
using System.Text.Json;
using Site_2024.Web.Api.Extensions;
using System.Data.SqlClient;
using Site_2024.Web.Api.Constructors;
using Site_2024.Web.Api.Requests;
using Microsoft.Extensions.Options;
using Site_2024.Web.Api.Configurations;
using Microsoft.Extensions.Logging;
using Site_2024.Models.Domain.Parts;
using StaticFileOptions = Site_2024.Web.Api.Configurations.StaticFileOptions;

namespace Site_2024.Web.Api.Services
{
    public class PartService : IPartService
    {
        private readonly IDataProvider _data;
        private readonly StaticFileOptions _staticFileOptions;
        private readonly ILogger _logger;

        public PartService(
            ILogger<PartService> logger,
            IDataProvider data,
            IOptions<StaticFileOptions> staticFileOptions)
        {
            _data = data;
            _staticFileOptions = staticFileOptions.Value;
            _logger = logger;
        }

        #region ---GET---

        public Part GetPartById(int id)
        {
            string procName = "[dbo].[Parts_GetById]";
            Part part = null;

            _data.ExecuteCmd(
                procName,
                inputParamMapper: delegate (SqlParameterCollection col)
                {
                    col.AddWithValue("@Id", id);
                },
                singleRecordMapper: delegate (IDataReader reader, short set)
                {
                    int startingIndex = 0;

                    if (set == 0)
                    {
                        part = MapSinglePart(reader, ref startingIndex);
                    }
                    else if (set == 1 && part != null)
                    {
                        PartCategory category = MapSinglePartCategory(reader, ref startingIndex);
                        part.Categories.Add(category);
                    }
                    else if (set == 2 && part != null)
                    {
                        PartFitment fitment = MapSinglePartFitment(reader, ref startingIndex);
                        part.Fitments.Add(fitment);
                    }
                });

            return part;
        }

        public Part GetPartByIdCustomer(int id)
        {
            string procName = "[dbo].[Parts_GetByIdCustomer]";
            Part part = null;

            _data.ExecuteCmd(
                procName,
                inputParamMapper: delegate (SqlParameterCollection col)
                {
                    col.AddWithValue("@Id", id);
                },
                singleRecordMapper: delegate (IDataReader reader, short set)
                {
                    int startingIndex = 0;

                    if (set == 0)
                    {
                        part = MapSinglePartForCustomer(reader, ref startingIndex);
                    }
                    else if (set == 1 && part != null)
                    {
                        PartCategory category = MapSinglePartCategory(reader, ref startingIndex);
                        part.Categories.Add(category);
                    }
                    else if (set == 2 && part != null)
                    {
                        PartFitment fitment = MapSinglePartFitment(reader, ref startingIndex);
                        part.Fitments.Add(fitment);
                    }
                });

            return part;
        }

        public Paged<PartSummary> GetAllPaginated(int pageIndex, int pageSize)
        {
            string procName = "[dbo].[Parts_GetAllPaginated]";
            Paged<PartSummary> pagedList = null;
            List<PartSummary> list = null;
            int totalCount = 0;

            _data.ExecuteCmd(
                procName,
                inputParamMapper: delegate (SqlParameterCollection col)
                {
                    col.AddWithValue("@PageIndex", pageIndex);
                    col.AddWithValue("@PageSize", pageSize);
                },
                singleRecordMapper: delegate (IDataReader reader, short set)
                {
                    int startingIndex = 0;
                    PartSummary part = MapPartSummary(reader, ref startingIndex);

                    if (totalCount == 0)
                    {
                        totalCount = reader.GetSafeInt32(startingIndex++);
                    }

                    list ??= new List<PartSummary>();
                    list.Add(part);
                });

            if (list != null)
            {
                pagedList = new Paged<PartSummary>(list, pageIndex, pageSize, totalCount);
            }

            return pagedList;
        }

        public Paged<PartSummary> GetAvailablePaginated(int pageIndex, int pageSize, int availableId)
        {
            string procName = "[dbo].[Parts_GetAvailablePaginated]";
            Paged<PartSummary> pagedList = null;
            List<PartSummary> list = null;
            int totalCount = 0;

            _data.ExecuteCmd(
                procName,
                inputParamMapper: delegate (SqlParameterCollection coll)
                {
                    coll.AddWithValue("@PageIndex", pageIndex);
                    coll.AddWithValue("@PageSize", pageSize);
                    coll.AddWithValue("@AvailableId", availableId);
                },
                singleRecordMapper: delegate (IDataReader reader, short set)
                {
                    int i = 0;
                    PartSummary part = MapPartSummary(reader, ref i);

                    if (totalCount == 0)
                    {
                        totalCount = reader.GetSafeInt32(i++);
                    }

                    list ??= new List<PartSummary>();
                    list.Add(part);
                });

            if (list != null)
            {
                pagedList = new Paged<PartSummary>(list, pageIndex, pageSize, totalCount);
            }

            return pagedList;
        }

        public Paged<PartCustomerSummary> GetAvailablePaginatedForCustomer(int pageIndex, int pageSize, int availableId)
        {
            string procName = "[dbo].[Parts_GetAvailablePaginatedCustomer]";
            Paged<PartCustomerSummary> pagedList = null;
            List<PartCustomerSummary> list = null;
            int totalCount = 0;

            _data.ExecuteCmd(
                procName,
                inputParamMapper: delegate (SqlParameterCollection coll)
                {
                    coll.AddWithValue("@PageIndex", pageIndex);
                    coll.AddWithValue("@PageSize", pageSize);
                    coll.AddWithValue("@AvailableId", availableId);
                },
                singleRecordMapper: delegate (IDataReader reader, short set)
                {
                    int i = 0;
                    PartCustomerSummary part = MapPartCustomerSummary(reader, ref i);

                    if (totalCount == 0)
                    {
                        totalCount = reader.GetSafeInt32(i++);
                    }

                    list ??= new List<PartCustomerSummary>();
                    list.Add(part);
                });

            if (list != null)
            {
                pagedList = new Paged<PartCustomerSummary>(list, pageIndex, pageSize, totalCount);
            }

            return pagedList;
        }

        public Paged<PartSummary> GetByModelPaginated(int pageIndex, int pageSize, int modelId)
        {
            string procName = "[dbo].[Parts_GetByModelPaginated]";
            List<PartSummary> list = null;
            int totalCount = 0;

            _data.ExecuteCmd(
                procName,
                inputParamMapper: delegate (SqlParameterCollection col)
                {
                    col.AddWithValue("@PageIndex", pageIndex);
                    col.AddWithValue("@PageSize", pageSize);
                    col.AddWithValue("@ModelId", modelId);
                },
                singleRecordMapper: (reader, set) =>
                {
                    int i = 0;
                    PartSummary part = MapPartSummary(reader, ref i);

                    if (totalCount == 0)
                    {
                        totalCount = reader.GetSafeInt32(i);
                    }

                    list ??= new List<PartSummary>();
                    list.Add(part);
                });

            return list == null ? null : new Paged<PartSummary>(list, pageIndex, pageSize, totalCount);
        }

        public Paged<PartCustomerSummary> GetByModelPaginatedCustomer(int pageIndex, int pageSize, int modelId)
        {
            string procName = "[dbo].[Parts_GetByModelPaginatedCustomer]";
            List<PartCustomerSummary> list = null;
            int totalCount = 0;

            _data.ExecuteCmd(
                procName,
                inputParamMapper: col =>
                {
                    col.AddWithValue("@PageIndex", pageIndex);
                    col.AddWithValue("@PageSize", pageSize);
                    col.AddWithValue("@ModelId", modelId);
                },
                singleRecordMapper: (reader, set) =>
                {
                    int i = 0;
                    PartCustomerSummary part = MapPartCustomerSummary(reader, ref i);

                    if (totalCount == 0)
                    {
                        totalCount = reader.GetSafeInt32(i);
                    }

                    list ??= new List<PartCustomerSummary>();
                    list.Add(part);
                });

            return list == null ? null : new Paged<PartCustomerSummary>(list, pageIndex, pageSize, totalCount);
        }

        public Paged<PartSummary> GetByCategoryPaginated(int pageIndex, int pageSize, int categoryId)
        {
            string procName = "[dbo].[Parts_GetByCatagoryPaginated]";
            List<PartSummary> list = null;
            int totalCount = 0;

            _data.ExecuteCmd(
                procName,
                inputParamMapper: delegate (SqlParameterCollection col)
                {
                    col.AddWithValue("@PageIndex", pageIndex);
                    col.AddWithValue("@PageSize", pageSize);
                    col.AddWithValue("@catagoryId", categoryId);
                },
                singleRecordMapper: (reader, set) =>
                {
                    int i = 0;
                    PartSummary part = MapPartSummary(reader, ref i);

                    if (totalCount == 0)
                    {
                        totalCount = reader.GetSafeInt32(i);
                    }

                    list ??= new List<PartSummary>();
                    list.Add(part);
                });

            return list == null ? null : new Paged<PartSummary>(list, pageIndex, pageSize, totalCount);
        }

        public Paged<PartCustomerSummary> GetByCategoryPaginatedCustomer(int pageIndex, int pageSize, int categoryId)
        {
            string procName = "[dbo].[Parts_GetByCatagoryPaginatedCustomer]";
            List<PartCustomerSummary> list = null;
            int totalCount = 0;

            _data.ExecuteCmd(
                procName,
                inputParamMapper: col =>
                {
                    col.AddWithValue("@PageIndex", pageIndex);
                    col.AddWithValue("@PageSize", pageSize);
                    col.AddWithValue("@catagoryId", categoryId);
                },
                singleRecordMapper: (reader, set) =>
                {
                    int i = 0;
                    PartCustomerSummary part = MapPartCustomerSummary(reader, ref i);

                    if (totalCount == 0)
                    {
                        totalCount = reader.GetSafeInt32(i);
                    }

                    list ??= new List<PartCustomerSummary>();
                    list.Add(part);
                });

            return list == null ? null : new Paged<PartCustomerSummary>(list, pageIndex, pageSize, totalCount);
        }

        public List<PartSearchResult> Search(PartSearchRequest model)
        {
            string procName = "[dbo].[Parts_Search]";
            List<PartSearchResult> list = null;

            _data.ExecuteCmd(
                procName,
                inputParamMapper: delegate (SqlParameterCollection col)
                {
                    col.AddWithValue("@q", (object?)model.q ?? DBNull.Value);
                    col.AddWithValue("@CatagoryId", (object?)model.CatagoryId ?? DBNull.Value);

                    string categoryIdsCsv = string.Join(",",
                        (model.CategoryIds ?? new List<int>())
                            .Where(id => id > 0)
                            .Distinct());
                    string conditionIdsCsv = string.Join(",",
                        (model.ConditionIds ?? new List<int>())
                            .Where(id => id > 0)
                            .Distinct());

                    col.AddWithValue("@CategoryIdsCsv",
                        string.IsNullOrWhiteSpace(categoryIdsCsv) ? DBNull.Value : categoryIdsCsv);
                    col.AddWithValue("@ConditionIdsCsv",
                        string.IsNullOrWhiteSpace(conditionIdsCsv) ? DBNull.Value : conditionIdsCsv);

                    col.AddWithValue("@MakeId", (object?)model.MakeId ?? DBNull.Value);
                    col.AddWithValue("@ModelId", (object?)model.ModelId ?? DBNull.Value);
                    col.AddWithValue("@Year", (object?)model.Year ?? DBNull.Value);
                    col.AddWithValue("@ConditionId", (object?)model.ConditionId ?? DBNull.Value);
                    col.AddWithValue("@AvailableId", (object?)model.AvailableId ?? DBNull.Value);
                    col.AddWithValue("@PriceMin", (object?)model.PriceMin ?? DBNull.Value);
                    col.AddWithValue("@PriceMax", (object?)model.PriceMax ?? DBNull.Value);
                    col.AddWithValue("@SiteId", (object?)model.SiteId ?? DBNull.Value);
                    col.AddWithValue("@BoxId", (object?)model.BoxId ?? DBNull.Value);
                },
                singleRecordMapper: delegate (IDataReader reader, short set)
                {
                    int i = 0;
                    PartSearchResult p = MapPart(reader, ref i);

                    list ??= new List<PartSearchResult>();
                    list.Add(p);
                });

            return list ?? new List<PartSearchResult>();
        }

        public Paged<PartCustomerSummary> SearchCustomer(int pageIndex, int pageSize, CustomerSearchRequest model)
        {
            string procName = "[dbo].[Parts_Search_Customers_Paged]";
            List<PartCustomerSummary> list = null;
            int totalCount = 0;

            _data.ExecuteCmd(
                procName,
                inputParamMapper: col =>
                {
                    col.AddWithValue("@q", (object?)model.q ?? DBNull.Value);
                    col.AddWithValue("@CatagoryId", (object?)model.CatagoryId ?? DBNull.Value);

                    string categoryIdsCsv = string.Join(",",
                        (model.CategoryIds ?? new List<int>())
                            .Where(id => id > 0)
                            .Distinct());
                    string conditionIdsCsv = string.Join(",",
                        (model.ConditionIds ?? new List<int>())
                            .Where(id => id > 0)
                            .Distinct());

                    col.AddWithValue("@CategoryIdsCsv",
                        string.IsNullOrWhiteSpace(categoryIdsCsv) ? DBNull.Value : categoryIdsCsv);
                    col.AddWithValue("@ConditionIdsCsv",
                        string.IsNullOrWhiteSpace(conditionIdsCsv) ? DBNull.Value : conditionIdsCsv);

                    col.AddWithValue("@MakeId", (object?)model.MakeId ?? DBNull.Value);
                    col.AddWithValue("@ModelId", (object?)model.ModelId ?? DBNull.Value);
                    col.AddWithValue("@Year", (object?)model.Year ?? DBNull.Value);
                    col.AddWithValue("@ConditionId", (object?)model.ConditionId ?? DBNull.Value);

                    // Customer-facing search must never expose unavailable listings.
                    // Do not trust or forward an AvailableId supplied by the browser.
                    col.AddWithValue("@AvailableId", 1);

                    col.AddWithValue("@PriceMin", (object?)model.PriceMin ?? DBNull.Value);
                    col.AddWithValue("@PriceMax", (object?)model.PriceMax ?? DBNull.Value);
                    col.AddWithValue("@PageIndex", pageIndex);
                    col.AddWithValue("@PageSize", pageSize);
                },
                singleRecordMapper: (reader, set) =>
                {
                    int i = 0;
                    PartCustomerSummary part = MapPartCustomerSummary(reader, ref i);

                    if (totalCount == 0)
                    {
                        totalCount = reader.GetSafeInt32(i);
                    }

                    list ??= new List<PartCustomerSummary>();
                    list.Add(part);
                });

            // A valid search with no matches should be a successful empty page,
            // not a 404 response. This allows the customer UI to show its
            // normal "No parts found" state.
            return new Paged<PartCustomerSummary>(
                list ?? new List<PartCustomerSummary>(),
                pageIndex,
                pageSize,
                totalCount);
        }

        #endregion

        #region ---POST&PUT---

        public int Insert(PartAddRequest model, int userId)
        {
            int id = 0;
            string procName = "[dbo].[Parts_Insert]";

            _data.ExecuteCmd(
                procName,
                inputParamMapper: col =>
                {
                    model.UserId = userId;

                    NormalizeLegacyFields(model);
                    AddCommonPartsParams(model, col);

                    SqlParameter idOut = new SqlParameter("@Id", SqlDbType.Int);
                    idOut.Direction = ParameterDirection.Output;
                    col.Add(idOut);
                },
                singleRecordMapper: (reader, set) =>
                {
                    id = reader.GetSafeInt32(0);
                });

            SavePartCategories(id, model.Categories);
            SavePartFitments(id, model.Fitments);

            return id;
        }

        public void PatchPart(int id, PartPatchRequest model, int userId)
        {
            const string procName = "[dbo].[Parts_AdminPatch]";

            string? categoriesJson = model.Categories == null
                ? null
                : JsonSerializer.Serialize(model.Categories.Select(category => new
                {
                    category.CatagoryId
                }));

            string? fitmentsJson = model.Fitments == null
                ? null
                : JsonSerializer.Serialize(model.Fitments.Select(fitment => new
                {
                    fitment.MakeId,
                    fitment.YearStart,
                    fitment.YearEnd
                }));

            _data.ExecuteNonQuery(procName, col =>
            {
                col.Add("@Id", SqlDbType.Int).Value = id;
                col.Add("@Name", SqlDbType.NVarChar, 128).Value = (object?)model.Name ?? DBNull.Value;
                col.Add("@PartNumber", SqlDbType.NVarChar, 128).Value = (object?)model.PartNumber ?? DBNull.Value;
                col.Add("@Brand", SqlDbType.NVarChar, 128).Value = (object?)model.Brand ?? DBNull.Value;

                SqlParameter pPrice = col.Add("@Price", SqlDbType.Decimal);
                pPrice.Precision = 18;
                pPrice.Scale = 2;
                pPrice.Value = (object?)model.Price ?? DBNull.Value;

                col.Add("@AvailableId", SqlDbType.Int).Value = (object?)model.AvailableId ?? DBNull.Value;
                col.Add("@Description", SqlDbType.NVarChar, 4000).Value = (object?)model.Description ?? DBNull.Value;
                col.Add("@Image", SqlDbType.NVarChar, 260).Value = (object?)model.Image ?? DBNull.Value;
                col.Add("@Quantity", SqlDbType.Int).Value = (object?)model.Quantity ?? DBNull.Value;
                col.Add("@LocationId", SqlDbType.Int).Value = (object?)model.LocationId ?? DBNull.Value;
                col.Add("@ShippingPolicyId", SqlDbType.Int).Value = (object?)model.ShippingPolicyId ?? DBNull.Value;
                col.Add("@OtherBox", SqlDbType.NVarChar, 100).Value = (object?)model.OtherBox ?? DBNull.Value;
                col.Add("@AdminNotes", SqlDbType.NVarChar, 2000).Value = (object?)model.AdminNotes ?? DBNull.Value;
                col.Add("@Year", SqlDbType.NVarChar, 50).Value = (object?)model.Year ?? DBNull.Value;
                col.Add("@ConditionId", SqlDbType.Int).Value = (object?)model.ConditionId ?? DBNull.Value;
                col.Add("@CategoriesJson", SqlDbType.NVarChar, -1).Value = (object?)categoriesJson ?? DBNull.Value;
                col.Add("@FitmentsJson", SqlDbType.NVarChar, -1).Value = (object?)fitmentsJson ?? DBNull.Value;
                col.Add("@ChangedByUserId", SqlDbType.Int).Value = userId;
            });
        }
        public void UpdateShopifyIds(int partId, long shopifyProductId, long shopifyVariantId, long shopifyInventoryItemId)
        {
            const string procName = "[dbo].[Parts_ShopifyIds_Update]";

            _data.ExecuteNonQuery(procName, col =>
            {
                col.AddWithValue("@Id", partId);
                col.AddWithValue("@ShopifyProductId", shopifyProductId);
                col.AddWithValue("@ShopifyVariantId", shopifyVariantId);
                col.AddWithValue("@ShopifyInventoryItemId", shopifyInventoryItemId);
            });
        }

        #endregion

        #region ---DELETE---

        public void DeletePart(int id)
        {
            string procName = "[dbo].[Parts_Delete]";

            _data.ExecuteNonQuery(
                procName,
                inputParamMapper: delegate (SqlParameterCollection col)
                {
                    col.AddWithValue("@Id", id);
                },
                returnParameters: null);
        }

        #endregion

        #region ---MAPPER---

        private static void AddCommonPartsParams(PartAddRequest model, SqlParameterCollection col)
        {
            col.AddWithValue("@name", model.Name);
            col.AddWithValue("@makeId", model.MakeId);
            col.AddWithValue("@year", (object?)model.Year ?? DBNull.Value);
            col.AddWithValue("@partnumber", model.PartNumber);
            col.AddWithValue("@Brand", (object?)model.Brand ?? DBNull.Value);
            col.AddWithValue("@catagoryId", model.CatagoryId);
            col.AddWithValue("@description", model.Description);
            col.AddWithValue("@price", model.Price);
            col.AddWithValue("@quantity", model.Quantity);
            col.AddWithValue("@conditionId", (object?)model.ConditionId ?? DBNull.Value);
            col.AddWithValue("@shippingPolicyId", model.ShippingPolicyId);
            col.AddWithValue("@locationId", model.LocationId);
            col.AddWithValue("@image", (object?)model.Image ?? DBNull.Value);
            col.AddWithValue("@availableId", model.AvailableId);
            col.AddWithValue("@lastmovedby", model.UserId);
            col.AddWithValue("@OtherBox", (object?)model.OtherBox ?? DBNull.Value);
            col.AddWithValue("@AdminNotes", (object?)model.AdminNotes ?? DBNull.Value);
        }

        private Part MapSinglePart(IDataReader reader, ref int startingIndex)
        {
            Part part = new Part();
            part.Catagory = new Catagory();
            part.Make = new Make();
            part.Make.Model = new Model();
            part.Location = new Location();
            part.Location.Site = new Site();
            part.Location.Area = new Area();
            part.Location.Aisle = new Aisle();
            part.Location.Shelf = new Shelf();
            part.Location.Section = new Section();
            part.Location.Box = new Box();
            part.Available = new Available();
            part.User = new Models.User.User();
            part.Condition = new Condition();
            part.ShippingPolicy = new ShippingPolicy();

            part.Id = reader.GetSafeInt32(startingIndex++);
            part.Name = reader.GetSafeString(startingIndex++);
            part.Catagory.Id = reader.GetSafeInt32(startingIndex++);
            part.Catagory.Name = reader.GetSafeString(startingIndex++);
            part.Make.Id = reader.GetSafeInt32(startingIndex++);
            part.Make.Company = reader.GetSafeString(startingIndex++);
            part.Make.Model.Id = reader.GetSafeInt32(startingIndex++);
            part.Make.Model.Name = reader.GetSafeString(startingIndex++);
            part.Year = reader.GetSafeString(startingIndex++);
            part.PartNumber = reader.GetSafeString(startingIndex++);
            part.Brand = reader.GetSafeString(startingIndex++);
            part.Description = reader.GetSafeString(startingIndex++);
            part.Price = reader.GetSafeDecimal(startingIndex++);
            part.Quantity = reader.GetSafeInt32(startingIndex++);
            part.Condition.Id = reader.GetSafeInt32(startingIndex++);
            part.Condition.Name = reader.GetSafeString(startingIndex++);
            part.ShippingPolicy.Id = reader.GetSafeInt32(startingIndex++);
            part.ShippingPolicy.Name = reader.GetSafeString(startingIndex++);
            part.ShippingPolicy.AllowsOnlineCheckout = reader.GetSafeBool(startingIndex++);
            part.Location.Id = reader.GetSafeInt32(startingIndex++);
            part.Location.Site.Id = reader.GetSafeInt32(startingIndex++);
            part.Location.Site.Name = reader.GetSafeString(startingIndex++);
            part.Location.Area.Id = reader.GetSafeInt32(startingIndex++);
            part.Location.Area.Name = reader.GetSafeString(startingIndex++);
            part.Location.Aisle.Id = reader.GetSafeInt32(startingIndex++);
            part.Location.Aisle.Name = reader.GetSafeString(startingIndex++);
            part.Location.Shelf.Id = reader.GetSafeInt32(startingIndex++);
            part.Location.Shelf.Name = reader.GetSafeString(startingIndex++);
            part.Location.Section.Id = reader.GetSafeInt32(startingIndex++);
            part.Location.Section.Name = reader.GetSafeString(startingIndex++);
            part.Location.Box.Id = reader.GetSafeInt32(startingIndex++);
            part.Location.Box.Name = reader.GetSafeString(startingIndex++);

            string imagePath = reader.GetSafeString(startingIndex++);
            part.Image = string.IsNullOrEmpty(imagePath)
                ? null
                : $"{_staticFileOptions.ImageBaseUrl}{imagePath}";

            part.Available.Id = reader.GetSafeInt32(startingIndex++);
            part.Available.Status = reader.GetSafeString(startingIndex++);
            part.DateCreated = reader.GetSafeDateTime(startingIndex++);
            part.DateModified = reader.GetSafeDateTime(startingIndex++);
            part.User.Id = reader.GetSafeInt32(startingIndex++);
            part.User.Name = reader.GetSafeString(startingIndex++);
            part.OtherBox = reader.GetSafeString(startingIndex++);
            part.AdminNotes = reader.GetSafeString(startingIndex++);
            part.ShopifyProductId = reader.GetSafeInt64Nullable(startingIndex++);
            part.ShopifyVariantId = reader.GetSafeInt64Nullable(startingIndex++);
            part.ShopifyInventoryItemId = reader.GetSafeInt64Nullable(startingIndex++);

            part.Categories = new List<PartCategory>();
            part.Fitments = new List<PartFitment>();

            return part;
        }

        private Part MapSinglePartForCustomer(IDataReader reader, ref int startingIndex)
        {
            Part part = new Part();
            part.Catagory = new Catagory();
            part.Make = new Make();
            part.Make.Model = new Model();
            part.Available = new Available();
            part.Condition = new Condition();
            part.ShippingPolicy = new ShippingPolicy();

            part.Id = reader.GetSafeInt32(startingIndex++);
            part.Name = reader.GetSafeString(startingIndex++);
            part.Catagory.Id = reader.GetSafeInt32(startingIndex++);
            part.Catagory.Name = reader.GetSafeString(startingIndex++);
            part.Make.Id = reader.GetSafeInt32(startingIndex++);
            part.Make.Company = reader.GetSafeString(startingIndex++);
            part.Make.Model.Id = reader.GetSafeInt32(startingIndex++);
            part.Make.Model.Name = reader.GetSafeString(startingIndex++);
            part.Year = reader.GetSafeString(startingIndex++);
            part.PartNumber = reader.GetSafeString(startingIndex++);
            part.Brand = reader.GetSafeString(startingIndex++);
            part.Description = reader.GetSafeString(startingIndex++);
            part.Price = reader.GetSafeDecimal(startingIndex++);
            part.Quantity = reader.GetSafeInt32(startingIndex++);
            part.Condition.Id = reader.GetSafeInt32(startingIndex++);
            part.Condition.Name = reader.GetSafeString(startingIndex++);
            part.ShippingPolicy.Id = reader.GetSafeInt32(startingIndex++);
            part.ShippingPolicy.Name = reader.GetSafeString(startingIndex++);
            part.ShippingPolicy.AllowsOnlineCheckout = reader.GetSafeBool(startingIndex++);

            string imagePath = reader.GetSafeString(startingIndex++);
            part.Image = string.IsNullOrEmpty(imagePath)
                ? null
                : $"{_staticFileOptions.ImageBaseUrl}{imagePath}";

            part.Available.Id = reader.GetSafeInt32(startingIndex++);
            part.Available.Status = reader.GetSafeString(startingIndex++);
            part.QuantitySold = Convert.ToInt64(reader.GetValue(startingIndex++));

            part.Categories = new List<PartCategory>();
            part.Fitments = new List<PartFitment>();

            return part;
        }

        private PartSearchResult MapPart(IDataReader reader, ref int startingIndex)
        {
            PartSearchResult p = new PartSearchResult();

            p.Id = reader.GetSafeInt32(startingIndex++);
            p.Name = reader.GetSafeString(startingIndex++);
            p.CatagoryId = reader.GetSafeInt32(startingIndex++);
            p.CatagoryName = reader.GetSafeString(startingIndex++);
            p.MakeId = reader.GetSafeInt32(startingIndex++);
            p.MakeName = reader.GetSafeString(startingIndex++);
            p.ModelId = reader.GetSafeInt32(startingIndex++);
            p.ModelName = reader.GetSafeString(startingIndex++);
            p.Year = reader.GetSafeString(startingIndex++);
            p.PartNumber = reader.GetSafeString(startingIndex++);
            p.Description = reader.GetSafeString(startingIndex++);
            p.Price = reader.GetSafeDecimal(startingIndex++);
            p.Quantity = reader.GetSafeInt32(startingIndex++);
            p.ConditionId = reader.GetSafeInt32(startingIndex++);
            p.ConditionName = reader.GetSafeString(startingIndex++);

            string imagePath = reader.GetSafeString(startingIndex++);
            p.Image = string.IsNullOrEmpty(imagePath)
                ? null
                : $"{_staticFileOptions.ImageBaseUrl}{imagePath}";

            p.AvailableId = reader.GetSafeInt32(startingIndex++);
            p.AvailableStatus = reader.GetSafeString(startingIndex++);
            p.SiteId = reader.GetSafeInt32(startingIndex++);
            p.SiteName = reader.GetSafeString(startingIndex++);
            p.BoxId = reader.GetSafeInt32(startingIndex++);
            p.BoxName = reader.GetSafeString(startingIndex++);
            p.OtherBox = reader.GetSafeString(startingIndex++);
            p.AdminNotes = reader.GetSafeString(startingIndex++);
            p.DateCreated = reader.GetSafeDateTime(startingIndex++);
            p.DateModified = reader.GetSafeDateTime(startingIndex++);

            return p;
        }

        private PartSummary MapPartSummary(IDataReader reader, ref int startingIndex)
        {
            PartSummary part = new PartSummary();
            part.Catagory = new Catagory();
            part.Make = new Make();
            part.Make.Model = new Model();
            part.Available = new Available();
            part.Condition = new Condition();
            part.ShippingPolicy = new ShippingPolicy();

            part.Id = reader.GetSafeInt32(startingIndex++);
            part.Name = reader.GetSafeString(startingIndex++);
            part.Catagory.Id = reader.GetSafeInt32(startingIndex++);
            part.Catagory.Name = reader.GetSafeString(startingIndex++);
            part.Make.Id = reader.GetSafeInt32(startingIndex++);
            part.Make.Company = reader.GetSafeString(startingIndex++);
            part.Make.Model.Id = reader.GetSafeInt32(startingIndex++);
            part.Make.Model.Name = reader.GetSafeString(startingIndex++);
            part.Year = reader.GetSafeString(startingIndex++);
            part.PartNumber = reader.GetSafeString(startingIndex++);
            part.Description = reader.GetSafeString(startingIndex++);
            part.Price = reader.GetSafeDecimal(startingIndex++);
            part.Quantity = reader.GetSafeInt32(startingIndex++);
            part.Condition.Id = reader.GetSafeInt32(startingIndex++);
            part.Condition.Name = reader.GetSafeString(startingIndex++);
            part.ShippingPolicy.Id = reader.GetSafeInt32(startingIndex++);
            part.ShippingPolicy.Name = reader.GetSafeString(startingIndex++);

            string imagePath = reader.GetSafeString(startingIndex++);
            part.Image = string.IsNullOrEmpty(imagePath)
                ? null
                : $"{_staticFileOptions.ImageBaseUrl}{imagePath}";

            part.Available.Id = reader.GetSafeInt32(startingIndex++);
            part.Available.Status = reader.GetSafeString(startingIndex++);
            part.DateCreated = reader.GetSafeDateTime(startingIndex++);
            part.DateModified = reader.GetSafeDateTime(startingIndex++);

            return part;
        }

        private PartCustomerSummary MapPartCustomerSummary(IDataReader reader, ref int startingIndex)
        {
            PartCustomerSummary part = new PartCustomerSummary();
            part.Catagory = new Catagory();
            part.Make = new Make();
            part.Make.Model = new Model();
            part.Available = new Available();
            part.Condition = new Condition();

            part.Id = reader.GetSafeInt32(startingIndex++);
            part.Name = reader.GetSafeString(startingIndex++);
            part.Catagory.Id = reader.GetSafeInt32(startingIndex++);
            part.Catagory.Name = reader.GetSafeString(startingIndex++);
            part.Make.Id = reader.GetSafeInt32(startingIndex++);
            part.Make.Company = reader.GetSafeString(startingIndex++);
            part.Make.Model.Id = reader.GetSafeInt32(startingIndex++);
            part.Make.Model.Name = reader.GetSafeString(startingIndex++);
            part.Year = reader.GetSafeString(startingIndex++);
            part.PartNumber = reader.GetSafeString(startingIndex++);
            part.Description = reader.GetSafeString(startingIndex++);
            part.Price = reader.GetSafeDecimal(startingIndex++);
            part.Quantity = reader.GetSafeInt32(startingIndex++);
            part.Condition.Id = reader.GetSafeInt32(startingIndex++);
            part.Condition.Name = reader.GetSafeString(startingIndex++);

            string imagePath = reader.GetSafeString(startingIndex++);
            part.Image = string.IsNullOrEmpty(imagePath)
                ? null
                : $"{_staticFileOptions.ImageBaseUrl}{imagePath}";

            part.Available.Id = reader.GetSafeInt32(startingIndex++);
            part.Available.Status = reader.GetSafeString(startingIndex++);
            part.DateCreated = reader.GetSafeDateTime(startingIndex++);
            part.DateModified = reader.GetSafeDateTime(startingIndex++);

            return part;
        }

        private static PartCategory MapSinglePartCategory(IDataReader reader, ref int startingIndex)
        {
            PartCategory category = new PartCategory();

            category.Id = reader.GetSafeInt32(startingIndex++);
            category.PartId = reader.GetSafeInt32(startingIndex++);
            category.CatagoryId = reader.GetSafeInt32(startingIndex++);
            category.CatagoryName = reader.GetSafeString(startingIndex++);

            return category;
        }

        private static PartFitment MapSinglePartFitment(IDataReader reader, ref int startingIndex)
        {
            PartFitment fitment = new PartFitment();

            fitment.Id = reader.GetSafeInt32(startingIndex++);
            fitment.PartId = reader.GetSafeInt32(startingIndex++);
            fitment.MakeId = reader.GetSafeInt32(startingIndex++);
            fitment.Company = reader.GetSafeString(startingIndex++);
            fitment.ModelId = reader.GetSafeInt32(startingIndex++);
            fitment.ModelName = reader.GetSafeString(startingIndex++);
            fitment.YearStart = reader.GetSafeInt32Nullable(startingIndex++);
            fitment.YearEnd = reader.GetSafeInt32Nullable(startingIndex++);

            return fitment;
        }

        #endregion

        #region ---HELPERS---

        private void SavePartCategories(int partId, IEnumerable<PartCategoryAddRequest> categories)
        {
            if (categories == null)
            {
                return;
            }

            foreach (PartCategoryAddRequest category in categories)
            {
                _data.ExecuteNonQuery("[dbo].[PartCategories_Insert]", col =>
                {
                    col.AddWithValue("@PartId", partId);
                    col.AddWithValue("@CatagoryId", category.CatagoryId);

                    SqlParameter idOut = new SqlParameter("@Id", SqlDbType.Int);
                    idOut.Direction = ParameterDirection.Output;
                    col.Add(idOut);
                });
            }
        }

        private void SavePartFitments(int partId, IEnumerable<PartFitmentAddRequest> fitments)
        {
            if (fitments == null)
            {
                return;
            }

            foreach (PartFitmentAddRequest fitment in fitments)
            {
                _data.ExecuteNonQuery("[dbo].[PartFitments_Insert]", col =>
                {
                    col.AddWithValue("@PartId", partId);
                    col.AddWithValue("@MakeId", fitment.MakeId);
                    col.AddWithValue("@YearStart", (object?)fitment.YearStart ?? DBNull.Value);
                    col.AddWithValue("@YearEnd", (object?)fitment.YearEnd ?? DBNull.Value);

                    SqlParameter idOut = new SqlParameter("@Id", SqlDbType.Int);
                    idOut.Direction = ParameterDirection.Output;
                    col.Add(idOut);
                });
            }
        }

        private static bool TryParseYearRange(string? value, out int yearStart, out int yearEnd)
        {
            yearStart = 0;
            yearEnd = 0;

            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            string normalized = value
                .Trim()
                .Replace("–", "-")
                .Replace("—", "-");

            string[] pieces = normalized.Split(
                '-',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            if (pieces.Length == 1 && int.TryParse(pieces[0], out yearStart))
            {
                yearEnd = yearStart;
                return true;
            }

            if (pieces.Length == 2 &&
                int.TryParse(pieces[0], out yearStart) &&
                int.TryParse(pieces[1], out yearEnd) &&
                yearStart <= yearEnd)
            {
                return true;
            }

            yearStart = 0;
            yearEnd = 0;
            return false;
        }

        private static string? FormatYearRange(int? yearStart, int? yearEnd)
        {
            if (!yearStart.HasValue || !yearEnd.HasValue)
            {
                return null;
            }

            return yearStart.Value == yearEnd.Value
                ? yearStart.Value.ToString()
                : $"{yearStart.Value} - {yearEnd.Value}";
        }

        private static void NormalizeLegacyFields(PartAddRequest model)
        {
            if ((model.Categories == null || model.Categories.Count == 0) && model.CatagoryId > 0)
            {
                model.Categories = new List<PartCategoryAddRequest>
                {
                    new PartCategoryAddRequest { CatagoryId = model.CatagoryId }
                };
            }
            else if (model.Categories != null && model.Categories.Count > 0)
            {
                model.CatagoryId = model.Categories[0].CatagoryId;
            }

            if ((model.Fitments == null || model.Fitments.Count == 0) && model.MakeId > 0)
            {
                int? yearStart = null;
                int? yearEnd = null;

                if (TryParseYearRange(model.Year, out int parsedYearStart, out int parsedYearEnd))
                {
                    yearStart = parsedYearStart;
                    yearEnd = parsedYearEnd;
                }

                model.Fitments = new List<PartFitmentAddRequest>
                {
                    new PartFitmentAddRequest
                    {
                        MakeId = model.MakeId,
                        YearStart = yearStart,
                        YearEnd = yearEnd
                    }
                };

                model.Year = FormatYearRange(yearStart, yearEnd);
            }
            else if (model.Fitments != null && model.Fitments.Count > 0)
            {
                PartFitmentAddRequest primaryFitment = model.Fitments[0];
                model.MakeId = primaryFitment.MakeId;
                model.Year = FormatYearRange(
                    primaryFitment.YearStart,
                    primaryFitment.YearEnd);
            }
        }

        public Paged<PartSummary> GetPartsPaginated(int pageIndex, int pageSize)
        {
            return GetAllPaginated(pageIndex, pageSize);
        }

        #endregion
    }
}