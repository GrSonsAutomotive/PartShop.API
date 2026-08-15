using Site_2024.Web.Api.Interfaces;
using Site_2024.Web.Api.Models;
using System.Data;
using System.Linq;
using Site_2024.Web.Api.Extensions;
using System.Data.SqlClient;
using Site_2024.Web.Api.Constructors;
using Site_2024.Web.Api.Requests;

namespace Site_2024.Web.Api.Services
{
    public class MakeService : IMakeService
    {
        private IDataProvider _data;

        public MakeService(IDataProvider data)
        {
            _data = data;
        }

        #region ---GET---

        public Make GetMakeById(int id)
        {
            string procName = "[dbo].[Make_GetById]";
            Make? make = null;

            _data.ExecuteCmd(procName,
                inputParamMapper: delegate (SqlParameterCollection col)
                {
                    col.AddWithValue("@Id", id);
                }, delegate (IDataReader reader, short set)
                {
                    int startingIndex = 0;
                    make = MapSingleMake(reader, ref startingIndex);
                });

            return make;
        }

        public List<Make> GetMakesAll()
        {
            string procName = "[dbo].[Make_GetAll]";

            List<Make> list = new List<Make>();

            _data.ExecuteCmd(
                procName,
                inputParamMapper: delegate (SqlParameterCollection coll)
                {
                    // No input parameters, but can be used if needed in the future
                },
                singleRecordMapper: delegate (IDataReader reader, short set)
                {
                    int i = 0;
                    Make make = MapSingleMake(reader, ref i);
                    list.Add(make); // Add the Make to the list
                });

            return list;
        }

        public List<Make> GetMakesAllCompanies()
        {
            string procName = "[dbo].[Make_GetAllCompany]";

            List<Make> list = new List<Make>();

            _data.ExecuteCmd(
                procName,
                inputParamMapper: delegate (SqlParameterCollection coll)
                {
                    // No input parameters, but can be used if needed in the future
                },
                singleRecordMapper: delegate (IDataReader reader, short set)
                {
                    int i = 0;
                    Make make = MapMakeForDisplay(reader, ref i);
                    list.Add(make); // Add the Make to the list
                });

            return list;
        }

        #endregion

        #region ---POST&PUT---

        public int AddMake(MakeAddRequest make)
        {
            int id = 0;

            string procName = "[dbo].[Make_Insert]";

            _data.ExecuteNonQuery(procName
            , inputParamMapper: delegate (SqlParameterCollection col)
            {
                AddCommonMakeParams(make, col);

                SqlParameter idOut = new SqlParameter("@Id", SqlDbType.Int);
                idOut.Direction = ParameterDirection.Output;

                col.Add(idOut);

            }
            , returnParameters: delegate (SqlParameterCollection returnCollection)
            {
                object oId = returnCollection["@Id"].Value;

                int.TryParse(oId.ToString(), out id);

            });

            return id;
        }

        public int AddMakeModel(MakeModelAdminCreateRequest model)
        {
            int makeId = 0;
            const string procName = "[dbo].[MakeModel_AdminCreate]";

            _data.ExecuteNonQuery(
                procName,
                inputParamMapper: delegate (SqlParameterCollection col)
                {
                    col.Add("@Company", SqlDbType.NVarChar, 128).Value = model.Company.Trim();
                    col.Add("@ModelName", SqlDbType.NVarChar, 128).Value = model.ModelName.Trim();

                    SqlParameter makeIdOut = new SqlParameter("@MakeId", SqlDbType.Int)
                    {
                        Direction = ParameterDirection.Output
                    };
                    col.Add(makeIdOut);
                },
                returnParameters: delegate (SqlParameterCollection returnCollection)
                {
                    object value = returnCollection["@MakeId"].Value;
                    int.TryParse(value?.ToString(), out makeId);
                });

            return makeId;
        }

        public void UpdateMake(MakeUpdateRequest make)
        {
            string procName = "[dbo].[Make_Update]";
            _data.ExecuteNonQuery(procName
            , inputParamMapper: delegate (SqlParameterCollection col)
            {
                AddCommonMakeParams(make, col);
                col.AddWithValue("@Id", make.Id);
            }
            , returnParameters: null);
        }

        #endregion

        #region ---DELETE---

        public void DeleteMake(int id)
        {
            string procName = "[dbo].[Make_Delete]";
            _data.ExecuteNonQuery(procName
            , inputParamMapper: delegate (SqlParameterCollection col)
            {
                col.AddWithValue("@Id", id);
            }
            , returnParameters: null);
        }

        #endregion

        #region ---MAPPERS---

        private static void AddCommonMakeParams(MakeAddRequest make, SqlParameterCollection col)
        {
            col.AddWithValue("@company", make.Company);
            col.AddWithValue("@modelId", make.ModelId);
        }
        
        private Make MapMakeForDisplay(IDataReader reader, ref int startingIndex)
        {
            Make make = new Make();
            make.Id = reader.GetSafeInt32(startingIndex++);
            make.Company = reader.GetString(startingIndex++);

            return make;
        }

        private Make MapSingleMake(IDataReader reader, ref int startingIndex)
        {
            Make make = new Make();
            make.Model = new Model();

            make.Id = reader.GetSafeInt32(startingIndex++);
            make.Company = reader.GetSafeString(startingIndex++);
            make.Model.Id = reader.GetSafeInt32(startingIndex++);
            make.Model.Name = reader.GetSafeString(startingIndex++);

            return make;
        }

        #endregion
    }
}
