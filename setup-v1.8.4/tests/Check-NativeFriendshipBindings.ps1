param([Parameter(Mandatory=$true)][string]$GameFolder, [string]$AlternateRoot)
$ErrorActionPreference = 'Stop'
[Reflection.Assembly]::LoadFrom((Join-Path $GameFolder 'MelonLoader\net472\Mono.Cecil.dll')) | Out-Null
$files = @((Join-Path $GameFolder 'A Township Tale_Data\Managed\Root.Township.dll'))
if ($AlternateRoot) { $files += $AlternateRoot }
$checks = 0
$requirements = @(
    @{ Name='UpdateFromNetwork'; Return='System.Void'; Types=@('Alta.Networking.Scripts.Player.IPlayer'); Names=@('player') },
    @{ Name='UpdateStatus'; Return='System.Void'; Types=@('System.Boolean'); Names=@('isFriend') },
    @{ Name='UpdateStatusWithPlayer'; Return='System.Void'; Types=@('System.Boolean','System.Int32'); Names=@('isFriend','playerId') },
    @{ Name='GetFriends'; Return='System.Threading.Tasks.Task'; Types=@('System.Boolean'); Names=@('isLocalPlayer') },
    @{ Name='IsFriendsWith'; Return='System.Boolean'; Types=@('System.Int32'); Names=@('player') },
    @{ Name='AddFriend'; Return='System.Void'; Types=@('Alta.Api.DataTransferModels.Models.Responses.FriendshipInfo'); Names=@('newFriendShip') }
)
foreach ($file in $files) {
    $module = [Mono.Cecil.ModuleDefinition]::ReadModule($file)
    try {
        $type = $module.GetType('FriendshipManager')
        if (!$type) { throw 'FriendshipManager is missing.' }
        $field = @($type.Fields | Where-Object Name -eq 'player')
        if ($field.Count -ne 1 -or $field[0].FieldType.FullName -ne 'System.Int32') { throw 'Native friendship manager player identifier changed.' }
        $checks++
        $friends = @($type.Properties | Where-Object Name -eq 'Friends')
        if ($friends.Count -ne 1 -or $friends[0].PropertyType.FullName -ne 'System.Collections.Generic.List`1<Alta.Api.DataTransferModels.Models.Responses.UserInfo>') { throw 'Native friendship list type changed.' }
        $checks++
        foreach ($item in $requirements) {
            $methods = @($type.Methods | Where-Object Name -eq $item.Name)
            if ($methods.Count -ne 1) { throw ('Native friendship method is missing or ambiguous: ' + $item.Name) }
            $method = $methods[0]
            if ($method.ReturnType.FullName -ne $item.Return -or $method.Parameters.Count -ne $item.Types.Count) { throw ('Native friendship signature changed: ' + $item.Name) }
            $checks++
            for ($i=0; $i -lt $item.Types.Count; $i++) {
                if ($method.Parameters[$i].ParameterType.FullName -ne $item.Types[$i] -or $method.Parameters[$i].Name -ne $item.Names[$i]) { throw ('Native friendship patch argument changed: ' + $item.Name) }
                $checks++
            }
        }
        $player = $module.GetType('Player')
        $current = @($player.Methods | Where-Object Name -eq 'get_Current')
        if ($current.Count -ne 1 -or !$current[0].IsStatic -or $current[0].ReturnType.FullName -ne 'Alta.Networking.Scripts.Player.IPlayer') { throw 'Native current player binding changed.' }
        $checks++
        $lookup = @($player.Methods | Where-Object Name -eq 'SafeGetPlayer')
        if ($lookup.Count -ne 1 -or !$lookup[0].IsStatic -or $lookup[0].ReturnType.FullName -ne 'System.Boolean' -or $lookup[0].Parameters.Count -ne 2 -or $lookup[0].Parameters[0].ParameterType.FullName -ne 'System.Int32' -or $lookup[0].Parameters[1].ParameterType.FullName -ne 'Player&') { throw 'Native player lookup binding changed.' }
        $checks++
    } finally { $module.Dispose() }
}
"PASS $checks exact native friendship signatures, fields, patch arguments, and player bindings."
