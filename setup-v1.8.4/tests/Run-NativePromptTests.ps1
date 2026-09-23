[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (!(Test-Path -LiteralPath $compiler)) { throw '.NET Framework C# compiler was not found.' }
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('TavernNativePrompt-tests-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($testRoot) | Out-Null
try {
    $exe = Join-Path $testRoot 'NativePromptTests.exe'
    & $compiler /nologo /target:exe /langversion:5 /warn:4 "/out:$exe" (Join-Path $PSScriptRoot 'NativePromptTests.cs') (Join-Path $PSScriptRoot '..\mod-src\NativePrompt.cs') (Join-Path $PSScriptRoot '..\mod-src\MenuArtworkRules.cs')
    if ($LASTEXITCODE -ne 0) { throw 'NativePrompt fixture compilation failed.' }
    & $exe
    if ($LASTEXITCODE -ne 0) { throw 'NativePrompt regression tests failed.' }
} finally {
    $resolved = [IO.Path]::GetFullPath($testRoot)
    $tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
    if (!$resolved.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($resolved) -notlike 'TavernNativePrompt-tests-*') { throw 'Refusing cleanup outside the test fixture directory.' }
    if (Test-Path -LiteralPath $resolved) { Remove-Item -LiteralPath $resolved -Recurse -Force }
}
