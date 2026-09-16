using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace TavernNativeSocial
{
    public sealed class ApiError : Exception
    {
        public readonly int Status;
        public ApiError(int status, string message) : base(message) { Status = status; }
    }
    public sealed class SocialUser
    {
        public string social_id; public string name; public string token_hash; public string last_seen_utc;
    }
    public sealed class TrustedServer
    {
        public string server_id; public string name; public string token_hash;
    }
    public sealed class ServerAddress
    {
        public string name; public string host; public int game_port; public int auth_port; public string kind = "official";
    }
    public sealed class Invitation
    {
        public string invite_id; public string from_id; public string to_id; public string from_name;
        public ServerAddress server; public string created_utc; public string expires_utc; public string state;
    }
    public sealed class SocialData
    {
        public int version = 1;
        public List<SocialUser> users = new List<SocialUser>();
        public List<TrustedServer> servers = new List<TrustedServer>();
        public List<string> friendships = new List<string>();
        public List<Invitation> invites = new List<Invitation>();
    }
    internal sealed class SessionTicket
    {
        internal string UserId; internal string ServerId; internal DateTime Expires;
    }
    public sealed class SocialRelay
    {
        private readonly object gate = new object();
        private readonly string path;
        private SocialData data;
        private string committed;
        private readonly Dictionary<string, SessionTicket> tickets = new Dictionary<string, SessionTicket>();
        private readonly Dictionary<string, DateTime> sessions = new Dictionary<string, DateTime>();
        private readonly Dictionary<string, Queue<DateTime>> rate = new Dictionary<string, Queue<DateTime>>();
        public SocialRelay(string file)
        {
            path = Path.GetFullPath(file);
            data = File.Exists(path) ? Json().Deserialize<SocialData>(File.ReadAllText(path)) : new SocialData();
            if (data == null || data.version != 1 || data.users == null || data.servers == null || data.friendships == null || data.invites == null)
                throw new InvalidDataException("Unsupported or corrupt social data; restore its backup instead of replacing it.");
            foreach (SocialUser user in data.users) user.last_seen_utc = DateTime.MinValue.ToString("o");
            committed = Json().Serialize(data);
        }
        public static JavaScriptSerializer Json() { return new JavaScriptSerializer { MaxJsonLength = 1024 * 1024, RecursionLimit = 32 }; }
        public object CreateServer(string name)
        {
            lock (gate)
            {
                string token = Secret();
                var server = new TrustedServer { server_id = Guid.NewGuid().ToString("N"), name = Name(name), token_hash = Hash(token) };
                data.servers.Add(server); Save();
                return new { server_id = server.server_id, server_token = token, name = server.name };
            }
        }
        public object Dispatch(string method, string route, string bearer, IDictionary<string, object> body, string origin)
        {
            lock (gate)
            {
                Cleanup();
                Limit("requests:" + origin, 600, 60);
                if (route == "/v1/health" && method == "GET") return new { service = "TavernNativeSocial", version = 1 };
                if (route == "/v1/register" && method == "POST")
                {
                    Limit("register:" + origin, 10, 60);
                    if (data.users.Count >= 10000) throw new ApiError(503, "This relay has reached its account limit.");
                    string token = Secret();
                    var user = new SocialUser { social_id = Guid.NewGuid().ToString("N"), name = Name(Text(body, "name")), token_hash = Hash(token), last_seen_utc = Utc() };
                    data.users.Add(user); Save();
                    return new { social_id = user.social_id, name = user.name, token = token };
                }
                if (route.StartsWith("/v1/server/", StringComparison.Ordinal)) return ServerRequest(method, route, bearer, body);
                SocialUser me = AuthorizeUser(bearer);
                Limit("user:" + me.social_id, 180, 60);
                if (route == "/v1/me" && method == "GET") return new { social_id = me.social_id, name = me.name };
                if (route == "/v1/presence" && method == "POST")
                {
                    if (body.ContainsKey("name")) me.name = Name(Text(body, "name"));
                    me.last_seen_utc = Utc();
                    return new { status = "online" };
                }
                if (route == "/v1/friends" && method == "GET")
                {
                    var friends = data.users.Where(x => x.social_id != me.social_id && IsFriend(me.social_id, x.social_id))
                        .OrderBy(x => x.name).Select(x => new { social_id = x.social_id, name = x.name,
                            online = Date(x.last_seen_utc) > DateTime.UtcNow.AddSeconds(-60), last_seen_utc = x.last_seen_utc }).ToArray();
                    return new { friends = friends };
                }
                if (route.StartsWith("/v1/friends/", StringComparison.Ordinal) && method == "DELETE")
                {
                    string other = route.Substring("/v1/friends/".Length);
                    data.friendships.Remove(Pair(me.social_id, other));
                    data.invites.RemoveAll(x => (x.from_id == me.social_id && x.to_id == other) || (x.to_id == me.social_id && x.from_id == other));
                    Save(); return new { status = "removed" };
                }
                if (route == "/v1/session-ticket" && method == "POST")
                {
                    string serverId = Text(body, "server_id");
                    if (!data.servers.Any(x => x.server_id == serverId)) throw new ApiError(404, "This game server is not registered with this relay.");
                    Limit("ticket:" + me.social_id, 12, 60);
                    string ticket = Secret(); DateTime expiry = DateTime.UtcNow.AddSeconds(90);
                    tickets[Hash(ticket)] = new SessionTicket { UserId = me.social_id, ServerId = serverId, Expires = expiry };
                    return new { ticket = ticket, expires_utc = expiry.ToString("o") };
                }
                if (route == "/v1/invites" && method == "POST")
                {
                    string recipient = Text(body, "to_id");
                    if (!IsFriend(me.social_id, recipient)) throw new ApiError(403, "You can invite only an accepted friend.");
                    Limit("invite:" + me.social_id, 20, 60);
                    if (data.invites.Count(x => x.to_id == recipient && x.state == "pending") >= 100) throw new ApiError(429, "That friend's invitation inbox is full.");
                    IDictionary<string, object> address = Object(body, "server");
                    var invitation = new Invitation { invite_id = Guid.NewGuid().ToString("N"), from_id = me.social_id, to_id = recipient, from_name = me.name,
                        server = Address(address), created_utc = Utc(), expires_utc = DateTime.UtcNow.AddDays(7).ToString("o"), state = "pending" };
                    data.invites.Add(invitation); Save(); return new { invite_id = invitation.invite_id };
                }
                if (route == "/v1/invites" && method == "GET")
                    return new { invites = data.invites.Where(x => x.to_id == me.social_id && x.state == "pending" && IsFriend(x.from_id, me.social_id)).ToArray() };
                if (route.StartsWith("/v1/invites/", StringComparison.Ordinal) && method == "POST")
                {
                    string[] parts = route.Split('/');
                    if (parts.Length != 5 || (parts[4] != "accept" && parts[4] != "dismiss")) throw new ApiError(404, "Unknown invitation action.");
                    Invitation invitation = data.invites.FirstOrDefault(x => x.invite_id == parts[3] && x.to_id == me.social_id);
                    if (invitation == null || !IsFriend(invitation.from_id, me.social_id)) throw new ApiError(404, "Invitation not found or no longer available.");
                    if (invitation.state != "pending") throw new ApiError(409, "That invitation was already handled.");
                    invitation.state = parts[4] == "accept" ? "accepted" : "dismissed";
                    Save(); return parts[4] == "accept" ? (object)invitation : new { status = "dismissed" };
                }
                throw new ApiError(404, "Unknown social API route.");
            }
        }
        private object ServerRequest(string method, string route, string token, IDictionary<string, object> body)
        {
            TrustedServer server = data.servers.FirstOrDefault(x => MatchHash(token, x.token_hash));
            if (server == null) throw new ApiError(401, "Invalid server credential.");
            if (method != "POST") throw new ApiError(405, "This route requires POST.");
            if (route == "/v1/server/resolve-ticket")
            {
                string hash = Hash(Text(body, "ticket")); SessionTicket ticket;
                if (!tickets.TryGetValue(hash, out ticket) || ticket.ServerId != server.server_id || ticket.Expires <= DateTime.UtcNow)
                    throw new ApiError(401, "Invalid, expired, used, or wrong-server session ticket.");
                tickets.Remove(hash); sessions[server.server_id + ":" + ticket.UserId] = DateTime.UtcNow.AddMinutes(3);
                SocialUser user = data.users.Single(x => x.social_id == ticket.UserId);
                return new { social_id = user.social_id, name = user.name };
            }
            if (route == "/v1/server/session-heartbeat")
            {
                object value; if (!body.TryGetValue("ids", out value) || !(value is System.Collections.IEnumerable) || value is string)
                    throw new ApiError(400, "ids must be an array.");
                int count = 0; var expired = new List<string>();
                foreach (object raw in (System.Collections.IEnumerable)value)
                {
                    if (++count > 500) throw new ApiError(400, "Too many sessions.");
                    string id = Convert.ToString(raw, CultureInfo.InvariantCulture);
                    string key = server.server_id + ":" + id;
                    if (sessions.ContainsKey(key)) sessions[key] = DateTime.UtcNow.AddMinutes(3); else expired.Add(id);
                }
                return new { status = "ok", expired_ids = expired.ToArray() };
            }
            if (route == "/v1/server/friendships")
            {
                var pairs = data.friendships.Select(x => x.Split(':')).Where(x => x.Length == 2 && ActiveSession(server.server_id, x[0]) && ActiveSession(server.server_id, x[1]))
                    .Select(x => new { left_id = x[0], right_id = x[1] }).ToArray();
                return new { pairs = pairs };
            }
            if (route == "/v1/server/friendship")
            {
                string left = Text(body, "left_id"), right = Text(body, "right_id");
                if (left == right || !ActiveSession(server.server_id, left) || !ActiveSession(server.server_id, right))
                    throw new ApiError(403, "Both card participants need authenticated active sessions on this server.");
                string pair = Pair(left, right); bool existed = data.friendships.Contains(pair);
                if (!existed) { data.friendships.Add(pair); Save(); }
                return new { status = "friends", existing = existed };
            }
            if (route == "/v1/server/session-end")
            {
                sessions.Remove(server.server_id + ":" + Text(body, "social_id")); return new { status = "ok" };
            }
            throw new ApiError(404, "Unknown server route.");
        }
        private bool ActiveSession(string server, string user) { DateTime expires; return sessions.TryGetValue(server + ":" + user, out expires) && expires > DateTime.UtcNow; }
        private SocialUser AuthorizeUser(string token)
        {
            SocialUser user = data.users.FirstOrDefault(x => MatchHash(token, x.token_hash));
            if (user == null) throw new ApiError(401, "Invalid social credential. Restore the existing social profile to keep your friendships.");
            return user;
        }
        private bool IsFriend(string left, string right) { return left != right && data.friendships.Contains(Pair(left, right)); }
        private static string Pair(string left, string right) { return String.CompareOrdinal(left, right) < 0 ? left + ":" + right : right + ":" + left; }
        private void Cleanup()
        {
            DateTime now = DateTime.UtcNow;
            foreach (string key in tickets.Where(x => x.Value.Expires < now).Select(x => x.Key).ToArray()) tickets.Remove(key);
            foreach (string key in sessions.Where(x => x.Value < now).Select(x => x.Key).ToArray()) sessions.Remove(key);
            data.invites.RemoveAll(x => Date(x.expires_utc) < now);
            foreach (string key in rate.Where(x => x.Value.Count == 0 || x.Value.Last() < now.AddMinutes(-2)).Select(x => x.Key).ToArray()) rate.Remove(key);
        }
        private void Limit(string key, int max, int seconds)
        {
            Queue<DateTime> entries; if (!rate.TryGetValue(key, out entries)) rate[key] = entries = new Queue<DateTime>();
            DateTime now = DateTime.UtcNow; while (entries.Count > 0 && entries.Peek() < now.AddSeconds(-seconds)) entries.Dequeue();
            if (entries.Count >= max) throw new ApiError(429, "Too many requests; wait before trying again."); entries.Enqueue(now);
        }
        private void Save()
        {
            string temp = path + ".new-" + Guid.NewGuid().ToString("N");
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                string serialized = Json().Serialize(data);
                File.WriteAllText(temp, serialized, new UTF8Encoding(false));
                if (File.Exists(path)) File.Replace(temp, path, path + ".bak"); else File.Move(temp, path);
                committed = serialized;
            }
            catch
            {
                // Do not expose a friendship in memory after failing to commit it.
                data = Json().Deserialize<SocialData>(committed);
                throw;
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
        private static ServerAddress Address(IDictionary<string, object> body)
        {
            string host = Text(body, "host").Trim();
            IPAddress ip;
            if (host.Length == 0 || host.Length > 253 || (!IPAddress.TryParse(host, out ip) && Uri.CheckHostName(host) != UriHostNameType.Dns))
                throw new ApiError(400, "Invalid server host.");
            string kind = Text(body, "kind");
            if (kind.Length == 0) kind = "official";
            if (kind != "official" && kind != "headless") throw new ApiError(400, "Unknown server join mode.");
            return new ServerAddress { name = Name(Text(body, "name")), host = host, game_port = Port(body, "game_port", 1757), auth_port = Port(body, "auth_port", 1762), kind = kind };
        }
        private static int Port(IDictionary<string, object> body, string key, int fallback)
        {
            if (!body.ContainsKey(key)) return fallback; int value;
            if (!Int32.TryParse(Text(body, key), out value) || value < 1 || value > 65535) throw new ApiError(400, "Invalid server port."); return value;
        }
        public static IDictionary<string, object> Object(IDictionary<string, object> data, string key)
        {
            object value; if (!data.TryGetValue(key, out value) || !(value is IDictionary<string, object>)) throw new ApiError(400, key + " must be an object."); return (IDictionary<string, object>)value;
        }
        public static string Text(IDictionary<string, object> data, string key)
        {
            object value; return data.TryGetValue(key, out value) && value != null ? Convert.ToString(value, CultureInfo.InvariantCulture) : "";
        }
        private static string Name(string value)
        {
            value = (value ?? "").Trim(); if (value.Length == 0 || value.Length > 64 || value.Any(Char.IsControl)) throw new ApiError(400, "Names must contain 1–64 printable characters."); return value;
        }
        private static string Secret() { byte[] bytes = new byte[32]; using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(bytes); return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_'); }
        private static string Hash(string value) { using (var hash = SHA256.Create()) return Convert.ToBase64String(hash.ComputeHash(Encoding.UTF8.GetBytes(value ?? ""))); }
        private static bool MatchHash(string token, string expected)
        {
            if (String.IsNullOrEmpty(token) || token.Length > 128 || expected == null) return false;
            string actual = Hash(token); int diff = actual.Length ^ expected.Length;
            for (int i = 0; i < Math.Min(actual.Length, expected.Length); i++) diff |= actual[i] ^ expected[i]; return diff == 0;
        }
        private static DateTime Date(string value) { DateTime result; return DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out result) ? result.ToUniversalTime() : DateTime.MinValue; }
        private static string Utc() { return DateTime.UtcNow.ToString("o"); }
    }
    // Loopback-only HTTP listener. A public deployment must terminate HTTPS at a
    // reverse proxy on the same host; account/server credentials never use public HTTP.
    public sealed class RelayListener : IDisposable
    {
        private readonly SocialRelay relay; private readonly Action<string> log;
        private readonly SemaphoreSlim slots = new SemaphoreSlim(32, 32);
        private TcpListener listener; private volatile bool running;
        public int Port { get; private set; }
        public RelayListener(SocialRelay relay, Action<string> log) { this.relay = relay; this.log = log ?? delegate { }; }
        public void Start(int port)
        {
            if (running) throw new InvalidOperationException("The relay is already running.");
            listener = new TcpListener(IPAddress.Loopback, port); listener.Start(32);
            Port = ((IPEndPoint)listener.LocalEndpoint).Port; running = true;
            Task.Run((Action)AcceptLoop); log("Relay listening on 127.0.0.1:" + Port + ". Public clients need an HTTPS reverse proxy.");
        }
        private void AcceptLoop()
        {
            while (running)
            {
                TcpClient client;
                try { client = listener.AcceptTcpClient(); } catch { if (!running) return; continue; }
                if (!slots.Wait(0)) { client.Close(); continue; }
                Task.Run(delegate { try { Handle(client); } finally { client.Close(); slots.Release(); } });
            }
        }
        private void Handle(TcpClient client)
        {
            client.ReceiveTimeout = 10000; client.SendTimeout = 10000;
            NetworkStream stream = client.GetStream(); int status = 200; object response; string audit = "request";
            try
            {
                byte[] header = ReadHeader(stream); string[] lines = Encoding.ASCII.GetString(header).Split(new[] { "\r\n" }, StringSplitOptions.None);
                string[] request = lines[0].Split(' ');
                if (request.Length != 3 || request[2] != "HTTP/1.1" || request[1].Length > 2048 || request[1].Contains("?") || !request[1].StartsWith("/v1/", StringComparison.Ordinal)) throw new ApiError(400, "Malformed request.");
                string method = request[0], route = request[1], bearer = ""; int length = 0; bool gotLength = false, expectContinue = false;
                audit = method + " " + route.Split('/').Take(3).Aggregate((a, b) => a + "/" + b);
                foreach (string line in lines.Skip(1))
                {
                    if (line.Length == 0) continue; int colon = line.IndexOf(':'); if (colon < 1) throw new ApiError(400, "Malformed header.");
                    string key = line.Substring(0, colon).Trim(), value = line.Substring(colon + 1).Trim();
                    if (key.Equals("Transfer-Encoding", StringComparison.OrdinalIgnoreCase)) throw new ApiError(400, "Chunked requests are not accepted.");
                    if (key.Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
                    {
                        if (gotLength || !Int32.TryParse(value, out length) || length < 0 || length > 65536) throw new ApiError(413, "Invalid or oversized request body."); gotLength = true;
                    }
                    if (key.Equals("Authorization", StringComparison.OrdinalIgnoreCase))
                    {
                        if (bearer.Length != 0 || !value.StartsWith("Bearer ", StringComparison.Ordinal)) throw new ApiError(401, "Invalid Authorization header."); bearer = value.Substring(7);
                    }
                    if (key.Equals("Expect", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!value.Equals("100-continue", StringComparison.OrdinalIgnoreCase)) throw new ApiError(417, "Unsupported expectation.");
                        expectContinue = true;
                    }
                }
                if (method == "POST" && !gotLength) throw new ApiError(411, "A Content-Length header is required.");
                if (expectContinue) { byte[] interim = Encoding.ASCII.GetBytes("HTTP/1.1 100 Continue\r\n\r\n"); stream.Write(interim, 0, interim.Length); }
                byte[] bytes = new byte[length]; int offset = 0;
                while (offset < length) { int read = stream.Read(bytes, offset, length - offset); if (read == 0) throw new ApiError(400, "Incomplete body."); offset += read; }
                IDictionary<string, object> body = new Dictionary<string, object>();
                if (length > 0)
                {
                    try { body = SocialRelay.Json().DeserializeObject(new UTF8Encoding(false, true).GetString(bytes)) as IDictionary<string, object>; }
                    catch { throw new ApiError(400, "Invalid JSON."); }
                    if (body == null) throw new ApiError(400, "JSON body must be an object.");
                }
                response = relay.Dispatch(method, route, bearer, body, ((IPEndPoint)client.Client.RemoteEndPoint).Address.ToString());
            }
            catch (ApiError ex) { status = ex.Status; response = new { error = ex.Message }; }
            catch (IOException) { status = 408; response = new { error = "The request timed out or the connection closed." }; }
            catch { status = 500; response = new { error = "The relay could not complete the request." }; }
            try
            {
                byte[] body = Encoding.UTF8.GetBytes(SocialRelay.Json().Serialize(response));
                byte[] header = Encoding.ASCII.GetBytes("HTTP/1.1 " + status + " " + (status == 200 ? "OK" : "Error") + "\r\nContent-Type: application/json; charset=utf-8\r\nCache-Control: no-store\r\nConnection: close\r\nContent-Length: " + body.Length + "\r\n\r\n");
                stream.Write(header, 0, header.Length); stream.Write(body, 0, body.Length);
            }
            catch { }
            log(audit + " -> " + status);
        }
        private static byte[] ReadHeader(Stream stream)
        {
            using (var bytes = new MemoryStream())
            {
                int previous = 0;
                while (bytes.Length < 16384)
                {
                    int value = stream.ReadByte(); if (value < 0) throw new ApiError(400, "Incomplete HTTP header.");
                    if (value > 127 || value == 0) throw new ApiError(400, "Invalid HTTP header."); bytes.WriteByte((byte)value);
                    previous = ((previous << 8) | value); if (previous == 0x0d0a0d0a) return bytes.ToArray();
                }
                throw new ApiError(431, "HTTP headers are too large.");
            }
        }
        public void Dispose() { running = false; if (listener != null) listener.Stop(); }
    }
}
