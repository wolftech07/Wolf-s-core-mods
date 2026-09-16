using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace TavernNativeMenu
{
    // These IDs belong to the configured relay. They are never derived from a
    // server's local Tavern user ID or the player's editable display name.
    internal static class TavernSocialClient
    {
        private sealed class Identity
        {
            [JsonProperty("social_id")] public string SocialId;
            [JsonProperty("token")] public string Token;
            [JsonProperty("name")] public string Name;
        }
        private sealed class Settings
        {
            [JsonProperty("relay_url")] public string RelayUrl = "";
            [JsonProperty("identities")] public Dictionary<string, Identity> Identities = new Dictionary<string, Identity>(StringComparer.Ordinal);
        }
        private sealed class Session
        {
            internal string Url, Token, Id;
            internal int Generation;
        }
        private static readonly object Gate = new object();
        private static readonly Queue<Action> MainThread = new Queue<Action>();
        private static readonly SemaphoreSlim ConfigureGate = new SemaphoreSlim(1, 1);
        private static Settings settings = new Settings();
        private static string path, username, lastError, settingsError;
        private static JArray friends = new JArray(), invites = new JArray();
        private static DateTime nextPoll = DateTime.MinValue;
        private static volatile bool polling;
        private static bool startPending, initialized;
        private static int generation;
        internal static event Action Changed;
        internal static string RelayUrl { get { lock (Gate) return settings.RelayUrl; } }
        internal static string SocialId { get { lock (Gate) { Identity identity; return settings.Identities.TryGetValue(settings.RelayUrl, out identity) ? identity.SocialId : ""; } } }
        internal static bool Configured { get { lock (Gate) { Identity identity; return settings.RelayUrl.Length > 0 && settings.Identities.TryGetValue(settings.RelayUrl, out identity) && ValidIdentity(identity); } } }
        internal static string LastError { get { lock (Gate) return lastError; } }

        internal static void Initialize(string gamePath, string playerName)
        {
            if (initialized) return;
            initialized = true;
            path = Path.Combine(Path.GetFullPath(gamePath), "UserData", "TavernSocial.json");
            username = (playerName ?? "Player").Trim();
            if (username.Length == 0) username = "Player";
            if (username.Length > 32) username = username.Substring(0, 32);
            try
            {
                if (File.Exists(path))
                {
                    if (new FileInfo(path).Length > 1024 * 1024) throw new InvalidDataException("The friends settings file is too large.");
                    var loaded = JsonConvert.DeserializeObject<Settings>(File.ReadAllText(path));
                    if (loaded == null) throw new InvalidDataException("The friends settings file is empty.");
                    loaded.RelayUrl = String.IsNullOrWhiteSpace(loaded.RelayUrl) ? "" : NormalizeRelayUrl(loaded.RelayUrl);
                    if (loaded.Identities == null) loaded.Identities = new Dictionary<string, Identity>(StringComparer.Ordinal);
                    foreach (var identity in loaded.Identities)
                        if (NormalizeRelayUrl(identity.Key) != identity.Key || !ValidIdentity(identity.Value))
                            throw new InvalidDataException("The friends settings contain an invalid saved identity. Restore TavernSocial.json from its backup.");
                    settings = loaded;
                }
                startPending = settings.RelayUrl.Length > 0;
            }
            catch (Exception error) { settingsError = "The saved friends settings could not be read. Restore UserData/TavernSocial.json from its backup before reconnecting; your existing identity has been preserved."; SetError(new InvalidDataException(settingsError, error)); }
            try { TavernSocialTransport.Initialize(); }
            catch { SetError(new InvalidOperationException("The server friends-card connection could not start. The friends board is still available.")); }
        }

        internal static string NormalizeRelayUrl(string address)
        {
            Uri uri;
            if (!Uri.TryCreate((address ?? "").Trim(), UriKind.Absolute, out uri) || uri.Host.Length == 0 ||
                uri.UserInfo.Length > 0 || uri.Query.Length > 0 || uri.Fragment.Length > 0 || uri.Port == 0)
                throw new ArgumentException("Enter the friends relay's HTTPS address without credentials, query parameters, or a fragment.");
            IPAddress ip;
            bool literalLoopback = IPAddress.TryParse(uri.DnsSafeHost.Trim('[', ']'), out ip) && IPAddress.IsLoopback(ip);
            if (uri.Scheme != Uri.UriSchemeHttps && !(uri.Scheme == Uri.UriSchemeHttp && literalLoopback))
                throw new ArgumentException("Friends relays require HTTPS. HTTP is allowed only for a literal loopback address such as 127.0.0.1.");
            return uri.GetLeftPart(UriPartial.Authority).TrimEnd('/') + uri.AbsolutePath.TrimEnd('/');
        }

        internal static async Task ConfigureAsync(string address)
        {
            string normalized = String.IsNullOrWhiteSpace(address) ? "" : NormalizeRelayUrl(address);
            await ConfigureGate.WaitAsync().ConfigureAwait(false);
            try
            {
                if (path == null) throw new InvalidOperationException("The friends service has not been initialized.");
                if (settingsError != null) throw new InvalidDataException(settingsError);
                Identity identity = null;
                lock (Gate) { if (normalized.Length > 0) settings.Identities.TryGetValue(normalized, out identity); }
                if (normalized.Length > 0 && identity == null)
                {
                    JObject registered = await Request(normalized, "POST", "v1/register", null, new JObject { { "name", username } }).ConfigureAwait(false);
                    identity = new Identity { SocialId = (string)registered["social_id"], Token = (string)registered["token"], Name = (string)registered["name"] };
                    if (!ValidIdentity(identity)) throw new InvalidDataException("The friends relay returned an invalid account.");
                }
                else if (identity != null)
                {
                    JObject me = await Request(normalized, "GET", "v1/me", identity.Token, null).ConfigureAwait(false);
                    if ((string)me["social_id"] != identity.SocialId) throw new InvalidDataException("The saved friends identity does not match this relay.");
                }
                lock (Gate)
                {
                    // Stage on a clone so an unsuccessful disk write cannot
                    // replace the working in-memory account or selected relay.
                    Settings next = JsonConvert.DeserializeObject<Settings>(JsonConvert.SerializeObject(settings));
                    if (identity != null) next.Identities[normalized] = identity;
                    next.RelayUrl = normalized;
                    Save(next);
                    settings = next;
                    generation++;
                    friends = new JArray(); invites = new JArray();
                    lastError = null; nextPoll = DateTime.MinValue; startPending = false;
                }
                SignalChanged();
            }
            catch (Exception error) { SetError(error); throw; }
            finally { ConfigureGate.Release(); }
        }

        internal static void Tick()
        {
            for (int count = 0; count < 64; count++)
            {
                Action action;
                lock (Gate) { if (MainThread.Count == 0) break; action = MainThread.Dequeue(); }
                try { action(); } catch { SetError(new InvalidOperationException("A friends board update failed. Reopen the friends menu.")); }
            }
            if (startPending)
            {
                startPending = false;
                StartConfiguredRelay();
            }
            if (Configured && !polling && DateTime.UtcNow >= nextPoll)
            {
                polling = true;
                nextPoll = DateTime.UtcNow.AddSeconds(20);
                Poll();
            }
        }

        private static async void StartConfiguredRelay()
        {
            try { await ConfigureAsync(RelayUrl).ConfigureAwait(false); }
            catch { /* LastError is presented by the friends board. */ }
        }
        private static async void Poll()
        {
            Session session = null;
            try
            {
                session = Capture();
                await Send(session, "POST", "v1/presence", new JObject { { "name", username } }).ConfigureAwait(false);
                var incomingFriends = await Send(session, "GET", "v1/friends", null).ConfigureAwait(false);
                var incomingInvites = await Send(session, "GET", "v1/invites", null).ConfigureAwait(false);
                JArray newFriends = RequireArray(incomingFriends, "friends"), newInvites = RequireArray(incomingInvites, "invites");
                lock (Gate)
                {
                    if (!Matches(session)) return;
                    bool changed = !JToken.DeepEquals(friends, newFriends) || !JToken.DeepEquals(invites, newInvites) || lastError != null;
                    friends = newFriends; invites = newInvites; lastError = null;
                    if (changed) SignalChanged();
                }
            }
            catch (Exception error) { if (Matches(session)) SetError(error); }
            finally { polling = false; }
        }

        internal static async Task<JArray> GetFriendsAsync()
        {
            Session session = Capture();
            try
            {
                JArray result = RequireArray(await Send(session, "GET", "v1/friends", null).ConfigureAwait(false), "friends");
                lock (Gate) { if (!Matches(session)) throw new InvalidOperationException("The selected friends relay changed."); friends = result; }
                return (JArray)result.DeepClone();
            }
            catch (Exception error) { SetError(error); throw; }
        }
        internal static async Task<JArray> GetInvitesAsync()
        {
            Session session = Capture();
            try
            {
                JArray result = RequireArray(await Send(session, "GET", "v1/invites", null).ConfigureAwait(false), "invites");
                lock (Gate) { if (!Matches(session)) throw new InvalidOperationException("The selected friends relay changed."); invites = result; }
                return (JArray)result.DeepClone();
            }
            catch (Exception error) { SetError(error); throw; }
        }
        internal static async Task SendInviteAsync(string friendID, ServerEntry server)
        {
            if (!ServerCatalog.ValidEntry(server)) throw new ArgumentException("Select a valid server before inviting a friend.");
            ValidateId(friendID);
            // Never include passwords, Tavern auth tokens, or connection JWTs.
            string name = new string((server.Name ?? server.Host).Where(c => !Char.IsControl(c)).ToArray()).Trim();
            if (name.Length == 0) name = server.Host;
            if (name.Length > 64) name = name.Substring(0, 64);
            var destination = new JObject { { "name", name }, { "host", server.Host.Trim() }, { "game_port", server.GamePort }, { "auth_port", server.AuthPort }, { "kind", server.Kind == "headless" ? "headless" : "official" } };
            await Mutate("POST", "v1/invites", new JObject { { "to_id", friendID }, { "server", destination } }).ConfigureAwait(false);
        }
        internal static async Task<ServerEntry> AcceptInviteAsync(string id)
        {
            ValidateId(id);
            JObject result = await Mutate("POST", "v1/invites/" + Uri.EscapeDataString(id) + "/accept", new JObject()).ConfigureAwait(false);
            JObject server = result["server"] as JObject;
            if (server == null) throw new InvalidDataException("The invitation contains no server address.");
            var entry = new ServerEntry { Name = (string)server["name"], Host = (string)server["host"], GamePort = (int?)server["game_port"] ?? 0, AuthPort = (int?)server["auth_port"] ?? 0, Private = true, Favorite = true };
            string kind = (string)server["kind"] ?? "official";
            if (kind != "official" && kind != "headless") throw new InvalidDataException("The invitation specifies an unsupported server join mode.");
            entry.Kind = kind;
            if (!ServerCatalog.ValidEntry(entry)) throw new InvalidDataException("The invitation contains an invalid server address.");
            return entry;
        }
        internal static Task DismissInviteAsync(string id) { ValidateId(id); return Mutate("POST", "v1/invites/" + Uri.EscapeDataString(id) + "/dismiss", new JObject()); }
        internal static Task RemoveFriendAsync(string id) { ValidateId(id); return Mutate("DELETE", "v1/friends/" + Uri.EscapeDataString(id), null); }
        private static async Task<JObject> Mutate(string method, string route, JObject data)
        {
            Session session = Capture();
            try
            {
                JObject result = await Send(session, method, route, data).ConfigureAwait(false);
                if (!Matches(session)) throw new InvalidOperationException("The selected friends relay changed.");
                RequestRefresh();
                return result;
            }
            catch (Exception error) { SetError(error); throw; }
        }
        internal static async Task<string> GetSessionTicketAsync(string serverId, string expectedRelay)
        {
            ValidateId(serverId);
            Session session = Capture();
            if (!String.Equals(session.Url, NormalizeRelayUrl(expectedRelay), StringComparison.Ordinal))
                throw new InvalidOperationException("This server uses a different friends relay. Configure the matching relay in the friends menu first.");
            JObject response = await Send(session, "POST", "v1/session-ticket", new JObject { { "server_id", serverId } }).ConfigureAwait(false);
            if (!Matches(session)) throw new InvalidOperationException("The selected friends relay changed.");
            string ticket = (string)response["ticket"];
            if (String.IsNullOrWhiteSpace(ticket) || ticket.Length > 512 || ticket.Any(Char.IsControl)) throw new InvalidDataException("The friends relay returned an invalid session ticket.");
            return ticket;
        }

        internal static void RequestRefresh() { lock (Gate) nextPoll = DateTime.MinValue; SignalChanged(); }
        internal static void Post(Action callback) { lock (Gate) MainThread.Enqueue(callback); }
        internal static void SetError(Exception error)
        {
            string message = error is ArgumentException || error is InvalidOperationException || error is InvalidDataException
                ? error.Message : error is TaskCanceledException ? "The friends relay did not respond in time."
                : error is HttpRequestException ? "Could not reach the friends relay. Check its address and HTTPS certificate."
                : "The friends service could not complete the request (" + error.GetType().Name + ").";
            lock (Gate) { if (lastError == message) return; lastError = message; }
            SignalChanged();
        }
        private static void SignalChanged() { Post(delegate { Action listener = Changed; if (listener != null) listener(); }); }
        private static void ValidateId(string id)
        {
            if (String.IsNullOrWhiteSpace(id) || id.Length > 128 || id.Any(c => !Char.IsLetterOrDigit(c) && c != '-' && c != '_'))
                throw new ArgumentException("The friends identity or invitation ID is invalid.");
        }
        private static bool ValidIdentity(Identity identity)
        {
            if (identity == null || String.IsNullOrWhiteSpace(identity.SocialId) || identity.SocialId.Length > 128 || String.IsNullOrWhiteSpace(identity.Token) || identity.Token.Length > 512) return false;
            return !identity.SocialId.Any(Char.IsControl) && !identity.Token.Any(Char.IsControl);
        }
        private static JArray RequireArray(JObject response, string property)
        {
            JArray result = response[property] as JArray;
            if (result == null || result.Count > 1000) throw new InvalidDataException("The friends relay returned an invalid " + property + " list.");
            return result;
        }
        private static Session Capture()
        {
            lock (Gate)
            {
                Identity identity;
                if (settings.RelayUrl.Length == 0 || !settings.Identities.TryGetValue(settings.RelayUrl, out identity) || !ValidIdentity(identity))
                    throw new InvalidOperationException("Configure a shared friends relay in the friends menu first.");
                return new Session { Url = settings.RelayUrl, Id = identity.SocialId, Token = identity.Token, Generation = generation };
            }
        }
        private static bool Matches(Session session) { lock (Gate) return session != null && session.Generation == generation && session.Url == settings.RelayUrl; }
        private static Task<JObject> Send(Session session, string method, string route, JObject body) { return Request(session.Url, method, route, session.Token, body); }
        private static void Save(Settings value)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporary, JsonConvert.SerializeObject(value, Formatting.Indented), new UTF8Encoding(false));
                if (File.Exists(path)) File.Replace(temporary, path, path + ".bak"); else File.Move(temporary, path);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        private static async Task<JObject> Request(string baseUrl, string method, string route, string token, JObject data)
        {
            const int limit = 256 * 1024;
            using (var handler = new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false })
            using (var http = new HttpClient(handler))
            using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15)))
            using (var request = new HttpRequestMessage(new HttpMethod(method), baseUrl + "/" + route))
            {
                http.Timeout = TimeSpan.FromSeconds(15);
                if (token != null) request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
                if (data != null) request.Content = new StringContent(data.ToString(Formatting.None), Encoding.UTF8, "application/json");
                using (HttpResponseMessage response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false))
                {
                    if (!response.IsSuccessStatusCode) throw new InvalidOperationException("The friends relay rejected the request (HTTP " + (int)response.StatusCode + ").");
                    if (response.Content.Headers.ContentLength > limit) throw new InvalidDataException("The friends relay response is too large.");
                    using (var input = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                    using (var output = new MemoryStream())
                    {
                        byte[] buffer = new byte[8192]; int read;
                        while ((read = await input.ReadAsync(buffer, 0, buffer.Length, timeout.Token).ConfigureAwait(false)) > 0)
                        {
                            if (output.Length + read > limit) throw new InvalidDataException("The friends relay response is too large.");
                            output.Write(buffer, 0, read);
                        }
                        try
                        {
                            using (var reader = new JsonTextReader(new StringReader(Encoding.UTF8.GetString(output.ToArray()))) { MaxDepth = 32 })
                                return JObject.Load(reader);
                        }
                        catch (JsonException) { throw new InvalidDataException("The friends relay returned invalid JSON."); }
                    }
                }
            }
        }
    }
}
