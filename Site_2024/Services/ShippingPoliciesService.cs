using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using Site_2024.Web.Api.Data;
using Site_2024.Web.Api.Interfaces;
using Site_2024.Web.Api.Models;
using Site_2024.Web.Api.Requests.ShippingPolicies;
using Site_2024.Web.Api.Extensions;

namespace Site_2024.Web.Api.Services
{
    public class ShippingPoliciesService : IShippingPoliciesService
    {
        private readonly IDataProvider _data;

        public ShippingPoliciesService(IDataProvider data)
        {
            _data = data;
        }

        public int Add(ShippingPolicyAddRequest model, int userId)
        {
            int id = 0;
            string procName = "[dbo].[ShippingPolicies_Insert]";

            _data.ExecuteNonQuery(procName,
                inputParamMapper: col =>
                {
                    col.AddWithValue("@Name", model.Name);
                    col.AddWithValue("@ShopifyProfileId", (object)model.ShopifyProfileId ?? DBNull.Value);
                    col.AddWithValue("@IsActive", model.IsActive);

                    SqlParameter idOut = new SqlParameter("@Id", SqlDbType.Int) { Direction = ParameterDirection.Output };
                    col.Add(idOut);
                },
                returnParameters: col =>
                {
                    id = (int)col["@Id"].Value;
                });

            return id;
        }

        public List<ShippingPolicy> GetAll()
        {
            string procName = "[dbo].[ShippingPolicies_SelectAll]";
            List<ShippingPolicy> list = null;

            _data.ExecuteCmd(procName,
                inputParamMapper: null,
                singleRecordMapper: (reader, set) =>
                {
                    int i = 0;
                    ShippingPolicy policy = new ShippingPolicy();

                    policy.Id = reader.GetSafeInt32(i++);
                    policy.Name = reader.GetSafeString(i++);
                    policy.ShopifyProfileId = reader.GetSafeInt64Nullable(i++);
                    policy.IsActive = reader.GetSafeBool(i++);
                    policy.AllowsOnlineCheckout = reader.GetSafeBool(i++);
                    policy.DateCreated = reader.GetSafeDateTime(i++);
                    policy.DateModified = reader.GetSafeDateTime(i++);

                    list ??= new List<ShippingPolicy>();
                    list.Add(policy);
                });

            return list;
        }

        public void UpdateShopifyProfileId(int id, long shopifyProfileId)
        {
            _data.ExecuteNonQuery(
                "[dbo].[ShippingPolicies_UpdateShopifyProfileId]",
                inputParamMapper: col =>
                {
                    col.AddWithValue("@Id", id);
                    col.AddWithValue("@ShopifyProfileId", shopifyProfileId);
                });
        }
    }
}

