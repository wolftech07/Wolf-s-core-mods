[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$GameFolder,
    [Parameter(Mandatory=$true)][string]$AlternateRoot
)
$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$cecil = Join-Path $GameFolder 'MelonLoader\net472\Mono.Cecil.dll'
$original = Join-Path $GameFolder 'A Township Tale_Data\Managed\Root.Township.dll'
foreach ($required in @($compiler, $cecil, $original, $AlternateRoot)) {
    if (!(Test-Path -LiteralPath $required -PathType Leaf)) { throw "Required file was not found: $required" }
}
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('TavernWheel-tests-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($testRoot) | Out-Null
try {
    $exe = Join-Path $testRoot 'WheelInteractionTests.exe'
    Copy-Item -LiteralPath $cecil -Destination (Join-Path $testRoot 'Mono.Cecil.dll')
    & $compiler /nologo /target:exe /langversion:5 /warn:4 "/out:$exe" "/reference:$cecil" (Join-Path $PSScriptRoot 'WheelInteractionTests.cs') (Join-Path $PSScriptRoot '..\mod-src\WheelInteraction.cs')
    if ($LASTEXITCODE -ne 0) { throw 'WheelInteraction fixture compilation failed.' }
    & $exe $original $AlternateRoot
    if ($LASTEXITCODE -ne 0) { throw 'WheelInteraction regression tests failed.' }
} finally {
    $resolved = [IO.Path]::GetFullPath($testRoot)
    $tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
    if (!$resolved.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($resolved) -notlike 'TavernWheel-tests-*') { throw 'Refusing cleanup outside the test fixture directory.' }
    if (Test-Path -LiteralPath $resolved) { Remove-Item -LiteralPath $resolved -Recurse -Force }
}
