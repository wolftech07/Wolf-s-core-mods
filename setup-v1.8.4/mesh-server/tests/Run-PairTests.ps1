$ErrorActionPreference = 'Stop'
$meshRoot = Split-Path -Parent $PSScriptRoot
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$testRoot = Join-Path $PSScriptRoot 'artifacts'
New-Item -ItemType Directory -Force -Path $testRoot | Out-Null
$testExe = Join-Path $testRoot 'PairTests.exe'
& $compiler /nologo /target:exe /langversion:5 /optimize+ ('/out:' + $testExe) /reference:System.dll /reference:System.Core.dll (Join-Path $meshRoot 'src\PairRegistry.cs') (Join-Path $meshRoot 'src\CardConsent.cs') (Join-Path $PSScriptRoot 'PairTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Mesh pair test compilation failed.' }
& $testExe
if ($LASTEXITCODE -ne 0) { throw 'Mesh pair tests failed.' }
