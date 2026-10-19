# activity-check: scatta 4 frame simulati, stampa activity.log e stato dirette
. (Join-Path (Split-Path -Parent $PSScriptRoot) 'lib\nina-test.ps1')
Start-Sleep 60
Connect-Sim
$ok = Capture 4
Write-Output "scatti riusciti: $ok"
Start-Sleep 30
Get-Content (Join-Path $script:data 'logs\activity.log') -Tail 40 | ForEach-Object { $_ -replace '(Bearer )\S+', '$1***' -replace 'tok=\S+','tok=***' }
# run: 4
