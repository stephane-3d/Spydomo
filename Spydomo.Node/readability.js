// readability.js
const puppeteer = require("puppeteer-core");
const { Readability } = require("@mozilla/readability");
const { JSDOM } = require("jsdom");

const REMOTE_WS = process.env.BRIGHTDATA_BROWSER_WS;

const { VirtualConsole } = require("jsdom");

function makeDom(html, url) {
  const vc = new VirtualConsole();

  vc.on("jsdomError", (err) => {
    // Drop only CSS parse noise
    if (String(err?.message || "").includes("Could not parse CSS stylesheet")) return;
    console.error("jsdomError:", err);
  });

  return new JSDOM(html, { url, virtualConsole: vc });
}


// --- helpers ---
function normalizeUrl(u) {
  if (!u) return null;
  let s = String(u).trim();

  // skip obvious junk
  if (!s || s.startsWith("#")) return null;
  if (s.startsWith("javascript:")) return null;
  if (s.startsWith("mailto:") || s.startsWith("tel:")) return null;

  // handle protocol-relative
  if (s.startsWith("//")) s = "https:" + s;

  // some sites store escaped slashes
  s = s.replace(/\\\//g, "/");

  try {
    const url = new URL(s);
    url.hash = "";

    // optional: drop common trackers (keep if you prefer)
    const drop = ["utm_source", "utm_medium", "utm_campaign", "utm_term", "utm_content", "gclid", "fbclid"];
    drop.forEach(k => url.searchParams.delete(k));

    // normalize host casing, strip trailing slash
    const out = url.toString().replace(/\/$/, "");
    return out;
  } catch {
    return null;
  }
}

function isSocialUrl(u) {
  if (!u) return false;
  const s = u.toLowerCase();

  // Expand as needed
  return (
    s.includes("linkedin.com/") ||
    s.includes("x.com/") ||
    s.includes("twitter.com/") ||
    s.includes("facebook.com/") ||
    s.includes("instagram.com/") ||
    s.includes("youtube.com/") ||
    s.includes("tiktok.com/") ||
    s.includes("github.com/") ||
    s.includes("threads.net/") ||
    s.includes("pinterest.") ||
    s.includes("medium.com/") ||
    s.includes("reddit.com/") ||
    s.includes("substack.com/")
  );
}

// Pull URLs from JSON-ish structures safely
function collectUrlsFromJson(node, out) {
  if (!node) return;
  if (typeof node === "string") {
    const n = normalizeUrl(node);
    if (n) out.add(n);
    return;
  }
  if (Array.isArray(node)) {
    node.forEach(x => collectUrlsFromJson(x, out));
    return;
  }
  if (typeof node === "object") {
    for (const k of Object.keys(node)) {
      // common keys
      collectUrlsFromJson(node[k], out);
    }
  }
}

async function extractLinksFromDom(page) {
  // Run in page context; return raw strings
  return await page.evaluate(() => {
    const out = new Set();

    // 1) a[href]
    document.querySelectorAll("a[href]").forEach(a => {
      out.add(a.getAttribute("href") || "");
    });

    // 2) common “social icon” patterns (sometimes href is on parent)
    document.querySelectorAll("[aria-label],[title]").forEach(el => {
      const label = (el.getAttribute("aria-label") || el.getAttribute("title") || "").toLowerCase();
      if (!label) return;

      // if element itself is link
      if (el.tagName.toLowerCase() === "a" && el.getAttribute("href")) {
        out.add(el.getAttribute("href"));
        return;
      }

      // if inside a link
      const parentLink = el.closest("a[href]");
      if (parentLink) out.add(parentLink.getAttribute("href"));
    });

    // 3) meta tags often include canonical / social handles
    document.querySelectorAll("meta[content]").forEach(m => {
      const name = (m.getAttribute("property") || m.getAttribute("name") || "").toLowerCase();
      if (!name) return;
      if (
        name.includes("og:see_also") ||
        name.includes("twitter:site") ||
        name.includes("twitter:creator") ||
        name.includes("al:android:url") ||
        name.includes("al:ios:url")
      ) {
        out.add(m.getAttribute("content") || "");
      }
    });

    // 4) JSON-LD scripts
    document.querySelectorAll('script[type="application/ld+json"]').forEach(s => {
      out.add(s.textContent || "");
    });

    return Array.from(out);
  });
}

async function renderHtmlFromUrl(url, opts = {}) {
  const debug = opts.debug === true || process.env.DEBUG_READABILITY === "1";
  const rid = opts._rid || `${Date.now().toString(36)}-${Math.random().toString(16).slice(2,6)}`;
  
  if (!REMOTE_WS) throw new Error("Missing env var BRIGHTDATA_BROWSER_WS");

  const lower = (url || "").toLowerCase();
  if (/\.(png|jpe?g|gif|webp|svg|pdf)(\?|#|$)/i.test(lower)) {
    return { ok: false, url, error: "Non-HTML URL (image/pdf) refused" };
  }

  const mode = (opts.mode || "lite").toLowerCase(); // "lite" | "full"
  const includeHtml = opts.includeHtml === true;    // default false
  const allowFullAnchorScan = opts.allowFullAnchorScan === true; // default false

  const scroll = opts.scroll === true;
  const extraWaitMs = Number.isFinite(opts.extraWaitMs)
    ? opts.extraWaitMs
    : (mode === "full" ? 800 : 0);

  dbg(debug, "START", {
    url,
    mode,
    includeHtml,
    allowFullAnchorScan,
    scroll,
    extraWaitMs
  });

  const browser = await puppeteer.connect({ browserWSEndpoint: REMOTE_WS });
  const page = await browser.newPage();
  const meter = await attachTrafficMeter(page, { logTop: 12 });

  // page.on("console", (msg) => dbg(debug, "PAGE console", msg.type(), msg.text().slice(0, 300)));
  page.on("pageerror", (err) => dbg(debug, "PAGE error", err?.message || String(err)));
  
  let baseHost = null;
  try { baseHost = new URL(url).hostname.replace(/^www\./i, ""); } catch {}

  try {
    await page.setViewport({ width: 1366, height: 768 });
    await page.setUserAgent(
      "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/123.0.0.0 Safari/537.36"
    );

    await page.setRequestInterception(true);
    page.on("request", (req) => {
      try {
        const rtype = req.resourceType();
        const reqUrl = req.url();

        const abort = (reason) => {
        dbg(debug, "ABORT", { rtype, reason, reqUrl: reqUrl.slice(0, 180) });
          return req.abort();
        };

        // Always block heavy
        if (rtype === "image" || rtype === "media" || rtype === "font") return req.abort();

        // Always block common trackers
        if (
          reqUrl.includes("googletagmanager") ||
          reqUrl.includes("google-analytics") ||
          reqUrl.includes("clarity.") ||
          reqUrl.includes("hotjar") ||
          reqUrl.includes("segment")
        ) return req.abort();

        if (mode === "lite") {
          // Same-origin only
          if (baseHost) {
            try {
              const h = new URL(reqUrl).hostname.replace(/^www\./i, "");
              if (h && h !== baseHost) return abort("cross-origin lite");
            } catch { /* ignore */ }
          }
          // Lite blocks scripts/styles/xhr/fetch (biggest savings)
          if (rtype === "stylesheet" || rtype === "script") return req.abort();
          if (rtype === "xhr" || rtype === "fetch" || rtype === "websocket" || rtype === "eventsource")
            return abort("lite blocks xhr/fetch/ws");
        }

        return req.continue();
      } catch {
        try { return req.continue(); } catch { return; }
      }
    });

    const response = await page.goto(url, { waitUntil: "domcontentloaded", timeout: 45000 });

    dbg(debug, "GOTO done", {
      status: response?.status?.(),
      ok: response?.ok?.(),
      finalUrl: page.url()
    });

    const headers = response?.headers?.() || {};
    dbg(debug, "HEADERS", {
      "content-type": headers["content-type"],
      "server": headers["server"],
      "content-length": headers["content-length"]
    });

    const ct = (response?.headers()?.["content-type"] || "").toLowerCase();
    if (!ct.includes("text/html") && !ct.includes("application/xhtml+xml")) {
      return { ok: false, url, error: `Non-HTML content-type: ${ct || "unknown"}` };
    }

    // Full mode: let hydration happen
    if (mode === "full") {
      try { await page.waitForNetworkIdle({ idleTime: 500, timeout: 8000 }); } catch {}
      if (scroll) {
        try {
          await page.evaluate(() => window.scrollTo(0, document.body.scrollHeight));
          await page.waitForTimeout(400);
        } catch {}
      }
      if (extraWaitMs > 0) {
        try { await page.waitForTimeout(extraWaitMs); } catch {}
      }
    }

    const s = meter.summarize();
    console.log("[traffic > RenderHtmlFromUrl]", {
      total: fmtBytes(s.totalBytes),
      reqCount: s.count,
      topTypes: s.topTypes.map(x => ({ ...x, bytes: fmtBytes(x.bytes) })),
      topHosts: s.topHosts.map(x => ({ ...x, bytes: fmtBytes(x.bytes) })),
    });

    const domStats = await page.evaluate(() => {
      const a = document.querySelectorAll("a[href]").length;
      const textLen = (document.body?.innerText || "").trim().length;
      const title = document.title || "";
      return { title, anchors: a, textLen };
    });
    dbg(debug, "DOM stats", domStats);

    // ---- staged DOM extraction ----
    const rawA = await extractLinks_MetadataOnly(page);
    dbg(debug, "extractLinks_MetadataOnly", { count: rawA?.length || 0, sample: (rawA || []).slice(0, 5) });

    let raw = rawA || [];

    // Parse Pass A first, to see if it yields any social URLs
    const prelimSocial = new Set();
    for (const x of raw) {
      const t = String(x || "").trim();
      if (!t) continue;

      if (t.startsWith("{") || t.startsWith("[")) {
        try {
          const parsed = JSON.parse(t);
          const tmp = new Set();
          (function collect(node) {
            if (!node) return;
            if (typeof node === "string") tmp.add(node);
            else if (Array.isArray(node)) node.forEach(collect);
            else if (typeof node === "object") Object.values(node).forEach(collect);
          })(parsed);

          for (const u of tmp) {
            const abs = resolveAgainst(page.url(), u);
            const n = normalizeUrl(abs);
            if (n && isSocialUrl(n)) prelimSocial.add(n);
          }
          continue;
        } catch {}
      }

      const abs = resolveAgainst(page.url(), t);
      const n = normalizeUrl(abs);
      if (n && isSocialUrl(n)) prelimSocial.add(n);
    }

    dbg(debug, "prelim social from PassA", { count: prelimSocial.size, sample: Array.from(prelimSocial).slice(0, 5) });

    // If Pass A didn't find any social links, go deeper
    if (prelimSocial.size === 0) {
      const rawB = await extractLinks_HeaderFooter(page);
      dbg(debug, "extractLinks_HeaderFooter", { count: rawB?.length || 0, sample: (rawB || []).slice(0, 10) });
      raw = raw.concat(rawB || []);
    }

    if (allowFullAnchorScan) {
      // If we STILL have no socials after Pass B, do full scan
      // (or you can always do it if allowFullAnchorScan is true)
      const rawC = await extractLinks_AllAnchors(page);
      dbg(debug, "extractLinks_AllAnchors", { count: rawC?.length || 0, sample: (rawC || []).slice(0, 10) });
      raw = raw.concat(rawC || []);
    }

    const allLinks = new Set();
    const socialLinks = new Set();

    for (const x of raw) {
      const t = String(x || "").trim();
      if (!t) continue;

      if (t.startsWith("{") || t.startsWith("[")) {
        try {
          const parsed = JSON.parse(t);
          const tmp = new Set();

          (function collectRaw(node) {
            if (!node) return;
            if (typeof node === "string") { tmp.add(node); return; }
            if (Array.isArray(node)) { node.forEach(collectRaw); return; }
            if (typeof node === "object") {
              for (const k of Object.keys(node)) collectRaw(node[k]);
            }
          })(parsed);

          for (const u of tmp) {
            const abs = resolveAgainst(page.url(), u);
            const n = normalizeUrl(abs);
            if (!n) continue;
            allLinks.add(n);
            if (isSocialUrl(n)) socialLinks.add(n);
          }
          continue;
        } catch { /* ignore */ }
      }

      const abs = resolveAgainst(page.url(), t);
      const n = normalizeUrl(abs);
      if (!n) continue;

      allLinks.add(n);
      if (isSocialUrl(n)) socialLinks.add(n);
    }

    const html = includeHtml ? await page.content() : null;

    dbg(debug, "RESULT sizes", {
      rawCount: raw?.length || 0,
      allLinks: allLinks.size,
      socialLinks: socialLinks.size
    });

    return {
      ok: true,
      url,
      finalUrl: page.url(),
      mode,
      html,
      links: Array.from(allLinks),
      socialLinks: Array.from(socialLinks),
    };
  } catch (e) {
    return { ok: false, url, error: e?.message || String(e) };
  } finally {
    try { page.removeAllListeners("request"); } catch {}
    try { await page.close(); } catch {}
    try { await browser.close(); } catch { try { browser.disconnect(); } catch {} }
  }
}

async function extractNextDataLinks(page) {
  try {
    const json = await page.evaluate(() => {
      const el = document.getElementById("__NEXT_DATA__");
      return el ? el.textContent : null;
    });
    if (!json) return [];

    const parsed = JSON.parse(json);
    const found = new Set();

    (function walk(node) {
      if (!node) return;
      if (typeof node === "string") {
        if (node.startsWith("http://") || node.startsWith("https://") || node.startsWith("//") || node.startsWith("/")) {
          found.add(node);
        }
        return;
      }
      if (Array.isArray(node)) return node.forEach(walk);
      if (typeof node === "object") Object.values(node).forEach(walk);
    })(parsed);

    return Array.from(found);
  } catch {
    return [];
  }
}


function resolveAgainst(base, href) {
  try {
    // handles /path, ./path, path, ?q=, etc.
    return new URL(href, base).toString();
  } catch {
    return null;
  }
}

async function extractFromUrl(url, opts = {}) {
  const debug = opts.debug === true || process.env.DEBUG_READABILITY === "1";
  const extraWaitMs = Number.isFinite(opts.extraWaitMs) ? opts.extraWaitMs : 0;

  if (debug) console.log("[extractFromUrl] opts", { extraWaitMs, debug });

  if (!REMOTE_WS) {
    throw new Error("Missing env var BRIGHTDATA_BROWSER_WS (BrightData browser WebSocket endpoint).");
  }

  // quick reject obvious binaries
  const lower = (url || "").toLowerCase();
  if (/\.(png|jpe?g|gif|webp|svg|pdf)(\?|#|$)/i.test(lower)) {
    return { ok: false, url, error: "Non-HTML URL (image/pdf) refused" };
  }

  const browser = await puppeteer.connect({ browserWSEndpoint: REMOTE_WS });
  const page = await browser.newPage();
  const meter = await attachTrafficMeter(page, { logTop: 12 });

  try {
    await page.setViewport({ width: 1366, height: 768 });
    await page.setUserAgent(
      "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/123.0.0.0 Safari/537.36"
    );

    await page.setRequestInterception(true);
    page.on("request", (req) => {
      try {
        const rtype = req.resourceType();
        const u = req.url();

        if (
          rtype === "image" ||
          rtype === "media" ||
          rtype === "font" ||
          u.includes("googletagmanager") ||
          u.includes("google-analytics") ||
          u.includes("clarity.") ||
          u.includes("hotjar") ||
          u.includes("segment")
        ) req.abort();
        else req.continue();
      } catch {
        try { req.continue(); } catch {}
      }
    }); 

    // ✅ single navigation
    const response = await page.goto(url, { waitUntil: "domcontentloaded", timeout: 60000 });

    const ct = (response?.headers()?.["content-type"] || "").toLowerCase();
    if (!ct.includes("text/html") && !ct.includes("application/xhtml+xml")) {
      return { ok: false, url, error: `Non-HTML content-type: ${ct || "unknown"}` };
    }

    try {
    await page.waitForNetworkIdle({ idleTime: 750, timeout: 12000 });
    } catch {}

    if (extraWaitMs > 0) {
      try { await page.waitForTimeout(extraWaitMs); } catch {}
    }

    const s = meter.summarize();
    console.log("[traffic > ExtractFromUrl]", {
      total: fmtBytes(s.totalBytes),
      reqCount: s.count,
      topTypes: s.topTypes.map(x => ({ ...x, bytes: fmtBytes(x.bytes) })),
      topHosts: s.topHosts.map(x => ({ ...x, bytes: fmtBytes(x.bytes) })),
    });

    const html = await page.content();
    return extractFromHtml(html, url);
  } catch (e) {
    return { ok: false, url, error: e?.message || String(e) };
  } finally {
    try { page.removeAllListeners("request"); } catch {}
    try { await page.close(); } catch {}

    // ✅ THIS is what ends the BrightData session properly
    try { await browser.close(); } catch { try { browser.disconnect(); } catch {} }
  }
}

function extractFromHtml(html, url) {
  const dom = makeDom(html, url);
  try {
    // Strip style tags + stylesheet links before Readability runs
    const doc = dom.window.document;
    doc.querySelectorAll("style, link[rel='stylesheet']").forEach(n => n.remove());

    const reader = new Readability(doc);
    const article = reader.parse();
    if (!article) return { ok: false, url, error: "Readability returned null" };

    return {
      ok: true,
      url,
      title: article.title,
      byline: article.byline,
      excerpt: article.excerpt,
      content: article.content,
      textContent: article.textContent,
      length: article.length,
      siteName: article.siteName,
      publishedAt,
    };
  } finally {
    dom.window.close();
  }
}

async function autoScroll(page) {
    await page.evaluate(async () => {
        await new Promise((resolve) => {
            let totalHeight = 0;
            const distance = 600;
            const timer = setInterval(() => {
                const scrollHeight = document.body.scrollHeight;
                window.scrollBy(0, distance);
                totalHeight += distance;

                if (totalHeight >= scrollHeight - window.innerHeight - 50) {
                    clearInterval(timer);
                    resolve();
                }
            }, 120);
        });
    });
}

// ---- DOM extractors ----

// 0) tiny helpers in page context
const SOCIAL_HOST_HINTS = [
  "linkedin.com", "x.com", "twitter.com", "facebook.com", "instagram.com",
  "youtube.com", "tiktok.com", "github.com", "threads.net",
  "pinterest.", "medium.com", "substack.com", "reddit.com"
];

// Pass A: JSON-LD + meta only (super cheap)
async function extractLinks_MetadataOnly(page) {
  return await page.evaluate((hostHints) => {
    const out = new Set();

    // meta tags (very cheap)
    document.querySelectorAll("meta[content]").forEach(m => {
      const name = (m.getAttribute("property") || m.getAttribute("name") || "").toLowerCase();
      if (!name) return;
      if (
        name.includes("og:see_also") ||
        name.includes("twitter:site") ||
        name.includes("twitter:creator") ||
        name.includes("al:android:url") ||
        name.includes("al:ios:url")
      ) {
        out.add(m.getAttribute("content") || "");
      }
    });

    // JSON-LD scripts (cheap)
    document.querySelectorAll('script[type="application/ld+json"]').forEach(s => {
      const t = s.textContent || "";
      if (t) out.add(t);
    });

    // sometimes social links are direct in rel=me
    document.querySelectorAll('a[rel~="me"][href]').forEach(a => out.add(a.getAttribute("href") || ""));

    // very small selector set: icons/labels only
    document.querySelectorAll("[aria-label],[title]").forEach(el => {
      const label = (el.getAttribute("aria-label") || el.getAttribute("title") || "").toLowerCase();
      if (!label) return;

      // only keep likely social label elements to avoid scanning everything
      for (const h of hostHints) {
        if (label.includes(h.split(".")[0])) { // "linkedin" from "linkedin.com"
          const a = el.closest("a[href]");
          if (a) out.add(a.getAttribute("href") || "");
          break;
        }
      }
    });

    return Array.from(out);
  }, SOCIAL_HOST_HINTS);
}

// Pass B: header/footer/nav targeted anchors (still cheap)
async function extractLinks_HeaderFooter(page) {
  return await page.evaluate((hostHints) => {
    const out = new Set();

    // Only search in likely areas (tiny portion of DOM)
    const roots = [];
    const header = document.querySelector("header");
    const footer = document.querySelector("footer");
    document.querySelectorAll("nav").forEach(n => roots.push(n));
    if (header) roots.push(header);
    if (footer) roots.push(footer);

    // fallback: common footer containers
    document.querySelectorAll('[class*="footer"],[id*="footer"]').forEach(x => roots.push(x));

    const uniqRoots = Array.from(new Set(roots)).slice(0, 6); // cap

    uniqRoots.forEach(root => {
      root.querySelectorAll("a[href]").forEach(a => {
        const href = a.getAttribute("href") || "";
        if (!href) return;

        // quick filter: keep if href likely points to social
        root.querySelectorAll("a[href]").forEach(a => out.add(a.getAttribute("href") || ""));
      });

      // aria/title patterns inside these areas
      root.querySelectorAll("[aria-label],[title]").forEach(el => {
        const label = (el.getAttribute("aria-label") || el.getAttribute("title") || "").toLowerCase();
        if (!label) return;
        const a = el.closest("a[href]");
        if (a) out.add(a.getAttribute("href") || "");
      });
    });

    return Array.from(out);
  }, SOCIAL_HOST_HINTS);
}

// Pass C: full anchor scan (expensive) — only when needed
async function extractLinks_AllAnchors(page) {
  return await page.evaluate(() => {
    const out = new Set();
    document.querySelectorAll("a[href]").forEach(a => out.add(a.getAttribute("href") || ""));
    document.querySelectorAll("[aria-label],[title]").forEach(el => {
      const a = el.closest("a[href]");
      if (a) out.add(a.getAttribute("href") || "");
    });
    document.querySelectorAll('script[type="application/ld+json"]').forEach(s => out.add(s.textContent || ""));
    return Array.from(out);
  });
}

function dbg(enabled, ...args) {
  if (!enabled) return;
  console.log("[readability]", ...args);
}

async function attachTrafficMeter(page, { logTop = 10 } = {}) {
  const client = await page.target().createCDPSession();
  await client.send("Network.enable");

  // requestId -> { url, type, mime, status }
  const req = new Map();

  let totalBytes = 0;

  client.on("Network.requestWillBeSent", (e) => {
    req.set(e.requestId, {
      url: e.request.url,
      type: e.type || "Other",
      mime: "",
      status: 0,
      bytes: 0
    });
  });

  client.on("Network.responseReceived", (e) => {
    const r = req.get(e.requestId);
    if (!r) return;
    r.status = e.response.status;
    r.mime = e.response.mimeType || "";
  });

  client.on("Network.loadingFinished", (e) => {
    const r = req.get(e.requestId);
    if (!r) return;
    r.bytes += Number(e.encodedDataLength || 0);
    totalBytes += Number(e.encodedDataLength || 0);
  });

  client.on("Network.loadingFailed", (e) => {
    // keep entry; bytes may be 0 or partial
  });

  function summarize() {
    const rows = Array.from(req.values());

    const byType = {};
    const byHost = {};

    for (const r of rows) {
      const b = r.bytes || 0;

      const t = r.type || "Other";
      byType[t] = (byType[t] || 0) + b;

      let host = "unknown";
      try { host = new URL(r.url).hostname; } catch {}
      byHost[host] = (byHost[host] || 0) + b;
    }

    const topHosts = Object.entries(byHost)
      .sort((a, b) => b[1] - a[1])
      .slice(0, logTop)
      .map(([host, bytes]) => ({ host, bytes }));

    const topTypes = Object.entries(byType)
      .sort((a, b) => b[1] - a[1])
      .map(([type, bytes]) => ({ type, bytes }));

    return { totalBytes, topHosts, topTypes, count: rows.length };
  }

  return { summarize, totalBytes: () => totalBytes };
}

function fmtBytes(n) {
  if (!Number.isFinite(n)) return String(n);
  const kb = 1024, mb = kb * 1024, gb = mb * 1024;
  if (n >= gb) return (n / gb).toFixed(2) + " GB";
  if (n >= mb) return (n / mb).toFixed(2) + " MB";
  if (n >= kb) return (n / kb).toFixed(1) + " KB";
  return n + " B";
}

module.exports = { extractFromUrl, extractFromHtml, renderHtmlFromUrl };
