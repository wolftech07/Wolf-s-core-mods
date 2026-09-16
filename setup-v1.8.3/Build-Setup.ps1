[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$setupRoot = $PSScriptRoot
$compiler = Join-Path ([Environment]::GetFolderPath('Windows')) 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (!(Test-Path -LiteralPath $compiler)) { throw 'The Windows .NET Framework C# compiler was not found. Install .NET Framework 4.8 before building.' }
$payloadDir = Join-Path $setupRoot 'payload'
$payloadMod = Join-Path $payloadDir 'TavernNativeMenu.dll'
if (!(Test-Path -LiteralPath $payloadMod)) { throw 'Missing payload/TavernNativeMenu.dll. Run Build-Mod.ps1 for the compatible game folder first.' }
$outDir = Join-Path $setupRoot 'dist'
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
$destination = Join-Path $outDir 'TavernNativeMenuSetup.exe'
$arguments = @('/nologo','/target:winexe','/platform:anycpu','/langversion:5','/optimize+',('/out:' + $destination),
    '/reference:System.dll','/reference:System.Core.dll','/reference:System.Drawing.dll','/reference:System.Windows.Forms.dll','/reference:System.Web.Extensions.dll')
foreach ($file in @(Get-ChildItem -LiteralPath $payloadDir -File -Recurse | Where-Object { $_.Extension -ne '.pyc' -and $_.FullName -notmatch '[\\/]__pycache__[\\/]' } | Sort-Object FullName)) {
    $relative = $file.FullName.Substring($payloadDir.Length + 1).Replace('\','/')
    $arguments += '/resource:' + $file.FullName + ',payload/' + $relative
}
$arguments += (Join-Path $setupRoot 'src\Setup.cs')
& $compiler @arguments
if ($LASTEXITCODE -ne 0) { throw "Setup compilation failed ($LASTEXITCODE)." }
Write-Output ('Built: ' + $destination)
Get-FileHash -LiteralPath $destination -Algorithm SHA256 | Format-List
