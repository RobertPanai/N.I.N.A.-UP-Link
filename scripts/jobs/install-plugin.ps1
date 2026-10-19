# install-plugin (da m0-install, senza spostamento dei vecchi plugin): chiude NINA con calma, sposta i vecchi plugin in _to_delete (nessuna cancellazione),
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

# 3) nuovo plugin
$dest = Join-Path $plugins '3.0.0\My Universe Uplink'
New-Item -ItemType Directory -Force -Path $dest | Out-Null
Copy-Item (Join-Path $src 'MyUniverseUplink.dll'), (Join-Path $src 'MyUniverseUplink.pdb'), (Join-Path $src 'MyUniverseUplink.Core.dll'), (Join-Path $src 'MyUniverseUplink.Core.pdb') -Destination $dest -Force
Get-ChildItem $dest | Select-Object Name, Length | Format-Table -AutoSize | Out-String


# 3b) bootstrap.json per configurare il plugin senza UI (token e bypass letti da file locali, mai stampati)
$dataDir = Join-Path $plugins 'MyUniverseUplink'
New-Item -ItemType Directory -Force -Path $dataDir | Out-Null
$tok = (Get-Content (Join-Path $dataDir 'dev-token.txt') -Raw).Trim()
$byp = (Get-Content (Join-Path $root 'config\bypass.txt') -Raw).Trim()
$bs = [ordered]@{ enabled = $true; serverUrl = 'https://sito-git-feat-uplink-robertpanai96-5335s-projects.vercel.app'; deviceId = 'serversat'; token = $tok; extraHeaderName = 'x-vercel-protection-bypass'; extraHeaderValue = $byp; requireTarget = $false; imageTypes = @('LIGHT'); thumbnailMode = 'Always'; thumbnailLongEdge = 400; thumbnailQuality = 70; batchSize = 25; htmlReport = $true }
[IO.File]::WriteAllText((Join-Path $dataDir 'bootstrap.json'), ($bs | ConvertTo-Json))
Write-Output 'bootstrap.json scritto'

# 4) riavvio di NINA
Start-Process -FilePath $ninaExe
Write-Output 'NINA riavviato'
