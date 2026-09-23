using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Alta.Api.DataTransferModels.Models.Responses;
using Alta.Meta.Friends;
using Alta.Meta.UI;
using Alta.Meta.UI.ServerManagement;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace TavernNativeMenu
{
    // Peer public keys remain separate from server-local player numbers.
    // Both generations of native boards render accepted contacts directly.
    internal static class NativeFriendsMenu
    {
        private sealed class Row
        {
            internal int Id;
            internal string Text;
            internal Action Accept, Reject;
        }
        private static JArray friends = new JArray(), invites = new JArray(), requests = new JArray();
        private static readonly Dictionary<string, int> rowIds = new Dictionary<string, int>();
        private static readonly Dictionary<int, Row> rows = new Dictionary<int, Row>();
        private static readonly Dictionary<InviteUserToGroupPanel, JObject> recipients = new Dictionary<InviteUserToGroupPanel, JObject>();
        private static Task refreshing;
        private static bool refreshPending;
        internal static string InvitationRecipient, InvitationName;
        internal static bool ChoosingInvitation { get { return InvitationRecipient != null; } }

        internal static void Install(HarmonyLib.Harmony harmony)
        {
            Patch(harmony, typeof(FriendsListPanel), "SetupFriendsList", "BeforeFriendsList");
            Patch(harmony, typeof(FriendsRequestsPanel), "SetupFriendsList", "BeforeRequests");
            Patch(harmony, typeof(InviteUserToGroupPanel), "SetupGroupsList", "BeforeServers");
            harmony.Patch(AccessTools.Method(typeof(FriendsListPanel), "AddFriend", new[] { typeof(string) }),
                new HarmonyMethod(typeof(NativeFriendsMenu), "BeforeAddByName"));
            Patch(harmony, typeof(FriendsManager), "UpdateFriends", "BeforeLegacyRefresh");
            Patch(harmony, typeof(FriendsManager), "GetUserInfoToDisplay", "BeforeLegacyUsers");
            Patch(harmony, typeof(FriendsManager), "ConfigureTokenForTab", "BeforeLegacyToken");
            MeshSocialClient.Changed += delegate { refreshPending = true; };
        }

        private static void Patch(HarmonyLib.Harmony harmony, Type type, string method, string replacement)
        {
            var original = AccessTools.Method(type, method);
            if (original == null) throw new MissingMethodException(type.FullName, method);
            harmony.Patch(original, new HarmonyMethod(typeof(NativeFriendsMenu), replacement));
        }

        internal static void Tick()
        {
            if (GameModeManager.CurrentMode != null) { InvitationRecipient = InvitationName = null; return; }
            if (!refreshPending || NativePrompt.Active) return;
            refreshPending = false;
            Run(async delegate
            {
                await Refresh();
                foreach (FriendsListPanel panel in UnityEngine.Object.FindObjectsOfType<FriendsListPanel>())
                    if (panel.isActiveAndEnabled) panel.RefreshData();
                foreach (FriendsRequestsPanel panel in UnityEngine.Object.FindObjectsOfType<FriendsRequestsPanel>())
                    if (panel.isActiveAndEnabled) panel.RefreshData();
                foreach (FriendsManager board in UnityEngine.Object.FindObjectsOfType<FriendsManager>())
                    if (board.isActiveAndEnabled) AccessTools.Method(typeof(FriendsManager), "UpdateDisplay").Invoke(board, null);
            });
        }

        private static Task Refresh()
        {
            if (refreshing == null || refreshing.IsCompleted) refreshing = LoadData();
            return refreshing;
        }

        private static async Task LoadData()
        {
            try
            {
                friends = await MeshSocialClient.GetFriendsAsync();
                invites = await MeshSocialClient.GetInvitesAsync();
                requests = await MeshSocialClient.GetRequestsAsync();
            }
            catch (Exception) { /* Keep the last list; the status row offers retry. */ }
        }

        private static bool BeforeFriendsList(FriendsListPanel __instance, ref Task __result)
        { __result = BuildFriends(__instance); return false; }

        private static async Task BuildFriends(FriendsListPanel panel)
        {
            await Refresh();
            if (panel == null) return;
            var list = new List<ListElementWithButton>();
            AddRow(panel, "listElement", list, ServiceLabel(), delegate { Run(Configure); });
            AddRow(panel, "listElement", list, "Add a friend code...", delegate { Run(AddCode); });
            AddRow(panel, "listElement", list, "Friendship cards / help", ShowCardHelp);
            foreach (JObject friend in friends.OfType<JObject>())
            {
                JObject item = friend;
                AddRow(panel, "listElement", list, FriendName(item), delegate
                {
                    panel.Panel.Window.ShowOptions(new[] {
                        new OptionsConfig("Invite to Server", delegate { ShowServerPicker(panel, item); }),
                        new OptionsConfig("Unfriend", delegate { Run(async delegate { await MeshSocialClient.RemoveFriendAsync((string)item["social_id"]); refreshPending = true; }); }, UIButtonType.Bad)
                    });
                });
            }
            foreach (JObject invite in invites.OfType<JObject>()) AddInviteRow(panel, list, invite);
            Complete(panel, list);
        }

        private static bool BeforeRequests(FriendsRequestsPanel __instance, ref Task __result)
        { __result = BuildInvites(__instance); return false; }

        private static async Task BuildInvites(FriendsRequestsPanel panel)
        {
            await Refresh();
            if (panel == null) return;
            var list = new List<ListElementWithButton>();
            AddRow(panel, "listElement", list, "Friend requests and server invitations", ShowCardHelp);
            foreach (JObject pending in requests.OfType<JObject>())
            {
                JObject item = pending;
                AddRow(panel, "listElement", list, "Friend request: " + Clean((string)item["name"]), delegate
                {
                    panel.Panel.Window.ShowOptions(new[] {
                        new OptionsConfig("Accept friend", delegate { Run(async delegate { await MeshSocialClient.AcceptRequestAsync((string)item["social_id"]); refreshPending = true; }); }),
                        new OptionsConfig("Decline", delegate { Run(async delegate { await MeshSocialClient.RemoveFriendAsync((string)item["social_id"]); refreshPending = true; }); }, UIButtonType.Bad)
                    });
                });
            }
            foreach (JObject invite in invites.OfType<JObject>()) AddInviteRow(panel, list, invite);
            if (invites.Count == 0 && requests.Count == 0) AddRow(panel, "listElement", list, "No pending requests or invitations", delegate { refreshPending = true; });
            Complete(panel, list);
        }

        private static void AddInviteRow(PanelComponent panel, List<ListElementWithButton> list, JObject invite)
        {
            AddRow(panel, "listElement", list, InviteName(invite), delegate
            {
                panel.Panel.Window.ShowOptions(new[] {
                    new OptionsConfig("Join Server", delegate { Run(delegate { return Accept(invite, true); }); }),
                    new OptionsConfig("Save Server", delegate { Run(delegate { return Accept(invite, false); }); }),
                    new OptionsConfig("Dismiss", delegate { Run(async delegate { await MeshSocialClient.DismissInviteAsync((string)invite["invite_id"]); refreshPending = true; }); }, UIButtonType.Bad)
                });
            });
        }

        private static async Task Accept(JObject invite, bool join)
        {
            if (GameModeManager.CurrentMode != null) throw new InvalidOperationException("Return to the server menu before joining an invitation.");
            ServerEntry entry = await MeshSocialClient.AcceptInviteAsync((string)invite["invite_id"]);
            entry.Private = true;
            MenuMod.Catalog.Add(entry);
            refreshPending = true;
            if (join)
            {
                var menu = UnityEngine.Object.FindObjectOfType<VrMainMenu>();
                if (menu == null) throw new InvalidOperationException("The server menu is no longer open. The invited server is saved in Tavern Saved / Recent.");
                InvitationRecipient = InvitationName = null;
                menu.JoinServer(ServerCatalog.ToMenuServer(entry));
            }
            else PopupManager.Show("Server saved", "The invitation's address is saved locally in Tavern Saved / Recent. Password and access checks still apply when joining.");
        }

        private static void ShowServerPicker(FriendsListPanel panel, JObject friend)
        {
            var window = MenuMod.Get<InviteUserToGroupPanel>(panel, "inviteWindow");
            if (window == null) { BeginInvite(friend); return; }
            recipients[window] = friend;
            window.SetupForUser(new UserInfo { Identifier = -1, Username = Clean((string)friend["name"]) });
            window.RefreshData();
        }

        private static bool BeforeServers(InviteUserToGroupPanel __instance, ref Task __result)
        { __result = BuildServers(__instance); return false; }

        private static Task BuildServers(InviteUserToGroupPanel panel)
        {
            JObject friend;
            var list = new List<ListElementWithButton>();
            if (!recipients.TryGetValue(panel, out friend))
                AddRow(panel, "elementTemplate", list, "Choose a friend on the Tavern friends board first", ShowCardHelp);
            else
            {
                foreach (ServerEntry server in SavedServers())
                {
                    ServerEntry entry = server;
                    AddRow(panel, "elementTemplate", list, Clean(entry.Name ?? entry.Host), delegate
                    {
                        panel.Panel.Window.ShowConfirmPopup("Invite " + Clean((string)friend["name"]) + " to " + Clean(entry.Name ?? entry.Host) + "?", "Send", "Cancel", delegate
                        {
                            Run(async delegate
                            {
                                await MeshSocialClient.SendInviteAsync((string)friend["social_id"], entry);
                                if (panel != null) panel.Panel.Window.gameObject.SetActive(false);
                                PopupManager.Show("Invitation queued", "The invitation will reach your friend when both games are online. Their normal server password and access checks still apply.");
                            });
                        });
                    });
                }
                AddRow(panel, "elementTemplate", list, "Add a private server...", delegate { Run(async delegate { await MenuMod.AddServer(); if (panel != null) panel.RefreshData(); }); });
            }
            Complete(panel, list);
            return Task.FromResult(0);
        }

        internal static IEnumerable<ServerEntry> SavedServers()
        {
            return MenuMod.Catalog.Settings.Servers.Concat(MenuMod.Catalog.Profile.Servers)
                .Where(ServerCatalog.ValidEntry).GroupBy(ServerCatalog.Key, StringComparer.OrdinalIgnoreCase).Select(group => group.First()).ToArray();
        }

        private static void AddRow(PanelComponent panel, string template, List<ListElementWithButton> list, string text, Action action)
        {
            var prefab = MenuMod.Get<ListElementWithButton>(panel, template);
            var row = UnityEngine.Object.Instantiate(prefab, panel.transform);
            row.Text.Text = text;
            // The friends panel uses the button, the invite panel uses Selected.
            if (panel is InviteUserToGroupPanel) row.Selected += delegate { action(); };
            else row.Button.Pressed += delegate { action(); };
            list.Add(row);
        }

        private static void Complete(PanelComponent panel, List<ListElementWithButton> list)
        {
            MenuMod.Get<UIList>(panel, "uiList").CompleteReset(list, true);
            var field = AccessTools.Field(panel.GetType(), "emptyListText");
            var empty = field == null ? null : field.GetValue(panel) as TextRenderer;
            if (empty != null) empty.gameObject.SetActive(false);
        }

        private static bool BeforeAddByName(string __0, ref Task __result)
        { __result = SendCode(__0); return false; }

        private static async Task SendCode(string code)
        {
            await MeshSocialClient.RequestFriendAsync(MeshSocialClient.ValidateAddress(code));
            refreshPending = true;
            PopupManager.Show("Friend request saved", "The request will reach your friend when both games are online. They accept it from Requests before appearing on your friends list.");
        }
        private static async Task AddCode()
        {
            string code = await NativePrompt.Ask("Add a friend code", "Enter your friend's full 76-character code. Friendship cards are an alternative while together in a server.", false);
            if (!String.IsNullOrWhiteSpace(code)) await SendCode(code);
        }

        private static void ShowCardHelp()
        {
            PopupManager.Show("Tavern friends", "Share friend codes and accept requests, or exchange friendship cards together on a server with current Tavern card support. Only accepted friends appear here. Choose a friend to invite them to a saved or private server; Requests shows incoming friend requests and invitations. Both games must be online together to deliver queued messages.");
        }

        private static string ServiceLabel()
        {
            return "My friend code / network status (" + (MeshSocialClient.Connected ? "online" : MeshSocialClient.Configured ? "connecting" : "starting / unavailable") + ")";
        }

        private static Task Configure()
        {
            string code = MeshSocialClient.Address;
            string display = String.IsNullOrEmpty(code) ? "Your identity is still starting." : "Your friend code:\n" + code;
            PopupManager.Show("Tavern peer friends", display + "\n\n" + (MeshSocialClient.LastError == null ? "Friends connect across the peer network; no shared relay address is needed. Your code is also saved in UserData/TavernFriendCode.txt for copying on desktop." : Clean(MeshSocialClient.LastError)));
            return Task.FromResult(0);
        }

        private static bool BeforeLegacyRefresh(ref Task __result) { __result = Refresh(); return false; }

        private static bool BeforeLegacyUsers(FriendsManager __instance, ref IEnumerable<UserInfo> __result)
        {
            var list = new List<UserInfo>();
            FriendsManager.Tab tab = MenuMod.Get<FriendsManager.Tab>(__instance, "currentTab");
            if (tab == FriendsManager.Tab.Requests)
            {
                foreach (JObject pending in requests.OfType<JObject>())
                {
                    JObject item = pending;
                    list.Add(LegacyRow("request:" + (string)item["social_id"], "Friend request: " + Clean((string)item["name"]),
                        delegate { Run(async delegate { await MeshSocialClient.AcceptRequestAsync((string)item["social_id"]); refreshPending = true; }); },
                        delegate { Run(async delegate { await MeshSocialClient.RemoveFriendAsync((string)item["social_id"]); refreshPending = true; }); }));
                }
                foreach (JObject invitation in invites.OfType<JObject>())
                {
                    JObject item = invitation;
                    list.Add(LegacyRow("invite:" + (string)item["invite_id"], InviteName(item),
                        delegate { Run(delegate { return Accept(item, true); }); },
                        delegate { Run(async delegate { await MeshSocialClient.DismissInviteAsync((string)item["invite_id"]); refreshPending = true; }); }));
                }
                if (list.Count == 0) list.Add(LegacyRow("no-invites", "No pending requests or invitations", delegate { refreshPending = true; }, null));
            }
            else
            {
                list.Add(LegacyRow("service", ServiceLabel(), delegate { Run(Configure); }, null));
                list.Add(LegacyRow("add-code", "Add a friend code...", delegate { Run(AddCode); }, null));
                foreach (JObject friendship in friends.OfType<JObject>())
                {
                    JObject item = friendship;
                    list.Add(LegacyRow("friend:" + (string)item["social_id"], FriendName(item),
                        delegate { BeginInvite(item); },
                        delegate { Run(async delegate { await MeshSocialClient.RemoveFriendAsync((string)item["social_id"]); refreshPending = true; }); }));
                }
                list.Add(LegacyRow("help", "Add friends with your friendship card / invitations in Requests", ShowCardHelp, null));
            }
            __result = list;
            return false;
        }

        private static UserInfo LegacyRow(string key, string text, Action accept, Action reject)
        {
            int id;
            if (!rowIds.TryGetValue(key, out id)) rowIds[key] = id = -1000 - rowIds.Count;
            rows[id] = new Row { Id = id, Text = text, Accept = accept, Reject = reject };
            return new UserInfo { Identifier = id, Username = text };
        }

        private static bool BeforeLegacyToken(FriendsManager __instance, UserToken token, UserInfo user)
        {
            Row row;
            if (!rows.TryGetValue(user.Identifier, out row)) return true;
            token.Setup(user, __instance, row.Accept == null ? (Action<UserToken>)null : delegate { row.Accept(); },
                row.Reject == null ? (Action<UserToken>)null : delegate { row.Reject(); });
            return false;
        }

        private static void BeginInvite(JObject friend)
        {
            InvitationRecipient = (string)friend["social_id"];
            InvitationName = Clean((string)friend["name"]);
            Run(RefreshBoards);
            PopupManager.Show("Choose a server for " + InvitationName, "Use the server wheel to choose one of your saved servers, then select its orb to send an invitation. The Cancel invitation entry returns to normal joining. You can also add a private server.");
        }

        internal static async Task SendSelected(ServerEntry entry)
        {
            string recipient = InvitationRecipient;
            if (recipient == null) return;
            await MeshSocialClient.SendInviteAsync(recipient, entry);
            await CancelInvitation();
            PopupManager.Show("Invitation queued", "The invitation will reach your friend when both games are online. They can join from Requests; normal password and access checks still apply.");
        }

        internal static Task CancelInvitation()
        { InvitationRecipient = InvitationName = null; return RefreshBoards(); }

        private static async Task RefreshBoards()
        {
            foreach (ServerBoard board in UnityEngine.Object.FindObjectsOfType<ServerBoard>()) await board.RefreshServersList();
        }

        private static string FriendName(JObject friend)
        { return Clean((string)friend["name"]) + ((bool?)friend["online"] == true ? " (online)" : " (offline)"); }
        private static string InviteName(JObject invite)
        { return "Invite: " + Clean((string)invite["from_name"]) + " - " + Clean((string)invite.SelectToken("server.name")); }
        private static string Clean(string value) { return MenuMod.SafeText(value); }

        private static async void Run(Func<Task> action)
        {
            try { await action(); }
            catch (Exception error) { PopupManager.Show("Tavern friends", Clean(error.Message)); }
        }
    }
}
