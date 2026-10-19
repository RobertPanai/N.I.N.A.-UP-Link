# m0-build: crea la solution, aggiunge i progetti, compila e lancia i test. Nessun push, nessuna installazione.
$ErrorActionPreference = 'Continue'
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
Set-Location $root
dotnet --version
if (-not (Test-Path 'MyUniverseUplink.sln')) {
    dotnet new sln -n MyUniverseUplink
    dotnet sln MyUniverseUplink.sln add src/Uplink.Core/Uplink.Core.csproj src/Uplink.Plugin/Uplink.Plugin.csproj tests/Uplink.Core.Tests/Uplink.Core.Tests.csproj
}
if (-not (Select-String -Path tests/Uplink.Core.Tests/Uplink.Core.Tests.csproj -Pattern 'xunit' -Quiet)) {
    dotnet add tests/Uplink.Core.Tests/Uplink.Core.Tests.csproj package Microsoft.NET.Test.Sdk
    dotnet add tests/Uplink.Core.Tests/Uplink.Core.Tests.csproj package xunit
    dotnet add tests/Uplink.Core.Tests/Uplink.Core.Tests.csproj package xunit.runner.visualstudio
}
Write-Output '=== BUILD'
dotnet build MyUniverseUplink.sln -c Release --nologo
Write-Output ('build exit: ' + $LASTEXITCODE)
Write-Output '=== TEST'
dotnet test tests/Uplink.Core.Tests/Uplink.Core.Tests.csproj -c Release --nologo --no-build
Write-Output ('test exit: ' + $LASTEXITCODE)
Write-Output '=== ARTEFATTI'
Get-ChildItem src/Uplink.Plugin/bin/Release -Recurse -Filter 'MyUniverseUplink*.dll' | Select-Object FullName, Length | Format-Table -AutoSize | Out-String -Width 250
