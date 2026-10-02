<#
    V-Max - televerse le pack d'assets sur Cloudflare R2 (compatible S3).

    Prerequis : AWS CLI installe (https://aws.amazon.com/cli/) - R2 parle le protocole S3.
    Tes cles R2 restent chez toi (jamais dans le code). Definis-les dans ta session PowerShell :

        $env:R2_ACCESS_KEY  = "ton_access_key_id"
        $env:R2_SECRET_KEY  = "ton_secret_access_key"
        $env:R2_ENDPOINT    = "https://<ton-account-id>.r2.cloudflarestorage.com"
        $env:R2_BUCKET      = "maxine-assets"

    Puis :
        powershell -ExecutionPolicy Bypass -File packaging\upload-assets.ps1
#>
param(
    [string]$Dir = (Join-Path $PSScriptRoot "assets-out")
)
$ErrorActionPreference = "Stop"

foreach ($v in "R2_ACCESS_KEY","R2_SECRET_KEY","R2_ENDPOINT","R2_BUCKET") {
    if (-not (Get-Item "env:$v" -ErrorAction SilentlyContinue)) {
        throw "Variable d'environnement $v absente. Definis tes cles R2 (voir l'entete du script)."
    }
}
if (-not (Get-Command aws -ErrorAction SilentlyContinue)) {
    throw "AWS CLI introuvable. Installe-le : https://aws.amazon.com/cli/  (R2 est compatible S3)."
}

# AWS CLI lit les cles depuis ces variables standard
$env:AWS_ACCESS_KEY_ID     = $env:R2_ACCESS_KEY
$env:AWS_SECRET_ACCESS_KEY = $env:R2_SECRET_KEY
$env:AWS_DEFAULT_REGION    = "auto"

$zip = Get-ChildItem $Dir -Filter "maxine-assets-*.zip" | Select-Object -First 1
$manifest = Join-Path $Dir "assets.json"
if (-not $zip -or -not (Test-Path $manifest)) { throw "Pack d'assets introuvable dans $Dir (lance pack-assets.ps1 d'abord)." }

# Les assets vivent sous le prefixe "maxine-assets/" dans le bucket (l'app les lit a cette meme adresse :
# https://pub-....r2.dev/maxine-assets/...). Garde ce prefixe identique a DefaultBaseUrl dans AssetService.cs.
$prefix = "maxine-assets"

Write-Host "==> Televersement de $($zip.Name) ($([math]::Round($zip.Length/1GB,2)) Go) sur R2..." -ForegroundColor Cyan
aws s3 cp $zip.FullName "s3://$($env:R2_BUCKET)/$prefix/$($zip.Name)" --endpoint-url $env:R2_ENDPOINT --no-progress
if ($LASTEXITCODE -ne 0) { throw "Televersement du zip echoue." }

# le manifeste en dernier : l'app ne voit la nouvelle version qu'une fois le zip en place
Write-Host "==> Televersement de assets.json" -ForegroundColor Cyan
aws s3 cp $manifest "s3://$($env:R2_BUCKET)/$prefix/assets.json" --endpoint-url $env:R2_ENDPOINT --content-type "application/json" --no-progress
if ($LASTEXITCODE -ne 0) { throw "Televersement du manifeste echoue." }

Write-Host ""
Write-Host "Assets publies sur R2. Les Maxine recupereront cette version au prochain lancement." -ForegroundColor Green
