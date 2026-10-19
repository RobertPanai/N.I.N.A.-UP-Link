# check-plugin-load: il nuovo plugin e' stato caricato da NINA? Solo lettura del log piu' recente.
$logDir = Join-Path $env:LOCALAPPDATA 'NINA\Logs'
$deadline = (Get-Date).AddSeconds(150)
do {
    $log = Get-ChildItem $logDir -Filter *.log | Sort-Object LastWriteTime -Descending | Select-Object -First 1
    $txt = if ($log) { Get-Content $log.FullName -Raw -ErrorAction SilentlyContinue } else { '' }
    if ($txt -match 'PluginFetcher.cs\|RequestAll') { break }
    Start-Sleep -Seconds 5
} while ((Get-Date) -lt $deadline)
Write-Output ('Log: ' + $log.FullName + '  (avviato: ' + $log.CreationTime + ')')
Get-Content $log.FullName | Select-String -Pattern 'Uplink|PluginLoader.cs\|.*(Error|Fail|Exception|Could not)|ERROR\|PluginLoader' | ForEach-Object { $_.Line.Substring(0, [Math]::Min(260, $_.Line.Length)) }
Write-Output '--- plugin caricati (conteggio)'
(Get-Content $log.FullName | Select-String 'Successfully loaded plugin').Count
Write-Output '--- profilo attivo / errori generali recenti'
Get-Content $log.FullName | Select-String -Pattern '\|ERROR\|' | Select-Object -First 8 | ForEach-Object { $_.Line.Substring(0, [Math]::Min(230, $_.Line.Length)) }
# run: 1792417237
# run: 1792417705
