<#
    V-Max - fabrique le pack d'assets (animations de base + mods traduits) pour l'hebergement R2.

    Usage :
        powershell -ExecutionPolicy Bypass -File packaging\pack-assets.ps1 -Version 1

    Produit dans packaging\assets-out\ :
        maxine-assets-<version>.zip   (0000_core + les 35 mods traduits, structure mod\ et mods-inclus\)
        assets.json                   manifeste lu par l'application (version, nom du zip, empreinte, taille)

    Ensuite : televerser ces 2 fichiers sur le bucket R2 (voir packaging\upload-assets.ps1).
    Incremente -Version a chaque fois que le contenu change (l'app re-telecharge quand la version change).
#>
param(
    [Parameter(Mandatory = $true)][string]$Version
)
$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$out = Join-Path $PSScriptRoot "assets-out"
$stage = Join-Path $PSScriptRoot "assets-stage"
$excluded = @("3027004255","3027542580","3030945675","3031981095","3035399894","3042568517",
              "3044723043","3045450089","3046644833","3065265367","3290665653","3176916830")

Write-Host "==> Preparation des assets (version $Version)" -ForegroundColor Cyan
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Force -Path $stage | Out-Null
New-Item -ItemType Directory -Force -Path $out | Out-Null

# 1) animations de base
$coreSrc = Join-Path $root "VPet-Simulator.Windows\mod\0000_core"
$coreDst = Join-Path $stage "mod\0000_core"
New-Item -ItemType Directory -Force -Path (Split-Path $coreDst) | Out-Null
Write-Host "    animations de base (0000_core)..."
robocopy $coreSrc $coreDst /E /NFL /NDL /NJH /NJS /NP | Out-Null
if ($LASTEXITCODE -ge 8) { throw "Copie de 0000_core echouee." }

# 2) mods traduits (MOD/1920960, hors mods refuses et dossiers vides)
$modsSrc = Join-Path $root "MOD\1920960"
if (-not (Test-Path $modsSrc)) { $modsSrc = Join-Path $env:APPDATA "V-Max\mods" }
$modsDst = Join-Path $stage "mods-inclus"
New-Item -ItemType Directory -Force -Path $modsDst | Out-Null
$n = 0
foreach ($dir in Get-ChildItem $modsSrc -Directory) {
    if ($excluded -contains $dir.Name) { continue }
    if (-not (Test-Path (Join-Path $dir.FullName "info.lps"))) { continue }
    robocopy $dir.FullName (Join-Path $modsDst $dir.Name) /E /NFL /NDL /NJH /NJS /NP /XF "*.bak" | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "Copie du mod $($dir.Name) echouee." }
    $n++
}
Write-Host "    $n mods traduits inclus"
if ($n -eq 0) { throw "Aucun mod trouve dans $modsSrc." }

# 3) zip
$zipName = "maxine-assets-$Version.zip"
$zipPath = Join-Path $out $zipName
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
Write-Host "==> Compression -> $zipName (ca peut prendre quelques minutes)" -ForegroundColor Cyan
Add-Type -AssemblyName System.IO.Compression.FileSystem
# ZipFile gere les archives > 2 Go (zip64), contrairement a Compress-Archive de PowerShell 5.1
[System.IO.Compression.ZipFile]::CreateFromDirectory($stage, $zipPath,
    [System.IO.Compression.CompressionLevel]::Optimal, $false)
Remove-Item $stage -Recurse -Force

# 4) manifeste
$size = (Get-Item $zipPath).Length
$sha = (Get-FileHash $zipPath -Algorithm SHA256).Hash
$manifest = [ordered]@{ version = "$Version"; zip = $zipName; sha256 = $sha; size = $size }
$manifestPath = Join-Path $out "assets.json"
[System.IO.File]::WriteAllText($manifestPath, ($manifest | ConvertTo-Json), (New-Object System.Text.UTF8Encoding $false))

Write-Host ""
Write-Host "Pack d'assets pret :" -ForegroundColor Green
Write-Host ("   {0}  ({1:N2} Go)" -f $zipName, ($size / 1GB))
Write-Host ("   assets.json  (version $Version)")
Write-Host ""
Write-Host "Etape suivante : televerser ces 2 fichiers sur R2 ->  packaging\upload-assets.ps1" -ForegroundColor Yellow
