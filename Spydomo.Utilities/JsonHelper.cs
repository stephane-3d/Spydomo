using System.Text.Json;

namespace Spydomo.Utilities
{
    public static class JsonHelper
    {
        public static string StripJsonCodeBlock(string content)
        {
            if (string.IsNullOrWhiteSpace(content))
                throw new ArgumentException("Content is empty");

            content = content.Trim();

            // Remove code block backticks
            if (content.StartsWith("```json") || content.StartsWith("```"))
            {
                content = content.Replace("```json", "").Replace("```", "").Trim();
            }

            // Try to parse the string directly
            try
            {
                using var jsonDoc = JsonDocument.Parse(content);
                return content;
            }
            catch (JsonException ex)
            {
                throw new JsonException("Invalid JSON after stripping code block: " + ex.Message + "\nContent:\n" + content, ex);
            }
        }



        public static List<string> ParseStringList(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return new List<string>();

            try
            {
                return JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>();
            }
            catch
            {
                return new List<string>(); // fallback if the json is malformed
            }
        }

        public static Dictionary<string, string> ParseKeyValueObject(JsonElement objElement)
        {
            var result = new Dictionary<string, string>();

            if (objElement.ValueKind != JsonValueKind.Object)
                return result;

            foreach (var prop in objElement.EnumerateObject())
            {
                var key = prop.Name;
                var value = prop.Value.GetString() ?? "";

                if (!string.IsNullOrWhiteSpace(key))
                {
                    result[key.Trim()] = value.Trim();
                }
            }

            return result;
        }
    }
}