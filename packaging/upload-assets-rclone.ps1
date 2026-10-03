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

$manifest = Join-Path $Dir "assets.json"
if (-not (Test-Path $manifest)) { throw "assets.json introuvable dans $Dir (lance pack-assets.ps1 d'abord)." }
# on televerse le zip EXACTEMENT nomme dans le manifeste (evite d'envoyer un vieux zip si plusieurs trainent)
$zipName = (Get-Content $manifest -Raw | ConvertFrom-Json).zip
$zip = Get-Item (Join-Path $Dir $zipName) -ErrorAction SilentlyContinue
if (-not $zip) { throw "Le zip '$zipName' reference par assets.json est introuvable dans $Dir." }

# L'URL publique sert pub-....r2.dev/<bucket>/<cle> : comme le bucket est "maxine-assets", la cle est JUSTE le
# nom de fichier (ne PAS re-prefixer par "maxine-assets/", sinon double prefixe maxine-assets/maxine-assets/...).
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
rclone copyto $zip.FullName ":s3:$($env:R2_BUCKET)/$($zip.Name)" @common --progress
if ($LASTEXITCODE -ne 0) { throw "Televersement du zip echoue." }

# 2) le manifeste en dernier (content-type JSON)
Write-Host "==> Televersement de assets.json" -ForegroundColor Cyan
rclone copyto $manifest ":s3:$($env:R2_BUCKET)/assets.json" @common --header-upload "Content-Type: application/json"
if ($LASTEXITCODE -ne 0) { throw "Televersement du manifeste echoue." }

Write-Host ""
Write-Host "Assets publies sur R2. Les Maxine recupereront cette version au prochain lancement." -ForegroundColor Green
