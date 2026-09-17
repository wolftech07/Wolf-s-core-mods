using System;
using System.Linq;
using System.Reflection;
using Mono.Cecil;
using Mono.Cecil.Cil;
using TavernNativeMenu;

internal static class JoinVisibilityTests
{
    static int passed;
    static void Check(bool value, string label) { if (!value) throw new Exception(label); passed++; Console.WriteLine("PASS " + label); }
    static int Main(string[] paths)
    {
        try { Run(paths); return 0; }
        catch (Exception error) { Console.Error.WriteLine(error.Message); return 1; }
    }
    static void Run(string[] paths)
    {
        var serverAction = new ActionOrb();
        var unrelatedAction = new ActionOrb();
        var selection = new ServerSelectionOrb(serverAction);
        JoinVisibility.Configure(selection);
        Check(!serverAction.FadeEnabled && unrelatedAction.FadeEnabled, "Only the selected server orb loses its preview fade");
        JoinVisibility.Configure(selection);
        JoinVisibility.Configure(null);
        JoinVisibility.Configure(new ServerSelectionOrb(null));
        Check(!serverAction.FadeEnabled, "Repeated setup and missing orbs are safe");
        var fader = new PlayerScreenFader { Alpha = 0.65f };
        PlayerController.Current = new PlayerController { ScreenFader = fader };
        JoinVisibility.RestoreMenu();
        Check(fader.Alpha == 0 && fader.Calls == 1, "Starting authentication clears a lingering orb fade");
        fader.Alpha = 0.75f;
        JoinVisibility.RestoreMenu();
        Check(fader.Alpha == 0 && fader.Calls == 2, "Cancellation cleanup restores menu visibility");
        fader.Locked = true;
        fader.Alpha = 1;
        JoinVisibility.RestoreMenu();
        Check(fader.Alpha == 1, "Recovery respects native loading locks");
        GameModeManager.CurrentMode = new object();
        int calls = fader.Calls;
        JoinVisibility.RestoreMenu();
        Check(fader.Calls == calls, "Recovery cannot clear a running game's fade");
        GameModeManager.CurrentMode = null;
        PlayerController.Current = null;
        JoinVisibility.RestoreMenu();
        PlayerController.Current = new PlayerController();
        JoinVisibility.RestoreMenu();
        Check(true, "Missing player and missing fader are safe");
        foreach (string path in paths)
        using (var module = ModuleDefinition.ReadModule(path))
        {
            var fade = module.GetType("ActionOrb").Methods.Single(m => m.Name == "StartScreenFade");
            var instructions = fade.Body.Instructions;
            int fieldIndex = -1;
            for (int i = 0; i < instructions.Count; i++)
                if (instructions[i].OpCode == OpCodes.Ldfld && ((FieldReference)instructions[i].Operand).Name == "hasScreenFade") fieldIndex = i;
            Instruction branch = fieldIndex < 0 ? null : instructions[fieldIndex + 1];
            bool returnsWhenFalse = branch != null &&
                (((branch.OpCode == OpCodes.Brtrue || branch.OpCode == OpCodes.Brtrue_S) && instructions[fieldIndex + 2].OpCode == OpCodes.Ret) ||
                ((branch.OpCode == OpCodes.Brfalse || branch.OpCode == OpCodes.Brfalse_S) && ((Instruction)branch.Operand).OpCode == OpCodes.Ret));
            Check(returnsWhenFalse, "Native orb skips its fade when disabled: " + path);
            var loop = module.GetType("ActionOrb").NestedTypes.SelectMany(t => t.Methods).First(m => m.HasBody && m.Body.Instructions.Any(i => i.Operand is MethodReference && ((MethodReference)i.Operand).Name == "StopInteract"));
            Check(!loop.Body.Instructions.Any(i => i.Operand is FieldReference && ((FieldReference)i.Operand).Name == "hasScreenFade"), "Disabling fade does not disable the native held-orb join timer");
            Check(loop.Body.Instructions.Any(i => i.Operand is FieldReference && ((FieldReference)i.Operand).Name == "OnValidLetGo"), "Native orb still invokes its valid-release join event");
            var startup = module.GetType("VrMainMenu").NestedTypes.SelectMany(t => t.Methods).Where(m => m.HasBody);
            Check(startup.Any(m => m.Body.Instructions.Any(i => i.Operand is MethodReference && ((MethodReference)i.Operand).Name == "FadeToBlack")), "Native approved-join loading fade remains present");
        }
        Console.WriteLine("Join visibility: " + passed + " checks passed. Headset rendering still requires VR verification.");
    }
}
public class ActionOrb
{
    private bool hasScreenFade = true;
    public bool FadeEnabled { get { return hasScreenFade; } }
}
public class ServerSelectionOrb
{
#pragma warning disable 0414
    private ActionOrb actionOrb;
#pragma warning restore 0414
    public ServerSelectionOrb(ActionOrb value) { actionOrb = value; }
}
public static class GameModeManager { public static object CurrentMode; }
public class PlayerController { public static PlayerController Current; public PlayerScreenFader ScreenFader; }
public class PlayerScreenFader
{
    public bool Locked;
    public float Alpha;
    public int Calls;
    public void FadeInIfNotLocked(float duration) { Calls++; if (!Locked) Alpha = 0; }
}
namespace TavernNativeMenu
{
    internal static class MenuMod
    {
        internal static T Get<T>(object obj, string name) { return (T)obj.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(obj); }
        internal static void Set(object obj, string name, object value) { obj.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(obj, value); }
    }
}
