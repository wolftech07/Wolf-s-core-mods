using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace TavernNativeMenu
{
    internal sealed class MeshContact
    {
        public string Key, Address, Name, State, Nonce, PairKind;
        public long Expires;
        public bool ExplicitBlock;
        [JsonIgnore] public DateTime LastSeen, LastSent;
    }
    internal sealed class MeshProfile
    {
        public int Version = 2;
        public string Savedata;
        public List<MeshContact> Contacts = new List<MeshContact>();
        public List<JObject> Invites = new List<JObject>(), Outbox = new List<JObject>();
        public List<JObject> Seen = new List<JObject>();
    }

    // A single worker owns toxcore, its callbacks, and persistent state. Unity
    // reads immutable snapshots and receives callbacks only through Tick.
    internal static class MeshPeerRuntime
    {
        private static readonly object Gate = new object();
        private sealed class Operation { internal Action Run; internal Action<Exception> Fail; }
        private sealed class Verification
        {
            internal string Key, Nonce;
            internal long Expires;
            internal DateTime LastSent;
            internal bool Confirmed;
            internal volatile bool Cancelled;
        }
        private static readonly Dictionary<string, Verification> Verifications = new Dictionary<string, Verification>();
        private static readonly Queue<Operation> Work = new Queue<Operation>();
        private static readonly Queue<Action> Main = new Queue<Action>();
        private static readonly AutoResetEvent Wake = new AutoResetEvent(false);
        private static readonly DateTime Epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        private static Thread worker;
        private static volatile bool stopping, ready, connected;
        private static MeshNative core;
        private static MeshProfile profile;
        private static string path, username, address = "", lastError;
        private static JArray friends = new JArray(), requests = new JArray(), invites = new JArray(), blocked = new JArray();
        private static DateTime lastPresence, lastBootstrap, nextPublish;
        private static JArray nodes;
        private static long rateWindow;
        private static readonly Dictionary<string, int> messageRates = new Dictionary<string, int>();
        internal static event Action Changed;
        internal static event Action<string, string> PairVerified;
        internal static event Action<string, string> PeerVerified;
        internal static string NetworkId { get { return "tavern-peer-v2"; } }
        internal static string Address { get { lock (Gate) return address; } }
        internal static string SocialId { get { string value = Address; return value.Length >= 64 ? value.Substring(0, 64) : ""; } }
        internal static bool Configured { get { return ready; } }
        internal static bool Connected { get { return connected; } }
        internal static string LastError { get { lock (Gate) return lastError; } }
        internal static long Now { get { return (long)(DateTime.UtcNow - Epoch).TotalSeconds; } }

        internal static void Initialize(string gamePath, string playerName)
        {
            if (worker != null) return;
            path = Path.Combine(gamePath, "UserData", "TavernMesh.json");
            username = Clean(playerName, 32);
            string native = Path.Combine(gamePath, "TavernNativeMenu", "native");
            worker = new Thread(delegate() { Run(native); }) { IsBackground = true, Name = "Tavern peer friends" };
            worker.Start();

        }
        private static void Run(string native)
        {
            Exception failure = null;
            FileStream identityLock = null;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                identityLock = new FileStream(path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                if (File.Exists(path))
                {
                    if (new FileInfo(path).Length > 8 * 1024 * 1024) throw new InvalidDataException("The peer identity file is too large.");
                    profile = JsonConvert.DeserializeObject<MeshProfile>(File.ReadAllText(path));
                    if (profile == null || profile.Version != 2 || String.IsNullOrEmpty(profile.Savedata) || profile.Contacts == null ||
                        profile.Invites == null || profile.Outbox == null || profile.Seen == null || profile.Contacts.Count > 500 ||
                        profile.Invites.Count > 100 || profile.Outbox.Count > 100 || profile.Seen.Count > 4096)
                        throw new InvalidDataException("The saved peer identity is invalid. Restore TavernMesh.json from its backup; it has not been replaced.");
                    foreach (MeshContact contact in profile.Contacts)
                    {
                        if (contact == null) throw new InvalidDataException("Invalid saved contact.");
                        MeshNative.Unhex(contact.Key, 32);
                        contact.Key = contact.Key.ToUpperInvariant();
                        if (contact.Address != null && MeshNative.Hex(MeshNative.ValidateAddress(contact.Address)).Substring(0, 64) != contact.Key) throw new InvalidDataException("Saved contact address does not match its identity.");
                        if (!new[] { "accepted", "incoming", "outgoing", "accepting", "pairing", "blocked" }.Contains(contact.State)) throw new InvalidDataException("Invalid saved friendship state.");
                        if (contact.ExplicitBlock && contact.State != "blocked") throw new InvalidDataException("Invalid saved blocked contact state.");
                        contact.Name = Clean(contact.Name, 32);
                        if (contact.State != "blocked" && (contact.PairKind != "code" && contact.PairKind != "card" || contact.Nonce == null || contact.Nonce.Length != (contact.PairKind == "code" ? 32 : 43))) throw new InvalidDataException("Invalid saved friendship confirmation.");
                    }
                    if (profile.Contacts.Select(x => x.Key).Distinct().Count() != profile.Contacts.Count) throw new InvalidDataException("Duplicate saved peer identities.");
                    foreach (JObject item in profile.Invites.Concat(profile.Outbox))
                    {
                        Guid id;
                        if (item == null || !Guid.TryParseExact((string)item["invite_id"], "N", out id) || item["expires"] == null) throw new InvalidDataException("Invalid saved invitation.");
                        long expiry = (long)item["expires"];
                        MeshNative.Unhex((string)(item["from_id"] ?? item["to_id"]), 32);
                        SanitizeServer(item["server"] as JObject);
                    }
                    foreach (JObject seen in profile.Seen)
                    {
                        Guid id;
                        if (seen == null || !Guid.TryParseExact((string)seen["id"], "N", out id) || seen["expires"] == null) throw new InvalidDataException("Invalid saved invitation receipt.");
                        long expiry = (long)seen["expires"];
                        MeshNative.Unhex((string)seen["key"], 32);
                    }
                }
                else profile = new MeshProfile();
                Publish();
                core = new MeshNative(native, profile.Savedata == null ? null : Convert.FromBase64String(profile.Savedata), username);
                lock (Gate) address = core.Address;
                if (profile.Contacts.Any(x => x.Key == SocialId)) throw new InvalidDataException("The saved profile contains its own identity as a contact.");
                core.Request = delegate(string key, string value) { ReceiveSafely(key, value, true); };
                core.Message = delegate(string key, string value) { ReceiveSafely(key, value, false); };
                // Verification sessions are transient. Saved tox state can
                // contain their native connections after a crash; never revive
                // those connections as contacts on a later game session.
                foreach (string key in core.FriendKeys()) if (!NeedsNative(Find(key))) core.Delete(key);
                nodes = JArray.Parse(File.ReadAllText(Path.Combine(native, "bootstrap-nodes.json")));
                Save(); ready = true;
                File.WriteAllText(Path.Combine(Path.GetDirectoryName(path), "TavernFriendCode.txt"), "Tavern friend code (safe to share with the person you want to add):\r\n" + address + "\r\n", new UTF8Encoding(false));
                Publish(); Signal();
                while (!stopping)
                {
                    for (int i = 0; i < 32; i++)
                    {
                        Operation action;
                        lock (Gate) { if (Work.Count == 0) break; action = Work.Dequeue(); }
                        action.Run();
                    }
                    if (DateTime.UtcNow - lastBootstrap > TimeSpan.FromMinutes(core.Connected ? 10 : 1)) Bootstrap();
                    core.Iterate();
                    bool wasConnected = connected;
                    connected = core.Connected;
                    if (wasConnected != connected) Signal();
                    PumpPeers();
                    if (DateTime.UtcNow >= nextPublish || wasConnected != connected) { Publish(); nextPublish = DateTime.UtcNow.AddSeconds(2); }
                    Wake.WaitOne(25);
                }
                Save();
            }
            catch (Exception error) { failure = error; SetError(new InvalidOperationException("Peer friends could not start or save: " + error.Message)); }
            finally
            {
                ready = false; connected = false;
                lock (Gate)
                {
                    foreach (Operation pending in Work) pending.Fail(failure ?? new OperationCanceledException("The friends network closed."));
                    Work.Clear();
                    foreach (JObject friend in friends.OfType<JObject>()) friend["online"] = false;
                }
                if (core != null) core.Dispose(); core = null; Signal();
                if (identityLock != null) identityLock.Dispose();
            }
        }
        private static void Bootstrap()
        {
            lastBootstrap = DateTime.UtcNow;
            foreach (JObject node in nodes.Take(12).OfType<JObject>())
            {
                try
                {
                    string host = (string)node["host"], key = (string)node["public_key"];
                    int port = (int)node["port"];
                    if (Uri.CheckHostName(host) == UriHostNameType.Unknown || port < 1 || port > 65535) continue;
                    ushort[] tcp = ((JArray)node["tcp_ports"]).Values<int>().Where(x => x > 0 && x < 65536).Take(3).Select(x => (ushort)x).ToArray();
                    core.Bootstrap(host, (ushort)port, key, tcp);
                }
                catch (Exception error) { if (error is IOException) throw; }
            }
        }
        internal static void Tick()
        {
            for (int i = 0; i < 64; i++)
            {
                Action action;
                lock (Gate) { if (Main.Count == 0) break; action = Main.Dequeue(); }
                try { action(); } catch (Exception e) { SetError(e); }
            }
        }
        internal static void Shutdown() { stopping = true; Wake.Set(); if (worker != null && worker.IsAlive) worker.Join(2000); }
        internal static void Post(Action action) { lock (Gate) { if (Main.Count < 256) Main.Enqueue(action); } }
        internal static void SetError(Exception error) { lock (Gate) lastError = error.Message; Signal(); }
        private static void Signal() { Post(delegate { Action handler = Changed; if (handler != null) handler(); }); }
        internal static void RequestRefresh() { Signal(); }
        private static Task<T> Command<T>(Func<T> action)
        {
            var result = new TaskCompletionSource<T>();
            lock (Gate)
            {
                if (!ready || stopping) { result.SetException(new InvalidOperationException(LastError ?? "The peer network is still starting.")); return result.Task; }
                if (Work.Count >= 100) { result.SetException(new InvalidOperationException("The friends network is busy. Try again shortly.")); return result.Task; }
                Work.Enqueue(new Operation {
                    Run = delegate
                    {
                        try { T value = action(); Save(); Publish(); result.TrySetResult(value); }
                        catch (Exception error) { result.TrySetException(error); if (error is IOException && !(error is InvalidDataException) || error is UnauthorizedAccessException) throw; }
                    },
                    Fail = delegate(Exception error) { result.TrySetException(error); }
                });
            }
            Wake.Set(); return result.Task;
        }
        internal static Task<JArray> GetFriendsAsync() { lock (Gate) return Task.FromResult((JArray)friends.DeepClone()); }
        internal static Task<JArray> GetInvitesAsync() { lock (Gate) return Task.FromResult((JArray)invites.DeepClone()); }
        internal static Task<JArray> GetRequestsAsync() { lock (Gate) return Task.FromResult((JArray)requests.DeepClone()); }
        internal static Task<JArray> GetBlockedAsync() { lock (Gate) return Task.FromResult((JArray)blocked.DeepClone()); }
        private static MeshContact Find(string key) { return profile.Contacts.FirstOrDefault(x => x.Key == key); }
        private static bool NeedsNative(MeshContact contact) { return contact != null && !contact.ExplicitBlock && contact.State != "blocked" && contact.State != "incoming"; }
        private static MeshContact AddContact(string key)
        {
            MeshContact contact = Find(key);
            if (contact != null) return contact;
            if (profile.Contacts.Count >= 500) throw new InvalidOperationException("The saved contact limit has been reached.");
            contact = new MeshContact { Key = key, Name = "Friend", State = "incoming" }; profile.Contacts.Add(contact); return contact;
        }
        private static string Clean(string value, int length)
        {
            string text = new string((value ?? "Friend").Where(c => !Char.IsControl(c)).ToArray()).Trim();
            if (text.Length == 0) text = "Friend";
            return text.Length > length ? text.Substring(0, length) : text;
        }
        private static JObject Packet(string kind) { return new JObject { { "v", 2 }, { "kind", kind }, { "name", username } }; }
        internal static Task RequestFriendAsync(string code)
        {
            return Command(delegate
            {
                string normalized = MeshNative.Hex(MeshNative.ValidateAddress(code));
                string key = normalized.Substring(0, 64);
                if (key == SocialId) throw new ArgumentException("That is your own friend code.");
                MeshContact contact = Find(key);
                if (contact != null && contact.ExplicitBlock) throw new InvalidOperationException("Unblock this player before sending a friend request.");
                if (contact != null && (contact.State == "accepted" || contact.State == "outgoing" || contact.State == "accepting")) return 0;
                if (contact != null && contact.State == "incoming") { core.AddWithoutRequest(key); contact.State = "accepting"; contact.LastSent = DateTime.MinValue; return 0; }
                if (contact == null && profile.Contacts.Count >= 500) throw new InvalidOperationException("The saved contact limit has been reached.");
                string nonce = Guid.NewGuid().ToString("N");
                JObject request = Packet("request"); request["nonce"] = nonce; request["address"] = address;
                core.Add(normalized, request.ToString(Formatting.None));
                // Identity verification may already have connected this native
                // peer without a social contact. Tox suppresses another native
                // friend-request callback for that connection, so also carry
                // the explicit request over its authenticated message channel.
                if (core.Online(key)) core.Send(key, request.ToString(Formatting.None));
                contact = AddContact(key);
                contact.State = "outgoing"; contact.Address = normalized; contact.Nonce = nonce; contact.PairKind = "code";
                return 0;
            });
        }
        private static JObject Parse(string text)
        {
            if (text == null || text.Length > 1372) return null;
            try { using (var reader = new JsonTextReader(new StringReader(text)) { MaxDepth = 6 }) { var value = JObject.Load(reader); if (reader.Read() || (int?)value["v"] != 2) return null; return value; } }
            catch (Exception e) { if (e is IOException) throw; return null; }
        }
        private static void ReceiveRequest(string key, string text)
        {
            JObject request = Parse(text); if (request == null || (string)request["kind"] != "request") return;
            string nonce = (string)request["nonce"], code = (string)request["address"];
            Guid requestId;
            if (!Guid.TryParseExact(nonce, "N", out requestId) || code == null || MeshNative.Hex(MeshNative.ValidateAddress(code)).Substring(0, 64) != key) return;
            MeshContact contact = Find(key);
            if (contact != null && contact.State == "accepted" && !contact.ExplicitBlock)
            {
                // This side still consents to the existing friendship. The
                // authenticated peer may have removed/unblocked us and issued
                // a fresh explicit request with a new nonce. Confirm that
                // request without changing our accepted contact or its nonce.
                JObject response = Packet("accept"); response["nonce"] = nonce;
                core.Send(key, response.ToString(Formatting.None));
                return;
            }
            if (contact != null && contact.State != "incoming") return;
            if (contact == null && (profile.Contacts.Count >= 500 || profile.Contacts.Count(x => x.State == "incoming") >= 100)) return;
            contact = AddContact(key);
            contact.Address = code; contact.Name = Clean((string)request["name"], 32); contact.Nonce = nonce; contact.PairKind = "code";
            Save(); Publish();
        }
        internal static Task AcceptRequestAsync(string key)
        {
            return Command(delegate { MeshContact contact = Find(key); if (contact == null || contact.ExplicitBlock || contact.State != "incoming") throw new InvalidOperationException("That request is no longer available."); core.AddWithoutRequest(key); contact.State = "accepting"; contact.LastSent = DateTime.MinValue; return 0; });
        }
        internal static Task RemoveFriendAsync(string key)
        {
            return Command(delegate
            {
                MeshContact contact = Find(key); if (contact == null) return 0;
                core.Delete(key);
                RemoveVerifications(key);
                contact.State = "blocked"; contact.Nonce = null; contact.PairKind = null;
                profile.Invites.RemoveAll(x => (string)x["from_id"] == key);
                profile.Outbox.RemoveAll(x => (string)x["to_id"] == key);
                return 0;
            });
        }
        internal static Task BlockPeerAsync(string code, string name)
        {
            return Command(delegate
            {
                string normalized = (code ?? "").Trim().ToUpperInvariant();
                string key;
                MeshContact contact;
                if (normalized.Length == 64)
                {
                    MeshNative.Unhex(normalized, 32); key = normalized; contact = Find(key);
                    if (contact == null) throw new ArgumentException("Enter the full friend code when blocking a player who is not a saved contact.");
                }
                else
                {
                    normalized = MeshNative.Hex(MeshNative.ValidateAddress(normalized)); key = normalized.Substring(0, 64); contact = Find(key);
                }
                if (key == SocialId) throw new ArgumentException("You cannot block your own friend code.");
                if (contact == null) contact = AddContact(key);
                core.Delete(key); RemoveVerifications(key);
                if (normalized.Length == 76) contact.Address = normalized;
                if (!String.IsNullOrWhiteSpace(name)) contact.Name = Clean(name, 32);
                contact.ExplicitBlock = true; contact.State = "blocked"; contact.Nonce = null; contact.PairKind = null; contact.Expires = 0;
                profile.Invites.RemoveAll(x => (string)x["from_id"] == key);
                profile.Outbox.RemoveAll(x => (string)x["to_id"] == key);
                return 0;
            });
        }
        internal static Task UnblockPeerAsync(string key)
        {
            return Command(delegate
            {
                string normalized = MeshNative.Hex(MeshNative.Unhex(key, 32));
                MeshContact contact = Find(normalized);
                // Removing the blocked tombstone permits a fresh incoming
                // request. No native friend or accepted contact is restored.
                if (contact != null && contact.State == "blocked") profile.Contacts.Remove(contact);
                return 0;
            });
        }
        private static void RemoveVerifications(string key)
        {
            foreach (string id in Verifications.Where(x => x.Value.Key == key).Select(x => x.Key).ToArray()) { Verifications[id].Cancelled = true; Verifications.Remove(id); }
        }
        internal static Task VerifyPeerAsync(string code, string nonce, long expires)
        {
            return Command(delegate
            {
                string normalized = MeshNative.Hex(MeshNative.ValidateAddress(code));
                string key = normalized.Substring(0, 64);
                if (key == SocialId || nonce == null || nonce.Length != 43 || nonce.Any(c => !Char.IsLetterOrDigit(c) && c != '-' && c != '_') || expires <= Now || expires > Now + 125)
                    throw new InvalidDataException("Invalid or expired peer verification.");
                MeshContact contact = Find(key);
                if (contact != null && contact.ExplicitBlock) throw new InvalidOperationException("That player is blocked.");
                string id = key + ":" + nonce;
                Verification existing;
                if (Verifications.TryGetValue(id, out existing)) return 0;
                if (Verifications.Count >= 64) throw new InvalidOperationException("Too many peer verifications are pending. Try again shortly.");
                core.AddWithoutRequest(key);
                Verifications[id] = new Verification { Key = key, Nonce = nonce, Expires = expires, LastSent = DateTime.MinValue };
                return 0;
            });
        }
        internal static Task PairCardAsync(string code, string name, string nonce, long expires)
        {
            return Command(delegate
            {
                string normalized = MeshNative.Hex(MeshNative.ValidateAddress(code));
                string key = normalized.Substring(0, 64);
                if (key == SocialId || nonce == null || nonce.Length != 43 || expires <= Now || expires > Now + 125) throw new InvalidDataException("Invalid or expired friend-card pairing.");
                if (Find(key) != null && Find(key).ExplicitBlock) throw new InvalidOperationException("Unblock this player before exchanging friendship cards.");
                if (Find(key) == null && profile.Contacts.Count >= 500) throw new InvalidOperationException("The saved contact limit has been reached.");
                core.AddWithoutRequest(key);
                MeshContact contact = AddContact(key);
                // A new physical exchange supplies new explicit consent even if previously removed.
                if (contact.State != "accepted") contact.State = "pairing";
                contact.Address = normalized; contact.Name = Clean(name, 32); contact.Nonce = nonce; contact.Expires = expires; contact.PairKind = "card"; contact.LastSent = DateTime.MinValue;
                return 0;
            });
        }
        private static void AcceptContact(MeshContact contact)
        {
            contact.State = "accepted"; contact.LastSeen = DateTime.UtcNow;
            Save(); Publish();
        }
        private static void ReceiveMessage(string key, string text)
        {
            JObject message = Parse(text); if (message == null) return;
            MeshContact contact = Find(key); if (contact != null && contact.ExplicitBlock) return;
            string kind = (string)message["kind"], nonce = (string)message["nonce"];
            if (kind == "request") { ReceiveRequest(key, text); return; }
            if (kind == "verify" || kind == "verify_ack")
            {
                Verification verification;
                if (nonce == null || !Verifications.TryGetValue(key + ":" + nonce, out verification) || verification.Expires <= Now) return;
                if (kind == "verify") { JObject ack = Packet("verify_ack"); ack["nonce"] = nonce; core.Send(key, ack.ToString(Formatting.None)); }
                if (!verification.Confirmed)
                {
                    verification.Confirmed = true;
                    Post(delegate {
                        // A block can be queued after receipt but before Tick.
                        if (verification.Cancelled || verification.Expires <= Now || GetBlockedAsync().Result.OfType<JObject>().Any(x => (string)x["social_id"] == key)) return;
                        Action<string, string> verified = PeerVerified; if (verified != null) verified(key, nonce);
                    });
                }
                return;
            }
            if (contact == null || contact.State == "blocked") return;
            // If both players add one another's codes at once, toxcore may
            // connect without a request callback. Their two outgoing intents
            // still supply mutual consent; select one confirmation deterministically.
            if (kind == "offer" && contact.State == "outgoing" && contact.PairKind == "code")
            {
                Guid parsed;
                if (!Guid.TryParseExact(nonce, "N", out parsed)) return;
                if (String.CompareOrdinal(SocialId, key) > 0) contact.Nonce = nonce;
                // The first offer can arrive before our periodic offer was
                // sent. Return the selected nonce now, or the other outgoing
                // peer may never learn which confirmation we are accepting.
                JObject selected = Packet("offer"); selected["nonce"] = contact.Nonce;
                core.Send(key, selected.ToString(Formatting.None));
                contact.State = "accepting"; contact.LastSent = DateTime.MinValue; Save(); return;
            }
            if ((kind == "card" || kind == "card_ack") && contact.PairKind == "card" && contact.Nonce == nonce && contact.Expires > Now)
            {
                bool newlyAccepted = contact.State != "accepted";
                if (newlyAccepted) AcceptContact(contact);
                if (kind == "card") { JObject ack = Packet("card_ack"); ack["nonce"] = nonce; core.Send(key, ack.ToString(Formatting.None)); }
                Post(delegate { Action<string, string> paired = PairVerified; if (paired != null) paired(key, nonce); });
                return;
            }
            if (kind == "accept" && contact.PairKind == "code" && contact.Nonce == nonce && (contact.State == "outgoing" || contact.State == "accepting" || contact.State == "accepted"))
            {
                contact.Name = Clean((string)message["name"], 32);
                if (contact.State != "accepted") AcceptContact(contact);
                JObject ack = Packet("accept_ack"); ack["nonce"] = nonce; core.Send(key, ack.ToString(Formatting.None)); return;
            }
            if (kind == "accept_ack" && contact.State == "accepting" && contact.Nonce == nonce) { AcceptContact(contact); return; }
            if (contact.State != "accepted") return;
            if (kind == "presence")
            {
                contact.LastSeen = DateTime.UtcNow;
                string name = Clean((string)message["name"], 32);
                if (contact.Name != name) { contact.Name = name; Save(); }
                return;
            }
            if (kind == "invite")
            {
                string id = (string)message["invite_id"];
                if ((string)message["to_id"] != SocialId) return;
                Guid parsed;
                if (!Guid.TryParseExact(id, "N", out parsed)) return;
                long expires = (long?)message["expires"] ?? 0;
                if (expires <= Now || expires > Now + 8 * 86400) return;
                JObject server = SanitizeServer(message["server"] as JObject);
                if (!profile.Seen.Any(x => (string)x["id"] == id && (string)x["key"] == key))
                {
                    if (profile.Invites.Count >= 100 || profile.Seen.Count >= 4096) return;
                    profile.Invites.Add(new JObject { { "invite_id", id }, { "from_id", key }, { "from_name", contact.Name }, { "server", server }, { "expires", expires } });
                    profile.Seen.Add(new JObject { { "id", id }, { "key", key }, { "expires", expires } });
                    Save(); Publish();
                }
                JObject ack = Packet("invite_ack"); ack["invite_id"] = id; core.Send(key, ack.ToString(Formatting.None));
            }
            else if (kind == "invite_ack")
            {
                string id = (string)message["invite_id"];
                if (profile.Outbox.RemoveAll(x => (string)x["invite_id"] == id && (string)x["to_id"] == key) > 0) Save();
            }
        }
        private static JObject SanitizeServer(JObject value)
        {
            if (value == null) throw new InvalidDataException("The invitation has no server details.");
            var server = new ServerEntry { Host = (string)value["host"], GamePort = (int?)value["game_port"] ?? 0, AuthPort = (int?)value["auth_port"] ?? 0 };
            if (!ServerCatalog.ValidEntry(server)) throw new InvalidDataException("The invitation has an invalid address.");
            string kind = (string)value["kind"];
            if (kind != "official" && kind != "headless") throw new InvalidDataException("Unknown server type in invitation.");
            return new JObject { { "host", server.Host.Trim() }, { "game_port", server.GamePort }, { "auth_port", server.AuthPort }, { "kind", kind }, { "name", Clean((string)value["name"], 64) } };
        }
        internal static Task SendInviteAsync(string key, ServerEntry server)
        {
            return Command(delegate
            {
                MeshContact contact = Find(key); if (contact == null || contact.ExplicitBlock || contact.State != "accepted") throw new InvalidOperationException("Add and accept this friend before inviting them.");
                if (profile.Outbox.Count >= 100) throw new InvalidOperationException("Too many undelivered invitations. Wait for your friends to reconnect.");
                JObject destination = SanitizeServer(new JObject { { "host", server.Host }, { "game_port", server.GamePort }, { "auth_port", server.AuthPort }, { "kind", server.Kind }, { "name", server.Name } });
                JObject message = Packet("invite"); message["to_id"] = key; message["invite_id"] = Guid.NewGuid().ToString("N"); message["expires"] = Now + 7 * 86400; message["server"] = destination;
                if (Encoding.UTF8.GetByteCount(message.ToString(Formatting.None)) > 1372) throw new InvalidDataException("The server name or address is too long for an invitation.");
                profile.Outbox.Add(message); contact.LastSent = DateTime.MinValue; return 0;
            });
        }
        internal static Task<ServerEntry> AcceptInviteAsync(string id)
        {
            return Command(delegate
            {
                JObject invite = profile.Invites.FirstOrDefault(x => (string)x["invite_id"] == id && (long)x["expires"] > Now);
                if (invite == null) throw new InvalidOperationException("That invitation expired or was removed.");
                MeshContact from = Find((string)invite["from_id"]);
                if (from == null || from.State != "accepted") throw new InvalidOperationException("That player is no longer your friend.");
                JObject server = SanitizeServer((JObject)invite["server"]);
                profile.Invites.Remove(invite);
                return new ServerEntry { Name = (string)server["name"], Host = (string)server["host"], GamePort = (int)server["game_port"], AuthPort = (int)server["auth_port"], Kind = (string)server["kind"], Private = true, Favorite = true };
            });
        }
        internal static Task DismissInviteAsync(string id) { return Command(delegate { profile.Invites.RemoveAll(x => (string)x["invite_id"] == id); return 0; }); }
        private static void PumpPeers()
        {
            DateTime now = DateTime.UtcNow;
            bool presence = (now - lastPresence).TotalSeconds >= 20;
            if (presence) lastPresence = now;
            bool dirty = profile.Invites.RemoveAll(x => (long)x["expires"] <= Now) + profile.Outbox.RemoveAll(x => (long)x["expires"] <= Now) + profile.Seen.RemoveAll(x => (long)x["expires"] <= Now) > 0;
            foreach (var entry in Verifications.ToArray())
            {
                Verification verification = entry.Value;
                if (verification.Expires <= Now)
                {
                    verification.Cancelled = true;
                    Verifications.Remove(entry.Key);
                    if (!NeedsNative(Find(verification.Key)) && !Verifications.Values.Any(x => x.Key == verification.Key)) core.Delete(verification.Key);
                    dirty = true;
                }
                else if ((now - verification.LastSent).TotalSeconds >= 3 && core.Online(verification.Key))
                {
                    verification.LastSent = now;
                    JObject packet = Packet("verify"); packet["nonce"] = verification.Nonce; core.Send(verification.Key, packet.ToString(Formatting.None));
                }
            }
            foreach (MeshContact contact in profile.Contacts.ToArray())
            {
                if (contact.State == "pairing" && contact.Expires <= Now) { contact.State = "blocked"; contact.Nonce = null; core.Delete(contact.Key); dirty = true; continue; }
                if (contact.State == "blocked" || !core.Online(contact.Key)) continue;
                if ((now - contact.LastSent).TotalSeconds >= 5)
                {
                    contact.LastSent = now;
                    string kind = contact.PairKind == "card" && contact.Expires > Now ? "card" : contact.State == "accepting" ? "accept" : contact.State == "outgoing" ? "offer" : null;
                    if (kind != null) { JObject packet = Packet(kind); packet["nonce"] = contact.Nonce; core.Send(contact.Key, packet.ToString(Formatting.None)); }
                    if (contact.State == "outgoing" && contact.PairKind == "code")
                    {
                        JObject request = Packet("request"); request["nonce"] = contact.Nonce; request["address"] = address;
                        core.Send(contact.Key, request.ToString(Formatting.None));
                    }
                    if (contact.State == "accepted") foreach (JObject invite in profile.Outbox.Where(x => (string)x["to_id"] == contact.Key).Take(5)) core.Send(contact.Key, invite.ToString(Formatting.None));
                }
                if (presence && contact.State == "accepted") core.Send(contact.Key, Packet("presence").ToString(Formatting.None));
            }
            if (dirty) Save();
        }
        private static void Publish()
        {
            var f = new JArray(profile.Contacts.Where(x => x.State == "accepted" && !x.ExplicitBlock).Select(x => new JObject { { "social_id", x.Key }, { "name", x.Name }, { "online", ready && core != null && core.Online(x.Key) && DateTime.UtcNow - x.LastSeen < TimeSpan.FromSeconds(75) } }));
            var r = new JArray(profile.Contacts.Where(x => x.State == "incoming" && !x.ExplicitBlock).Select(x => new JObject { { "social_id", x.Key }, { "name", x.Name } }));
            var b = new JArray(profile.Contacts.Where(x => x.ExplicitBlock).Select(x => new JObject { { "social_id", x.Key }, { "name", x.Name }, { "address", x.Address } }));
            var i = new JArray(profile.Invites.Select(x => x.DeepClone()));
            bool changed;
            lock (Gate) { changed = !JToken.DeepEquals(friends, f) || !JToken.DeepEquals(requests, r) || !JToken.DeepEquals(invites, i) || !JToken.DeepEquals(blocked, b); friends = f; requests = r; invites = i; blocked = b; }
            if (changed) Signal();
        }
        private static void Save()
        {
            profile.Savedata = Convert.ToBase64String(core.Save());
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporary, JsonConvert.SerializeObject(profile, Formatting.Indented), new UTF8Encoding(false));
                if (File.Exists(path)) File.Replace(temporary, path, path + ".bak"); else File.Move(temporary, path);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        private static void ReceiveSafely(string key, string value, bool request)
        {
            if (Now / 10 != rateWindow) { rateWindow = Now / 10; messageRates.Clear(); }
            int count; messageRates.TryGetValue(key, out count);
            if (count >= 40 || messageRates.Count >= 600 && count == 0) return;
            messageRates[key] = count + 1;
            try { if (request) ReceiveRequest(key, value); else ReceiveMessage(key, value); }
            catch (JsonException) { }
            catch (InvalidCastException) { }
            catch (OverflowException) { }
            // Storage errors are intentionally not caught here.
        }
    }
}
