param([Parameter(Mandatory=$true)][string]$GameFolder)
$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$managed = Join-Path $GameFolder 'A Township Tale_Data\Managed'
$root = Join-Path $PSScriptRoot ('mesh-integration-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $root | Out-Null
try {
 $json = Join-Path $managed 'Newtonsoft.Json.dll'
 Copy-Item -LiteralPath $json -Destination $root
 $exe = Join-Path $root 'MeshIntegrationTests.exe'
 & $compiler /nologo /target:exe /platform:x64 /langversion:5 "/out:$exe" "/reference:$json" ('/reference:'+(Join-Path $managed 'netstandard.dll')) (Join-Path $PSScriptRoot 'MeshIntegrationTests.cs') (Join-Path $PSScriptRoot '..\mod-src\MeshSocialClient.cs') (Join-Path $PSScriptRoot '..\mesh-peer\src\MeshNative.cs')
 if ($LASTEXITCODE -ne 0) { throw 'Mesh integration test compilation failed.' }
 & $exe $root ([IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\mesh-native\dist'))) ([IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\mesh-peer\dist')))
 if ($LASTEXITCODE -ne 0) { throw 'Mesh integration tests failed.' }
} finally {
 $resolved = [IO.Path]::GetFullPath($root)
 $parent = [IO.Path]::GetFullPath($PSScriptRoot).TrimEnd('\')+'\'
 if (!$resolved.StartsWith($parent,[StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($resolved) -notmatch '^mesh-integration-[0-9a-f]{32}$') { throw 'Unsafe fixture cleanup path.' }
 if (Test-Path -LiteralPath $resolved) { Remove-Item -LiteralPath $resolved -Recurse -Force }
}
