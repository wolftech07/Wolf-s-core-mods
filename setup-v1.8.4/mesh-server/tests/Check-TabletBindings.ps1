param([Parameter(Mandatory=$true)][string]$GamePath,[Parameter(Mandatory=$true)][string]$TavernLib)
$ErrorActionPreference='Stop'
[Reflection.Assembly]::Load([IO.File]::ReadAllBytes((Join-Path $GamePath 'MelonLoader\net472\Mono.Cecil.dll'))) | Out-Null
$root=[Mono.Cecil.ModuleDefinition]::ReadModule((Join-Path $GamePath 'A Township Tale_Data\Managed\Root.Township.dll'))
$tavern=[Mono.Cecil.ModuleDefinition]::ReadModule($TavernLib)
$checks=0
function Assert-Property($type,[string]$name,[string]$returnType) {
 $property=@($type.Properties | Where-Object Name -eq $name)
 if($property.Count -ne 1 -or !$property[0].GetMethod -or !$property[0].GetMethod.IsPublic -or ($returnType -and $property[0].PropertyType.FullName -ne $returnType)){throw "Missing or changed public property $($type.FullName).$name"}
 $script:checks++
}
function Assert-Method($type,[string]$name,[string]$returnType,[string[]]$parameters) {
 $method=@($type.Methods | Where-Object { $_.Name -eq $name -and $_.Parameters.Count -eq $parameters.Count })
 if($method.Count -ne 1 -or $method[0].ReturnType.FullName -ne $returnType){throw "Missing or ambiguous method $($type.FullName).$name"}
 for($i=0;$i -lt $parameters.Count;$i++){if($method[0].Parameters[$i].ParameterType.FullName -ne $parameters[$i]){throw "Changed argument $($type.FullName).$name"}}
 $script:checks++
 return $method[0]
}
try {
 $connection=$root.GetType('Alta.Networking.Connection')
 $join=$root.GetType('Alta.Networking.RequestJoinMessage')
 if(!$connection -or !$join -or $connection.Module -ne $join.Module){throw 'Join DTO must be in the same actual assembly as Connection.'};$checks++
 $handler=$root.GetType('Alta.Networking.Servers.ServerPlayerConnectionHandlerOld')
 $method=Assert-Method $handler 'CheckApproved' 'System.Void' @('Alta.Networking.Connection','Alta.Serialization.Stream')
 if($method.Parameters[0].Name -ne 'connection' -or $method.Parameters[1].Name -ne 'stream'){throw 'Harmony admission patch argument names changed.'};$checks++
 $method=Assert-Method $handler 'PlayerDenied' 'System.Threading.Tasks.Task' @('Alta.Networking.Connection','System.String')
 if(!$method.IsStatic){throw 'PlayerDenied is no longer static.'};$checks++
 $null=Assert-Method $join '.ctor' 'System.Void' @()
 $null=Assert-Method $join 'Serialize' 'System.Void' @('Alta.Networking.Connection','Alta.Serialization.Stream')
 Assert-Property $join 'PlayerId' 'System.Int32'
 Assert-Property $join 'UserCredentials' 'System.String'
 $null=Assert-Method $connection 'Disconnect' 'System.Void' @('System.String')
 $services=$tavern.GetType('TavernLib.Services.TavernServices')
 $manager=$tavern.GetType('TavernLib.Backend.Api.TavernManager')
 if(!$manager){$manager=$tavern.GetType('TavernLib.Backend.Api.TavernApiManager')}
 $get=@($services.Methods | Where-Object Name -eq GetService)
 if($get.Count -ne 1 -or !$get[0].IsPublic -or !$get[0].IsStatic -or $get[0].Parameters.Count -ne 0 -or $get[0].GenericParameters.Count -ne 1){throw 'TavernServices.GetService<T>() binding changed.'};$checks++
 Assert-Property $manager 'UserConfig' 'TavernLib.Backend.Server.Configs.UserConfigFile'
 $config=$tavern.GetType('TavernLib.Backend.Server.Configs.UserConfigFile')
 $base=$tavern.GetType('TavernLib.Backend.Server.Configs.ServerConfigFile`1')
 if(!$config -or !$base -or $config.BaseType.ElementType.FullName -ne $base.FullName){throw 'UserConfigFile no longer inherits expected server config reader.'};$checks++
 Assert-Property $base 'LastRead' 'T'
 $read=Assert-Method $base 'ReadFromFile' 'System.Void' @()
 if(!$read.IsPublic){throw 'ReadFromFile is no longer public.'};$checks++
 $users=$tavern.GetType('TavernLib.Backend.Server.Configs.UserConfig')
 Assert-Property $users 'Users' ''
 $record=@($users.NestedTypes | Where-Object Name -eq User)[0]
 Assert-Property $record 'UserId' 'System.UInt64'
 Assert-Property $record 'Token' 'System.String'
 Assert-Property $record 'Roles' 'System.Collections.Generic.List`1<System.String>'
 "PASS $checks actual game/Tavern server admission and authority bindings: $TavernLib"
} finally {$root.Dispose();$tavern.Dispose()}
