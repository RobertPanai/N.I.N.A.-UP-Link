# capture-run: N scatti LIGHT simulati (camera simulata, profilo "Uplink Test"), con pausa tra uno e l'altro.
$n = 20; $dur = 2; $pause = 4
$base = 'http://localhost:1888/v2/api'
$prof = Invoke-RestMethod "$base/profile/show?active=true" -TimeoutSec 20
if (($prof | ConvertTo-Json -Depth 3 -Compress) -notmatch 'Uplink Test') { Write-Output 'Profilo attivo diverso da Uplink Test: interrotto'; return }
$ok = 0
for ($i = 1; $i -le $n; $i++) {
  try {
    $r = Invoke-RestMethod "$base/equipment/camera/capture?duration=$dur&save=true&waitForResult=true&omitImage=true&imageType=LIGHT&targetName=UplinkTest" -TimeoutSec 90
    if ($r.Success) { $ok++ } else { Write-Output ("{0}: {1}" -f $i, $r.Error) }
  } catch { Write-Output ("{0}: eccezione {1}" -f $i, $_.Exception.Message) }
  Start-Sleep -Seconds $pause
}
Write-Output ("scatti riusciti: $ok / $n")
# run: 1
