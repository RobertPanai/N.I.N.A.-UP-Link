# git-setup: collega la cartella di lavoro al repo RobertPanai/N.I.N.A.-UP-Link su un nuovo branch m0/skeleton da main.
# Non fa push. Non tocca i file esistenti (scripts, logs, test-images, config restano non tracciati).
$ErrorActionPreference = 'Continue'
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
Set-Location $root
Write-Output ('Cartella: ' + $root)
Write-Output ('git user.name  : ' + (git config user.name))
Write-Output ('git user.email : ' + (git config user.email))
Write-Output ('credential.helper: ' + (git config credential.helper))
if (-not (Test-Path (Join-Path $root '.git'))) { git init -b main 2>&1 | Write-Output }
if (-not (git remote)) { git remote add origin https://github.com/RobertPanai/N.I.N.A.-UP-Link.git }
git remote -v
Write-Output '--- fetch (GIT_TERMINAL_PROMPT=0: niente richieste di password)'
$env:GIT_TERMINAL_PROMPT = '0'
git fetch origin 2>&1 | Write-Output
Write-Output '--- branch remoti'
git branch -r
if (git rev-parse --verify origin/main 2>$null) {
    git checkout -B m0/skeleton origin/main 2>&1 | Write-Output
    git status --short | Select-Object -First 20
    git log --oneline -3
} else { Write-Output 'origin/main non disponibile (autenticazione?)' }
