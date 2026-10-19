# env-check: versioni di .NET e git, pacchetti NINA, lab esistente, cartella dei plugin. Solo lettura.
Write-Output ('Data: ' + (Get-Date -Format s))
Write-Output ('Utente: ' + $env:USERNAME + ' su ' + $env:COMPUTERNAME)
Write-Output '--- dotnet'
try { dotnet --list-sdks; dotnet --list-runtimes | Select-String 'WindowsDesktop|NETCore.App' } catch { Write-Output 'dotnet NON trovato' }
Write-Output '--- git'
try { git --version } catch { Write-Output 'git NON trovato' }
Write-Output '--- pacchetti NuGet nina.*'
Get-ChildItem "$env:USERPROFILE\.nuget\packages" -Directory -Filter 'nina.*' | ForEach-Object { $_.Name + '  ' + ((Get-ChildItem $_.FullName -Directory | Select-Object -ExpandProperty Name) -join ',') }
Write-Output '--- fonti NuGet'
try { dotnet nuget list source } catch { Write-Output 'n/d' }
Write-Output '--- plugin installati in NINA'
Get-ChildItem "$env:LOCALAPPDATA\NINA\Plugins" -Directory -ErrorAction SilentlyContinue | ForEach-Object { $_.Name; Get-ChildItem $_.FullName -Directory | ForEach-Object { '   ' + $_.Name } }
Write-Output '--- lab esistente'
Get-ChildItem "$env:USERPROFILE\Documents\Codex" -Recurse -Filter 'build-plugin.ps1' -ErrorAction SilentlyContinue | Select-Object -First 3 -ExpandProperty FullName
Write-Output '--- NINA in esecuzione'
Get-Process NINA -ErrorAction SilentlyContinue | Select-Object Id, StartTime, Path | Format-Table -AutoSize | Out-String
