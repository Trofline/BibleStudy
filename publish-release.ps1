# Builds a release (release.ps1), pushes main and publishes it as a GitHub release.
#
#   .\publish-release.ps1                    version from src\BibleStudy.App\BibleStudy.App.csproj, notes from release-notes\<version>.md
#   .\publish-release.ps1 -DryRun            builds and checks everything, but pushes and publishes NOTHING
#   .\publish-release.ps1 -SkipBuild         uses the files already in .\release (if release.ps1 was run for this version)
#
# Before the real thing it shows what it is going to do and asks for a "j" (yes). Nothing is ever deleted on GitHub.
param(
    [string]$Version,
    [string]$Notes,
    [switch]$DryRun,
    [switch]$SkipBuild
)
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

function Fail([string]$message) { Write-Host "STOPP: $message" -ForegroundColor Red; exit 1 }

# ---- what and which version -------------------------------------------------------------------------------------------------
$csproj = ([xml](Get-Content 'src\BibleStudy.App\BibleStudy.App.csproj' -Raw)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
if (-not $Version) { $Version = $csproj }
if ($Version -notmatch '^\d+\.\d+\.\d+$') { Fail "Ungueltige Version '$Version' (erwartet: 1.2.3)" }
if ($Version -ne $csproj) { Fail "Version $Version, aber BibleStudy.App.csproj sagt $csproj. Erst die Version in der csproj aendern und committen." }
$tag = "v$Version"
if (-not $Notes) { $Notes = "release-notes\$Version.md" }
if (-not (Test-Path $Notes)) { Fail "Es fehlt der Release-Text: $Notes (Abschnitte 'Neu in $Version' und 'New in $Version' schreiben)" }

# ---- checks before anything leaves the computer -------------------------------------------------------------------------------
if (-not (Get-Command gh -ErrorAction SilentlyContinue)) { Fail 'Die GitHub-CLI (gh) ist nicht installiert.' }
gh auth status 2>&1 | Out-Null
if ($LASTEXITCODE) { Fail 'gh ist nicht angemeldet (gh auth login).' }
$branch = (git rev-parse --abbrev-ref HEAD).Trim()
if ($branch -ne 'main') { Fail "Du bist auf dem Zweig '$branch', veroeffentlicht wird nur von 'main'." }
if (git status --porcelain) { Fail 'Es gibt nicht committete Aenderungen. Erst committen (sonst steht im Release etwas anderes als im Code).' }
gh release view $tag 2>&1 | Out-Null
if ($LASTEXITCODE -eq 0) { Fail "Das Release $tag gibt es schon auf GitHub." }

# ---- build ---------------------------------------------------------------------------------------------------------------------
if (-not $SkipBuild) {
    Get-Process BibleStudy.App -ErrorAction SilentlyContinue | Where-Object { $_.Path -like "$PSScriptRoot*" } | ForEach-Object {
        Write-Host 'Das Programm aus diesem Ordner laeuft noch und wird fuer den Bau geschlossen.'
        $_ | Stop-Process -Force
    }
    & "$PSScriptRoot\release.ps1" -Version $Version
    if ($LASTEXITCODE) { Fail 'release.ps1 ist fehlgeschlagen.' }
}
$files = @(Get-ChildItem 'release' -File -ErrorAction SilentlyContinue)
$needed = "BibleStudy-Setup-$Version.exe", "BibleStudy-Portable-$Version.zip", "BibleStudy-Linux-x64-$Version.tar.gz", "BibleStudy-macOS-arm64-$Version.zip", "BibleStudy-macOS-x64-$Version.zip", 'SHA256SUMS.txt'
foreach ($n in $needed) { if (-not ($files.Name -contains $n)) { Fail "In .\release fehlt $n (fuer diese Version gebaut?)" } }

# ---- show and ask --------------------------------------------------------------------------------------------------------------
Write-Host ''
Write-Host "Version:    $Version  (Tag $tag)"
Write-Host "Release-Text: $Notes"
Get-Content $Notes -TotalCount 6 | ForEach-Object { Write-Host "   $_" }
Write-Host 'Dateien:'
$files | ForEach-Object { '   {0,-52} {1,8:N1} MB' -f $_.Name, ($_.Length / 1MB) }
Write-Host ''
Write-Host "Das passiert jetzt: 'git push origin main', dann wird das Release $tag auf GitHub oeffentlich angelegt."
if ($DryRun) { Write-Host 'DRY RUN: nichts gepusht, nichts veroeffentlicht.' -ForegroundColor Yellow; exit 0 }
$answer = Read-Host 'Veroeffentlichen? (j/n)'
if ($answer -notin 'j', 'J', 'ja', 'Ja', 'y', 'Y') { Write-Host 'Abgebrochen, nichts veroeffentlicht.'; exit 0 }

# ---- publish -------------------------------------------------------------------------------------------------------------------
git push origin main
if ($LASTEXITCODE) { Fail 'git push ist fehlgeschlagen.' }
gh release create $tag @($files.FullName) --title "BibleStudy $Version" --notes-file $Notes
if ($LASTEXITCODE) { Fail 'gh release create ist fehlgeschlagen.' }
Write-Host "Fertig: https://github.com/Trofline/BibleStudy/releases/tag/$tag" -ForegroundColor Green
