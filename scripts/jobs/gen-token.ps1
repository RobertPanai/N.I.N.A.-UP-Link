# gen-token: token ingest per il dispositivo "serversat". Il token in chiaro resta SOLO in %LOCALAPPDATA%; nel log va l'hash.
$dir = Join-Path $env:LOCALAPPDATA 'NINA\Plugins\MyUniverseUplink'
New-Item -ItemType Directory -Force -Path $dir | Out-Null
$f = Join-Path $dir 'dev-token.txt'
if (-not (Test-Path $f)) {
  $b = New-Object byte[] 32; [Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($b)
  [IO.File]::WriteAllText($f, [Convert]::ToBase64String($b).TrimEnd('=').Replace('+','-').Replace('/','_'))
}
$t = (Get-Content $f -Raw).Trim()
$h = [BitConverter]::ToString([Security.Cryptography.SHA256]::Create().ComputeHash([Text.Encoding]::UTF8.GetBytes($t))).Replace('-','').ToLower()
Write-Output ('ENTRY=serversat:' + $h)
