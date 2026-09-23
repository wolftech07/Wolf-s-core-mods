param([Parameter(Mandatory=$true)][string]$GameFolder)
$ErrorActionPreference='Stop'
$compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$managed=Join-Path $GameFolder 'A Township Tale_Data\Managed'
$fixture=Join-Path ([IO.Path]::GetTempPath()) ('Tavern-peer-proof-tests-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $fixture | Out-Null
try {
    $json=Join-Path $managed 'Newtonsoft.Json.dll'
    Copy-Item -LiteralPath $json -Destination $fixture
    $exe=Join-Path $fixture 'MeshPeerProofTests.exe'
    & $compiler /nologo /target:exe /platform:x64 /langversion:5 "/out:$exe" "/reference:$json" ('/reference:'+(Join-Path $managed 'netstandard.dll')) (Join-Path $PSScriptRoot 'MeshPeerProofTests.cs') (Join-Path $PSScriptRoot '..\mesh-peer\src\MeshNative.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Peer proof integration tests did not compile.' }
    & $exe $fixture ([IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\mesh-native\dist'))) ([IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\mesh-peer\dist')))
    if ($LASTEXITCODE -ne 0) { throw 'Peer proof integration tests failed.' }
} finally {
    $resolved=[IO.Path]::GetFullPath($fixture)
    $prefix=[IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\')+'\'
    if (!$resolved.StartsWith($prefix,[StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($resolved) -notmatch '^Tavern-peer-proof-tests-[0-9a-f]{32}$') { throw 'Unsafe peer-test cleanup path.' }
    if (Test-Path -LiteralPath $resolved) { Remove-Item -LiteralPath $resolved -Recurse -Force }
}
