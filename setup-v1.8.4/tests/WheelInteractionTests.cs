// The real WheelInteraction.cs is compiled unchanged. Native method IL is read
// from each supplied game DLL, transformed, and executed against a small fake
// Unity surface. Neither the game nor its assembly code is loaded or launched.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Mono.Cecil;
using Instruction = Mono.Cecil.Cil.Instruction;
using OpCode = System.Reflection.Emit.OpCode;
using OpCodes = System.Reflection.Emit.OpCodes;

internal static class WheelInteractionTests
{
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    private static readonly Dictionary<string, OpCode> Opcodes = typeof(OpCodes).GetFields(BindingFlags.Static | BindingFlags.Public).Where(x => x.FieldType == typeof(OpCode)).Select(x => (OpCode)x.GetValue(null)).ToDictionary(x => x.Name);
    private static int passed;
    private static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static void Run(string name, Action test) { test(); passed++; Console.WriteLine("PASS " + name); }

    public static int Main(string[] args)
    {
        try
        {
            foreach (string file in args)
            using (ModuleDefinition module = ModuleDefinition.ReadModule(file))
            {
                Console.WriteLine("Assembly: " + file);
                var menu = module.GetType("ServerSelectionMenu");
                Assert(menu != null, "Native menu type was not found.");
                MethodDefinition switcher = menu.Methods.Single(x => x.Name == "SwitchToBoard" && x.Parameters[0].ParameterType.Name == "ServerBoardType");
                MethodDefinition setup = menu.Methods.Single(x => x.Name == "Setup");
                var unpatched = Emit<Action<ServerSelectionMenu, ServerBoardType>>(switcher, false);
                var repaired = Emit<Action<ServerSelectionMenu, ServerBoardType>>(switcher, true);
                var initialize = Emit<Action<ServerSelectionMenu>>(setup, false);
                ServerSelectionMenu.RopeSetup = Emit<Action<ServerSelectionMenu, RopeGrab>>(menu.Methods.Single(x => x.Name == "<Setup>b__25_0"), false);
                ServerSelectionMenu.RopeScroll = Emit<Action<ServerSelectionMenu, float, float>>(menu.Methods.Single(x => x.Name == "ScrollServerList"), false);
                ServerSelectionMenu.WheelScroll = Emit<Action<ServerSelectionMenu, float, float>>(menu.Methods.Single(x => x.Name == "ScrollServerListFromWheel"), false);
                Run("reproduces original missing-lever failure before input subscriptions", delegate
                {
                    ServerSelectionMenu.Switch = unpatched;
                    var subject = Fixture(new SelectionLever[4], ServerBoardType.PublicServers);
                    bool threw = false;
                    try { initialize(subject); } catch (NullReferenceException) { threw = true; }
                    Assert(threw, "Unmodified native setup did not expose the regression.");
                    Assert(subject.wheel.Subscribers == 0 && subject.ropeGrabs.All(x => x.Subscribers == 0), "Baseline failure unexpectedly completed native subscriptions.");
                });
                ServerSelectionMenu.Switch = repaired;
                Run("missing optional lever still enables native wheel and both ropes", delegate
                {
                    var subject = Fixture(new SelectionLever[4], ServerBoardType.PublicServers);
                    initialize(subject);
                    AssertInputs(subject);
                    subject.wheel.Turn(3, 5);
                    subject.ropeGrabs[0].Pull(1, 4);
                    subject.ropeGrabs[1].Pull(10, 8);
                    Assert(subject.currentBoard.ScrollValues.SequenceEqual(new[] { 4f, 9f, -6f }), "Native scroll deltas or wheel/rope multipliers changed.");
                    Assert(subject.headingText.Text == "Board 0" && subject.SelectedChanges == 1, "Native board display/selection synchronization was skipped.");
                });
                Run("null lever array leaves native startup usable", delegate { var subject = Fixture(null, ServerBoardType.PublicServers); initialize(subject); AssertInputs(subject); });
                Run("short lever array leaves native startup usable", delegate { var subject = Fixture(new SelectionLever[1], ServerBoardType.DiscoverServers); initialize(subject); AssertInputs(subject); });
                Run("negative optional lever index is ignored", delegate { var subject = Fixture(new SelectionLever[1], (ServerBoardType)(-1)); initialize(subject); AssertInputs(subject); });
                Run("destroyed Unity lever is treated as absent", delegate
                {
                    var lever = new SelectionLever { Destroyed = true };
                    var subject = Fixture(new[] { lever }, ServerBoardType.PublicServers);
                    initialize(subject); AssertInputs(subject);
                    Assert(lever.SetCalls == 0, "A destroyed Unity lever was invoked.");
                });
                Run("present physical lever still synchronizes to index one", delegate
                {
                    var lever = new SelectionLever();
                    var subject = Fixture(new[] { lever }, ServerBoardType.PublicServers);
                    initialize(subject); AssertInputs(subject);
                    Assert(lever.SetCalls == 1 && lever.LastIndex == 1, "The actual lever visual synchronization was lost.");
                });
                Run("native setup removes old handlers before subscribing again", delegate
                {
                    var subject = Fixture(null, ServerBoardType.PublicServers);
                    initialize(subject); initialize(subject); AssertInputs(subject);
                    subject.wheel.Turn(0, 1); subject.ropeGrabs[0].Pull(0, 1);
                    Assert(subject.currentBoard.ScrollValues.SequenceEqual(new[] { 2f, 3f }), "Repeated setup duplicated an input handler.");
                });
                Run("switching boards retains native selection cleanup and routes input to new board", delegate
                {
                    var subject = Fixture(null, ServerBoardType.PublicServers);
                    initialize(subject);
                    ServerBoard first = subject.currentBoard;
                    subject.SwitchToBoard(ServerBoardType.DiscoverServers);
                    Assert(!first.Active && first.SelectedSubscribers == 0, "Old board remained active/subscribed.");
                    Assert(subject.currentBoard.Active && subject.currentBoard.SelectedSubscribers == 1, "New board was not activated/subscribed.");
                    subject.wheel.Turn(0, 2);
                    Assert(first.ScrollValues.Count == 0 && subject.currentBoard.ScrollValues.SequenceEqual(new[] { 4f }), "Input did not follow current board.");
                });
                Run("transpiler rejects missing or ambiguous native call sites", delegate
                {
                    ExpectLayoutFailure(new List<CodeInstruction> { new CodeInstruction(OpCodes.Ret) });
                    var pattern = new[] {
                        new CodeInstruction(OpCodes.Ldfld, typeof(ServerSelectionMenu).GetField("boardLevers")),
                        new CodeInstruction(OpCodes.Ldarg_1), new CodeInstruction(OpCodes.Ldelem_Ref),
                        new CodeInstruction(OpCodes.Ldc_I4_1), new CodeInstruction(OpCodes.Callvirt, typeof(SelectionLever).GetMethod("SetLeverToIndex")) };
                    ExpectLayoutFailure(pattern.Concat(pattern.Select(x => new CodeInstruction(x.opcode, x.operand))).ToList());
                });
            }
            Console.WriteLine("PASS " + passed + " native IL wheel/rope regression checks across " + args.Length + " supplied assemblies.");
            Console.WriteLine("Limit: fake Unity objects verify native control flow and delegates, not headset physics or runtime patch ordering.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }

    private static ServerSelectionMenu Fixture(SelectionLever[] levers, ServerBoardType starting)
    {
        var menu = new ServerSelectionMenu { boardLevers = levers, startingBoard = starting };
        foreach (int value in new[] { -1, 0, 1, 2, 3 }) menu.boardsByType[(ServerBoardType)value] = new ServerBoard { Type = (ServerBoardType)value, HeadingText = "Board " + value };
        return menu;
    }
    private static void AssertInputs(ServerSelectionMenu menu)
    {
        Assert(menu.wheel.Subscribers == 1 && menu.ropeGrabs.All(x => x.Subscribers == 1), "Native wheel/rope callbacks were not installed exactly once.");
        Assert(!menu.waitingForLoginText.gameObject.Active, "Native login placeholder remains visible.");
    }
    private static List<CodeInstruction> Transform(List<CodeInstruction> codes)
    {
        var method = typeof(TavernNativeMenu.WheelInteraction).GetMethod("GuardOptionalLever", Flags);
        return ((IEnumerable<CodeInstruction>)method.Invoke(null, new object[] { codes })).ToList();
    }
    private static void ExpectLayoutFailure(List<CodeInstruction> codes)
    {
        try { Transform(codes); }
        catch (TargetInvocationException error) { if (error.InnerException is InvalidOperationException) return; throw; }
        throw new Exception("Unknown native layout was accepted.");
    }
    private static T Emit<T>(MethodDefinition method, bool repair)
    {
        var signature = typeof(T).GetMethod("Invoke");
        var dynamic = new DynamicMethod(method.Name + (repair ? "Repaired" : "Original"), signature.ReturnType, signature.GetParameters().Select(x => x.ParameterType).ToArray(), typeof(WheelInteractionTests), true);
        ILGenerator il = dynamic.GetILGenerator();
        foreach (var variable in method.Body.Variables) il.DeclareLocal(MapType(variable.VariableType));
        var native = method.Body.Instructions.ToList();
        var labels = native.ToDictionary(x => x, x => il.DefineLabel());
        var instructions = native.Select(x => new CodeInstruction(Opcodes[x.OpCode.Name], MapOperand(x.Operand, labels))).ToList();
        for (int i = 0; i < native.Count; i++) instructions[i].labels.Add(labels[native[i]]);
        var priorCodes = instructions.Select(x => x.opcode).ToArray();
        var priorOperands = instructions.Select(x => x.operand).ToArray();
        if (repair)
        {
            instructions = Transform(instructions);
            Assert(instructions.Count == native.Count, "Transpiler changed method length.");
            Assert(instructions.Where((x, i) => x.opcode != priorCodes[i] || !Equals(x.operand, priorOperands[i])).Count() == 2, "Transpiler changed unrelated native instructions.");
            Assert(instructions.Select((x, i) => x.labels.Count == 1 && x.labels[0].Equals(labels[native[i]])).All(x => x), "Transpiler dropped native branch labels.");
            passed++; Console.WriteLine("PASS exact native call site transformed with all branch labels preserved");
        }
        foreach (CodeInstruction instruction in instructions)
        {
            foreach (Label label in instruction.labels) il.MarkLabel(label);
            object operand = instruction.operand;
            if (operand == null) il.Emit(instruction.opcode);
            else if (operand is FieldInfo) il.Emit(instruction.opcode, (FieldInfo)operand);
            else if (operand is MethodInfo) il.Emit(instruction.opcode, (MethodInfo)operand);
            else if (operand is ConstructorInfo) il.Emit(instruction.opcode, (ConstructorInfo)operand);
            else if (operand is Label) il.Emit(instruction.opcode, (Label)operand);
            else if (operand is int) il.Emit(instruction.opcode, (int)operand);
            else if (operand is sbyte) il.Emit(instruction.opcode, (sbyte)operand);
            else throw new NotSupportedException("Unexpected native operand " + operand.GetType() + ": " + operand);
        }
        return (T)(object)dynamic.CreateDelegate(typeof(T));
    }
    private static object MapOperand(object value, Dictionary<Instruction, Label> labels)
    {
        if (value is Instruction) return labels[(Instruction)value];
        if (value is FieldReference)
        {
            var field = (FieldReference)value;
            return MapType(field.DeclaringType).GetField(field.Name, Flags) ?? throwMissing(field.FullName);
        }
        if (value is MethodReference)
        {
            var method = (MethodReference)value;
            Type declaring = MapType(method.DeclaringType);
            string name = method.Name == "<Setup>b__25_0" ? "SetupRope" : method.Name;
            if (name == ".ctor") return declaring.GetConstructors(Flags).Single(x => x.GetParameters().Length == method.Parameters.Count);
            var generic = method as GenericInstanceMethod;
            MethodInfo candidate = declaring.GetMethods(Flags).Single(x => x.Name == name && x.GetParameters().Length == method.Parameters.Count);
            return generic == null ? candidate : candidate.MakeGenericMethod(generic.GenericArguments.Select(MapType).ToArray());
        }
        return value;
    }
    private static FieldInfo throwMissing(string name) { throw new MissingFieldException(name); }
    private static Type MapType(TypeReference native)
    {
        var generic = native as GenericInstanceType;
        if (generic != null) return MapType(generic.ElementType).MakeGenericType(generic.GenericArguments.Select(MapType).ToArray());
        var array = native as ArrayType;
        if (array != null) return MapType(array.ElementType).MakeArrayType();
        Type type = typeof(WheelInteractionTests).Assembly.GetType(native.FullName) ?? Type.GetType(native.FullName);
        if (type == null) throw new TypeLoadException(native.FullName);
        return type;
    }
}

namespace HarmonyLib
{
    // Minimal API surface used by the production transpiler; the instructions
    // emitted and run above come from the actual native assemblies, not stubs.
    public sealed class CodeInstruction
    {
        public OpCode opcode; public object operand; public List<Label> labels = new List<Label>();
        public CodeInstruction(OpCode code, object value = null) { opcode = code; operand = value; }
    }
    public static class AccessTools
    {
        private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        public static FieldInfo Field(Type type, string name) { return type.GetField(name, Flags); }
        public static MethodInfo Method(Type type, string name, Type[] args = null) { return args == null ? type.GetMethod(name, Flags) : type.GetMethod(name, Flags, null, args, null); }
    }
    public sealed class HarmonyMethod { public HarmonyMethod(Type type, string name) { } }
    public sealed class Harmony { public void Patch(MethodBase method, HarmonyMethod transpiler = null) { } }
}
namespace UnityEngine
{
    public class Object
    {
        public bool Destroyed;
        public static bool operator ==(Object a, Object b) { return (ReferenceEquals(a, null) || a.Destroyed) == (ReferenceEquals(b, null) || b.Destroyed) && ((ReferenceEquals(a, null) || a.Destroyed) || ReferenceEquals(a, b)); }
        public static bool operator !=(Object a, Object b) { return !(a == b); }
        public override bool Equals(object other) { return ReferenceEquals(this, other); }
        public override int GetHashCode() { return base.GetHashCode(); }
    }
    public class Component : Object { public GameObject gameObject { get; private set; } public Component() { gameObject = new GameObject(); } }
    public sealed class GameObject : Object { public bool Active = true; public void SetActive(bool active) { Active = active; } }
}
public enum ServerBoardType { PublicServers = 0, MyServers = 1, OpenServers = 2, DiscoverServers = 3 }
public sealed class TextRenderer : UnityEngine.Component { public string Text { get; set; } }
public sealed class SelectionLever : UnityEngine.Object
{
    public int SetCalls, LastIndex;
    public void SetLeverToIndex(int index) { if (Destroyed) throw new InvalidOperationException("Destroyed lever invoked."); SetCalls++; LastIndex = index; }
}
public sealed class ServerElement { }
public sealed class ServerSelectionDisplay { public ServerElement CurrentSelection { get; set; } }
public delegate void SelectedElementChanged(ServerElement before, ServerElement after);
public sealed class ServerBoard : UnityEngine.Object
{
    public ServerBoardType Type { get; set; }
    public string HeadingText { get; set; }
    public bool Active;
    public ServerSelectionDisplay Display { get; private set; }
    public List<float> ScrollValues = new List<float>();
    public event SelectedElementChanged SelectedChanged;
    public event Action OnRefreshFinished;
    public int SelectedSubscribers { get { return SelectedChanged == null ? 0 : SelectedChanged.GetInvocationList().Length; } }
    public ServerBoard() { Display = new ServerSelectionDisplay(); }
    public void SetActive(bool active) { Active = active; }
    public void UpdateValue(float value) { ScrollValues.Add(value); }
    public void CompleteRefresh() { if (OnRefreshFinished != null) OnRefreshFinished(); }
}
public sealed class CaptainsWheel
{
    public event Action<float, float> ValueChanged;
    public int Subscribers { get { return ValueChanged == null ? 0 : ValueChanged.GetInvocationList().Length; } }
    public void Turn(float before, float after) { if (ValueChanged != null) ValueChanged(before, after); }
}
public sealed class RopeGrab
{
    public event Action<float, float> ProgressChanged;
    public int Subscribers { get { return ProgressChanged == null ? 0 : ProgressChanged.GetInvocationList().Length; } }
    public void Pull(float before, float after) { if (ProgressChanged != null) ProgressChanged(before, after); }
}
public static class IEnumerableExtensions { public static void ForEach<T>(IEnumerable<T> values, Action<T> action) { foreach (T item in values) action(item); } }
public sealed class ServerSelectionMenu
{
    public static Action<ServerSelectionMenu, ServerBoardType> Switch;
    public static Action<ServerSelectionMenu, RopeGrab> RopeSetup;
    public static Action<ServerSelectionMenu, float, float> RopeScroll, WheelScroll;
    public SelectionLever[] boardLevers;
    public Dictionary<ServerBoardType, ServerBoard> boardsByType = new Dictionary<ServerBoardType, ServerBoard>();
    public ServerBoard currentBoard;
    public ServerBoard CurrentBoard { get { return currentBoard; } }
    public ServerBoardType startingBoard;
    public TextRenderer waitingForLoginText = new TextRenderer(), headingText = new TextRenderer();
    public CaptainsWheel wheel = new CaptainsWheel();
    public RopeGrab[] ropeGrabs = { new RopeGrab(), new RopeGrab() };
    public float ropeSpeedMultiplier = 3f, wheelSpeedMultiplier = 2f;
    public int SelectedChanges;
    public void SwitchToBoard(ServerBoardType board) { Switch(this, board); }
    public void OnSelectedChanged(ServerElement before, ServerElement after) { SelectedChanges++; }
    public void CheckForEmpty() { }
    public void SetupRope(RopeGrab grab) { RopeSetup(this, grab); }
    public void ScrollServerList(float before, float after) { RopeScroll(this, before, after); }
    public void ScrollServerListFromWheel(float before, float after) { WheelScroll(this, before, after); }
}
