# Data made by the last published version must open in this one: the probe is built against the old code and writes a vault and an imported
# Bible, then it is built against this code and reads them. It tests the data, not the download of the update.
# All of it happens in the temp folder; the repository gets a temporary worktree that is removed afterwards.
param([string]$Against = '')
$ErrorActionPreference = 'Continue'
$root = $PSScriptRoot
git -C $root fetch --tags --quiet origin 2>$null
if (-not $Against) {
    $Against = git -C $root tag --list 'v*' | Where-Object { $_ -match '^v\d+\.\d+\.\d+$' } | Sort-Object { [version]$_.Substring(1) } | Select-Object -Last 1
}
if (-not $Against) { throw 'No published version found to compare with' }
$work = Join-Path ([IO.Path]::GetTempPath()) ("bs_probe_" + [guid]::NewGuid().ToString('N'))
$old = Join-Path $work 'old'; $data = Join-Path $work 'data'
New-Item -ItemType Directory -Force $work | Out-Null
function Build-Probe([string]$name, [string]$core, [string]$extra = "") {
    $src = Join-Path $work "probe-$name"
    New-Item -ItemType Directory -Force $src | Out-Null
    Copy-Item "$root\tests\BibleStudy.UpdateProbe\Program.cs", "$root\tests\BibleStudy.UpdateProbe\BibleStudy.UpdateProbe.csproj" $src
    dotnet build "$src\BibleStudy.UpdateProbe.csproj" -c Release --nologo -v q "-p:CoreProject=$core" "-p:DefineConstants=$extra" -o "$src\out" | Out-Null
    if ($LASTEXITCODE) { throw "The probe could not be built against $name" }
    return "$src\out\BibleStudy.UpdateProbe.dll"
}
$failed = $false
try {
    Write-Host "Comparing with $Against"
    git -C $root worktree add --quiet --detach $old $Against
    if ($LASTEXITCODE) { throw "No worktree for $Against" }
    $seed = Build-Probe 'old' "$old\src\BibleStudy.Core\BibleStudy.Core.csproj"
    dotnet $seed seed $data
    if ($LASTEXITCODE) { throw 'The old version could not write its data' }
    $verify = Build-Probe 'new' "$root\src\BibleStudy.Core\BibleStudy.Core.csproj" 'VERIFY'
    dotnet $verify verify $data
    if ($LASTEXITCODE) { $failed = $true }
}
finally {
    git -C $root worktree remove --force $old 2>$null
    git -C $root worktree prune
    Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
}
if ($failed) { throw 'Data of the last version does not open correctly in this one' }
