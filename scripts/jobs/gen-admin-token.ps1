# gen-admin-token
$dir = Join-Path $env:LOCALAPPDATA 'NINA\Plugins\MyUniverseUplink'
New-Item -ItemType Directory -Force -Path $dir | Out-Null
$f = Join-Path $dir 'admin-token.txt'
if (-not (Test-Path $f)) {
  $b = New-Object byte[] 32; [Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($b)
  [IO.File]::WriteAllText($f, [Convert]::ToBase64String($b).TrimEnd('=').Replace('+','-').Replace('/','_'))
}
$t = (Get-Content $f -Raw).Trim()
$h = [BitConverter]::ToString([Security.Cryptography.SHA256]::Create().ComputeHash([Text.Encoding]::UTF8.GetBytes($t))).Replace('-','').ToLower()
Write-Output ('ENTRY=ADMINHASH=' + $h)
