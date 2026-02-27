// capterra-scraper.js
const puppeteer = require("puppeteer-core");

// ─── Helpers ─────────────────────────────────────────────────────────────────

const sleep = (ms) => new Promise((resolve) => setTimeout(resolve, ms));

/**
 * Append Capterra's sort-by-most-recent query param to the reviews URL so we
 * arrive pre-sorted and can skip the sort-click interaction entirely.
 */
function withSortParam(url) {
  try {
    const u = new URL(url);
    u.searchParams.set("sortOrder", "MOST_RECENT");
    return u.toString();
  } catch {
    return url;
  }
}

/**
 * Primary CAPTCHA handler — delegates to BrightData's built-in CDP solver.
 * Captcha.waitForSolve returns:
 *   { status: 'not_detected' } — no challenge found within detectTimeout
 *   { status: 'solved' }       — challenge found and auto-solved
 *   { status: 'solve_failed' } — challenge found but not solvable
 *
 * Throws if solving failed.
 * Returns false if the CDP command is unavailable (non-BrightData browser),
 * so the caller can fall back to the HTML-based check.
 */
async function solveCaptchaViaCdp(cdpClient) {
  try {
    const result = await cdpClient.send("Captcha.waitForSolve", {
      detectTimeout: 60000,
    });
    const status = result?.status ?? "unknown";

    if (status === "not_found") {
      console.log("✅ No CAPTCHA detected.");
    } else if (status === "solved" || status === "solve_finished") {
      console.log(`✅ CAPTCHA solved by BrightData (${status}).`);
    } else {
      throw new Error(`❌ CAPTCHA solve status: ${status}`);
    }
    return true; // CDP handled it
  } catch (err) {
    // CDP command not available on this browser (e.g. local dev without BrightData)
    const msg = err.message ?? "";
    if (
      msg.includes("Method not found") ||
      msg.includes("Unknown method") ||
      msg.includes("not supported") ||
      msg.includes("Captcha")
    ) {
      console.warn(`⚠️ Captcha.waitForSolve unavailable: ${msg}`);
      return false; // caller should use HTML fallback
    }
    throw err; // real error — propagate
  }
}

/**
 * Fallback CAPTCHA handler for environments without BrightData CDP support.
 * Polls the DOM for Cloudflare challenge indicators and waits up to 25s.
 */
async function solveCaptchaViaHtml(page) {
  const hasCaptcha = await page.evaluate(() => {
    const html = document.documentElement.innerHTML;
    return (
      html.includes("cf-browser-verification") ||
      html.includes("Checking your browser before accessing") ||
      html.includes("Just a moment...") ||
      html.includes("g-recaptcha") ||
      html.includes("h-captcha") ||
      html.includes("data-sitekey") ||
      html.includes("captcha-wrapper") ||
      html.includes("Access to this page has been denied") ||
      html.includes("why did this happen") ||
      html.includes("px-captcha-wrapper")
    );
  });

  if (!hasCaptcha) return;

  console.warn("🚨 CAPTCHA detected (HTML fallback). Waiting up to 25s...");
  await sleep(15000);

  const checkCaptcha = () =>
    page.evaluate(() => {
      const html = document.documentElement.innerHTML;
      return (
        html.includes("cf-browser-verification") ||
        html.includes("Just a moment...")
      );
    });

  if (await checkCaptcha()) {
    console.warn("⏳ Still on CAPTCHA after 15s, waiting 10s more...");
    await sleep(10000);
    if (await checkCaptcha()) {
      throw new Error("❌ CAPTCHA not solved after 25s");
    }
  }
  console.log("✅ CAPTCHA passed (HTML fallback).");
}

// ─── Main scraper ─────────────────────────────────────────────────────────────

async function scrapeCapterra(url) {
  const BROWSER_WS = process.env.BRIGHTDATA_BROWSER_WS;
  if (!BROWSER_WS) throw new Error("Missing env var BRIGHTDATA_BROWSER_WS");

  const TRAFFIC_METER_ENABLED = process.env.TRAFFIC_METER !== "false";

  const browser = await puppeteer.connect({ browserWSEndpoint: BROWSER_WS });
  const page = await browser.newPage();

  // One shared CDP session for both the traffic meter and the CAPTCHA solver.
  // BrightData's remote browser requires page.target().createCDPSession().
  // eslint-disable-next-line deprecation/deprecation
  const cdpClient = await page.target().createCDPSession();
  const meter = TRAFFIC_METER_ENABLED
    ? attachTrafficMeterToClient(cdpClient, { logTop: 12 })
    : null;

  // Enable BrightData's auto-solve before any navigation so it can intercept
  // Cloudflare Turnstile challenges as soon as they appear.
  try {
    await cdpClient.send("Captcha.setAutoSolve", { autoSolve: true });
    console.log("✅ Captcha.setAutoSolve enabled.");
  } catch (err) {
    console.warn(`⚠️ Captcha.setAutoSolve unavailable: ${err.message}`);
  }

  try {
    await page.setRequestInterception(true);
    page.on("request", (req) => {
      const type = req.resourceType();
      const u = req.url();

      // Never block Cloudflare challenge resources — the CAPTCHA solver needs
      // these to render the Turnstile widget and complete its JS verification.
      if (u.includes("challenges.cloudflare.com") || u.includes("cloudflare.com/cdn-cgi")) {
        req.continue();
        return;
      }

      if (
        type === "image" ||
        type === "stylesheet" ||
        type === "font" ||
        type === "media" ||
        u.includes("qualtrics.com") ||
        u.includes("clarity.") ||
        u.includes("google-analytics") ||
        u.includes("googletagmanager")
      ) {
        req.abort();
      } else {
        req.continue();
      }
    });

    page.on("console", (msg) => {
      const text = msg.text();
      // Suppress noise from blocked sub-resources (our own aborts + external 403s)
      if (text.includes("Failed to load resource")) return;
      // Suppress harmless CSS preload warnings caused by Early Hints + our stylesheet blocking
      if (text.includes("was preloaded using link preload") && text.includes(".css")) return;
      console.log(`📺 Console: ${text}`);
    });
    page.on("pageerror", (err) => console.error(`❌ Page error: ${err}`));

    await page.setViewport({ width: 1366, height: 768 });
    await page.setUserAgent(
      "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/123.0.0.0 Safari/537.36"
    );

    const sortedUrl = withSortParam(url);
    console.log(`🌐 Navigating to ${sortedUrl}`);
    await page.goto(sortedUrl, { waitUntil: "networkidle2", timeout: 120000 });

    // ── CAPTCHA handling ────────────────────────────────────────────────────
    // Primary: BrightData CDP solver (handles Cloudflare Turnstile natively).
    // Fallback: HTML polling for local dev / non-BrightData environments.
    const cdpHandled = await solveCaptchaViaCdp(cdpClient);
    if (!cdpHandled) {
      await solveCaptchaViaHtml(page);
    }

    // ── Wait for review content ─────────────────────────────────────────────
    try {
      await page.waitForSelector("p.typo-10.col-span-2", {
        timeout: 10000,
        visible: true,
      });
      console.log("✅ Review element detected.");
    } catch {
      console.warn("⚠️ Review selector not found, using fallback delay.");
      await sleep(5000);
    }

    if (meter) {
      const s = meter.summarize();
      console.log("[traffic after load]", {
        total: fmtBytes(s.totalBytes),
        reqCount: s.count,
        topTypes: s.topTypes.map((x) => ({ ...x, bytes: fmtBytes(x.bytes) })),
        topHosts: s.topHosts.map((x) => ({ ...x, bytes: fmtBytes(x.bytes) })),
      });
    }

    // ── Sort ────────────────────────────────────────────────────────────────
    // If Capterra honoured the URL param the current URL will still contain it.
    const currentUrl = page.url().toLowerCase();
    const alreadySorted = currentUrl.includes("sortorder=most_recent");

    if (alreadySorted) {
      console.log("✅ Sort already applied via URL param — skipping sort interaction.");
    } else {
      console.log("⚠️ URL param not reflected — falling back to sort click.");

      try {
        const dropdown = await page.waitForSelector(
          "div[data-testid='filters-sort-by']",
          { timeout: 10000 }
        );
        await dropdown.evaluate((el) => el.scrollIntoView());
        await dropdown.click();
        await sleep(1500);
        console.log("✅ Opened sorting dropdown.");
      } catch (err) {
        console.warn(`⚠️ Could not open sorting dropdown: ${err.message}`);
      }

      try {
        const mostRecent = await page.waitForSelector(
          "div[data-testid='filter-sort-MOST_RECENT']",
          { timeout: 10000 }
        );
        await mostRecent.evaluate((el) => el.scrollIntoView());
        await mostRecent.click();
        await page.evaluate(
          (el) => el.dispatchEvent(new Event("click", { bubbles: true })),
          mostRecent
        );
        await sleep(3000);
        console.log('✅ Selected "Most Recent" sorting option (fallback click).');
      } catch (err) {
        console.warn(`⚠️ Could not select "Most Recent": ${err.message}`);
      }

      try {
        await page.waitForFunction(
          () => {
            const dateDivs = document.querySelectorAll("div.typo-0.text-neutral-90");
            if (!dateDivs.length) return false;
            return /20\d{2}/.test(dateDivs[0].innerText);
          },
          { timeout: 10000 }
        );
        console.log("✅ Verified sorting via date pattern.");
      } catch (err) {
        console.warn(`⚠️ Date check failed: ${err.message}`);
      }
    }

    const reviewCount = await page.evaluate(() => {
      return Array.from(document.querySelectorAll("div")).filter((div) => {
        const hasStars =
          div.querySelector('i[role="img"][aria-label^="star-"]') !== null;
        const hasPros = Array.from(div.querySelectorAll("span")).some(
          (el) => el.innerText.trim() === "Pros"
        );
        const hasCons = Array.from(div.querySelectorAll("span")).some(
          (el) => el.innerText.trim() === "Cons"
        );
        const hasText =
          div.innerText.includes("Used the software for") ||
          div.innerText.includes("Alternatives considered");
        return hasStars && (hasPros || hasCons || hasText);
      }).length;
    });

    console.log(`🔍 Detected ${reviewCount} review blocks`);

    const html = await page.content();
    return html;
  } finally {
    try { await page.close(); } catch { /* ignore */ }
    try { browser.disconnect(); } catch { /* ignore */ }
  }
}

// ─── Traffic meter ────────────────────────────────────────────────────────────
// Accepts an existing CDP client so we don't open a second session per page.

function attachTrafficMeterToClient(client, { logTop = 10 } = {}) {
  // Network domain must be enabled; safe to call even if already enabled.
  client.send("Network.enable").catch(() => {});

  const req = new Map();
  let totalBytes = 0;

  client.on("Network.requestWillBeSent", (e) => {
    req.set(e.requestId, { url: e.request.url, type: e.type || "Other", bytes: 0 });
  });

  client.on("Network.responseReceived", (e) => {
    const r = req.get(e.requestId);
    if (r) r.status = e.response.status;
  });

  client.on("Network.loadingFinished", (e) => {
    const r = req.get(e.requestId);
    if (!r) return;
    r.bytes += Number(e.encodedDataLength || 0);
    totalBytes += Number(e.encodedDataLength || 0);
  });

  function summarize() {
    const rows = Array.from(req.values());
    const byType = {};
    const byHost = {};

    for (const r of rows) {
      const b = r.bytes || 0;
      byType[r.type || "Other"] = (byType[r.type || "Other"] || 0) + b;
      let host = "unknown";
      try { host = new URL(r.url).hostname; } catch {}
      byHost[host] = (byHost[host] || 0) + b;
    }

    return {
      totalBytes,
      count: rows.length,
      topTypes: Object.entries(byType).sort((a, b) => b[1] - a[1]).map(([type, bytes]) => ({ type, bytes })),
      topHosts: Object.entries(byHost).sort((a, b) => b[1] - a[1]).slice(0, logTop).map(([host, bytes]) => ({ host, bytes })),
    };
  }

  return { summarize };
}

function fmtBytes(n) {
  if (!Number.isFinite(n)) return String(n);
  const kb = 1024, mb = kb * 1024, gb = mb * 1024;
  if (n >= gb) return (n / gb).toFixed(2) + " GB";
  if (n >= mb) return (n / mb).toFixed(2) + " MB";
  if (n >= kb) return (n / kb).toFixed(1) + " KB";
  return n + " B";
}

module.exports = { scrapeCapterra };
