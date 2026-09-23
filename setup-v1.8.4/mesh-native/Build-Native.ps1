param(
    [string]$VisualStudioPath,
    [switch]$SkipDependencies
)
$ErrorActionPreference = 'Stop'
$meshRoot = $PSScriptRoot

function Invoke-NativeTool([string]$Executable, [string[]]$Arguments) {
    $start = New-Object System.Diagnostics.ProcessStartInfo
    $start.FileName = $Executable
    $start.UseShellExecute = $false
    $start.WorkingDirectory = $meshRoot
    # Some shell hosts supply both Path and PATH; MSBuild rejects that environment.
    $start.EnvironmentVariables.Clear()
    foreach ($entry in [Environment]::GetEnvironmentVariables().GetEnumerator()) {
        $start.EnvironmentVariables[$entry.Key.ToString().ToUpperInvariant()] = $entry.Value.ToString()
    }
    $start.Arguments = (($Arguments | ForEach-Object { '"' + ($_ -replace '(\\*)"', '$1$1\"' -replace '(\\+)$', '$1$1') + '"' }) -join ' ')
    $process = [System.Diagnostics.Process]::Start($start)
    $process.WaitForExit()
    if ($process.ExitCode -ne 0) { throw "$Executable exited with $($process.ExitCode)." }
}

if (!$VisualStudioPath) {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    if (!(Test-Path -LiteralPath $vswhere)) { throw 'Install Visual Studio with Desktop development with C++ first.' }
    $VisualStudioPath = (& $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath | Select-Object -First 1)
}
if (!$VisualStudioPath) { throw 'A Visual Studio C++ installation was not found.' }
$cmake = Join-Path $VisualStudioPath 'Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin\cmake.exe'
$vcpkg = Join-Path $VisualStudioPath 'VC\vcpkg\vcpkg.exe'
if (!(Test-Path -LiteralPath $cmake) -or !(Test-Path -LiteralPath $vcpkg)) { throw 'Visual Studio CMake and vcpkg components are required.' }
$sourceArchive = Join-Path $meshRoot 'downloads\c-toxcore-v0.2.23.tar.gz'
$sourceHash = '15cdd006ed7793dfc657e340ef9f218f6637d2fe5b130704d39b961389bb6cd6'
New-Item -ItemType Directory -Path (Join-Path $meshRoot 'downloads'),(Join-Path $meshRoot 'src'),(Join-Path $meshRoot 'dist'),(Join-Path $meshRoot 'binary-cache'),(Join-Path $meshRoot 'registry-cache') -Force | Out-Null
if (!(Test-Path -LiteralPath $sourceArchive)) {
    Invoke-WebRequest -UseBasicParsing -Uri 'https://github.com/TokTok/c-toxcore/releases/download/v0.2.23/c-toxcore-v0.2.23.tar.gz' -OutFile $sourceArchive
}
if ((Get-FileHash -LiteralPath $sourceArchive -Algorithm SHA256).Hash.ToLowerInvariant() -ne $sourceHash) { throw 'Tox source checksum mismatch.' }
if (!(Test-Path -LiteralPath (Join-Path $meshRoot 'src\toxcore\tox.h'))) {
    # The tar contains an unrelated Apple packaging LICENSE symlink that Windows tar cannot create.
    Invoke-NativeTool 'tar.exe' @('-xzf', $sourceArchive, '--exclude=./other/deploy/apple/LICENSE', '-C', (Join-Path $meshRoot 'src'))
}

if (!$SkipDependencies) {
    $env:VCPKG_DOWNLOADS = Join-Path $meshRoot 'downloads'
    $env:VCPKG_DEFAULT_BINARY_CACHE = Join-Path $meshRoot 'binary-cache'
    $env:X_VCPKG_REGISTRIES_CACHE = Join-Path $meshRoot 'registry-cache'
    Invoke-NativeTool $vcpkg @('install', '--triplet', 'x64-windows-static', '--host-triplet', 'x64-windows-static', "--x-manifest-root=$meshRoot", "--x-install-root=$meshRoot\installed", "--x-buildtrees-root=$meshRoot\buildtrees", "--x-packages-root=$meshRoot\packages", '--disable-metrics')
}
$generator = if ((Split-Path (Split-Path $VisualStudioPath -Parent) -Leaf) -eq '18') { 'Visual Studio 18 2026' } else { 'Visual Studio 17 2022' }
$arguments = @('--fresh', '-S', "$meshRoot\src", '-B', "$meshRoot\build", '-G', $generator, '-A', 'x64',
    "-DCMAKE_TOOLCHAIN_FILE=$VisualStudioPath\VC\vcpkg\scripts\buildsystems\vcpkg.cmake",
    '-DVCPKG_MANIFEST_MODE=OFF', "-DVCPKG_INSTALLED_DIR=$meshRoot\installed", '-DVCPKG_TARGET_TRIPLET=x64-windows-static', '-DVCPKG_HOST_TRIPLET=x64-windows-static',
    '-DENABLE_SHARED=ON', '-DENABLE_STATIC=OFF', '-DBUILD_TOXAV=OFF', '-DDHT_BOOTSTRAP=OFF', '-DBOOTSTRAP_DAEMON=OFF', '-DUNITTEST=OFF', '-DAUTOTEST=OFF', '-DBUILD_MISC_TESTS=OFF',
    '-DCMAKE_MSVC_RUNTIME_LIBRARY=MultiThreaded', '-DMSVC_STATIC_SODIUM=ON', '-DCMAKE_WINDOWS_EXPORT_ALL_SYMBOLS=ON', '-DFLAT_OUTPUT_STRUCTURE=ON')
Invoke-NativeTool $cmake $arguments
Invoke-NativeTool $cmake @('--build', "$meshRoot\build", '--config', 'Release', '--target', 'toxcore_shared', '--parallel', '4')
Copy-Item -LiteralPath "$meshRoot\build\bin\Release\toxcore.dll" -Destination "$meshRoot\dist\libtoxcore.dll" -Force
Get-FileHash -LiteralPath "$meshRoot\dist\libtoxcore.dll" -Algorithm SHA256
