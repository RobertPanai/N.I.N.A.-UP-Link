# plugin-folders: dove NINA 3.2 carica i plugin? Solo lettura.
$base = Join-Path $env:LOCALAPPDATA 'NINA\Plugins'
foreach ($v in '3.0.0','3.2.0') {
    $d = Join-Path $base $v
    Write-Output ("--- $d (esiste: " + (Test-Path $d) + ')')
    if (Test-Path $d) { Get-ChildItem $d -Force | Select-Object Mode, LastWriteTime, Name | Format-Table -AutoSize | Out-String -Width 200 }
}
Write-Output '--- plugin 0.1.0 "My Universe Uplink" (cartella 3.0.0)'
Get-ChildItem (Join-Path $base '3.0.0\My Universe Uplink') -Force -ErrorAction SilentlyContinue | Select-Object Name, Length, LastWriteTime | Format-Table -AutoSize | Out-String -Width 200
$logDir = Join-Path $env:LOCALAPPDATA 'NINA\Logs'
$log = Get-ChildItem $logDir -Filter *.log | Sort-Object LastWriteTime -Descending | Select-Object -First 1
Write-Output ('--- log NINA: ' + $log.FullName)
Select-String -Path $log.FullName -Pattern 'Plugin' | Select-Object -First 60 | ForEach-Object { $_.Line.Substring(0, [Math]::Min(230, $_.Line.Length)) }
