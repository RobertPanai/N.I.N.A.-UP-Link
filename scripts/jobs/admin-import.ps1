# admin-import: import una tantum di integration.csv sulla Preview (dry-run, poi reale). Token letti da file, mai stampati.
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$base = 'https://sito-git-feat-uplink-robertpanai96-5335s-projects.vercel.app'
$tok = (Get-Content (Join-Path $env:LOCALAPPDATA 'NINA\Plugins\MyUniverseUplink\admin-token.txt') -Raw).Trim()
$byp = (Get-Content (Join-Path $root 'config\bypass.txt') -Raw).Trim()
$h = @{ 'x-vercel-protection-bypass' = $byp; Authorization = "Bearer $tok" }
foreach ($q in '?dryRun=1','') {
  try { $r = Invoke-WebRequest -Uri "$base/api/uplink/admin/import-csv$q" -Method POST -Headers $h -UseBasicParsing -TimeoutSec 120; Write-Output ("import$q : HTTP {0} {1}" -f $r.StatusCode, $r.Content) }
  catch { $c = if ($_.Exception.Response) { [int]$_.Exception.Response.StatusCode } else { 'ERR' }; Write-Output ("import$q : HTTP $c " + $_.Exception.Message) }
}
# run: 1
