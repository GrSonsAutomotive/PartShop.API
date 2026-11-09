using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.ActionConstraints;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Infrastructure;

internal static class ProductionRouteChecks
{
    public static void Verify(IServiceProvider services)
    {
        var actions = services.GetRequiredService<IActionDescriptorCollectionProvider>()
            .ActionDescriptors.Items.OfType<ControllerActionDescriptor>().ToArray();
        var routes = actions.Where(a => a.AttributeRouteInfo?.Template != null)
            .SelectMany(action =>
            {
                // Parameter names do not distinguish HTTP routes. Keep constraints,
                // defaults, optional markers and catch-all shape when comparing.
                var template = Regex.Replace(action.AttributeRouteInfo!.Template!,
                    @"\{(\*{0,2})[^}:?=]+", "{$1").Trim('/').ToLowerInvariant();
                var methods = action.ActionConstraints?.OfType<HttpMethodActionConstraint>()
                    .SelectMany(c => c.HttpMethods).ToArray() ?? Array.Empty<string>();
                return (methods.Length == 0 ? new[] { "ANY" } : methods)
                    .Select(method => new { Template = template, Method = method.ToUpperInvariant(), Action = action });
            }).ToArray();

        foreach (var group in routes.GroupBy(r => r.Template))
        {
            var duplicates = group.GroupBy(r => r.Method).Any(g => g.Count() > 1)
                || (group.Any(r => r.Method == "ANY") && group.Count() > 1);
            if (duplicates)
                throw new Exception("Duplicate HTTP route: " + group.Key + " => "
                    + string.Join(", ", group.Select(r => r.Method + " "
                        + r.Action.ControllerName + "." + r.Action.ActionName)));
        }

        var identity = routes.Where(r => r.Method == "GET"
            && r.Template == "api/shipping-migration/legacy/shop-identity").ToArray();
        if (identity.Length != 1
            || identity[0].Action.ControllerName != "ShippingLegacyRetirementReadiness")
            throw new Exception("The canonical production Shopify identity endpoint changed.");
        if (actions.Any(a => a.ControllerName is "ShopifyDevLegacyRehearsal" or "ShopifyDevStoreIdentity")
            || routes.Any(r => r.Template.StartsWith("api/shipping-migration/dev-rehearsal")))
            throw new Exception("DEV Shopify rehearsal/identity controllers must not enter this release.");

        Console.WriteLine($"PASS production route audit: {routes.Length} method/template registrations; "
            + "no duplicates; canonical identity preserved; DEV controllers excluded.");
    }
}
