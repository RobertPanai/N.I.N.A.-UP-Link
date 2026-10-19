# T6: token errato -> AuthError, nessun retry infinito, nessuna perdita; poi token corretto -> svuotamento
. (Join-Path (Split-Path -Parent $PSScriptRoot) 'lib\nina-test.ps1')
$ErrorActionPreference = 'Stop'
Stop-Nina $false
Write-Bootstrap $script:goodUrl 'wrongtoken0123456789'
Start-NinaWait; Connect-Sim
$ok = Capture 3
Write-Output "scatti riusciti: $ok"
Start-Sleep 60
Show-Status 'dopo 60 s con token errato'
$before = (Get-Content (Join-Path $script:data 'logs\uplink.log')).Count
Start-Sleep 60
$after = (Get-Content (Join-Path $script:data 'logs\uplink.log')).Count
Write-Output ("righe di log nei 60 s successivi: {0}" -f ($after - $before))
Show-Log 8
Stop-Nina $false
Write-Bootstrap $script:goodUrl $null
Start-NinaWait
Start-Sleep 75
Show-Status 'dopo token corretto'; Show-Log 5
# run: 1
