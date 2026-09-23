param([Parameter(Mandatory=$true)][string]$GameFolder)
$ErrorActionPreference = 'Stop'
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('TavernDescriptions-tests-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($testRoot) | Out-Null
try {
    $json = Join-Path $GameFolder 'A Township Tale_Data\Managed\Newtonsoft.Json.dll'
    $facade = Join-Path $GameFolder 'A Township Tale_Data\Managed\netstandard.dll'
    Copy-Item -LiteralPath $json -Destination $testRoot
    $exe = Join-Path $testRoot 'Descriptions.exe'
    $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
    $sources = @('LauncherProfile.cs','DirectoryParser.cs','ServerCatalog.cs') | ForEach-Object { Join-Path $PSScriptRoot ('..\mod-src\' + $_) }
    & $compiler /nologo /target:exe /langversion:5 "/out:$exe" "/reference:$json" "/reference:$facade" (Join-Path $PSScriptRoot 'DirectoryDescriptionTests.cs') @sources
    if ($LASTEXITCODE -ne 0) { throw 'Description test compilation failed.' }
    & $exe
    if ($LASTEXITCODE -ne 0) { throw 'Description tests failed.' }
} finally {
    $resolved = [IO.Path]::GetFullPath($testRoot)
    $temp = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
    if (!$resolved.StartsWith($temp, [StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($resolved) -notlike 'TavernDescriptions-tests-*') { throw 'Unsafe fixture cleanup path.' }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
