using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Alta.Api.DataTransferModels.Models.Responses;
using Alta.Meta.UI;
using Alta.SocialTablet;
using Features.Meta.Resolvers;
using HarmonyLib;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using Button = Alta.Meta.UI.Button;

namespace TavernNativeMenu
{
    // The tablet keeps the game's physical touch controls and pagination. Account
    // actions use verified peer keys; server numbers are scoped to one server.
    internal static class NativeSocialTablet
    {
        private sealed class BlockRecord
        {
            public string Server, Address, Name;
            public int Id;
            public bool WasMuted;
        }
        private sealed class View
        {
            internal SocialTablet Tablet;
            internal RecentPlayersPage List;
            internal RecentPlayerActionPage Actions;
            internal TouchScreenMenuBase Screen;
            internal Button Friend, Decline, Block, Kick, Ban, OnlineTab, BansTab, BlockedTab;
            internal Button Mute, Unmute;
            internal readonly Dictionary<int, JObject> Players = new Dictionary<int, JObject>();
            internal JArray Bans = new JArray();
            internal string Server, Message = "", Pending;
            internal int Selected, Generation;
            internal bool CanModerate, BannedList, BlockedList, Refreshing, Busy, Dead;
            internal DateTime NextRefresh, ConfirmUntil;
        }
        private static readonly Dictionary<SocialTablet, View> Views = new Dictionary<SocialTablet, View>();
        private static readonly Queue<Action> Main = new Queue<Action>();
        private static readonly List<BlockRecord> Blocks = new List<BlockRecord>();
        private static JArray friends = new JArray(), requests = new JArray(), peerBlocks = new JArray();
        private static string blockPath, storageError;
        private static bool stopped;
        private static DateTime nextMute;
        private static readonly TabletMuteState forcedMutes = new TabletMuteState();

        internal static void Install(HarmonyLib.Harmony harmony, string gamePath)
        {
            blockPath = Path.Combine(gamePath, "UserData", "TavernTablet.json");
            LoadBlocks();
            Patch(harmony, typeof(SocialTablet), "StartupForLocalPlayer", "BeforeStart");
            Patch(harmony, typeof(RecentPlayersPage), "RefreshData", "BeforeRefresh");
            Patch(harmony, typeof(RecentPlayerActionPage), "Setup", "BeforeSetup");
            // Original async setup must never fetch official group permissions.
            Patch(harmony, typeof(RecentPlayerActionPage), "SetupBanButton", "SkipNativeBanSetup");
            harmony.Patch(AccessTools.Method(typeof(RecentPlayerElement), "UpdateView"), null,
                new HarmonyMethod(typeof(NativeSocialTablet), "AfterRow"));
            MeshSocialClient.Changed += Changed;
            stopped = false;
        }
        private static void Patch(HarmonyLib.Harmony harmony, Type type, string method, string patch)
        {
            MethodInfo original = AccessTools.Method(type, method);
            if (original == null) throw new MissingMethodException(type.FullName, method);
            harmony.Patch(original, new HarmonyMethod(typeof(NativeSocialTablet), patch));
        }
        private static T Field<T>(object item, string name)
        { return (T)AccessTools.Field(item.GetType(), name).GetValue(item); }
        private static void Set(object item, string name, object value)
        { AccessTools.Field(item.GetType(), name).SetValue(item, value); }
        private static void Call(object item, string name, params object[] args)
        { AccessTools.Method(item.GetType(), name).Invoke(item, args); }
        private static void Post(Action action) { lock (Main) { if (!stopped && Main.Count < 256) Main.Enqueue(action); } }
        private static Task OnMain(Action action)
        {
            var done = new TaskCompletionSource<bool>();
            lock (Main)
            {
                if (stopped || Main.Count >= 256) { done.SetException(new InvalidOperationException("Tablet is no longer available")); return done.Task; }
                Main.Enqueue(delegate { try { action(); done.SetResult(true); } catch (Exception error) { done.SetException(error); } });
            }
            return done.Task;
        }
        private static void Changed() { foreach (View view in Views.Values) view.NextRefresh = DateTime.MinValue; }
        private static void BeforeStart(SocialTablet __instance) { Ensure(__instance); }
        private static bool SkipNativeBanSetup(RecentPlayerActionPage __instance)
        { return Find(__instance) == null; }
        private static View Find(Component component)
        {
            if (component == null) return null;
            SocialTablet tablet = component.GetComponentInParent<SocialTablet>();
            return tablet == null ? null : Ensure(tablet);
        }
        private static View Ensure(SocialTablet tablet)
        {
            View found;
            if (Views.TryGetValue(tablet, out found)) return found;
            if (!tablet.IsLocalPlayer) return null;
            var view = new View { Tablet = tablet, List = tablet.GetComponentInChildren<RecentPlayersPage>(true),
                Actions = tablet.GetComponentInChildren<RecentPlayerActionPage>(true), Screen = tablet.GetComponentInChildren<TouchScreenMenuBase>(true) };
            if (view.List == null || view.Actions == null || view.Screen == null) return null;
            Views.Add(tablet, view);
            view.Mute = Field<Button>(view.Actions, "muteButton");
            view.Unmute = Field<Button>(view.Actions, "unmuteButton");
            foreach (Button button in view.Actions.GetComponentsInChildren<Button>(true))
                if (button.transform.parent == view.Actions.transform && button != view.Mute && button != view.Unmute && button.name != "Back Page Button") button.gameObject.SetActive(false);
            foreach (RecentPlayerActionPage.FriendButtonMap map in Field<List<RecentPlayerActionPage.FriendButtonMap>>(view.Actions, "friendButtons")) map.button.SetActive(false);
            Field<Button>(view.Actions, "banButton").gameObject.SetActive(false);
            Field<Button>(view.Actions, "unbanButton").gameObject.SetActive(false);
            view.Friend = Clone(view, view.Actions.transform, "Tavern Friend", -.084f, .035f, delegate { Act(view, "friend"); });
            view.Decline = Clone(view, view.Actions.transform, "Tavern Decline", .032f, .035f, delegate { Act(view, view.Pending == null ? "decline" : "cancel"); });
            Adjust(view.Mute, -.084f, -.022f, .47f, .82f);
            Adjust(view.Unmute, -.084f, -.022f, .47f, .82f);
            view.Block = Clone(view, view.Actions.transform, "Tavern Block", .032f, -.022f, delegate { Act(view, "block"); });
            view.Kick = Clone(view, view.Actions.transform, "Tavern Kick", -.084f, -.080f, delegate { Act(view, "kick"); });
            view.Ban = Clone(view, view.Actions.transform, "Tavern Ban", .032f, -.080f, delegate { Act(view, "ban"); });
            view.OnlineTab = Clone(view, view.List.transform, "Online", -.102f, -.193f, delegate { view.BannedList = view.BlockedList = false; view.Message = ""; RenderList(view); });
            view.BansTab = Clone(view, view.List.transform, "Banned", -.026f, -.193f, delegate { view.BannedList = true; view.BlockedList = false; view.Message = ""; RenderList(view); });
            view.BlockedTab = Clone(view, view.List.transform, "Blocked", .050f, -.193f, delegate { view.BlockedList = true; view.BannedList = false; view.Message = ""; RenderList(view); });
            Adjust(view.OnlineTab, -.102f, -.193f, .31f, .64f);
            Adjust(view.BansTab, -.026f, -.193f, .31f, .64f);
            Adjust(view.BlockedTab, .050f, -.193f, .31f, .64f);
            Transform status = view.Actions.transform.Find("Text");
            if (status != null)
            {
                status.localPosition = new Vector3(-.02844f, -.135f, 0f);
                TextRenderer text = status.GetComponent<TextRenderer>();
                if (text != null) { Set(text, "isMultiLine", true); Set(text, "bestFitToWidth", false); Set(text, "hasLineLimit", true); text.Scale = .065f; Set(text, "lineWidth", 3.3f); Set(text, "lineSpacing", .018f); text.MaxLines = 4; }
            }
            SetTitle(view.List.transform, "Title", "Players on this server");
            view.Screen.InstantiatedButtons.Clear();
            view.Screen.MapButtons();
            Refresh(view);
            return view;
        }
        private static Button Clone(View view, Transform parent, string name, float x, float y, Action action)
        {
            // Mute's prefab has no NavigateTo or persistent UnityEvent callbacks.
            var holder = new GameObject("Tavern inactive button staging"); holder.SetActive(false);
            GameObject copy = UnityEngine.Object.Instantiate(view.Mute.gameObject, holder.transform);
            copy.name = name;
            Button button = copy.GetComponent<Button>(); button.Label = name; button.IsEnabled = true;
            Adjust(button, x, y, .47f, .82f);
            copy.SetActive(true);
            copy.transform.SetParent(parent, false);
            UnityEngine.Object.Destroy(holder);
            button.Released += delegate { if (button.IsEnabled) action(); };
            return button;
        }
        private static void Adjust(Button button, float x, float y, float sx, float sy)
        {
            // This prefab's screen lies in its parent's Y/Z plane; X is depth.
            button.transform.localPosition = new Vector3(-.02588f, y, x + .026f);
            button.transform.localScale = new Vector3(sx, sy, 1f);
            // These are screen-coordinate hit boxes; transform scale is not applied by MapButtons.
            Set(button, "touchSize", new Vector2(.218f * sx, .05f * sy));
            var visual = button.GetComponent<TouchScreenButtonVisual>();
            if (visual != null) Set(visual, "startingScale", button.transform.localScale);
        }
        private static bool BeforeRefresh(RecentPlayersPage __instance)
        {
            View view = Find(__instance); if (view == null) return true;
            RenderList(view); Refresh(view); return false;
        }
        private static bool BeforeSetup(RecentPlayerActionPage __instance, RecentPlayerInteractionInfo recentPlayer)
        {
            View view = Find(__instance); if (view == null) return true;
            view.Selected = recentPlayer.UserInfo.Identifier; view.Message = ""; view.Pending = null; view.Generation++;
            Set(__instance, "recentPlayer", recentPlayer);
            foreach (RecentPlayerElement element in Field<RecentPlayerElement[]>(__instance, "recentPlayerElements")) element.Setup(recentPlayer);
            RenderActions(view); return false;
        }
        private static void Refresh(View view)
        {
            if (view.Refreshing || view.Dead || stopped) return;
            view.Refreshing = true; view.NextRefresh = DateTime.UtcNow.AddSeconds(5);
            // Native network roster remains useful on servers without companion support.
            var fallback = new JArray();
            foreach (var player in Player.AllPlayers.ToArray())
                if (player != null && player.UserInfo != null && !player.IsLocalPlayer)
                    fallback.Add(new JObject { { "id", player.UserInfo.Identifier }, { "name", player.UserInfo.Username }, { "can_target", false } });
            if (view.Players.Count == 0)
            {
                foreach (JObject item in fallback) view.Players[(int)item["id"]] = item;
                RenderList(view);
            }
            Load(view, fallback);
        }
        private static async void Load(View view, JArray fallback)
        {
            JObject roster = null; string issue = null;
            JArray nextFriends = friends, nextRequests = requests, nextBlocks = peerBlocks;
            try { roster = await MeshSocialTransport.TabletRequestAsync("roster", 0).ConfigureAwait(false); }
            catch { issue = "Update server tablet support in Setup"; }
            try
            {
                nextFriends = await MeshSocialClient.GetFriendsAsync().ConfigureAwait(false);
                nextRequests = await MeshSocialClient.GetRequestsAsync().ConfigureAwait(false);
                nextBlocks = await MeshSocialClient.GetBlockedAsync().ConfigureAwait(false);
            }
            catch { /* Voice and current-server controls do not require the peer helper. */ }
            Post(delegate
            {
                view.Refreshing = false;
                if (!Alive(view)) return;
                if (roster != null && (string)roster["server_key"] != MeshSocialTransport.CurrentServerKey) { view.NextRefresh = DateTime.MinValue; return; }
                friends = nextFriends; requests = nextRequests; peerBlocks = nextBlocks;
                view.Server = roster == null ? MeshSocialTransport.CurrentServerKey : (string)roster["server_key"];
                view.CanModerate = roster != null && (bool?)roster["can_moderate"] == true;
                view.Bans = roster == null ? new JArray() : (roster["bans"] as JArray ?? new JArray());
                view.Players.Clear();
                foreach (JObject item in ((roster == null ? null : roster["players"] as JArray) ?? fallback).OfType<JObject>())
                {
                    int id = (int?)item["id"] ?? 0;
                    if (id > 0 && (Player.Current == null || Player.Current.UserInfo.Identifier != id)) view.Players[id] = item;
                }
                if (!view.CanModerate) view.BannedList = false;
                if (issue != null && String.IsNullOrEmpty(view.Message)) view.Message = issue;
                RenderList(view); RenderActions(view); ApplyMutes();
            });
        }
        private static void RenderList(View view)
        {
            if (!Alive(view)) return;
            IEnumerable<JObject> entries = view.BlockedList ? Blocks.Where(x => x.Server == view.Server).Select(x => new JObject { { "id", x.Id }, { "name", x.Name } }) : view.BannedList ? view.Bans.OfType<JObject>() : view.Players.Values;
            var rows = entries.OrderBy(x => Clean((string)x["name"])).Select(x => new RecentPlayerInteractionInfo {
                UserInfo = new UserInfo((int)x["id"], Clean((string)x["name"])),
                InteractionTime = DateTime.UtcNow, Type = RecentPlayerInteractionType.OnServer,
                Server = new GameServerInfoMinimal { Identifier = 0 }
            }).ToArray();
            Set(view.List, "recentPlayers", rows);
            int slots = Field<RecentPlayerElement[]>(view.List, "playerListElements").Length;
            int page = Field<int>(view.List, "currentPage");
            Set(view.List, "currentPage", Math.Max(0, Math.Min(page, (rows.Length - 1) / Math.Max(1, slots))));
            Call(view.List, "UpdatePageView", Field<int>(view.List, "currentPage"));
            TextRenderer empty = Field<TextRenderer>(view.List, "emptyText");
            empty.Text = view.BlockedList ? "No blocked players on this server" : view.BannedList ? "No banned players" : "No other players on this server";
            SetTitle(view.List.transform, "Title", view.BlockedList ? "Blocked on this server" : view.BannedList ? "Banned from this server" : "Players on this server");
            view.OnlineTab.Label = "Online"; view.BansTab.Label = "Banned"; view.BlockedTab.Label = "Blocked";
            view.BansTab.gameObject.SetActive(view.CanModerate);
        }
        private static void AfterRow(RecentPlayerElement __instance)
        {
            View view = Find(__instance); if (view == null || __instance.InteractionInfo == null) return;
            int id = __instance.InteractionInfo.UserInfo.Identifier;
            string address = MeshSocialTransport.VerifiedAddress(id), key = Key(address);
            bool banned = view.Bans.OfType<JObject>().Any(x => (int?)x["id"] == id);
            string status = Player.IsOnServer(id) ? "On this server" : banned ? "Banned from server" : "Offline";
            if (IsBlocked(view, id)) status += " - Blocked";
            else if (Has(friends, key)) status += " - Friend";
            else if (Has(requests, key)) status += " - Friend request";
            __instance.ExtraText.Text = status;
        }
        private static void RenderActions(View view)
        {
            if (!Alive(view) || view.Selected <= 0) return;
            int id = view.Selected; JObject player; bool online = view.Players.TryGetValue(id, out player);
            string key = Key(MeshSocialTransport.VerifiedAddress(id));
            bool blocked = IsBlocked(view, id), friend = Has(friends, key), request = Has(requests, key);
            JObject bannedRow = view.Bans.OfType<JObject>().FirstOrDefault(x => (int?)x["id"] == id);
            bool banned = bannedRow != null;
            bool allowed = view.CanModerate && (banned ? (bool?)bannedRow["can_target"] == true : online && (bool?)player["can_target"] == true);
            bool muted = false;
            try { muted = InterfaceResolver.IVoiceChat.GetPlayerMuted(id); } catch { }
            view.Mute.gameObject.SetActive(online && !muted); view.Unmute.gameObject.SetActive(online && muted);
            view.Mute.IsEnabled = view.Unmute.IsEnabled = !view.Busy && !blocked;
            SetButton(view.Friend, friend ? "Unfriend" : request ? "Accept friend" : "Add friend", online && !blocked && !view.Busy);
            view.Decline.gameObject.SetActive(view.Pending != null || request);
            SetButton(view.Decline, view.Pending != null ? "Cancel" : "Decline", !view.Busy);
            SetButton(view.Block, blocked ? "Unblock" : "Block", !view.Busy && !String.IsNullOrEmpty(view.Server) && storageError == null);
            view.Kick.gameObject.SetActive(view.CanModerate);
            view.Ban.gameObject.SetActive(view.CanModerate);
            SetButton(view.Kick, "Kick", allowed && online && !view.Busy);
            SetButton(view.Ban, banned ? "Unban" : "Ban", allowed && !view.Busy);
            if (view.Pending != null)
            {
                Button confirm = view.Pending == "kick" ? view.Kick : view.Pending == "ban" || view.Pending == "unban" ? view.Ban : view.Pending == "block" || view.Pending == "unblock" ? view.Block : view.Pending == "unfriend" ? view.Friend : view.Decline;
                confirm.Label = "Confirm " + view.Pending;
            }
            string message = view.Busy ? (String.IsNullOrEmpty(view.Message) ? "Working..." : view.Message) : view.Message;
            if (String.IsNullOrEmpty(message)) message = storageError ?? (String.IsNullOrEmpty(view.Server) ? "Update server tablet support in Setup" : blocked ? "Blocked: voice and friend actions off" : "Select an action");
            SetTitle(view.Actions.transform, "Text", message);
        }
        private static void SetButton(Button button, string label, bool enabled)
        { button.Label = label; button.IsEnabled = enabled; }
        private static void SetTitle(Transform root, string name, string text)
        {
            Transform child = root.Find(name); if (child == null) return;
            TextRenderer renderer = child.GetComponent<TextRenderer>(); if (renderer != null) renderer.Text = Clean(text);
        }
        private static void Act(View view, string action)
        {
            if (!Alive(view) || view.Busy || view.Selected <= 0) return;
            if (action == "cancel") { view.Pending = null; view.Message = "Cancelled"; RenderActions(view); return; }
            if (view.Server != MeshSocialTransport.CurrentServerKey) { view.Pending = null; view.Message = "Server changed; refresh the player list"; RenderActions(view); return; }
            string key = Key(MeshSocialTransport.VerifiedAddress(view.Selected));
            if (action == "friend") action = Has(friends, key) ? "unfriend" : Has(requests, key) ? "accept" : "request";
            if (action == "block") action = IsBlocked(view, view.Selected) ? "unblock" : "block";
            if (action == "ban" && view.Bans.OfType<JObject>().Any(x => (int?)x["id"] == view.Selected)) action = "unban";
            bool destructive = action != "request" && action != "accept" && action != "decline";
            if (destructive && (view.Pending != action || view.ConfirmUntil < DateTime.UtcNow))
            { view.Pending = action; view.ConfirmUntil = DateTime.UtcNow.AddSeconds(10); view.Message = "Press Confirm again, or Cancel"; RenderActions(view); return; }
            view.Pending = null; view.Busy = true; view.Message = "Working...";
            int id = view.Selected, generation = view.Generation;
            string scope = view.Server;
            JObject row; view.Players.TryGetValue(id, out row);
            BlockRecord blockRecord = Blocks.FirstOrDefault(x => x.Server == scope && x.Id == id);
            string name = row == null ? blockRecord == null ? "Player" : blockRecord.Name : Clean((string)row["name"]);
            bool canVerify = row != null && !String.IsNullOrEmpty((string)row["address"]);
            JObject bannedRow = view.Bans.OfType<JObject>().FirstOrDefault(x => (int?)x["id"] == id);
            bool canModerate = view.CanModerate && (action == "unban" ? bannedRow != null && (bool?)bannedRow["can_target"] == true : row != null && (bool?)row["can_target"] == true);
            RenderActions(view);
            Perform(view, action, id, name, scope, generation, canVerify, canModerate);
        }
        private static async void Perform(View view, string action, int id, string name, string scope, int generation, bool canVerify, bool canModerate)
        {
            string message = "Done", address = MeshSocialTransport.VerifiedAddress(id);
            BlockRecord saved = Blocks.FirstOrDefault(x => x.Server == scope && x.Id == id);
            bool localSaved = false;
            try
            {
                if (action == "kick" || action == "ban" || action == "unban")
                {
                    if (!canModerate) throw new InvalidOperationException("You cannot moderate this player");
                    await MeshSocialTransport.TabletRequestAsync(action, id, scope).ConfigureAwait(false);
                    message = action == "kick" ? "Player kicked" : action == "ban" ? "Player banned" : "Player unbanned";
                }
                else if (action == "block" || action == "unblock")
                {
                    if (String.IsNullOrEmpty(scope)) throw new InvalidOperationException("Update server tablet support in Setup");
                    if (storageError != null) throw new InvalidOperationException(storageError);
                    if (action == "unblock")
                    {
                        address = saved == null ? address : saved.Address ?? address;
                        JArray remainingPeerBlocks = null;
                        if (Key(address) != null)
                        {
                            await MeshSocialClient.UnblockPeerAsync(Key(address)).ConfigureAwait(false);
                            remainingPeerBlocks = await MeshSocialClient.GetBlockedAsync().ConfigureAwait(false);
                        }
                        await OnMain(delegate { if (remainingPeerBlocks != null) peerBlocks = remainingPeerBlocks; ChangeBlock(view, id, name, scope, address, false); }).ConfigureAwait(false);
                        message = "Player unblocked";
                    }
                    else
                    {
                        // Local blocking also works for players without the peer helper.
                        await OnMain(delegate { ChangeBlock(view, id, name, scope, address, true); }).ConfigureAwait(false);
                        localSaved = true;
                        if (Key(address) == null)
                        {
                            if (canVerify)
                            {
                                Post(delegate { if (Alive(view) && view.Generation == generation) { view.Message = "Blocked locally; verifying peer identity..."; RenderActions(view); } });
                                JObject verified = await VerifyOnMain(id, scope).ConfigureAwait(false);
                                if ((bool?)verified["verified"] != true || (int?)verified["id"] != id) throw new InvalidOperationException("Peer identity could not be verified");
                                address = (string)verified["address"];
                            }
                        }
                        if (Key(address) != null)
                        {
                            // Retain the proven address before the helper refuses future proofs
                            // from this blocked peer, so an offline unblock remains possible.
                            await OnMain(delegate { ChangeBlock(view, id, name, scope, address, true); }).ConfigureAwait(false);
                            await MeshSocialClient.BlockPeerAsync(address, name).ConfigureAwait(false);
                            message = "Player blocked: voice and peer messages";
                        }
                        else message = "Player blocked on this server";
                    }
                }
                else
                {
                    if (Key(address) == null)
                    {
                        Post(delegate { if (Alive(view) && view.Generation == generation) { view.Message = "Verifying peer identity..."; RenderActions(view); } });
                        JObject verified = await VerifyOnMain(id, scope).ConfigureAwait(false);
                        if ((bool?)verified["verified"] != true || (int?)verified["id"] != id) throw new InvalidOperationException("Peer identity could not be verified");
                        address = (string)verified["address"];
                    }
                    if (Key(address) == null) throw new InvalidOperationException("This player needs current Tavern peer support");
                    if (action == "request")
                    {
                        JArray knownFriends = await MeshSocialClient.GetFriendsAsync().ConfigureAwait(false);
                        JArray incoming = await MeshSocialClient.GetRequestsAsync().ConfigureAwait(false);
                        if (Has(knownFriends, Key(address))) message = "You are already friends";
                        else if (Has(incoming, Key(address))) { await MeshSocialClient.AcceptRequestAsync(Key(address)).ConfigureAwait(false); message = "Friend request accepted"; }
                        else { await MeshSocialClient.RequestFriendAsync(address).ConfigureAwait(false); message = "Friend request sent - waiting for acceptance"; }
                    }
                    else if (action == "accept") { await MeshSocialClient.AcceptRequestAsync(Key(address)).ConfigureAwait(false); message = "Friend request accepted"; }
                    else { await MeshSocialClient.RemoveFriendAsync(Key(address)).ConfigureAwait(false); message = action == "decline" ? "Friend request declined" : "Friend removed"; }
                }
            }
            catch (Exception error) { message = (localSaved ? "Blocked on this server; peer block unavailable. " : "") + Clean(error.Message); }
            finally
            {
                Post(delegate
                {
                    if (!Alive(view)) return;
                    view.Busy = false;
                    if (view.Generation == generation) view.Message = message;
                    view.NextRefresh = DateTime.MinValue; RenderActions(view); Refresh(view);
                });
            }
        }
        private static async Task<JObject> VerifyOnMain(int id, string scope)
        {
            Task<JObject> operation = null;
            await OnMain(delegate
            {
                if (scope != MeshSocialTransport.CurrentServerKey) throw new InvalidOperationException("Server changed; select the player again");
                operation = MeshSocialTransport.TabletRequestAsync("verify", id, scope);
            }).ConfigureAwait(false);
            return await operation.ConfigureAwait(false);
        }
        private static bool IsBlocked(View view, int id)
        {
            string key = Key(MeshSocialTransport.VerifiedAddress(id));
            return Blocks.Any(x => x.Server == view.Server && x.Id == id) || Has(peerBlocks, key);
        }
        internal static bool IsPlayerBlocked(int nativeId)
        {
            string scope = MeshSocialTransport.CurrentServerKey;
            return (!String.IsNullOrEmpty(scope) && Blocks.Any(x => x.Server == scope && x.Id == nativeId)) || Has(peerBlocks, Key(MeshSocialTransport.VerifiedAddress(nativeId)));
        }
        private static bool Has(JArray array, string key)
        { return key != null && array.OfType<JObject>().Any(x => String.Equals((string)x["social_id"] ?? (string)x["key"], key, StringComparison.OrdinalIgnoreCase)); }
        private static string Key(string address) { return address != null && address.Length == 76 ? address.Substring(0, 64) : null; }
        private static string Clean(string text)
        { text = (text ?? "").Replace("<", "").Replace(">", "").Replace("\r", " ").Replace("\n", " "); return text.Length > 140 ? text.Substring(0, 140) : text; }
        private static bool Alive(View view) { return view != null && !stopped && !view.Dead && view.Tablet != null && view.List != null && view.Actions != null; }
        private static void ChangeBlock(View view, int id, string name, string scope, string address, bool block)
        {
            BlockRecord previous = Blocks.FirstOrDefault(x => x.Server == scope && x.Id == id);
            bool wasMuted = previous != null && previous.WasMuted;
            bool sameServer = String.Equals(scope, MeshSocialTransport.CurrentServerKey, StringComparison.Ordinal);
            if (block && previous == null && sameServer)
                try { wasMuted = forcedMutes.PreviousState(id) ?? InterfaceResolver.IVoiceChat.GetPlayerMuted(id); } catch { }
            var replacement = Blocks.Where(x => x.Server != scope || x.Id != id).ToList();
            if (block) replacement.Add(new BlockRecord { Server = scope, Id = id, Address = address, Name = name, WasMuted = wasMuted });
            Directory.CreateDirectory(Path.GetDirectoryName(blockPath));
            string pending = blockPath + ".tmp";
            File.WriteAllText(pending, JsonConvert.SerializeObject(replacement, Formatting.Indented));
            if (File.Exists(blockPath)) File.Replace(pending, blockPath, blockPath + ".bak"); else File.Move(pending, blockPath);
            Blocks.Clear(); Blocks.AddRange(replacement);
            if (sameServer) ApplyMutes();
            if (Alive(view)) { RenderActions(view); RenderList(view); }
        }
        private static void LoadBlocks()
        {
            try
            {
                if (!File.Exists(blockPath)) return;
                if (new FileInfo(blockPath).Length > 1024 * 1024) throw new InvalidDataException("Block settings exceed the size limit");
                var loaded = JsonConvert.DeserializeObject<List<BlockRecord>>(File.ReadAllText(blockPath));
                if (loaded == null || loaded.Count > 10000 || loaded.Any(x => x == null || String.IsNullOrEmpty(x.Server) || x.Id <= 0)) throw new InvalidDataException("Invalid saved block settings");
                Blocks.AddRange(loaded);
            }
            catch { storageError = "Restore UserData/TavernTablet.json from its backup to change blocks"; }
        }
        private static void ApplyMutes()
        {
            string scope = MeshSocialTransport.CurrentServerKey;
            if (String.IsNullOrEmpty(scope)) { forcedMutes.Restore(); return; }
            var desired = new Dictionary<int, bool?>();
            foreach (var player in Player.AllPlayers.ToArray())
            {
                if (player == null || player.UserInfo == null || player.IsLocalPlayer) continue;
                int id = player.UserInfo.Identifier;
                BlockRecord saved = Blocks.FirstOrDefault(x => x.Server == scope && x.Id == id);
                if (saved != null || Has(peerBlocks, Key(MeshSocialTransport.VerifiedAddress(id))))
                    desired[id] = saved == null ? (bool?)null : saved.WasMuted;
            }
            var voice = InterfaceResolver.IVoiceChat;
            if (voice == null) { forcedMutes.Restore(); return; }
            forcedMutes.Synchronize(scope, voice, voice.GetPlayerMuted, voice.SetPlayerMuted, desired);
        }
        internal static void Tick()
        {
            Action next;
            for (int i = 0; i < 64; i++) { lock (Main) { if (Main.Count == 0) break; next = Main.Dequeue(); } try { next(); } catch (Exception error) { MeshSocialClient.SetError(error); } }
            foreach (View view in Views.Values.ToArray())
            {
                if (!Alive(view)) { view.Dead = true; Views.Remove(view.Tablet); continue; }
                if (view.Pending != null && (view.ConfirmUntil < DateTime.UtcNow || !view.Actions.Panel.IsActive)) { view.Pending = null; view.Message = "Confirmation cancelled"; RenderActions(view); }
                if ((view.List.Panel.IsActive || view.Actions.Panel.IsActive) && DateTime.UtcNow >= view.NextRefresh) Refresh(view);
            }
            if (DateTime.UtcNow >= nextMute) { nextMute = DateTime.UtcNow.AddSeconds(1); ApplyMutes(); }
        }
        internal static void Shutdown()
        {
            stopped = true; MeshSocialClient.Changed -= Changed;
            forcedMutes.Restore();
            foreach (View view in Views.Values) view.Dead = true;
            Views.Clear(); lock (Main) Main.Clear();
        }
    }

    // Native voice providers key their persistent mutes by numeric account ID.
    // Keep only the block's temporary ownership here so one server's IDs cannot
    // carry a tablet-imposed mute into another server or a later connection.
    internal sealed class TabletMuteState
    {
        private sealed class Lease
        {
            internal string Scope;
            internal object Voice;
            internal bool Previous;
            internal Func<int, bool> Get;
            internal Action<int, bool> Set;
        }
        private readonly Dictionary<int, Lease> leases = new Dictionary<int, Lease>();
        internal bool? PreviousState(int id)
        { Lease lease; return leases.TryGetValue(id, out lease) ? (bool?)lease.Previous : null; }
        private void Release(int id, Lease lease)
        {
            try
            {
                if (lease.Get(id) != lease.Previous) lease.Set(id, lease.Previous);
                leases.Remove(id);
            }
            catch { /* Retry restoration on the next tick if the provider is temporarily unavailable. */ }
        }
        internal void Restore()
        { foreach (var entry in leases.ToArray()) Release(entry.Key, entry.Value); }
        internal void Synchronize(string scope, object voice, Func<int, bool> get, Action<int, bool> set, IDictionary<int, bool?> desired)
        {
            foreach (var entry in leases.ToArray())
                if (entry.Value.Scope != scope || !ReferenceEquals(entry.Value.Voice, voice) || !desired.ContainsKey(entry.Key)) Release(entry.Key, entry.Value);
            foreach (var entry in desired)
            {
                try
                {
                    Lease lease;
                    if (!leases.TryGetValue(entry.Key, out lease))
                    {
                        lease = new Lease { Scope = scope, Voice = voice, Previous = entry.Value ?? get(entry.Key), Get = get, Set = set };
                        leases.Add(entry.Key, lease);
                    }
                    // A failed restoration still belongs to the previous provider/scope.
                    if (lease.Scope == scope && ReferenceEquals(lease.Voice, voice) && !get(entry.Key)) set(entry.Key, true);
                }
                catch { /* A missing voice provider must not prevent social actions. */ }
            }
        }
    }
}
