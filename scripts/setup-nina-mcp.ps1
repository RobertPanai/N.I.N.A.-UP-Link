# setup-nina-mcp.ps1
# Prepara il server MCP per NINA Advanced API. Non modifica NINA ne' la configurazione di Claude.
# Scrive tutto in logs\setup-nina-mcp-<data>.log

$ErrorActionPreference = 'Continue'
$root   = Split-Path -Parent $PSScriptRoot
$logDir = Join-Path $root 'logs'
New-Item -ItemType Directory -Force -Path $logDir | Out-Null
$log = Join-Path $logDir ('setup-nina-mcp-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '.log')

function Log([string]$m) {
    $line = (Get-Date -Format 'HH:mm:ss') + '  ' + $m
    Write-Host $line
    Add-Content -Path $log -Value $line
}

Log ('Inizio. Log: ' + $log)

# 1. uv
if (Get-Command uv -ErrorAction SilentlyContinue) {
    Log ('uv OK: ' + (uv --version))
} else {
    Log 'uv non trovato: installo con winget...'
    winget install --id astral-sh.uv -e --accept-package-agreements --accept-source-agreements 2>&1 | ForEach-Object { Log ([string]$_) }
    $env:Path = [Environment]::GetEnvironmentVariable('Path', 'Machine') + ';' + [Environment]::GetEnvironmentVariable('Path', 'User')
    if (Get-Command uv -ErrorAction SilentlyContinue) { Log ('uv installato: ' + (uv --version)) }
    else { Log 'ERRORE: uv ancora non trovato dopo l installazione (riapri PowerShell e rilancia lo script)' }
}
$uvPath = (Get-Command uv -ErrorAction SilentlyContinue).Source
Log ('Percorso uv: ' + $uvPath)

# 2. Python (solo informativo: uv puo' gestirlo da solo)
if (Get-Command python -ErrorAction SilentlyContinue) { Log ('python: ' + (python --version 2>&1)) }
else { Log 'python non nel PATH: non e un problema, uv lo gestisce' }

# 3. Repo del server MCP
$repo = Join-Path (Split-Path -Parent $root) 'Nina_advanced_api_mcp'
if (Test-Path (Join-Path $repo 'nina_advanced_mcp.py')) { Log ('Repo OK: ' + $repo) }
else { Log ('ERRORE: repo non trovato in ' + $repo) }

# 4. NINA Advanced API sulla porta 1888
try {
    $v = Invoke-RestMethod -Uri 'http://localhost:1888/v2/api/version' -TimeoutSec 5
    Log ('Advanced API OK: ' + ($v | ConvertTo-Json -Compress))
} catch {
    Log ('Advanced API NON raggiungibile su porta 1888: ' + $_.Exception.Message)
    Log '  -> controlla che NINA sia aperto e che il plugin Advanced API sia in Installed'
}

# 5. Configurazione Claude (solo lettura)
$cfg = Join-Path $env:APPDATA 'Claude\claude_desktop_config.json'
Log ('Config Claude: ' + $cfg + ' (esiste: ' + (Test-Path $cfg) + ')')

Log 'Fine. Mandami il file di log (o il suo contenuto).'
Write-Host ''
Write-Host ('Log salvato in: ' + $log)
