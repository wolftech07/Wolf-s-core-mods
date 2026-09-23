$ErrorActionPreference = 'Stop'
$dist = Join-Path $PSScriptRoot 'dist'
$source = Join-Path $PSScriptRoot 'source-package'
New-Item -ItemType Directory -Force -Path $source,$dist | Out-Null
$archives = @('c-toxcore-v0.2.23.tar.gz','jedisct1-libsodium-1.0.22-RELEASE.tar.gz','pthreads4w-code-v3.0.0.zip')
$hashes = foreach ($archive in $archives) {
    $file = Join-Path $PSScriptRoot ('downloads\'+$archive)
    Copy-Item -LiteralPath $file -Destination $source -Force
    (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant()+'  '+$archive
}
$ports = @{
    'libsodium-port'='74ee7dfd5fc96bb0b291f6ebe61d3bef57f80512'
    'pthreads-port'='2e0a6df2800d3677b941dc6504f083965b7886d9'
}
foreach ($name in $ports.Keys) {
    $target = Join-Path $source $name
    New-Item -ItemType Directory -Force -Path $target | Out-Null
    Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot ('registry-cache\git-trees\'+$ports[$name])) -Force | Copy-Item -Destination $target -Recurse -Force
}
foreach ($name in @('Build-Native.ps1','Package-Native.ps1','Refresh-Seeds.ps1','Native-Smoke.py','vcpkg.json','README.md','seed-provenance.json')) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination $source -Force
}
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'src\LICENSE') -Destination (Join-Path $dist 'TOXCORE-LICENSE.txt') -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'installed\x64-windows-static\share\libsodium\copyright') -Destination (Join-Path $dist 'LIBSODIUM-LICENSE.txt') -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'installed\x64-windows-static\share\pthreads\copyright') -Destination (Join-Path $dist 'PTHREADS-LICENSE.txt') -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'THIRD-PARTY-NOTICES.txt') -Destination $dist -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'seed-provenance.json') -Destination $dist -Force
[IO.File]::WriteAllLines((Join-Path $source 'SOURCE-SHA256SUMS.txt'),[string[]]$hashes)
Compress-Archive -LiteralPath (Get-ChildItem -LiteralPath $source -Force | ForEach-Object FullName) -DestinationPath (Join-Path $dist 'native-source.zip') -Force
Get-FileHash -LiteralPath (Join-Path $dist 'libtoxcore.dll'),(Join-Path $dist 'native-source.zip') -Algorithm SHA256
