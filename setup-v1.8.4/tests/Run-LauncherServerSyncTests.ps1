param([Parameter(Mandatory=$true)][string]$GameFolder)
$ErrorActionPreference = 'Stop'
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('TavernServerSync-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($testRoot) | Out-Null
$managed = Join-Path $GameFolder 'A Township Tale_Data\Managed'
$json = Join-Path $managed 'Newtonsoft.Json.dll'
Copy-Item -LiteralPath $json -Destination $testRoot
$exe = Join-Path $testRoot 'Checks.exe'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$sources = @('LauncherProfile.cs','DirectoryParser.cs','ServerCatalog.cs') | ForEach-Object { Join-Path $PSScriptRoot ('..\mod-src\' + $_) }
& $compiler /nologo /target:exe /langversion:5 "/out:$exe" "/reference:$json" ('/reference:' + (Join-Path $managed 'netstandard.dll')) (Join-Path $PSScriptRoot 'LauncherServerSyncTests.cs') @sources
if ($LASTEXITCODE -ne 0) { throw 'PC-to-VR server sync tests did not compile.' }
& $exe $testRoot
if ($LASTEXITCODE -ne 0) { throw 'PC-to-VR server sync tests failed.' }
