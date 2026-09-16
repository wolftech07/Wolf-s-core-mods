[CmdletBinding()]
param(
    [ValidateSet('Validate','Install','Uninstall')][string]$Operation,
    [string]$GameExe,
    [string]$LauncherExe,
    [string]$PayloadRoot = $PSScriptRoot
)
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = New-Object Text.UTF8Encoding($false)

function Write-Step([string]$Kind, [string]$Message) {
    Write-Output ('[{0}] {1}' -f $Kind, $Message)
}
function Get-Digest([string]$Path) {
    if (Test-Path -LiteralPath $Path -PathType Leaf) { return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash }
    return $null
}
function Get-SetupAppDataRoot { return [Environment]::GetFolderPath('ApplicationData') }
function Assert-NoReparse([string]$Path) {
    $cursor = [IO.Path]::GetFullPath($Path)
    while ($cursor) {
        if (Test-Path -LiteralPath $cursor) {
            if ((Get-Item -LiteralPath $cursor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) {
                throw "A selected path contains a link/junction. Choose its direct folder instead: $cursor"
            }
        }
        $parent = [IO.Path]::GetDirectoryName($cursor)
        if ($parent -eq $cursor) { break }
        $cursor = $parent
    }
}
function Join-Contained([string]$Root, [string]$Relative) {
    if ([string]::IsNullOrWhiteSpace($Relative) -or [IO.Path]::IsPathRooted($Relative) -or $Relative.Contains(':') -or
        @($Relative -split '[\\/]' | Where-Object { $_ -eq '..' -or $_ -eq '.' -or $_ -eq '' }).Count) {
        throw 'An installer file record contains an invalid relative path.'
    }
    $prefix = [IO.Path]::GetFullPath($Root).TrimEnd('\','/') + [IO.Path]::DirectorySeparatorChar
    $resolved = [IO.Path]::GetFullPath((Join-Path $prefix $Relative))
    if (!$resolved.StartsWith($prefix,[StringComparison]::OrdinalIgnoreCase)) { throw 'An installer file record leaves its selected folder.' }
    Assert-NoReparse $resolved
    return $resolved
}
function Get-Context([string]$Game, [string]$Launcher, [string]$Payload) {
    if ([string]::IsNullOrWhiteSpace($Game) -or [string]::IsNullOrWhiteSpace($Launcher)) { throw 'Select the game executable and Tavern Launcher client executable.' }
    $gameFile = [IO.Path]::GetFullPath($Game)
    if (Test-Path -LiteralPath $gameFile -PathType Container) { $gameFile = Join-Path $gameFile 'A Township Tale.exe' }
    $launcherFile = [IO.Path]::GetFullPath($Launcher)
    if ([IO.Path]::GetFileName($gameFile) -ne 'A Township Tale.exe' -or !(Test-Path -LiteralPath $gameFile -PathType Leaf)) { throw 'Choose the A Township Tale.exe file in the game installation.' }
    if (!(Test-Path -LiteralPath $launcherFile -PathType Leaf) -or [IO.Path]::GetExtension($launcherFile) -ne '.exe') { throw 'Choose the Tavern Launcher client .exe file.' }
    Assert-NoReparse $gameFile
    Assert-NoReparse $launcherFile
    $gameDir = [IO.Path]::GetDirectoryName($gameFile)
    $launcherDir = [IO.Path]::GetDirectoryName($launcherFile)
    $state = Join-Contained $gameDir 'TavernNativeMenuSetup-1.8.3'
    return [pscustomobject]@{ GameExe=$gameFile; LauncherExe=$launcherFile; GameRoot=$gameDir; LauncherRoot=$launcherDir;
        PayloadRoot=[IO.Path]::GetFullPath($Payload); StateRoot=$state; Manifest=(Join-Path $state 'installed.json'); Journal=(Join-Path $state 'pending.json');
        AppDataRoot=[IO.Path]::GetFullPath((Get-SetupAppDataRoot)) }
}
function Assert-ApplicationsClosed($Context) {
    foreach ($process in @(Get-Process -ErrorAction SilentlyContinue)) {
        try { $processPath = $process.Path } catch { continue }
        if ($processPath -and ($processPath -eq $Context.GameExe -or $processPath -eq $Context.LauncherExe)) {
            throw ('Close {0} before installing or undoing setup, then try again.' -f [IO.Path]::GetFileName($processPath))
        }
    }
}
function Confirm-Compatibility($Context) {
    Write-Step CHECK 'Checking the game, MelonLoader, TavernLib, and Tavern Launcher 1.8.3.'
    $launcherHash = Get-Digest $Context.LauncherExe
    if ($launcherHash -ne '19DA46DFF42DF3328993A1F1A8BE45EA53210A886699FD709A4F8417A5EB65A0') {
        throw 'This setup supports the original Tavern Launcher Client 1.8.3 executable. Select that release; another version or a modified EXE was found.'
    }
    foreach ($relative in @('MelonLoader\net472\MelonLoader.dll','Plugins\TavernLib.dll','version.dll')) {
        if (!(Test-Path -LiteralPath (Join-Contained $Context.GameRoot $relative) -PathType Leaf)) {
            throw "The patched game is missing $relative. Use Tavern Launcher to install its game patch and MelonLoader first, close it, and retry."
        }
    }
    $rootHash = Get-Digest (Join-Contained $Context.GameRoot 'A Township Tale_Data\Managed\Root.Township.dll')
    if (@('06E1FC38F1B1A592D30DCCE34FE26B608D5C56761E7A23B8E165C32C8063D735','A4BD5661EF8176C644C3F0F66E32EC90C05F66C0C5A9575668A86196577DB643') -notcontains $rootHash) {
        throw 'This game assembly is unsupported. Apply the compatible Tavern Launcher client patch before using this setup.'
    }
    $libHash = Get-Digest (Join-Contained $Context.GameRoot 'Plugins\TavernLib.dll')
    if (@('770B2EE118709B6472CA99869FED668D1D850DB55916FB634FB9EEF0A5DA0498','3C02046C9647821EA549F35A113D4F8F48A0130252B7EFC8D83DAE7A98BD6074','B1260921CCAE6F48CE688850402DE19BCF6952CA75C1C073476AE03533586790') -notcontains $libHash) {
        throw 'This TavernLib build has not been checked. Apply the supplied Tavern Launcher 1.8.3 client patch and retry.'
    }
    if ($libHash -ne '770B2EE118709B6472CA99869FED668D1D850DB55916FB634FB9EEF0A5DA0498') {
        Write-Step WARN 'A previously supported TavernLib build is installed. Use Tavern Launcher 1.8.3 to update the client patch when ready.'
    }
    Write-Step OK 'Supported launcher and game dependencies found.'
}
function Resolve-Target($Context, [string]$Scope, [string]$Relative) {
    $normal = $Relative.Replace('/','\')
    if ($Scope -eq 'game') {
        if ($normal -ne 'Mods\TavernNativeMenu.dll' -and !$normal.StartsWith('TavernNativeMenu\',[StringComparison]::OrdinalIgnoreCase)) { throw 'The setup plan contains an unapproved game target.' }
        return Join-Contained $Context.GameRoot $normal
    }
    if ($Scope -eq 'launcher') {
        if (!$normal.StartsWith('addons\tavern_native_menu\',[StringComparison]::OrdinalIgnoreCase) -and @('Play-TavernMenu.ps1','Play-TavernMenu.cmd') -notcontains $normal) { throw 'The setup plan contains an unapproved launcher target.' }
        return Join-Contained $Context.LauncherRoot $normal
    }
    if ($Scope -eq 'enablement' -and $normal -eq 'TheModdingTavern\client_enabled_addons.json') { return Join-Contained $Context.AppDataRoot $normal }
    throw 'Unknown setup target scope.'
}
function Get-InstallPlan($Context) {
    $planFile = Join-Contained $Context.PayloadRoot 'integration-plan.json'
    if (!(Test-Path -LiteralPath $planFile -PathType Leaf)) { throw 'The setup package is incomplete: integration-plan.json is missing.' }
    $document = [IO.File]::ReadAllText($planFile) | ConvertFrom-Json
    $records = if ($document.PSObject.Properties['Entries']) { @($document.Entries) } else { @($document) }
    if ($records.Count -lt 2 -or @($records | Where-Object { $_.Scope -eq 'game' -and $_.Target.Replace('/','\') -eq 'Mods\TavernNativeMenu.dll' }).Count -ne 1) { throw 'The setup plan is incomplete.' }
    $seen = @{}
    foreach ($record in $records) {
        if (@('game','launcher') -notcontains [string]$record.Scope) { throw 'The setup plan contains an invalid scope.' }
        $target = Resolve-Target $Context ([string]$record.Scope) ([string]$record.Target)
        if ($seen.ContainsKey($target)) { throw 'The setup plan contains duplicate target files.' }
        $seen[$target] = $true
        $source = Join-Contained $Context.PayloadRoot ([string]$record.Source)
        $hash = Get-Digest $source
        if (!$hash) { throw "The setup package is incomplete: $($record.Source) is missing." }
        if (Test-Path -LiteralPath $target -PathType Container) { throw "A folder blocks an installer file: $target" }
        [pscustomobject]@{Source=$source;Scope=[string]$record.Scope;Target=[string]$record.Target;Path=$target;InstalledHash=$hash}
    }
}
function Write-AtomicBytes([string]$Destination, [byte[]]$Bytes) {
    Assert-NoReparse $Destination
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($Destination)) | Out-Null
    $temporary = $Destination + '.setup-' + [Guid]::NewGuid().ToString('N') + '.tmp'
    try {
        [IO.File]::WriteAllBytes($temporary,$Bytes)
        if (Test-Path -LiteralPath $Destination -PathType Leaf) { [IO.File]::Replace($temporary,$Destination,[NullString]::Value) }
        else { [IO.File]::Move($temporary,$Destination) }
    } finally { if (Test-Path -LiteralPath $temporary -PathType Leaf) { Remove-Item -LiteralPath $temporary -Force } }
}
function Write-AtomicJson([string]$Destination, $Value) {
    Write-AtomicBytes $Destination ([Text.Encoding]::UTF8.GetBytes(($Value | ConvertTo-Json -Depth 12)))
}
function Read-AddonEnablement($Context) {
    $path = Resolve-Target $Context 'enablement' 'TheModdingTavern\client_enabled_addons.json'
    if (!(Test-Path -LiteralPath $path -PathType Leaf)) { return [pscustomobject]@{Path=$path;Names=@();Hash=$null} }
    if ((Get-Item -LiteralPath $path).Length -gt 1048576) { throw 'The launcher addon settings file is unusually large. Check it before retrying setup.' }
    $raw = [IO.File]::ReadAllText($path)
    if (!$raw.TrimStart().StartsWith('[')) { throw 'The launcher enabled-addon settings are not a JSON list. Setup has left them unchanged.' }
    try { $parsed = $raw | ConvertFrom-Json; $names = @($parsed) } catch { throw 'The launcher enabled-addon settings are invalid JSON. Setup has left them unchanged.' }
    foreach ($name in $names) { if ($name -isnot [string] -or [string]::IsNullOrWhiteSpace($name)) { throw 'The launcher enabled-addon settings contain an invalid name.' } }
    return [pscustomobject]@{Path=$path;Names=$names;Hash=(Get-Digest $path)}
}
function New-AddonJsonSource($Context, [array]$Names) {
    $source = Join-Contained $Context.StateRoot ('staged\' + [Guid]::NewGuid().ToString('N') + '.json')
    Write-AtomicBytes $source ([Text.Encoding]::UTF8.GetBytes((ConvertTo-Json -InputObject @($Names))))
    return $source
}
function Get-EnableAddonChange($Context, $Current) {
    if (@($Current.Names) -ccontains 'tavern_native_menu') { return $null }
    $source = New-AddonJsonSource $Context (@($Current.Names) + @('tavern_native_menu'))
    return [pscustomobject]@{Scope='enablement';Target='TheModdingTavern\client_enabled_addons.json';Source=$source;Hash=(Get-Digest $source);ExpectedBefore=$Current.Hash}
}
function Read-InstallRecord($Context) {
    if (!(Test-Path -LiteralPath $Context.Manifest -PathType Leaf)) { return $null }
    $record = [IO.File]::ReadAllText($Context.Manifest) | ConvertFrom-Json
    if ($record.Format -ne 1 -or $record.GameExe -ne $Context.GameExe -or $record.LauncherExe -ne $Context.LauncherExe) {
        throw 'The existing installation record belongs to another game/launcher selection or has an unsupported format.'
    }
    $seen = @{}
    foreach ($entry in @($record.Files)) {
        $target = Resolve-Target $Context $entry.Scope $entry.Target
        if ($seen.ContainsKey($target) -or $entry.InstalledHash -notmatch '^[A-Fa-f0-9]{64}$') { throw 'The installation record contains invalid file data.' }
        $seen[$target] = $true
        if ($entry.Existed) {
            $backup = Join-Contained $Context.StateRoot $entry.Backup
            if ($entry.OriginalHash -notmatch '^[A-Fa-f0-9]{64}$' -or (Get-Digest $backup) -ne $entry.OriginalHash) { throw 'A previous-file backup is missing or changed. No installed files have been touched.' }
        }
    }
    if (@($record.Files).Count -lt 2) { throw 'The installation record is incomplete.' }
    if (!$record.PSObject.Properties['Enablement']) { throw 'The addon enablement record is missing.' }
    if ($record.Enablement.OriginalHash) {
        $enableBackup = Join-Contained $Context.StateRoot $record.Enablement.OriginalBackup
        if ((Get-Digest $enableBackup) -ne $record.Enablement.OriginalHash) { throw 'The original addon settings backup is missing or changed.' }
    }
    return $record
}
function Assert-InstalledUnchanged($Context, $Record) {
    foreach ($entry in @($Record.Files)) {
        $path = Resolve-Target $Context $entry.Scope $entry.Target
        if ((Get-Digest $path) -ne $entry.InstalledHash) { throw "An installed file changed or is missing: $path. Restore that file or keep a copy of your changes before retrying. Setup has left it untouched." }
    }
}
function Get-ManagedFileChanges($Context, $Record, [array]$Plan) {
    if (@($Record.Files).Count -ne $Plan.Count) { throw 'This release changes the files managed by setup. Undo the installed release before installing this one. Existing files and backups were left untouched.' }
    $installed = @{}
    foreach ($entry in @($Record.Files)) {
        $installed[(Resolve-Target $Context $entry.Scope $entry.Target)] = $entry
    }
    # Match resolved targets, so path separators and plan ordering do not change ownership.
    foreach ($item in $Plan) {
        if (!$installed.ContainsKey($item.Path)) { throw 'This release changes the files managed by setup. Undo the installed release before installing this one. Existing files and backups were left untouched.' }
    }
    foreach ($item in $Plan) {
        $entry = $installed[$item.Path]
        if ($entry.InstalledHash -ne $item.InstalledHash) {
            [pscustomobject]@{Scope=$item.Scope;Target=$item.Target;Source=$item.Source;Hash=$item.InstalledHash;ExpectedBefore=$entry.InstalledHash}
        }
    }
}
function Undo-PendingTransaction($Context) {
    if (!(Test-Path -LiteralPath $Context.Journal -PathType Leaf)) { return }
    $journal = [IO.File]::ReadAllText($Context.Journal) | ConvertFrom-Json
    if ($journal.Format -ne 1 -or $journal.GameExe -ne $Context.GameExe -or $journal.LauncherExe -ne $Context.LauncherExe) { throw 'An unrecognized recovery record exists. No files were changed.' }
    $entries = @($journal.Files)
    if ($journal.ManifestBefore) {
        $prior = Join-Contained $Context.StateRoot $journal.ManifestBefore
        if ((Get-Digest $prior) -ne $journal.ManifestBeforeHash) { throw 'The previous installation record backup has changed. Keep the setup recovery folder.' }
    }
    foreach ($entry in $entries) {
        $path = Resolve-Target $Context $entry.Scope $entry.Target
        $current = Get-Digest $path
        if ($current -ne $entry.BeforeHash -and $current -ne $entry.AfterHash) { throw "Recovery found an independently changed file: $path. Keep the setup recovery folder and resolve this file before retrying." }
        if ($entry.BeforeHash) {
            $backup = Join-Contained $Context.StateRoot $entry.Backup
            if ((Get-Digest $backup) -ne $entry.BeforeHash) { throw 'A recovery backup is missing or changed. Keep the setup recovery folder.' }
        }
    }
    Write-Step ROLLBACK 'Restoring files from the incomplete operation.'
    for ($index=$entries.Count-1; $index -ge 0; $index--) {
        $entry = $entries[$index]
        $path = Resolve-Target $Context $entry.Scope $entry.Target
        if ($entry.BeforeHash) { Write-AtomicBytes $path ([IO.File]::ReadAllBytes((Join-Contained $Context.StateRoot $entry.Backup))) }
        elseif (Test-Path -LiteralPath $path -PathType Leaf) { Remove-Item -LiteralPath $path -Force }
        if ((Get-Digest $path) -ne $entry.BeforeHash) { throw 'Recovery verification failed. Keep the setup recovery folder.' }
    }
    if ($journal.ManifestBefore) {
        $prior = Join-Contained $Context.StateRoot $journal.ManifestBefore
        if ((Get-Digest $prior) -ne $journal.ManifestBeforeHash) { throw 'The previous installation record backup has changed.' }
        Write-AtomicBytes $Context.Manifest ([IO.File]::ReadAllBytes($prior))
    } elseif (Test-Path -LiteralPath $Context.Manifest -PathType Leaf) { Remove-Item -LiteralPath $Context.Manifest -Force }
    Remove-Item -LiteralPath $Context.Journal -Force
    Write-Step OK 'The incomplete operation was rolled back and verified.'
}
function Invoke-FileTransaction($Context, [array]$Changes, $NewRecord) {
    $transaction = 'transactions\' + [Guid]::NewGuid().ToString('N')
    [IO.Directory]::CreateDirectory((Join-Contained $Context.StateRoot $transaction)) | Out-Null
    $journal = [ordered]@{Format=1;GameExe=$Context.GameExe;LauncherExe=$Context.LauncherExe;ManifestBefore=$null;ManifestBeforeHash=$null;Files=@()}
    if (Test-Path -LiteralPath $Context.Manifest -PathType Leaf) {
        $journal.ManifestBefore = $transaction + '\installed.before.json'
        $journal.ManifestBeforeHash = Get-Digest $Context.Manifest
        [IO.File]::Copy($Context.Manifest,(Join-Contained $Context.StateRoot $journal.ManifestBefore))
    }
    $index=0; $stagedSources=@()
    foreach ($change in $Changes) {
        $path = Resolve-Target $Context $change.Scope $change.Target
        $before = Get-Digest $path
        $backup = $transaction + '\' + $index + '.before'
        if ($before -ne $change.ExpectedBefore) { throw 'A target changed while setup was preparing. No installation files were changed.' }
        if ($before) {
            $snapshot=Join-Contained $Context.StateRoot $backup
            [IO.File]::Copy($path,$snapshot)
            if ((Get-Digest $snapshot) -ne $before) { throw 'A transaction backup failed verification. No installation files were changed.' }
        }
        $staged=$null
        if ($change.Source) {
            $staged=Join-Contained $Context.StateRoot ($transaction + '\' + $index + '.after')
            [IO.File]::Copy($change.Source,$staged)
            if ((Get-Digest $staged) -ne $change.Hash) { throw 'A staged setup file failed verification. No installation files were changed.' }
        }
        $stagedSources += $staged
        $journal.Files += [ordered]@{Scope=$change.Scope;Target=$change.Target;BeforeHash=$before;AfterHash=$change.Hash;Backup=$backup}
        $index++
    }
    Write-AtomicJson $Context.Journal $journal
    try {
        Assert-ApplicationsClosed $Context
        for ($index=0; $index -lt $Changes.Count; $index++) {
            $change = $Changes[$index]
            $path = Resolve-Target $Context $change.Scope $change.Target
            if ((Get-Digest $path) -ne $journal.Files[$index].BeforeHash) { throw 'A target file changed while setup was preparing. The operation has been canceled.' }
            Write-Step COPY ("{0}: {1}" -f $change.Scope,$change.Target)
            if ($stagedSources[$index]) { Write-AtomicBytes $path ([IO.File]::ReadAllBytes($stagedSources[$index])) }
            elseif (Test-Path -LiteralPath $path -PathType Leaf) { Remove-Item -LiteralPath $path -Force }
            if ((Get-Digest $path) -ne $change.Hash) { throw 'A written file failed verification.' }
            Write-Step VERIFY ("Verified {0}." -f $change.Target)
        }
        if ($null -ne $NewRecord) { Write-AtomicJson $Context.Manifest $NewRecord }
        elseif (Test-Path -LiteralPath $Context.Manifest -PathType Leaf) { Remove-Item -LiteralPath $Context.Manifest -Force }
        Remove-Item -LiteralPath $Context.Journal -Force
    } catch {
        $failure = $_.Exception.Message
        Write-Step ERROR $failure
        try { Undo-PendingTransaction $Context }
        catch { throw ("{0} Recovery also needs attention: {1}" -f $failure,$_.Exception.Message) }
        throw $failure
    }
}
function Invoke-SetupCore([string]$Action, [string]$Game, [string]$Launcher, [string]$Payload) {
    $context = Get-Context $Game $Launcher $Payload
    Write-Step CHECK ('Selected game folder: ' + $context.GameRoot)
    Write-Step CHECK ('Selected launcher: ' + $context.LauncherExe)
    if ($Action -eq 'Validate') {
        if (Test-Path -LiteralPath $context.Journal) { throw 'An incomplete operation needs recovery. Close the game and launcher, then click Install or Undo to recover safely.' }
        Confirm-Compatibility $context
        $plan = @(Get-InstallPlan $context)
        $record = Read-InstallRecord $context
        if ($record) {
            Assert-InstalledUnchanged $context $record
            $updates = @(Get-ManagedFileChanges $context $record $plan)
            if ($updates.Count) { Write-Step INFO ("An update is ready for {0} installed files. Install mod will keep the original Undo backups." -f $updates.Count) }
        }
        $enablement = Read-AddonEnablement $context
        Write-Step OK ("Validation passed for {0} setup files. No files were changed." -f $plan.Count)
        return
    }
    Assert-ApplicationsClosed $context
    Undo-PendingTransaction $context
    $record = Read-InstallRecord $context
    if ($Action -eq 'Uninstall') {
        if (!$record) { Write-Step OK 'No installation record was found for these selections. Nothing was removed.'; return }
        Assert-InstalledUnchanged $context $record
        $changes = @($record.Files | ForEach-Object {
            [pscustomobject]@{Scope=$_.Scope;Target=$_.Target;Hash=$_.OriginalHash;Source=$(if ($_.Existed) { Join-Contained $context.StateRoot $_.Backup } else { $null });ExpectedBefore=$_.InstalledHash}
        })
        if (!$record.Enablement.WasEnabled) {
            $current = Read-AddonEnablement $context
            if (@($current.Names) -ccontains 'tavern_native_menu') {
                $source=$null; $after=$null
                $remaining = @($current.Names | Where-Object { $_ -cne 'tavern_native_menu' })
                $originalNames = @()
                if ($record.Enablement.OriginalHash) {
                    $originalNames = @([IO.File]::ReadAllText((Join-Contained $context.StateRoot $record.Enablement.OriginalBackup)) | ConvertFrom-Json)
                }
                $matchesOriginal = $remaining.Count -eq $originalNames.Count
                for ($i=0; $matchesOriginal -and $i -lt $remaining.Count; $i++) { $matchesOriginal = $remaining[$i] -ceq $originalNames[$i] }
                if ($matchesOriginal) {
                    if ($record.Enablement.OriginalHash) { $source=Join-Contained $context.StateRoot $record.Enablement.OriginalBackup; $after=$record.Enablement.OriginalHash }
                } else {
                    if ($remaining.Count -or $record.Enablement.OriginalHash) { $source=New-AddonJsonSource $context $remaining; $after=Get-Digest $source }
                }
                $changes += [pscustomobject]@{Scope='enablement';Target='TheModdingTavern\client_enabled_addons.json';Source=$source;Hash=$after;ExpectedBefore=$current.Hash}
            }
        }
        Invoke-FileTransaction $context $changes $null
        Write-Step OK 'Setup was undone. Previous files were restored; launcher profiles, tokens, game saves, and mod settings were retained.'
        Write-Step INFO 'Verified backups remain in the game folder under TavernNativeMenuSetup-1.8.3.'
        return
    }
    if ($Action -ne 'Install') { throw 'Choose Validate, Install, or Uninstall.' }
    Confirm-Compatibility $context
    $plan = @(Get-InstallPlan $context)
    if ($record) {
        Assert-InstalledUnchanged $context $record
        $changes = @(Get-ManagedFileChanges $context $record $plan)
        $updatedCount = $changes.Count
        $current = Read-AddonEnablement $context
        $enableChange = Get-EnableAddonChange $context $current
        # Preserve each original-file backup and all original enablement metadata.
        # Only the installed hashes move forward when the transaction succeeds.
        foreach ($change in $changes) {
            $path = Resolve-Target $context $change.Scope $change.Target
            foreach ($entry in @($record.Files)) {
                if ((Resolve-Target $context $entry.Scope $entry.Target) -eq $path) { $entry.InstalledHash = $change.Hash; break }
            }
        }
        if ($enableChange) {
            $record.Enablement.InstalledHash=$enableChange.Hash
            $changes += $enableChange
        }
        if ($changes.Count) { Invoke-FileTransaction $context $changes $record }
        if ($enableChange) { Write-Step OK 'The Native Menu addon was enabled again; other addon settings were preserved.' }
        if ($updatedCount) {
            Write-Step OK ("Updated {0} installed files and verified them. The original Undo backups were retained." -f $updatedCount)
            Write-Step INFO 'Reopen Tavern Launcher and use Play Game to choose a server inside the game.'
        } else {
            Write-Step OK 'This setup is already installed. All installed files and previous-file backups were verified.'
        }
        return
    }
    Write-Step CHECK 'Preparing verified backups before installing any files.'
    $current = Read-AddonEnablement $context
    $baseline = 'originals\' + [Guid]::NewGuid().ToString('N')
    [IO.Directory]::CreateDirectory((Join-Contained $context.StateRoot $baseline)) | Out-Null
    $entries=@(); $changes=@(); $index=0
    foreach ($item in $plan) {
        $before = Get-Digest $item.Path
        $backup = $baseline + '\' + $index + '.original'
        if ($before) {
            $backupFile = Join-Contained $context.StateRoot $backup
            [IO.File]::Copy($item.Path,$backupFile)
            if ((Get-Digest $backupFile) -ne $before) { throw 'An original-file backup failed verification. No install files were changed.' }
        }
        $entries += [ordered]@{Scope=$item.Scope;Target=$item.Target;Existed=[bool]$before;OriginalHash=$before;Backup=$(if($before){$backup}else{$null});InstalledHash=$item.InstalledHash}
        $changes += [pscustomobject]@{Scope=$item.Scope;Target=$item.Target;Source=$item.Source;Hash=$item.InstalledHash;ExpectedBefore=$before}
        $index++
    }
    $enableRecord=[ordered]@{WasEnabled=(@($current.Names) -ccontains 'tavern_native_menu');OriginalHash=$current.Hash;OriginalBackup=$null;InstalledHash=$current.Hash}
    if ($current.Hash) {
        $enableRecord.OriginalBackup=$baseline + '\enabled-addons.original.json'
        [IO.File]::Copy($current.Path,(Join-Contained $context.StateRoot $enableRecord.OriginalBackup))
        if ((Get-Digest (Join-Contained $context.StateRoot $enableRecord.OriginalBackup)) -ne $current.Hash) { throw 'The original addon settings backup failed verification.' }
    }
    $enableChange=Get-EnableAddonChange $context $current
    if ($enableChange) { $changes += $enableChange; $enableRecord.InstalledHash=$enableChange.Hash }
    $newRecord = [ordered]@{Format=1;Product='Tavern Native Menu setup for Tavern Launcher 1.8.3';GameExe=$context.GameExe;LauncherExe=$context.LauncherExe;Files=$entries;Enablement=$enableRecord}
    Invoke-FileTransaction $context $changes $newRecord
    Write-Step OK 'Installation completed and every installed file was verified.'
    Write-Step INFO 'Reopen Tavern Launcher and use Play Game to choose a server inside the game. The Native Menu addon is enabled.'
}

function Invoke-SetupOperation([string]$Action, [string]$Game, [string]$Launcher, [string]$Payload) {
    $context=Get-Context $Game $Launcher $Payload
    $sha=[Security.Cryptography.SHA256]::Create()
    try { $key=[BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($context.GameRoot.ToUpperInvariant()))).Replace('-','') }
    finally { $sha.Dispose() }
    $mutex=New-Object Threading.Mutex($false,('Local\TavernNativeMenuSetup-' + $key))
    $held=$false
    try {
        try { $held=$mutex.WaitOne(0) } catch [Threading.AbandonedMutexException] { $held=$true }
        if (!$held) { throw 'Another setup is already working on this game folder. Wait for it to finish, then retry.' }
        Invoke-SetupCore $Action $Game $Launcher $Payload
    } finally {
        if ($held) { $mutex.ReleaseMutex() }
        $mutex.Dispose()
    }
}

if ($MyInvocation.InvocationName -ne '.') {
    try {
        if (!$Operation) { throw 'A setup operation was not supplied.' }
        Invoke-SetupOperation $Operation $GameExe $LauncherExe $PayloadRoot
        exit 0
    } catch {
        Write-Step ERROR $_.Exception.Message
        exit 1
    }
}
