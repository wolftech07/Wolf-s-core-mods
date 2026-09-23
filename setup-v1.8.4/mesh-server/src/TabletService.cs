using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Alta.Networking;
using Alta.Networking.Servers;
using HarmonyLib;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace TavernNativeMeshServer
{
    internal sealed class TabletUser
    {
        internal int Id, Rank;
        internal string Name, Token;
    }
    internal static class TabletAuthority
    {
        private static object Property(object value, string name)
        { return value == null ? null : value.GetType().GetProperty(name).GetValue(value, null); }
        internal static Dictionary<int, TabletUser> Read()
        {
            // Roles come exclusively from Tavern's server service, never the identity JWT's Policy claims.
            Type services = AccessTools.TypeByName("TavernLib.Services.TavernServices");
            Type managerType = AccessTools.TypeByName("TavernLib.Backend.Api.TavernManager") ?? AccessTools.TypeByName("TavernLib.Backend.Api.TavernApiManager");
            if (services == null || managerType == null) throw new InvalidOperationException("Tavern server authority unavailable.");
            object manager = services.GetMethod("GetService").MakeGenericMethod(managerType).Invoke(null, null);
            object config = Property(manager, "UserConfig");
            if (config == null) throw new InvalidOperationException("Tavern server users unavailable.");
            // The launcher may have edited roles while the game was running.
            config.GetType().GetMethod("ReadFromFile").Invoke(config, null);
            var records = Property(Property(config, "LastRead"), "Users") as IDictionary;
            if (records == null) throw new InvalidOperationException("Tavern server users unavailable.");
            var result = new Dictionary<int, TabletUser>();
            foreach (DictionaryEntry record in records)
            {
                ulong id = Convert.ToUInt64(Property(record.Value, "UserId"));
                if (id == 0 || id > Int32.MaxValue) continue;
                if (result.ContainsKey((int)id)) throw new InvalidDataException("Duplicate server user identifier.");
                var roles = Property(record.Value, "Roles") as IEnumerable;
                result.Add((int)id, new TabletUser { Id = (int)id, Name = (string)record.Key,
                    Token = Property(record.Value, "Token") as string,
                    Rank = TabletPolicy.Rank(roles == null ? null : roles.Cast<object>().Select(x => x as string)) });
            }
            return result;
        }
        internal static bool Matches(TabletUser record, int id, string name)
        { return record != null && record.Id == id && String.Equals(record.Name, name, StringComparison.OrdinalIgnoreCase); }
    }
    internal sealed class TabletService
    {
        private sealed class Verification
        { internal Connection Issuer; internal string Request; }
        private sealed class Requests
        {
            internal readonly Dictionary<string, JObject> Replies = new Dictionary<string, JObject>(StringComparer.Ordinal);
            internal readonly Queue<string> Order = new Queue<string>();
            internal readonly HashSet<string> Pending = new HashSet<string>(StringComparer.Ordinal);
            internal int ReplyChars;
        }
        private readonly Companion owner;
        private readonly TabletStore store;
        private readonly PairRegistry proofs = new PairRegistry();
        private readonly Dictionary<Connection, Requests> requests = new Dictionary<Connection, Requests>();
        private static readonly MethodInfo Deny = AccessTools.Method(typeof(ServerPlayerConnectionHandlerOld), "PlayerDenied");
        private static readonly Type JoinType = typeof(Connection).Assembly.GetType("Alta.Networking.RequestJoinMessage", true);
        private static readonly MethodInfo ReadJoin = AccessTools.Method(JoinType, "Serialize");
        private static readonly PropertyInfo JoinCredentials = AccessTools.Property(JoinType, "UserCredentials");
        private static readonly PropertyInfo JoinPlayerId = AccessTools.Property(JoinType, "PlayerId");
        internal TabletService(Companion owner)
        {
            this.owner = owner;
            store = new TabletStore(Path.Combine(Path.GetDirectoryName(UnityEngine.Application.dataPath), "UserData", "TavernTabletServer.json"));
        }
        private static bool Live(Connection connection)
        { return connection != null && !connection.IsDisposed && connection.IsApproved && connection.Player != null && connection.Socket.IsServer; }
        private static Connection[] Online()
        { return Socket.Current == null || !Socket.Current.IsServer ? new Connection[0] : Socket.Current.Connections.Where(Live).ToArray(); }
        internal void Expire(DateTime now)
        { foreach (PendingPair pair in proofs.Expire(now)) FinishProof(pair, false, "Player identity verification expired."); }
        internal void Disconnected(Connection connection)
        {
            requests.Remove(connection);
            foreach (PendingPair pair in proofs.RemovePeer(connection)) FinishProof(pair, false, "A player disconnected during identity verification.");
        }
        internal void Request(Connection issuer, JObject packet)
        {
            string request = (string)packet["request_id"], action = (string)packet["action"];
            if (!Live(issuer) || !TabletPolicy.ValidRequestId(request)) return;
            Requests history;
            if (!requests.TryGetValue(issuer, out history)) { history = new Requests(); requests.Add(issuer, history); }
            JObject previous;
            if (history.Replies.TryGetValue(request, out previous)) { Companion.Send(issuer, previous); return; }
            if (history.Pending.Contains(request)) return;
            try
            {
                if (action == "verify") { Verify(issuer, request, packet); return; }
                var users = TabletAuthority.Read(); TabletUser actor;
                int actorId = issuer.Player.UserInfo.Identifier;
                if (!users.TryGetValue(actorId, out actor) || !TabletAuthority.Matches(actor, actorId, issuer.Player.UserInfo.Username))
                { Reply(issuer, request, false, "Your server account could not be verified.", null); return; }
                if (action == "roster") { Reply(issuer, request, true, "", Roster(actor, users, (int?)packet["player_offset"] ?? 0, (int?)packet["ban_offset"] ?? 0)); return; }
                if (action != "kick" && action != "ban" && action != "unban")
                { Reply(issuer, request, false, "Unknown tablet action.", null); return; }
                int targetId = (int?)packet["target_id"] ?? 0; TabletUser target;
                users.TryGetValue(targetId, out target);
                if (target == null && action == "unban" && store.IsBanned(targetId)) target = new TabletUser { Id = targetId, Rank = 0 };
                if (target == null || !TabletPolicy.CanTarget(actor.Id, actor.Rank, targetId, target.Rank))
                { Reply(issuer, request, false, "You do not have permission to moderate this player.", null); return; }
                if (action == "unban")
                {
                    bool removed = store.Unban(targetId);
                    Reply(issuer, request, removed, removed ? "Player unbanned." : "This player is not banned.", Roster(actor, users));
                    return;
                }
                Connection targetConnection = Online().FirstOrDefault(x => x.Player.UserInfo.Identifier == targetId);
                if (targetConnection == null || !TabletAuthority.Matches(target, targetId, targetConnection.Player.UserInfo.Username))
                { Reply(issuer, request, false, "This player is no longer on the server.", null); return; }
                // Failure to persist throws before any disconnect or success reply.
                if (action == "ban") store.Ban(targetId, targetConnection.Player.UserInfo.Username, actor.Id);
                targetConnection.Disconnect(action == "ban" ? "Banned by a server moderator." : "Kicked by a server moderator.");
                Reply(issuer, request, true, action == "ban" ? "Player banned." : "Player kicked.", Roster(actor, users));
            }
            catch (Exception ex)
            {
                owner.LoggerInstance.Warning("Tablet request failed: " + ex.GetType().Name);
                Reply(issuer, request, false, "The server could not complete this action. Try again.", null);
            }
        }
        private JObject Roster(TabletUser actor, Dictionary<int, TabletUser> users, int playerOffset = 0, int banOffset = 0)
        {
            Connection[] online = Online().OrderBy(x => x.Player.UserInfo.Identifier).ToArray();
            TabletBan[] allBans = actor.Rank > 0 ? store.Bans.OrderBy(x => x.Id).ToArray() : new TabletBan[0];
            playerOffset = Math.Max(0, Math.Min(playerOffset, online.Length));
            banOffset = Math.Max(0, Math.Min(banOffset, allBans.Length));
            var players = new JArray(); int playerBytes = 0, banBytes = 0;
            foreach (Connection connection in online.Skip(playerOffset).Take(256))
            {
                int id = connection.Player.UserInfo.Identifier; TabletUser target; users.TryGetValue(id, out target);
                PairPeer peer = owner.TabletPeer(connection);
                bool matched = TabletAuthority.Matches(target, id, connection.Player.UserInfo.Username);
                JObject entry = new JObject { { "id", id }, { "name", TrimName(connection.Player.UserInfo.Username) },
                    { "address", peer == null ? null : peer.Address },
                    { "can_target", matched && TabletPolicy.CanTarget(actor.Id, actor.Rank, id, target.Rank) } };
                int length = entry.ToString(Formatting.None).Length + 1;
                if (playerBytes + length > 44000) break;
                playerBytes += length; players.Add(entry);
            }
            var bans = new JArray();
            if (actor.Rank > 0)
                foreach (TabletBan ban in allBans.Skip(banOffset).Take(100))
                {
                    TabletUser target; users.TryGetValue(ban.Id, out target);
                    JObject entry = new JObject { { "id", ban.Id }, { "name", TrimName(ban.Name) },
                        { "can_target", TabletPolicy.CanTarget(actor.Id, actor.Rank, ban.Id, target == null ? 0 : target.Rank) } };
                    int length = entry.ToString(Formatting.None).Length + 1;
                    if (banBytes + length > 16000) break;
                    banBytes += length; bans.Add(entry);
                }
            return new JObject { { "players", players }, { "bans", bans }, { "can_moderate", actor.Rank > 0 },
                { "role", actor.Rank == 2 ? "owner" : actor.Rank == 1 ? "moderator" : "player" }, { "server_key", store.ServerKey },
                { "player_count", online.Length }, { "player_offset", playerOffset },
                { "next_player_offset", playerOffset + players.Count < online.Length ? playerOffset + players.Count : -1 },
                { "ban_count", allBans.Length }, { "ban_offset", banOffset },
                { "next_ban_offset", banOffset + bans.Count < allBans.Length ? banOffset + bans.Count : -1 } };
        }
        private static string TrimName(string name)
        { return String.IsNullOrEmpty(name) ? "Player" : name.Length > 64 ? name.Substring(0, 64) : name; }
        private void Verify(Connection issuer, string request, JObject packet)
        {
            int id = (int?)packet["target_id"] ?? 0;
            Connection target = Online().FirstOrDefault(x => x.Player.UserInfo.Identifier == id);
            PairPeer left = owner.TabletPeer(issuer), right = owner.TabletPeer(target);
            if (left == null || right == null || ReferenceEquals(issuer, target))
            { Reply(issuer, request, false, "Both players must have mesh connected to verify an identity.", null); return; }
            string expectedAddress = (string)packet["address"];
            if (expectedAddress != null && AddressCodec.Normalize(expectedAddress) != right.Address)
            { Reply(issuer, request, false, "The player's mesh identity changed. Refresh the player list.", null); return; }
            PendingPair pair = proofs.Create(left, right, new Verification { Issuer = issuer, Request = request }, DateTime.UtcNow);
            if (pair == null) { Reply(issuer, request, false, "An identity check is already pending for one of these players.", null); return; }
            requests[issuer].Pending.Add(request);
            SendProof(pair.Left, pair.Right, pair); SendProof(pair.Right, pair.Left, pair);
        }
        private static void SendProof(PairPeer receiver, PairPeer other, PendingPair pair)
        {
            long expires = (long)(pair.Expires - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
            Companion.Send((Connection)receiver.Connection, new JObject { { "v", 2 }, { "kind", "mesh_verify" },
                { "address", other.Address }, { "name", other.Name }, { "native_id", other.NativeId },
                { "pair_nonce", pair.Nonce }, { "expires_unix", expires } });
        }
        internal void Confirm(Connection connection, string nonce)
        {
            if (!Live(connection)) return;
            PendingPair pair;
            if (proofs.Confirm(connection, nonce, DateTime.UtcNow, out pair) != PairConfirmation.Complete) return;
            bool bound = owner.TabletPeer((Connection)pair.Left.Connection) == pair.Left && owner.TabletPeer((Connection)pair.Right.Connection) == pair.Right;
            FinishProof(pair, bound, bound ? "Player identity verified." : "A player's identity changed during verification.");
        }
        private void FinishProof(PendingPair pair, bool success, string message)
        {
            var verification = (Verification)pair.Tag;
            Requests history; if (requests.TryGetValue(verification.Issuer, out history)) history.Pending.Remove(verification.Request);
            Reply(verification.Issuer, verification.Request, success, message, success ? new JObject {
                { "id", pair.Right.NativeId }, { "name", pair.Right.Name }, { "address", pair.Right.Address },
                { "verified", true }, { "pair_nonce", pair.Nonce } } : null);
        }
        private void Reply(Connection connection, string request, bool okay, string message, JObject data)
        {
            if (!Live(connection)) return;
            JObject packet = new JObject { { "v", 2 }, { "kind", "tablet_reply" }, { "request_id", request },
                { "ok", okay }, { "message", message }, { "data", data ?? new JObject() } };
            Requests history;
            if (requests.TryGetValue(connection, out history) && !history.Replies.ContainsKey(request))
            {
                int length = packet.ToString(Formatting.None).Length;
                while (history.Order.Count > 0 && (history.Order.Count >= 16 || history.ReplyChars + length > 100000))
                {
                    string expired = history.Order.Dequeue();
                    history.ReplyChars -= history.Replies[expired].ToString(Formatting.None).Length;
                    history.Replies.Remove(expired);
                }
                history.Order.Enqueue(request); history.Replies.Add(request, packet);
                history.ReplyChars += length;
            }
            Companion.Send(connection, packet);
        }
        internal bool AllowJoin(Connection connection, Alta.Serialization.Stream stream)
        {
            try
            {
                object join = Activator.CreateInstance(JoinType, true);
                using (var copy = (Alta.Serialization.Stream)stream.Clone()) ReadJoin.Invoke(join, new object[] { connection, copy });
                string[] parts = ((string)JoinCredentials.GetValue(join, null) ?? "").Split('.');
                if (parts.Length != 3 || parts[1].Length > 16384) return Reject(connection, "Invalid server credentials.");
                string encoded = parts[1].Replace('-', '+').Replace('_', '/');
                encoded += new string('=', (4 - encoded.Length % 4) % 4);
                JObject claims = JObject.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(encoded)));
                int id = (int?)claims["UserId"] ?? 0; string name = (string)claims["Username"], token = (string)claims["TavernToken"];
                TabletUser record; var users = TabletAuthority.Read();
                if (!users.TryGetValue(id, out record) || !TabletPolicy.AllowCredentials((int)JoinPlayerId.GetValue(join, null), id, record.Name, name, record.Token, token, false))
                    return Reject(connection, "Your server credentials could not be verified.");
                if (store.IsBanned(id)) return Reject(connection, "You are banned from this server.");
                return true;
            }
            catch (Exception ex)
            {
                owner.LoggerInstance.Warning("Tablet join check failed: " + ex.GetType().Name);
                return Reject(connection, "The server could not verify your account. Try again.");
            }
        }
        internal static bool Reject(Connection connection, string message)
        {
            try
            {
                Task task = (Task)Deny.Invoke(null, new object[] { connection, message });
                task.ContinueWith(delegate(Task failed) { var ignored = failed.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
            }
            catch { connection.Disconnect(message); }
            return false;
        }
    }
    [HarmonyPatch(typeof(ServerPlayerConnectionHandlerOld), "CheckApproved")]
    internal static class TabletAdmission
    {
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(Connection connection, Alta.Serialization.Stream stream)
        {
            Companion current = Companion.Current;
            return current == null || current.TabletAllowsJoin(connection, stream);
        }
    }
}
