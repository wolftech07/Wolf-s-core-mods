using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Alta.Api.DataTransferModels.Models.Responses;
using HarmonyLib;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace TavernNativeMenu
{
    internal sealed class MenuSettingsFile
    {
        public string LauncherConfig = null;
        public string DirectoryUrl = "http://themoddingtavern.com:1763/servers";
        public List<ServerEntry> Servers = new List<ServerEntry>();
    }

    internal sealed class MenuServer : DevGameServerInfo
    {
        internal ServerEntry Entry;
        internal string MenuAction;
    }

    internal sealed class ServerCatalog
    {
        internal LauncherProfile Profile;
        internal MenuSettingsFile Settings;
        internal string LastDirectoryError;
        private readonly string settingsPath;
        private List<ServerEntry> community = new List<ServerEntry>();
        private DateTime refreshed = DateTime.MinValue;
        private Task refreshTask;
        private static readonly Dictionary<string, int> identifiers = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        internal ServerCatalog(string gamePath)
        {
            settingsPath = Path.Combine(gamePath, "UserData", "TavernNativeMenu.json");
            Settings = File.Exists(settingsPath)
                ? JsonConvert.DeserializeObject<MenuSettingsFile>(File.ReadAllText(settingsPath))
                : new MenuSettingsFile();
            if (Settings == null) throw new InvalidDataException("TavernNativeMenu.json is empty or invalid.");
            if (Settings.Servers == null) Settings.Servers = new List<ServerEntry>();
            Profile = LauncherProfile.Load(Settings.LauncherConfig);
            if (!File.Exists(settingsPath)) Save();
        }

        internal async Task<IEnumerable<GameServerInfo>> GetServers(ServerBoardType board)
        {
            if (!NativeFriendsMenu.ChoosingInvitation && (board == ServerBoardType.PublicServer || board == ServerBoardType.DiscoverServers))
            {
                if (refreshTask == null || (refreshTask.IsCompleted && DateTime.UtcNow - refreshed > TimeSpan.FromSeconds(25)))
                    refreshTask = RefreshCommunity();
                await refreshTask;
            }
            var saved = Settings.Servers.Concat(Profile.Servers).Where(ValidEntry).ToList();
            IEnumerable<ServerEntry> selected;
            if (NativeFriendsMenu.ChoosingInvitation) selected = saved;
            else if (board == ServerBoardType.MyServers) selected = saved.Where(x => x.Favorite);
            else if (board == ServerBoardType.OpenServers) selected = saved;
            else selected = community;
            var list = selected.GroupBy(Key, StringComparer.OrdinalIgnoreCase).Select(g => g.First())
                .Select(ToMenuServer).Cast<GameServerInfo>().ToList();
            list.Add(ActionServer("Add a private server...", "Save a server address for yourself using the VR keyboard. This does not publish it on the community dashboard.", "add", 2000000001));
            if (NativeFriendsMenu.ChoosingInvitation)
                list.Add(ActionServer("Cancel invitation", "Return to joining servers.", "cancel-invite", 2000000002));
            // Native SelectFirst chooses the final spline element. Let the
            // native reversal put the first actual server there, not Add Server.
            return list;
        }

        internal async Task ResolveKind(ServerEntry entry, string resolvedHost)
        {
            if (entry.Private) return;
            if (entry.Kind == "headless") return;
            if (refreshTask == null || (refreshTask.IsCompleted && DateTime.UtcNow - refreshed > TimeSpan.FromSeconds(25)))
                refreshTask = RefreshCommunity();
            await refreshTask;
            // Favorites in the original launcher do not persist 'kind'. Reuse
            // current directory metadata so saved headless servers also join.
            var match = community.FirstOrDefault(x => x.GamePort == entry.GamePort &&
                (String.Equals(x.Host, entry.Host, StringComparison.OrdinalIgnoreCase) ||
                 String.Equals(x.Host, resolvedHost, StringComparison.OrdinalIgnoreCase)));
            if (match != null && match.Kind == "headless") entry.Kind = "headless";
        }

        internal void Remember(ServerEntry entry)
        {
            if (!Settings.Servers.Any(x => String.Equals(Key(x), Key(entry), StringComparison.OrdinalIgnoreCase)))
            {
                Settings.Servers.Add(entry);
                Save();
            }
        }

        private async Task RefreshCommunity()
        {
            try
            {
                var rows = await TavernWire.GetDirectoryAsync(Settings.DirectoryUrl);
                community = DirectoryParser.Parse(rows);
                LastDirectoryError = null;
            }
            catch (Exception ex)
            {
                // Keep the last usable directory and all locally saved entries on network failure.
                LastDirectoryError = ex.Message;
            }
            finally { refreshed = DateTime.UtcNow; }
        }

        internal void Add(ServerEntry entry)
        {
            if (!ValidEntry(entry)) throw new ArgumentException("Enter a valid server address and ports.");
            int index = Settings.Servers.FindIndex(x => String.Equals(Key(x), Key(entry), StringComparison.OrdinalIgnoreCase));
            if (index < 0) Settings.Servers.Add(entry);
            else
            {
                entry.Favorite |= Settings.Servers[index].Favorite;
                entry.Private |= Settings.Servers[index].Private;
                Settings.Servers[index] = entry;
            }
            Save();
        }

        private void Save()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(settingsPath));
            string temporary = settingsPath + ".tmp";
            File.WriteAllText(temporary, JsonConvert.SerializeObject(Settings, Formatting.Indented));
            if (File.Exists(settingsPath)) File.Replace(temporary, settingsPath, settingsPath + ".bak");
            else File.Move(temporary, settingsPath);
        }

        internal static int Port(JToken value, int fallback)
        {
            return DirectoryParser.Port(value, fallback);
        }

        internal static bool ValidEntry(ServerEntry entry)
        {
            if (entry == null || String.IsNullOrWhiteSpace(entry.Host) || entry.GamePort < 1 || entry.GamePort > 65535 || entry.AuthPort < 1 || entry.AuthPort > 65535) return false;
            var type = Uri.CheckHostName(entry.Host.Trim());
            return type == UriHostNameType.IPv4 || type == UriHostNameType.Dns;
        }

        internal static string Key(ServerEntry entry) { return entry.Host.Trim().ToLowerInvariant() + ":" + entry.GamePort + ":" + entry.AuthPort; }

        internal static MenuServer ToMenuServer(ServerEntry entry)
        {
            int id;
            if (!identifiers.TryGetValue(Key(entry), out id)) identifiers[Key(entry)] = id = identifiers.Count + 1;
            var info = new MenuServer { Entry = entry, Identifier = id,
                Name = (NativeFriendsMenu.ChoosingInvitation ? "Invite " + NativeFriendsMenu.InvitationName + ": " : "") + MenuMod.SafeText(entry.Name ?? entry.Host), Target = 0, SceneIndex = 0,
                OnlinePlayers = new UserInfo[0], Playability = 0.5f,
                Description = MenuMod.SafeText(entry.Host) + ":" + entry.GamePort
                    + (entry.PlayerCount.HasValue ? "\nPlayers: " + entry.PlayerCount + (entry.PlayerLimit.HasValue ? "/" + entry.PlayerLimit : "") : "")
                    + (entry.HasPassword ? "\nPassword required." : "")
                    + (entry.Private ? "\nPrivate saved entry (not published by this mod)." : "")
                    + "\n" + (entry.Kind == "headless" ? "Headless server (Tavern direct join)." : "Tavern server. Password and access checks run when joining.")
                    + (NativeFriendsMenu.ChoosingInvitation ? "\nSelect the orb to send this address to your friend." : "\nSelect the join orb to connect."),
                ConnectionInfo = new ConnectionInfo { Address = IPAddress.Loopback, GamePort = entry.GamePort } };
            return info;
        }

        private static MenuServer ActionServer(string name, string description, string action, int id)
        {
            return new MenuServer { Name = name, Description = description, MenuAction = action, Identifier = id,
                Target = 0, SceneIndex = 0, OnlinePlayers = new UserInfo[0], Playability = 1 };
        }
    }
}
