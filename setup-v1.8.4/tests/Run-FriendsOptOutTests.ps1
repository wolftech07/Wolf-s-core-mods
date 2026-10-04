param([Parameter(Mandatory=$true)][string]$GameFolder)
$ErrorActionPreference = 'Stop'
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('TavernFriendsOff-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($testRoot) | Out-Null
$managed = Join-Path $GameFolder 'A Township Tale_Data\Managed'
$json = Join-Path $managed 'Newtonsoft.Json.dll'
Copy-Item -LiteralPath $json -Destination $testRoot
$harness = @'
using System;
using System.Reflection;
using TavernNativeMenu;
namespace TavernNativeMenu {
    internal static class MeshSocialTransport {
        internal static int Starts;
        internal static void Initialize() { Starts++; }
        internal static void Tick() { }
    }
}
internal static class Checks {
    public static void Main() {
        MeshSocialClient.Initialize("not-an-installed-game", "Alice", false);
        if (MeshSocialClient.Configured || MeshSocialClient.Connected) throw new Exception("disabled friends became active");
        if (MeshSocialTransport.Starts != 1) throw new Exception("tablet transport was not initialized");
        if (!MeshSocialClient.LastError.Contains("disabled")) throw new Exception("disabled status is not explicit");
        if (typeof(MeshSocialClient).GetField("process", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null) != null) throw new Exception("disabled friends launched a process");
        MeshSocialClient.Shutdown();
        Console.WriteLine("PASS disabled friends do not start a helper, tablet transport remains available, and shutdown succeeds.");
    }
}
'@
$checks = Join-Path $testRoot 'Checks.cs'
[IO.File]::WriteAllText($checks,$harness)
$exe = Join-Path $testRoot 'Checks.exe'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
& $compiler /nologo /target:exe /langversion:5 "/out:$exe" "/reference:$json" ('/reference:' + (Join-Path $managed 'netstandard.dll')) $checks (Join-Path $PSScriptRoot '..\mod-src\MeshSocialClient.cs') (Join-Path $PSScriptRoot '..\mod-src\LauncherProfile.cs')
if ($LASTEXITCODE -ne 0) { throw 'Friends opt-out checks did not compile.' }
& $exe
if ($LASTEXITCODE -ne 0) { throw 'Friends opt-out checks failed.' }
