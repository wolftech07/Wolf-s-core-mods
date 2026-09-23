[CmdletBinding()]
param()
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot '..\payload\Setup-ServerCards.ps1')
$checks=0
function Check([bool]$Condition,[string]$Message) { if (!$Condition) { throw "FAIL $Message" }; $script:checks++; Write-Output "PASS $Message" }
function Fails([scriptblock]$Action,[string]$Message) { $failed=$false; try { & $Action | Out-Null } catch { $failed=$true }; Check $failed $Message }
function Put([string]$Path,[string]$Text) { [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($Path)) | Out-Null; [IO.File]::WriteAllText($Path,$Text) }
function Fixture([string]$Name) {
    $folder=Join-Path $script:root ($Name + " & [server] O'Brien")
    foreach ($file in @('A Township Tale.exe','MelonLoader\net472\MelonLoader.dll','Plugins\TavernLib.dll','A Township Tale_Data\Managed\Root.Township.dll')) { Put (Join-Path $folder $file) 'fixture dependency' }
    $payload=Join-Path $script:root ($Name + '-payload')
    Put (Join-Path $payload 'server\TavernNativeSocialServer.dll') 'new mesh companion'
    return Get-ServerCardContext $folder $payload
}
function Run($Context,[string]$Action) { Invoke-ServerCardOperation $Action $Context.GameRoot $Context.PayloadRoot | Out-Null }
$script:root=Join-Path ([IO.Path]::GetTempPath()) ('Tavern-server-card-tests-' + [Guid]::NewGuid().ToString('N'))
try {
    $c=Fixture 'existing'
    $dll=Resolve-Target $c 'game' 'Mods/TavernNativeSocialServer.dll'
    $config=Join-Path $c.GameRoot 'UserData\TavernNativeSocialServer.json'
    Put $dll 'original relay companion'
    Put $config '{"old":"relay credentials are retained"}'
    $configHash=Get-Digest $config
    Run $c Install
    Check ([IO.File]::ReadAllText($dll) -eq 'new mesh companion') 'Install replaces old companion at the same DLL path'
    Check ((Get-Digest $config) -eq $configHash) 'Install retains old relay configuration bytes'
    $record=Read-ServerCardRecord $c
    $manifestHash=Get-Digest $c.Manifest
    Run $c Install
    Check ((Get-Digest $c.Manifest) -eq $manifestHash) 'Repeated server-card install is idempotent'
    Put (Join-Path $c.PayloadRoot 'server\TavernNativeSocialServer.dll') 'updated mesh companion'
    Run $c Install
    Check ([IO.File]::ReadAllText($dll) -eq 'updated mesh companion') 'Server-card update installs changed companion'
    Check ((Read-ServerCardRecord $c).Files[0].Backup -eq $record.Files[0].Backup) 'Server-card update retains first original backup'
    Run $c Uninstall
    Check ([IO.File]::ReadAllText($dll) -eq 'original relay companion') 'Undo after update restores original relay companion'
    Check ((Get-Digest $config) -eq $configHash) 'Undo leaves existing server social configuration untouched'
    Run $c Uninstall
    Check ([IO.File]::ReadAllText($dll) -eq 'original relay companion') 'Repeated server-card Undo is harmless'

    $c=Fixture 'absent'
    $dll=Resolve-Target $c 'game' 'Mods/TavernNativeSocialServer.dll'
    Run $c Install
    Check (!(Test-Path -LiteralPath (Join-Path $c.GameRoot 'UserData\TavernNativeSocialServer.json'))) 'Fresh server-card install needs no relay configuration'
    Run $c Uninstall
    Check (!(Test-Path -LiteralPath $dll)) 'Undo restores original companion absence'
    Fails { Resolve-Target $c 'game' 'Mods/AnotherMod.dll' } 'Server-card context rejects another mod DLL target'
    Fails { Resolve-Target $c 'game' 'Mods/../outside.dll' } 'Server-card context rejects traversal'
    Fails { Resolve-Target $c 'launcher' 'Mods/TavernNativeSocialServer.dll' } 'Server-card context rejects launcher scope'
    Run $c Install
    Put $dll 'independent user change'
    Fails { Run $c Install } 'Server-card update refuses independently changed DLL'
    Fails { Run $c Uninstall } 'Server-card Undo refuses independently changed DLL'
    Check ([IO.File]::ReadAllText($dll) -eq 'independent user change') 'Conflicting server companion remains intact'

    foreach ($interrupt in @($false,$true)) {
        $c=Fixture ('rollback-' + $interrupt)
        $dll=Resolve-Target $c 'game' 'Mods/TavernNativeSocialServer.dll'
        Put $dll 'original companion before failure'
        Run $c Install
        $priorHash=Get-Digest $c.Manifest
        Put (Join-Path $c.PayloadRoot 'server\TavernNativeSocialServer.dll') 'new companion to recover'
        $script:RealAtomic=${function:Write-AtomicBytes}
        $script:RealRecovery=${function:Undo-PendingTransaction}
        $script:Injected=$false
        $script:FailManifest=$c.Manifest
        function Write-AtomicBytes([string]$Destination,[byte[]]$Bytes) {
            & $script:RealAtomic $Destination $Bytes
            if (!$script:Injected -and $Destination -eq $script:FailManifest) { $script:Injected=$true; throw 'Injected server-card manifest failure.' }
        }
        if ($interrupt) { function Undo-PendingTransaction($Context) { if (Test-Path -LiteralPath $Context.Journal) { throw 'Injected exit before server-card recovery.' } } }
        try { Fails { Run $c Install } ('Server-card commit failure reports error; interrupted=' + $interrupt) }
        finally { ${function:Write-AtomicBytes}=$script:RealAtomic; ${function:Undo-PendingTransaction}=$script:RealRecovery }
        Check $script:Injected ('Server-card failure occurs after DLL and manifest writes; interrupted=' + $interrupt)
        if ($interrupt) {
            Check (Test-Path -LiteralPath $c.Journal) 'Interrupted server-card update keeps recovery journal'
            Run $c Uninstall
            Check ([IO.File]::ReadAllText($dll) -eq 'original companion before failure') 'Next server-card Undo automatically recovers and restores original companion'
        } else {
            Check ((Get-Digest $c.Manifest) -eq $priorHash -and [IO.File]::ReadAllText($dll) -eq 'new mesh companion') 'Failed server-card update restores previous release and manifest'
            Run $c Install
            Check ([IO.File]::ReadAllText($dll) -eq 'new companion to recover') 'Server-card update can be retried after rollback'
            Run $c Uninstall
            Check ([IO.File]::ReadAllText($dll) -eq 'original companion before failure') 'Undo after server-card retry preserves initial baseline'
        }
        Check (!(Test-Path -LiteralPath $c.Journal)) ('Recovered server-card operation clears journal; interrupted=' + $interrupt)
    }
    Write-Output "Server-card worker: $checks checks passed in isolated fixtures."
} finally {
    $full=[IO.Path]::GetFullPath($script:root)
    $parent=[IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\')+'\'
    if ($full.StartsWith($parent,[StringComparison]::OrdinalIgnoreCase) -and [IO.Path]::GetFileName($full).StartsWith('Tavern-server-card-tests-') -and (Test-Path -LiteralPath $full)) { Remove-Item -LiteralPath $full -Recurse -Force }
}
