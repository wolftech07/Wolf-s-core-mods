param([Parameter(Mandatory=$true)][string]$GamePath)
$ErrorActionPreference = 'Stop'
$meshRoot = Split-Path -Parent $PSScriptRoot
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$testRoot = Join-Path $PSScriptRoot 'artifacts\tablet'
New-Item -ItemType Directory -Force -Path $testRoot | Out-Null
$managed = Join-Path $GamePath 'A Township Tale_Data\Managed'
$json = Join-Path $managed 'Newtonsoft.Json.dll'
Copy-Item -LiteralPath $json -Destination $testRoot -Force
$testExe = Join-Path $testRoot 'TabletPolicyTests.exe'
& $compiler /nologo /target:exe /langversion:5 /optimize+ ('/out:' + $testExe) /reference:System.dll /reference:System.Core.dll ('/reference:' + $json) ('/reference:' + (Join-Path $managed 'netstandard.dll')) (Join-Path $meshRoot 'src\PairRegistry.cs') (Join-Path $meshRoot 'src\TabletPolicy.cs') (Join-Path $PSScriptRoot 'TabletPolicyTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Tablet policy test compilation failed.' }
& $testExe $managed
if ($LASTEXITCODE -ne 0) { throw 'Tablet policy tests failed.' }
