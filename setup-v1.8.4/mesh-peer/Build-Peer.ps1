[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$GameFolder)
$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$managed = Join-Path $GameFolder 'A Township Tale_Data\Managed'
$json = Join-Path $managed 'Newtonsoft.Json.dll'
$dist = Join-Path $PSScriptRoot 'dist'
New-Item -ItemType Directory -Force -Path $dist | Out-Null
$argsForCompiler = @('/nologo','/target:exe','/platform:x64','/langversion:5','/optimize+',('/out:'+(Join-Path $dist 'TavernMeshPeer.exe')),('/reference:'+$json),('/reference:'+(Join-Path $managed 'netstandard.dll')))
$argsForCompiler += @(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'src') -Filter '*.cs' -File | ForEach-Object FullName)
& $compiler @argsForCompiler
if ($LASTEXITCODE -ne 0) { throw 'Peer helper compilation failed.' }
Copy-Item -LiteralPath $json -Destination $dist -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'LICENSE.txt') -Destination (Join-Path $dist 'MESH-PEER-LICENSE.txt') -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'NEWTONSOFT-LICENSE.txt') -Destination $dist -Force
Compress-Archive -LiteralPath @((Join-Path $PSScriptRoot 'src'),(Join-Path $PSScriptRoot 'Build-Peer.ps1'),(Join-Path $PSScriptRoot 'LICENSE.txt'),(Join-Path $PSScriptRoot 'NEWTONSOFT-LICENSE.txt'),(Join-Path $PSScriptRoot 'README.md')) -DestinationPath (Join-Path $dist 'mesh-peer-source.zip') -Force
Write-Output ('Built automatic peer helper: '+(Join-Path $dist 'TavernMeshPeer.exe'))
