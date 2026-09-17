// Compiles the production NativePrompt.cs, not a copied implementation.
// The fake keyboard below preserves the relevant Input / SetupNewUser / Hide /
// ClearFocus ordering from research/game/TouchScreenKeyboard.cs. This is a
// deterministic logic harness; it does not simulate Unity physics or VR input.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using TavernNativeMenu;
using UnityEngine;

internal static class NativePromptTests
{
    private static int passed;
    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
    private static void Run(string name, Action body)
    {
        Reset();
        try { body(); passed++; Console.WriteLine("PASS " + name); }
        finally { if (NativePrompt.Active) { UnityEngine.Object.Destroy(UnityEngine.Object.FindObjectOfType<ServerSelectionMenu>()); NativePrompt.CheckLifetime(); } }
    }
    private static void Reset()
    {
        UnityEngine.Object.All.Clear();
        PopupManager.Reset();
        MelonLoader.MelonLogger.Lines.Clear();
        new ServerSelectionMenu();
        PlayerController.Current = new PlayerController { Head = new Transform() };
        TouchScreenKeyboard.Instance = new TouchScreenKeyboard();
        TouchScreenKeyboard.Instance.gameObject.SetActive(false);
    }
    private static Task<string> Open(bool secret)
    {
        var task = NativePrompt.Ask("Join private server", "Enter password", secret);
        NativePrompt.CheckLifetime();
        Assert(NativePrompt.Active && TouchScreenKeyboard.Instance.CurrentUser is NativePrompt, "Prompt did not take keyboard focus.");
        return task;
    }
    private static void Type(string text)
    {
        foreach (char letter in text) TouchScreenKeyboard.Instance.TypeCustom(letter.ToString());
    }
    private static bool ContainsSecret(IEnumerable<string> values, string secret) { return values.Any(x => x != null && x.Contains(secret)); }
    public static int Main()
    {
        try
        {
            Run("Cancel moves right and touch map follows without accumulating offset", delegate
            {
                var task = Open(false);
                var keyboard = TouchScreenKeyboard.Instance;
                float x = keyboard.InstantiatedButtons[0].transform.localPosition.x;
                Assert(x > 0 && x < 0.02f && keyboard.MappedPositions[0].x == x, "Rightward nudge must update touch mapping too.");
                keyboard.EventSetUpNewKeyboard(1);
                NativePrompt.CheckLifetime();
                Assert(keyboard.InstantiatedButtons[0].transform.localPosition.x == x && keyboard.MappedPositions[0].x == x, "Layout change accumulated or lost Cancel's offset.");
                keyboard.KeyPressed(KeyCode.Escape);
                NativePrompt.CheckLifetime();
                Assert(task.Result == null, "Shifted Cancel must still cancel.");
            });
            Run("artwork rules target sponsor and Vivox assets without touching other menu content", delegate
            {
                foreach (string name in new[] { "ScreenLogos", "ScreenQld", "ScreenNSW", "ScreenQLDLogo", "Vivox Logo", "ScreenLogos (Instance)" })
                    Assert(MenuArtworkRules.Remove(name), "Missed sponsor asset " + name);
                foreach (string name in new[] { "DiscordLogo", "Township Logo", "LogoBoard", "Server List", "VivoxController", "The Modded Tavern - menu artwork" })
                    Assert(!MenuArtworkRules.Remove(name), "Overbroad artwork match " + name);
                Assert(MenuArtworkRules.ReplaceBadge("Alta Logo Ridged") && !MenuArtworkRules.ReplaceBadge("AltaAccountName"), "Badge replacement scope changed.");
                Assert(MenuArtworkRules.RemoveWholeBoard("LogoBoard", "Credits", "Static Models"), "Sponsor board backing was not targeted.");
                Assert(MenuArtworkRules.RemoveWholeBoard("ScreenLogos", "Credits", "Static Models"), "White sponsor quad group was not targeted.");
                Assert(!MenuArtworkRules.RemoveWholeBoard("LogoBoard", "Server Filter Board", "Menu") &&
                    !MenuArtworkRules.RemoveWholeBoard("DiscordLogo", "Credits", "Static Models"), "Whole-board removal would hide unrelated menu content.");
            });
            Run("visible Cancel defers cleanup and restores existing Escape binding", delegate
            {
                var keyboard = TouchScreenKeyboard.Instance;
                var original = new FunctionKeyDefinition();
                keyboard.MappedFunctionKeys[KeyCode.Escape] = original;
                var task = Open(true);
                Type("private-password");
                Assert(keyboard.MappedFunctionKeys[KeyCode.Escape].text == "Cancel" && keyboard.InstantiatedButtons.Count == 1, "Missing visible Cancel key.");
                keyboard.KeyPressed(KeyCode.Escape);
                Assert(!task.IsCompleted, "Cancel must not mutate the native key list during its input iteration.");
                NativePrompt.CheckLifetime();
                Assert(task.IsCompleted && task.Result == null && !NativePrompt.Active, "Cancel must discard input and release the prompt.");
                Assert(ReferenceEquals(keyboard.MappedFunctionKeys[KeyCode.Escape], original) && keyboard.InstantiatedButtons.Count == 0, "Cancel must restore original bindings and remove its key.");
            });
            Run("Cancel returns after keyboard layout changes", delegate
            {
                var task = Open(false);
                var keyboard = TouchScreenKeyboard.Instance;
                keyboard.EventSetUpNewKeyboard(1);
                NativePrompt.CheckLifetime();
                Assert(keyboard.InstantiatedButtons.Count == 1, "Layout change lost the Cancel key.");
                keyboard.KeyPressed(KeyCode.Escape);
                NativePrompt.CheckLifetime();
                Assert(task.Result == null && !keyboard.MappedFunctionKeys.ContainsKey(KeyCode.Escape), "Cancel binding leaked.");
            });
            Run("popup callback waits for next frame before borrowing keyboard", delegate
            {
                var keyboard = TouchScreenKeyboard.Instance;
                bool seenCallback = false;
                PopupManager.BeforeAssignCurrent = delegate
                {
                    seenCallback = true;
                    Assert(keyboard.CurrentUser == null && !keyboard.gameObject.activeSelf, "Keyboard opened while PopupManager had not assigned CurrentPopup.");
                };
                var task = NativePrompt.Ask("Password", "Type", true);
                Assert(seenCallback && !task.IsCompleted && keyboard.CurrentUser == null, "Ask must leave keyboard opening pending.");
                NativePrompt.CheckLifetime();
                Assert(keyboard.CurrentUser is NativePrompt && keyboard.gameObject.activeSelf, "Next frame must activate keyboard.");
                keyboard.KeyPressed(KeyCode.Return);
                Assert(task.IsCompleted && task.Result == "", "Native Enter must complete the prompt.");
            });
            Run("native key events submit exact password and mask the board", delegate
            {
                var task = Open(true);
                var keyboard = TouchScreenKeyboard.Instance;
                keyboard.KeyPressed(KeyCode.A);
                keyboard.KeyPressed(KeyCode.B);
                keyboard.KeyPressed(KeyCode.Alpha7);
                Assert(PopupManager.CurrentPopup.Message.Contains("***") && !PopupManager.CurrentPopup.Message.Contains("ab7"), "Password must be masked.");
                keyboard.ToggleSecret();
                Assert(PopupManager.CurrentPopup.Message.Contains("ab7"), "Native reveal key must toggle visibility.");
                keyboard.ToggleSecret();
                Assert(!PopupManager.CurrentPopup.Message.Contains("ab7"), "Second reveal press must mask the password.");
                keyboard.KeyPressed(KeyCode.Return);
                Assert(task.IsCompleted && task.Result == "ab7", "Enter did not return actual keyboard input.");
                Assert(!NativePrompt.Active && PopupManager.CurrentPopup == null, "Submit must release modal state.");
            });
            Run("borrowed search handlers and serialized output never receive password", delegate
            {
                var keyboard = TouchScreenKeyboard.Instance;
                var search = new Alta.Meta.UI.InputFieldWithKeyboard { Text = "existing search" };
                search.ShowKeyboard();
                var inputEvents = new List<string>();
                var serialized = new List<string>();
                int oldSubmit = 0, oldToggle = 0;
                keyboard.InputChanged += inputEvents.Add;
                keyboard.AddOutputListener(serialized.Add);
                keyboard.NextPressed += delegate { oldSubmit++; };
                keyboard.ToggleSecretDisplay += delegate { oldToggle++; };
                var originalOutput = keyboard.OutputIdentity;
                var task = Open(true);
                Type("private-123");
                keyboard.ToggleSecret();
                keyboard.KeyPressed(KeyCode.Return);
                Assert(task.Result == "private-123", "Secret was changed during submission.");
                Assert(!ContainsSecret(search.Received, "private") && !ContainsSecret(inputEvents, "private") && !ContainsSecret(serialized, "private"), "Secret leaked to a borrowed handler.");
                Assert(oldSubmit == 0 && oldToggle == 0 && search.Submitted == 0, "Prompt fired an unrelated control's action.");
                Assert(!ContainsSecret(MelonLoader.MelonLogger.Lines, "private-123"), "Secret leaked to logs.");
                Assert(ReferenceEquals(originalOutput, keyboard.OutputIdentity), "Serialized output object was not restored.");
                keyboard.Input = "search after";
                Assert(inputEvents.Last() == "search after" && serialized.Last() == "search after" && search.Text == "search after", "Handlers were not restored after prompt.");
                keyboard.KeyPressed(KeyCode.Return);
                Assert(oldSubmit == 1 && search.Submitted == 1, "Original submit handlers must run exactly once after cleanup.");
            });
            Run("cancel restores previous focus input transform and suggestion state", delegate
            {
                var keyboard = TouchScreenKeyboard.Instance;
                var owner = new KeyboardOwner();
                var parent = new Transform();
                keyboard.transform.SetParent(parent, true);
                keyboard.transform.position = new Vector3(3, 4, 5);
                keyboard.transform.localScale = new Vector3(2, 2, 2);
                keyboard.setupSecretKey = false;
                keyboard.SetupNewUser(owner);
                keyboard.Input = "previous input";
                var visible = new Component();
                var hidden = new Component();
                hidden.gameObject.SetActive(false);
                keyboard.Suggestions[1] = visible;
                keyboard.Suggestions[2] = hidden;
                var task = Open(true);
                Assert(!visible.gameObject.activeSelf && !hidden.gameObject.activeSelf, "Autocomplete must be hidden while typing a secret.");
                Type("cancelled password");
                PopupManager.CurrentPopup.Close();
                Assert(task.IsCompleted && task.Result == null, "Close must cancel, not submit.");
                Assert(ReferenceEquals(owner, keyboard.CurrentUser) && keyboard.Input == "previous input", "Previous owner/input was not restored.");
                Assert(ReferenceEquals(parent, keyboard.transform.parent) && keyboard.transform.position.Equals(new Vector3(3, 4, 5)) && keyboard.transform.localScale.Equals(new Vector3(2, 2, 2)), "Keyboard placement was not restored.");
                Assert(visible.gameObject.activeSelf && !hidden.gameObject.activeSelf && !keyboard.setupSecretKey && keyboard.gameObject.activeSelf, "Keyboard state was not restored.");
            });
            Run("lost focus is deferred through game's unsafe handover ordering", delegate
            {
                var keyboard = TouchScreenKeyboard.Instance;
                var task = Open(true);
                Type("hidden secret");
                var newOwner = new KeyboardOwner();
                // The real implementation dereferences Instance.CurrentUser
                // after LostFocus(). Synchronous cleanup would throw here.
                keyboard.SetupNewUser(newOwner);
                Assert(!task.IsCompleted && ReferenceEquals(keyboard.CurrentUser, newOwner), "Prompt must not hide/restack inside LostFocus.");
                Assert(keyboard.Input == "", "Secret must be cleared before the new owner receives focus.");
                NativePrompt.CheckLifetime();
                Assert(task.IsCompleted && task.Result == null && ReferenceEquals(keyboard.CurrentUser, newOwner), "Deferred cleanup stole the new owner's focus.");
                Assert(keyboard.gameObject.activeSelf, "Deferred cleanup hid the new owner's keyboard.");
                keyboard.Input = "new owner input";
                Assert(newOwner.Received.Last() == "new owner input", "New owner lost its input subscription.");
            });
            Run("ClearFocus cancels without hiding a later owner's keyboard", delegate
            {
                var keyboard = TouchScreenKeyboard.Instance;
                var task = Open(true);
                Type("clear me");
                keyboard.ClearFocus();
                var newOwner = new KeyboardOwner();
                keyboard.SetupNewUser(newOwner);
                NativePrompt.CheckLifetime();
                Assert(task.IsCompleted && task.Result == null && ReferenceEquals(newOwner, keyboard.CurrentUser), "ClearFocus cleanup stole subsequent focus.");
                Assert(keyboard.Input == "", "ClearFocus left a password in the shared keyboard.");
            });
            Run("destroyed server menu cancels and removes prompt handlers", delegate
            {
                var task = Open(true);
                var keyboard = TouchScreenKeyboard.Instance;
                Type("abandoned");
                UnityEngine.Object.Destroy(UnityEngine.Object.FindObjectOfType<ServerSelectionMenu>());
                NativePrompt.CheckLifetime();
                Assert(task.IsCompleted && task.Result == null && !NativePrompt.Active, "Destroyed menu left a hanging prompt.");
                Assert(keyboard.CurrentUser == null && !keyboard.gameObject.activeSelf && keyboard.Input == "", "Destroyed-menu cleanup left active input.");
                keyboard.KeyPressed(KeyCode.Return);
                Assert(task.Result == null, "Stale Enter submitted a cancelled prompt.");
            });
            Run("queued prompt borrows only after its own board has spawned", delegate
            {
                PopupManager.Show("Other board", "Please wait");
                var other = PopupManager.CurrentPopup;
                var task = NativePrompt.Ask("Secret", "Type", true);
                NativePrompt.CheckLifetime();
                Assert(TouchScreenKeyboard.Instance.CurrentUser == null && !task.IsCompleted, "Queued prompt stole focus from an unrelated board.");
                other.Close();
                Assert(TouchScreenKeyboard.Instance.CurrentUser == null, "Queued callback opened synchronously.");
                NativePrompt.CheckLifetime();
                Assert(TouchScreenKeyboard.Instance.CurrentUser is NativePrompt, "Queued prompt failed to open after board spawn.");
            });
            Run("replacing a pending prompt cancels its popup queue entry", delegate
            {
                PopupManager.Show("Other board", "Wait");
                var other = PopupManager.CurrentPopup;
                var first = NativePrompt.Ask("First", "Type", true);
                var second = NativePrompt.Ask("Second", "Type", false);
                Assert(first.IsCompleted && first.Result == null && !second.IsCompleted, "New prompt did not cancel old pending request.");
                other.Close();
                Assert(PopupManager.CurrentPopup.Title == "Second", "Cancelled prompt remained in the popup queue.");
                NativePrompt.CheckLifetime();
            });
            Run("missing local VR player returns cancellation and recoverable error", delegate
            {
                PlayerController.Current = null;
                var task = NativePrompt.Ask("Password", "Type", true);
                NativePrompt.CheckLifetime();
                Assert(task.IsCompleted && task.Result == null && !NativePrompt.Active, "Missing player left task pending.");
                Assert(PopupManager.CurrentPopup != null && PopupManager.CurrentPopup.Title == "Tavern menu keyboard", "Missing player did not show an actionable error.");
            });
            Run("missing keyboard prefab returns cancellation without hanging queue", delegate
            {
                TouchScreenKeyboard.Instance = null;
                var task = NativePrompt.Ask("Password", "Type", true);
                NativePrompt.CheckLifetime();
                Assert(task.IsCompleted && task.Result == null && !NativePrompt.Active, "Missing keyboard left task pending.");
                Assert(PopupManager.CurrentPopup != null && PopupManager.CurrentPopup.Title == "Tavern menu keyboard", "Missing keyboard left popup queue inconsistent.");
            });
            Run("text escaping and keyboard character limits match prompt type", delegate
            {
                var task = Open(false);
                TouchScreenKeyboard.Instance.TypeCustom("<b>name</b>");
                Assert(PopupManager.CurrentPopup.Message.Contains("\u2039b\u203aname\u2039/b\u203a"), "Nonsecret input was not passed through SafeText.");
                TouchScreenKeyboard.Instance.Input = new string('x', 300);
                TouchScreenKeyboard.Instance.KeyPressed(KeyCode.Return);
                Assert(task.Result.Length == 253, "Address prompt did not expose the correct native character limit.");
                task = Open(true);
                TouchScreenKeyboard.Instance.Input = new string('y', 300);
                TouchScreenKeyboard.Instance.KeyPressed(KeyCode.Return);
                Assert(task.Result.Length == 256, "Password prompt did not expose the correct native character limit.");
            });
            Console.WriteLine("NativePrompt: " + passed + " regression scenarios passed. Unity physics, keyboard reach, and headset rendering require VR testing.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine("FAIL " + error); return 1; }
    }
}

internal sealed class KeyboardOwner : IUseVirtualKeyboard
{
    public readonly List<string> Received = new List<string>();
    public int MaxCharactersLimit { get { return -1; } }
    public void HandleKeyboardInput(string value) { Received.Add(value); }
    public void HandleKeyboardSubmit() { }
    public void LostFocus() { }
    public void Next() { }
    public void Previous() { }
    public bool IsShowingSecret() { return false; }
}
public interface IUseVirtualKeyboard
{
    int MaxCharactersLimit { get; }
    void HandleKeyboardInput(string input);
    void HandleKeyboardSubmit();
    void LostFocus();
    void Next();
    void Previous();
    bool IsShowingSecret();
}
public class ServerSelectionMenu : Component { }
public class PlayerController { public static PlayerController Current; public Transform Head; }
public class TouchScreenMenuBase : Component
{
    public readonly List<TouchScreenKeyboardKey> InstantiatedButtons = new List<TouchScreenKeyboardKey>();
    public readonly List<Vector3> MappedPositions = new List<Vector3>();
    public void MapButtons() { MappedPositions.Clear(); foreach (var key in InstantiatedButtons) MappedPositions.Add(key.transform.localPosition); }
    protected readonly Dictionary<int, Component> instantiatedSuggestionKeys = new Dictionary<int, Component>();
    public IDictionary Suggestions { get { return instantiatedSuggestionKeys; } }
}
public class TouchScreenKeyboard : TouchScreenMenuBase
{
    public readonly Dictionary<KeyCode, FunctionKeyDefinition> MappedFunctionKeys = new Dictionary<KeyCode, FunctionKeyDefinition>();
    public TouchScreenKeyboardKey AddExtraKey(VirtualKeyInfo info, bool leftSide, int row) { var key = new TouchScreenKeyboardKey(); InstantiatedButtons.Add(key); return key; }
    private class OutputEvent
    {
        public readonly List<Action<string>> Handlers = new List<Action<string>>();
        public void Invoke(string text) { foreach (var action in Handlers.ToArray()) action(text); }
    }
    private OutputEvent output = new OutputEvent();
    private string input = "";
    public static TouchScreenKeyboard Instance;
    public bool setupSecretKey;
    public IUseVirtualKeyboard CurrentUser { get; private set; }
    public event Action<string> InputChanged;
    public event Action NextPressed;
    public event Action BackPressedOnEmpty;
    public event Action ToggleSecretDisplay;
    public object OutputIdentity { get { return output; } }
    public void AddOutputListener(Action<string> listener) { output.Handlers.Add(listener); }
    public string Input
    {
        get { return input; }
        set
        {
            input = value ?? "";
            if (CurrentUser != null && CurrentUser.MaxCharactersLimit > 0 && input.Length > CurrentUser.MaxCharactersLimit) input = input.Substring(0, CurrentUser.MaxCharactersLimit);
            if (InputChanged != null) InputChanged(input);
            output.Invoke(input);
        }
    }
    public void SetupNewUser(IUseVirtualKeyboard newUser)
    {
        if (CurrentUser != null)
        {
            if (CurrentUser == newUser) return;
            CurrentUser.LostFocus();
            InputChanged -= Instance.CurrentUser.HandleKeyboardInput;
            NextPressed -= Instance.CurrentUser.HandleKeyboardSubmit;
        }
        CurrentUser = newUser;
        if (newUser != null)
        {
            gameObject.SetActive(true);
            InputChanged += newUser.HandleKeyboardInput;
            NextPressed += newUser.HandleKeyboardSubmit;
        }
    }
    public void Hide(IUseVirtualKeyboard user)
    {
        if (CurrentUser == user)
        {
            InputChanged -= CurrentUser.HandleKeyboardInput;
            NextPressed -= CurrentUser.HandleKeyboardSubmit;
            CurrentUser = null;
            gameObject.SetActive(false);
        }
    }
    public void ClearFocus()
    {
        InputChanged = null;
        NextPressed = null;
        BackPressedOnEmpty = null;
        if (CurrentUser != null) { CurrentUser.LostFocus(); CurrentUser = null; }
    }
    public void TypeCustom(string text) { Input += text; }
    public void KeyPressed(KeyCode key)
    {
        FunctionKeyDefinition definition;
        if (MappedFunctionKeys.TryGetValue(key, out definition)) { definition.onPressed.Invoke(); return; }
        if (key >= KeyCode.A && key <= KeyCode.Z) { Input += key.ToString().ToLowerInvariant(); return; }
        if (key >= KeyCode.Alpha0 && key <= KeyCode.Alpha9) { Input += (int)(key - 48); return; }
        if (key == KeyCode.Return && NextPressed != null) NextPressed();
        if (key == KeyCode.Backspace)
        {
            if (Input.Length > 0) Input = Input.Substring(0, Input.Length - 1);
            else if (BackPressedOnEmpty != null) BackPressedOnEmpty();
        }
    }
    public void ToggleSecret() { if (ToggleSecretDisplay != null) ToggleSecretDisplay(); }
    public void EventSetUpNewKeyboard(int index) { foreach (var key in InstantiatedButtons) UnityEngine.Object.Destroy(key); InstantiatedButtons.Clear(); }
}
public class TouchScreenKeyboardKey : Component { }
public class VirtualKeyInfo { public KeyCode key, shiftKey; public int row; }
public class FunctionKeyDefinition { public KeyCode key; public bool hasVisual; public string text; public UnityEngine.Events.UnityEvent onPressed; }
namespace UnityEngine.Events { public class UnityEvent { private event Action handlers; public void AddListener(Action action) { handlers += action; } public void Invoke() { if (handlers != null) handlers(); } } }
public interface IPopupBoard
{
    string Title { get; set; }
    string Message { get; set; }
    Transform Transform { get; }
    event Action<IPopupBoard> Closed;
    void Close();
}
public sealed class PopupBoard : IPopupBoard
{
    public string Title { get; set; }
    public string Message { get; set; }
    public Transform Transform { get; private set; }
    public event Action<IPopupBoard> Closed;
    public PopupBoard() { Transform = new Transform(); }
    public void Close() { if (Closed != null) Closed(this); UnityEngine.Object.Destroy(Transform); }
}
public static class PopupManager
{
    public interface IPopupConfig { void Cancel(); }
    private sealed class Config : IPopupConfig
    {
        public Action<IPopupBoard> Spawn;
        public bool Cancelled;
        public void Cancel() { Cancelled = true; }
    }
    private static readonly Queue<Config> Queue = new Queue<Config>();
    public static IPopupBoard CurrentPopup;
    public static Action BeforeAssignCurrent;
    public static void Reset() { Queue.Clear(); CurrentPopup = null; BeforeAssignCurrent = null; }
    public static IPopupConfig Show(string title, string message) { return Show(delegate(IPopupBoard board) { board.Title = title; board.Message = message; }); }
    public static IPopupConfig Show(Action<IPopupBoard> callback)
    {
        var config = new Config { Spawn = callback };
        Queue.Enqueue(config);
        if (CurrentPopup == null) ProcessQueue();
        return config;
    }
    private static void ProcessQueue()
    {
        while (Queue.Count > 0)
        {
            var next = Queue.Dequeue();
            if (next.Cancelled) continue;
            var popup = new PopupBoard();
            popup.Closed += OnClosed;
            next.Spawn(popup);
            if (BeforeAssignCurrent != null) BeforeAssignCurrent();
            CurrentPopup = popup;
            break;
        }
    }
    private static void OnClosed(IPopupBoard board)
    {
        board.Closed -= OnClosed;
        if (!ReferenceEquals(CurrentPopup, board)) throw new InvalidOperationException("Popup closed before CurrentPopup assignment.");
        CurrentPopup = null;
        ProcessQueue();
    }
}
namespace Alta.Meta.UI
{
    public class InputFieldWithKeyboard : Component, IUseVirtualKeyboard
    {
#pragma warning disable 0414 // Reflection fixture for the production prefab lookup.
        private TouchScreenKeyboard keyboardPrefab = null;
#pragma warning restore 0414
        private TouchScreenKeyboard keyboard;
        public string Text = "";
        public int Submitted;
        public readonly List<string> Received = new List<string>();
        public int MaxCharactersLimit { get { return -1; } }
        public void ShowKeyboard()
        {
            keyboard = TouchScreenKeyboard.Instance;
            // Mirrors TouchScreenKeyboard.Get before InputField.InternalShow.
            keyboard.gameObject.SetActive(true);
            if (keyboard.CurrentUser != null) keyboard.Hide(keyboard.CurrentUser);
            keyboard.Input = "";
            keyboard.SetupNewUser(this);
            keyboard.setupSecretKey = false;
            keyboard.Input = Text;
            keyboard.ToggleSecretDisplay += Toggle;
            keyboard.EventSetUpNewKeyboard(0);
        }
        public void HideKeyboard()
        {
            if (keyboard == null) return;
            keyboard.ToggleSecretDisplay -= Toggle;
            keyboard.Hide(this);
            keyboard = null;
        }
        private void Toggle() { }
        public void HandleKeyboardInput(string value) { Text = value; Received.Add(value); }
        public void HandleKeyboardSubmit() { Submitted++; HideKeyboard(); }
        public void LostFocus() { }
        public void Next() { }
        public void Previous() { }
        public bool IsShowingSecret() { return false; }
    }
}
namespace TavernNativeMenu { internal static class MenuMod { internal static string SafeText(string text) { if (text == null) return ""; string clean = text.Replace('<', '\u2039').Replace('>', '\u203a').Replace('\r', ' '); return clean.Length > 700 ? clean.Substring(0, 700) : clean; } } }
namespace HarmonyLib { public static class AccessTools { public static FieldInfo Field(Type type, string name) { while (type != null) { var field = type.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly); if (field != null) return field; type = type.BaseType; } return null; } } }
namespace MelonLoader { public static class MelonLogger { public static readonly List<string> Lines = new List<string>(); public static void Msg(string text) { Lines.Add(text); } public static void Warning(string text) { Lines.Add(text); } } }
namespace UnityEngine
{
    public class Object
    {
        internal static readonly List<Object> All = new List<Object>();
        private bool destroyed;
        public Object() { All.Add(this); }
        public static T FindObjectOfType<T>() where T : Object { return All.OfType<T>().FirstOrDefault(x => !x.destroyed); }
        public static T Instantiate<T>(T value) where T : Object { var result = (T)Activator.CreateInstance(value.GetType()); if (result is global::TouchScreenKeyboard) global::TouchScreenKeyboard.Instance = result as global::TouchScreenKeyboard; return result; }
        public static void Destroy(Object value) { if (!ReferenceEquals(value, null)) value.destroyed = true; }
        public static bool operator ==(Object left, Object right)
        {
            bool a = ReferenceEquals(left, null) || left.destroyed, b = ReferenceEquals(right, null) || right.destroyed;
            return a || b ? a == b : ReferenceEquals(left, right);
        }
        public static bool operator !=(Object left, Object right) { return !(left == right); }
        public override bool Equals(object value) { return ReferenceEquals(this, value); }
        public override int GetHashCode() { return base.GetHashCode(); }
    }
    public class GameObject : Object
    {
        public bool activeSelf = true;
        public bool activeInHierarchy { get { return activeSelf; } }
        public void SetActive(bool value) { activeSelf = value; }
    }
    public class Component : Object
    {
        public readonly GameObject gameObject = new GameObject();
        public readonly Transform transform = new Transform();
    }
    public class Transform : Object
    {
        public Transform parent;
        public Vector3 position, localPosition, localScale = new Vector3(1, 1, 1);
        public Quaternion rotation;
        public Vector3 forward { get { return Vector3.forward; } }
        public void SetParent(Transform value, bool keepWorldPosition) { parent = value; }
    }
    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public static Vector3 up { get { return new Vector3(0, 1, 0); } }
        public static Vector3 forward { get { return new Vector3(0, 0, 1); } }
        public float sqrMagnitude { get { return x * x + y * y + z * z; } }
        public static Vector3 ProjectOnPlane(Vector3 value, Vector3 normal) { return new Vector3(value.x, 0, value.z); }
        public void Normalize() { float length = (float)Math.Sqrt(sqrMagnitude); if (length != 0) { x /= length; y /= length; z /= length; } }
        public static Vector3 operator +(Vector3 a, Vector3 b) { return new Vector3(a.x + b.x, a.y + b.y, a.z + b.z); }
        public static Vector3 operator -(Vector3 a, Vector3 b) { return new Vector3(a.x - b.x, a.y - b.y, a.z - b.z); }
        public static Vector3 operator *(Vector3 a, float b) { return new Vector3(a.x * b, a.y * b, a.z * b); }
    }
    public struct Quaternion
    {
        public static Quaternion LookRotation(Vector3 forward, Vector3 up) { return new Quaternion(); }
        public static Quaternion Euler(float x, float y, float z) { return new Quaternion(); }
        public static Quaternion operator *(Quaternion a, Quaternion b) { return new Quaternion(); }
    }
    public static class Resources { public static T[] FindObjectsOfTypeAll<T>() where T : Object { return Object.All.OfType<T>().Where(x => x != null).ToArray(); } }
    public enum KeyCode { Backspace = 8, Return = 13, Escape = 27, Alpha0 = 48, Alpha7 = 55, Alpha9 = 57, A = 97, B = 98, Z = 122 }
}
