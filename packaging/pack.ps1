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
    [switch]$FullOffline,   # inclure animations + mods dans l'installeur (sinon : installeur leger, assets sur R2)
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

if ($FullOffline) {
    Write-Host "==> Installeur COMPLET : copie des animations de base et des mods" -ForegroundColor Cyan
    $modSrc = Join-Path $root "VPet-Simulator.Windows\mod\0000_core"
    $modDst = Join-Path $publish "mod\0000_core"
    New-Item -ItemType Directory -Force -Path (Split-Path $modDst) | Out-Null
    robocopy $modSrc $modDst /E /NFL /NDL /NJH /NJS /NP | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "La copie de mod/0000_core a echoue." }
    $excluded = @("3027004255","3027542580","3030945675","3031981095","3035399894","3042568517",
                  "3044723043","3045450089","3046644833","3065265367","3290665653","3176916830")
    $bundledSrc = Join-Path $root "MOD\1920960"
    if (-not (Test-Path $bundledSrc)) { $bundledSrc = Join-Path $env:APPDATA "V-Max\mods" }
    $bundledDst = Join-Path $publish "mods-inclus"
    New-Item -ItemType Directory -Force -Path $bundledDst | Out-Null
    $n = 0
    foreach ($dir in Get-ChildItem $bundledSrc -Directory) {
        if ($excluded -contains $dir.Name) { continue }
        if (-not (Test-Path (Join-Path $dir.FullName "info.lps"))) { continue }
        robocopy $dir.FullName (Join-Path $bundledDst $dir.Name) /E /NFL /NDL /NJH /NJS /NP /XF "*.bak" | Out-Null
        if ($LASTEXITCODE -ge 8) { throw "La copie du mod $($dir.Name) a echoue." }
        $n++
    }
    Write-Host "    $n mods inclus"
} else {
    Write-Host "==> Installeur LEGER : animations et mods seront telecharges depuis R2 au premier lancement" -ForegroundColor Cyan
}

Write-Host "==> Fabrication de l'installeur Velopack ($Version)" -ForegroundColor Cyan
New-Item -ItemType Directory -Force -Path $releases | Out-Null
# NB : packId reste "Maxine" (identité interne Velopack = NE PAS changer, sinon les installs existantes ne se
# mettent plus à jour). packTitle est le nom VISIBLE (menu Démarrer, Programmes et fonctionnalités, raccourci).
vpk pack `
    --packId Maxine `
    --packTitle "V-Max Compagnon" `
    --packAuthors "V-Max" `
    --packVersion $Version `
    --packDir $publish `
    --mainExe Maxine.exe `
    --runtime $Runtime `
    --icon $icon `
    --outputDir $releases
if ($LASTEXITCODE -ne 0) { throw "vpk pack a échoué." }

# nom de fichier d'installeur plus parlant pour les amis (l'auto-update utilise le .nupkg, pas ce fichier)
$setup = Get-ChildItem $releases -Filter "*Setup.exe" | Select-Object -First 1
if ($setup) {
    $nice = Join-Path $releases "V-Max-Compagnon-Setup.exe"
    if (Test-Path $nice) { Remove-Item $nice -Force }
    Rename-Item $setup.FullName $nice
    $setup = Get-Item $nice
}
Write-Host "Installeur prêt : $($setup.FullName)" -ForegroundColor Green

if ($Upload) {
    if (-not $env:GITHUB_TOKEN) { throw "Définis GITHUB_TOKEN (jeton GitHub avec le droit 'repo') pour publier." }
    Write-Host "==> Publication de la release $Version sur GitHub" -ForegroundColor Cyan
    vpk upload github `
        --repoUrl $repo `
        --token $env:GITHUB_TOKEN `
        --publish `
        --releaseName "V-Max Compagnon $Version" `
        --tag "v$Version" `
        --outputDir $releases
    if ($LASTEXITCODE -ne 0) { throw "La publication GitHub a échoué." }
    Write-Host "Release v$Version publiée. Les clients l'installeront à leur prochain lancement." -ForegroundColor Green
}
