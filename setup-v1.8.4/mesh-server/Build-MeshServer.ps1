param([Parameter(Mandatory=$true)][string]$GamePath)
$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$managed = Join-Path $GamePath 'A Township Tale_Data\Managed'
$melon = Join-Path $GamePath 'MelonLoader\net472'
$output = Join-Path $PSScriptRoot 'dist'
New-Item -ItemType Directory -Force -Path $output | Out-Null
$companion = Join-Path $output 'TavernNativeSocialServer.dll'
$refs = @('mscorlib.dll','System.dll','System.Core.dll','netstandard.dll','Newtonsoft.Json.dll','Root.Township.dll','UnityEngine.dll','UnityEngine.CoreModule.dll','Alta.Api.DataTransferModels.dll','Alta.Core.Runtime.dll','Alta.CommandLine.dll','Alta.Timing.Runtime.dll','Alta.Serialization.Runtime.dll','Alta.NetworkTransport.Runtime.dll','Alta.Global.Runtime.dll','Alta.Singletons.dll','Township.Core.Runtime.dll')
$arguments = @('/nologo','/noconfig','/target:library','/optimize+','/langversion:5','/nostdlib+',('/out:' + $companion))
foreach ($ref in $refs) { $arguments += '/reference:' + (Join-Path $managed $ref) }
foreach ($ref in @('MelonLoader.dll','0Harmony.dll')) { $arguments += '/reference:' + (Join-Path $melon $ref) }
$arguments += @(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'src') -Filter '*.cs' | ForEach-Object { $_.FullName })
& $compiler @arguments
if ($LASTEXITCODE -ne 0) { throw 'Mesh server companion compilation failed.' }
Write-Output ('Built ' + $companion)
