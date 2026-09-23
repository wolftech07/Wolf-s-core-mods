[CmdletBinding()]
param(
    [ValidateSet('Install','Uninstall')][string]$Operation,
    [string]$ServerFolder,
    [string]$PayloadRoot = $PSScriptRoot
)
$ErrorActionPreference = 'Stop'
$workerOptions=@{PayloadRoot=$PayloadRoot}
if ($Operation) { $workerOptions.Operation=$Operation }
. (Join-Path $PSScriptRoot 'Setup-Worker.ps1') @workerOptions

function Get-ServerCardContext([string]$Folder,[string]$Payload) {
    if ([string]::IsNullOrWhiteSpace($Folder)) { throw 'Select the patched game server folder.' }
    $root=[IO.Path]::GetFullPath($Folder)
    Assert-NoReparse $root
    $exe=Join-Contained $root 'A Township Tale.exe'
    foreach ($relative in @('A Township Tale.exe','MelonLoader\net472\MelonLoader.dll','Plugins\TavernLib.dll','A Township Tale_Data\Managed\Root.Township.dll')) {
        if (!(Test-Path -LiteralPath (Join-Contained $root $relative) -PathType Leaf)) { throw "The selected server folder is missing $relative. Apply the Tavern server patch and MelonLoader first." }
    }
    $state=Join-Contained $root 'TavernNativeMenuCardSetup'
    return [pscustomobject]@{GameExe=$exe;LauncherExe=$exe;GameRoot=$root;LauncherRoot=$root;ServerOnly=$true;
        PayloadRoot=[IO.Path]::GetFullPath($Payload);StateRoot=$state;Manifest=(Join-Path $state 'installed.json');Journal=(Join-Path $state 'pending.json')}
}
function Read-ServerCardRecord($Context) {
    if (!(Test-Path -LiteralPath $Context.Manifest -PathType Leaf)) { return $null }
    $record=[IO.File]::ReadAllText($Context.Manifest) | ConvertFrom-Json
    if ($record.Format -ne 1 -or $record.Product -ne 'Tavern native mesh server cards' -or $record.GameExe -ne $Context.GameExe -or $record.LauncherExe -ne $Context.LauncherExe -or @($record.Files).Count -ne 1) {
        throw 'The server-card installation record is invalid or belongs to a different server folder.'
    }
    $entry=@($record.Files)[0]
    $null=Resolve-Target $Context $entry.Scope $entry.Target
    if ($entry.InstalledHash -notmatch '^[A-Fa-f0-9]{64}$') { throw 'The server-card installation hash is invalid.' }
    if ($entry.Existed) {
        if ($entry.OriginalHash -notmatch '^[A-Fa-f0-9]{64}$' -or (Get-Digest (Join-Contained $Context.StateRoot $entry.Backup)) -ne $entry.OriginalHash) { throw 'The original server-card backup is missing or changed. No server files were changed.' }
    } elseif ($entry.OriginalHash -or $entry.Backup) { throw 'The original server-card absence record is invalid.' }
    return $record
}
function Invoke-ServerCardCore([string]$Action,$Context) {
    Assert-ApplicationsClosed $Context
    Undo-PendingTransaction $Context
    $record=Read-ServerCardRecord $Context
    $relative='Mods\TavernNativeSocialServer.dll'
    $target=Resolve-Target $Context 'game' $relative
    if (Test-Path -LiteralPath $target -PathType Container) { throw 'A folder blocks the server companion DLL.' }
    if ($record) { Assert-InstalledUnchanged $Context $record }
    if ($Action -eq 'Uninstall') {
        if (!$record) { Write-Step OK 'No managed server-card installation was found. Nothing was removed.'; return }
        $entry=@($record.Files)[0]
        $source=if ($entry.Existed) { Join-Contained $Context.StateRoot $entry.Backup } else { $null }
        $changes=@([pscustomobject]@{Scope='game';Target=$relative;Source=$source;Hash=$entry.OriginalHash;ExpectedBefore=$entry.InstalledHash})
        Invoke-FileTransaction $Context $changes $null
        Write-Step OK 'Server-card setup undone. Previous companion restored; server saves and settings retained.'
        return
    }
    if ($Action -ne 'Install') { throw 'Choose Install or Uninstall for server cards.' }
    $source=Join-Contained $Context.PayloadRoot 'server\TavernNativeSocialServer.dll'
    $hash=Get-Digest $source
    if (!$hash) { throw 'This setup is missing its embedded server-card companion.' }
    $before=Get-Digest $target
    if ($record) {
        if (@($record.Files)[0].InstalledHash -eq $hash) { Write-Step OK 'Server-card support is already up to date.'; return }
        @($record.Files)[0].InstalledHash=$hash
    } else {
        $backup=$null
        if ($before) {
            $backup='originals\' + [Guid]::NewGuid().ToString('N') + '.dll'
            $destination=Join-Contained $Context.StateRoot $backup
            [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($destination)) | Out-Null
            [IO.File]::Copy($target,$destination)
            if ((Get-Digest $destination) -ne $before) { throw 'The original server companion backup failed verification.' }
        }
        $record=[ordered]@{Format=1;Product='Tavern native mesh server cards';GameExe=$Context.GameExe;LauncherExe=$Context.LauncherExe;
            Files=@([pscustomobject]@{Scope='game';Target=$relative;Existed=[bool]$before;OriginalHash=$before;Backup=$backup;InstalledHash=$hash})}
    }
    Invoke-FileTransaction $Context @([pscustomobject]@{Scope='game';Target=$relative;Source=$source;Hash=$hash;ExpectedBefore=$before}) $record
    Write-Step OK 'Server-card support installed and verified. Start the game server normally; no relay process or relay address is required.'
    Write-Step INFO 'Both players need the current mesh client mod. Server saves and existing social configuration were retained.'
}
function Invoke-ServerCardOperation([string]$Action,[string]$Folder,[string]$Payload) {
    $context=Get-ServerCardContext $Folder $Payload
    $sha=[Security.Cryptography.SHA256]::Create()
    try { $key=[BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($context.GameRoot.ToUpperInvariant()))).Replace('-','') }
    finally { $sha.Dispose() }
    $mutex=New-Object Threading.Mutex($false,('Local\TavernNativeMenuSetup-' + $key))
    $held=$false
    try {
        try { $held=$mutex.WaitOne(0) } catch [Threading.AbandonedMutexException] { $held=$true }
        if (!$held) { throw 'Another setup is already working on this server folder.' }
        Invoke-ServerCardCore $Action $context
    } finally { if ($held) { $mutex.ReleaseMutex() }; $mutex.Dispose() }
}
if ($MyInvocation.InvocationName -ne '.') {
    try { Invoke-ServerCardOperation $Operation $ServerFolder $PayloadRoot; exit 0 }
    catch { Write-Step ERROR $_.Exception.Message; exit 1 }
}
