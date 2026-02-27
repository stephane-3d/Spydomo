// server.js
const express = require('express');
const cors = require('cors');

const { extractFromUrl, extractFromHtml, renderHtmlFromUrl } = require('./readability');
const { scrapeCapterra } = require('./capterra-scraper');

const app = express();
const PORT = process.env.PORT || 3000;

const READABILITY_MAX_CONCURRENT = parseInt(process.env.READABILITY_MAX_CONCURRENT || "2", 10);

// Capterra scrapes must be serialised: concurrent BrightData sessions on the
// same domain trigger Cloudflare CAPTCHAs. Queue depth is unlimited; each
// request waits its turn. A cooldown between sessions further reduces
// fingerprinting risk.
const SCRAPE_COOLDOWN_MS = parseInt(process.env.SCRAPE_COOLDOWN_MS || "5000", 10);

const pLimit = require("p-limit");
const limit = pLimit(2); // <= max concurrent extracts

class Semaphore {
    constructor(max) {
        this.max = Math.max(1, max);
        this.current = 0;
        this.queue = [];
    }

    acquire() {
        if (this.current < this.max) {
            this.current++;
            return Promise.resolve();
        }
        return new Promise((resolve) => this.queue.push(resolve));
    }

    release() {
        const next = this.queue.shift();
        if (next) {
            // hand the slot to the next waiter (current stays the same)
            next();
        } else {
            this.current = Math.max(0, this.current - 1);
        }
    }

    async run(fn) {
        await this.acquire();
        try {
            return await fn();
        } finally {
            this.release();
        }
    }
}

const readabilitySem = new Semaphore(READABILITY_MAX_CONCURRENT);
console.log(`🧵 Readability concurrency limit = ${READABILITY_MAX_CONCURRENT}`);

const scrapeSem = new Semaphore(1); // max 1 Capterra session at a time
console.log(`🧵 Capterra scrape concurrency = 1, cooldown = ${SCRAPE_COOLDOWN_MS}ms`);

let active = 0;
function logActive(delta, label) {
  active += delta;
  console.log(`[extract] ${label} active=${active}`);
}


app.use(cors());
app.use(express.json({ limit: '10mb' }));
app.use(express.urlencoded({ limit: '20mb', extended: true }));

app.use((req, res, next) => {
  console.log("➡️", req.method, req.url, "pid", process.pid);
  next();
});


// --- Readability endpoints ---
// ReadabilityService.BaseUrl = "https://node.spydomo.com/readability"
// => POST {BaseUrl}/extract  with { url }
// => POST {BaseUrl}/extract-html with { html, url }

app.post('/readability/extract', async (req, res) => {
  console.log("🔥 HIT /readability/extract", req.body);

  const body = req.body || {};
  const { url } = body;
  if (!url) return res.status(400).json({ error: 'Missing URL' });

  logActive(+1, "start");
  try {
    const rid = `${Date.now().toString(36)}-${Math.random().toString(16).slice(2,6)}`;
    const result = await limit(() =>
      withTimeout(renderHtmlFromUrl(url, { ...body, _rid: rid }), 65000, "extract timed out")
    );
    res.json(result);
  } catch (err) {
    console.error('❌ /readability/extract failed:', err);
    res.status(500).json({ error: 'Failed to extract content' });
  } finally { logActive(-1, "end"); }
});

app.post('/readability/extract-html', async (req, res) => {
    const { html, url } = req.body;

    console.log('Received /readability/extract-html payload:', {
        htmlLength: html?.length,
        url
    });

    if (!html || !url) {
        return res.status(400).json({ error: 'Missing html or url' });
    }

    try {
        const result = await extractFromHtml(html, url);
        res.json(result);
    } catch (err) {
        console.error('❌ /readability/extract-html failed:', err);
        res.status(500).json({ error: 'Failed to parse HTML' });
    }
});

app.post('/readability/render-html', async (req, res) => {
  const body = req.body || {};
  const { url } = body;
  if (!url) return res.status(400).json({ error: 'Missing URL' });

  const rid = `${Date.now().toString(36)}-${Math.random().toString(16).slice(2,6)}`;

  logActive(+1, "render start");
  try {
    // carry-through opts like debug, extraWaitMs, scroll, includeHtml, etc.
    const baseOpts = { ...body, _rid: rid };

    const lite = await renderHtmlFromUrl(url, {
      ...baseOpts,
      mode: "lite",
      includeHtml: false,
      allowFullAnchorScan: false
    });

    if (!lite.ok) return res.status(502).json(lite);
    if (lite.socialLinks?.length) return res.json(lite);

    const full = await renderHtmlFromUrl(url, {
      ...baseOpts,
      mode: "full",
      scroll: true,
      extraWaitMs: Number.isFinite(body.extraWaitMs) ? body.extraWaitMs : 800,
      includeHtml: false,
      allowFullAnchorScan: false
    });

    if (!full.ok) return res.status(502).json(full);
    if (full.socialLinks?.length) return res.json(full);

    const desperate = await renderHtmlFromUrl(url, {
      ...baseOpts,
      mode: "full",
      scroll: true,
      extraWaitMs: Number.isFinite(body.extraWaitMs) ? body.extraWaitMs : 800,
      includeHtml: false,
      allowFullAnchorScan: true
    });

    return res.json(desperate);
  } catch (err) {
    console.error('❌ /readability/render-html failed:', err);
    res.status(500).json({ error: 'Failed to render HTML' });
  } finally {
    logActive(-1, "render end");
  }
});



// --- Capterra scraper endpoint ---
// CapterraScraper.BaseUrl = "https://node.spydomo.com/scrape"
// => POST {BaseUrl} with { url }

app.post('/scrape', async (req, res) => {
    const { url } = req.body;
    if (!url) {
        return res.status(400).json({ error: 'Missing URL' });
    }

    const queue = scrapeSem.queue.length;
    if (queue > 0) {
        console.log(`⏳ /scrape queued (${queue} ahead) for ${url}`);
    }

    try {
        const html = await scrapeSem.run(async () => {
            const result = await scrapeCapterra(url);
            // Cooldown between sessions to reduce Cloudflare fingerprinting
            await new Promise(r => setTimeout(r, SCRAPE_COOLDOWN_MS));
            return result;
        });
        res.send(html);
    } catch (err) {
        console.error('❌ /scrape failed:', err);
        res.status(500).json({ error: err.message || 'Scraping failed' });
    }
});

// Simple health check
app.get('/', (req, res) => {
    res.send('spydomo-node is running');
});

app.listen(PORT, "0.0.0.0", () => {
  console.log(`🚀 spydomo-node listening on port ${PORT}`);
});

app.get("/ready", async (req, res) => {
  // Keep it lightweight; don't scrape anything here.
  res.status(200).json({ ok: true });
});

// Simple health check (for Azure "Health check" feature)
app.get('/health', (req, res) => {
  res.json({
    ok: true,
    service: "spydomo-node",
    timeUtc: new Date().toISOString(),
    usingBrightData: !!process.env.BRIGHTDATA_BROWSER_WS
  });
});

function listRoutes(app) {
  const routes = [];
  app._router?.stack?.forEach((m) => {
    if (m.route) {
      const methods = Object.keys(m.route.methods).join(",").toUpperCase();
      routes.push(`${methods} ${m.route.path}`);
    } else if (m.name === "router" && m.handle?.stack) {
      m.handle.stack.forEach((h) => {
        if (h.route) {
          const methods = Object.keys(h.route.methods).join(",").toUpperCase();
          routes.push(`${methods} ${h.route.path}`);
        }
      });
    }
  });
  console.log("📌 Registered routes:\n" + routes.sort().join("\n"));
}
listRoutes(app);


function withTimeout(promise, ms, label = "timeout") {
    return Promise.race([
        promise,
        new Promise((_, reject) => setTimeout(() => reject(new Error(label)), ms)),
    ]);
}