# nina-api.ps1 - esegue le chiamate elencate in scripts\api-calls.txt contro NINA Advanced API
# Formato riga:  GET /percorso?query     (righe vuote e che iniziano con # ignorate)
# Esito in logs\nina-api-<data>.log
# Protezioni: (1) endpoint che muovono hardware sempre bloccati;
#             (2) endpoint che cambiano stato eseguiti solo se il profilo attivo contiene "Uplink Test".

$ErrorActionPreference = 'Continue'
$root   = Split-Path -Parent $PSScriptRoot
$logDir = Join-Path $root 'logs'
New-Item -ItemType Directory -Force -Path $logDir | Out-Null
$log = Join-Path $logDir ('nina-api-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '.log')
function Log([string]$m) { Write-Host $m; Add-Content -Path $log -Value $m -Encoding UTF8 }

$calls = Join-Path $PSScriptRoot 'api-calls.txt'
if (-not (Test-Path $calls)) { Log "api-calls.txt mancante"; return }

# Porta: 1888, con ricerca nelle porte vicine (il plugin puo' spostarsi se occupata)
$base = $null
foreach ($p in 1888..1898) {
    try {
        $r = Invoke-RestMethod -Uri "http://localhost:$p/v2/api/version" -TimeoutSec 3
        $base = "http://localhost:$p/v2/api"; Log ("Advanced API trovata sulla porta $p : " + ($r | ConvertTo-Json -Compress)); break
    } catch { }
}
if (-not $base) { Log 'ERRORE: Advanced API non raggiungibile (porte 1888-1898). NINA aperto? Server API attivo nelle opzioni del plugin?'; return }

$hardwareBlock = '(?i)^/equipment/mount/(slew|park|unpark|flip|home|set-park|stop-slew|tracking)|^/equipment/dome/(open|close|stop|set-follow|sync|slew|set-park|park|home)|^/equipment/camera/(cool|warm|dew-heater|usb-limit)|^/equipment/focuser/(move|set-temp)|^/equipment/rotator/(move|halt|sync|set-reverse)|^/equipment/flatdevice/(set-light|set-cover|set-brightness)'
$readOnly = '(?i)^/(version|application-start|plugin/settings|equipment/info|event-history|image-history|image/|prepared-image|profile/show|sequence/(json|state|list-available)|application/(get-tab|plugins|logs|screenshot)|equipment/[a-z]+/(info|list-devices|rescan)|equipment/camera/capture/statistics)'

function Test-TestProfile {
    try {
        $t = (Invoke-WebRequest -Uri "$base/profile/show?active=true" -UseBasicParsing -TimeoutSec 8).Content
        return ($t -match 'Uplink Test')
    } catch { return $false }
}

foreach ($line in Get-Content $calls) {
    $l = $line.Trim()
    if ($l -eq '' -or $l.StartsWith('#')) { continue }
    $parts = $l -split '\s+', 2
    $method = $parts[0].ToUpper(); $path = $parts[1]
    Log ''
    Log ("=== $method $path")
    if ($path -match $hardwareBlock) { Log 'BLOCCATO: endpoint che muove hardware'; continue }
    if ($path -notmatch $readOnly -and $path -notmatch '^/profile/switch') {
        $ok = Test-TestProfile
        # Eccezione di avvio: configurare il profilo vuoto creato in automatico (nome "Predefinito-...") per farlo diventare "Uplink Test"
        if (-not $ok -and $path -match '^/profile/change-value') {
            try { $ok = ((Invoke-WebRequest -Uri "$base/profile/show?active=true" -UseBasicParsing -TimeoutSec 8).Content -match '"Name":"Predefinito-') } catch { $ok = $false }
        }
        if (-not $ok) { Log 'BLOCCATO: cambia stato ma il profilo attivo non e "Uplink Test"'; continue }
    }
    try {
        $resp = Invoke-WebRequest -Uri ($base + $path) -Method $method -UseBasicParsing -TimeoutSec 60
        $body = $resp.Content
        if ($body.Length -gt 20000) { $body = $body.Substring(0, 20000) + "`n...[troncato, totale $($resp.Content.Length) caratteri]" }
        Log ("HTTP " + $resp.StatusCode)
        Log $body
    } catch {
        Log ("ERRORE: " + $_.Exception.Message)
    }
}
Log ''
Log ('Fine. Log: ' + $log)
