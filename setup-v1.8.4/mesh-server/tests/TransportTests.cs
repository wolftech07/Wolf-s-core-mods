using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Alta.Networking;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TavernNativeMenu;
using TavernNativeMeshServer;

internal static class TransportTests
{
    private static int checks;
    private static void Check(bool okay, string name)
    { if (!okay) throw new Exception(name); checks++; Console.WriteLine("PASS " + name); }
    private static string Address(int seed)
    {
        byte[] bytes = new byte[38];
        for (int i = 0; i < 32; i++) bytes[i] = (byte)(i + seed);
        for (int i = 0; i < 36; i += 2) { bytes[36] ^= bytes[i]; bytes[37] ^= bytes[i + 1]; }
        return BitConverter.ToString(bytes).Replace("-", "");
    }
    private static JObject Pair(string address, int id, string nonce, long expires)
    { return new JObject { { "v", 2 }, { "kind", "mesh_pair" }, { "address", address }, { "name", "Peer" }, { "native_id", id }, { "pair_nonce", nonce }, { "expires_unix", expires } }; }
    private static JObject Friend(string address, int id)
    { return new JObject { { "v", 2 }, { "kind", "mesh_friend" }, { "address", address }, { "native_id", id }, { "name", "Peer" } }; }
    private static int Sent(Connection connection, string kind)
    { return connection.Sent.Count(x => (string)x["kind"] == kind); }
    private static void Receive(Connection connection, JObject packet)
    { connection.Receive(packet); MeshSocialClient.Drain(); }
    private static string RequestId(Connection connection)
    { return (string)connection.Sent.Last(x => (string)x["kind"] == "tablet_request")["request_id"]; }
    private static JObject Reply(string id, JObject data)
    { return new JObject { { "v", 2 }, { "kind", "tablet_reply" }, { "request_id", id }, { "ok", true }, { "data", data } }; }
    private static void TabletTests(Connection connection, string peer, string later)
    {
        Task<JObject> unsupported = MeshSocialTransport.TabletRequestAsync("roster", 0); MeshSocialClient.Drain();
        Check(unsupported.IsFaulted, "older companion fails tablet request with upgrade instruction");
        Receive(connection, new JObject { { "v", 2 }, { "kind", "mesh_hello" }, { "tablet", true }, { "tablet_version", 1 } });
        Task<JObject> roster = MeshSocialTransport.TabletRequestAsync("roster", 0); MeshSocialClient.Drain(); string rid = RequestId(connection);
        var players = new JArray(new JObject { { "id", 20 }, { "name", "Peer" }, { "address", peer }, { "can_target", false } });
        Receive(connection, Reply(Guid.NewGuid().ToString("N"), new JObject()));
        Check(!roster.IsCompleted, "unsolicited tablet reply cannot complete a request");
        Receive(connection, Reply(rid, new JObject { { "server_key", "serverA" }, { "players", players } }));
        Check(roster.IsCompleted && !roster.IsFaulted && MeshSocialTransport.CurrentServerKey == "serverA", "roster reply binds stable server scope");
        Check(MeshSocialTransport.VerifiedAddress(20) == peer, "completed friend card retains verified player mapping");
        Task<JObject> forged = MeshSocialTransport.TabletRequestAsync("verify", 30); MeshSocialClient.Drain();
        string proofNonce = new string('g', 43);
        var proofData = new JObject { { "id", 30 }, { "address", later }, { "name", "Later" }, { "verified", true }, { "pair_nonce", proofNonce } };
        Receive(connection, Reply(RequestId(connection), proofData));
        Check(forged.IsFaulted && MeshSocialTransport.VerifiedAddress(30) == null, "server claim without authenticated local proof cannot map identity");
        Task<JObject> verification = MeshSocialTransport.TabletRequestAsync("verify", 30); MeshSocialClient.Drain(); string vid = RequestId(connection);
        JObject challenge = Pair(later, 30, proofNonce, MeshSocialClient.Now + 120); challenge["kind"] = "mesh_verify";
        Receive(connection, challenge);
        Check(MeshSocialClient.ProofCalls == 1 && MeshSocialClient.LastProofNonce == proofNonce, "tablet challenge begins separate identity-only exchange");
        MeshSocialClient.VerifyIdentity(peer.Substring(0, 64), proofNonce);
        MeshSocialClient.VerifyIdentity(later.Substring(0, 64), new string('h', 43));
        Check(Sent(connection, "mesh_verify_confirm") == 0, "wrong identity and nonce cannot confirm tablet proof");
        MeshSocialClient.VerifyIdentity(later.Substring(0, 64), proofNonce);
        MeshSocialClient.VerifyIdentity(later.Substring(0, 64), proofNonce);
        Check(Sent(connection, "mesh_verify_confirm") == 1, "valid identity-only proof acknowledges exactly once");
        Receive(connection, Reply(vid, proofData));
        Check(verification.IsCompleted && !verification.IsFaulted && MeshSocialTransport.VerifiedAddress(30) == later, "verified tablet response maps correct native player");
        Check(!Player.Current.FriendshipManager.IsFriendsWith(30), "identity proof never creates friendship");
        Task<JObject> refresh = MeshSocialTransport.TabletRequestAsync("roster", 0); MeshSocialClient.Drain();
        Receive(connection, Reply(RequestId(connection), new JObject { { "server_key", "serverA" }, { "players", players.DeepClone() } }));
        Check(!refresh.IsFaulted && MeshSocialTransport.VerifiedAddress(30) == null, "player leaving roster invalidates verified mapping");
        Task<JObject> denied = MeshSocialTransport.TabletRequestAsync("ban", 20); MeshSocialClient.Drain();
        var rejection = Reply(RequestId(connection), new JObject()); rejection["ok"] = false; rejection["message"] = "Owner permission required";
        Receive(connection, rejection);
        Check(denied.IsFaulted && denied.Exception.InnerException.Message.Contains("permission"), "server authority rejection reaches tablet");
        Task<JObject> unknown = MeshSocialTransport.TabletRequestAsync("invented", 20); MeshSocialClient.Drain();
        Check(unknown.IsFaulted, "unknown tablet actions never reach server");
        Task<JObject> pending = MeshSocialTransport.TabletRequestAsync("roster", 0); MeshSocialClient.Drain();
        connection.Disconnect();
        Check(pending.IsFaulted && MeshSocialTransport.CurrentServerKey == null && MeshSocialTransport.VerifiedAddress(20) == null, "disconnect cancels pending tablet work and clears identity scope");
    }
    private static void PagingTests(Socket socket, string peer)
    {
        var connection = new Connection(socket); socket.Add(connection);
        MeshSocialClient.Configured = false;
        Receive(connection, new JObject { { "v", 2 }, { "kind", "mesh_hello" }, { "tablet", true }, { "tablet_version", 1 } });
        Check(Sent(connection, "tablet_request") == 1 && Sent(connection, "mesh_identity") == 0, "server scope primes without the peer helper or an open tablet");
        Receive(connection, Reply(RequestId(connection), new JObject { { "server_key", "serverB" }, { "players", new JArray() }, { "bans", new JArray() } }));
        Check(MeshSocialTransport.CurrentServerKey == "serverB", "saved local blocks can acquire their scope while peer helper is unavailable");
        Task<JObject> roster = MeshSocialTransport.TabletRequestAsync("roster", 0); MeshSocialClient.Drain();
        var first = new JObject { { "server_key", "serverB" }, { "players", new JArray(new JObject { { "id", 20 }, { "address", peer } }) },
            { "bans", new JArray(new JObject { { "id", 100 }, { "name", "First ban" } }) }, { "next_player_offset", -1 }, { "next_ban_offset", 1 } };
        int before = Sent(connection, "tablet_request"); Receive(connection, Reply(RequestId(connection), first));
        DateTime until = DateTime.UtcNow.AddSeconds(3);
        while (Sent(connection, "tablet_request") == before && DateTime.UtcNow < until) { System.Threading.Thread.Sleep(10); MeshSocialClient.Drain(); }
        Check((int?)connection.Sent.Last()["ban_offset"] == 1, "older bans are requested using server pagination");
        Receive(connection, Reply(RequestId(connection), new JObject { { "server_key", "serverB" }, { "players", first["players"].DeepClone() },
            { "bans", new JArray(new JObject { { "id", 101 }, { "name", "Older ban" } }) }, { "next_player_offset", -1 }, { "next_ban_offset", -1 } }));
        until = DateTime.UtcNow.AddSeconds(1);
        while (!roster.IsCompleted && DateTime.UtcNow < until) { System.Threading.Thread.Sleep(10); MeshSocialClient.Drain(); }
        Check(roster.IsCompleted && !roster.IsFaulted && ((JArray)roster.Result["bans"]).Count == 2 && ((JArray)roster.Result["players"]).Count == 1, "paged bans merge without duplicating current players");
        Task<JObject> noProof = MeshSocialTransport.TabletRequestAsync("verify", 20); MeshSocialClient.Drain();
        Check(noProof.IsFaulted, "identity proof still requires bound peer networking");
        connection.Disconnect(); MeshSocialClient.Configured = true;
    }
    private static int Main()
    {
        try
        {
            Player.Current = new Player(10, "Local"); new Player(20, "Peer"); new Player(30, "Later");
            MeshSocialClient.Address = Address(10); string peer = Address(20), later = Address(30);
            var socket = new Socket(); Socket.Current = socket;
            var connection = new Connection(socket); socket.Add(connection);
            MeshSocialTransport.Initialize();
            Check(connection.HasHandler(34), "native client handler installed on existing connection");
            Receive(connection, new JObject { { "v", 2 }, { "kind", "mesh_hello" } });
            Check(Sent(connection, "mesh_identity") == 0, "identity waits for peer network startup");
            MeshSocialClient.Configured = true; MeshSocialTransport.Tick();
            Check(Sent(connection, "mesh_identity") == 1 && (string)connection.Sent.Last()["address"] == MeshSocialClient.Address, "ready client binds global address over native connection");
            Receive(connection, new JObject { { "v", 2 }, { "kind", "mesh_bound" } });
            MeshSocialTransport.Tick();
            Check(Sent(connection, "mesh_identity") == 1, "confirmed identity is not retransmitted");
            string nonce = new string('a', 43);
            Receive(connection, Pair(peer, 20, nonce, MeshSocialClient.Now + 120));
            Check(MeshSocialClient.PairCalls == 1 && MeshSocialClient.LastPairNonce == nonce, "native consent begins peer ownership challenge");
            Receive(connection, Pair(peer, 20, nonce, MeshSocialClient.Now + 120));
            Check(MeshSocialClient.PairCalls == 1, "duplicate native pairing is ignored");
            Receive(connection, Friend(peer, 20));
            Check(!Player.Current.FriendshipManager.IsFriendsWith(20), "unsolicited native success cannot accept friendship");
            MeshSocialClient.Verify(later.Substring(0, 64), nonce);
            MeshSocialClient.Verify(peer.Substring(0, 64), new string('b', 43));
            Check(Sent(connection, "mesh_pair_confirm") == 0, "wrong peer or wrong nonce cannot acknowledge");
            MeshSocialClient.Verify(peer.Substring(0, 64), nonce);
            MeshSocialClient.Verify(peer.Substring(0, 64), nonce);
            Check(Sent(connection, "mesh_pair_confirm") == 1, "verified peer sends exactly one native acknowledgement");
            Receive(connection, Friend(peer, 20));
            Check(!Player.Current.FriendshipManager.IsFriendsWith(20), "native success still requires accepted local peer graph");
            MeshSocialClient.Friends.Add(new JObject { { "social_id", peer.Substring(0, 64) }, { "name", "Peer" }, { "online", true } });
            int linked = 0; MeshSocialTransport.FriendLinked += delegate { linked++; };
            Receive(connection, Friend(peer, 20));
            Check(Player.Current.FriendshipManager.IsFriendsWith(20) && Player.Get(20).FriendshipManager.Status && linked == 1, "confirmed peer updates native card friendship once");
            Receive(connection, Friend(peer, 20));
            Check(linked == 1, "replayed success cannot duplicate native friendship event");
            string nextNonce = new string('c', 43);
            Player.Get(30).FriendshipManager.Status = true;
            Receive(connection, Pair(later, 30, nextNonce, MeshSocialClient.Now + 120));
            MeshSocialClient.Now += 120; MeshSocialTransport.Tick();
            Check(!Player.Get(30).FriendshipManager.Status, "expired card clears optimistic grab status");
            MeshSocialClient.Verify(later.Substring(0, 64), nextNonce);
            Check(Sent(connection, "mesh_pair_confirm") == 1, "expired pair cannot acknowledge later");
            Receive(connection, Pair(later, 30, new string('d', 43), MeshSocialClient.Now + 200));
            Receive(connection, Pair(later.Substring(0, 74) + "FF", 30, new string('e', 43), MeshSocialClient.Now + 120));
            Check(MeshSocialClient.PairCalls == 2, "invalid expiry and checksum do not reach peer worker");
            Receive(connection, new JObject { { "v", 1 }, { "kind", "hello" }, { "relay_url", "https://old.example" } });
            Check(MeshSocialClient.ErrorText.Contains("old relay-based"), "old companion gets useful upgrade message");
            TabletTests(connection, peer, later);
            connection = new Connection(socket); socket.Add(connection);
            SerializeConnectionMethod replacement = delegate { };
            connection.SetHandler((MessageType)34, replacement); connection.Disconnect();
            Check(ReferenceEquals(connection.Handler(34), replacement), "detachment preserves replacement mod handler");
            var collision = new Connection(socket); collision.SetHandler((MessageType)34, replacement); socket.Add(collision);
            Check(ReferenceEquals(collision.Handler(34), replacement) && MeshSocialClient.ErrorText.Contains("another mod"), "existing native channel owner is not overwritten");
            var unapproved = new Connection(socket) { IsApproved = false }; socket.Add(unapproved);
            Receive(unapproved, new JObject { { "v", 2 }, { "kind", "mesh_hello" } });
            Check(Sent(unapproved, "mesh_identity") == 0, "unapproved connection receives no identity");
            PagingTests(socket, peer);
            Console.WriteLine("Passed " + checks + " mesh native transport checks."); return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}

namespace TavernNativeMenu
{
    internal static class NativeSocialTablet
    {
        internal static bool IsPlayerBlocked(int id) { return false; }
    }
    internal static class MeshSocialClient
    {
        internal static string Address, ErrorText, LastPairNonce, LastProofNonce;
        internal static string SocialId { get { return Address.Substring(0, 64); } }
        internal static bool Configured;
        internal static long Now = 2000000000;
        internal static int PairCalls, ProofCalls;
        internal static JArray Friends = new JArray();
        private static readonly Queue<Action> actions = new Queue<Action>();
        internal static event Action<string, string> PairVerified;
        internal static event Action<string, string> PeerVerified;
        internal static string ValidateAddress(string value)
        { string result = AddressCodec.Normalize(value); if (result == null) throw new ArgumentException("Invalid address"); return result; }
        internal static void Verify(string key, string nonce) { if (PairVerified != null) PairVerified(key, nonce); }
        internal static void VerifyIdentity(string key, string nonce) { if (PeerVerified != null) PeerVerified(key, nonce); }
        internal static Task VerifyPeerAsync(string code, string nonce, long expires) { ProofCalls++; LastProofNonce = nonce; return Task.FromResult(0); }
        internal static Task PairCardAsync(string code, string name, string nonce, long expires)
        { PairCalls++; LastPairNonce = nonce; return Task.FromResult(0); }
        internal static Task<JArray> GetFriendsAsync() { return Task.FromResult(Friends); }
        internal static void RequestRefresh() { }
        internal static void Post(Action action) { actions.Enqueue(action); }
        internal static void Drain() { while (actions.Count != 0) actions.Dequeue()(); }
        internal static void SetError(Exception error) { ErrorText = error.Message; }
    }
}
namespace Alta.Api.DataTransferModels.Models.Responses
{
    public enum FriendshipType { Accepted }
    public sealed class FriendshipInfo { public int Identifier; public string Username; public FriendshipType Type; public DateTime CreatedAt; }
}
namespace Alta.Networking.Scripts.Player
{
    public interface IPlayer { UserInfo UserInfo { get; } FriendshipManager FriendshipManager { get; } }
}
public sealed class UserInfo { public int Identifier; public string Username; }
public sealed class FriendshipManager
{
    private readonly HashSet<int> friends = new HashSet<int>();
    public bool Status;
    public bool IsFriendsWith(int id) { return friends.Contains(id); }
    public void AddFriend(Alta.Api.DataTransferModels.Models.Responses.FriendshipInfo info) { friends.Add(info.Identifier); }
    public void UpdateStatus(bool value) { Status = value; }
}
public sealed class Player : Alta.Networking.Scripts.Player.IPlayer
{
    public static Player Current;
    private static readonly Dictionary<int, Player> players = new Dictionary<int, Player>();
    public UserInfo UserInfo { get; private set; }
    public FriendshipManager FriendshipManager { get; private set; }
    public Player(int id, string name) { UserInfo = new UserInfo { Identifier = id, Username = name }; FriendshipManager = new FriendshipManager(); players.Add(id, this); }
    public static bool SafeGetPlayer(int id, out Player player) { return players.TryGetValue(id, out player); }
    public static Player Get(int id) { return players[id]; }
}
namespace Alta.Serialization
{
    public class Stream
    {
        public enum StringEncoding { Utf8 }
        public bool IsReading;
        public string Text;
        public void SerializeString(ref string value, StringEncoding encoding) { if (IsReading) value = Text; else Text = value; }
    }
}
namespace Alta.Networking
{
    public enum MessageType { None }
    public delegate void SerializeConnectionMethod(Connection sender, Alta.Serialization.Stream stream);
    public delegate void ConnectionEventHandler(Connection connection);
    public interface ISocket
    {
        bool IsServer { get; }
        IEnumerable<Connection> Connections { get; }
        event ConnectionEventHandler ConnectionCreated;
        event ConnectionEventHandler ConnectionDestroyed;
        event Action<ISocket> SocketDestroyed;
    }
    public sealed class Socket : ISocket
    {
        public static ISocket Current;
        private readonly List<Connection> connections = new List<Connection>();
        public bool IsServer { get { return false; } }
        public IEnumerable<Connection> Connections { get { return connections; } }
        public event ConnectionEventHandler ConnectionCreated;
        public event ConnectionEventHandler ConnectionDestroyed;
        public event Action<ISocket> SocketDestroyed;
        public void Add(Connection connection) { connections.Add(connection); if (ConnectionCreated != null) ConnectionCreated(connection); }
        public void Destroy(Connection connection) { connections.Remove(connection); if (ConnectionDestroyed != null) ConnectionDestroyed(connection); }
        public void Destroy() { if (SocketDestroyed != null) SocketDestroyed(this); }
    }
    public sealed class Connection
    {
        private readonly Dictionary<MessageType, SerializeConnectionMethod> messageHandlers = new Dictionary<MessageType, SerializeConnectionMethod>();
        public bool IsDisposed, IsApproved = true;
        public ISocket Socket;
        public readonly List<JObject> Sent = new List<JObject>();
        public event ConnectionEventHandler Disconnected;
        public Connection(ISocket socket) { Socket = socket; }
        public bool HasHandler(int id) { return messageHandlers.ContainsKey((MessageType)id); }
        public SerializeConnectionMethod Handler(int id) { return messageHandlers[(MessageType)id]; }
        public void SetHandler(MessageType id, SerializeConnectionMethod handler) { messageHandlers[id] = handler; }
        public void ClearHandler(MessageType id) { messageHandlers.Remove(id); }
        public void Send(object unused, MessageType id, SerializeConnectionMethod writer)
        { var stream = new Alta.Serialization.Stream(); writer(this, stream); Sent.Add(JObject.Parse(stream.Text)); }
        public void Receive(JObject packet) { messageHandlers[(MessageType)34](this, new Alta.Serialization.Stream { IsReading = true, Text = packet.ToString(Formatting.None) }); }
        public void Disconnect() { IsDisposed = true; if (Disconnected != null) Disconnected(this); }
    }
}
namespace TavernLib.Utils
{
    public sealed class SocketEvent
    {
        private Action<ISocket> handlers;
        public void Subscribe(Action<ISocket> handler) { handlers += handler; }
        public void Invoke(ISocket socket) { if (handlers != null) handlers(socket); }
    }
    public static class TavernEvents { public static SocketEvent SocketCreated = new SocketEvent(); }
}
