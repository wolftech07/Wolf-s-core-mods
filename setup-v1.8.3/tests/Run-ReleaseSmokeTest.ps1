[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$GameFolder,
    [Parameter(Mandatory=$true)][string]$LauncherExe,
    [Parameter(Mandatory=$true)][string]$NewTavernLib
)
$ErrorActionPreference = 'Stop'
$smokeLauncherSource = $LauncherExe
. (Join-Path $PSScriptRoot '..\payload\Setup-Worker.ps1')
function Get-SetupAppDataRoot { return $script:smokeAppData }
function Assert-Smoke([bool]$Condition, [string]$Label) {
    if (!$Condition) { throw "FAIL: $Label" }
    Write-Output "PASS: $Label"
}
$fixtureRoot = Join-Path $PSScriptRoot ('release-smoke-' + [Guid]::NewGuid().ToString('N'))
$script:smokeAppData = Join-Path $fixtureRoot 'AppData'
$gameCopy = Join-Path $fixtureRoot 'Game [test] & with spaces'
$launcherCopy = Join-Path $fixtureRoot 'Launcher'
$payload = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\payload'))
try {
    [IO.Directory]::CreateDirectory($gameCopy) | Out-Null
    [IO.Directory]::CreateDirectory($launcherCopy) | Out-Null
    [IO.Directory]::CreateDirectory($script:smokeAppData) | Out-Null
    foreach ($relative in @('A Township Tale.exe','version.dll','MelonLoader\net472\MelonLoader.dll','A Township Tale_Data\Managed\Root.Township.dll')) {
        $destination = Join-Path $gameCopy $relative
        [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($destination)) | Out-Null
        [IO.File]::Copy((Join-Path $GameFolder $relative), $destination)
    }
    [IO.Directory]::CreateDirectory((Join-Path $gameCopy 'Plugins')) | Out-Null
    [IO.File]::Copy($NewTavernLib, (Join-Path $gameCopy 'Plugins\TavernLib.dll'))
    $copiedLauncher = Join-Path $launcherCopy 'TavernLauncher - Client.exe'
    [IO.File]::Copy($smokeLauncherSource, $copiedLauncher)
    $initialLauncher = Get-Digest $copiedLauncher
    $copiedGame = Join-Path $gameCopy 'A Township Tale.exe'
    Invoke-SetupOperation Validate $copiedGame $copiedLauncher $payload
    Invoke-SetupOperation Install $copiedGame $copiedLauncher $payload
    $context = Get-Context $copiedGame $copiedLauncher $payload
    Assert-Smoke ((Get-Digest $copiedLauncher) -eq $initialLauncher) 'Launcher EXE bytes unchanged'
    Assert-Smoke ((Get-Digest (Join-Path $gameCopy 'Mods\TavernNativeMenu.dll')) -eq (Get-Digest (Join-Path $payload 'TavernNativeMenu.dll'))) 'Embedded mod installed exactly'
    Assert-Smoke ((Read-AddonEnablement $context).Names -contains 'tavern_native_menu') 'Addon enabled using actual JSON format'
    Assert-Smoke ((Get-Digest (Join-Path $launcherCopy 'addons\tavern_native_menu\client.py')) -eq (Get-Digest (Join-Path $payload 'addons\tavern_native_menu\client.py'))) 'Release Play Game addon installed exactly'
    Invoke-SetupOperation Install $copiedGame $copiedLauncher $payload
    Invoke-SetupOperation Validate $copiedGame $copiedLauncher $payload
    Invoke-SetupOperation Uninstall $copiedGame $copiedLauncher $payload
    Assert-Smoke (!(Test-Path -LiteralPath (Join-Path $gameCopy 'Mods\TavernNativeMenu.dll'))) 'Undo removes newly installed mod'
    Assert-Smoke (!(Test-Path -LiteralPath (Join-Path $launcherCopy 'addons\tavern_native_menu\client.py'))) 'Undo removes newly installed addon'
    Assert-Smoke (!(Test-Path -LiteralPath (Join-Path $script:smokeAppData 'TheModdingTavern\client_enabled_addons.json'))) 'Undo restores original settings absence'
    Assert-Smoke ((Get-Digest $copiedLauncher) -eq $initialLauncher) 'Launcher EXE still matches after Undo'
    Write-Output 'PASS: Release install/reinstall/validate/undo using copies of real v1.8.3 dependencies.'
} finally {
    $resolved = [IO.Path]::GetFullPath($fixtureRoot)
    $testFolder = [IO.Path]::GetFullPath($PSScriptRoot).TrimEnd('\') + '\'
    if (!$resolved.StartsWith($testFolder, [StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($resolved) -notmatch '^release-smoke-[0-9a-f]{32}$') { throw 'Unsafe fixture cleanup path.' }
    if (Test-Path -LiteralPath $resolved) { Remove-Item -LiteralPath $resolved -Recurse -Force }
}
