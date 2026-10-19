# uplink-status: stato del plugin (conteggi cartelle, ultime righe del log del plugin). Nessun segreto stampato.
$d = Join-Path $env:LOCALAPPDATA 'NINA\Plugins\MyUniverseUplink'
foreach ($s in 'pending','dead','thumbs','report') { $p = Join-Path $d $s; Write-Output ("{0}: {1} file" -f $s, @(Get-ChildItem $p -File -ErrorAction SilentlyContinue).Count) }
Write-Output ('bootstrap.json presente: ' + (Test-Path (Join-Path $d 'bootstrap.json')))
Write-Output '--- uplink.log (ultime 25)'
Get-Content (Join-Path $d 'logs\uplink.log') -Tail 25 -ErrorAction SilentlyContinue
# run: 1
# run: 2
