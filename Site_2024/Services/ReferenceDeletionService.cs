using System.Data;
using System.Data.SqlClient;
using Site_2024.Web.Api.Interfaces;

namespace Site_2024.Web.Api.Services;

public sealed class ReferenceDeletionResult
{
    public bool Exists { get; set; }
    public int PrimaryParts { get; set; }
    public int Categories { get; set; }
    public int Fitments { get; set; }
    public int Makes { get; set; }
    public int DiscountRules { get; set; }
    public int TargetRows { get; set; }
    public bool Deleted { get; set; }
    public bool CanDelete => Exists && PrimaryParts + Categories + Fitments + Makes + DiscountRules == 0;
}

public sealed class ReferenceDeletionService
{
    private readonly IDataProvider data;
    public ReferenceDeletionService(IDataProvider data) => this.data = data;

    public ReferenceDeletionResult Execute(string kind, int? id, string? company, bool delete)
    {
        ReferenceDeletionResult? result = null;
        try
        {
            data.ExecuteCmd("dbo.ReferenceData_DeleteSafe", p =>
            {
                p.Add("@Kind", SqlDbType.VarChar, 20).Value = kind;
                p.Add("@Id", SqlDbType.Int).Value = (object?)id ?? DBNull.Value;
                p.Add("@Company", SqlDbType.NVarChar, 128).Value = (object?)company ?? DBNull.Value;
                p.Add("@Delete", SqlDbType.Bit).Value = delete;
            }, (r, _) => result = new ReferenceDeletionResult
            {
                Exists = r.GetBoolean(0), PrimaryParts = r.GetInt32(1), Categories = r.GetInt32(2),
                Fitments = r.GetInt32(3), Makes = r.GetInt32(4), DiscountRules = r.GetInt32(5),
                TargetRows = r.GetInt32(6), Deleted = r.GetBoolean(7)
            });
        }
        catch (SqlException exception) when (exception.Number == 1205)
        {
            // SQL Server has rolled back the deadlock victim. Do not automatically
            // retry an administrative DELETE or report dependency counts as current.
            throw new ReferenceDeletionBusyException(exception);
        }
        return result ?? throw new InvalidOperationException("Deletion procedure returned no result.");
    }
}

public sealed class ReferenceDeletionBusyException : Exception
{
    public ReferenceDeletionBusyException(Exception inner)
        : base("Deletion was rolled back because of a concurrent operation. Refresh dependencies and try again.", inner) { }
}
