param([Parameter(Mandatory=$true)][string]$GameFolder, [Parameter(Mandatory=$true)][string]$AlternateRoot)
$ErrorActionPreference = 'Stop'
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('TavernVisibility-tests-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($testRoot) | Out-Null
try {
    $cecil = Join-Path $GameFolder 'MelonLoader\net472\Mono.Cecil.dll'
    Copy-Item -LiteralPath $cecil -Destination $testRoot
    $exe = Join-Path $testRoot 'JoinVisibilityTests.exe'
    $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
    & $compiler /nologo /target:exe /langversion:5 "/out:$exe" "/reference:$cecil" (Join-Path $PSScriptRoot 'JoinVisibilityTests.cs') (Join-Path $PSScriptRoot '..\mod-src\JoinVisibility.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Join visibility test compilation failed.' }
    & $exe (Join-Path $GameFolder 'A Township Tale_Data\Managed\Root.Township.dll') $AlternateRoot
    if ($LASTEXITCODE -ne 0) { throw 'Join visibility tests failed.' }
} finally {
    $resolved = [IO.Path]::GetFullPath($testRoot)
    $temp = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
    if (!$resolved.StartsWith($temp, [StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($resolved) -notlike 'TavernVisibility-tests-*') { throw 'Unsafe fixture cleanup path.' }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
