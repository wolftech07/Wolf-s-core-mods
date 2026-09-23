using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Alta.Api.DataTransferModels.Models.Responses;
using Alta.Networking;
using Alta.Networking.Scripts.Player;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TavernLib.Utils;

namespace TavernNativeMenu
{
    // Message 34 only exchanges consent/contact hints inside an approved game
    // connection. Global friendship is accepted by authenticated peer transport.
    internal static partial class MeshSocialTransport
    {
        private sealed class Pair
        {
            internal string Nonce, Address, Key, Name;
            internal int NativeId;
            internal long Expires;
            internal bool Acknowledged;
        }
        private sealed class Binding
        {
            internal Connection Connection;
            internal SerializeConnectionMethod Handler;
            internal readonly Dictionary<string, Pair> Pairs = new Dictionary<string, Pair>(StringComparer.Ordinal);
            internal readonly Dictionary<string, Pair> Proofs = new Dictionary<string, Pair>(StringComparer.Ordinal);
            internal readonly Dictionary<string, TabletCall> Calls = new Dictionary<string, TabletCall>(StringComparer.Ordinal);
            internal readonly Dictionary<int, string> Verified = new Dictionary<int, string>();
            internal bool Tablet;
            internal string SentIdentity;
            internal bool MeshServer, Bound, OldServerWarned;
            internal DateTime LastIdentity = DateTime.MinValue, Window = DateTime.MinValue;
            internal int Received;
        }
        private const MessageType SocialMessage = (MessageType)34;
        private static readonly Dictionary<Connection, Binding> Bindings = new Dictionary<Connection, Binding>();
        private static readonly HashSet<ISocket> Sockets = new HashSet<ISocket>();
        private static readonly FieldInfo Handlers = typeof(Connection).GetField("messageHandlers", BindingFlags.Instance | BindingFlags.NonPublic);
        private static bool initialized;
        private static long connectionGeneration;
        internal static event Action<int, string, string> FriendLinked;
        internal static void Initialize()
        {
            if (initialized) return;
            initialized = true;
            MeshSocialClient.PairVerified += PairVerified;
            MeshSocialClient.PeerVerified += PeerVerified;
            TavernEvents.SocketCreated.Subscribe(OnSocketCreated);
            if (Socket.Current != null) OnSocketCreated(Socket.Current);
        }
        private static void OnSocketCreated(ISocket socket)
        {
            if (socket == null || socket.IsServer || Sockets.Count >= 8 || !Sockets.Add(socket)) return;
            socket.ConnectionCreated += OnConnectionCreated;
            socket.ConnectionDestroyed += OnConnectionDestroyed;
            socket.SocketDestroyed += OnSocketDestroyed;
            foreach (Connection connection in socket.Connections) OnConnectionCreated(connection);
        }
        private static void OnSocketDestroyed(ISocket socket)
        {
            socket.ConnectionCreated -= OnConnectionCreated;
            socket.ConnectionDestroyed -= OnConnectionDestroyed;
            socket.SocketDestroyed -= OnSocketDestroyed;
            Sockets.Remove(socket);
            foreach (Connection connection in Bindings.Keys.Where(x => x.Socket == socket).ToArray()) OnConnectionDestroyed(connection);
        }
        private static void OnConnectionCreated(Connection connection)
        {
            if (connection == null || connection.IsDisposed || connection.Socket.IsServer || Bindings.ContainsKey(connection) || Bindings.Count >= 32) return;
            var handlers = Handlers == null ? null : Handlers.GetValue(connection) as IDictionary<MessageType, SerializeConnectionMethod>;
            if (handlers == null || handlers.ContainsKey(SocialMessage))
            { Error("Friend cards are unavailable because another mod owns native message 34. Update the client and server card mods together."); return; }
            var binding = new Binding { Connection = connection };
            binding.Handler = delegate(Connection sender, Alta.Serialization.Stream stream) { Receive(binding, sender, stream); };
            Bindings.Add(connection, binding);
            System.Threading.Interlocked.Increment(ref connectionGeneration);
            connection.SetHandler(SocialMessage, binding.Handler);
            connection.Disconnected += OnConnectionDestroyed;
        }
        private static void OnConnectionDestroyed(Connection connection)
        {
            Binding binding;
            if (!Bindings.TryGetValue(connection, out binding)) return;
            Bindings.Remove(connection);
            System.Threading.Interlocked.Increment(ref connectionGeneration);
            connection.Disconnected -= OnConnectionDestroyed;
            var handlers = Handlers == null ? null : Handlers.GetValue(connection) as IDictionary<MessageType, SerializeConnectionMethod>;
            SerializeConnectionMethod active;
            if (handlers != null && handlers.TryGetValue(SocialMessage, out active) && active == binding.Handler) connection.ClearHandler(SocialMessage);
            binding.Pairs.Clear();
            EndTabletBinding(binding);
        }
        private static bool Current(Binding binding)
        {
            Binding current;
            return !binding.Connection.IsDisposed && binding.Connection.IsApproved && Bindings.TryGetValue(binding.Connection, out current) && ReferenceEquals(current, binding);
        }
        internal static void Tick()
        {
            foreach (Binding binding in Bindings.Values.ToArray())
            {
                if (binding.Connection.IsDisposed) { OnConnectionDestroyed(binding.Connection); continue; }
                if (!Current(binding)) continue;
                TickTablet(binding);
                if (binding.MeshServer && !binding.Bound && DateTime.UtcNow - binding.LastIdentity >= TimeSpan.FromSeconds(10)) SendIdentity(binding);
                foreach (Pair pair in binding.Pairs.Values.Where(x => x.Expires <= MeshSocialClient.Now).ToArray())
                {
                    binding.Pairs.Remove(pair.Nonce); UndoOptimisticFriend(pair.NativeId);
                    Error("Friend-card confirmation expired. Exchange cards again when both peer clients are connected.");
                }
            }
        }
        private static void Receive(Binding binding, Connection sender, Alta.Serialization.Stream stream)
        {
            if (!stream.IsReading || !ReferenceEquals(binding.Connection, sender)) return;
            try
            {
                string text = ""; stream.SerializeString(ref text, (Alta.Serialization.Stream.StringEncoding)0);
                if (text == null || text.Length > 65536) return;
                JObject packet;
                using (var reader = new JsonTextReader(new StringReader(text)) { MaxDepth = 8 })
                { packet = JObject.Load(reader); if (reader.Read()) return; }
                MeshSocialClient.Post(delegate { Handle(binding, packet); });
            }
            catch (JsonException) { }
            catch (InvalidCastException) { }
            catch (FormatException) { }
            catch (ArgumentException) { }
        }
        private static void Handle(Binding binding, JObject packet)
        {
            if (!Current(binding)) return;
            DateTime now = DateTime.UtcNow;
            if ((now - binding.Window).TotalSeconds >= 10) { binding.Window = now; binding.Received = 0; }
            if (++binding.Received > 40) return;
            int version = (int?)packet["v"] ?? 0;
            string kind = (string)packet["kind"];
            if (version == 1 && kind == "hello")
            {
                if (!binding.OldServerWarned)
                {
                    binding.OldServerWarned = true;
                    Error("This server still has the old relay-based friend-card companion. Its host needs to update server card support using the latest setup. Your peer friends and invitations still work.");
                }
                return;
            }
            if (version != 2) return;
            if (kind == "mesh_hello") { binding.MeshServer = true; binding.Tablet = (bool?)packet["tablet"] == true && (int?)packet["tablet_version"] == 1; SendIdentity(binding); PrimeTablet(binding); return; }
            if (!binding.MeshServer) return;
            if (kind == "mesh_bound") { if (!String.IsNullOrEmpty(binding.SentIdentity)) binding.Bound = true; return; }
            if (kind == "mesh_error")
            {
                int id = (int?)packet["native_id"] ?? 0;
                foreach (Pair pair in binding.Pairs.Values.Where(x => x.NativeId == id).ToArray()) binding.Pairs.Remove(pair.Nonce);
                UndoOptimisticFriend(id);
                string message = (string)packet["message"] ?? "The server could not complete that friend-card exchange.";
                message = new string(message.Where(c => !Char.IsControl(c)).Take(240).ToArray());
                Error(message); return;
            }
            if (HandleTablet(binding, kind, packet)) return;
            if (!binding.Bound) return;
            if (kind == "mesh_pair") BeginPair(binding, packet);
            else if (kind == "mesh_friend") CompletePair(binding, packet);
        }
        private static void SendIdentity(Binding binding)
        {
            if (!Current(binding) || !MeshSocialClient.Configured || binding.Bound || DateTime.UtcNow - binding.LastIdentity < TimeSpan.FromSeconds(1)) return;
            string address = MeshSocialClient.Address;
            if (String.IsNullOrEmpty(address)) return;
            binding.LastIdentity = DateTime.UtcNow;
            binding.SentIdentity = address;
            Send(binding, new JObject { { "v", 2 }, { "kind", "mesh_identity" }, { "address", address } });
        }
        private static async void BeginPair(Binding binding, JObject packet)
        {
            Pair pair = null;
            try
            {
                string address = MeshSocialClient.ValidateAddress((string)packet["address"]);
                string key = address.Substring(0, 64), nonce = (string)packet["pair_nonce"];
                long expires = (long?)packet["expires_unix"] ?? 0;
                int nativeId = (int?)packet["native_id"] ?? 0;
                if (NativeSocialTablet.IsPlayerBlocked(nativeId)) { UndoOptimisticFriend(nativeId); Error("Unblock this player in your social tablet before exchanging friend cards."); return; }
                if (key == MeshSocialClient.SocialId || nativeId <= 0 || Player.Current == null || nativeId == Player.Current.UserInfo.Identifier ||
                    !ValidNonce(nonce) || expires <= MeshSocialClient.Now || expires > MeshSocialClient.Now + 125 || binding.Pairs.ContainsKey(nonce) || binding.Pairs.Count >= 8) return;
                // Only one pending exchange with a native player/key per connection.
                if (binding.Pairs.Values.Any(x => x.NativeId == nativeId || x.Key == key)) return;
                pair = new Pair { Nonce = nonce, Address = address, Key = key, NativeId = nativeId, Expires = expires, Name = (string)packet["name"] ?? "Friend" };
                binding.Pairs.Add(nonce, pair);
                await MeshSocialClient.PairCardAsync(address, pair.Name, nonce, expires).ConfigureAwait(false);
            }
            catch (Exception error)
            {
                Pair failed = pair;
                MeshSocialClient.Post(delegate
                {
                    if (!Current(binding)) return;
                    if (failed != null) { binding.Pairs.Remove(failed.Nonce); UndoOptimisticFriend(failed.NativeId); }
                    MeshSocialClient.SetError(error);
                });
            }
        }
        private static bool ValidNonce(string nonce)
        { return nonce != null && nonce.Length == 43 && nonce.All(c => (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '-' || c == '_'); }
        private static void PairVerified(string key, string nonce)
        {
            foreach (Binding binding in Bindings.Values.ToArray())
            {
                Pair pair;
                if (!Current(binding) || !binding.Pairs.TryGetValue(nonce, out pair) || pair.Key != key || pair.Expires <= MeshSocialClient.Now || pair.Acknowledged) continue;
                Send(binding, new JObject { { "v", 2 }, { "kind", "mesh_pair_confirm" }, { "pair_nonce", nonce } });
                pair.Acknowledged = true;
            }
        }
        private static async void CompletePair(Binding binding, JObject packet)
        {
            try
            {
                string address = MeshSocialClient.ValidateAddress((string)packet["address"]);
                int nativeId = (int?)packet["native_id"] ?? 0;
                Pair pair = binding.Pairs.Values.FirstOrDefault(x => x.Address == address && x.NativeId == nativeId && x.Acknowledged && x.Expires > MeshSocialClient.Now);
                if (pair == null) return;
                JArray friends = await MeshSocialClient.GetFriendsAsync().ConfigureAwait(false);
                JObject match = friends.OfType<JObject>().FirstOrDefault(x => (string)x["social_id"] == pair.Key);
                MeshSocialClient.Post(delegate
                {
                    Pair current;
                    if (!Current(binding) || match == null || !binding.Pairs.TryGetValue(pair.Nonce, out current) || !ReferenceEquals(pair, current) || pair.Expires <= MeshSocialClient.Now) return;
                    binding.Pairs.Remove(pair.Nonce);
                    binding.Verified[nativeId] = address;
                    IPlayer local = Player.Current; Player other;
                    if (local == null || !Player.SafeGetPlayer(nativeId, out other)) return;
                    string name = (string)match["name"] ?? other.UserInfo.Username;
                    other.FriendshipManager.UpdateStatus(true);
                    if (!local.FriendshipManager.IsFriendsWith(nativeId))
                        local.FriendshipManager.AddFriend(new FriendshipInfo { Identifier = nativeId, Username = name, Type = FriendshipType.Accepted, CreatedAt = DateTime.UtcNow });
                    Action<int, string, string> handler = FriendLinked;
                    if (handler != null) handler(nativeId, pair.Key, name);
                    MeshSocialClient.RequestRefresh();
                });
            }
            catch (Exception error) { MeshSocialClient.SetError(error); }
        }
        private static void UndoOptimisticFriend(int nativeId)
        {
            if (nativeId <= 0) return;
            IPlayer local = Player.Current; Player other;
            if (local != null && Player.SafeGetPlayer(nativeId, out other) && !local.FriendshipManager.IsFriendsWith(nativeId)) other.FriendshipManager.UpdateStatus(false);
        }
        private static void Send(Binding binding, JObject packet)
        {
            if (!Current(binding)) return;
            string json = packet.ToString(Formatting.None);
            binding.Connection.Send(null, SocialMessage, delegate(Connection connection, Alta.Serialization.Stream stream) { stream.SerializeString(ref json, (Alta.Serialization.Stream.StringEncoding)0); });
        }
        private static void Error(string message) { MeshSocialClient.SetError(new InvalidOperationException(message)); }
    }
}
