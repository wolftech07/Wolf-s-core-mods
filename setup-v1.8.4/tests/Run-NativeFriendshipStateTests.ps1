param([Parameter(Mandatory=$true)][string]$GameFolder)
$ErrorActionPreference = 'Stop'
$testRoot = Join-Path $PSScriptRoot 'artifacts\native-friendship'
[IO.Directory]::CreateDirectory($testRoot) | Out-Null
$managed = Join-Path $GameFolder 'A Township Tale_Data\Managed'
$melon = Join-Path $GameFolder 'MelonLoader\net472'
$json = Join-Path $managed 'Newtonsoft.Json.dll'
$harmony = Join-Path $melon '0Harmony.dll'
Copy-Item -LiteralPath $json -Destination $testRoot -Force
# Harmony's runtime detours use the dependencies shipped alongside MelonLoader.
foreach ($file in Get-ChildItem -LiteralPath $melon -File -Filter '*.dll') {
    if ($file.Name -ne 'Newtonsoft.Json.dll') { Copy-Item -LiteralPath $file.FullName -Destination $testRoot -Force }
}
$exe = Join-Path $testRoot 'NativeFriendshipStateTests.exe'
$redirects = foreach ($file in Get-ChildItem -LiteralPath $testRoot -File -Filter '*.dll') {
    $name = [Reflection.AssemblyName]::GetAssemblyName($file.FullName)
    $token = -join ($name.GetPublicKeyToken() | ForEach-Object { $_.ToString('x2') })
    if ($token) {
        '<dependentAssembly><assemblyIdentity name="' + $name.Name + '" publicKeyToken="' + $token + '" culture="neutral"/><bindingRedirect oldVersion="0.0.0.0-65535.65535.65535.65535" newVersion="' + $name.Version + '"/></dependentAssembly>'
    }
}
# MelonLoader resolves the bundled dependency versions in game. This standalone
# .NET Framework fixture needs equivalent redirects for Harmony's older refs.
[IO.File]::WriteAllText(($exe + '.config'), ('<?xml version="1.0"?><configuration><runtime><assemblyBinding xmlns="urn:schemas-microsoft-com:asm.v1">' + ($redirects -join '') + '</assemblyBinding></runtime></configuration>'))
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
& $compiler /nologo /target:exe /langversion:5 /optimize+ "/out:$exe" "/reference:$json" "/reference:$harmony" ('/reference:' + (Join-Path $managed 'netstandard.dll')) (Join-Path $PSScriptRoot 'NativeFriendshipStateTests.cs') (Join-Path $PSScriptRoot '..\mod-src\NativeFriendshipState.cs')
if ($LASTEXITCODE -ne 0) { throw 'Native friendship state checks did not compile.' }
& $exe
if ($LASTEXITCODE -ne 0) { throw 'Native friendship state checks failed.' }
