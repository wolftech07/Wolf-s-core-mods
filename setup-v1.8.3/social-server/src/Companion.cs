using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Alta.Api.DataTransferModels.Models.Responses;
using Alta.Networking;
using Alta.Serialization;
using Alta.Timing;
using HarmonyLib;
using MelonLoader;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

[assembly: MelonInfo(typeof(TavernNativeSocial.Companion), "Tavern Native Social Server", "1.0.0", "Tavern Native Menu contributors")]
[assembly: MelonGame(null, "A Township Tale")]

namespace TavernNativeSocial
{
    internal sealed class CompanionConfig
    {
        public string relay_url;
        public string public_relay_url;
        public string server_id;
        public string server_token;
    }
    internal sealed class Binding
    {
        internal Connection Connection;
        internal string SocialId;
        internal string Name;
        internal int NativeId;
    }
    public sealed class Companion : MelonMod
    {
        // TavernLib currently reserves 32/33. We claim 34 only when unoccupied.
        public const int WireId = 34;
        internal static Companion Current;
        private readonly ConcurrentQueue<Action> queue = new ConcurrentQueue<Action>();
        private readonly Dictionary<Connection, Binding> bindings = new Dictionary<Connection, Binding>();
        private readonly HashSet<Connection> attached = new HashSet<Connection>();
        private readonly HashSet<Connection> resolving = new HashSet<Connection>();
        private readonly HashSet<FriendRequestToken> pendingCards = new HashSet<FriendRequestToken>();
        private readonly CardConsent cardConsent = new CardConsent();
        private readonly Dictionary<Connection, DateTime> lastHello = new Dictionary<Connection, DateTime>();
        private CompanionConfig config;
        private DateTime nextHeartbeat;
        private bool heartbeatBusy;

        public override void OnInitializeMelon()
        {
            if (!CommandLineArguments.Contains("/start_server")) return;
            string path = Path.Combine(Environment.CurrentDirectory, "UserData", "TavernNativeSocialServer.json");
            if (!File.Exists(path)) { LoggerInstance.Warning("Social server disabled: select the server folder in TavernNativeSocialHost to install its configuration."); return; }
            try
            {
                config = JsonConvert.DeserializeObject<CompanionConfig>(File.ReadAllText(path));
                if (config == null || String.IsNullOrWhiteSpace(config.server_id) || String.IsNullOrWhiteSpace(config.server_token)) throw new InvalidDataException("Missing server credential.");
                config.relay_url = SafeUrl(config.relay_url);
                config.public_relay_url = SafeUrl(String.IsNullOrWhiteSpace(config.public_relay_url) ? config.relay_url : config.public_relay_url);
                Current = this;
                HarmonyInstance.PatchAll(typeof(Companion).Assembly);
                if (Socket.Current != null && Socket.Current.IsServer)
                    foreach (Connection connection in Socket.Current.Connections.ToArray()) Attach(connection);
                LoggerInstance.Msg("Native friend-card relay bridge enabled. Friendships require both players to connect the same social relay.");
            }
            catch (Exception ex) { Current = null; config = null; LoggerInstance.Error("Social server configuration could not be loaded: " + ex.Message); }
        }
        public override void OnUpdate()
        {
            if (Current != this) return;
            Action action; int count = 0;
            while (count++ < 100 && queue.TryDequeue(out action)) { try { action(); } catch (Exception ex) { LoggerInstance.Warning("Social bridge action failed: " + ex.GetType().Name); } }
            DateTime now = DateTime.UtcNow;
            foreach (Connection connection in attached.ToArray())
            {
                if (connection.IsDisposed) { Disconnected(connection); continue; }
                if (connection.IsApproved && connection.Player != null && !bindings.ContainsKey(connection) && !resolving.Contains(connection))
                {
                    DateTime last; if (!lastHello.TryGetValue(connection, out last) || (now - last).TotalSeconds >= 15) Hello(connection);
                }
            }
            if (!heartbeatBusy && now >= nextHeartbeat && bindings.Count > 0)
            {
                nextHeartbeat = now.AddSeconds(45); heartbeatBusy = true;
                string[] ids = bindings.Values.Select(x => x.SocialId).Distinct().ToArray();
                Task.Run(delegate
                {
                    try
                    {
                        JObject heartbeat = Request("/v1/server/session-heartbeat", new JObject { { "ids", new JArray(ids) } });
                        string[] expired = (heartbeat["expired_ids"] as JArray ?? new JArray()).Values<string>().ToArray();
                        queue.Enqueue(delegate
                        {
                            foreach (Connection connection in bindings.Where(x => expired.Contains(x.Value.SocialId)).Select(x => x.Key).ToArray())
                            { bindings.Remove(connection); lastHello.Remove(connection); Hello(connection); }
                        });
                    }
                    catch { queue.Enqueue(delegate { LoggerInstance.Warning("Social relay heartbeat failed. Card exchange will retry when the relay is reachable."); }); }
                    finally { queue.Enqueue(delegate { heartbeatBusy = false; }); }
                });
            }
        }
        internal void Attach(Connection connection)
        {
            if (config == null || connection == null || !connection.Socket.IsServer || attached.Contains(connection)) return;
            IDictionary handlers = AccessTools.Field(typeof(Connection), "messageHandlers").GetValue(connection) as IDictionary;
            if (handlers == null || handlers.Contains((MessageType)WireId))
            {
                LoggerInstance.Warning("Native message 34 is already in use; social support was not attached to this connection."); return;
            }
            attached.Add(connection);
            connection.SetHandler((MessageType)WireId, Receive);
            connection.Disconnected += Disconnected;
        }
        private void Disconnected(Connection connection)
        {
            attached.Remove(connection); resolving.Remove(connection); lastHello.Remove(connection);
            Binding binding;
            if (bindings.TryGetValue(connection, out binding))
            {
                bindings.Remove(connection);
                if (!bindings.Values.Any(x => x.SocialId == binding.SocialId))
                    Task.Run(delegate { try { Request("/v1/server/session-end", new JObject { { "social_id", binding.SocialId } }); } catch { } });
            }
        }
        private void Hello(Connection connection)
        {
            lastHello[connection] = DateTime.UtcNow;
            Send(connection, new JObject { { "v", 1 }, { "kind", "hello" }, { "server_id", config.server_id }, { "relay_url", config.public_relay_url } });
        }
        private void Receive(Connection connection, Alta.Serialization.Stream stream)
        {
            string text = ""; stream.SerializeString(ref text, (Alta.Serialization.Stream.StringEncoding)0);
            if (!stream.IsReading || text == null || text.Length > 1024) return;
            JObject packet;
            try { packet = JObject.Parse(text); } catch { return; }
            if ((int?)packet["v"] != 1 || (string)packet["kind"] != "ticket") return;
            string ticket = (string)packet["ticket"];
            if (String.IsNullOrEmpty(ticket) || ticket.Length > 128) return;
            queue.Enqueue(delegate { Bind(connection, ticket); });
        }
        private void Bind(Connection connection, string ticket)
        {
            if (!attached.Contains(connection) || !connection.IsApproved || connection.Player == null || resolving.Contains(connection) || bindings.ContainsKey(connection)) return;
            resolving.Add(connection);
            int nativeId = connection.Player.UserInfo.Identifier;
            Task.Run(delegate
            {
                JObject response = null;
                try { response = Request("/v1/server/resolve-ticket", new JObject { { "ticket", ticket } }); }
                catch { }
                queue.Enqueue(delegate
                {
                    resolving.Remove(connection);
                    if (!attached.Contains(connection) || !connection.IsApproved || connection.Player == null || connection.Player.UserInfo.Identifier != nativeId) return;
                    if (response == null || String.IsNullOrEmpty((string)response["social_id"]))
                    { Error(connection, "Social sign-in failed. Check the relay connection and try again."); return; }
                    string id = (string)response["social_id"];
                    if (bindings.Values.Any(x => x.SocialId == id && x.NativeId != nativeId))
                    { Error(connection, "That social profile is already bound to another player on this server."); return; }
                    bindings[connection] = new Binding { Connection = connection, NativeId = nativeId, SocialId = id, Name = (string)response["name"] ?? "Friend" };
                    Send(connection, new JObject { { "v", 1 }, { "kind", "bound" }, { "social_id", id } });
                    RestoreFriendLinks();
                });
            });
        }
        internal bool Card(FriendRequestToken card)
        {
            if (config == null || !NetworkSceneManager.IsServer) return true;
            if (!pendingCards.Add(card)) return false;
            int rightId = (int)AccessTools.Field(typeof(FriendRequestToken), "otherTokenId").GetValue(card);
            Binding left = bindings.Values.FirstOrDefault(x => x.NativeId == card.Owner);
            Binding right = bindings.Values.FirstOrDefault(x => x.NativeId == rightId);
            bool consent = cardConsent.Consume(card.GetInstanceID(), card.Owner, rightId, DateTime.UtcNow);
            // AddFriend runs only after the game's reciprocal physical handoff.
            // Do not infer friendship from the optimistic client-side grab effect.
            Timeline.IEntry entry = (Timeline.IEntry)AccessTools.Field(typeof(FriendRequestToken), "timelineEntry").GetValue(card);
            Timeline.Cancel(ref entry);
            AccessTools.Field(typeof(FriendRequestToken), "timelineEntry").SetValue(card, entry);
            AccessTools.Field(typeof(FriendRequestToken), "receivedOwnerToken").SetValue(card, false);
            AccessTools.Field(typeof(FriendRequestToken), "otherTokenId").SetValue(card, -1);
            if (!consent || left == null || right == null || left.SocialId == right.SocialId)
            {
                Player ownerPlayer = Player.GetPlayer(card.Owner), otherPlayer = Player.GetPlayer(rightId);
                Error(ownerPlayer == null ? null : ownerPlayer.ConnectionToRemotePlayer, "Both players must connect the same social relay before exchanging friend cards.", rightId);
                Error(otherPlayer == null ? null : otherPlayer.ConnectionToRemotePlayer, "Both players must connect the same social relay before exchanging friend cards.", card.Owner);
                FinishCard(card, 2); return false;
            }
            Task.Run(delegate
            {
                JObject result = null;
                try { result = Request("/v1/server/friendship", new JObject { { "left_id", left.SocialId }, { "right_id", right.SocialId } }); }
                catch { }
                queue.Enqueue(delegate
                {
                    if (result == null)
                    {
                        Error(left.Connection, "Friendship could not be saved. Check the social relay and exchange cards again.", right.NativeId);
                        Error(right.Connection, "Friendship could not be saved. Check the social relay and exchange cards again.", left.NativeId);
                        FinishCard(card, 2); return;
                    }
                    AddNativeFriend(left, right); AddNativeFriend(right, left);
                    SendFriend(left, right); SendFriend(right, left);
                    FinishCard(card, (bool?)result["existing"] == true ? 1 : 0);
                });
            });
            return false;
        }
        internal void RecordOwner(FriendRequestToken card, int friend)
        { if (NetworkSceneManager.IsServer) cardConsent.Owner(card.GetInstanceID(), card.Owner, friend, DateTime.UtcNow); }
        internal void RecordPeer(FriendRequestToken card, Alta.Networking.Scripts.Player.IPlayer peer)
        { if (NetworkSceneManager.IsServer && peer != null) cardConsent.Peer(card.GetInstanceID(), card.Owner, peer.UserInfo.Identifier, DateTime.UtcNow); }
        private static void AddNativeFriend(Binding owner, Binding friend)
        {
            if (owner.Connection.IsDisposed || owner.Connection.Player == null) return;
            FriendshipManager manager = owner.Connection.Player.FriendshipManager;
            if (!manager.IsFriendsWith(friend.NativeId)) manager.AddFriend(new FriendshipInfo { Identifier = friend.NativeId, Username = friend.Name, Type = FriendshipType.Accepted, CreatedAt = DateTime.UtcNow });
        }
        private static void SendFriend(Binding owner, Binding friend)
        {
            Send(owner.Connection, new JObject { { "v", 1 }, { "kind", "friend" }, { "social_id", friend.SocialId }, { "name", friend.Name }, { "native_id", friend.NativeId } });
        }
        private void RestoreFriendLinks()
        {
            Task.Run(delegate
            {
                JObject result;
                try { result = Request("/v1/server/friendships", new JObject()); } catch { return; }
                queue.Enqueue(delegate
                {
                    foreach (JObject pair in (result["pairs"] as JArray ?? new JArray()).OfType<JObject>())
                    {
                        Binding left = bindings.Values.FirstOrDefault(x => x.SocialId == (string)pair["left_id"]);
                        Binding right = bindings.Values.FirstOrDefault(x => x.SocialId == (string)pair["right_id"]);
                        if (left == null || right == null) continue;
                        AddNativeFriend(left, right); AddNativeFriend(right, left); SendFriend(left, right); SendFriend(right, left);
                    }
                });
            });
        }
        private void FinishCard(FriendRequestToken card, int result)
        {
            pendingCards.Remove(card); if (card == null || card.Entity.IsBeingDestroyed) return;
            try
            {
                MethodInfo effects = AccessTools.Method(typeof(FriendRequestToken), "RunEffects");
                object enumValue = Enum.ToObject(effects.GetParameters()[0].ParameterType, result);
                effects.Invoke(card, new[] { enumValue });
                var sync = (MethodSyncStruct<int>)AccessTools.Field(typeof(FriendRequestToken), "syncFriendshipRequestEffect").GetValue(card);
                sync.SendToChunks(result); card.Fade(false);
            }
            catch (Exception ex) { LoggerInstance.Warning("Card completion failed: " + ex.GetType().Name); }
        }
        private static void Error(Connection connection, string message, int nativeId = 0)
        { Send(connection, new JObject { { "v", 1 }, { "kind", "error" }, { "message", message }, { "native_id", nativeId } }); }
        private static void Send(Connection connection, JObject packet)
        {
            if (connection == null || connection.IsDisposed || !connection.IsApproved) return;
            string text = packet.ToString(Formatting.None);
            connection.Send(null, (MessageType)WireId, delegate(Connection target, Alta.Serialization.Stream stream) { stream.SerializeString(ref text, (Alta.Serialization.Stream.StringEncoding)0); });
        }
        private JObject Request(string route, JObject body)
        {
            var request = (HttpWebRequest)WebRequest.Create(config.relay_url + route);
            request.Method = "POST"; request.ContentType = "application/json"; request.Timeout = 8000; request.ReadWriteTimeout = 8000;
            request.AllowAutoRedirect = false; request.Proxy = null;
            request.Headers[HttpRequestHeader.Authorization] = "Bearer " + config.server_token;
            byte[] bytes = Encoding.UTF8.GetBytes(body.ToString(Formatting.None)); request.ContentLength = bytes.Length;
            using (var stream = request.GetRequestStream()) stream.Write(bytes, 0, bytes.Length);
            using (var response = (HttpWebResponse)request.GetResponse())
            using (var stream = response.GetResponseStream())
            using (var output = new MemoryStream())
            {
                byte[] buffer = new byte[4096]; int read;
                while ((read = stream.Read(buffer, 0, buffer.Length)) > 0) { if (output.Length + read > 65536) throw new InvalidDataException("Oversized relay response."); output.Write(buffer, 0, read); }
                return JObject.Parse(Encoding.UTF8.GetString(output.ToArray()));
            }
        }
        public static string SafeUrl(string value)
        {
            Uri uri;
            if (!Uri.TryCreate((value ?? "").Trim().TrimEnd('/'), UriKind.Absolute, out uri) || !String.IsNullOrEmpty(uri.UserInfo) || !String.IsNullOrEmpty(uri.Query) || !String.IsNullOrEmpty(uri.Fragment) || uri.AbsolutePath != "/")
                throw new InvalidDataException("Relay URL must be an HTTPS origin, or HTTP on the local computer.");
            IPAddress address;
            bool local = IPAddress.TryParse(uri.DnsSafeHost.Trim('[', ']'), out address) && IPAddress.IsLoopback(address);
            if (uri.Scheme != "https" && !(uri.Scheme == "http" && local)) throw new InvalidDataException("Public social relays require HTTPS.");
            return uri.GetLeftPart(UriPartial.Authority).TrimEnd('/');
        }
    }
    [HarmonyPatch(typeof(Connection), MethodType.Constructor)]
    internal static class AttachSocialConnection
    {
        private static void Postfix(Connection __instance) { if (Companion.Current != null) Companion.Current.Attach(__instance); }
    }
    [HarmonyPatch(typeof(FriendRequestToken), "AddFriend")]
    internal static class NativeFriendCard
    {
        private static bool Prefix(FriendRequestToken __instance) { return Companion.Current == null || Companion.Current.Card(__instance); }
    }
    [HarmonyPatch(typeof(FriendRequestToken), "SyncFriendRequestForPlayer")]
    internal static class CardOwnerConsent
    {
        private static void Prefix(FriendRequestToken __instance, int friendID)
        { if (Companion.Current != null) Companion.Current.RecordOwner(__instance, friendID); }
    }
    [HarmonyPatch(typeof(FriendRequestToken), "SyncFriendRequestForTokenOwner")]
    internal static class CardPeerConsent
    {
        private static void Prefix(FriendRequestToken __instance, Alta.Networking.Scripts.Player.IPlayer player)
        { if (Companion.Current != null) Companion.Current.RecordPeer(__instance, player); }
    }
}
