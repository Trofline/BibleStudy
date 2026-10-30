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

Write-Host '== Checking that every text is translated'
dotnet test "$root\tests\BibleStudy.Tests\BibleStudy.Tests.csproj" --filter "FullyQualifiedName~LocalizationTests" --nologo -v q
if ($LASTEXITCODE) { throw 'Translations are incomplete: fix the missing texts before releasing' }

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

Write-Host "== Plugins (Windows): one zip per folder in .\plugins, to unpack into the program's plugin folder"
foreach ($dir in Get-ChildItem (Join-Path $root 'plugins') -Directory) {
    $proj = Get-ChildItem $dir.FullName -Filter *.csproj | Select-Object -First 1
    if (-not $proj) { continue }
    $pjson = Get-Content (Join-Path $dir.FullName 'plugin.json') -Raw | ConvertFrom-Json
    $pstage = Join-Path $rel "pstage\$($pjson.id)"
    dotnet publish $proj.FullName -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none -o $pstage
    if ($LASTEXITCODE) { throw "publish plugin $($pjson.id) failed" }
    Copy-Item (Join-Path $dir.FullName 'plugin.json'), (Join-Path $dir.FullName 'README.md') $pstage
    [IO.Compression.ZipFile]::CreateFromDirectory((Join-Path $rel 'pstage'), "$rel\BibleStudy-Plugin-$($pjson.id)-$($pjson.version).zip", 'Optimal', $false)
    Remove-Item -LiteralPath (Join-Path $rel 'pstage') -Recurse -Force
}

Write-Host "== Linux and macOS packages (not tested by the build; see README-Unix.txt in each)"
foreach ($rid in 'linux-x64', 'osx-arm64', 'osx-x64') {
    $u = Join-Path $rel "unix-$rid\BibleStudy"
    dotnet publish "$root\src\BibleStudy.App\BibleStudy.App.csproj" -c Release -r $rid --self-contained true -p:Version=$Version -p:DebugType=none -o $u
    if ($LASTEXITCODE) { throw "publish app $rid failed" }
    dotnet publish "$root\src\BibleStudy.Mcp\BibleStudy.Mcp.csproj" -c Release -r $rid --self-contained true -p:Version=$Version -p:DebugType=none -o "$u\mcp"
    if ($LASTEXITCODE) { throw "publish mcp $rid failed" }
    Copy-Item "$root\LICENSE", "$root\THIRD-PARTY-NOTICES.md" $u
    Set-Content -LiteralPath "$u\README-Unix.txt" -Value @"
BibleStudy for $rid (built on Windows, not tested on this system - please report problems).
1. Unpack, then in a terminal in this folder:  chmod +x BibleStudy.App mcp/BibleStudy.Mcp
2. macOS only: xattr -dr com.apple.quarantine .   (the program is not signed)
3. Start:  ./BibleStudy.App
Your data is stored in ~/.local/share/BibleStudy. Self-updating is Windows only; new versions are announced and the download page opens.
"@
    $name = if ($rid -like 'linux*') { "BibleStudy-Linux-x64-$Version.tar.gz" } else { "BibleStudy-macOS-$($rid.Split('-')[1])-$Version.zip" }
    if ($rid -like 'linux*') { tar -czf "$rel\$name" -C (Join-Path $rel "unix-$rid") BibleStudy } else { [IO.Compression.ZipFile]::CreateFromDirectory((Join-Path $rel "unix-$rid"), "$rel\$name", 'Optimal', $false) }
    Remove-Item -LiteralPath (Join-Path $rel "unix-$rid") -Recurse -Force
}
Remove-Item -LiteralPath (Join-Path $rel 'stage'), (Join-Path $rel 'setup'), $payload -Recurse -Force
Get-ChildItem $rel -File | Where-Object { $_.Name -ne 'SHA256SUMS.txt' } | ForEach-Object { '{0}  {1}' -f (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLower(), $_.Name } | Set-Content "$rel\SHA256SUMS.txt"
Get-ChildItem $rel -File | ForEach-Object { '{0,-40} {1,8:N1} MB' -f $_.Name, ($_.Length / 1MB) }
