<#
    V-Max - televerse le pack d'assets sur Cloudflare R2 avec RCLONE (alternative a aws CLI).

    Prerequis : rclone installe (tu l'as deja). Tes cles R2 restent chez toi (jamais dans le code).
    Definis-les dans ta session PowerShell :

        $env:R2_ACCESS_KEY  = "ton_access_key_id"
        $env:R2_SECRET_KEY  = "ton_secret_access_key"
        $env:R2_ENDPOINT    = "https://<ton-account-id>.r2.cloudflarestorage.com"
        $env:R2_BUCKET      = "maxine-assets"

    Puis :
        powershell -ExecutionPolicy Bypass -File packaging\upload-assets-rclone.ps1
#>
param(
    [string]$Dir = (Join-Path $PSScriptRoot "assets-out")
)
$ErrorActionPreference = "Stop"

foreach ($v in "R2_ACCESS_KEY", "R2_SECRET_KEY", "R2_ENDPOINT", "R2_BUCKET") {
    if (-not (Get-Item "env:$v" -ErrorAction SilentlyContinue)) {
        throw "Variable d'environnement $v absente. Definis tes cles R2 (voir l'entete du script)."
    }
}
if (-not (Get-Command rclone -ErrorAction SilentlyContinue)) {
    throw "rclone introuvable dans le PATH."
}

$zip = Get-ChildItem $Dir -Filter "maxine-assets-*.zip" | Select-Object -First 1
$manifest = Join-Path $Dir "assets.json"
if (-not $zip -or -not (Test-Path $manifest)) { throw "Pack d'assets introuvable dans $Dir (lance pack-assets.ps1 d'abord)." }

# Meme prefixe que DefaultBaseUrl dans AssetService.cs : l'app lit https://pub-....r2.dev/maxine-assets/...
$prefix = "maxine-assets"

# backend S3 a la volee (pas besoin de configurer un remote rclone)
$common = @(
    "--s3-provider", "Cloudflare",
    "--s3-access-key-id", $env:R2_ACCESS_KEY,
    "--s3-secret-access-key", $env:R2_SECRET_KEY,
    "--s3-endpoint", $env:R2_ENDPOINT,
    "--s3-region", "auto",
    "--s3-no-check-bucket"
)

# 1) le zip D'ABORD (le manifeste ne doit pointer que vers un zip deja en place)
Write-Host "==> Televersement de $($zip.Name) ($([math]::Round($zip.Length/1GB,2)) Go) sur R2..." -ForegroundColor Cyan
rclone copyto $zip.FullName ":s3:$($env:R2_BUCKET)/$prefix/$($zip.Name)" @common --progress
if ($LASTEXITCODE -ne 0) { throw "Televersement du zip echoue." }

# 2) le manifeste en dernier (content-type JSON)
Write-Host "==> Televersement de assets.json" -ForegroundColor Cyan
rclone copyto $manifest ":s3:$($env:R2_BUCKET)/$prefix/assets.json" @common --header-upload "Content-Type: application/json"
if ($LASTEXITCODE -ne 0) { throw "Televersement du manifeste echoue." }

Write-Host ""
Write-Host "Assets publies sur R2. Les Maxine recupereront cette version au prochain lancement." -ForegroundColor Green
