param([Parameter(Mandatory=$true)][string]$GamePath)
$ErrorActionPreference = 'Stop'
$meshRoot = Split-Path -Parent $PSScriptRoot
$setupRoot = Split-Path -Parent $meshRoot
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$testRoot = Join-Path $PSScriptRoot 'artifacts'
New-Item -ItemType Directory -Force -Path $testRoot | Out-Null
$json = Join-Path $GamePath 'A Township Tale_Data\Managed\Newtonsoft.Json.dll'
Copy-Item -LiteralPath $json -Destination $testRoot -Force
$testExe = Join-Path $testRoot 'TransportTests.exe'
& $compiler /nologo /target:exe /langversion:5 /optimize+ ('/out:' + $testExe) /reference:System.dll /reference:System.Core.dll ('/reference:' + $json) ('/reference:' + (Join-Path $GamePath 'A Township Tale_Data\Managed\netstandard.dll')) (Join-Path $meshRoot 'src\PairRegistry.cs') (Join-Path $setupRoot 'mod-src\MeshSocialTransport.cs') (Join-Path $setupRoot 'mod-src\MeshTabletTransport.cs') (Join-Path $PSScriptRoot 'TransportTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Mesh native transport test compilation failed.' }
& $testExe
if ($LASTEXITCODE -ne 0) { throw 'Mesh native transport tests failed.' }
