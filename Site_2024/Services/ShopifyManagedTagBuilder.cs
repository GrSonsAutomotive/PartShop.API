using Site_2024.Web.Api.Models;
using System.Text;

namespace Site_2024.Web.Api.Services
{
    public static class ShopifyManagedTagBuilder
    {
        private const int MaxShopifyTagLength = 255;

        private static readonly string[] ManagedPrefixes =
        {
            "SitePartId_",
            "Category_",
            "Condition_",
            "Make_",
            "Model_"
        };

        public static string BuildManagedTag(string ruleType, string rawValue)
        {
            if (string.IsNullOrWhiteSpace(ruleType))
            {
                throw new ArgumentException("RuleType is required.", nameof(ruleType));
            }

            if (string.IsNullOrWhiteSpace(rawValue))
            {
                throw new ArgumentException("RuleValue is required.", nameof(rawValue));
            }

            string prefix = ruleType.Trim() switch
            {
                "Category" => "Category_",
                "Condition" => "Condition_",
                "Make" => "Make_",
                "Model" => "Model_",
                "CustomTag" => string.Empty,
                _ => throw new ArgumentException(
                    "RuleType must be Category, Condition, Make, Model, or CustomTag.",
                    nameof(ruleType))
            };

            string normalized = NormalizeTagValue(rawValue);
            string tag = string.IsNullOrEmpty(prefix) ? normalized : prefix + normalized;

            return tag.Length <= MaxShopifyTagLength
                ? tag
                : tag[..MaxShopifyTagLength].TrimEnd('_');
        }

        public static string[] BuildManagedTags(Part part)
        {
            if (part == null)
            {
                throw new ArgumentNullException(nameof(part));
            }

            HashSet<string> tags = new(StringComparer.OrdinalIgnoreCase)
            {
                "Site_2024",
                $"SitePartId_{part.Id}"
            };

            AddManagedTag(tags, "Category", part.Catagory?.Name);

            foreach (PartCategory category in part.Categories ?? new List<PartCategory>())
            {
                AddManagedTag(tags, "Category", category.CatagoryName);
            }

            AddManagedTag(tags, "Condition", part.Condition?.Name);
            AddManagedTag(tags, "Make", part.Make?.Company);
            AddManagedTag(tags, "Model", part.Make?.Model?.Name);

            foreach (PartFitment fitment in part.Fitments ?? new List<PartFitment>())
            {
                AddManagedTag(tags, "Make", fitment.Company);
                AddManagedTag(tags, "Model", fitment.ModelName);
            }

            return tags
                .OrderBy(tag => tag, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        public static string[] MergeWithExistingTags(
            IEnumerable<string>? existingTags,
            IEnumerable<string> managedTags)
        {
            HashSet<string> merged = new(StringComparer.OrdinalIgnoreCase);

            foreach (string tag in existingTags ?? Array.Empty<string>())
            {
                if (string.IsNullOrWhiteSpace(tag) || IsManagedTag(tag))
                {
                    continue;
                }

                merged.Add(tag.Trim());
            }

            foreach (string tag in managedTags)
            {
                if (!string.IsNullOrWhiteSpace(tag))
                {
                    merged.Add(tag.Trim());
                }
            }

            return merged
                .OrderBy(tag => tag, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        public static string BuildCollectionHandle(int discountId, string? code)
        {
            string normalizedCode = NormalizeTagValue(code ?? "discount")
                .ToLowerInvariant()
                .Replace('_', '-');

            return $"site-discount-{discountId}-{normalizedCode}";
        }

        private static bool IsManagedTag(string tag)
        {
            string trimmed = tag.Trim();

            if (string.Equals(trimmed, "Site_2024", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return ManagedPrefixes.Any(prefix =>
                trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        }

        private static void AddManagedTag(
            ISet<string> tags,
            string ruleType,
            string? rawValue)
        {
            if (!string.IsNullOrWhiteSpace(rawValue))
            {
                tags.Add(BuildManagedTag(ruleType, rawValue));
            }
        }

        private static string NormalizeTagValue(string value)
        {
            StringBuilder builder = new();
            bool lastWasSeparator = false;

            foreach (char character in value.Trim())
            {
                if (char.IsLetterOrDigit(character))
                {
                    builder.Append(character);
                    lastWasSeparator = false;
                }
                else if (!lastWasSeparator && builder.Length > 0)
                {
                    builder.Append('_');
                    lastWasSeparator = true;
                }
            }

            string normalized = builder.ToString().Trim('_');

            if (string.IsNullOrWhiteSpace(normalized))
            {
                throw new ArgumentException("Tag value must contain at least one letter or number.", nameof(value));
            }

            return normalized;
        }
    }
}
