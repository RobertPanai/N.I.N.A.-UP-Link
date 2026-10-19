# page-check: scarica /sessioni-astronomiche dalla Preview e stampa un estratto del testo.
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$base = 'https://sito-git-feat-uplink-robertpanai96-5335s-projects.vercel.app'
$byp = (Get-Content (Join-Path $root 'config\bypass.txt') -Raw).Trim()
$sw = [Diagnostics.Stopwatch]::StartNew()
$r = Invoke-WebRequest -Uri "$base/sessioni-astronomiche" -Headers @{ 'x-vercel-protection-bypass' = $byp } -UseBasicParsing -TimeoutSec 120
$sw.Stop()
$t = [regex]::Replace([regex]::Replace($r.Content, '<script[\s\S]*?</script>|<style[\s\S]*?</style>', ' '), '<[^>]+>', ' ')
$t = [regex]::Replace($t, '\s+', ' ')
Write-Output ("HTTP {0}, {1} ms, {2} byte" -f $r.StatusCode, $sw.ElapsedMilliseconds, $r.Content.Length)
Write-Output ('thumb img blob: ' + ([regex]::Matches($r.Content, 'public\.blob\.vercel-storage\.com')).Count)
$i = $t.IndexOf('Ultima ripresa'); if ($i -ge 0) { Write-Output $t.Substring($i, [Math]::Min(900, $t.Length - $i)) } else { Write-Output 'blocco Ultima ripresa NON trovato'; Write-Output $t.Substring(0, [Math]::Min(1200, $t.Length)) }
# run: 1
