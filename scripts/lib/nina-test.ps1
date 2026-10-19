# nina-test.ps1: funzioni comuni degli scenari di collaudo (solo profilo "Uplink Test", camera simulata)
$script:root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$script:data = Join-Path $env:LOCALAPPDATA 'NINA\Plugins\MyUniverseUplink'
$script:api = 'http://localhost:1888/v2/api'
$script:ninaExe = "C:\Program Files\N.I.N.A. - Nighttime Imaging 'N' Astronomy\NINA.exe"
$script:goodUrl = 'https://sito-git-feat-uplink-robertpanai96-5335s-projects.vercel.app'
function Stop-Nina([bool]$force) {
  $p = Get-Process NINA -ErrorAction SilentlyContinue
  if (-not $p) { return }
  if ($force) { Stop-Process -Id $p.Id -Force; Start-Sleep 3 }
  else { [void]$p.CloseMainWindow(); if (-not $p.WaitForExit(90000)) { throw 'NINA non si chiude' } }
}
function Write-Bootstrap([string]$url, [string]$token) {
  $byp = (Get-Content (Join-Path $script:root 'config\bypass.txt') -Raw).Trim()
  if (-not $token) { $token = (Get-Content (Join-Path $script:data 'dev-token.txt') -Raw).Trim() }
  $bs = [ordered]@{ enabled = $true; serverUrl = $url; deviceId = 'serversat'; token = $token; extraHeaderName = 'x-vercel-protection-bypass'; extraHeaderValue = $byp; requireTarget = $false; imageTypes = @('LIGHT'); thumbnailMode = 'Always'; thumbnailLongEdge = 400; thumbnailQuality = 70; batchSize = 25; htmlReport = $true }
  [IO.File]::WriteAllText((Join-Path $script:data 'bootstrap.json'), ($bs | ConvertTo-Json))
}
function Start-NinaWait {
  Start-Process -FilePath $script:ninaExe
  for ($i = 0; $i -lt 60; $i++) { Start-Sleep 5; try { $r = Invoke-RestMethod "$($script:api)/profile/show?active=true" -TimeoutSec 5; if ($r.Success) { return } } catch {} }
  throw 'API NINA non risponde'
}
function Connect-Sim {
  [void](Invoke-RestMethod "$($script:api)/equipment/camera/connect?to=ASCOM.Simulator.Camera" -TimeoutSec 60)
  [void](Invoke-RestMethod "$($script:api)/equipment/filterwheel/connect?to=FilterWheelSim.FilterWheel" -TimeoutSec 60)
}
function Test-Profile { $p = Invoke-RestMethod "$($script:api)/profile/show?active=true" -TimeoutSec 20; return (($p | ConvertTo-Json -Depth 3 -Compress) -match 'Uplink Test') }
function Capture([int]$n, [int]$pause = 4) {
  if (-not (Test-Profile)) { throw 'profilo diverso da Uplink Test' }
  $ok = 0
  for ($i = 1; $i -le $n; $i++) {
    try { $r = Invoke-RestMethod "$($script:api)/equipment/camera/capture?duration=2&save=true&waitForResult=true&omitImage=true&imageType=LIGHT&targetName=UplinkTest" -TimeoutSec 90; if ($r.Success) { $ok++ } } catch {}
    Start-Sleep $pause
  }
  return $ok
}
function Show-Status([string]$label) {
  $pc = @(Get-ChildItem (Join-Path $script:data 'pending') -File -ErrorAction SilentlyContinue).Count
  $dc = @(Get-ChildItem (Join-Path $script:data 'dead') -File -ErrorAction SilentlyContinue).Count
  Write-Output ("[{0}] pending={1} dead={2}" -f $label, $pc, $dc)
}
function Show-Log([int]$n = 12) { Get-Content (Join-Path $script:data 'logs\uplink.log') -Tail $n -ErrorAction SilentlyContinue | ForEach-Object { $_ -replace '(Bearer )\S+', '$1***' } }
