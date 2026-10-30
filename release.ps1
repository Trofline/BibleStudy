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

# ---- packages for Linux and macOS, made on Windows: the files need their "executable" flag, which a plain zip/tar from Windows lacks ----
function Get-UnixMode([string]$name) {
    $exec = $name -in 'BibleStudy', 'BibleStudy.Mcp', 'createdump', 'StrongsAdder' -or $name -like '*.command' -or $name -like '*.sh' -or $name -like '*.dylib' -or $name -like '*.so'
    if ($exec) { 493 } else { 420 }   # 0755 or 0644
}
# text files that run on Linux/macOS must have Unix line endings, whatever git did on this Windows checkout
function Copy-UnixText([string]$from, [string]$to) {
    $text = [IO.File]::ReadAllText($from) -replace "`r`n", "`n"
    [IO.File]::WriteAllText($to, $text, (New-Object Text.UTF8Encoding $false))
}
function New-UnixZip([string]$srcDir, [string]$zipPath, [string]$topName) {
    $base = (Get-Item $srcDir).FullName.TrimEnd('\')
    $zip = [IO.Compression.ZipFile]::Open($zipPath, 'Create')
    try {
        foreach ($f in Get-ChildItem $srcDir -Recurse -File -Force) {
            $entry = $zip.CreateEntry("$topName/" + $f.FullName.Substring($base.Length + 1).Replace('\', '/'), 'Optimal')
            $attr = [uint32](([uint32](Get-UnixMode $f.Name) -bor 0x8000) * 65536)   # Unix mode in the upper 16 bits, regular file
            $entry.ExternalAttributes = [BitConverter]::ToInt32([BitConverter]::GetBytes($attr), 0)
            $in = [IO.File]::OpenRead($f.FullName); $out = $entry.Open()
            try { $in.CopyTo($out) } finally { $out.Dispose(); $in.Dispose() }
        }
    } finally { $zip.Dispose() }
}
function New-UnixTgz([string]$srcDir, [string]$tgzPath, [string]$topName) {
    $base = (Get-Item $srcDir).FullName.TrimEnd('\')
    $file = [IO.File]::Create($tgzPath)
    $gz = New-Object IO.Compression.GZipStream($file, [IO.Compression.CompressionLevel]::Optimal)
    $tar = [Formats.Tar.TarWriter]::new($gz, [Formats.Tar.TarEntryFormat]::Pax, $false)
    try {
        foreach ($f in Get-ChildItem $srcDir -Recurse -File -Force) {
            $entry = [Formats.Tar.PaxTarEntry]::new([Formats.Tar.TarEntryType]::RegularFile, ("$topName/" + $f.FullName.Substring($base.Length + 1).Replace('\', '/')))
            $entry.Mode = [IO.UnixFileMode](Get-UnixMode $f.Name)
            $entry.DataStream = [IO.File]::OpenRead($f.FullName)
            $tar.WriteEntry($entry)
            $entry.DataStream.Dispose()
        }
    } finally { $tar.Dispose(); $gz.Dispose(); $file.Dispose() }
}

Write-Host "== Plugins: one package per folder in .\plugins and system (Windows zip, Linux tar.gz, macOS zip) to unpack into the program's plugin folder"
foreach ($dir in Get-ChildItem (Join-Path $root 'plugins') -Directory) {
    $proj = Get-ChildItem $dir.FullName -Filter *.csproj | Select-Object -First 1
    if (-not $proj) { continue }
    $pjson = Get-Content (Join-Path $dir.FullName 'plugin.json') -Raw | ConvertFrom-Json
    foreach ($rid in 'win-x64', 'linux-x64', 'osx-arm64', 'osx-x64') {
        $pstage = Join-Path $rel "pstage\$($pjson.id)"
        dotnet publish $proj.FullName -c Release -r $rid --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none -o $pstage
        if ($LASTEXITCODE) { throw "publish plugin $($pjson.id) $rid failed" }
        Copy-Item (Join-Path $dir.FullName 'README.md') $pstage
        $manifest = Get-Content (Join-Path $dir.FullName 'plugin.json') -Raw
        # the source manifest names the Windows program (.exe); on Linux and macOS the file has no extension
        if ($rid -ne 'win-x64') { $manifest = $manifest -replace '\.exe"', '"' }
        [IO.File]::WriteAllText((Join-Path $pstage 'plugin.json'), $manifest, (New-Object Text.UTF8Encoding $false))
        $base = "$rel\BibleStudy-Plugin-$($pjson.id)-$($pjson.version)"
        switch ($rid) {
            'win-x64'   { [IO.Compression.ZipFile]::CreateFromDirectory((Join-Path $rel 'pstage'), "$base-Windows.zip", 'Optimal', $false) }
            'linux-x64' { New-UnixTgz $pstage "$base-Linux-x64.tar.gz" $pjson.id }
            default     { New-UnixZip $pstage "$base-macOS-$($rid.Split('-')[1]).zip" $pjson.id }
        }
        Remove-Item -LiteralPath (Join-Path $rel 'pstage') -Recurse -Force
    }
}

Write-Host "== Linux and macOS packages (made on Windows, not tested by the build)"
foreach ($rid in 'linux-x64', 'osx-arm64', 'osx-x64') {
    $u = Join-Path $rel "unix-$rid\BibleStudy"
    dotnet publish "$root\src\BibleStudy.App\BibleStudy.App.csproj" -c Release -r $rid --self-contained true -p:Version=$Version -p:DebugType=none -o $u
    if ($LASTEXITCODE) { throw "publish app $rid failed" }
    dotnet publish "$root\src\BibleStudy.Mcp\BibleStudy.Mcp.csproj" -c Release -r $rid --self-contained true -p:Version=$Version -p:DebugType=none -o "$u\mcp"
    if ($LASTEXITCODE) { throw "publish mcp $rid failed" }
    # macOS treats a file called "BibleStudy.App" like an application bundle (".app") and kills it: the start file is just "BibleStudy"
    Move-Item (Join-Path $u 'BibleStudy.App') (Join-Path $u 'BibleStudy')
    Copy-Item "$root\LICENSE", "$root\THIRD-PARTY-NOTICES.md" $u
    if ($rid -like 'linux*') {
        Copy-UnixText "$root\packaging\linux\README-Linux.txt" (Join-Path $u 'README-Linux.txt')
        New-UnixTgz $u "$rel\BibleStudy-Linux-x64-$Version.tar.gz" 'BibleStudy'
    } else {
        Copy-UnixText "$root\packaging\macos\README-macOS.txt" (Join-Path $u 'README-macOS.txt')
        Copy-UnixText "$root\packaging\macos\Start-BibleStudy.command" (Join-Path $u 'Start-BibleStudy.command')
        New-UnixZip $u "$rel\BibleStudy-macOS-$($rid.Split('-')[1])-$Version.zip" 'BibleStudy'
    }
    Remove-Item -LiteralPath (Join-Path $rel "unix-$rid") -Recurse -Force
}
Remove-Item -LiteralPath (Join-Path $rel 'stage'),(Join-Path $rel 'setup'), $payload -Recurse -Force
Get-ChildItem $rel -File | Where-Object { $_.Name -ne 'SHA256SUMS.txt' } | ForEach-Object { '{0}  {1}' -f (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLower(), $_.Name } | Set-Content "$rel\SHA256SUMS.txt"
Get-ChildItem $rel -File | ForEach-Object { '{0,-40} {1,8:N1} MB' -f $_.Name, ($_.Length / 1MB) }
