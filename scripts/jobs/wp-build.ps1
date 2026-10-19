# wp-build: build + test completi di Uplink.Core (WP-P1/P2). Nessun push, nessuna installazione, nessuna creazione sln.
# run: 20261019-134505
$ErrorActionPreference = 'Continue'
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
Set-Location $root
dotnet --version
Write-Output '=== BUILD'
dotnet build MyUniverseUplink.sln -c Release --nologo
Write-Output ('build exit: ' + $LASTEXITCODE)
Write-Output '=== TEST'
dotnet test tests/Uplink.Core.Tests/Uplink.Core.Tests.csproj -c Release --nologo --no-build -v normal
Write-Output ('test exit: ' + $LASTEXITCODE)
