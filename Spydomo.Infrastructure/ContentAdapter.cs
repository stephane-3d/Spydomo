using Spydomo.Common.Enums;
using Spydomo.Infrastructure.Interfaces;
using Spydomo.Models;
using System.Text.Json;

namespace Spydomo.Infrastructure
{
    public class ContentAdapter : IContentAdapter
    {
        public string GetCanonicalText(RawContent content)
        {
            if (string.IsNullOrWhiteSpace(content.Content))
                return string.Empty;

            var typeId = content.DataSourceTypeId;

            // Plain text types (skip JSON parsing)
            if ((DataSourceTypeEnum)typeId == DataSourceTypeEnum.CompanyContent ||
                (DataSourceTypeEnum)typeId == DataSourceTypeEnum.Blog ||
                 (DataSourceTypeEnum)typeId == DataSourceTypeEnum.News)
            {
                return content.Content;
            }

            // Try parsing as JSON for the rest
            try
            {
                using var doc = JsonDocument.Parse(content.Content);

                return (DataSourceTypeEnum)typeId switch
                {
                    DataSourceTypeEnum.Capterra => ExtractCapterra(doc.RootElement),
                    DataSourceTypeEnum.G2 => ExtractG2(doc.RootElement),
                    DataSourceTypeEnum.Linkedin => ExtractLinkedIn(doc.RootElement),
                    DataSourceTypeEnum.Reddit => ExtractReddit(doc.RootElement),
                    DataSourceTypeEnum.Instagram => ExtractInstagram(doc.RootElement),
                    DataSourceTypeEnum.Facebook => ExtractFacebook(doc.RootElement),
                    DataSourceTypeEnum.FacebookReviews => ExtractFacebook(doc.RootElement),
                    _ => content.Content
                };
            }
            catch (Exception ex)
            {
                throw new Exception($"ContentAdapter - GetCanonicalText - Failed to process RawContent ID {content.Id} Message: {ex.Message}. Stack: {ex.StackTrace}");
            }
        }


        private string ExtractCapterra(JsonElement root)
        {
            var parts = new List<string>();

            if (root.TryGetProperty("Text", out var textBlock) && textBlock.ValueKind == JsonValueKind.Object)
            {
                if (textBlock.TryGetProperty("title", out var title))
                    parts.Add(title.GetString());

                if (textBlock.TryGetProperty("overall", out var overall))
                    parts.Add(overall.GetString());

                if (textBlock.TryGetProperty("pros", out var pros) && !string.IsNullOrWhiteSpace(pros.GetString()))
                    parts.Add("Pros: " + pros.GetString());

                if (textBlock.TryGetProperty("cons", out var cons) && !string.IsNullOrWhiteSpace(cons.GetString()))
                    parts.Add("Cons: " + cons.GetString());

                if (textBlock.TryGetProperty("alternativesConsidered", out var alt) && !string.IsNullOrWhiteSpace(alt.GetString()))
                    parts.Add("Alternatives considered: " + alt.GetString());

                if (textBlock.TryGetProperty("reasonsForChoosing", out var reasons) && !string.IsNullOrWhiteSpace(reasons.GetString()))
                    parts.Add("Reason for choosing: " + reasons.GetString());
            }

            return string.Join("\n\n", parts.Where(p => !string.IsNullOrWhiteSpace(p)));
        }



        private string ExtractG2(JsonElement root)
        {
            if (root.TryGetProperty("Text", out var textProp) && textProp.ValueKind == JsonValueKind.String)
            {
                return textProp.GetString()?.Trim() ?? string.Empty;
            }
            return string.Empty;
        }

        private string ExtractLinkedIn(JsonElement root)
        {
            var parts = new List<string>();

            if (root.TryGetProperty("Title", out var title)) parts.Add(title.GetString());
            if (root.TryGetProperty("Text", out var text)) parts.Add(text.GetString());

            return string.Join("\n", parts.Where(p => !string.IsNullOrWhiteSpace(p)));
        }

        private string ExtractReddit(JsonElement root)
        {
            var parts = new List<string>();

            if (root.TryGetProperty("Title", out var title)) parts.Add(title.GetString());
            if (root.TryGetProperty("Body", out var text)) parts.Add(text.GetString());

            if (root.TryGetProperty("Comments", out var comments) && comments.ValueKind == JsonValueKind.Array)
            {
                var topComments = comments.EnumerateArray()
                    .Take(3)
                    .Select(c => c.GetString());
                parts.AddRange(topComments);
            }

            return string.Join("\n", parts.Where(p => !string.IsNullOrWhiteSpace(p)));
        }

        private string ExtractInstagram(JsonElement root)
        {
            return root.TryGetProperty("Text", out var contentProp)
                ? contentProp.GetString() ?? string.Empty
                : string.Empty;
        }

        private string ExtractFacebook(JsonElement root)
        {
            return root.TryGetProperty("Text", out var contentProp)
                ? contentProp.GetString() ?? string.Empty
                : string.Empty;
        }

    }
}
