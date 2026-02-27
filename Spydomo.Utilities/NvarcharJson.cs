using System.Globalization;
using System.Text.Json;

namespace Spydomo.Utilities
{
    public static class NvarcharJson
    {
        /// <summary>
        /// Try to read a value at 'path' (dot + [index] syntax) and coerce to T.
        /// Returns true on success; false if path not found or conversion failed.
        /// </summary>
        public static bool TryGet<T>(
            string? jsonText,
            string path,
            out T? value,
            bool caseInsensitiveKeys = true)
        {
            value = default;
            if (string.IsNullOrWhiteSpace(jsonText) || string.IsNullOrWhiteSpace(path))
                return false;

            try
            {
                using var doc = JsonDocument.Parse(jsonText);
                if (!TryTraverse(doc.RootElement, path, caseInsensitiveKeys, out var el))
                    return false;

                return TryConvert(el, out value);
            }
            catch
            {
                value = default;
                return false;
            }
        }

        /// <summary>
        /// Try multiple fallback paths; the first that resolves & converts wins.
        /// </summary>
        public static bool TryGetAny<T>(
            string? jsonText,
            out T? value,
            params string[] paths)
        {
            value = default;
            if (paths is null || paths.Length == 0) return false;

            foreach (var p in paths)
                if (TryGet(jsonText, p, out value))
                    return true;

            return false;
        }

        /// <summary>
        /// Traverse a JsonElement using a dot path with optional [index] parts.
        /// Example: "items[0].name" or "Text.cons" or "Metadata.Rating"
        /// </summary>
        private static bool TryTraverse(
            JsonElement current,
            string path,
            bool caseInsensitive,
            out JsonElement result)
        {
            result = current;
            foreach (var segment in SplitPath(path))
            {
                if (segment.IsArrayIndex)
                {
                    if (result.ValueKind != JsonValueKind.Array) return false;
                    var arr = result;
                    if (segment.Index < 0 || segment.Index >= arr.GetArrayLength()) return false;
                    result = arr[segment.Index];
                }
                else
                {
                    if (result.ValueKind != JsonValueKind.Object) return false;

                    if (caseInsensitive)
                    {
                        JsonElement found = default;
                        var matched = false;
                        foreach (var prop in result.EnumerateObject())
                        {
                            if (prop.Name.Equals(segment.Property!, StringComparison.OrdinalIgnoreCase))
                            {
                                found = prop.Value;
                                matched = true;
                                break;
                            }
                        }
                        if (!matched) return false;
                        result = found;
                    }
                    else
                    {
                        if (!result.TryGetProperty(segment.Property!, out var next))
                            return false;
                        result = next;
                    }
                }
            }
            return true;
        }

        private static bool TryConvert<T>(JsonElement el, out T? value)
        {
            object? boxed = null;

            switch (el.ValueKind)
            {
                case JsonValueKind.String:
                    {
                        var s = el.GetString();
                        boxed = ConvertStringToTarget<T>(s);
                        break;
                    }
                case JsonValueKind.Number:
                    {
                        boxed = ConvertNumberToTarget<T>(el);
                        break;
                    }
                case JsonValueKind.True:
                case JsonValueKind.False:
                    {
                        var b = el.GetBoolean();
                        boxed = Convert.ChangeType(b, Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T), CultureInfo.InvariantCulture);
                        break;
                    }
                case JsonValueKind.Null:
                case JsonValueKind.Undefined:
                    boxed = default(T);
                    break;

                default:
                    // Fallback: if T is string, return the raw JSON text
                    if (typeof(T) == typeof(string))
                        boxed = el.GetRawText();
                    break;
            }

            if (boxed is null && typeof(T).IsValueType && Nullable.GetUnderlyingType(typeof(T)) is null)
            {
                value = default;
                return false;
            }

            try
            {
                value = (T?)boxed;
                return true;
            }
            catch
            {
                value = default;
                return false;
            }
        }

        private static object? ConvertStringToTarget<T>(string? s)
        {
            var target = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);

            if (target == typeof(string)) return s;

            if (string.IsNullOrWhiteSpace(s))
                return default(T);

            if (target == typeof(int))
                return int.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var i) ? i : default(int?);

            if (target == typeof(double))
                return double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : default(double?);

            if (target == typeof(decimal))
                return decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var m) ? m : default(decimal?);

            if (target == typeof(bool))
            {
                if (bool.TryParse(s, out var b)) return b;
                // common numeric bools
                if (s == "1") return true;
                if (s == "0") return false;
                return default(bool?);
            }

            if (target == typeof(DateTime))
                return DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var dt)
                    ? dt : default(DateTime?);

            // Last resort: attempt change type
            try { return Convert.ChangeType(s, target, CultureInfo.InvariantCulture); }
            catch { return null; }
        }

        private static object? ConvertNumberToTarget<T>(JsonElement el)
        {
            var target = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);

            if (target == typeof(int))
                return el.TryGetInt32(out var i) ? i : default(int?);

            if (target == typeof(long))
                return el.TryGetInt64(out var l) ? l : default(long?);

            if (target == typeof(double))
                return el.TryGetDouble(out var d) ? d : default(double?);

            if (target == typeof(decimal))
                return el.TryGetDecimal(out var m) ? m : default(decimal?);

            if (target == typeof(string))
                return el.GetRawText();

            // For bool, treat 0 => false, non-zero => true
            if (target == typeof(bool))
            {
                if (el.TryGetInt64(out var ln)) return ln != 0;
                if (el.TryGetDouble(out var dn)) return Math.Abs(dn) > double.Epsilon;
                return null;
            }

            // Attempt generic conversion via string
            var asString = el.GetRawText();
            return ConvertStringToTarget<T>(asString);
        }

        private readonly struct PathSegment
        {
            public string? Property { get; init; }
            public int Index { get; init; }
            public bool IsArrayIndex => Property is null;
        }

        private static IEnumerable<PathSegment> SplitPath(string path)
        {
            // Supports: a.b.c, a[0].b, items[10]
            // No escaped dots/quotes for simplicity
            var i = 0;
            while (i < path.Length)
            {
                // read property name until '.' or '['
                int start = i;
                while (i < path.Length && path[i] != '.' && path[i] != '[') i++;
                if (i > start)
                {
                    yield return new PathSegment { Property = path[start..i] };
                }

                if (i < path.Length && path[i] == '[')
                {
                    i++; // skip '['
                    int idxStart = i;
                    while (i < path.Length && path[i] != ']') i++;
                    if (i >= path.Length) yield break;
                    var idxStr = path[idxStart..i];
                    if (int.TryParse(idxStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var idx))
                        yield return new PathSegment { Index = idx };
                    i++; // skip ']'
                }

                if (i < path.Length && path[i] == '.')
                    i++; // skip '.'
            }
        }
    }
}
