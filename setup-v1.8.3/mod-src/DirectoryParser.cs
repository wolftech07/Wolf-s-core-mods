using System;
using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json.Linq;

namespace TavernNativeMenu
{
    internal static class DirectoryParser
    {
        // 1.8.2 publishes address as host:gamePort, not a bare host.
        internal static List<ServerEntry> Parse(JArray rows)
        {
            var result = new List<ServerEntry>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (JObject row in rows)
            {
                string address = Text(row["address"] ?? row["host"] ?? row["ip"]);
                string host;
                int port;
                if (!TryAddress(address, Port(row["game_port"] ?? row["port"], 1757), out host, out port)) continue;
                string kind = Text(row["kind"]);
                if (String.IsNullOrEmpty(kind)) kind = "official";
                if (kind != "official" && kind != "headless") continue;
                int authPort = Port(row["auth_port"], 1762);
                if (authPort == 0 || !seen.Add(host + ":" + port + ":" + authPort)) continue;
                string name = Text(row["name"] ?? row["server_name"]);
                bool password;
                Boolean.TryParse(Text(row["has_password"]), out password);
                result.Add(new ServerEntry { Name = String.IsNullOrWhiteSpace(name) ? host : name,
                    Description = Text(row["description"] ?? row["server_description"] ?? row["motd"]),
                    Host = host, GamePort = port, AuthPort = authPort, Kind = kind,
                    PlayerCount = Count(row["player_count"]), PlayerLimit = Count(row["player_limit"]), HasPassword = password });
            }
            return result;
        }

        internal static bool TryAddress(string address, int defaultPort, out string host, out int port)
        {
            host = (address ?? "").Trim();
            port = defaultPort;
            int colon = host.LastIndexOf(':');
            if (colon >= 0)
            {
                if (host.IndexOf(':') != colon) return false; // matches launcher's IPv4 transport
                port = Port(new JValue(host.Substring(colon + 1)), 0);
                host = host.Substring(0, colon).Trim();
            }
            UriHostNameType type = Uri.CheckHostName(host);
            return (type == UriHostNameType.IPv4 || type == UriHostNameType.Dns) && port > 0 && port <= 65535;
        }

        internal static int Port(JToken value, int fallback)
        {
            if (value == null || value.Type == JTokenType.Null) return fallback;
            int port;
            return Int32.TryParse(Text(value), NumberStyles.None, CultureInfo.InvariantCulture, out port) && port > 0 && port <= 65535 ? port : 0;
        }

        private static int? Count(JToken value)
        {
            int count;
            return Int32.TryParse(Text(value), out count) && count >= 0 && count <= 10000 ? (int?)count : null;
        }
        private static string Text(JToken value) { return value == null || value.Type == JTokenType.Null ? "" : value.ToString(); }
    }
}
