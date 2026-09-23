[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$GameFolder)
$ErrorActionPreference='Stop'
$compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$json=Join-Path $GameFolder 'A Township Tale_Data\Managed\Newtonsoft.Json.dll'
$facade=Join-Path $GameFolder 'A Township Tale_Data\Managed\netstandard.dll'
$root=Join-Path ([IO.Path]::GetTempPath()) ('TavernSocial-tests-'+[Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($root)|Out-Null
try {
    Copy-Item -LiteralPath $json -Destination (Join-Path $root 'Newtonsoft.Json.dll')
    $exe=Join-Path $root 'SocialClientTests.exe'
    & $compiler /nologo /target:exe /langversion:5 "/out:$exe" /reference:System.Net.Http.dll "/reference:$json" "/reference:$facade" (Join-Path $PSScriptRoot 'SocialClientTests.cs') (Join-Path $PSScriptRoot '..\mod-src\TavernSocialClient.cs')
    if($LASTEXITCODE -ne 0){throw 'Social client tests failed to compile.'}
    & $exe $root
    if($LASTEXITCODE -ne 0){throw 'Social client regression tests failed.'}
} finally {
    $resolved=[IO.Path]::GetFullPath($root)
    $temp=[IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\')+'\'
    if(!$resolved.StartsWith($temp,[StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($resolved) -notlike 'TavernSocial-tests-*'){throw 'Unsafe test cleanup path.'}
    if(Test-Path -LiteralPath $resolved){Remove-Item -LiteralPath $resolved -Recurse -Force}
}
