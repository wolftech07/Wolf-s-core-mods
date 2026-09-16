[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$output = Join-Path $PSScriptRoot 'dist'
$exe = Join-Path $output 'TavernNativeMenuSetup.exe'
if (!(Test-Path -LiteralPath $exe)) { throw 'Build-Setup.ps1 must complete before packaging.' }
$readme = Join-Path $output 'README.md'
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'README.md') -Destination $readme -Force
$socialHost = Join-Path $output 'TavernNativeSocialHost.exe'
$socialGuide = Join-Path $output 'SOCIAL-HOSTING.md'
$builtSocial = Join-Path $PSScriptRoot 'social-server\dist\TavernNativeSocialHost.exe'
if (!(Test-Path -LiteralPath $builtSocial)) { throw 'Build social-server/Build-Social.ps1 before packaging.' }
Copy-Item -LiteralPath $builtSocial -Destination $socialHost -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'social-server\SOCIAL-HOSTING.md') -Destination $socialGuide -Force
$hashes = Join-Path $output 'SHA256SUMS.txt'
$files = @($exe, $readme, $socialHost, $socialGuide)
$lines = foreach ($file in $files) { (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant() + '  ' + [IO.Path]::GetFileName($file) }
[IO.File]::WriteAllLines($hashes, [string[]]$lines, (New-Object Text.UTF8Encoding($false)))
$archive = Join-Path $output 'TavernNativeMenuSetup-for-1.8.3.zip'
Compress-Archive -LiteralPath ($files + @($hashes)) -DestinationPath $archive -CompressionLevel Optimal -Force
Get-Item -LiteralPath $exe, $archive | Select-Object Name, Length
