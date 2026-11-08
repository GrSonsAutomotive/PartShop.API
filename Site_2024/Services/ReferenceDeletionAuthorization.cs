using Microsoft.AspNetCore.Authorization;

namespace Site_2024.Web.Api.Services;

public static class ReferenceDeletionAuthorization
{
    public static void AddReferenceDeletionPolicy(this AuthorizationOptions options) =>
        options.AddPolicy("ReferenceDataDelete", policy => policy.RequireRole("AdminHigh"));
}
