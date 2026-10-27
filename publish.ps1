# Builds a self-contained single-file Windows release into ./release (no .NET install needed on the target PC).
$ErrorActionPreference = 'Stop'
dotnet publish src/BibleStudy.App/BibleStudy.App.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o release
Copy-Item LICENSE, THIRD-PARTY-NOTICES.md release/ -Force
Write-Host "Done: release\BibleStudy.App.exe"
