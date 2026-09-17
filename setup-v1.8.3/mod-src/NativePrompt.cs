using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using Alta.Meta.UI;
using HarmonyLib;
using MelonLoader;
using UnityEngine;

namespace TavernNativeMenu
{
    // Borrow the actual touch keyboard with a private modal owner. Passwords
    // must never be sent to search filters, autocomplete, or serialized output.
    internal sealed class NativePrompt : IUseVirtualKeyboard
    {
        private readonly TaskCompletionSource<string> completion = new TaskCompletionSource<string>();
        private readonly Dictionary<FieldInfo, Delegate> savedEvents = new Dictionary<FieldInfo, Delegate>();
        private readonly Dictionary<GameObject, bool> suggestions = new Dictionary<GameObject, bool>();
        private global::TouchScreenKeyboard keyboard;
        private IUseVirtualKeyboard previousUser;
        private IPopupBoard popup;
        private PopupManager.IPopupConfig request;
        private ServerSelectionMenu ownerMenu;
        private string value = "", instructions, previousInput;
        private bool secret, reveal, finished, openPending, borrowed, lostFocus, wasActive, previousSecret;
        private Transform previousParent;
        private Vector3 previousPosition, previousScale;
        private Quaternion previousRotation;
        private FieldInfo outputField;
        private object previousOutput, isolatedOutput;
        private static NativePrompt current;
        private TouchScreenKeyboardKey cancelKey;
        private FunctionKeyDefinition cancelDefinition, previousEscape;
        private bool cancelRequested;
        private static readonly string[] EventNames = { "InputChanged", "NextPressed", "BackPressedOnEmpty", "ToggleSecretDisplay" };
        public static bool Active { get { return current != null && !current.finished; } }
        public int MaxCharactersLimit { get { return secret ? 256 : 253; } }

        public static Task<string> Ask(string title, string instructions, bool secret)
        {
            if (current != null) current.Finish(null);
            var prompt = new NativePrompt { secret = secret, instructions = instructions,
                ownerMenu = UnityEngine.Object.FindObjectOfType<ServerSelectionMenu>() };
            current = prompt;
            try
            {
                if (prompt.ownerMenu == null) throw new InvalidOperationException("The server menu is no longer open.");
                prompt.request = PopupManager.Show(delegate(IPopupBoard board)
                {
                    prompt.popup = board;
                    board.Title = title;
                    board.Closed += prompt.Closed;
                    prompt.UpdateMessage();
                    // PopupManager assigns CurrentPopup after this callback.
                    // Open/close on the next update to keep its queue consistent.
                    prompt.openPending = true;
                });
            }
            catch (Exception error) { prompt.Fail(error); }
            return prompt.completion.Task;
        }

        internal static void CheckLifetime()
        {
            NativePrompt prompt = current;
            if (prompt == null) return;
            try
            {
                if (prompt.ownerMenu == null || prompt.lostFocus || prompt.cancelRequested) { prompt.Finish(null); return; }
                if (prompt.openPending) { prompt.openPending = false; prompt.OpenKeyboard(); }
                else if (prompt.borrowed && (prompt.popup == null || prompt.popup.Transform == null ||
                    prompt.keyboard == null || prompt.keyboard.CurrentUser != prompt || !prompt.keyboard.gameObject.activeInHierarchy))
                    prompt.Finish(null);
                else if (prompt.borrowed) prompt.EnsureCancelKey();
            }
            catch (Exception error) { prompt.Fail(error); }
        }

        private void OpenKeyboard()
        {
            Transform head = PlayerController.Current == null ? null : PlayerController.Current.Head;
            if (head == null) throw new InvalidOperationException("The local VR player is not ready. Try joining again after the menu has loaded.");
            keyboard = global::TouchScreenKeyboard.Instance;
            if (keyboard == null)
            {
                foreach (InputFieldWithKeyboard field in Resources.FindObjectsOfTypeAll<InputFieldWithKeyboard>())
                {
                    var prefab = AccessTools.Field(typeof(InputFieldWithKeyboard), "keyboardPrefab").GetValue(field) as global::TouchScreenKeyboard;
                    if (prefab == null) continue;
                    keyboard = UnityEngine.Object.Instantiate(prefab);
                    break;
                }
            }
            if (keyboard == null) throw new InvalidOperationException("The game's VR keyboard prefab could not be found.");
            previousUser = keyboard.CurrentUser;
            previousInput = keyboard.Input;
            previousSecret = keyboard.setupSecretKey;
            wasActive = keyboard.gameObject.activeSelf;
            previousParent = keyboard.transform.parent;
            previousPosition = keyboard.transform.position;
            previousRotation = keyboard.transform.rotation;
            previousScale = keyboard.transform.localScale;
            borrowed = true;
            var fieldOwner = previousUser as InputFieldWithKeyboard;
            if (fieldOwner != null) fieldOwner.HideKeyboard();
            else if (previousUser != null) keyboard.Hide(previousUser);
            foreach (string name in EventNames)
            {
                FieldInfo field = AccessTools.Field(typeof(global::TouchScreenKeyboard), name);
                if (field == null) throw new MissingFieldException("Keyboard event: " + name);
                savedEvents.Add(field, field.GetValue(keyboard) as Delegate);
                field.SetValue(keyboard, null);
            }
            outputField = AccessTools.Field(typeof(global::TouchScreenKeyboard), "output");
            if (outputField == null) throw new MissingFieldException("Keyboard output event");
            previousOutput = outputField.GetValue(keyboard);
            isolatedOutput = Activator.CreateInstance(outputField.FieldType, true);
            outputField.SetValue(keyboard, isolatedOutput);
            IDictionary keys = AccessTools.Field(typeof(TouchScreenMenuBase), "instantiatedSuggestionKeys").GetValue(keyboard) as IDictionary;
            if (keys != null) foreach (DictionaryEntry item in keys)
            {
                Component key = item.Value as Component;
                if (key == null) continue;
                suggestions[key.gameObject] = key.gameObject.activeSelf;
                key.gameObject.SetActive(false);
            }
            // The touch side is local -Z. Keep the native keyboard within arm
            // reach, below a dialog in front of the player, not at a search field.
            Vector3 forward = Vector3.ProjectOnPlane(head.forward, Vector3.up);
            if (forward.sqrMagnitude < 0.01f) forward = Vector3.forward;
            forward.Normalize();
            Quaternion facing = Quaternion.LookRotation(forward, Vector3.up);
            popup.Transform.position = head.position + forward * 0.95f - Vector3.up * 0.05f;
            popup.Transform.rotation = facing;
            keyboard.transform.SetParent(null, true);
            keyboard.transform.position = head.position + forward * 0.55f - Vector3.up * 0.45f;
            keyboard.transform.rotation = facing * Quaternion.Euler(30f, 0f, 0f);
            keyboard.setupSecretKey = secret;
            keyboard.SetupNewUser(this);
            keyboard.ToggleSecretDisplay += ToggleSecret;
            keyboard.EventSetUpNewKeyboard(0);
            keyboard.Input = "";
            EnsureCancelKey();
            MelonLogger.Msg("[Tavern Native Menu] Native VR input opened. Touch Enter to continue or Cancel to go back.");
        }

        private void EnsureCancelKey()
        {
            if (cancelKey != null) return;
            if (cancelDefinition == null)
            {
                keyboard.MappedFunctionKeys.TryGetValue(KeyCode.Escape, out previousEscape);
                cancelDefinition = new FunctionKeyDefinition { key = KeyCode.Escape, hasVisual = true,
                    text = "Cancel", onPressed = new UnityEngine.Events.UnityEvent() };
                // Native input iterates the key collection. Remove it on the next update.
                cancelDefinition.onPressed.AddListener(delegate { cancelRequested = true; });
            }
            keyboard.MappedFunctionKeys[KeyCode.Escape] = cancelDefinition;
            cancelKey = keyboard.AddExtraKey(new VirtualKeyInfo { key = KeyCode.Escape, shiftKey = KeyCode.Escape, row = 0 }, true, 0);
            cancelKey.transform.localPosition += new Vector3(MenuArtworkRules.CancelRightShift, 0, 0);
            keyboard.MapButtons();
        }

        public void HandleKeyboardInput(string input) { if (!finished) { value = input ?? ""; UpdateMessage(); } }
        public void HandleKeyboardSubmit() { Finish(value); }
        public void Next() { HandleKeyboardSubmit(); }
        public void Previous() { Finish(null); }
        public bool IsShowingSecret() { return secret && !reveal; }
        private void ToggleSecret() { reveal = !reveal; UpdateMessage(); }
        public void LostFocus()
        {
            // SetupNewUser dereferences CurrentUser again after this callback.
            // Defer Hide/restoration until it has finished handing over focus.
            lostFocus = true;
            if (keyboard != null && keyboard.CurrentUser == this) keyboard.Input = "";
            value = "";
        }
        private void Closed(IPopupBoard board) { board.Closed -= Closed; popup = null; Finish(null); }
        private void UpdateMessage()
        {
            if (popup == null) return;
            string shown = secret && !reveal ? new string('*', value.Length) : MenuMod.SafeText(value);
            popup.Message = instructions + "\n\n" + (shown.Length == 0 ? "[Type using the keyboard below]" : shown)
                + "\n\nTouch Enter to continue.\nTouch Cancel at the left of the keyboard to go back.";
        }
        private void Fail(Exception error)
        {
            MelonLogger.Warning("[Tavern Native Menu] VR input could not open: " + error.GetType().Name);
            Finish(null);
            PopupManager.Show("Tavern menu keyboard", error.Message);
        }

        private void RestoreKeyboard()
        {
            if (!borrowed || keyboard == null) return;
            if (cancelKey != null)
            {
                keyboard.InstantiatedButtons.Remove(cancelKey);
                UnityEngine.Object.Destroy(cancelKey.gameObject);
                cancelKey = null;
                keyboard.MapButtons();
            }
            FunctionKeyDefinition mapped;
            if (cancelDefinition != null && keyboard.MappedFunctionKeys.TryGetValue(KeyCode.Escape, out mapped) && ReferenceEquals(mapped, cancelDefinition))
            {
                if (previousEscape == null) keyboard.MappedFunctionKeys.Remove(KeyCode.Escape);
                else keyboard.MappedFunctionKeys[KeyCode.Escape] = previousEscape;
            }
            bool ownFocus = keyboard.CurrentUser == this;
            bool canRestore = ownFocus || (!lostFocus && keyboard.CurrentUser == null);
            keyboard.InputChanged -= HandleKeyboardInput;
            keyboard.NextPressed -= HandleKeyboardSubmit;
            keyboard.ToggleSecretDisplay -= ToggleSecret;
            if (ownFocus) keyboard.Hide(this);
            if (canRestore) keyboard.Input = "";
            if (outputField != null && ReferenceEquals(outputField.GetValue(keyboard), isolatedOutput)) outputField.SetValue(keyboard, previousOutput);
            foreach (KeyValuePair<FieldInfo, Delegate> pair in savedEvents)
            {
                Delegate active = pair.Key.GetValue(keyboard) as Delegate;
                if (pair.Value != null) foreach (Delegate handler in pair.Value.GetInvocationList())
                {
                    bool exists = false;
                    if (active != null) foreach (Delegate registered in active.GetInvocationList()) if (registered.Equals(handler)) exists = true;
                    if (!exists) active = Delegate.Combine(active, handler);
                }
                pair.Key.SetValue(keyboard, active);
            }
            if (!canRestore) return; // A different UI owns the keyboard now.
            keyboard.setupSecretKey = previousSecret;
            keyboard.transform.SetParent(previousParent, true);
            keyboard.transform.position = previousPosition;
            keyboard.transform.rotation = previousRotation;
            keyboard.transform.localScale = previousScale;
            foreach (KeyValuePair<GameObject, bool> item in suggestions) if (item.Key != null) item.Key.SetActive(item.Value);
            bool previousAlive = previousUser != null && (!(previousUser is UnityEngine.Object) || (UnityEngine.Object)previousUser != null);
            if (previousAlive)
            {
                var field = previousUser as InputFieldWithKeyboard;
                if (field != null) field.ShowKeyboard();
                else { keyboard.SetupNewUser(previousUser); keyboard.EventSetUpNewKeyboard(0); }
            }
            else keyboard.EventSetUpNewKeyboard(0);
            keyboard.Input = previousInput ?? "";
            keyboard.gameObject.SetActive(wasActive);
        }
        private void Finish(string result)
        {
            if (finished) return;
            finished = true;
            if (request != null) request.Cancel();
            try { RestoreKeyboard(); }
            catch (Exception error) { MelonLogger.Warning("[Tavern Native Menu] Keyboard cleanup: " + error.GetType().Name); }
            finally
            {
                value = "";
                if (ReferenceEquals(current, this)) current = null;
                if (popup != null && popup.Transform != null) { popup.Closed -= Closed; popup.Close(); }
                popup = null;
                completion.TrySetResult(result);
            }
        }
    }
}
