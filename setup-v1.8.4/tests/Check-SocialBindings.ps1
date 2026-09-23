param([Parameter(Mandatory=$true)][string]$GameFolder, [Parameter(Mandatory=$true)][string]$AlternateRoot)
$ErrorActionPreference='Stop'
[Reflection.Assembly]::LoadFrom((Join-Path $GameFolder 'MelonLoader\net472\Mono.Cecil.dll')) | Out-Null
$checks=0
$requirements=@{
 'Alta.Meta.UI.ServerManagement.FriendsListPanel'=@{ Methods=@('SetupFriendsList','RefreshData'); Fields=@('listElement','uiList','emptyListText','inviteWindow') }
 'Alta.Meta.UI.ServerManagement.FriendsRequestsPanel'=@{ Methods=@('SetupFriendsList','RefreshData'); Fields=@('listElement','uiList','emptyListText') }
 'Alta.Meta.UI.ServerManagement.InviteUserToGroupPanel'=@{ Methods=@('SetupForUser','SetupGroupsList','RefreshData'); Fields=@('elementTemplate','uiList') }
 'Alta.Meta.Friends.FriendsManager'=@{ Methods=@('UpdateFriends','GetUserInfoToDisplay','ConfigureTokenForTab','UpdateDisplay'); Fields=@('currentTab') }
 'FriendRequestToken'=@{ Methods=@('AddFriend','RunEffects','SyncFriendRequestForPlayer','SyncFriendRequestForTokenOwner'); Fields=@('timelineEntry','otherTokenId','receivedOwnerToken','syncFriendshipRequestEffect') }
 'FriendshipManager'=@{ Methods=@('AddFriend','UpdateStatus','IsFriendsWith'); Fields=@() }
}
foreach($file in @((Join-Path $GameFolder 'A Township Tale_Data\Managed\Root.Township.dll'),$AlternateRoot)) {
 $module=[Mono.Cecil.ModuleDefinition]::ReadModule($file)
 try {
  foreach($name in $requirements.Keys) {
   $type=$module.GetType($name); if(!$type){throw "Missing social type: $name"}
   foreach($method in $requirements[$name].Methods){if(!@($type.Methods | Where-Object Name -eq $method).Count){throw "Missing social method $name.$method"};$checks++}
   foreach($field in $requirements[$name].Fields){if(!@($type.Fields | Where-Object Name -eq $field).Count){throw "Missing social field $name.$field"};$checks++}
  }
  $card=$module.GetType('FriendRequestToken')
  foreach($pair in @(@('SyncFriendRequestForPlayer','friendID'),@('SyncFriendRequestForTokenOwner','player'))) {
   $method=@($card.Methods | Where-Object Name -eq $pair[0])[0]
   if($method.Parameters.Count -ne 1 -or $method.Parameters[0].Name -ne $pair[1]){throw "Unexpected card patch argument $($pair[0])"};$checks++
  }
  foreach($name in @('Alta.Meta.UI.ServerManagement.FriendsListPanel','Alta.Meta.UI.ServerManagement.FriendsRequestsPanel')) {
   $method=@($module.GetType($name).Methods | Where-Object Name -eq SetupFriendsList)[0]
   if($method.ReturnType.FullName -ne 'System.Threading.Tasks.Task'){throw "Unexpected friends loading signature: $name"};$checks++
  }
 } finally {$module.Dispose()}
}
$module=[Mono.Cecil.ModuleDefinition]::ReadModule((Join-Path $GameFolder 'A Township Tale_Data\Managed\Root.Township.dll'))
try {
 $connection=$module.GetType('Alta.Networking.Connection')
 if(@($connection.Methods | Where-Object Name -eq '.ctor').Count -ne 1){throw 'Connection constructor patch is ambiguous.'};$checks++
 if(!@($connection.Fields | Where-Object Name -eq messageHandlers).Count){throw 'Native message handler dictionary missing.'};$checks++
 foreach($name in @('SetHandler','ClearHandler','Send','get_IsApproved','get_Player','get_IsDisposed')) {if(!@($connection.Methods | Where-Object Name -eq $name).Count){throw "Missing networking method $name"};$checks++}
} finally {$module.Dispose()}
"PASS $checks native social/card/transport binding checks against the supplied assemblies."
