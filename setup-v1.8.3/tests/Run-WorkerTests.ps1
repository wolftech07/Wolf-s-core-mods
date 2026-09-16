[CmdletBinding()]
param()
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot '..\payload\Setup-Worker.ps1')
$script:checks=0
$script:FixtureAppData=$null
$script:ActualCompatibility=${function:Confirm-Compatibility}
function Get-SetupAppDataRoot { return $script:FixtureAppData }
function Confirm-Compatibility($Context) {
    foreach ($relative in @('MelonLoader\net472\MelonLoader.dll','Plugins\TavernLib.dll','version.dll','A Township Tale_Data\Managed\Root.Township.dll')) {
        if (!(Test-Path -LiteralPath (Join-Contained $Context.GameRoot $relative) -PathType Leaf)) { throw 'Fixture dependency is missing.' }
    }
    Write-Step CHECK 'Fixture-only dependency check passed.'
}
function Assert-Test([bool]$Condition,[string]$Message) {
    if (!$Condition) { throw "FAILED: $Message" }
    $script:checks++
    Write-Output "PASS $Message"
}
function Assert-Fails([scriptblock]$Action,[string]$Message) {
    $failed=$false
    try { & $Action | Out-Null } catch { $failed=$true }
    Assert-Test $failed $Message
}
function Put-Text([string]$Path,[string]$Text) {
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($Path)) | Out-Null
    [IO.File]::WriteAllText($Path,$Text,(New-Object Text.UTF8Encoding($false)))
}
function New-Fixture([string]$Name) {
    $base=Join-Path $script:testRoot $Name
    $game=Join-Path $base "Game & [test] O'Brien"
    $launcher=Join-Path $base ('Launcher folder ' + [char]0x00E9)
    $payload=Join-Path $base 'Payload'
    $script:FixtureAppData=Join-Path $base 'AppData'
    $files=@('A Township Tale.exe','MelonLoader\net472\MelonLoader.dll','Plugins\TavernLib.dll','version.dll','A Township Tale_Data\Managed\Root.Township.dll')
    foreach($file in $files) { Put-Text (Join-Path $game $file) 'fixture dependency' }
    $launcherExe=Join-Path $launcher 'TavernLauncher - Client.exe'
    Put-Text $launcherExe 'fixture launcher'
    Put-Text (Join-Path $payload 'TavernNativeMenu.dll') 'new mod'
    Put-Text (Join-Path $payload 'addons\tavern_native_menu\client.py') 'new addon'
    Put-Text (Join-Path $payload 'integration-plan.json') '{"Entries":[{"Source":"TavernNativeMenu.dll","Scope":"game","Target":"Mods/TavernNativeMenu.dll"},{"Source":"addons/tavern_native_menu/client.py","Scope":"launcher","Target":"addons/tavern_native_menu/client.py"}]}'
    return Get-Context (Join-Path $game 'A Township Tale.exe') $launcherExe $payload
}
function Run-Fixture($Context,[string]$Action) {
    Invoke-SetupOperation $Action $Context.GameExe $Context.LauncherExe $Context.PayloadRoot | Out-Null
}
$script:testRoot=Join-Path ([IO.Path]::GetTempPath()) ('TavernNativeMenu-worker-tests-' + [Guid]::NewGuid().ToString('N'))
try {
    $c=New-Fixture 'normal'
    $mod=Join-Path $c.GameRoot 'Mods\TavernNativeMenu.dll'
    $addon=Join-Path $c.LauncherRoot 'addons\tavern_native_menu\client.py'
    $enabled=Join-Path $c.AppDataRoot 'TheModdingTavern\client_enabled_addons.json'
    Put-Text $mod 'previous mod'
    Put-Text $enabled '["existing_addon"]'
    $originalEnabled=Get-Digest $enabled
    Run-Fixture $c Validate
    Assert-Test (!(Test-Path -LiteralPath $c.StateRoot)) 'Validate makes no files'
    Assert-Fails { & $script:ActualCompatibility $c } 'Production compatibility guard rejects fake launcher'
    Run-Fixture $c Install
    Assert-Test ((Get-Digest $mod) -eq (Get-Digest (Join-Path $c.PayloadRoot 'TavernNativeMenu.dll'))) 'Install copies and verifies the mod'
    Assert-Test ((Get-Digest $addon) -eq (Get-Digest (Join-Path $c.PayloadRoot 'addons\tavern_native_menu\client.py'))) 'Install copies addon'
    $names=(Read-AddonEnablement $c).Names
    Assert-Test ($names.Count -eq 2 -and $names -contains 'existing_addon' -and $names -contains 'tavern_native_menu') 'Enablement retains other addons'
    $bytes=[IO.File]::ReadAllBytes($enabled)
    Assert-Test (!($bytes.Length -ge 3 -and $bytes[0] -eq 239 -and $bytes[1] -eq 187 -and $bytes[2] -eq 191)) 'Addon settings UTF8 has no BOM'
    $manifestHash=Get-Digest $c.Manifest
    Run-Fixture $c Install
    Assert-Test ((Get-Digest $c.Manifest) -eq $manifestHash) 'Second Install is idempotent'
    Run-Fixture $c Uninstall
    Assert-Test ([IO.File]::ReadAllText($mod) -eq 'previous mod') 'Undo restores previous mod bytes'
    Assert-Test (!(Test-Path -LiteralPath $addon)) 'Undo removes only installed addon file'
    Assert-Test ((Get-Digest $enabled) -eq $originalEnabled) 'Undo restores untouched original enablement bytes'
    Assert-Test (!(Test-Path -LiteralPath $c.Manifest)) 'Undo removes active install record'
    Run-Fixture $c Uninstall
    Assert-Test ([IO.File]::ReadAllText($mod) -eq 'previous mod') 'Second Undo is harmless'
    Run-Fixture $c Install
    Put-Text $enabled '["existing_addon","tavern_native_menu","added_later"]'
    Run-Fixture $c Uninstall
    $names=(Read-AddonEnablement $c).Names
    Assert-Test ($names.Count -eq 2 -and $names -contains 'existing_addon' -and $names -contains 'added_later') 'Undo preserves addon changes made after setup'

    $c=New-Fixture 'empty'
    $enabled=Join-Path $c.AppDataRoot 'TheModdingTavern\client_enabled_addons.json'
    Run-Fixture $c Install
    Assert-Test ((Read-AddonEnablement $c).Names.Count -eq 1) 'Missing addon settings created as JSON array'
    Put-Text $enabled '[]'
    Run-Fixture $c Install
    Assert-Test ((Read-AddonEnablement $c).Names -contains 'tavern_native_menu') 'Reinstall can re-enable addon without replacing backups'
    Run-Fixture $c Uninstall
    Assert-Test (!(Test-Path -LiteralPath $enabled)) 'Undo removes addon settings that setup alone created'
    Assert-Test (!(Test-Path -LiteralPath (Join-Path $c.GameRoot 'Mods\TavernNativeMenu.dll'))) 'Undo restores original mod absence'

    $c=New-Fixture 'already-enabled'
    $enabled=Join-Path $c.AppDataRoot 'TheModdingTavern\client_enabled_addons.json'
    Put-Text $enabled '["tavern_native_menu","other"]'
    $original=Get-Digest $enabled
    Run-Fixture $c Install
    Run-Fixture $c Uninstall
    Assert-Test ((Get-Digest $enabled) -eq $original) 'Undo retains preexisting addon enablement'

    $c=New-Fixture 'upgrade'
    $mod=Join-Path $c.GameRoot 'Mods\TavernNativeMenu.dll'
    $addon=Join-Path $c.LauncherRoot 'addons\tavern_native_menu\client.py'
    $enabled=Join-Path $c.AppDataRoot 'TheModdingTavern\client_enabled_addons.json'
    Put-Text $mod 'original mod before any setup'
    Put-Text $addon 'original addon before any setup'
    Put-Text $enabled '["existing_addon"]'
    $originalEnabled=Get-Digest $enabled
    Run-Fixture $c Install
    $firstRecord=Read-InstallRecord $c
    $firstManifestHash=Get-Digest $c.Manifest
    Put-Text (Join-Path $c.PayloadRoot 'TavernNativeMenu.dll') 'updated mod release'
    Put-Text (Join-Path $c.PayloadRoot 'addons\tavern_native_menu\client.py') 'updated addon release'
    # A release may reorder its plan and use different path separators without changing targets.
    Put-Text (Join-Path $c.PayloadRoot 'integration-plan.json') '{"Entries":[{"Source":"addons/tavern_native_menu/client.py","Scope":"launcher","Target":"addons\\tavern_native_menu\\client.py"},{"Source":"TavernNativeMenu.dll","Scope":"game","Target":"Mods\\TavernNativeMenu.dll"}]}'
    Run-Fixture $c Validate
    Assert-Test ((Get-Digest $c.Manifest) -eq $firstManifestHash -and [IO.File]::ReadAllText($mod) -eq 'new mod') 'Validate recognizes an upgrade without changing files or the record'
    Run-Fixture $c Install
    Assert-Test ([IO.File]::ReadAllText($mod) -eq 'updated mod release' -and [IO.File]::ReadAllText($addon) -eq 'updated addon release') 'Install upgrades mod and addon from a previous release'
    $updatedRecord=Read-InstallRecord $c
    foreach ($entry in @($updatedRecord.Files)) {
        $prior=@($firstRecord.Files | Where-Object { $_.Scope -eq $entry.Scope })[0]
        Assert-Test ($entry.Backup -eq $prior.Backup -and $entry.OriginalHash -eq $prior.OriginalHash) ('Upgrade retains original backup and hash for ' + $entry.Scope)
        Assert-Test ($entry.InstalledHash -eq (Get-Digest (Resolve-Target $c $entry.Scope $entry.Target))) ('Upgrade records verified installed hash for ' + $entry.Scope)
    }
    Assert-Test ($updatedRecord.Enablement.OriginalBackup -eq $firstRecord.Enablement.OriginalBackup -and $updatedRecord.Enablement.OriginalHash -eq $firstRecord.Enablement.OriginalHash) 'Upgrade retains original addon enablement baseline'
    $updatedManifestHash=Get-Digest $c.Manifest
    Run-Fixture $c Install
    Assert-Test ((Get-Digest $c.Manifest) -eq $updatedManifestHash) 'Installing the updated release again is idempotent'
    Run-Fixture $c Uninstall
    Assert-Test ([IO.File]::ReadAllText($mod) -eq 'original mod before any setup') 'Undo after upgrade restores original mod instead of the previous release'
    Assert-Test ([IO.File]::ReadAllText($addon) -eq 'original addon before any setup') 'Undo after upgrade restores original addon instead of the previous release'
    Assert-Test ((Get-Digest $enabled) -eq $originalEnabled) 'Undo after upgrade restores original addon enablement bytes'

    $c=New-Fixture 'upgrade-target-set'
    Run-Fixture $c Install
    $manifestHash=Get-Digest $c.Manifest
    $mod=Join-Path $c.GameRoot 'Mods\TavernNativeMenu.dll'
    Put-Text (Join-Path $c.PayloadRoot 'TavernNativeMenu.dll') 'new release that must not be copied'
    Put-Text (Join-Path $c.PayloadRoot 'integration-plan.json') '{"Entries":[{"Source":"TavernNativeMenu.dll","Scope":"game","Target":"Mods/TavernNativeMenu.dll"},{"Source":"addons/tavern_native_menu/client.py","Scope":"launcher","Target":"addons/tavern_native_menu/other.py"}]}'
    Assert-Fails { Run-Fixture $c Validate } 'Validate rejects an unsupported change to the managed target set'
    Assert-Fails { Run-Fixture $c Install } 'Upgrade rejects a different target despite matching file counts'
    Assert-Test ((Get-Digest $c.Manifest) -eq $manifestHash -and [IO.File]::ReadAllText($mod) -eq 'new mod') 'Rejected upgrade retains previous mod and installation record'
    Assert-Test (!(Test-Path -LiteralPath (Join-Path $c.LauncherRoot 'addons\tavern_native_menu\other.py'))) 'Rejected target-set change does not write a new addon file'

    $c=New-Fixture 'changed-installed'
    Run-Fixture $c Install
    $mod=Join-Path $c.GameRoot 'Mods\TavernNativeMenu.dll'
    Put-Text $mod 'user modification'
    Assert-Fails { Run-Fixture $c Uninstall } 'Undo refuses changed installed files'
    Assert-Test ([IO.File]::ReadAllText($mod) -eq 'user modification') 'Changed installed file retained'
    Assert-Fails { Run-Fixture $c Install } 'Reinstall refuses changed installed files'
    Put-Text (Join-Path $c.PayloadRoot 'TavernNativeMenu.dll') 'updated mod release'
    Assert-Fails { Run-Fixture $c Install } 'Upgrade refuses independently modified installed files'
    Assert-Test ([IO.File]::ReadAllText($mod) -eq 'user modification') 'Rejected upgrade retains independent changes'

    $c=New-Fixture 'changed-backup'
    $mod=Join-Path $c.GameRoot 'Mods\TavernNativeMenu.dll'
    Put-Text $mod 'original mod'
    Run-Fixture $c Install
    $record=Read-InstallRecord $c
    Put-Text (Join-Contained $c.StateRoot $record.Files[0].Backup) 'damaged backup'
    Assert-Fails { Run-Fixture $c Uninstall } 'Undo refuses damaged original backup'
    Assert-Test ([IO.File]::ReadAllText($mod) -eq 'new mod') 'Damaged backup does not overwrite installed mod'

    $c=New-Fixture 'locked-target'
    $mod=Join-Path $c.GameRoot 'Mods\TavernNativeMenu.dll'
    Put-Text $mod 'locked original'
    $lock=New-Object IO.FileStream($mod,[IO.FileMode]::Open,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None)
    try { Assert-Fails { Run-Fixture $c Install } 'Locked target fails safely before installation' }
    finally { $lock.Dispose() }
    Assert-Test ([IO.File]::ReadAllText($mod) -eq 'locked original') 'Locked target remains intact'

    $c=New-Fixture 'bad-settings'
    Put-Text (Join-Path $c.AppDataRoot 'TheModdingTavern\client_enabled_addons.json') '{"not":"an array"}'
    Assert-Fails { Run-Fixture $c Install } 'Invalid addon settings rejected'
    Assert-Test (!(Test-Path -LiteralPath (Join-Path $c.GameRoot 'Mods\TavernNativeMenu.dll'))) 'Invalid settings fail before copying mod'

    $c=New-Fixture 'bad-plan'
    Put-Text (Join-Path $c.PayloadRoot 'integration-plan.json') '{"Entries":[{"Source":"TavernNativeMenu.dll","Scope":"game","Target":"Mods/TavernNativeMenu.dll"},{"Source":"addons/tavern_native_menu/client.py","Scope":"launcher","Target":"addons/tavern_native_menu/../../../outside.py"}]}'
    Assert-Fails { Run-Fixture $c Install } 'Traversal in integration plan rejected'
    Assert-Test (!(Test-Path -LiteralPath $c.StateRoot)) 'Bad plan fails before creating backup state'
    Assert-Fails { Join-Contained $c.GameRoot 'C:\outside.dll' } 'Absolute relative-path injection rejected'
    Assert-Fails { Resolve-Target $c 'launcher' 'TavernLauncher - Client.exe' } 'Launcher EXE mutation rejected by target policy'

    $c=New-Fixture 'rollback'
    $mod=Join-Path $c.GameRoot 'Mods\TavernNativeMenu.dll'
    Put-Text $mod 'previous mod to recover'
    $script:RealAtomic=${function:Write-AtomicBytes}
    $script:Injected=$false
    function Write-AtomicBytes([string]$Destination,[byte[]]$Bytes) {
        if (!$script:Injected -and $Destination.EndsWith('client_enabled_addons.json',[StringComparison]::OrdinalIgnoreCase)) {
            $script:Injected=$true
            throw 'Injected configuration commit failure.'
        }
        & $script:RealAtomic $Destination $Bytes
    }
    try { Assert-Fails { Run-Fixture $c Install } 'Injected late commit failure surfaces as failure' }
    finally { ${function:Write-AtomicBytes}=$script:RealAtomic }
    Assert-Test $script:Injected 'Failure occurred after mod and addon file commits'
    Assert-Test ([IO.File]::ReadAllText($mod) -eq 'previous mod to recover') 'Rollback restores prior mod bytes'
    Assert-Test (!(Test-Path -LiteralPath (Join-Path $c.LauncherRoot 'addons\tavern_native_menu\client.py'))) 'Rollback restores addon absence'
    Assert-Test (!(Test-Path -LiteralPath $c.Manifest) -and !(Test-Path -LiteralPath $c.Journal)) 'Rollback clears incomplete active records'
    Run-Fixture $c Install
    Run-Fixture $c Uninstall
    Assert-Test ([IO.File]::ReadAllText($mod) -eq 'previous mod to recover') 'Retry after rollback succeeds and preserves original baseline'

    $c=New-Fixture 'upgrade-rollback'
    $mod=Join-Path $c.GameRoot 'Mods\TavernNativeMenu.dll'
    $addon=Join-Path $c.LauncherRoot 'addons\tavern_native_menu\client.py'
    $enabled=Join-Path $c.AppDataRoot 'TheModdingTavern\client_enabled_addons.json'
    Put-Text $mod 'original before upgrade rollback'
    Put-Text $addon 'original addon before upgrade rollback'
    Put-Text $enabled '["existing_addon"]'
    Run-Fixture $c Install
    $priorManifestHash=Get-Digest $c.Manifest
    Put-Text (Join-Path $c.PayloadRoot 'TavernNativeMenu.dll') 'updated mod to roll back'
    Put-Text (Join-Path $c.PayloadRoot 'addons\tavern_native_menu\client.py') 'updated addon to roll back'
    Put-Text $enabled '["existing_addon","added_later"]'
    $priorEnabledHash=Get-Digest $enabled
    $script:RealAtomic=${function:Write-AtomicBytes}
    $script:Injected=$false
    $script:FailureManifest=$c.Manifest
    function Write-AtomicBytes([string]$Destination,[byte[]]$Bytes) {
        & $script:RealAtomic $Destination $Bytes
        if (!$script:Injected -and $Destination -eq $script:FailureManifest) {
            $script:Injected=$true
            throw 'Injected failure after committing the updated installation record.'
        }
    }
    try { Assert-Fails { Run-Fixture $c Install } 'Injected failure after updated manifest commit surfaces as failure' }
    finally { ${function:Write-AtomicBytes}=$script:RealAtomic }
    Assert-Test $script:Injected 'Upgrade failure occurred after all file and manifest commits'
    Assert-Test ([IO.File]::ReadAllText($mod) -eq 'new mod' -and [IO.File]::ReadAllText($addon) -eq 'new addon') 'Failed upgrade restores the previous installed release'
    Assert-Test ((Get-Digest $enabled) -eq $priorEnabledHash) 'Failed upgrade restores previous addon settings, including user changes'
    Assert-Test ((Get-Digest $c.Manifest) -eq $priorManifestHash) 'Failed upgrade restores the previous installation record byte for byte'
    Assert-Test (!(Test-Path -LiteralPath $c.Journal)) 'Failed upgrade clears journal after verified rollback'
    Run-Fixture $c Install
    Assert-Test ([IO.File]::ReadAllText($mod) -eq 'updated mod to roll back') 'Upgrade can be retried after rollback'
    Assert-Test ((Read-AddonEnablement $c).Names -contains 'tavern_native_menu') 'Upgrade re-enables the addon when necessary'
    Run-Fixture $c Uninstall
    Assert-Test ([IO.File]::ReadAllText($mod) -eq 'original before upgrade rollback' -and [IO.File]::ReadAllText($addon) -eq 'original addon before upgrade rollback') 'Undo after a retried upgrade retains the original file baseline'
    $names=(Read-AddonEnablement $c).Names
    Assert-Test ($names.Count -eq 2 -and $names -contains 'existing_addon' -and $names -contains 'added_later') 'Undo after upgrade preserves later addon settings'

    $c=New-Fixture 'interrupted-recovery'
    $mod=Join-Path $c.GameRoot 'Mods\TavernNativeMenu.dll'
    Put-Text $mod 'original before interrupted transaction'
    $script:RealRecovery=${function:Undo-PendingTransaction}
    $script:RealAtomic=${function:Write-AtomicBytes}
    $script:Injected=$false
    function Write-AtomicBytes([string]$Destination,[byte[]]$Bytes) {
        if (!$script:Injected -and $Destination.EndsWith('client_enabled_addons.json',[StringComparison]::OrdinalIgnoreCase)) { $script:Injected=$true; throw 'Injected interruption.' }
        & $script:RealAtomic $Destination $Bytes
    }
    function Undo-PendingTransaction($Context) {
        if (Test-Path -LiteralPath $Context.Journal) { throw 'Injected process ending before recovery.' }
    }
    try { Assert-Fails { Run-Fixture $c Install } 'Interrupted operation retains a recovery journal' }
    finally { ${function:Write-AtomicBytes}=$script:RealAtomic; ${function:Undo-PendingTransaction}=$script:RealRecovery }
    Assert-Test ((Test-Path -LiteralPath $c.Journal) -and [IO.File]::ReadAllText($mod) -eq 'new mod') 'Incomplete transaction retains recoverable state'
    Run-Fixture $c Uninstall
    Assert-Test ([IO.File]::ReadAllText($mod) -eq 'original before interrupted transaction') 'Next Undo automatically recovers interrupted file changes'
    Assert-Test (!(Test-Path -LiteralPath $c.Journal) -and !(Test-Path -LiteralPath (Join-Path $c.LauncherRoot 'addons\tavern_native_menu\client.py'))) 'Recovery verifies prior absence and clears journal'

    $c=New-Fixture 'missing-payload'
    Remove-Item -LiteralPath (Join-Path $c.PayloadRoot 'TavernNativeMenu.dll')
    Assert-Fails { Run-Fixture $c Validate } 'Missing packaged mod rejected'
    $c=New-Fixture 'missing-dependency'
    Remove-Item -LiteralPath (Join-Path $c.GameRoot 'Plugins\TavernLib.dll')
    Assert-Fails { Run-Fixture $c Install } 'Missing game dependency rejected before install'

    Write-Output ("Worker tests: {0} checks passed. Only isolated temporary fixtures were changed." -f $script:checks)
} finally {
    $resolved=[IO.Path]::GetFullPath($script:testRoot)
    $prefix=[IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\')+'\'
    if ($resolved.StartsWith($prefix,[StringComparison]::OrdinalIgnoreCase) -and [IO.Path]::GetFileName($resolved).StartsWith('TavernNativeMenu-worker-tests-')) {
        if (Test-Path -LiteralPath $resolved) { Remove-Item -LiteralPath $resolved -Recurse -Force }
    }
}
