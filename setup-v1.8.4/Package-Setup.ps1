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
$hashes = Join-Path $output 'SHA256SUMS.txt'
$files = @($exe, $readme, $meshGuide, $tabletGuide, $advancedGuide)
$lines = foreach ($file in $files) { (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant() + '  ' + [IO.Path]::GetFileName($file) }
[IO.File]::WriteAllLines($hashes, [string[]]$lines, (New-Object Text.UTF8Encoding($false)))
$archive = Join-Path $output 'TavernHubSetup.zip'
Compress-Archive -LiteralPath ($files + @($hashes)) -DestinationPath $archive -CompressionLevel Optimal -Force
Get-Item -LiteralPath $exe, $archive | Select-Object Name, Length
