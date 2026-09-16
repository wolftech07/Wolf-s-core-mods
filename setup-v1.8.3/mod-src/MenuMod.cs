using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Alta.Api.DataTransferModels.Models.Responses;
using HarmonyLib;
using MelonLoader;
using Newtonsoft.Json.Linq;
using UnityEngine;

[assembly: MelonInfo(typeof(TavernNativeMenu.MenuMod), "Tavern Native Menu", "1.0.2", "TavernNativeMenu contributors")]
[assembly: MelonGame(null, "A Township Tale")]

namespace TavernNativeMenu
{
    public sealed class MenuMod : MelonMod
    {
        internal static ServerCatalog Catalog;
        private static string startupError;
        private static bool joining;
        private static bool allowNativeJoin;
        private static Task joinTask;
        private static HarmonyLib.Harmony patches;
        private static readonly BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic;

        public override void OnInitializeMelon()
        {
            if (CommandLineArguments.Contains("/start_server")) return;
            try
            {
                Catalog = new ServerCatalog(Path.GetDirectoryName(Application.dataPath));
                if (!AppDomain.CurrentDomain.GetAssemblies().Any(x => x.GetName().Name == "TavernLib"))
                    throw new InvalidOperationException("TavernLib is missing. Install the Tavern Launcher client patch before this mod.");
                patches = new HarmonyLib.Harmony("tavern.native-menu");
                WheelInteraction.Install(patches);
                Hook(typeof(ServerBoard), "RefreshServersList", "BeforeRefresh", null);
                Hook(typeof(ServerBoard), "LoadServers", "BeforeLoadServers", null);
                Hook(typeof(ServerSelectionMenu), "Start", "BeforeMenuStart", "AfterMenuStart");
                Hook(typeof(ServerElement), "SetupForServer", null, "AfterElementSetup");
                Hook(typeof(VrMainMenu), "JoinServer", "BeforeJoin", null);
                Hook(typeof(Features.ServerBoardFilter), "Filter", "BeforeFilter", null);
                LifecycleHooks.Install(patches);
                TavernSocialClient.Initialize(Path.GetDirectoryName(Application.dataPath), Catalog.Profile.Username);
                NativeFriendsMenu.Install(patches);
                MelonLogger.Msg("[Tavern Native Menu] Ready. Community servers will appear in the original game picker.");
            }
            catch (Exception ex)
            {
                startupError = ex.Message;
                MelonLogger.Error("[Tavern Native Menu] Initialization failed: " + ex);
                if (patches != null) patches.UnpatchSelf();
            }
        }

        public override void OnUpdate()
        {
            NativePrompt.CheckLifetime();
            if (startupError != null || Catalog == null) return;
            TavernSocialClient.Tick();
            NativeFriendsMenu.Tick();
        }
        public override void OnLateInitializeMelon()
        {
            if (startupError != null) PopupManager.Show("Tavern Native Menu could not start", SafeText(startupError));
        }

        private static void Hook(Type type, string method, string prefix, string postfix)
        {
            MethodInfo original = AccessTools.Method(type, method);
            if (original == null) throw new MissingMethodException(type.FullName, method);
            patches.Patch(original, prefix == null ? null : new HarmonyMethod(typeof(MenuMod), prefix),
                postfix == null ? null : new HarmonyMethod(typeof(MenuMod), postfix));
        }

        private static void BeforeMenuStart(ServerSelectionMenu __instance)
        {
            Set(__instance, "startingBoard", ServerBoardType.PublicServer);
            foreach (ServerBoard board in Get<ServerBoard[]>(__instance, "boards"))
                Set(board, "headingText", Heading(board.Type));
        }

        private static void AfterMenuStart(ServerSelectionMenu __instance)
        {
            if (__instance.CurrentBoard == null) AccessTools.Method(typeof(ServerSelectionMenu), "Setup").Invoke(__instance, null);
            MelonLogger.Msg("[Tavern Native Menu] Native picker initialized: community, favorites, saved servers, and discovery.");
        }

        private static string Heading(ServerBoardType type)
        {
            switch (type)
            {
                case ServerBoardType.MyServers: return "Tavern Favorites";
                case ServerBoardType.OpenServers: return "Tavern Saved / Recent";
                case ServerBoardType.DiscoverServers: return "Discover Tavern Servers";
                default: return "Tavern Community Servers";
            }
        }

        private static bool BeforeLoadServers(ServerBoard __instance, ref IEnumerator __result)
        {
            __result = UpdateLoop(__instance);
            return false;
        }

        private static IEnumerator UpdateLoop(ServerBoard board)
        {
            while (board != null && board.isActiveAndEnabled)
            {
                Task refresh = board.RefreshServersList();
                while (!refresh.IsCompleted && board != null) yield return null;
                for (int remaining = 30; remaining > 0 && board != null; remaining--)
                {
                    TextRenderer counter = Get<TextRenderer>(board, "updateCountdownText");
                    if (counter != null)
                        counter.Text = Catalog.LastDirectoryError != null && (board.Type == ServerBoardType.PublicServer || board.Type == ServerBoardType.DiscoverServers)
                            ? "Directory unavailable. Retrying in " + remaining + "s; saved servers still available."
                            : "Updating in " + remaining + "s";
                    yield return new WaitForSeconds(1);
                }
            }
        }

        private static bool BeforeRefresh(ServerBoard __instance, ref Task __result)
        {
            __result = Refresh(__instance);
            return false;
        }

        private static async Task Refresh(ServerBoard board)
        {
            try
            {
                IEnumerable<GameServerInfo> servers = await Catalog.GetServers(board.Type);
                if (board == null || NativePrompt.Active) return;
                Set(board, "lastReceivedServers", servers);
                AccessTools.Method(typeof(ServerBoard), "FilterServers").Invoke(board, null);
                var finished = Get<Action>(board, "OnRefreshFinished");
                Set(board, "OnRefreshFinished", null);
                if (finished != null) finished();
            }
            catch (Exception ex)
            {
                MelonLogger.Error("[Tavern Native Menu] Server list update failed: " + ex.Message);
                if (board != null)
                {
                    var counter = Get<TextRenderer>(board, "updateCountdownText");
                    if (counter != null) counter.Text = "Server list failed to load. See MelonLoader/Latest.log.";
                }
            }
        }

        private static bool BeforeFilter(IEnumerable<GameServerInfo> servers, ref IEnumerable<GameServerInfo> __result)
        {
            if (!NativePrompt.Active) return true;
            __result = servers;
            return false;
        }

        private static void AfterElementSetup(ServerElement __instance, GameServerInfo server)
        {
            var item = server as MenuServer;
            if (item == null) return;
            var text = Get<TextRenderer>(__instance, "numberOfPlayersText");
            if (text != null) text.Text = item.MenuAction != null ? "+" : item.Entry.PlayerCount.HasValue ? item.Entry.PlayerCount.Value.ToString() : "?";
        }

        private static bool BeforeJoin(VrMainMenu __instance, GameServerInfo serverInfo)
        {
            if (allowNativeJoin) return true;
            var item = serverInfo as MenuServer;
            if (item == null) return true;
            if (joining) return false;
            joining = true;
            joinTask = Join(__instance, item);
            return false;
        }

        private static async Task Join(VrMainMenu menu, MenuServer item)
        {
            try
            {
                if (item.MenuAction == "add") { await AddServer(); return; }
                if (item.MenuAction == "cancel-invite") { await NativeFriendsMenu.CancelInvitation(); return; }
                ServerEntry entry = item.Entry;
                if (NativeFriendsMenu.ChoosingInvitation) { await NativeFriendsMenu.SendSelected(entry); return; }
                Status("Contacting " + SafeText(entry.Name ?? entry.Host) + "...");
                IPAddress address = await Resolve(entry.Host);
                await Catalog.ResolveKind(entry, address.ToString());
                int gamePort = entry.GamePort;
                int sceneIndex = 0;
                int userId;
                string token = "";
                if (String.Equals(entry.Kind, "headless", StringComparison.OrdinalIgnoreCase))
                {
                    // The launcher uses this path ONLY for explicitly listed headless servers.
                    userId = HeadlessUserId(Catalog.Profile.Username);
                }
                else
                {
                    var pingRequest = new JObject();
                    pingRequest["ping"] = true;
                    JObject ping = await TavernWire.ExchangeAsync(address.ToString(), entry.AuthPort, pingRequest);
                    if ((string)ping["status"] != "pong") throw new InvalidDataException("The server did not return a Tavern status response.");
                    gamePort = ServerCatalog.Port(ping["game_port"], gamePort);
                    if (gamePort == 0) throw new InvalidDataException("The server advertised an invalid game port.");
                    token = Catalog.Profile.GetOrCreateToken(address.ToString());
                    string password = null;
                    if ((bool?)ping["password_required"] == true)
                    {
                        password = await NativePrompt.Ask("Server password", "Enter the password for " + SafeText(entry.Name ?? entry.Host) + ".", true);
                        if (password == null) return;
                    }
                    while (true)
                    {
                        var request = new JObject();
                        request["username"] = Catalog.Profile.Username;
                        request["token"] = token;
                        request["password"] = password == null ? "" : PasswordHash(password);
                        JObject response = await TavernWire.ExchangeAsync(address.ToString(), entry.AuthPort, request);
                        string result = (string)response["status"];
                        if (result == "needs_password" || result == "wrong_password")
                        {
                            password = await NativePrompt.Ask("Server password", result == "wrong_password" ? "That password was rejected. Try again or close this board." : "This server requires a password.", true);
                            if (password == null) return;
                            continue;
                        }
                        if (result != "ok") throw new InvalidOperationException(AuthError(result, (string)response["message"]));
                        if (!Int32.TryParse((string)response["user_id"], out userId) || userId <= 0)
                            throw new InvalidDataException("The server returned an invalid player ID.");
                        // Tavern 1.8.3 normally adds /questScene before launching.
                        // Here the server is selected after launch, so pass the
                        // requested scene through the game's own join descriptor.
                        // Keep this local to each join so it cannot leak to the next server.
                        sceneIndex = (bool?)response["quest_scene_required"] == true ? 4 : 0;
                        break;
                    }
                }
                if (menu == null || GameModeManager.CurrentMode != null) return;
                TavernIdentity.Apply(Catalog.Profile.Username, userId, token);
                try { Catalog.Remember(entry); }
                catch (IOException) { MelonLogger.Warning("[Tavern Native Menu] Could not save the recent server; continuing to join."); }
                var target = DevGameServerInfo.GetDevServer(address.ToString(), gamePort, sceneIndex);
                target.Name = item.Name;
                target.OnlinePlayers = new UserInfo[0];
                // Use the original fade, loading scene, networking pipeline and failure return.
                allowNativeJoin = true;
                try { menu.JoinServer(target); }
                finally { allowNativeJoin = false; }
                var starting = Get<Task>(menu, "startingGameTask");
                if (starting != null) await starting;
                if (GameModeManager.CurrentMode == null) RecoverMenu(menu);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning("[Tavern Native Menu] Join failed: " + ex.GetType().Name + ": " + ex.Message);
                PopupManager.Show("Could not join server", SafeText(ex.Message));
                if (PlayerController.Current != null && PlayerController.Current.ScreenFader != null)
                    PlayerController.Current.ScreenFader.FadeBackIn(0);
                RecoverMenu(menu);
            }
            finally { joining = false; }
        }

        private static void RecoverMenu(VrMainMenu menu)
        {
            // VrMainMenu.StartGameAsync catches some exceptions without resuming
            // its board. Restore interaction and stop the old refresh coroutine
            // first, avoiding duplicate refresh loops on repeated failed joins.
            if (menu == null || GameModeManager.CurrentMode != null) return;
            var selection = Get<ServerSelectionMenu>(menu, "serverSelection");
            if (selection == null || selection.CurrentBoard == null) return;
            var board = selection.CurrentBoard;
            var loop = Get<Coroutine>(board, "loadServers");
            if (loop != null) board.StopCoroutine(loop);
            board.ResumeUpdate();
            if (PlayerController.Current != null && PlayerController.Current.ScreenFader != null)
                PlayerController.Current.ScreenFader.FadeBackIn(0);
        }

        internal static async Task AddServer()
        {
            string host = await NativePrompt.Ask("Add private server", "Enter an IPv4 address or hostname (no http://). Optionally add :gamePort. This is saved only on your computer.", false);
            if (host == null) return;
            host = host.Trim();
            int gamePort = 1757;
            int colon = host.LastIndexOf(':');
            if (colon >= 0)
            {
                if (host.IndexOf(':') != colon) throw new ArgumentException("Tavern Launcher uses IPv4. Enter an IPv4 address or hostname.");
                gamePort = ServerCatalog.Port(new JValue(host.Substring(colon + 1)), 0);
                host = host.Substring(0, colon);
            }
            host = host.Trim();
            var entry = new ServerEntry { Name = host, Host = host, GamePort = gamePort, Favorite = true, Private = true };
            if (!ServerCatalog.ValidEntry(entry)) throw new ArgumentException("Enter a valid IPv4 address or hostname and a game port from 1 to 65535.");
            string name = await NativePrompt.Ask("Private server name", "Give this server a name, or press Enter to use its address.", false);
            if (name == null) return;
            if (!String.IsNullOrWhiteSpace(name)) entry.Name = name.Trim();
            string authPort = await NativePrompt.Ask("Server authentication port", "Press Enter to use Tavern's default auth port (1762), or type the server's custom auth port.", false);
            if (authPort == null) return;
            entry.AuthPort = String.IsNullOrWhiteSpace(authPort) ? 1762 : ServerCatalog.Port(new JValue(authPort.Trim()), 0);
            Catalog.Add(entry);
            PopupManager.Show("Private server saved", SafeText(entry.Name) + " is now in Tavern Favorites and Tavern Saved / Recent. Its address was not published to the community dashboard. Server owners control any separate listing by their server.");
            foreach (var board in UnityEngine.Object.FindObjectsOfType<ServerBoard>()) await board.RefreshServersList();
        }

        private static async Task<IPAddress> Resolve(string host)
        {
            IPAddress literal;
            if (IPAddress.TryParse(host, out literal))
            {
                if (literal.AddressFamily != AddressFamily.InterNetwork) throw new ArgumentException("Tavern Launcher uses IPv4 addresses.");
                return literal;
            }
            Task<IPAddress[]> lookup = Dns.GetHostAddressesAsync(host);
            if (await Task.WhenAny(lookup, Task.Delay(5000)) != lookup) throw new TimeoutException("Server address lookup timed out.");
            IPAddress found = (await lookup).FirstOrDefault(x => x.AddressFamily == AddressFamily.InterNetwork);
            if (found == null) throw new InvalidOperationException("The server hostname has no IPv4 address.");
            return found;
        }

        private static string PasswordHash(string password)
        {
            using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(password))).Replace("-", "").ToLowerInvariant();
        }

        private static int HeadlessUserId(string username)
        {
            using (var sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(username.Trim().ToLowerInvariant()));
                long remainder = 0;
                foreach (byte value in hash) remainder = (remainder * 256 + value) % 999999999;
                return 1000000000 + (int)remainder;
            }
        }

        private static string AuthError(string status, string message)
        {
            if (status == "not_whitelisted") return "This username is not on the server's whitelist. Contact the server owner.";
            return String.IsNullOrWhiteSpace(message) ? "Tavern authentication was rejected (" + SafeText(status ?? "unknown response") + ")." : SafeText(message);
        }

        private static void Status(string text)
        {
            if (PlayerController.Current != null && PlayerController.Current.MessageDisplay != null) PlayerController.Current.MessageDisplay.Display(text, 5);
        }
        internal static string SafeText(string text)
        {
            if (text == null) return "";
            string clean = text.Replace('<', '‹').Replace('>', '›').Replace('\r', ' ');
            return clean.Length > 700 ? clean.Substring(0, 700) : clean;
        }
        internal static T Get<T>(object instance, string field) { return (T)instance.GetType().GetField(field, Fields).GetValue(instance); }
        internal static void Set(object instance, string field, object value) { instance.GetType().GetField(field, Fields).SetValue(instance, value); }
    }
}
