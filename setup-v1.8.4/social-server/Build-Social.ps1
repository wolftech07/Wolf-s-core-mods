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
$arguments += (Join-Path $PSScriptRoot 'src\Companion.cs')
$arguments += (Join-Path $PSScriptRoot 'src\CardConsent.cs')
& $compiler @arguments
if ($LASTEXITCODE -ne 0) { throw 'Server companion compilation failed.' }
$hostExe = Join-Path $output 'TavernNativeSocialHost.exe'
& $compiler /nologo /target:winexe /langversion:5 /optimize+ ('/out:' + $hostExe) /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll /reference:System.Web.Extensions.dll ('/resource:' + $companion + ',TavernNativeSocialServer.dll') (Join-Path $PSScriptRoot 'src\Relay.cs') (Join-Path $PSScriptRoot 'src\Host.cs')
if ($LASTEXITCODE -ne 0) { throw 'Social host compilation failed.' }
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'SOCIAL-HOSTING.md') -Destination (Join-Path $output 'SOCIAL-HOSTING.md') -Force
Write-Output ('Built ' + $hostExe)
