using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.WebUtilities;

namespace Spydomo.Web.Classes.Extensions
{
    public static class NavigationManagerExtensions
    {
        public static bool TryGetQueryString<T>(this NavigationManager navManager, string key, out T value)
        {
            var uri = navManager.ToAbsoluteUri(navManager.Uri);
            if (QueryHelpers.ParseQuery(uri.Query).TryGetValue(key, out var valueFromQuery))
            {
                if (typeof(T) == typeof(int) && int.TryParse(valueFromQuery, out var intVal))
                {
                    value = (T)(object)intVal;
                    return true;
                }

                // Add other types if needed
            }

            value = default;
            return false;
        }
    }

}
