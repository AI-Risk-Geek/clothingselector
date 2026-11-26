# Outfit Finder (placeholder name)

Upload a photo of an outfit → the AI finds every piece → where to buy each one ("Buy it") or a sewing pattern sized to you
("Make it"). A Blazor Server website, a Chrome extension (Blazor WASM), an API with a background search worker, and an
identity server — all C# / .NET 10. Start with [HANDOFF.md](HANDOFF.md) and [docs/PLAN.md](docs/PLAN.md); the decisions
made while building are in [docs/DECISIONS.md](docs/DECISIONS.md).

## Set up and run it in Visual Studio Community

No keys, Docker or database server needed: the solution runs in **demo mode** with sample results.

### 1. Install (once)

* **Visual Studio 2026 Community** (free) from https://visualstudio.microsoft.com/vs/community/. In the installer, tick the
  **ASP.NET and web development** workload. It includes .NET 10, IIS Express and Git.
* Chrome or Edge, if you want to try the browser extension.

### 2. Get the code

On the Visual Studio start window choose **Clone a repository**, paste
`https://github.com/AI-Risk-Geek/clothingselector.git`, pick a folder and click **Clone**. Visual Studio opens
`OutfitFinder.sln`. (Already have the folder? Open `OutfitFinder.sln` with **File > Open > Project/Solution**.)

### 3. Start all three projects together

The website signs people in through the sign-in server and gets results from the API, so all three must run.

* In the start-up dropdown on the toolbar (next to the green ▶) choose **IIS Express (all three)**.
* Not in the list? In **Solution Explorer**, right-click **Solution 'OutfitFinder'** > **Configure Startup Projects…** >
  **Multiple startup projects**. Set the **Action** to **Start** for `OutfitFinder.Identity`, `OutfitFinder.Api` and
  `OutfitFinder.Web` (in that order, using the arrows) and leave the rest as **None**. Click **OK**.

### 4. Press F5

* The first time, Visual Studio asks to trust its development certificate. Click **Yes** each time it asks.
* The first build downloads packages and takes a few minutes.
* The browser opens **https://localhost:44303**. The database is created automatically in the `.local` folder.

### 5. Try it

Click **Try a sample photo**. A yellow "Demo mode" banner shows that results are sample data. The "For testing only"
human check is Cloudflare's test widget and is expected here.

### 6. Make yourself the admin

Click **Sign in** > **Create account** and register the username **`admin`**. That account gets admin rights, and
**Admin** appears in the menu.

### 7. Optional: use a real AI

Menu > **Admin** (AI settings): pick **OpenAI**, **Azure AI Foundry** or **Google Gemini**, type the model name, paste the
API key, click **Test connection**, then **Save settings**. New searches use it straight away. Real searches cost money;
searches stop for the day after `Limits:DailyBudgetUsd` (25 USD).

### If something goes wrong

| What you see | What to do |
|---|---|
| `No connection could be made … localhost:44302` (or `44301`) | Only the website started. Do step 3. |
| `Configuration value '…' is required` | A project started with the wrong launch profile. Do step 3 and pick **IIS Express**. |
| The browser warns the connection isn't private | Open **View > Terminal**, run `dotnet dev-certs https --trust`, then restart Visual Studio. |
| A port is already in use | Stop debugging, right-click the IIS Express icon in the Windows taskbar tray and choose **Exit**, then press F5 again. |
| You want to start again with no data | Stop debugging and delete the `.local` folder. |

The local settings are in each project's `appsettings.Development.json` (local development only: SQLite, a local photo
folder, the Demo AI and the development moderation bypass, which the app refuses outside Development). Never put real
production keys in those files.

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

To build it and put a download on the website's **Get the extension** page, run this in **View > Terminal**:

```powershell
.\scripts\package-extension.ps1
```

Then, with the solution running, open https://localhost:44303/install and follow **Manual install (for testers)**: unzip
the download, open `chrome://extensions`, switch on **Developer mode**, click **Load unpacked** and pick the folder.
(Or load `publish/extension/browserextension` directly.) Right-click any photo on a website and choose **Find with AI**.

The manifest has a fixed `key`, so the ID is always `edhhknmhcehhhfpifnnkkbfcedljdlhi` (the private key is in
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
