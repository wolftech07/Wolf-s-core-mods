using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using Alta.Api.DataTransferModels.Models.Responses;
using Alta.Networking;
using Alta.Serialization;
using Alta.Timing;
using HarmonyLib;
using MelonLoader;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

[assembly: MelonInfo(typeof(TavernNativeMeshServer.Companion), "Tavern In-Game Hub Server", "2.2.0", "Tavern In-Game Hub contributors")]
[assembly: MelonGame(null, "A Township Tale")]

namespace TavernNativeMeshServer
{
    // No listener, relay process, web service, or shared secret.
    // The game authenticates the physical handoff; the peer transport must prove
    // ownership of the supplied address before either client confirms the pair.
    public sealed class Companion : MelonMod
    {
        public const int WireId = 34;
        internal static Companion Current;
        private sealed class Session
        {
            internal Connection Connection;
            internal SerializeConnectionMethod Handler;
            internal PairPeer Peer;
            internal DateTime LastHello = DateTime.MinValue, Window = DateTime.MinValue;
            internal int Received;
        }
        private readonly ConcurrentQueue<Action> queue = new ConcurrentQueue<Action>();
        private int queued;
        private readonly Dictionary<Connection, Session> sessions = new Dictionary<Connection, Session>();
        private readonly PairRegistry pairs = new PairRegistry();
        private readonly CardConsent consent = new CardConsent();
        private readonly HashSet<FriendRequestToken> pendingCards = new HashSet<FriendRequestToken>();
        private TabletService tablet;
        private static readonly FieldInfo Handlers = AccessTools.Field(typeof(Connection), "messageHandlers");

        public override void OnInitializeMelon()
        {
            if (!CommandLineArguments.Contains("/start_server")) return;
            Current = this;
            try { tablet = new TabletService(this); }
            catch (Exception ex) { LoggerInstance.Error("Tablet server state could not be loaded: " + ex.GetType().Name); }
            HarmonyInstance.PatchAll(typeof(Companion).Assembly);
            if (Socket.Current != null && Socket.Current.IsServer)
                foreach (Connection connection in Socket.Current.Connections.ToArray()) Attach(connection);
            LoggerInstance.Msg("Mesh friend-card bridge enabled. Friendship is confirmed by both clients over their peer connection.");
        }
        public override void OnUpdate()
        {
            if (Current != this) return;
            Action action; int count = 0;
            while (count++ < 64 && queue.TryDequeue(out action))
            {
                Interlocked.Decrement(ref queued);
                try { action(); } catch (Exception ex) { LoggerInstance.Warning("Mesh card action failed: " + ex.GetType().Name); }
            }
            DateTime now = DateTime.UtcNow;
            foreach (Session session in sessions.Values.ToArray())
            {
                Connection connection = session.Connection;
                if (connection.IsDisposed || (session.Peer != null && !StillBound(session.Peer))) { Detach(connection); continue; }
                if (session.Peer == null && connection.IsApproved && connection.Player != null && (now - session.LastHello).TotalSeconds >= 15)
                {
                    session.LastHello = now;
                    Send(connection, new JObject { { "v", 2 }, { "kind", "mesh_hello" }, { "tablet", tablet != null }, { "tablet_version", 1 } });
                }
            }
            foreach (PendingPair pair in pairs.Expire(now)) DenyPair(pair, "Friend-card confirmation expired. Exchange cards again.");
            consent.Expire(now);
            if (tablet != null) tablet.Expire(now);
        }
        public override void OnDeinitializeMelon()
        {
            if (Current != this) return;
            Current = null;
            foreach (Connection connection in sessions.Keys.ToArray()) Detach(connection);
            Action ignored; while (queue.TryDequeue(out ignored)) { }
            Interlocked.Exchange(ref queued, 0);
        }
        internal void Attach(Connection connection)
        {
            if (Current != this || connection == null || connection.IsDisposed || !connection.Socket.IsServer || sessions.ContainsKey(connection) || sessions.Count >= 512) return;
            IDictionary handlers = Handlers == null ? null : Handlers.GetValue(connection) as IDictionary;
            if (handlers == null || handlers.Contains((MessageType)WireId))
            { LoggerInstance.Warning("Native message 34 is already in use; the mesh card bridge left that handler unchanged."); return; }
            var session = new Session { Connection = connection };
            session.Handler = delegate(Connection sender, Alta.Serialization.Stream stream) { Receive(session, sender, stream); };
            sessions.Add(connection, session);
            connection.SetHandler((MessageType)WireId, session.Handler);
            connection.Disconnected += Detach;
        }
        private void Detach(Connection connection)
        {
            Session session;
            if (!sessions.TryGetValue(connection, out session)) return;
            sessions.Remove(connection);
            connection.Disconnected -= Detach;
            IDictionary handlers = Handlers == null ? null : Handlers.GetValue(connection) as IDictionary;
            if (handlers != null && ReferenceEquals(handlers[(MessageType)WireId], session.Handler)) connection.ClearHandler((MessageType)WireId);
            foreach (PendingPair pair in pairs.RemovePeer(connection)) DenyPair(pair, "A player disconnected before the friend card was confirmed.");
            if (tablet != null) tablet.Disconnected(connection);
        }
        private void Receive(Session session, Connection sender, Alta.Serialization.Stream stream)
        {
            if (!stream.IsReading || !ReferenceEquals(sender, session.Connection)) return;
            string text = "";
            try
            {
                stream.SerializeString(ref text, (Alta.Serialization.Stream.StringEncoding)0);
                if (text == null || text.Length > 1024) return;
                JObject packet;
                using (var reader = new JsonTextReader(new StringReader(text)) { MaxDepth = 4 })
                { packet = JObject.Load(reader); if (reader.Read()) return; }
                if ((int?)packet["v"] != 2) return;
                string kind = (string)packet["kind"];
                if (kind != "mesh_identity" && kind != "mesh_pair_confirm" && kind != "tablet_request" && kind != "mesh_verify_confirm") return;
                if (Interlocked.Increment(ref queued) > 256) { Interlocked.Decrement(ref queued); return; }
                queue.Enqueue(delegate { Handle(session, packet); });
            }
            catch (JsonException) { }
            catch (InvalidCastException) { }
            catch (FormatException) { }
            catch (ArgumentException) { }
        }
        private void Handle(Session session, JObject packet)
        {
            Session active; Connection connection = session.Connection;
            if (!sessions.TryGetValue(connection, out active) || !ReferenceEquals(active, session) || connection.IsDisposed || !connection.IsApproved || connection.Player == null) return;
            DateTime now = DateTime.UtcNow;
            if ((now - session.Window).TotalSeconds >= 10) { session.Window = now; session.Received = 0; }
            if (++session.Received > 30) return;
            string kind = (string)packet["kind"];
            if (kind == "tablet_request")
            {
                if (tablet != null) tablet.Request(connection, packet);
                return;
            }
            if (kind == "mesh_verify_confirm")
            {
                if (tablet != null) tablet.Confirm(connection, (string)packet["pair_nonce"]);
                return;
            }
            if (kind == "mesh_identity")
            {
                string address = AddressCodec.Normalize((string)packet["address"]);
                if (address == null) return;
                if (session.Peer != null)
                {
                    if (session.Peer.Address != address) Error(connection, "Your mesh identity changed. Rejoin the server before exchanging cards.", 0);
                    else Send(connection, new JObject { { "v", 2 }, { "kind", "mesh_bound" } });
                    return;
                }
                // This address is still a claim until the peer channel confirms
                // it. A claim must not reserve a key and block its real owner.
                string name = connection.Player.UserInfo.Username ?? "Friend";
                session.Peer = new PairPeer { Connection = connection, NativeId = connection.Player.UserInfo.Identifier, Address = address, Name = name.Length > 64 ? name.Substring(0, 64) : name };
                Send(connection, new JObject { { "v", 2 }, { "kind", "mesh_bound" } });
                return;
            }
            if (session.Peer == null || !StillBound(session.Peer)) return;
            PendingPair pair;
            if (pairs.Confirm(connection, (string)packet["pair_nonce"], now, out pair) != PairConfirmation.Complete) return;
            if (!StillBound(pair.Left) || !StillBound(pair.Right)) { DenyPair(pair, "A player changed sessions before the friend card was confirmed."); return; }
            Connection left = (Connection)pair.Left.Connection, right = (Connection)pair.Right.Connection;
            bool existing = left.Player.FriendshipManager.IsFriendsWith(pair.Right.NativeId) && right.Player.FriendshipManager.IsFriendsWith(pair.Left.NativeId);
            AddNativeFriend(pair.Left, pair.Right); AddNativeFriend(pair.Right, pair.Left);
            SendFriend(pair.Left, pair.Right); SendFriend(pair.Right, pair.Left);
            FinishCard(pair.Tag as FriendRequestToken, existing ? 1 : 0);
        }
        private bool StillBound(PairPeer peer)
        {
            Connection connection = peer.Connection as Connection; Session session;
            return connection != null && !connection.IsDisposed && connection.IsApproved && connection.Player != null &&
                sessions.TryGetValue(connection, out session) && ReferenceEquals(session.Peer, peer) && connection.Player.UserInfo.Identifier == peer.NativeId;
        }
        internal PairPeer TabletPeer(Connection connection)
        {
            Session session;
            return connection != null && sessions.TryGetValue(connection, out session) && session.Peer != null && StillBound(session.Peer) ? session.Peer : null;
        }
        internal bool TabletAllowsJoin(Connection connection, Alta.Serialization.Stream stream)
        { return tablet != null ? tablet.AllowJoin(connection, stream) : TabletService.Reject(connection, "The server's moderation state is unavailable."); }
        internal bool Card(FriendRequestToken card)
        {
            if (Current != this || !NetworkSceneManager.IsServer) return true;
            // Only intercept cards owned by a mesh connection. Another message-34
            // owner retains its own card flow when both mods are installed.
            Session owner = sessions.Values.FirstOrDefault(x => x.Connection.Player != null && x.Connection.Player.UserInfo.Identifier == card.Owner);
            if (owner == null) return true;
            if (!pendingCards.Add(card)) return false;
            int rightId = (int)AccessTools.Field(typeof(FriendRequestToken), "otherTokenId").GetValue(card);
            Session other = sessions.Values.FirstOrDefault(x => x.Connection.Player != null && x.Connection.Player.UserInfo.Identifier == rightId);
            bool physicalConsent = consent.Consume(card.GetInstanceID(), card.Owner, rightId, DateTime.UtcNow);
            Timeline.IEntry entry = (Timeline.IEntry)AccessTools.Field(typeof(FriendRequestToken), "timelineEntry").GetValue(card);
            Timeline.Cancel(ref entry);
            AccessTools.Field(typeof(FriendRequestToken), "timelineEntry").SetValue(card, entry);
            AccessTools.Field(typeof(FriendRequestToken), "receivedOwnerToken").SetValue(card, false);
            AccessTools.Field(typeof(FriendRequestToken), "otherTokenId").SetValue(card, -1);
            if (!physicalConsent || owner.Peer == null || other == null || other.Peer == null || !StillBound(owner.Peer) || !StillBound(other.Peer))
            {
                Error(owner.Connection, "Both players need the mesh mod connected before exchanging friend cards.", rightId);
                if (other != null) Error(other.Connection, "Both players need the mesh mod connected before exchanging friend cards.", card.Owner);
                FinishCard(card, 2); return false;
            }
            PendingPair pair = pairs.Create(owner.Peer, other.Peer, card, DateTime.UtcNow);
            if (pair == null)
            {
                Error(owner.Connection, "A friend-card confirmation is already pending. Wait for it to finish, then try again.", rightId);
                Error(other.Connection, "A friend-card confirmation is already pending. Wait for it to finish, then try again.", card.Owner);
                FinishCard(card, 2); return false;
            }
            SendPair(pair.Left, pair.Right, pair); SendPair(pair.Right, pair.Left, pair);
            return false;
        }
        internal void RecordOwner(FriendRequestToken card, int friend)
        { if (NetworkSceneManager.IsServer) consent.Owner(card.GetInstanceID(), card.Owner, friend, DateTime.UtcNow); }
        internal void RecordPeer(FriendRequestToken card, Alta.Networking.Scripts.Player.IPlayer peer)
        { if (NetworkSceneManager.IsServer && peer != null) consent.Peer(card.GetInstanceID(), card.Owner, peer.UserInfo.Identifier, DateTime.UtcNow); }
        private static void SendPair(PairPeer owner, PairPeer friend, PendingPair pair)
        {
            long expires = (long)(pair.Expires - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
            Send((Connection)owner.Connection, new JObject { { "v", 2 }, { "kind", "mesh_pair" }, { "address", friend.Address }, { "name", friend.Name }, { "native_id", friend.NativeId }, { "pair_nonce", pair.Nonce }, { "expires_unix", expires } });
        }
        private static void AddNativeFriend(PairPeer owner, PairPeer friend)
        {
            Connection connection = (Connection)owner.Connection;
            if (!connection.Player.FriendshipManager.IsFriendsWith(friend.NativeId))
                connection.Player.FriendshipManager.AddFriend(new FriendshipInfo { Identifier = friend.NativeId, Username = friend.Name, Type = FriendshipType.Accepted, CreatedAt = DateTime.UtcNow });
        }
        private static void SendFriend(PairPeer owner, PairPeer friend)
        { Send((Connection)owner.Connection, new JObject { { "v", 2 }, { "kind", "mesh_friend" }, { "native_id", friend.NativeId }, { "address", friend.Address }, { "name", friend.Name } }); }
        private void DenyPair(PendingPair pair, string reason)
        {
            Error((Connection)pair.Left.Connection, reason, pair.Right.NativeId);
            Error((Connection)pair.Right.Connection, reason, pair.Left.NativeId);
            FinishCard(pair.Tag as FriendRequestToken, 2);
        }
        private void FinishCard(FriendRequestToken card, int result)
        {
            pendingCards.Remove(card);
            if (card == null || card.Entity.IsBeingDestroyed) return;
            try
            {
                MethodInfo effects = AccessTools.Method(typeof(FriendRequestToken), "RunEffects");
                object enumValue = Enum.ToObject(effects.GetParameters()[0].ParameterType, result);
                effects.Invoke(card, new[] { enumValue });
                var sync = (MethodSyncStruct<int>)AccessTools.Field(typeof(FriendRequestToken), "syncFriendshipRequestEffect").GetValue(card);
                sync.SendToChunks(result); card.Fade(false);
            }
            catch (Exception ex) { LoggerInstance.Warning("Mesh card completion failed: " + ex.GetType().Name); }
        }
        private static void Error(Connection connection, string message, int nativeId)
        { Send(connection, new JObject { { "v", 2 }, { "kind", "mesh_error" }, { "message", message }, { "native_id", nativeId } }); }
        internal static void Send(Connection connection, JObject packet)
        {
            if (connection == null || connection.IsDisposed || !connection.IsApproved) return;
            string text = packet.ToString(Formatting.None);
            connection.Send(null, (MessageType)WireId, delegate(Connection target, Alta.Serialization.Stream stream) { stream.SerializeString(ref text, (Alta.Serialization.Stream.StringEncoding)0); });
        }
    }
    [HarmonyPatch(typeof(Connection), MethodType.Constructor)]
    internal static class AttachMeshConnection
    { private static void Postfix(Connection __instance) { if (Companion.Current != null) Companion.Current.Attach(__instance); } }
    [HarmonyPatch(typeof(FriendRequestToken), "AddFriend")]
    internal static class NativeMeshFriendCard
    { private static bool Prefix(FriendRequestToken __instance) { return Companion.Current == null || Companion.Current.Card(__instance); } }
    [HarmonyPatch(typeof(FriendRequestToken), "SyncFriendRequestForPlayer")]
    internal static class MeshCardOwnerConsent
    { private static void Prefix(FriendRequestToken __instance, int friendID) { if (Companion.Current != null) Companion.Current.RecordOwner(__instance, friendID); } }
    [HarmonyPatch(typeof(FriendRequestToken), "SyncFriendRequestForTokenOwner")]
    internal static class MeshCardPeerConsent
    { private static void Prefix(FriendRequestToken __instance, Alta.Networking.Scripts.Player.IPlayer player) { if (Companion.Current != null) Companion.Current.RecordPeer(__instance, player); } }
}
