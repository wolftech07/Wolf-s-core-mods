using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using TavernNativeSocial;

internal static class RelayTests
{
    private static int checks;
    private static SocialRelay relay;
    private static readonly Dictionary<string, object> Empty = new Dictionary<string, object>();
    private static void Check(bool condition, string name) { if (!condition) throw new Exception("FAIL: " + name); checks++; Console.WriteLine("PASS " + name); }
    private static IDictionary<string, object> Obj(object value) { return SocialRelay.Json().DeserializeObject(SocialRelay.Json().Serialize(value)) as IDictionary<string, object>; }
    private static IDictionary<string, object> Req(string method, string route, string token, object body)
    { return Obj(relay.Dispatch(method, route, token, Obj(body ?? Empty), "test")); }
    private static string Text(IDictionary<string, object> obj, string key) { return SocialRelay.Text(obj, key); }
    private static object[] Array(IDictionary<string, object> obj, string key) { return (object[])obj[key]; }
    private static void Fails(int status, Action action, string name)
    {
        try { action(); throw new Exception("FAIL: accepted " + name); }
        catch (ApiError error) { Check(error.Status == status, name); }
    }
    private static IDictionary<string, object> User(string name) { return Req("POST", "/v1/register", "", new { name = name }); }
    private static string Ticket(string token, string server) { return Text(Req("POST", "/v1/session-ticket", token, new { server_id = server }), "ticket"); }
    private static int Main(string[] args)
    {
        try { Run(args); return 0; }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
    private static void Run(string[] args)
    {
        string root = Path.GetFullPath(args[0]); Directory.CreateDirectory(root);
        var proof = new CardConsent(); DateTime now = DateTime.UtcNow;
        proof.Owner(1, 101, 202, now); proof.Owner(1, 101, 202, now);
        Check(!proof.Consume(1, 101, 202, now), "repeated owner packet cannot forge peer card consent");
        proof.Owner(1, 101, 202, now); proof.Peer(1, 101, 202, now);
        Check(proof.Consume(1, 101, 202, now), "owner then peer reciprocal card accepted");
        Check(!proof.Consume(1, 101, 202, now), "card consent cannot be replayed");
        proof.Peer(1, 101, 202, now); proof.Owner(1, 101, 202, now);
        Check(proof.Consume(1, 101, 202, now), "peer then owner reciprocal card accepted");
        proof.Owner(1, 101, 202, now); proof.Peer(1, 101, 303, now);
        Check(!proof.Consume(1, 101, 202, now), "a different peer cannot approve a friendship");
        proof.Owner(1, 101, 202, now); proof.Peer(1, 101, 202, now);
        Check(!proof.Consume(1, 101, 202, now.AddSeconds(3)), "expired card consent rejected");
        proof.Owner(1, 101, 202, now); proof.Peer(2, 101, 202, now);
        Check(!proof.Consume(1, 101, 202, now), "consent from a different card cannot be reused");
        string path = Path.Combine(root, "social-data.json"); relay = new SocialRelay(path);
        Check(Text(Req("GET", "/v1/health", "", null), "service") == "TavernNativeSocial", "health available without account");
        Fails(401, delegate { Req("GET", "/v1/friends", "", null); }, "anonymous friendship access denied");
        var alice = User("Alice"); var bob = User("Bob"); var outsider = User("Other");
        string at = Text(alice, "token"), bt = Text(bob, "token"), ot = Text(outsider, "token");
        string aid = Text(alice, "social_id"), bid = Text(bob, "social_id"), oid = Text(outsider, "social_id");
        Check(aid.Length == 32 && aid != bid && at.Length > 40, "opaque distinct global IDs and high-entropy credentials");
        var host = Obj(relay.CreateServer("Card test server")); var host2 = Obj(relay.CreateServer("Other server"));
        string sid = Text(host, "server_id"), st = Text(host, "server_token"), st2 = Text(host2, "server_token");
        Fails(401, delegate { Req("POST", "/v1/server/friendship", at, new { left_id = aid, right_id = bid }); }, "user cannot assert a card friendship");
        Fails(401, delegate { Req("GET", "/v1/friends", st, null); }, "server credential cannot read private user friend lists");
        string ticket = Ticket(at, sid);
        Fails(401, delegate { Req("POST", "/v1/server/resolve-ticket", st2, new { ticket = ticket }); }, "session ticket bound to selected server");
        var resolved = Req("POST", "/v1/server/resolve-ticket", st, new { ticket = ticket });
        Check(Text(resolved, "social_id") == aid, "ticket resolves social identity independently of Tavern user ID");
        Fails(401, delegate { Req("POST", "/v1/server/resolve-ticket", st, new { ticket = ticket }); }, "ticket is single use");
        Fails(403, delegate { Req("POST", "/v1/server/friendship", st, new { left_id = aid, right_id = bid }); }, "both participants must have verified sessions");
        string bticket = Ticket(bt, sid); Req("POST", "/v1/server/resolve-ticket", st, new { ticket = bticket });
        Fails(403, delegate { Req("POST", "/v1/server/friendship", st2, new { left_id = aid, right_id = bid }); }, "unrelated trusted server cannot assert pair from other server");
        Check((bool)Req("POST", "/v1/server/friendship", st, new { left_id = aid, right_id = bid })["existing"] == false, "reciprocal card friendship committed");
        Check((bool)Req("POST", "/v1/server/friendship", st, new { left_id = aid, right_id = bid })["existing"] == true, "repeated card exchange is idempotent");
        Check(Array(Req("GET", "/v1/friends", at, null), "friends").Length == 1 && Array(Req("GET", "/v1/friends", bt, null), "friends").Length == 1, "friendship visible on both sides");
        Check(Array(Req("GET", "/v1/friends", ot, null), "friends").Length == 0, "outsider cannot see friendship");
        Check(Array(Req("POST", "/v1/server/friendships", st, Empty), "pairs").Length == 1 && Array(Req("POST", "/v1/server/friendships", st2, Empty), "pairs").Length == 0, "native reconnect pairs restricted to verified server sessions");
        var address = new { name = "Private world", host = "private.example.org", game_port = 1857, auth_port = 1862, password = "notforwarded", token = "notforwarded" };
        Fails(403, delegate { Req("POST", "/v1/invites", at, new { to_id = oid, server = address }); }, "invites require existing accepted friendship");
        string inviteId = Text(Req("POST", "/v1/invites", at, new { to_id = bid, server = address }), "invite_id");
        Check(Array(Req("GET", "/v1/invites", bt, null), "invites").Length == 1, "intended recipient can poll invite");
        Check(Array(Req("GET", "/v1/invites", ot, null), "invites").Length == 0 && Array(Req("GET", "/v1/invites", at, null), "invites").Length == 0, "private server address excluded from others' inboxes");
        Fails(404, delegate { Req("POST", "/v1/invites/" + inviteId + "/accept", ot, Empty); }, "guessed invitation ID does not grant access");
        var accepted = Req("POST", "/v1/invites/" + inviteId + "/accept", bt, Empty);
        Check(Text(Obj(accepted["server"]), "host") == "private.example.org", "accept returns actual private endpoint");
        Check(!SocialRelay.Json().Serialize(accepted).Contains("notforwarded"), "invites never forward passwords or Tavern tokens");
        Fails(409, delegate { Req("POST", "/v1/invites/" + inviteId + "/accept", bt, Empty); }, "handled invite not consumed twice");
        string second = Text(Req("POST", "/v1/invites", at, new { to_id = bid, server = address }), "invite_id");
        Check(Text(Req("POST", "/v1/invites/" + second + "/dismiss", bt, Empty), "status") == "dismissed", "recipient can dismiss invitation");
        Fails(400, delegate { Req("POST", "/v1/invites", at, new { to_id = bid, server = new { name = "Bad", host = "https://bad/path", game_port = 70000 } }); }, "malformed invitation host rejected");
        string pending = Text(Req("POST", "/v1/invites", at, new { to_id = bid, server = address }), "invite_id");
        Req("DELETE", "/v1/friends/" + bid, at, null);
        Check(Array(Req("GET", "/v1/friends", bt, null), "friends").Length == 0 && Array(Req("GET", "/v1/invites", bt, null), "invites").Length == 0, "unfriend removes reciprocal relation and private invites");
        Fails(404, delegate { Req("POST", "/v1/invites/" + pending + "/accept", bt, Empty); }, "unfriended sender invite revoked");
        Req("POST", "/v1/server/friendship", st, new { left_id = aid, right_id = bid });
        Req("POST", "/v1/server/session-end", st, new { social_id = aid });
        var heartbeat = Req("POST", "/v1/server/session-heartbeat", st, new { ids = new[] { aid, bid } });
        Check(Array(heartbeat, "expired_ids").Length == 1, "heartbeats cannot invent or resurrect unverified sessions");
        Fails(403, delegate { Req("POST", "/v1/server/friendship", st, new { left_id = aid, right_id = bid }); }, "disconnected session cannot exchange card");
        Check(!File.ReadAllText(path).Contains(at) && !File.ReadAllText(path).Contains(st), "relay stores hashes rather than bearer secrets");
        relay = new SocialRelay(path);
        Check(Array(Req("GET", "/v1/friends", at, null), "friends").Length == 1, "friendship persists across service restart");
        Check(!(bool)Obj(Array(Req("GET", "/v1/friends", at, null), "friends")[0])["online"], "relay restart clears stale presence until next heartbeat");
        SocialData saved = SocialRelay.Json().Deserialize<SocialData>(File.ReadAllText(path));
        saved.users.Single(x => x.social_id == bid).last_seen_utc = DateTime.UtcNow.AddSeconds(-61).ToString("o");
        File.WriteAllText(path, SocialRelay.Json().Serialize(saved)); relay = new SocialRelay(path);
        Check(!(bool)Obj(Array(Req("GET", "/v1/friends", at, null), "friends")[0])["online"], "presence expires after client stops heartbeat");
        Req("POST", "/v1/presence", bt, Empty);
        Check((bool)Obj(Array(Req("GET", "/v1/friends", at, null), "friends")[0])["online"], "client heartbeat restores online presence");
        var logs = new List<string>();
        using (var listener = new RelayListener(relay, delegate(string line) { lock (logs) logs.Add(line); }))
        {
            listener.Start(0); string url = "http://127.0.0.1:" + listener.Port;
            var web = (HttpWebRequest)WebRequest.Create(url + "/v1/me"); web.Headers[HttpRequestHeader.Authorization] = "Bearer " + at;
            using (var response = web.GetResponse()) using (var reader = new StreamReader(response.GetResponseStream())) Check(reader.ReadToEnd().Contains(aid), "loopback HTTP real request and authorization");
            string malformed = SendRaw(listener.Port, "POST /v1/presence HTTP/1.1\r\nHost: localhost\r\nContent-Length: 2\r\nContent-Length: 2\r\n\r\n{}");
            Check(malformed.StartsWith("HTTP/1.1 413"), "duplicate HTTP body length rejected");
            Check(SendRaw(listener.Port, "POST /v1/presence HTTP/1.1\r\nHost: localhost\r\nTransfer-Encoding: chunked\r\n\r\n0\r\n\r\n").StartsWith("HTTP/1.1 400"), "chunked request rejected");
            Check(SendRaw(listener.Port, "POST /v1/register HTTP/1.1\r\nHost: localhost\r\nContent-Length: 65537\r\n\r\n").StartsWith("HTTP/1.1 413"), "oversized body rejected before allocation");
            Check(SendRaw(listener.Port, "POST /v1/register HTTP/1.1\r\nHost: localhost\r\nContent-Length: 1\r\n\r\n[").StartsWith("HTTP/1.1 400"), "malformed JSON rejected");
        }
        lock (logs) Check(!String.Join("\n", logs).Contains(at) && !String.Join("\n", logs).Contains("private.example.org"), "live log excludes credentials and private invitation endpoints");
        Console.WriteLine("All " + checks + " social relay checks passed.");
    }
    private static string SendRaw(int port, string text)
    {
        using (var client = new TcpClient("127.0.0.1", port))
        {
            client.ReceiveTimeout = 3000;
            using (var stream = client.GetStream())
            {
                byte[] bytes = Encoding.ASCII.GetBytes(text); stream.Write(bytes, 0, bytes.Length);
                using (var reader = new StreamReader(stream))
                {
                    // Parse the declared HTTP response, not EOF. A server may
                    // close with unread rejected request bytes still buffered.
                    string first = reader.ReadLine(), line; int length = 0;
                    while (!String.IsNullOrEmpty(line = reader.ReadLine()))
                        if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase)) length = Int32.Parse(line.Substring(15).Trim());
                    char[] body = new char[length]; int offset = 0;
                    while (offset < length) { int read = reader.Read(body, offset, length - offset); if (read == 0) break; offset += read; }
                    return first + "\r\n" + new string(body, 0, offset);
                }
            }
        }
    }
}
