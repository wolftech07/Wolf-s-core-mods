[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$output = Join-Path $PSScriptRoot 'dist'
$exe = Join-Path $output 'TavernHubSetup.exe'
if (!(Test-Path -LiteralPath $exe)) { throw 'Build-Setup.ps1 must complete before packaging.' }
$readme = Join-Path $output 'README.md'
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'README.md') -Destination $readme -Force
$meshGuide = Join-Path $output 'MESH-FRIENDS.md'
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'MESH-FRIENDS.md') -Destination $meshGuide -Force
$tabletGuide = Join-Path $output 'SOCIAL-TABLET.md'
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'SOCIAL-TABLET.md') -Destination $tabletGuide -Force
foreach ($legacyName in @('TavernNativeSocialHost.exe','SOCIAL-HOSTING.md','TavernNativeMenuSetup-for-1.8.3.zip','TavernNativeMenuSetup.exe','TavernNativeMenuSetup.zip')) {
    $legacyFile = Join-Path $output $legacyName
    if (Test-Path -LiteralPath $legacyFile -PathType Leaf) { Remove-Item -LiteralPath $legacyFile -Force }
}
$advancedGuide = Join-Path $output 'ADVANCED.md'
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'ADVANCED.md') -Destination $advancedGuide -Force
$sourceGuide = Join-Path $output 'SOURCE-AND-BUILD.md'
$securityGuide = Join-Path $output 'SECURITY.md'
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'SOURCE-AND-BUILD.md') -Destination $sourceGuide -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'SECURITY.md') -Destination $securityGuide -Force
$sourceArchive = Join-Path $output 'TavernHubSource.zip'
if (!(Test-Path -LiteralPath $sourceArchive -PathType Leaf)) { throw 'Run Package-Source.ps1 before packaging the installer release.' }
$componentManifest = Join-Path $output 'COMPONENTS.json'
$payloadRoot = Join-Path $PSScriptRoot 'payload'
$components = @([ordered]@{Path='TavernHubSetup.exe';SHA256=(Get-FileHash -LiteralPath $exe).Hash.ToLowerInvariant();Source='setup-v1.8.4/src/Setup.cs';SignatureStatus=[string](Get-AuthenticodeSignature -LiteralPath $exe).Status})
foreach ($file in @(Get-ChildItem -LiteralPath $payloadRoot -Recurse -File | Where-Object { $_.Extension -ne '.pyc' -and $_.FullName -notmatch '[\\/]__pycache__[\\/]' } | Sort-Object FullName)) {
    $relative = $file.FullName.Substring($payloadRoot.Length+1).Replace('\','/')
    $source = 'setup-v1.8.4/payload/' + $relative
    if ($relative -eq 'TavernNativeMenu.dll') { $source = 'setup-v1.8.4/mod-src/' }
    elseif ($relative -eq 'server/TavernNativeSocialServer.dll') { $source = 'setup-v1.8.4/mesh-server/src/' }
    elseif ($relative -eq 'native/TavernMeshPeer.exe') { $source = 'setup-v1.8.4/mesh-peer/src/' }
    elseif ($relative -eq 'native/libtoxcore.dll') { $source = 'setup-v1.8.4/mesh-native/downloads/ and ports/' }
    elseif ($relative -eq 'native/Newtonsoft.Json.dll') { $source = 'MIT dependency; https://github.com/JamesNK/Newtonsoft.Json' }
    elseif ($relative.StartsWith('native/')) { $source = 'setup-v1.8.4/mesh-native/ and mesh-peer/ dependency inputs' }
    $components += [ordered]@{EmbeddedPath=('payload/'+$relative);SHA256=(Get-FileHash -LiteralPath $file.FullName).Hash.ToLowerInvariant();Source=$source}
}
$inventory = [ordered]@{Format=1;Product='Tavern In-Game Hub';Version=[Diagnostics.FileVersionInfo]::GetVersionInfo($exe).FileVersion;SourceArchive='TavernHubSource.zip';Components=$components}
[IO.File]::WriteAllText($componentManifest,($inventory | ConvertTo-Json -Depth 8),(New-Object Text.UTF8Encoding($false)))
$hashes = Join-Path $output 'SHA256SUMS.txt'
$files = @($exe, $readme, $meshGuide, $tabletGuide, $advancedGuide, $sourceGuide, $securityGuide, $sourceArchive, $componentManifest)
$lines = foreach ($file in $files) { (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant() + '  ' + [IO.Path]::GetFileName($file) }
[IO.File]::WriteAllLines($hashes, [string[]]$lines, (New-Object Text.UTF8Encoding($false)))
$archive = Join-Path $output 'TavernHubSetup.zip'
Compress-Archive -LiteralPath ($files + @($hashes)) -DestinationPath $archive -CompressionLevel Optimal -Force
Get-Item -LiteralPath $exe, $archive | Select-Object Name, Length
