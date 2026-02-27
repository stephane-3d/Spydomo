using System;
using System.Collections.Generic;
using System.Text;

namespace Spydomo.Common.Enums
{
    /// <summary>
    /// Which Bright Data path to use when fetching HTML.
    /// </summary>
    public enum BrightDataFetchMode
    {
        Auto = 0,      // recommended: proxy first, unlocker fallback (or your preference)
        Proxy = 1,     // GET target URL through brd.superproxy.io
        Unlocker = 2,   // POST { zone, url, format } to the Unlocker/request API
        Browser = 3    // Use the Bright Data Browser (last resort, not recommended for HTML fetching)
    }
}
