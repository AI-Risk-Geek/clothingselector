# Outfit Finder (placeholder name)

Upload a photo of an outfit → the AI finds every piece → where to buy each one ("Buy it") or a sewing pattern sized to you
("Make it"). A Blazor Server website, a Chrome extension (Blazor WASM), an API with a background search worker, and an
identity server — all C# / .NET 10. Start with [HANDOFF.md](HANDOFF.md) and [docs/PLAN.md](docs/PLAN.md); the decisions
made while building are in [docs/DECISIONS.md](docs/DECISIONS.md).

## Solution

| Project | What it is | Local URL |
|---|---|---|
| `OutfitFinder.Core` | Entities, DTOs, interfaces, AI instructions + JSON schema, `Brand` constants (generated from `BrandName`) | |
| `OutfitFinder.Infrastructure` | EF Core (MySQL / SQLite), migrations, AI adapters, Content Safety, blob store, imaging, pattern engine, PDF | |
| `OutfitFinder.Identity` | Duende IdentityServer + ASP.NET Identity (register, sign in, forgot password) | https://localhost:44301 |
| `OutfitFinder.Api` | Upload, search worker, history, patterns, privacy, admin endpoints | https://localhost:44302 |
| `OutfitFinder.Web` | Blazor Server website | https://localhost:44303 |
| `OutfitFinder.UI` | Shared Razor components, `tokens.css`, fonts, legal Markdown, API client | |
| `OutfitFinder.Extension` | Chrome MV3 extension (Blazor WASM pages + JS service worker) | |
| `tests/OutfitFinder.Tests` | xUnit + bUnit; SQLite unit tests, Api/Identity integration, Testcontainers MySQL | |

## Run it in Visual Studio

Open `OutfitFinder.sln` (Visual Studio 2022 17.12+ or 2026). In the start-up dropdown on the toolbar pick
**IIS Express (all three)** and press F5. It starts the sign-in server, the API and the website together; the website
opens at https://localhost:44303. (**Kestrel (demo)** does the same without IIS Express.)

The website can't run on its own: it signs people in through the sign-in server and gets results from the API, so always
start all three. If the start-up dropdown doesn't list these, right-click the solution > **Configure Startup Projects**
> **Multiple startup projects** and set Identity, Api and Web to **Start**.

Everything it needs is in each project's `appsettings.Development.json` (local development only): SQLite in `.local/`, a
local blob folder, the **Demo** AI provider (sample results and a yellow "Demo mode" banner) and the development moderation
bypass, which are refused outside `Development`. The admin account is the one named `admin`; set a real AI provider on
`/admin/ai`. Never put production keys in these files.

## Run the real setup from a terminal

```powershell
docker compose up -d
```

One-time secrets. User-secrets override `appsettings.Development.json`, so also switch the database and storage:

```powershell
$cs = "server=localhost;port=3306;database=outfitfinder;user=outfit;password=local-dev-only"
dotnet user-secrets --project src/OutfitFinder.Api set ConnectionStrings:OutfitDb $cs
dotnet user-secrets --project src/OutfitFinder.Api set Database:Provider MySql
dotnet user-secrets --project src/OutfitFinder.Identity set Database:Provider MySql
dotnet user-secrets --project src/OutfitFinder.Api set Storage:Provider Azure
dotnet user-secrets --project src/OutfitFinder.Api set Storage:BlobConnection "UseDevelopmentStorage=true"
dotnet user-secrets --project src/OutfitFinder.Api set Safety:Mode AzureContentSafety
dotnet user-secrets --project src/OutfitFinder.Identity set ConnectionStrings:OutfitDb $cs
dotnet user-secrets --project src/OutfitFinder.Identity set Clients:Web:Secret "choose-a-long-random-secret"
dotnet user-secrets --project src/OutfitFinder.Web set Identity:ClientSecret "choose-a-long-random-secret"
dotnet user-secrets --project src/OutfitFinder.Identity set Admin:Username "your-username"
dotnet user-secrets --project src/OutfitFinder.Api set AI:Provider OpenAI
dotnet user-secrets --project src/OutfitFinder.Api set AI:Model "your-model"
dotnet user-secrets --project src/OutfitFinder.Api set AI:ApiKey "your-key"
dotnet user-secrets --project src/OutfitFinder.Api set Safety:ContentSafetyEndpoint "https://<resource>.cognitiveservices.azure.com/"
dotnet user-secrets --project src/OutfitFinder.Api set Safety:ContentSafetyKey "your-key"
```

Start the three apps (each in its own terminal):

```powershell
dotnet run --project src/OutfitFinder.Identity --launch-profile "Kestrel (demo)"
```

```powershell
dotnet run --project src/OutfitFinder.Api --launch-profile "Kestrel (demo)"
```

```powershell
dotnet run --project src/OutfitFinder.Web --launch-profile "Kestrel (demo)"
```

In Development the Api and Identity apply migrations on start. Elsewhere run them explicitly:

```powershell
dotnet ef database update -p src/OutfitFinder.Infrastructure -c OutfitDbContext
```

If the AI provider isn't configured the Api logs a CRITICAL line at start-up and every search fails with
"Searching isn't set up yet: AI:Provider is not set…" — there is no silent fallback.

## Tests

```powershell
dotnet test
```

The MySQL test starts `mysql:8.4` with Testcontainers and is skipped if Docker isn't running. The AI regression set
(`tests/photos`) runs only with `OF_AI_REGRESSION=1` because it costs money.

## Chrome extension

```powershell
dotnet build src/OutfitFinder.Extension
```

Load `src/OutfitFinder.Extension/bin/Debug/net10.0/browserextension` from `chrome://extensions` → Developer mode →
Load unpacked. The manifest has a fixed `key`, so the ID is always `edhhknmhcehhhfpifnnkkbfcedljdlhi` (the private key is in
`.local/`, git-ignored; the Web Store signs its own builds). The extension name comes from `BrandName`; the API and website
origins from the `ExtensionApiOrigin` / `ExtensionWebOrigin` build properties (CI sets them).

## Brand and design rules

* Brand name and tagline: `Directory.Build.props` (`BrandName`, `BrandTagline`) → `Brand.Name` everywhere, `{BRAND}` in legal Markdown.
* Colors and shapes only in `src/OutfitFinder.UI/wwwroot/css/tokens.css`; fonts are self-hosted in `fonts.css`.
* Guard tests fail the build on hex colors, font names or the brand name anywhere else, and on secrets in `appsettings*.json`.

## Deploy

`infra/main.bicep` creates App Services (Identity, Api, Web), MySQL Flexible Server, Storage, Key Vault, Content Safety,
Application Insights, managed identities and budget alerts. `.github/workflows/ci.yml` builds, tests (including MySQL),
zips the extension with `latest.json`, deploys with OIDC and smoke-tests. Secrets live in Key Vault only.
