$ErrorActionPreference = 'Stop'
$socialRoot = Split-Path -Parent $PSScriptRoot
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$testRoot = Join-Path $PSScriptRoot ('artifacts\' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $testRoot | Out-Null
$testExe = Join-Path $testRoot 'RelayTests.exe'
& $compiler /nologo /target:exe /langversion:5 /optimize+ ('/out:' + $testExe) /reference:System.dll /reference:System.Core.dll /reference:System.Web.Extensions.dll (Join-Path $socialRoot 'src\Relay.cs') (Join-Path $socialRoot 'src\CardConsent.cs') (Join-Path $PSScriptRoot 'RelayTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Relay test compilation failed.' }
& $testExe $testRoot
if ($LASTEXITCODE -ne 0) { throw 'Relay tests failed.' }
