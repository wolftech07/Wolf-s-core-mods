using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace TavernNativeMenu
{
    internal sealed class ServerEntry
    {
        public string Name;
        public string Host;
        public int GamePort = 1757;
        public int AuthPort = 1762;
        public bool Favorite;
        public string Kind = "official";
        public int? PlayerCount;
        public int? PlayerLimit;
        public bool HasPassword;
        public bool Private;
    }

    // Settings are imported read-only. Token creation is deliberately separate and
    // happens only when the player actually joins a previously unused server.
    internal sealed class LauncherProfile
    {
        public string Username;
        public string GameExe;
        public string Platform;
        public string SourcePath;
        public string TokenDirectory;
        public List<ServerEntry> Servers = new List<ServerEntry>();

        public static LauncherProfile Load(string configOverride)
        {
            if (String.IsNullOrWhiteSpace(configOverride))
                configOverride = Environment.GetEnvironmentVariable("TAVERN_NATIVE_MENU_LAUNCHER_CONFIG");
            string path = String.IsNullOrWhiteSpace(configOverride)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TheModdingTavern", "tavern_launcher.json")
                : Environment.ExpandEnvironmentVariables(configOverride);
            path = Path.GetFullPath(path);
            if (!File.Exists(path))
                throw new FileNotFoundException("Tavern Launcher settings were not found. Save your username and game path in Tavern Launcher first, or set LauncherConfigPath.", path);

            JObject config;
            try { config = JObject.Parse(File.ReadAllText(path)); }
            catch (Exception ex) { throw new InvalidDataException("Cannot read Tavern Launcher settings at " + path + ".", ex); }
            var profile = new LauncherProfile {
                Username = Text(config, "username").Trim(),
                GameExe = Text(config, "game_exe"),
                Platform = Text(config, "platform"),
                SourcePath = path,
                TokenDirectory = Path.Combine(Path.GetDirectoryName(path), "tokens")
            };
            if (String.IsNullOrWhiteSpace(profile.Username))
                throw new InvalidDataException("Tavern Launcher has no saved username. Enter and save the username you use on your servers before starting the game.");
            if (String.IsNullOrWhiteSpace(profile.Platform)) profile.Platform = "SteamVR";
            profile.ImportServers(config["saved_servers"] as JArray, true);
            profile.ImportServers(config["recent_servers"] as JArray, false);
            string lastHost = Text(config, "last_ip").Trim();
            if (lastHost.Length > 0)
                profile.AddServer(new ServerEntry { Name = lastHost, Host = lastHost, GamePort = Port(config["last_port"], 1757) });
            return profile;
        }

        private void ImportServers(JArray entries, bool favorite)
        {
            if (entries == null) return;
            foreach (JToken item in entries)
            {
                JObject entry = item as JObject;
                if (entry == null) continue;
                string host = Text(entry, "ip").Trim();
                if (host.Length == 0) continue;
                string name = Text(entry, "name").Trim();
                AddServer(new ServerEntry { Name = name.Length == 0 ? host : name,
                    Host = host, GamePort = Port(entry["port"], 1757), Favorite = favorite });
            }
        }

        private void AddServer(ServerEntry entry)
        {
            foreach (ServerEntry existing in Servers)
            {
                if (String.Equals(existing.Host, entry.Host, StringComparison.OrdinalIgnoreCase) && existing.GamePort == entry.GamePort)
                {
                    existing.Favorite = existing.Favorite || entry.Favorite;
                    return;
                }
            }
            Servers.Add(entry);
        }

        // The 1.8.2 launcher keys tokens by resolved IPv4, rather than the DNS
        // name displayed in the server list. Pass the same resolved address here.
        public string GetOrCreateToken(string resolvedHost)
        {
            if (String.IsNullOrWhiteSpace(resolvedHost)) throw new ArgumentException("A server address is required.", "resolvedHost");
            resolvedHost = resolvedHost.Trim();
            string path = Path.Combine(TokenDirectory, ".token_" + SafePart(resolvedHost) + "__" + SafePart(Username) + ".json");
            if (File.Exists(path)) return ReadToken(path, resolvedHost, true);

            string legacy = Path.Combine(TokenDirectory, ".token_" + SafePart(Username) + ".json");
            string token;
            if (File.Exists(legacy)) token = ReadToken(legacy, resolvedHost, false);
            else
            {
                byte[] bytes = new byte[18];
                using (RandomNumberGenerator random = RandomNumberGenerator.Create()) random.GetBytes(bytes);
                token = Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
            }
            Directory.CreateDirectory(TokenDirectory);
            var record = new JObject();
            record["username"] = Username;
            record["host"] = resolvedHost;
            record["token"] = token;
            // Finish writing before publishing, and never replace an existing
            // token, including one another launcher creates at the same time.
            string temporary = Path.Combine(TokenDirectory, ".new-token-" + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
                    writer.Write(record.ToString(Formatting.Indented));
                try { File.Move(temporary, path); }
                catch (IOException) { if (!File.Exists(path)) throw; }
                return ReadToken(path, resolvedHost, true);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }

        public string GetToken(string resolvedHost) { return GetOrCreateToken(resolvedHost); }

        private string ReadToken(string path, string host, bool checkHost)
        {
            JObject record;
            try { record = JObject.Parse(File.ReadAllText(path)); }
            catch (Exception ex) { throw new InvalidDataException("The existing Tavern token file cannot be read. Restore its backup before joining; it has not been replaced. File: " + path, ex); }
            if (!String.Equals(Text(record, "username"), Username, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("An existing Tavern token belongs to another username. It has not been replaced. File: " + path);
            if (checkHost && !String.Equals(Text(record, "host"), host, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("An existing Tavern token belongs to another server address. It has not been replaced. File: " + path);
            string token = Text(record, "token");
            if (String.IsNullOrWhiteSpace(token) || token.Length > 128)
                throw new InvalidDataException("The existing Tavern token is empty or invalid. Restore its backup before joining; it has not been replaced. File: " + path);
            return token;
        }

        internal static string SafePart(string value)
        {
            var result = new StringBuilder();
            foreach (char ch in (value ?? "").ToLowerInvariant())
            {
                UnicodeCategory category = Char.GetUnicodeCategory(ch);
                if (Char.IsLetterOrDigit(ch) || category == UnicodeCategory.LetterNumber || category == UnicodeCategory.OtherNumber || ch == '-' || ch == '_') result.Append(ch);
            }
            return result.Length == 0 ? "x" : result.ToString();
        }

        private static string Text(JObject obj, string name)
        {
            JToken value = obj[name];
            return value == null || value.Type == JTokenType.Null ? "" : value.ToString();
        }

        private static int Port(JToken token, int fallback)
        {
            int value;
            return token != null && Int32.TryParse(token.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value) && value >= 1 && value <= 65535 ? value : fallback;
        }
    }
}
