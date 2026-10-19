# nina-watcher.ps1 (v2) - due compiti, nessun clic:
#  1) api-calls.txt cambiato   -> esegue nina-api.ps1 (chiamate all'API di NINA, con protezioni)
#  2) scripts\jobs\<nome>.ps1 nuovo o modificato -> lo esegue UNA volta (build/test/installazione del plugin)
# Esegue solo file di queste due posizioni, scritti da Claude nella sessione con Robert.
# I job girano con i permessi dell'utente: Claude li usa solo per build, test e installazione del plugin UP-Link.
# Log: logs\nina-api-*.log e logs\job-<nome>-<data>.log (+ .exitcode). Timeout job: 15 minuti.
# Per fermarlo: creare un file STOP-WATCHER in questa cartella.

$root    = Split-Path -Parent $PSScriptRoot
$calls   = Join-Path $PSScriptRoot 'api-calls.txt'
$stop    = Join-Path $PSScriptRoot 'STOP-WATCHER'
$runner  = Join-Path $PSScriptRoot 'nina-api.ps1'
$jobsDir = Join-Path $PSScriptRoot 'jobs'
$logDir  = Join-Path $root 'logs'
$alive   = Join-Path $logDir 'watcher-alive.txt'
New-Item -ItemType Directory -Force -Path $logDir, $jobsDir | Out-Null
Remove-Item $stop -ErrorAction SilentlyContinue

$lastCalls = (Get-Item $calls).LastWriteTimeUtc.Ticks
$seen = @{}
foreach ($f in Get-ChildItem $jobsDir -Filter *.ps1 -ErrorAction SilentlyContinue) { $seen[$f.Name] = $f.LastWriteTimeUtc.Ticks }   # i job gia' presenti non vengono rieseguiti
Write-Host 'Watcher v2 attivo (API + jobs). Crea STOP-WATCHER per fermarlo.'

while (-not (Test-Path $stop)) {
    Set-Content -Path $alive -Value ((Get-Date).ToString('s') + ' v2') -Encoding ASCII
    Start-Sleep -Seconds 2

    # 1) chiamate API
    $t = (Get-Item $calls).LastWriteTimeUtc.Ticks
    if ($t -ne $lastCalls) {
        Start-Sleep -Seconds 1
        $lastCalls = (Get-Item $calls).LastWriteTimeUtc.Ticks
        Write-Host ((Get-Date).ToString('HH:mm:ss') + ' api-calls.txt cambiato: eseguo')
        try { & $runner } catch { Write-Host ('ERRORE: ' + $_.Exception.Message) }
    }

    # 2) job
    foreach ($f in Get-ChildItem $jobsDir -Filter *.ps1 -ErrorAction SilentlyContinue) {
        $ticks = $f.LastWriteTimeUtc.Ticks
        if ($seen[$f.Name] -eq $ticks) { continue }
        Start-Sleep -Seconds 1
        $f.Refresh(); $seen[$f.Name] = $f.LastWriteTimeUtc.Ticks
        $stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
        $base  = Join-Path $logDir ('job-' + $f.BaseName + '-' + $stamp)
        Write-Host ((Get-Date).ToString('HH:mm:ss') + ' job: ' + $f.Name)
        $p = Start-Process -FilePath 'powershell.exe' -ArgumentList @('-NoProfile','-ExecutionPolicy','Bypass','-File',$f.FullName) `
              -RedirectStandardOutput ($base + '.log') -RedirectStandardError ($base + '.err') -NoNewWindow -PassThru
        if (-not $p.WaitForExit(900000)) { try { $p.Kill($true) } catch {}; Add-Content ($base + '.log') 'TIMEOUT: job interrotto dopo 15 minuti' }
        Set-Content -Path ($base + '.exitcode') -Value $p.ExitCode -Encoding ASCII
    }
}
Write-Host 'Watcher fermato.'
