param([Parameter(Mandatory=$true)][string]$GamePath, [string]$AlternateRoot)
$ErrorActionPreference = 'Stop'
[Reflection.Assembly]::LoadFrom((Join-Path $GamePath 'MelonLoader\net472\Mono.Cecil.dll')) | Out-Null
$paths = @((Join-Path $GamePath 'A Township Tale_Data\Managed\Root.Township.dll'))
if ($AlternateRoot) { $paths += $AlternateRoot }
$requirements = @{
    ServerBoard = @{ Methods=@('RefreshServersList','LoadServers','FilterServers','ResumeUpdate'); Fields=@('lastReceivedServers','OnRefreshFinished','headingText','loadServers','updateCountdownText') }
    ServerSelectionMenu = @{ Methods=@('Start','Setup'); Fields=@('startingBoard','boards') }
    ServerElement = @{ Methods=@('SetupForServer'); Fields=@('numberOfPlayersText') }
    VrMainMenu = @{ Methods=@('JoinServer'); Fields=@('startingGameTask','serverSelection') }
    ApplicationStartupManager = @{ Methods=@('RunStartupActions'); Fields=@() }
    GameModeManager = @{ Methods=@('StopCurrentModeAsync','JoinServer'); Fields=@() }
    AltaSceneManager = @{ Methods=@('LoadMainMenuSceneAsync','ReturnToMainMenuAsync','get_IsLoading'); Fields=@() }
    'Alta.QuickAccessActions.ReturnToMainMenuAction' = @{ Methods=@('LetGoValid'); Fields=@() }
    'Features.ServerBoardFilter' = @{ Methods=@('Filter'); Fields=@() }
}
$total=0
foreach ($path in $paths) {
    $module = [Mono.Cecil.ModuleDefinition]::ReadModule($path)
    try {
        foreach ($name in $requirements.Keys) {
            $type=$module.GetType($name); if (!$type) { throw "Missing type $name in $path" }
            foreach($method in $requirements[$name].Methods) {
                if (!@($type.Methods | Where-Object Name -eq $method).Count) { throw "Missing method $name.$method" }; $total++
            }
            foreach($field in $requirements[$name].Fields) {
                if (!@($type.Fields | Where-Object Name -eq $field).Count) { throw "Missing field $name.$field" }; $total++
            }
        }
        $refresh = @($module.GetType('ServerBoard').Methods | Where-Object Name -eq 'RefreshServersList')[0]
        if($refresh.ReturnType.FullName -ne 'System.Threading.Tasks.Task'){throw 'Unexpected refresh task type.'}; $total++
        $stop = @($module.GetType('GameModeManager').Methods | Where-Object Name -eq 'StopCurrentModeAsync')[0]
        if($stop.Parameters[1].Name -ne 'isReturningToMenu'){throw 'Return hook parameter name changed.'}; $total++
    } finally { $module.Dispose() }
}
Write-Output "PASS $total native method/field binding checks across $($paths.Count) supplied game assemblies."
