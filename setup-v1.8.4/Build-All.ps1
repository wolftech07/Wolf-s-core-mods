[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$GameFolder,
    [string]$TavernLib,
    [string]$VisualStudioPath,
    [switch]$RefreshBootstrapSeeds,
    [string]$NativeBundle
)
$ErrorActionPreference = 'Stop'

# The source release does not contain the game, launcher, or prebuilt mod files.
# NativeBundle is an explicit development shortcut; the default rebuilds native code.
$GameFolder = [IO.Path]::GetFullPath($GameFolder)
$managed = Join-Path $GameFolder 'A Township Tale_Data\Managed'
foreach ($relative in @('Root.Township.dll','Newtonsoft.Json.dll','netstandard.dll')) {
    if (!(Test-Path -LiteralPath (Join-Path $managed $relative) -PathType Leaf)) {
        throw "The compatible game reference assembly is missing: $relative. Supply -GameFolder with your patched game folder."
    }
}
foreach ($relative in @('MelonLoader.dll','0Harmony.dll')) {
    if (!(Test-Path -LiteralPath (Join-Path $GameFolder ('MelonLoader\net472\'+$relative)) -PathType Leaf)) {
        throw "The MelonLoader reference is missing: $relative. Install the compatible Mono MelonLoader first."
    }
}
if ([String]::IsNullOrWhiteSpace($TavernLib)) { $TavernLib = Join-Path $GameFolder 'Plugins\TavernLib.dll' }
$TavernLib = [IO.Path]::GetFullPath($TavernLib)
if (!(Test-Path -LiteralPath $TavernLib -PathType Leaf)) { throw 'Supply -TavernLib with the supported launcher Patch\TavernLib.dll.' }
$compiler = Join-Path ([Environment]::GetFolderPath('Windows')) 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (!(Test-Path -LiteralPath $compiler -PathType Leaf)) { throw 'Install .NET Framework 4.8 before building.' }

$native = Join-Path $PSScriptRoot 'mesh-native'
$nativeDist = Join-Path $native 'dist'
New-Item -ItemType Directory -Force -Path $nativeDist,(Join-Path $PSScriptRoot 'payload') | Out-Null
if ($NativeBundle) {
    $NativeBundle = [IO.Path]::GetFullPath($NativeBundle)
    foreach ($name in @('libtoxcore.dll','bootstrap-nodes.json','native-source.zip','TOXCORE-LICENSE.txt','LIBSODIUM-LICENSE.txt','PTHREADS-LICENSE.txt','THIRD-PARTY-NOTICES.txt','seed-provenance.json')) {
        $file = Join-Path $NativeBundle $name
        if (!(Test-Path -LiteralPath $file -PathType Leaf)) { throw "The optional prebuilt native bundle is incomplete: $name." }
        if (![String]::Equals($NativeBundle.TrimEnd('\'),$nativeDist.TrimEnd('\'),[StringComparison]::OrdinalIgnoreCase)) {
            Copy-Item -LiteralPath $file -Destination (Join-Path $nativeDist $name) -Force
        }
    }
    Write-Output 'Using the explicitly supplied native bundle. This run does not rebuild the native dependency.'
} else {
    Write-Output 'Building the pinned native peer library and packaging its corresponding source.'
    & (Join-Path $native 'Build-Native.ps1') -VisualStudioPath $VisualStudioPath
    if ($RefreshBootstrapSeeds) {
        & (Join-Path $native 'Refresh-Seeds.ps1')
    } else {
        $seedFile = Join-Path $native 'bootstrap-nodes.json'
        if (!(Test-Path -LiteralPath $seedFile -PathType Leaf)) {
            $seedFile = Join-Path $nativeDist 'bootstrap-nodes.json'
        }
        if (!(Test-Path -LiteralPath $seedFile -PathType Leaf)) {
            throw 'Missing the bundled bootstrap snapshot. Use -RefreshBootstrapSeeds to retrieve a new public snapshot.'
        }
        if (![String]::Equals([IO.Path]::GetFullPath($seedFile),[IO.Path]::GetFullPath((Join-Path $nativeDist 'bootstrap-nodes.json')),[StringComparison]::OrdinalIgnoreCase)) {
            Copy-Item -LiteralPath $seedFile -Destination (Join-Path $nativeDist 'bootstrap-nodes.json') -Force
        }
    }
    & (Join-Path $native 'Package-Native.ps1')
}

& (Join-Path $PSScriptRoot 'mesh-peer\Build-Peer.ps1') -GameFolder $GameFolder
& (Join-Path $PSScriptRoot 'mesh-server\Build-MeshServer.ps1') -GamePath $GameFolder
& (Join-Path $PSScriptRoot 'Build-Mod.ps1') -GameFolder $GameFolder -TavernLib $TavernLib
& (Join-Path $PSScriptRoot 'Build-Setup.ps1')
& (Join-Path $PSScriptRoot 'Package-Source.ps1')
& (Join-Path $PSScriptRoot 'Package-Setup.ps1')
Write-Output 'Build complete. Installer and readable source archives are in dist.'
