param([Parameter(Mandatory=$true)][string]$GameFolder)
$ErrorActionPreference='Stop'
$compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$managed=Join-Path $GameFolder 'A Township Tale_Data\Managed'
$root=Join-Path $PSScriptRoot ('social-integration-'+[Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($root)|Out-Null
try {
 $json=Join-Path $managed 'Newtonsoft.Json.dll'
 Copy-Item -LiteralPath $json -Destination (Join-Path $root 'Newtonsoft.Json.dll')
 $exe=Join-Path $root 'SocialIntegrationTests.exe'
 & $compiler /nologo /target:exe /langversion:5 "/out:$exe" /reference:System.Net.Http.dll /reference:System.Web.Extensions.dll "/reference:$json" ('/reference:'+(Join-Path $managed 'netstandard.dll')) (Join-Path $PSScriptRoot 'SocialIntegrationTests.cs') (Join-Path $PSScriptRoot '..\mod-src\TavernSocialClient.cs') (Join-Path $PSScriptRoot '..\social-server\src\Relay.cs')
 if($LASTEXITCODE -ne 0){throw 'Social integration tests failed to compile.'}
 & $exe $root
 if($LASTEXITCODE -ne 0){throw 'Social integration tests failed.'}
} finally {
 $resolved=[IO.Path]::GetFullPath($root)
 $expected=[IO.Path]::GetFullPath($PSScriptRoot).TrimEnd('\')+'\'
 if(!$resolved.StartsWith($expected,[StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($resolved) -notmatch '^social-integration-[0-9a-f]{32}$'){throw 'Unsafe test cleanup path.'}
 if(Test-Path -LiteralPath $resolved){Remove-Item -LiteralPath $resolved -Recurse -Force}
}
