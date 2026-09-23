[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$setupRoot = $PSScriptRoot
$compiler = Join-Path ([Environment]::GetFolderPath('Windows')) 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (!(Test-Path -LiteralPath $compiler)) { throw 'The Windows .NET Framework C# compiler was not found. Install .NET Framework 4.8 before building.' }
$payloadDir = Join-Path $setupRoot 'payload'
$payloadMod = Join-Path $payloadDir 'TavernNativeMenu.dll'
if (!(Test-Path -LiteralPath $payloadMod)) { throw 'Missing payload/TavernNativeMenu.dll. Run Build-Mod.ps1 for the compatible game folder first.' }
$nativeFiles = @('libtoxcore.dll','bootstrap-nodes.json','native-source.zip','TOXCORE-LICENSE.txt','LIBSODIUM-LICENSE.txt','PTHREADS-LICENSE.txt','THIRD-PARTY-NOTICES.txt','seed-provenance.json')
$peerFiles = @('TavernMeshPeer.exe','Newtonsoft.Json.dll','mesh-peer-source.zip','MESH-PEER-LICENSE.txt','NEWTONSOFT-LICENSE.txt')
New-Item -ItemType Directory -Force -Path (Join-Path $payloadDir 'native'),(Join-Path $payloadDir 'server') | Out-Null
foreach ($bundle in @(@{Root='mesh-native';Files=$nativeFiles},@{Root='mesh-peer';Files=$peerFiles})) {
    foreach ($name in $bundle.Files) {
        $source = Join-Path $setupRoot ($bundle.Root+'\dist\'+$name)
        if (!(Test-Path -LiteralPath $source -PathType Leaf)) { throw "Build and package $($bundle.Root) first: missing $name." }
        Copy-Item -LiteralPath $source -Destination (Join-Path $payloadDir ('native\'+$name)) -Force
    }
}
$companion = Join-Path $setupRoot 'mesh-server\dist\TavernNativeSocialServer.dll'
if (!(Test-Path -LiteralPath $companion)) { throw 'Build mesh-server/Build-MeshServer.ps1 first.' }
Copy-Item -LiteralPath $companion -Destination (Join-Path $payloadDir 'server\TavernNativeSocialServer.dll') -Force
$outDir = Join-Path $setupRoot 'dist'
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
$destination = Join-Path $outDir 'TavernHubSetup.exe'
$arguments = @('/nologo','/target:winexe','/platform:anycpu','/langversion:5','/optimize+',('/out:' + $destination),
    '/reference:System.dll','/reference:System.Core.dll','/reference:System.Drawing.dll','/reference:System.Windows.Forms.dll','/reference:System.Web.Extensions.dll')
foreach ($file in @(Get-ChildItem -LiteralPath $payloadDir -File -Recurse | Where-Object { $_.Extension -ne '.pyc' -and $_.FullName -notmatch '[\\/]__pycache__[\\/]' } | Sort-Object FullName)) {
    $relative = $file.FullName.Substring($payloadDir.Length + 1).Replace('\','/')
    $arguments += '/resource:' + $file.FullName + ',payload/' + $relative
}
$arguments += (Join-Path $setupRoot 'src\Setup.cs')
& $compiler @arguments
if ($LASTEXITCODE -ne 0) { throw "Setup compilation failed ($LASTEXITCODE)." }
Write-Output ('Built: ' + $destination)
Get-FileHash -LiteralPath $destination -Algorithm SHA256 | Format-List
