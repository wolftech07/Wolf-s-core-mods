using System;
using System.Collections.Generic;
using System.Reflection;
using System.Linq;
using Alta.Networking;
using Alta.Networking.Scripts.Player;
using Alta.Serialization;
using Alta.Api.DataTransferModels.Models.Responses;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TavernLib.Utils;

namespace TavernNativeMenu
{
    // ID 34 is available after TavernLib's message-width patch. Leave another
    // mod's handler alone. All sends happen from the game's main-thread Tick.
    internal static class TavernSocialTransport
    {
        private sealed class Binding
        {
            internal Connection Connection;
            internal SerializeConnectionMethod Handler;
            internal string ServerId, Relay;
            internal bool Pending;
            internal DateTime LastHello = DateTime.MinValue;
        }
        private const MessageType SocialMessage = (MessageType)34;
        private static readonly Dictionary<Connection, Binding> Bindings = new Dictionary<Connection, Binding>();
        private static readonly HashSet<ISocket> Sockets = new HashSet<ISocket>();
        private static readonly FieldInfo Handlers = typeof(Connection).GetField("messageHandlers", BindingFlags.Instance | BindingFlags.NonPublic);
        private static bool initialized;
        internal static event Action<int, string, string> FriendLinked;
        internal static void Initialize()
        {
            if (initialized) return;
            initialized = true;
            TavernEvents.SocketCreated.Subscribe(OnSocketCreated);
            if (Socket.Current != null) OnSocketCreated(Socket.Current);
        }
        private static void OnSocketCreated(ISocket socket)
        {
            if (socket == null || socket.IsServer || !Sockets.Add(socket)) return;
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
            var remove = new List<Connection>();
            foreach (var pair in Bindings) if (pair.Key.Socket == socket) remove.Add(pair.Key);
            foreach (Connection connection in remove) OnConnectionDestroyed(connection);
        }
        private static void OnConnectionCreated(Connection connection)
        {
            if (connection == null || connection.IsDisposed || connection.Socket.IsServer || Bindings.ContainsKey(connection)) return;
            var handlers = Handlers == null ? null : Handlers.GetValue(connection) as IDictionary<MessageType, SerializeConnectionMethod>;
            if (handlers == null || handlers.ContainsKey(SocialMessage))
            {
                TavernSocialClient.SetError(new InvalidOperationException("The friends card channel is unavailable because another mod owns message 34."));
                return;
            }
            var binding = new Binding { Connection = connection };
            binding.Handler = delegate(Connection sender, Alta.Serialization.Stream stream) { Receive(binding, sender, stream); };
            Bindings.Add(connection, binding);
            connection.SetHandler(SocialMessage, binding.Handler);
            connection.Disconnected += OnConnectionDestroyed;
        }
        private static void OnConnectionDestroyed(Connection connection)
        {
            Binding binding;
            if (!Bindings.TryGetValue(connection, out binding)) return;
            Bindings.Remove(connection);
            connection.Disconnected -= OnConnectionDestroyed;
            var handlers = Handlers == null ? null : Handlers.GetValue(connection) as IDictionary<MessageType, SerializeConnectionMethod>;
            SerializeConnectionMethod active;
            if (handlers != null && handlers.TryGetValue(SocialMessage, out active) && active == binding.Handler) connection.ClearHandler(SocialMessage);
        }
        private static void Receive(Binding binding, Connection sender, Alta.Serialization.Stream stream)
        {
            try
            {
                string json = "";
                stream.SerializeString(ref json, (Alta.Serialization.Stream.StringEncoding)0);
                if (json == null || json.Length > 8192) return;
                JObject message;
                using (var reader = new JsonTextReader(new System.IO.StringReader(json)) { MaxDepth = 8 }) message = JObject.Load(reader);
                if ((int?)message["v"] != 1) return;
                TavernSocialClient.Post(delegate { HandleMessage(binding, sender, message); });
            }
            catch { TavernSocialClient.SetError(new InvalidOperationException("The server sent an invalid friends message.")); }
        }
        private static bool Current(Binding binding)
        {
            Binding current;
            return !binding.Connection.IsDisposed && Bindings.TryGetValue(binding.Connection, out current) && ReferenceEquals(current, binding);
        }
        private static void HandleMessage(Binding binding, Connection sender, JObject message)
        {
            if (!Current(binding) || !ReferenceEquals(sender, binding.Connection)) return;
            string kind = (string)message["kind"];
            if (kind == "hello")
            {
                if (binding.Pending || DateTime.UtcNow - binding.LastHello < TimeSpan.FromSeconds(10)) return;
                binding.LastHello = DateTime.UtcNow;
                string relay;
                try { relay = TavernSocialClient.NormalizeRelayUrl((string)message["relay_url"]); }
                catch { TavernSocialClient.SetError(new InvalidOperationException("The server advertised an invalid friends relay.")); return; }
                if (!TavernSocialClient.Configured || !String.Equals(relay, TavernSocialClient.RelayUrl, StringComparison.Ordinal))
                {
                    TavernSocialClient.SetError(new InvalidOperationException("This server's friends cards require its shared friends relay. Configure the matching relay in the friends menu."));
                    return;
                }
                binding.ServerId = (string)message["server_id"];
                binding.Relay = relay;
                AnswerHello(binding);
            }
            else if (kind == "friend")
            {
                if (binding.Relay != TavernSocialClient.RelayUrl) return;
                TavernSocialClient.RequestRefresh();
                RefreshNativeFriend(binding, (int?)message["native_id"] ?? 0, (string)message["social_id"], false);
            }
            else if (kind == "bound") TavernSocialClient.RequestRefresh();
            else if (kind == "error")
            {
                TavernSocialClient.SetError(new InvalidOperationException("The server could not complete the friends-card request. Check that both players use its shared friends relay."));
                if (binding.Relay == TavernSocialClient.RelayUrl)
                    RefreshNativeFriend(binding, (int?)message["native_id"] ?? 0, (string)message["social_id"], true);
            }
        }
        private static async void RefreshNativeFriend(Binding binding, int nativeId, string socialId, bool denied)
        {
            if (nativeId <= 0) return;
            string relay = binding.Relay;
            try
            {
                JArray friends = await TavernSocialClient.GetFriendsAsync().ConfigureAwait(false);
                JObject match = friends.OfType<JObject>().FirstOrDefault(x => (string)x["social_id"] == socialId);
                TavernSocialClient.Post(delegate
                {
                    if (!Current(binding) || TavernSocialClient.RelayUrl != relay) return;
                    IPlayer local = Player.Current;
                    Player other;
                    if (local == null || !Player.SafeGetPlayer(nativeId, out other)) return;
                    if (match != null)
                    {
                        string name = (string)match["name"] ?? other.UserInfo.Username;
                        other.FriendshipManager.UpdateStatus(true);
                        if (!local.FriendshipManager.IsFriendsWith(nativeId))
                            local.FriendshipManager.AddFriend(new FriendshipInfo { Identifier = nativeId, Username = name, Type = FriendshipType.Accepted, CreatedAt = DateTime.UtcNow });
                        Action<int, string, string> changed = FriendLinked;
                        if (changed != null) changed(nativeId, socialId, name);
                    }
                    else if (denied && !local.FriendshipManager.IsFriendsWith(nativeId))
                        other.FriendshipManager.UpdateStatus(false); // Undo only the card's optimistic status.
                });
            }
            catch (Exception error) { TavernSocialClient.SetError(error); }
        }
        private static async void AnswerHello(Binding binding)
        {
            binding.Pending = true;
            string relay = binding.Relay;
            string serverId = binding.ServerId;
            try
            {
                string ticket = await TavernSocialClient.GetSessionTicketAsync(serverId, relay).ConfigureAwait(false);
                TavernSocialClient.Post(delegate
                {
                    if (!Current(binding) || binding.Relay != relay || binding.ServerId != serverId || TavernSocialClient.RelayUrl != relay) return;
                    string json = new JObject { { "v", 1 }, { "kind", "ticket" }, { "ticket", ticket } }.ToString(Formatting.None);
                    binding.Connection.Send(null, SocialMessage, delegate(Connection connection, Alta.Serialization.Stream stream) { stream.SerializeString(ref json, (Alta.Serialization.Stream.StringEncoding)0); });
                });
            }
            catch (Exception error) { TavernSocialClient.SetError(error); }
            finally { TavernSocialClient.Post(delegate { binding.Pending = false; }); }
        }
    }
}
