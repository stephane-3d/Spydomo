# Spydomo — Claude Code Context

## Product

Spydomo is an AI-powered competitive intelligence platform for B2B SaaS teams. It turns reviews, social chatter, and launch news into curated briefs so teams can spot opportunities and risks faster.

**Core workflow:**
1. **Discover** — Map a company to high-signal sources (reviews, communities, social, blogs)
2. **Collect** — Pull new content on a cadence, keeping raw sources linked
3. **Distill** — AI extracts themes, pain points, launches, positioning shifts — structured and searchable
4. **Compare** — Track history to see what's new, recurring, and trending
5. **Deliver** — Weekly brief + clean UI for the team, no extra work required

---

## Tech Stack

| Layer | Technology |
|---|---|
| Framework | .NET 10 / C# |
| UI | Blazor Server + MudBlazor 8.x |
| Database | SQL Server + Entity Framework Core 10 |
| Auth | Clerk.NET (JWT-based) |
| Background jobs | Hangfire (SQL Server storage) |
| AI | OpenAI SDK |
| Web scraping | Playwright, PuppeteerSharp, HtmlAgilityPack, BrightData proxy |
| Email | Azure Communication Services (ACS) + SendGrid |
| Billing | Stripe |
| Monitoring | Azure Application Insights |
| Fuzzy matching | FuzzySharp |
| Culture | en-CA |

---

## Solution Structure

```
Spydomo.sln
├── Spydomo.Web/          # Blazor app + public website, controllers, Razor components
├── Spydomo.Worker/       # Hangfire job runner — all scheduled data processing
├── Spydomo.Infrastructure/ # Core business logic, services, AI, scrapers, parsers
├── Spydomo.Models/       # EF Core entities + SpydomoContext (DbContext)
├── Spydomo.DTO/          # Data Transfer Objects
├── Spydomo.Common/       # Shared enums and constants
├── Spydomo.Utilities/    # Helper utilities
└── Spydomo.Node/         # Node.js tooling
```

### Project dependencies
- **Web** → Infrastructure, Models, DTO, Common, Utilities
- **Worker** → Infrastructure, Models
- **Infrastructure** → Models, DTO, Common, Utilities
- **Models** → DTO, Common, Utilities

### Key folders in Infrastructure
- `AiServices/` — OpenAI integrations
- `Parsers/` — Source-specific content parsers
- `PulseRules/` — Signal detection rules
- `Interfaces/` — Service interfaces
- `Clients/` — HTTP clients (including WorkerAdminClient)
- `Extensions/` — DI registration helpers (`AddSpydomoShared`, `AddSpydomoWeb`)

### Key models (Spydomo.Models)
`Company`, `Competitor`, `DataSource`, `RawContent`, `SemanticSignal`, `StrategicSummary`, `TrackedCompany`, `CompanyGroup`, `SnapshotJob`, `User`, `Client`

---

## Architecture Notes

- **DbContext**: Uses `IDbContextFactory<SpydomoContext>` (pooled) — never inject `SpydomoContext` directly via DI. Always resolve via the factory.
- **Worker ↔ Web**: Worker exposes an HTTP API; Web communicates with it via `IWorkerAdminClient`.
- **Authentication**: Clerk handles auth. A `ClerkUserSyncMiddleware` syncs user data on `/app/**` routes. Unauthenticated users are redirected to `/app/login`.
- **Routing**: `/app/signup` and `/app/login` map to static HTML files in `wwwroot/auth/`.
- **Blazor render mode**: Interactive Server (`AddInteractiveServerComponents`).

---

## Common Commands

```bash
# Build the full solution
dotnet build Spydomo.sln

# Run the web app
dotnet run --project Spydomo.Web/Spydomo.Web.csproj

# Run the worker
dotnet run --project Spydomo.Worker/Spydomo.Worker.csproj

# Add an EF migration (run from solution root)
dotnet ef migrations add <MigrationName> --project Spydomo.Models --startup-project Spydomo.Web

# Apply migrations
dotnet ef database update --project Spydomo.Models --startup-project Spydomo.Web
```

---

## Conventions

- Use **interfaces** for services in Infrastructure; register them via the extension methods in `ServiceCollectionExtensions.cs`
- **Nullable reference types** are enabled across all projects
- **Implicit usings** are enabled — no need to add `using System;` etc.
- MudBlazor components for all UI — do not introduce other component libraries
- Keep business logic in **Infrastructure**, not in Web controllers or Blazor components
- Do not use `SpydomoContext` directly via constructor injection — always use `IDbContextFactory<SpydomoContext>`
