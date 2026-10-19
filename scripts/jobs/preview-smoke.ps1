# preview-smoke: prova l'ingest della Preview con la fixture. Token e bypass letti da file locali, mai stampati.
$ErrorActionPreference = 'Continue'
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$base = 'https://sito-git-feat-uplink-robertpanai96-5335s-projects.vercel.app'
$token = (Get-Content (Join-Path $env:LOCALAPPDATA 'NINA\Plugins\MyUniverseUplink\dev-token.txt') -Raw).Trim()
$bypass = (Get-Content (Join-Path $root 'config\bypass.txt') -Raw).Trim()
$h = @{ 'x-vercel-protection-bypass' = $bypass }
function Call($name, $method, $path, $headers, $body) {
  try {
    $r = Invoke-WebRequest -Uri ($base + $path) -Method $method -Headers $headers -Body $body -ContentType 'application/json' -UseBasicParsing -TimeoutSec 60
    Write-Output ("{0}: HTTP {1} {2}" -f $name, $r.StatusCode, $r.Content.Substring(0, [Math]::Min(400, $r.Content.Length)))
  } catch {
    $code = if ($_.Exception.Response) { [int]$_.Exception.Response.StatusCode } else { 'ERR' }
    Write-Output ("{0}: HTTP {1} {2}" -f $name, $code, $_.Exception.Message)
  }
}
$auth = $h.Clone(); $auth['Authorization'] = "Bearer $token"
$bad = $h.Clone(); $bad['Authorization'] = 'Bearer wrong'
$body = (Get-Content (Join-Path $root 'contract\batch-v1.request.json') -Raw).Replace('osservatorio-casa','serversat')
Call 'health-auth' 'GET' '/api/uplink/health' $auth $null
Call 'post-1' 'POST' '/api/uplink/frames' $auth $body
Call 'post-2-dup' 'POST' '/api/uplink/frames' $auth $body
Call 'post-badtoken' 'POST' '/api/uplink/frames' $bad $body
$sim = $auth.Clone(); $sim['X-Uplink-Simulate'] = '429'
Call 'sim-429' 'POST' '/api/uplink/frames' $sim $body
# run: 2
