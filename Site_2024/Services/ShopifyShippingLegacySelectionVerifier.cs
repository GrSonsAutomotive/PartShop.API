using Site_2024.Web.Api.Models;

namespace Site_2024.Web.Api.Services
{
    /// <summary>
    /// Full, fail-closed READ-ONLY membership verification for an individual part.
    /// In Shopify Markets, changing a product tag does not remove historical
    /// manually selected VARIANTS from migrated shipping collections.
    /// This verifier must be run BEFORE a queue job is created and AGAIN by
    /// the worker before making Shopify writes.
    /// </summary>
    public sealed class ShopifyShippingLegacySelectionVerifier
    {
        private readonly IShopifyAdminService _shopify;

        public ShopifyShippingLegacySelectionVerifier(IShopifyAdminService shopify)
        {
            _shopify = shopify;
        }

        public async Task RequireTagOnlyMembershipAsync(
            IReadOnlyCollection<ShippingPolicy> policies, string productGid,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(productGid) ||
                !productGid.StartsWith("gid://shopify/Product/", StringComparison.Ordinal))
                throw new InvalidOperationException("Invalid Shopify product identity.");

            var checkout = policies.Where(p => p.IsActive && p.AllowsOnlineCheckout).ToList();
            if (checkout.Count == 0 ||
                checkout.Any(p => string.IsNullOrWhiteSpace(p.ShopifyShippingCollectionGid)))
                throw new InvalidOperationException(
                    "All active checkout shipping policies must have mapped collections.");

            foreach (var group in checkout.GroupBy(
                p => p.ShopifyShippingCollectionGid!, StringComparer.Ordinal))
            {
                ct.ThrowIfCancellationRequested();
                ShopifyShippingCollectionInfo? collection =
                    await _shopify.GetShippingCollectionAsync(group.Key);

                if (collection == null ||
                    collection.CollectionGid != group.Key ||
                    collection.Sources.Count == 0)
                    throw new InvalidOperationException(
                        "Mapped shipping collection could not be verified.");

                foreach (ShopifyShippingCollectionSourceInfo source in collection.Sources)
                {
                    ct.ThrowIfCancellationRequested();
                    if (source.SourceType != "CollectionConditionsSource" ||
                        source.HasExclusions)
                        throw new InvalidOperationException(
                            "Unknown or excluded Shopify shipping source. Reassignment blocked.");

                    if (!source.HasExplicitSelections)
                    {
                        // Only a single automatic product-tag rule is an accepted
                        // source for this FIRST production reassignment rollout.
                        if (source.TargetType != "PRODUCTS" ||
                            source.InclusionConditionCount != 1 ||
                            source.TagConditions.Count != 1 ||
                            source.TagConditions[0].Relation != "TAGGED_WITH" ||
                            source.TagConditions[0].MatchType != "ANY" ||
                            source.TagConditions[0].Values.Count != 1 ||
                            !group.Any(p => string.Equals(
                                source.TagConditions[0].Values[0],
                                $"ShippingClass_{p.Id}",
                                StringComparison.OrdinalIgnoreCase)))
                            throw new InvalidOperationException(
                                "Unrecognized automatic Shopify shipping source.");
                        continue;
                    }

                    // A mixed source or any source with unexpected conditions
                    // cannot safely be moved by replacing only the product tag.
                    if (source.TargetType != "VARIANTS" ||
                        source.InclusionConditionCount != 0)
                        throw new InvalidOperationException(
                            "Unsupported Shopify manual selection source; reassignment blocked.");

                    string? cursor = null;
                    var seenCursors = new HashSet<string>(StringComparer.Ordinal);
                    bool completed = false;

                    // Shopify returns up to 100 selections per page. A bound is
                    // deliberate: >5,000 requires a separately reviewed plan.
                    for (int pageNumber = 0; pageNumber < 50; pageNumber++)
                    {
                        ct.ThrowIfCancellationRequested();
                        ShopifyShippingMembershipPage? page =
                            await _shopify.GetShippingMembershipPageAsync(
                                group.Key, source.SourceGid, "selections", cursor);

                        if (page == null || !page.ReadOnly ||
                            page.Kind != "selections" ||
                            page.CollectionGid != group.Key ||
                            page.SourceGid != source.SourceGid ||
                            page.Selections.Count != page.ProductGids.Count)
                            throw new InvalidOperationException(
                                "Legacy Shopify selections could not be exhaustively audited.");

                        if (page.ProductGids.Contains(productGid))
                            throw new InvalidOperationException(
                                "Part has a legacy explicitly selected Shopify variant. " +
                                "Automatic tag-only shipping reassignment is not safe for this part.");

                        if (!page.HasNextPage)
                        {
                            completed = true;
                            break;
                        }
                        if (string.IsNullOrWhiteSpace(page.EndCursor) ||
                            !seenCursors.Add(page.EndCursor))
                            throw new InvalidOperationException(
                                "Invalid or repeated Shopify source selections cursor.");
                        cursor = page.EndCursor;
                    }

                    if (!completed)
                        throw new InvalidOperationException(
                            "Legacy Shopify source exceeded exhaustive selection scan limit.");
                }
            }
        }
    }
}
