$ErrorActionPreference = 'Stop'
$source = Get-Content -LiteralPath (Join-Path $PSScriptRoot '..\mod-src\NativeSocialTablet.cs') -Raw
$start = $source.IndexOf('internal sealed class TabletMuteState')
if ($start -lt 0) { throw 'Production tablet mute state was not found.' }
# Compile the exact production class with a fake voice provider. The rest of
# NativeSocialTablet requires Unity; this lifecycle state deliberately does not.
$production = "using System; using System.Collections.Generic; using System.Linq; namespace TavernNativeMenu {`n" + $source.Substring($start)
$harness = @'
using System;
using System.Collections.Generic;
using TavernNativeMenu;
internal static class TabletMuteChecks {
    private sealed class Voice {
        internal readonly Dictionary<int, bool> Values = new Dictionary<int, bool>();
        internal int Writes;
        internal bool Fail;
        internal bool Get(int id) { if (Fail) throw new InvalidOperationException(); bool value; return Values.TryGetValue(id, out value) && value; }
        internal void Set(int id, bool value) { if (Fail) throw new InvalidOperationException(); Values[id] = value; Writes++; }
    }
    private static int count;
    private static void Check(bool pass, string text) { if (!pass) throw new Exception(text); count++; }
    public static void Main() {
        var state = new TabletMuteState(); var voice = new Voice();
        var blocked = new Dictionary<int, bool?> { { 7, null } };
        var empty = new Dictionary<int, bool?>();
        state.Synchronize("server-a", voice, voice.Get, voice.Set, blocked);
        Check(voice.Get(7) && voice.Writes == 1, "block acquires voice mute");
        Check(state.PreviousState(7) == false, "unmuted baseline is remembered");
        state.Synchronize("server-a", voice, voice.Get, voice.Set, blocked);
        Check(voice.Writes == 1, "periodic sync avoids redundant persistent writes");
        state.Synchronize("server-b", voice, voice.Get, voice.Set, empty);
        Check(!voice.Get(7), "same numeric ID on another server is restored");
        state.Synchronize("server-a", voice, voice.Get, voice.Set, blocked);
        state.Synchronize(null, voice, voice.Get, voice.Set, empty);
        Check(!voice.Get(7), "disconnect restores the baseline");
        voice.Set(7, true);
        state.Synchronize("server-a", voice, voice.Get, voice.Set, blocked);
        state.Synchronize("server-a", voice, voice.Get, voice.Set, empty);
        Check(voice.Get(7), "manual mute survives unblock");
        voice.Set(7, false);
        state.Synchronize("server-a", voice, voice.Get, voice.Set, blocked);
        state.Synchronize("server-a", voice, voice.Get, voice.Set, empty);
        Check(!voice.Get(7), "removed peer mapping releases its forced mute");
        voice.Set(7, true);
        blocked[7] = false;
        state.Synchronize("server-a", voice, voice.Get, voice.Set, blocked);
        state.Restore();
        Check(!voice.Get(7), "saved pre-block state restores a mute left by an interrupted session");
        blocked[7] = true;
        state.Synchronize("server-a", voice, voice.Get, voice.Set, blocked);
        state.Restore();
        Check(voice.Get(7), "saved manual mute is preserved");
        voice.Set(7, false); blocked[7] = null;
        state.Synchronize("server-a", voice, voice.Get, voice.Set, blocked);
        var replacement = new Voice();
        state.Synchronize("server-a", replacement, replacement.Get, replacement.Set, blocked);
        Check(!voice.Get(7) && replacement.Get(7), "provider replacement restores old and acquires new mute");
        state.Restore();
        Check(!replacement.Get(7), "shutdown restores owned mutes");
        state.Synchronize("server-a", voice, voice.Get, voice.Set, blocked);
        voice.Fail = true; state.Restore();
        Check(state.PreviousState(7) == false, "failed restoration stays pending");
        voice.Fail = false; state.Restore();
        Check(!voice.Get(7) && state.PreviousState(7) == null, "failed restoration succeeds on retry");
        Console.WriteLine("PASS " + count + " tablet mute lifecycle checks against production state class.");
    }
}
'@
$testFolder = Join-Path ([IO.Path]::GetTempPath()) ('tavern-tablet-mutes-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($testFolder) | Out-Null
$productionPath = Join-Path $testFolder 'Production.cs'
$harnessPath = Join-Path $testFolder 'Checks.cs'
$exePath = Join-Path $testFolder 'Checks.exe'
[IO.File]::WriteAllText($productionPath, $production)
[IO.File]::WriteAllText($harnessPath, $harness)
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
& $compiler /nologo /langversion:5 "/out:$exePath" $productionPath $harnessPath
if ($LASTEXITCODE -ne 0) { throw 'Tablet mute lifecycle tests did not compile.' }
& $exePath
if ($LASTEXITCODE -ne 0) { throw 'Tablet mute lifecycle tests failed.' }
