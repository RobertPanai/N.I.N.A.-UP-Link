# m0-install: chiude NINA con calma, sposta i vecchi plugin in _to_delete (nessuna cancellazione),
# installa il nuovo plugin in Plugins\3.0.0\My Universe Uplink\ e riavvia NINA.
$ErrorActionPreference = 'Stop'
$root    = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$plugins = Join-Path $env:LOCALAPPDATA 'NINA\Plugins'
$ninaExe = "C:\Program Files\N.I.N.A. - Nighttime Imaging 'N' Astronomy\NINA.exe"
$trash   = Join-Path $plugins ('_to_delete\' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
$src     = Join-Path $root 'src\Uplink.Plugin\bin\Release\net8.0-windows7.0'

if (-not (Test-Path (Join-Path $src 'MyUniverseUplink.dll'))) { throw "Build mancante: $src" }

# 1) chiusura di NINA (solo chiusura regolare, niente kill)
$p = Get-Process NINA -ErrorAction SilentlyContinue
if ($p) {
    Write-Output ('Chiudo NINA (PID ' + $p.Id + ')')
    [void]$p.CloseMainWindow()
    if (-not $p.WaitForExit(90000)) { throw 'NINA non si e chiuso entro 90 s (finestra di conferma?). Nessuna modifica fatta.' }
}
Start-Sleep -Seconds 2

# 2) vecchi plugin -> _to_delete
New-Item -ItemType Directory -Force -Path $trash | Out-Null
$old = @(
    (Join-Path $plugins '3.0.0\My Universe Uplink'),
    (Join-Path $plugins '3.2.0\MyUniverseUplink.dll'), (Join-Path $plugins '3.2.0\MyUniverseUplink.pdb'),
    (Join-Path $plugins '3.2.0\MyUniverseUplink.Core.dll'), (Join-Path $plugins '3.2.0\MyUniverseUplink.Core.pdb'),
    (Join-Path $plugins '3.2.0\HelloNina.dll'), (Join-Path $plugins '3.2.0\HelloNina.pdb')
)
foreach ($o in $old) {
    if (Test-Path $o) { Move-Item -LiteralPath $o -Destination (Join-Path $trash ((Split-Path $o -Parent | Split-Path -Leaf) + '__' + (Split-Path $o -Leaf))) ; Write-Output ('spostato: ' + $o) }
}

# 3) nuovo plugin
$dest = Join-Path $plugins '3.0.0\My Universe Uplink'
New-Item -ItemType Directory -Force -Path $dest | Out-Null
Copy-Item (Join-Path $src 'MyUniverseUplink.dll'), (Join-Path $src 'MyUniverseUplink.pdb'), (Join-Path $src 'MyUniverseUplink.Core.dll'), (Join-Path $src 'MyUniverseUplink.Core.pdb') -Destination $dest -Force
Get-ChildItem $dest | Select-Object Name, Length | Format-Table -AutoSize | Out-String

# 4) riavvio di NINA
Start-Process -FilePath $ninaExe
Write-Output 'NINA riavviato'
