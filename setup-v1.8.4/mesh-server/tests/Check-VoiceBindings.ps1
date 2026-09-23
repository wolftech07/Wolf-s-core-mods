param([Parameter(Mandatory=$true)][string]$GamePath,[Parameter(Mandatory=$true)][string]$VoicePlugin)
$ErrorActionPreference='Stop'
[Reflection.Assembly]::Load([IO.File]::ReadAllBytes((Join-Path $GamePath 'MelonLoader\net472\Mono.Cecil.dll'))) | Out-Null
$module=[Mono.Cecil.ModuleDefinition]::ReadModule($VoicePlugin)
$checks=0
function Method($type,[string]$name) {
 $method=@($type.Methods | Where-Object Name -eq $name)
 if($method.Count -ne 1 -or !$method[0].HasBody){throw "Missing or ambiguous voice method $($type.FullName).$name"}
 return $method[0]
}
function Calls($method,[string]$type,[string]$name) {
 return @($method.Body.Instructions | Where-Object { $_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.DeclaringType.FullName -like $type -and $_.Operand.Name -eq $name }).Count -gt 0
}
function Assert($condition,[string]$name) {
 if(!$condition){throw $name};$script:checks++
}
try {
 $replacement=$module.GetType('CircuitsVoiceChat.ReplacementVoiceChat')
 $runtime=$module.GetType('CircuitsVoiceChat.VoiceRuntime')
 $remote=$module.GetType('CircuitsVoiceChat.RemoteVoice')
 Assert ($replacement -and $runtime -and $remote) 'Expected Circuits voice types must exist.'
 Assert (@($replacement.Interfaces | Where-Object { $_.InterfaceType.FullName -eq 'Alta.Voice.IVoiceChat' }).Count -eq 1) 'Replacement must implement the native voice interface.'
 $set=Method $replacement 'SetPlayerMuted'
 Assert ($set.IsPublic -and $set.Parameters.Count -eq 2 -and $set.Parameters[0].ParameterType.FullName -eq 'System.Int32' -and $set.Parameters[1].ParameterType.FullName -eq 'System.Boolean') 'Native mute signature must be int ID and bool muted.'
 Assert ((Calls $set 'System.Collections.Generic.HashSet*' 'Add') -and (Calls $set 'System.Collections.Generic.HashSet*' 'Remove') -and (Calls $set 'CircuitsVoiceChat.VoiceRuntime' 'ClearMutedAudio')) 'Mute must modify membership and clear buffered audio.'
 $audible=Method $replacement 'IsAudible'
 Assert (Calls $audible 'System.Collections.Generic.HashSet*' 'Contains') 'Audibility must consult the muted-player set.'
 $initialize=Method $runtime 'Initialize'
 Assert (@($initialize.Body.Instructions | Where-Object { $_.Operand -is [Mono.Cecil.GenericInstanceMethod] -and $_.Operand.Name -eq 'SetInstanceOverride' -and $_.Operand.GenericArguments[0].FullName -eq 'Alta.Voice.IVoiceChat' }).Count -eq 1) 'Plugin must register its replacement with InterfaceResolver.'
 $update=Method $runtime 'Update'
 Assert ((Calls $update 'CircuitsVoiceChat.ReplacementVoiceChat' 'IsAudible') -and (Calls $update 'CircuitsVoiceChat.RemoteVoice' 'Update')) 'Playback updates must consult replacement audibility.'
 $playback=Method $remote 'Update'
 Assert (@($playback.Body.Instructions | Where-Object { $_.OpCode.Name -eq 'stfld' -and $_.Operand.Name -eq 'playbackMuted' }).Count -gt 0) 'Remote playback must record muted state.'
 $read=Method $remote 'ReadAudio'
 Assert (@($read.Body.Instructions | Where-Object { $_.OpCode.Name -eq 'ldfld' -and $_.Operand.Name -eq 'playbackMuted' }).Count -gt 0 -and (Calls $read 'CircuitsVoiceChat.PcmRingBuffer' 'Clear')) 'Audio callback must check playback mute and clear buffered samples.'
 "PASS $checks actual voice-interface and mute playback bindings: $VoicePlugin"
} finally {$module.Dispose()}
