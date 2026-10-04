param()
$ErrorActionPreference = 'Stop'
$folder = Join-Path $PSScriptRoot 'artifacts'
New-Item -ItemType Directory -Force -Path $folder | Out-Null
$exe = Join-Path $folder 'FriendCardPickupTests.exe'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
& $compiler /nologo /target:exe /langversion:5 "/out:$exe" (Join-Path $PSScriptRoot 'FriendCardPickupTests.cs') (Join-Path $PSScriptRoot '..\mod-src\NativeFriendCard.cs')
if ($LASTEXITCODE -ne 0) { throw 'Friend-card pickup tests failed to compile.' }
& $exe
if ($LASTEXITCODE -ne 0) { throw 'Friend-card pickup tests failed.' }
