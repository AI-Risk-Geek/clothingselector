# Builds the Chrome extension and puts a downloadable zip on the website (the Install page's "Manual install" section).
# Same steps as the CI pipeline, for local testing. Defaults point the extension at the local IIS Express ports.
#
#   .\scripts\package-extension.ps1
#   .\scripts\package-extension.ps1 -ApiOrigin https://api.example.com -IdentityOrigin https://id.example.com -WebOrigin https://www.example.com
param(
    [string]$ApiOrigin = "https://localhost:44302",
    [string]$IdentityOrigin = "https://localhost:44301",
    [string]$WebOrigin = "https://localhost:44303",
    [string]$WebStoreUrl = ""
)
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$manifest = Get-Content (Join-Path $root "src\OutfitFinder.Extension\wwwroot\manifest.json") -Raw | ConvertFrom-Json
$version = $manifest.version
$out = Join-Path $root "publish\extension"

Write-Host "Building extension $version ..."
if (Test-Path $out) { Remove-Item $out -Recurse -Force }
dotnet publish (Join-Path $root "src\OutfitFinder.Extension") -c Release -o $out "-p:ExtensionApiOrigin=$ApiOrigin" "-p:ExtensionWebOrigin=$WebOrigin" -nologo -v q
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

$ext = Join-Path $out "browserextension"
if (-not (Test-Path (Join-Path $ext "manifest.json"))) { throw "No browserextension folder with a manifest in $out" }
# Chrome refuses a manifest that still has build placeholders in it.
if ((Get-Content (Join-Path $ext "manifest.json") -Raw) -match "__[A-Z_]+__") { throw "manifest.json still contains build placeholders" }

# Point the extension at the chosen services.
$settingsPath = Join-Path $ext "appsettings.json"
$settings = Get-Content $settingsPath -Raw | ConvertFrom-Json
$settings.ApiBaseUrl = $ApiOrigin; $settings.IdentityAuthority = $IdentityOrigin; $settings.WebsiteUrl = $WebOrigin
[IO.File]::WriteAllText($settingsPath, ($settings | ConvertTo-Json), (New-Object Text.UTF8Encoding $false))

$dest = Join-Path $root "src\OutfitFinder.Web\wwwroot\extension"
New-Item -ItemType Directory -Force $dest | Out-Null
Get-ChildItem $dest -Filter "outfit-finder-extension-*.zip" | Remove-Item -Force
$zipName = "outfit-finder-extension-$version.zip"
Compress-Archive -Path (Join-Path $ext "*") -DestinationPath (Join-Path $dest $zipName) -Force

$latest = [ordered]@{
    version = $version
    downloadUrl = "extension/$zipName"
    webStoreUrl = $(if ($WebStoreUrl) { $WebStoreUrl } else { $null })
    publishedUtc = (Get-Date).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")
}
[IO.File]::WriteAllText((Join-Path $dest "latest.json"), ($latest | ConvertTo-Json), (New-Object Text.UTF8Encoding $false))

Write-Host "Done: $zipName and latest.json are in src\OutfitFinder.Web\wwwroot\extension"
Write-Host "Unpacked copy for 'Load unpacked': $ext"
