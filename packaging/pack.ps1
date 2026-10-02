<#
    V-Max - fabrication de l'installeur et d'une release (Velopack).

    Usage :
        powershell -ExecutionPolicy Bypass -File packaging\pack.ps1 -Version 1.0.0   # fabrique l'installeur dans packaging/releases
        powershell -ExecutionPolicy Bypass -File packaging\pack.ps1 -Version 1.0.1 -Upload   # fabrique ET publie la release sur GitHub

    Produit :
        packaging/releases/Maxine-win-Setup.exe installeur " un clic "
        packaging/releases/*.nupkg + RELEASES fichiers lus par l'auto-update

    Prérequis (une fois) :
        dotnet tool install -g vpk
        # pour -Upload : une variable d'environnement GITHUB_TOKEN avec un jeton " repo "
#>
param(
    [Parameter(Mandatory = $true)][string]$Version,
    [switch]$Upload,
    [string]$Runtime = "win-x64"
)
$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$proj = Join-Path $root "VPet-Simulator.Windows\VPet-Simulator.Windows.csproj"
$publish = Join-Path $PSScriptRoot "publish"
$releases = Join-Path $PSScriptRoot "releases"
$repo = "https://github.com/maximelebatardsartre/V-Max"
$icon = Join-Path $root "VPet-Simulator.Windows\maxine.ico"

if (-not (Get-Command vpk -ErrorAction SilentlyContinue)) {
    throw "vpk introuvable. Installe-le : dotnet tool install -g vpk"
}

Write-Host "==> Publication (Release, autonome, $Runtime)" -ForegroundColor Cyan
if (Test-Path $publish) { Remove-Item $publish -Recurse -Force }
dotnet publish $proj -c Release -r $Runtime --self-contained true `
    -p:Platform=x64 -p:PublishSingleFile=false -o $publish
if ($LASTEXITCODE -ne 0) { throw "La publication a échoué." }

Write-Host "==> Copie des animations de base (mod/0000_core)" -ForegroundColor Cyan
$modSrc = Join-Path $root "VPet-Simulator.Windows\mod\0000_core"
$modDst = Join-Path $publish "mod\0000_core"
New-Item -ItemType Directory -Force -Path (Split-Path $modDst) | Out-Null
# robocopy : rapide pour les ~6300 images (code de sortie < 8 = succès)
robocopy $modSrc $modDst /E /NFL /NDL /NJH /NJS /NP | Out-Null
if ($LASTEXITCODE -ge 8) { throw "La copie de mod/0000_core a échoué." }

# Mods du Workshop traduits, livrés avec l'installeur (chargés et actifs d'office via " mods-inclus ")
$bundledSrc = Join-Path $env:APPDATA "V-Max\mods"
if (Test-Path $bundledSrc) {
    Write-Host "==> Copie des mods inclus (activités traduites)" -ForegroundColor Cyan
    $bundledDst = Join-Path $publish "mods-inclus"
    New-Item -ItemType Directory -Force -Path $bundledDst | Out-Null
    robocopy $bundledSrc $bundledDst /E /NFL /NDL /NJH /NJS /NP | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "La copie des mods inclus a échoué." }
    $n = (Get-ChildItem $bundledDst -Directory).Count
    Write-Host "    $n mods inclus"
} else {
    Write-Host "==> Aucun mod inclus (dossier $bundledSrc absent) - installeur de base" -ForegroundColor Yellow
}

Write-Host "==> Fabrication de l'installeur Velopack ($Version)" -ForegroundColor Cyan
New-Item -ItemType Directory -Force -Path $releases | Out-Null
vpk pack `
    --packId Maxine `
    --packTitle Maxine `
    --packAuthors "V-Max" `
    --packVersion $Version `
    --packDir $publish `
    --mainExe Maxine.exe `
    --runtime $Runtime `
    --icon $icon `
    --outputDir $releases
if ($LASTEXITCODE -ne 0) { throw "vpk pack a échoué." }

$setup = Get-ChildItem $releases -Filter "*Setup.exe" | Select-Object -First 1
Write-Host "Installeur prêt : $($setup.FullName)" -ForegroundColor Green

if ($Upload) {
    if (-not $env:GITHUB_TOKEN) { throw "Définis GITHUB_TOKEN (jeton GitHub avec le droit 'repo') pour publier." }
    Write-Host "==> Publication de la release $Version sur GitHub" -ForegroundColor Cyan
    vpk upload github `
        --repoUrl $repo `
        --token $env:GITHUB_TOKEN `
        --publish `
        --releaseName "Maxine $Version" `
        --tag "v$Version" `
        --outputDir $releases
    if ($LASTEXITCODE -ne 0) { throw "La publication GitHub a échoué." }
    Write-Host "Release v$Version publiée. Les clients l'installeront à leur prochain lancement." -ForegroundColor Green
}
