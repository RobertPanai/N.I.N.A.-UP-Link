# T4 (server irraggiungibile) + T5 (kill di NINA con coda piena, replay) + T1 (impostazioni dopo riavvio)
. (Join-Path (Split-Path -Parent $PSScriptRoot) 'lib\nina-test.ps1')
$ErrorActionPreference = 'Stop'
Write-Output '--- T4: URL irraggiungibile'
Stop-Nina $false
Write-Bootstrap 'https://127.0.0.1:9' $null
Start-NinaWait; Connect-Sim
$ok = Capture 5
Write-Output "scatti riusciti: $ok"
Start-Sleep 25
Show-Status 'dopo 5 scatti con server irraggiungibile'; Show-Log 8
Write-Output '--- T5: kill di NINA con coda piena, poi riavvio con URL corretto'
Stop-Nina $true
Show-Status 'dopo kill'
Write-Bootstrap $script:goodUrl $null
Start-NinaWait
Start-Sleep 90
Show-Status 'dopo riavvio e replay'; Show-Log 10
Write-Output '--- T1: impostazioni conservate (riavvio senza bootstrap)'
Stop-Nina $false
Start-NinaWait
Start-Sleep 5
Show-Log 4
# run: 1
