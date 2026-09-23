param([Parameter(Mandatory=$true)][string]$GameFolder, [Parameter(Mandatory=$true)][string]$AlternateRoot)
$ErrorActionPreference = 'Stop'
Add-Type -Path (Join-Path $GameFolder 'MelonLoader\net472\Mono.Cecil.dll')
$requirements = @{
 'SocialTablet' = @{ Methods=@('StartupForLocalPlayer','get_IsLocalPlayer'); Fields=@() }
 'RecentPlayerActionPage' = @{ Methods=@('Setup','SetupBanButton'); Fields=@('muteButton','unmuteButton','friendButtons','banButton','unbanButton','recentPlayer','recentPlayerElements') }
 'Alta.SocialTablet.RecentPlayersPage' = @{ Methods=@('RefreshData','UpdatePageView'); Fields=@('recentPlayers','currentPage','playerListElements','emptyText') }
 'RecentPlayerElement' = @{ Methods=@('UpdateView','get_ExtraText'); Fields=@() }
 'TouchScreenMenuBase' = @{ Methods=@('MapButtons','get_InstantiatedButtons'); Fields=@() }
 'Alta.Meta.UI.TouchScreenButton' = @{ Methods=@(); Fields=@('touchSize') }
 'Alta.Meta.UI.TouchScreenButtonVisual' = @{ Methods=@(); Fields=@('startingScale') }
 'TextRenderer' = @{ Methods=@('set_Scale','set_MaxLines'); Fields=@('isMultiLine','bestFitToWidth','hasLineLimit','lineWidth','lineSpacing') }
}
$checks = 0
foreach ($dll in @((Join-Path $GameFolder 'A Township Tale_Data\Managed\Root.Township.dll'), $AlternateRoot)) {
 $assembly = [Mono.Cecil.ModuleDefinition]::ReadModule($dll)
 try {
  foreach ($name in $requirements.Keys) {
   $type = $assembly.GetType($name)
   if (!$type) { throw "Missing tablet type $name" }
   foreach ($kind in @('Methods','Fields')) {
    foreach ($member in $requirements[$name][$kind]) {
     $found = $false; $current = $type
     while ($null -ne $current) {
      if (@($current.$kind | Where-Object Name -eq $member).Count) { $found = $true; break }
      $current = if ($null -ne $current.BaseType) { $assembly.GetType($current.BaseType.FullName) } else { $null }
     }
     if (!$found) { throw "Missing tablet $kind $name.$member" }; $checks++
    }
   }
  }
  $setup = @($assembly.GetType('RecentPlayerActionPage').Methods | Where-Object Name -eq 'Setup')[0]
  if ($setup.Parameters.Count -ne 1 -or $setup.Parameters[0].Name -ne 'recentPlayer' -or $setup.Parameters[0].ParameterType.Name -ne 'RecentPlayerInteractionInfo') { throw 'Tablet selection prefix no longer matches native Setup' }; $checks++
  $refresh = @($assembly.GetType('Alta.SocialTablet.RecentPlayersPage').Methods | Where-Object Name -eq 'RefreshData')[0]
  if ($refresh.ReturnType.FullName -ne 'System.Void') { throw 'Unexpected native tablet refresh return type' }; $checks++
 } finally { $assembly.Dispose() }
}
"PASS $checks tablet reflection and Harmony binding checks against both supplied game assemblies."
