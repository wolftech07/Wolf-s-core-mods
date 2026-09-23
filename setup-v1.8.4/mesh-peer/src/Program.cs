// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace TavernNativeMenu
{
    // Standalone companion process. It uses no game, Unity, or MelonLoader
    // assemblies. The game exchanges only JSON commands and public snapshots.
    internal static class Program
    {
        private static readonly BlockingCollection<JObject> Commands = new BlockingCollection<JObject>(100);
        private static volatile bool closing, dirty = true;
        private static readonly object OutputGate = new object();
        public static int Main(string[] args)
        {
            if (args.Length != 3 || args[0] != "--stdio-v2") return 2;
            string game = args[1];
            string name = args[2];
            try
            {
                if (String.IsNullOrWhiteSpace(game) || !Directory.Exists(game)) throw new InvalidDataException("The game folder is missing.");
                MeshPeerRuntime.Changed += delegate { dirty = true; };
                MeshPeerRuntime.PairVerified += delegate(string key, string nonce) { Write(new JObject { { "type", "paired" }, { "key", key }, { "nonce", nonce } }); };
                MeshPeerRuntime.PeerVerified += delegate(string key, string nonce) { Write(new JObject { { "type", "verified" }, { "key", key }, { "nonce", nonce } }); };
                MeshPeerRuntime.Initialize(game, name);
                var input = new Thread(ReadCommands) { IsBackground = true, Name = "Tavern mesh commands" }; input.Start();
                DateTime next = DateTime.MinValue;
                while (!closing)
                {
                    MeshPeerRuntime.Tick();
                    JObject command; for (int n = 0; n < 16 && Commands.TryTake(out command); n++) Dispatch(command);
                    if (dirty && DateTime.UtcNow >= next) { Snapshot(); dirty = false; next = DateTime.UtcNow.AddMilliseconds(250); }
                    Thread.Sleep(20);
                }
                return 0;
            }
            catch (Exception error) { Console.Error.WriteLine(error.Message); return 1; }
            finally { MeshPeerRuntime.Shutdown(); }
        }
        private static void ReadCommands()
        {
            try
            {
                while (!closing)
                {
                    string line = ReadBounded(Console.In, 16384);
                    if (line == null) break;
                    JObject command;
                    using (var reader = new JsonTextReader(new StringReader(line)) { MaxDepth = 8 })
                    { command = JObject.Load(reader); if (reader.Read()) throw new InvalidDataException("Invalid command."); }
                    Commands.Add(command);
                }
            }
            catch (Exception error) { Console.Error.WriteLine("Command stream closed: " + error.Message); }
            finally { closing = true; }
        }
        internal static string ReadBounded(TextReader input, int limit)
        {
            var text = new System.Text.StringBuilder();
            for (int c; (c = input.Read()) != -1;)
            {
                if (c == '\n') return text.ToString();
                if (text.Length >= limit) throw new InvalidDataException("Message exceeds the size limit.");
                if (c != '\r') text.Append((char)c);
            }
            return text.Length == 0 ? null : text.ToString();
        }
        private static void Dispatch(JObject command)
        {
            long id = (long?)command["id"] ?? 0;
            var response = new JObject { { "type", "reply" }, { "id", id } };
            try
            {
                switch ((string)command["action"])
                {
                    case "request": MeshPeerRuntime.RequestFriendAsync((string)command["code"]).GetAwaiter().GetResult(); break;
                    case "accept-request": MeshPeerRuntime.AcceptRequestAsync((string)command["key"]).GetAwaiter().GetResult(); break;
                    case "remove": MeshPeerRuntime.RemoveFriendAsync((string)command["key"]).GetAwaiter().GetResult(); break;
                    case "block": MeshPeerRuntime.BlockPeerAsync((string)command["code"], (string)command["name"]).GetAwaiter().GetResult(); break;
                    case "unblock": MeshPeerRuntime.UnblockPeerAsync((string)command["key"]).GetAwaiter().GetResult(); break;
                    case "verify": MeshPeerRuntime.VerifyPeerAsync((string)command["code"], (string)command["nonce"], (long)command["expires"]).GetAwaiter().GetResult(); break;
                    case "pair": MeshPeerRuntime.PairCardAsync((string)command["code"], (string)command["name"], (string)command["nonce"], (long)command["expires"]).GetAwaiter().GetResult(); break;
                    case "invite": MeshPeerRuntime.SendInviteAsync((string)command["key"], command["server"].ToObject<ServerEntry>()).GetAwaiter().GetResult(); break;
                    case "accept-invite": response["result"] = JObject.FromObject(MeshPeerRuntime.AcceptInviteAsync((string)command["invite_id"]).GetAwaiter().GetResult()); break;
                    case "dismiss": MeshPeerRuntime.DismissInviteAsync((string)command["invite_id"]).GetAwaiter().GetResult(); break;
                    case "refresh": dirty = true; break;
                    case "shutdown": closing = true; break;
                    default: throw new ArgumentException("Unknown friends command.");
                }
                response["ok"] = true;
                // A completed command's snapshot precedes its reply. Menus can
                // immediately refresh without seeing stale request state.
                Snapshot(); dirty = false;
            }
            catch (Exception error) { response["ok"] = false; response["error"] = error.Message; }
            Write(response);
        }
        private static void Snapshot()
        {
            Write(new JObject { { "type", "state" }, { "v", 2 }, { "ready", MeshPeerRuntime.Configured }, { "connected", MeshPeerRuntime.Connected },
                { "address", MeshPeerRuntime.Address }, { "error", MeshPeerRuntime.LastError },
                { "friends", MeshPeerRuntime.GetFriendsAsync().GetAwaiter().GetResult() },
                { "requests", MeshPeerRuntime.GetRequestsAsync().GetAwaiter().GetResult() },
                { "blocked", MeshPeerRuntime.GetBlockedAsync().GetAwaiter().GetResult() },
                { "invites", MeshPeerRuntime.GetInvitesAsync().GetAwaiter().GetResult() } });
        }
        private static void Write(JObject value) { lock (OutputGate) { Console.Out.WriteLine(value.ToString(Formatting.None)); Console.Out.Flush(); } }
    }
    internal sealed class ServerEntry
    {
        public string Name, Host, Kind;
        public int GamePort, AuthPort;
        public bool Private, Favorite;
    }
    internal static class ServerCatalog
    {
        internal static bool ValidEntry(ServerEntry server)
        {
            if (server == null || String.IsNullOrWhiteSpace(server.Host) || server.Host.Length > 253 || server.Host != server.Host.Trim() ||
                server.GamePort < 1 || server.GamePort > 65535 || server.AuthPort < 1 || server.AuthPort > 65535) return false;
            UriHostNameType kind = Uri.CheckHostName(server.Host);
            return kind == UriHostNameType.Dns || kind == UriHostNameType.IPv4;
        }
    }
}
