# Outfit Finder — Phased Build Plan (v2, consolidated)

Upload a photo of a person → AI finds every item they're wearing → finds where to buy each one, or gives a sewing pattern to make it → saved to the user's history.
Delivered as a **Blazor website** and a **Chrome extension**, in C# / .NET.

This version replaces the earlier plan: every decision made so far is folded in, and the work is laid out as phases to execute one at a time. **Do not start a phase until the previous phase's "Done when" list is fully ticked.**

Design reference (clickable prototype, light/dark, mobile): https://claude.ai/artifact/9hciHP2fFCYya94QjYuD7f#play-Prototype

---

## Decisions (locked)

| Area | Decision |
|---|---|
| Stack | C#, Blazor Web App (site), Blazor WASM via `Blazor.BrowserExtension` (Chrome MV3 extension) |
| Database | **MySQL 8**, all access through **EF Core** (no raw SQL). Docker locally, Azure Database for MySQL in cloud. Provider chosen by config so it can be swapped |
| Background work | Built-in EF-backed queue (hosted worker polls `Pending` rows). No Hangfire |
| Auth | **Duende IdentityServer** + ASP.NET Core Identity, username + password, sign-up and sign-in |
| Anonymous history | `gf_visitor` cookie (set after consent); merged into the account at sign-in |
| AI | **Configurable provider**: Azure AI Foundry, OpenAI, or Google. One interface (`IOutfitAnalyzer`), one adapter each. Instructions + JSON schema live in our repo and are sent with each call |
| Search | The model's **own built-in web search** (no Bing grounding). Any store, no allowlist |
| Safety | Azure AI Content Safety on every image before anything else; faces blurred; only the blurred copy stored; 90-day retention |
| Make it | Parametric sewing patterns (engine: FreeSewing sidecar *or* own C# drafters — **open decision**), life-size tiled PDF, computed materials list |
| Design | Shared UI library; one `BrandName` variable; one `tokens.css` for colors/fonts/shapes; light + dark; mobile-first with hamburger menu |
| Extension | Right-click any image → **"Find with AI"** → results in the side panel. Install page on the website |
| Repo | New repo, created by you |

## Open decisions (answer before the phase that needs them)

| # | Question | Needed by |
|---|---|---|
| 1 | Exact AI model name, and does it support image input + its own web search? ("GPT Luna" unconfirmed) | Phase 0 |
| 2 | Which provider to launch on (first adapter built) | Phase 4 |
| 3 | Collect email at sign-up? Without it there is **no password reset** | Phase 2 |
| 4 | Business model / monthly budget cap for AI costs | Phase 4 |
| 5 | Shopping preferences (country, men's/women's, budget) in v1? | Phase 5 |
| 6 | Pattern engine: FreeSewing (JavaScript sidecar) or own C# code (3 garments to start) | Phase 9 |
| 7 | Azure region (EU region if serving EU users) | Phase 10 |
| 8 | Lawyer review of Privacy Policy / Terms / face-detection question | Before public launch |

---

## Solution layout

```
OutfitFinder.sln
├─ src/
│  ├─ OutfitFinder.Core            Entities, DTOs, interfaces, prompts + JSON schema, Brand constants
│  ├─ OutfitFinder.Infrastructure  EF DbContext + migrations, AI adapters, moderation, blob store, imaging, PDF
│  ├─ OutfitFinder.Identity        Duende IdentityServer + ASP.NET Identity (sign-in / register pages)
│  ├─ OutfitFinder.Api             Upload, search worker, history, admin, privacy endpoints
│  ├─ OutfitFinder.UI              Shared Razor components, tokens.css, fonts, logo, legal Markdown
│  ├─ OutfitFinder.Web             Blazor Web App (public site)
│  └─ OutfitFinder.Extension       Blazor WASM Chrome extension
├─ tests/OutfitFinder.Tests        xUnit + bUnit; SQLite for unit, Testcontainers MySQL for integration
├─ tests/photos/                   ~10 fixed test photos + expected results (AI regression set)
├─ infra/                          Bicep
├─ docker-compose.yml              mysql:8 + Azurite
└─ Directory.Build.props           BrandName, BrandTagline, warnings-as-errors
```

---

# Phases

## Phase 0 — Prove the core (no app code)
**Goal:** know the AI can do the job before building around it.
1. In the provider's playground, deploy/select the model; confirm image input and built-in web search are available together.
2. Run the draft instructions (Appendix A) on 10 real outfit photos.
3. For each photo record: items found vs. actually present, bounding-box quality, number of product links, how many links open a real product page, price correctness, time taken, cost.
4. Check whether web search + strict JSON output work in one call; if not, note that the adapter needs two calls.
5. Save the 10 photos and notes into `tests/photos/`.

**Done when:** ≥ 80% of items found; ≥ 60% of returned links are live product pages; a search finishes in under 2 minutes; cost per photo is known. If not met, stop and revisit the search design.

## Phase 1 — Skeleton and database
**Goal:** an empty solution that builds, runs and migrates.
1. Create the solution and projects above; `Directory.Build.props` with `BrandName` ("Outfit Finder"), `TreatWarningsAsErrors`.
2. Generate `Brand.Name` / `Brand.Tagline` constants into Core from the MSBuild property.
3. `docker-compose.yml`: `mysql:8` (utf8mb4) + Azurite.
4. EF provider: `Pomelo.EntityFrameworkCore.MySql` — **confirm it supports the EF Core version in use**; otherwise Oracle's `MySql.EntityFrameworkCore` or drop to .NET 9. Provider selected by `Database:Provider` in one `switch`.
5. Entities + configurations (Appendix B); enums stored as strings, UTC `datetime(6)`, Guid as `char(36)`.
6. First migration; `dotnet ef database update` works against Docker MySQL.
7. Test project wired: one SQLite unit test, one Testcontainers-MySQL integration test.
8. `.gitignore`, README with run steps, user-secrets initialised.

**Done when:** `dotnet build` clean; `dotnet test` green; the database is created by migrations only.

## Phase 2 — Identity
**Goal:** sign up, sign in, sign out; API protected.
1. `OutfitFinder.Identity`: Duende IdentityServer + ASP.NET Identity on the same MySQL database (own EF contexts + migrations).
2. Pages: Register, Sign in, Sign out — built with the shared UI styles. Username + password (+ email if decision 3 = yes, with reset flow).
3. Password rules, lockout after failed attempts, CAPTCHA (Cloudflare Turnstile) on register.
4. Clients: `web` (auth code + PKCE, cookie session) and `extension` (auth code + PKCE, refresh tokens, redirect `https://<extension-id>.chromiumapp.org/`). Scope `outfit.api`.
5. Api validates bearer tokens; Web signs in through OIDC.
6. Register consent checkbox (16+, Terms, Privacy) recorded in `ConsentRecord`.
7. Roles: `Admin` seeded for one configured username.

**Done when:** a new user can register, sign out, sign in; an unauthenticated API call to a protected endpoint returns 401; lockout works; tests cover each.

## Phase 3 — Design system, shell and mobile
**Goal:** the site looks like the prototype, on desktop and phone, in both themes, with no features yet.
1. `OutfitFinder.UI/wwwroot/css/tokens.css`: every color, font, radius, spacing, shadow as CSS variables; light values on `:root`, dark values under `[data-theme=dark]` and `prefers-color-scheme`.
2. `app.css` base styles; self-hosted fonts in `wwwroot/fonts/` (`fonts.css`); logo SVG.
3. Components: `AppHeader` (with hamburger menu below 760 px), `ThemeToggle` (remembers choice), `AppFooter`, `Button`, `Chip/Tab`, `Card`, `ColorBlock`, `Field`, `Badge`, `Spinner/ProgressSteps`, `EmptyState`, `CookieBanner`.
4. Layouts and empty pages: Home, Results, My looks, Install, Sign in, Account & privacy, Legal, Admin.
5. Hamburger: opens/closes, closes on navigation, `aria-expanded`, keyboard reachable, 44 px+ touch targets.
6. Guard tests: no hex color or font name outside `tokens.css`/`fonts.css`; no hard-coded brand name.
7. Playwright checks at 390 px and 1280 px, light and dark: no horizontal scroll, menu opens, every page renders.

**Done when:** all pages render at both widths in both themes with no horizontal scroll; changing `BrandName` or a token changes every page.

## Phase 4 — Search pipeline (the core feature)
**Goal:** upload a photo, get stored results.
1. `POST /api/searches` (multipart): type check by file signature (jpg/png/webp), ≤ 10 MB, re-encode + resize to ≤ 2048 px (strips EXIF/GPS) with ImageSharp. Creates `OutfitSearch` (Pending).
2. Worker (hosted service): claims a Pending row with a concurrency token; retries stuck jobs up to 3 times; marks Failed with a user-readable error.
3. **Moderation first**: Azure AI Content Safety image check; reject at configured severity; record `ModerationEvent` (hash + reason only); never store or forward a rejected image.
4. `IOutfitAnalyzer` + the **first provider adapter** (decision 2): sends image + instructions + schema with web search on; validates the JSON against the schema.
5. Agent flags handled: no clothing → friendly message; apparent minor or inappropriate → refuse, do not store.
6. Face blur: pad face boxes 25%, heavy blur, save **only** the blurred image to Blob.
7. Link check: request each URL (5 s timeout, parallel); drop dead links; never render model output as HTML.
8. Save `OutfitItem` + `ItemMatch`; record provider + model on the search; cache by image hash.
9. Rate limits per visitor / user / IP; global daily budget cap that pauses searches and alerts.
10. `GET /api/searches/{id}` for polling; run the Phase 0 photo set as an automated regression check (manual trigger, costs money).

**Done when:** a real photo goes in and items + verified links come out; a nudity test image is rejected and nothing is stored; an unconfigured provider refuses loudly; the worker survives a restart mid-job.

## Phase 5 — Results, history and visitor cookie
**Goal:** the full "Buy it" experience on the site.
1. Home upload (drag/drop, file picker, phone camera) → loading screen with live steps → results.
2. Results: photo with numbered markers, item chips, match cards (Exact/Similar badge, store, price, "View at store"), whole-look price range, "found on" date, "Search again", delete.
3. Fix-a-box editor: untick an item, drag a box, draw a new one → re-search that item only.
4. Cookie banner; `gf_visitor` set only after consent; sent to the Api as `X-Visitor-Id`.
5. My looks (history) for anonymous visitors and signed-in users; merge visitor searches into the account at sign-in.
6. Thumbs up/down per match (stored for quality tracking). Preferences if decision 5 = yes.
7. "Report a problem" link on results.

**Done when:** an anonymous visitor can search, see history, sign up, and still see the same history on another device after signing in.

## Phase 6 — Chrome extension
**Goal:** right-click any image → "Find with AI" → results.
1. `OutfitFinder.Extension` with `Blazor.BrowserExtension`; fixed extension ID (key in manifest); name from `BrandName`.
2. **Context menu "Find with AI"** on images (`contexts: ["image"]`). On click:
   a. open the side panel immediately (must happen inside the click handler);
   b. send the image URL to `POST /api/searches/from-url`;
   c. if the server cannot fetch it (login-only or expiring image links), fall back to grabbing the image bytes from the page (`activeTab` + `scripting`) and uploading them;
   d. side panel shows the same loading steps, then the same results component as the site; "Open full results" links to the website.
3. Server-side URL fetch protections: https only, block private/loopback/metadata addresses (after DNS resolution and on redirects), 10 MB / 10 s limits, image types only.
4. First use shows the one-time upload notice (photo is sent to AI, faces blurred, 90-day retention, you have the right to use this photo).
5. Popup: upload from disk, last 3 looks, sign in/out. Sign-in via `chrome.identity.launchWebAuthFlow`. Anonymous use allowed with a visitor id in `chrome.storage`.
6. Permissions: `contextMenus`, `sidePanel`, `storage`, `identity`, `activeTab`, `scripting`; host permission for the API only.
7. Website `/install` page: "Add to Chrome" (Web Store) + "Download for manual install" (zip + steps), browser detection, installed/update-available check, permissions explained.
8. CI zips the extension and publishes `latest.json`.

**Done when:** on three different sites (including one with login-only images) right-click → "Find with AI" opens the side panel and returns results; manual install from the website works on a clean Chrome profile.

## Phase 7 — Privacy, legal and abuse hardening
**Goal:** safe to show to the public.
1. Legal pages from Markdown with version + effective date: Privacy, Terms, Cookies, Takedown form. Linked in footer and extension.
2. Re-consent flow when a document version changes.
3. Account & privacy: download my data (JSON + blurred images zip), delete account and data, clear history, delete one search, edit/delete measurements.
4. Nightly retention job (delete after `Retention:Days`, default 90).
5. Auto-block after repeated moderation rejections; CAPTCHA on anonymous upload; safe-browsing check on outbound links.
6. Logging/monitoring (Application Insights), cost alert on the AI resource, audit log for admin changes.
7. Drafted legal text sent for lawyer review (decision 8).

**Done when:** export and delete both verified against the database and Blob storage (nothing left behind); retention job proven with back-dated rows.

## Phase 8 — AI provider admin and remaining adapters
**Goal:** switch provider without redeploying.
1. Remaining two adapters (Azure AI Foundry / OpenAI / Google); two-call fallback where search + strict JSON can't combine.
2. `/admin/ai` (Admin role): provider, model/deployment, endpoint, API key (stored encrypted; last 4 shown), web search toggle, **Test connection** (runs a sample photo; shows items, working links, time, cost estimate), Save. Privacy warning on provider change. Audit-logged.
3. Run the Phase 0 photo set against each provider; record the comparison.

**Done when:** switching provider in the admin page changes the next search with no restart; keys never appear in logs or responses.

## Phase 9 — Make it yourself
**Goal:** pattern, materials list and printable life-size PDF for sewn garments.
1. Agent returns make-it fields per item (garment type, fit, lengths, neckline, closures, fabric suggestion, difficulty, makeable); logos/branded prints excluded.
2. Measurements: standard size chart or own measurements (`MeasurementProfile`).
3. Pattern engine per decision 6 → vector pieces with seam allowance, grainline, notches, labels.
4. PDF generator: tiled 1:1 on Letter/A4 with page grid, overlap, alignment marks, assembly map, 5 cm / 2 in test square; plus single-sheet copy-shop version.
5. Materials list computed from the pieces (fabric yardage at 45/60 in widths, notions); agent finds where to buy each line.
6. Step-by-step instructions; "inspired by, not an exact copy" note; "No pattern for this piece yet" for unsupported items.
7. **Print a real pattern and measure it with a ruler.**

**Done when:** a printed test square measures exactly 5 cm; tiled pages align; a T-shirt pattern in size M matches the size chart within 5 mm.

## Phase 10 — Azure and launch
**Goal:** live, monitored, installable.
1. Bicep: App Services (Identity, Api, Web), MySQL Flexible Server, Storage, Key Vault, Content Safety, managed identities, App Insights. Region per decision 7.
2. Secrets in Key Vault; no keys in `appsettings.json`.
3. GitHub Actions: build → test → deploy; extension zip artifact.
4. Duende licence registered. Chrome Web Store listing (privacy policy URL, permission justifications, screenshots) submitted; `/install` button switched to the store once approved.
5. Smoke test in production: sign up, search, extension search, export, delete.
6. Budget cap and alerts verified by forcing them in a test.

**Done when:** the production smoke test passes end to end and the Web Store listing is approved.

---

## Appendix A — AI instructions (starting draft)

```
You analyze a photo of a person and find where to buy what they are wearing.
1. Identify every visible clothing item and accessory (tops, outerwear, bottoms, dresses,
   shoes, bags, hats, eyewear, jewelry, belts, watches).
2. For each: category, detailed description (color, material, cut, pattern, details),
   visible brand/logo text, bounding box as 0–1 fractions of the image.
3. Return a generously padded bounding box for every human face.
4. For each item, use web search to find up to 5 products currently for sale from
   reputable online stores. Prefer the exact product; otherwise the closest similar ones.
5. Mark "exact" only when brand AND style are confirmed by the product page; else "similar".
6. Only return URLs that appeared in your search results. Never construct or guess a URL.
7. Prices as numbers with ISO currency code. Skip out-of-stock products.
8. Do not identify, name, or describe the person.
9. Set flags: noPersonOrClothing, appearsToBeMinor, inappropriate.
Respond only with JSON matching the provided schema.
```

## Appendix B — Data model

```
Visitor            Id (Guid), CreatedUtc, UserId?
OutfitSearch       Id, VisitorId, UserId?, BlurredBlobPath, ImageHash, Status, Error?, Provider, Model,
                   Source (Web|ExtensionUpload|ExtensionUrl), ClaimedUtc?, AttemptCount, RowVersion,
                   CreatedUtc, CompletedUtc?
OutfitItem         Id, OutfitSearchId, Category, Description, BrandHints?, BoxX/Y/W/H, Confidence, IsUserAdjusted
ItemMatch          Id, OutfitItemId, Store, Title, Price?, Currency?, Url, ImageUrl?, MatchType, Rank,
                   LinkVerified, Feedback?
ModerationEvent    Id, ImageHash, Reason, VisitorId?, UserId?, CreatedUtc
ConsentRecord      Id, VisitorId?, UserId?, Document, Version, AcceptedUtc
MeasurementProfile Id, UserId?/VisitorId?, Name, MeasurementsJson
PatternJob         Id, OutfitItemId, Block, OptionsJson, MeasurementsSnapshot, Status, PdfBlobPath, A0PdfBlobPath
MaterialLine       Id, PatternJobId, Kind, Description, Quantity, Unit, Url?, Price?
AiProviderSetting  Id, Provider, Model, Deployment?, Endpoint?, EncryptedKey, WebSearchEnabled, UpdatedBy, UpdatedUtc
(+ ASP.NET Identity and Duende IdentityServer tables via their own EF contexts)
```

## Appendix C — Settings

```json
"Database": { "Provider": "MySql" },
"ConnectionStrings": { "OutfitDb": "" },
"Storage": { "BlobConnection": "", "Container": "outfits" },
"AI": { "Provider": "", "Model": "", "Deployment": "", "Endpoint": "", "ApiKey": "", "WebSearchEnabled": true, "TimeoutSeconds": 120 },
"Safety": { "ContentSafetyEndpoint": "", "RejectSeverity": 2 },
"Limits": { "SearchesPerVisitorPerDay": 5, "SearchesPerUserPerDay": 25, "DailyBudgetUsd": 0 },
"Retention": { "Days": 90 },
"Extension": { "Id": "", "WebStoreUrl": "", "ManualDownloadEnabled": true }
```
Keys: `dotnet user-secrets` locally, Key Vault in Azure. Never commit a key.
