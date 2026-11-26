# Decisions taken while building

The owner asked for the whole plan to be built without stopping for the open questions, so each open decision below got
a sensible default. Every one is a configuration switch or an isolated module; change any of them without touching the rest.

| # | Open decision (PLAN.md) | Default used | How to change |
|---|---|---|---|
| 1 | Exact AI model | Not hard-coded. `AI:Model` (or the admin page). Adapters use the OpenAI **Responses API** (`web_search` tool + strict JSON schema in one call) and Gemini (two-call fallback). "GPT Luna" is still unverified — run Phase 0 | `/admin/ai` or `AI:*` settings |
| 2 | Launch provider | **OpenAI** adapter is the reference; Azure AI Foundry (same Responses API at `{endpoint}/openai/v1/responses`) and Google Gemini are also built (Phase 8) | `AI:Provider` |
| 3 | Email at sign-up | **Optional** email field. With email: password reset by link. Without: the form says the password can't be recovered | `Register.cshtml` |
| 4 | Budget cap | `Limits:DailyBudgetUsd = 25` (0 = no cap) plus Azure monthly budget alerts in Bicep | settings |
| 5 | Shopping preferences | **Not in v1** | — |
| 6 | Pattern engine | **Own C# drafters**: T-shirt, A-line pull-on skirt, pull-on trousers. No Node sidecar | `Infrastructure/Patterns` |
| 7 | Azure region | `westeurope` in `infra/main.parameters.json` | parameter |
| 8 | Lawyer review | Legal pages say "Draft text: final wording needs legal review" | before launch |

## Technical choices and deviations to review

* **EF provider**: Pomelo has no EF Core 10 release (latest 9.0.0), so per the plan's fallback the solution uses Oracle's
  `MySql.EntityFrameworkCore` 10.0.12. Migrations are generated for MySQL; SQLite is only used by tests and the Demo profile.
* **Package versions** were checked against nuget.org, not memory: EF Core / ASP.NET 10.0.15, Duende IdentityServer 8.0.11,
  ImageSharp 4.1.4 (3.1.x has published CVEs), QuestPDF 2026.9.2 (Community licence: free under USD 1M revenue),
  Blazor.BrowserExtension 5.1.0, Azure.AI.ContentSafety 1.0.0.
* **Licences to buy before launch**: Duende IdentityServer (free in development only), Six Labors ImageSharp 4.x (its
  build prints "Please obtain a license"; a free community key covers qualifying open-source / small-revenue use) and
  QuestPDF depending on revenue.
* **Extension service worker is JavaScript**, not C#: `chrome.sidePanel.open()` must be called synchronously inside the
  context-menu click. The popup and side panel are Blazor and reuse the website's `ResultsView`.
* **Demo AI provider and moderation bypass** exist for local UI work and automated tests. Both are chosen explicitly
  and refused at start-up outside `Development`; the site shows a "Demo mode" banner while active.
* **Moderation runs during upload** (before anything is stored), so a rejected photo never reaches storage or the AI.
* **Image links** are short-lived HMAC-signed URLs (`Api:UrlSigningKey`), so `<img>` tags and PDF downloads work without
  auth headers.
* **"Open full results" in the side panel** only shows for signed-in users: anonymous extension and website visitors have
  different visitor ids, and putting the id in a URL would leak it.
* **Bug fixes found by the tests**: (1) deleting an account raced the database cascade (pattern jobs deleted twice);
  (2) an edited (fix-a-box) look could be served from the image-hash cache to someone uploading the same photo.
