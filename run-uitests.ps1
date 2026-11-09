# Drives the real main window in a window that is not on the screen, with its own data folder.
# Backups and exports of the test go into the temp folder, nothing is opened on the screen.
# A failed build stops here: an old test program must never run in place of the new one.
$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot 'tests\BibleStudy.UiTests'
$exe = Join-Path $project 'bin\Debug\net10.0\BibleStudy.UiTests.exe'
if (Test-Path $exe) { Remove-Item -LiteralPath $exe -Force }
dotnet build "$project\BibleStudy.UiTests.csproj" -c Debug --nologo -v q
if ($LASTEXITCODE -or -not (Test-Path $exe)) { throw 'The window tests could not be built' }
$out = Join-Path ([IO.Path]::GetTempPath()) 'bs_uitest_out.txt'
$err = Join-Path ([IO.Path]::GetTempPath()) 'bs_uitest_err.txt'
$p = Start-Process -FilePath $exe -NoNewWindow -PassThru -RedirectStandardOutput $out -RedirectStandardError $err
$handle = $p.Handle   # without it the exit code of a process started like this is lost
if (-not $p.WaitForExit(560000)) { $p.Kill(); throw 'The window tests did not finish in time' }
Get-Content $out -Encoding utf8
Get-Content $err | Select-Object -First 25
if ($p.ExitCode -ne 0 -or -not (Select-String -Path $out -Pattern '^ALL OK' -Quiet)) { throw 'The window tests failed' }
