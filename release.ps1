# Builds the release files into .\release :
#   BibleStudy-Setup-<version>.exe     installer (also used by the in-app updater)
#   BibleStudy-Portable-<version>.zip  portable version (data in the "Data" folder next to the program)
#   SHA256SUMS.txt
# Usage: .\release.ps1 -Version 1.0.0
param([string]$Version = "1.0.0")
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$rel = Join-Path $root 'release'
$stage = Join-Path $rel 'stage\BibleStudy'
if ($rel -ne (Join-Path $root 'release')) { throw 'unexpected release path' }
if (Test-Path $rel) { Remove-Item -LiteralPath $rel -Recurse -Force }
New-Item -ItemType Directory -Force $stage | Out-Null

Write-Host "== Publishing program $Version"
dotnet publish "$root\src\BibleStudy.App\BibleStudy.App.csproj" -c Release -r win-x64 --self-contained true -p:Version=$Version -p:DebugType=none -o $stage
if ($LASTEXITCODE) { throw 'publish app failed' }
dotnet publish "$root\src\BibleStudy.Mcp\BibleStudy.Mcp.csproj" -c Release -r win-x64 --self-contained true -p:Version=$Version -p:DebugType=none -o "$stage\mcp"
if ($LASTEXITCODE) { throw 'publish mcp failed' }
Copy-Item "$root\LICENSE", "$root\THIRD-PARTY-NOTICES.md" $stage

Write-Host "== Installer payload"
Add-Type -AssemblyName System.IO.Compression.FileSystem
$payload = Join-Path $rel 'payload.zip'
[IO.Compression.ZipFile]::CreateFromDirectory($stage, $payload, 'Optimal', $false)

Write-Host "== Building installer"
dotnet publish "$root\src\BibleStudy.Setup\BibleStudy.Setup.csproj" -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:Version=$Version -p:PayloadZip=$payload -p:DebugType=none -o "$rel\setup"
if ($LASTEXITCODE) { throw 'publish setup failed' }
Move-Item "$rel\setup\BibleStudy-Setup.exe" "$rel\BibleStudy-Setup-$Version.exe"

Write-Host "== Portable package"
Set-Content -LiteralPath "$stage\portable.flag" -Value 'This file switches BibleStudy to portable mode: all data stays in the Data folder next to the program.'
Set-Content -LiteralPath "$stage\README-Portable.txt" -Value @"
BibleStudy portable
Start BibleStudy.App.exe. Everything you create (vaults, notes, documents, settings) is stored in the folder "Data" next to the program.
To move to another computer or USB stick, copy the whole folder.
To remove the program: Settings > Updates and program > "Remove portable version" (asks whether to keep your data).
"@
[IO.Compression.ZipFile]::CreateFromDirectory((Join-Path $rel 'stage'), "$rel\BibleStudy-Portable-$Version.zip", 'Optimal', $false)

Remove-Item -LiteralPath (Join-Path $rel 'stage'), (Join-Path $rel 'setup'), $payload -Recurse -Force
Get-ChildItem $rel -File | Where-Object { $_.Name -ne 'SHA256SUMS.txt' } | ForEach-Object { '{0}  {1}' -f (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLower(), $_.Name } | Set-Content "$rel\SHA256SUMS.txt"
Get-ChildItem $rel -File | ForEach-Object { '{0,-40} {1,8:N1} MB' -f $_.Name, ($_.Length / 1MB) }
