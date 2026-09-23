[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$GameFolder, [string]$TavernLib)
$ErrorActionPreference = 'Stop'
$managed = Join-Path $GameFolder 'A Township Tale_Data\Managed'
$melon = Join-Path $GameFolder 'MelonLoader\net472'
$compiler = Join-Path ([Environment]::GetFolderPath('Windows')) 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$destination = Join-Path $PSScriptRoot 'payload\TavernNativeMenu.dll'
if (!(Test-Path -LiteralPath (Join-Path $managed 'Root.Township.dll'))) { throw 'GameFolder must contain the compatible game Managed assemblies.' }
$references = @('mscorlib.dll','System.dll','System.Core.dll','System.Net.Http.dll','netstandard.dll',
    'Newtonsoft.Json.dll','Root.Township.dll','UnityEngine.dll','UnityEngine.CoreModule.dll',
    'UnityEngine.PhysicsModule.dll','UnityEngine.InputLegacyModule.dll','UnityEngine.IMGUIModule.dll','UnityEngine.ImageConversionModule.dll',
    'UnityEngine.TextRenderingModule.dll','UnityEngine.UI.dll','UnityEngine.InputSystem.dll',
    'Alta.Api.Client.dll','Alta.Api.DataTransferModels.dll','Alta.Core.Runtime.dll','Alta.Build.dll',
    'Alta.CommandLine.dll','Alta.Coroutines.dll','Township.Core.Runtime.dll','Alta.Platform.dll',
    'Alta.Platform.Generic.dll','Alta.Authentication.dll','Alta.Global.Runtime.dll','Alta.Singletons.dll',
    'Alta.Utilities.dll','Alta.Serialization.Runtime.dll','Alta.NetworkTransport.Runtime.dll',
    'System.IdentityModel.Tokens.Jwt.dll','Microsoft.IdentityModel.Tokens.dll')
$compile = @('/nologo','/noconfig','/target:library','/optimize+','/langversion:5','/nostdlib+',('/out:' + $destination))
foreach ($reference in $references) {
    $file = Join-Path $managed $reference
    if (Test-Path -LiteralPath $file) { $compile += '/reference:' + $file }
}
foreach ($reference in @('MelonLoader.dll','0Harmony.dll')) { $compile += '/reference:' + (Join-Path $melon $reference) }
if ([string]::IsNullOrWhiteSpace($TavernLib)) { $TavernLib = Join-Path $GameFolder 'Plugins\TavernLib.dll' }
if (!(Test-Path -LiteralPath $TavernLib -PathType Leaf)) { throw 'Supply -TavernLib with the compatible Tavern Launcher 1.8.3 or 1.8.4 Patch/TavernLib.dll file.' }
$compile += '/reference:' + [IO.Path]::GetFullPath($TavernLib)
$compile += '/resource:' + (Join-Path $PSScriptRoot 'mod-assets\tavern-badge-hires.png') + ',TavernNativeMenu.Badge.png'
$compile += @(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'mod-src') -File -Filter '*.cs' | ForEach-Object FullName)
& $compiler @compile
if ($LASTEXITCODE -ne 0) { throw 'The updated mod failed to compile.' }
Write-Output ('Built menu mod: ' + $destination)
