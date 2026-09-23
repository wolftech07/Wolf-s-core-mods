using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace TavernNativeMenu
{
    // JSON process boundary: the GPL peer engine is a standalone program with
    // no dependency on the game. It starts and exits with this client.
    internal static class MeshSocialClient
    {
        private static readonly object Gate = new object(), WriteGate = new object();
        private static readonly Queue<Action> Main = new Queue<Action>();
        private static readonly Dictionary<long, TaskCompletionSource<JToken>> Pending = new Dictionary<long, TaskCompletionSource<JToken>>();
        private static Process process;
        private static bool started;
        private static volatile bool ready, connected, stopping;
        private static long serial;
        private static string address = "", error;
        private static JArray friends = new JArray(), requests = new JArray(), invites = new JArray(), blocked = new JArray();
        internal static event Action Changed;
        internal static event Action<string> Diagnostic;
        internal static event Action<string, string> PairVerified;
        internal static event Action<string, string> PeerVerified;
        internal static bool Configured { get { return ready; } }
        internal static bool Connected { get { return connected; } }
        internal static string NetworkId { get { return "tavern-peer-v2"; } }
        internal static string Address { get { lock (Gate) return address; } }
        internal static string SocialId { get { string value = Address; return value.Length == 76 ? value.Substring(0, 64) : ""; } }
        internal static string LastError { get { lock (Gate) return error; } }
        internal static long Now { get { return (long)(DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds; } }

        internal static void Initialize(string gamePath, string name)
        {
            if (started) return; started = true;
            MeshSocialTransport.Initialize();
            try
            {
                string native = Path.Combine(gamePath, "TavernNativeMenu", "native");
                string executable = Path.Combine(native, "TavernMeshPeer.exe");
                if (!File.Exists(executable)) throw new FileNotFoundException("Friends networking files are missing. Run Install / Update in the setup.");
                var info = new ProcessStartInfo(executable, "--stdio-v2 " + Quote(Path.GetFullPath(gamePath)) + " " + Quote(name ?? "Friend")) {
                    WorkingDirectory = native, UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
                    RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true
                };
                process = Process.Start(info);
                new Thread(ReadOutput) { IsBackground = true, Name = "Tavern friends updates" }.Start();
                new Thread(ReadErrors) { IsBackground = true, Name = "Tavern friends diagnostics" }.Start();
            }
            catch (Exception failure) { Fail(failure); }
        }
        private static string Quote(string value)
        {
            // Windows CommandLineToArgvW quoting, including terminal slashes.
            var text = new System.Text.StringBuilder("\""); int slashes = 0;
            foreach (char c in value)
            {
                if (c == '\0') throw new ArgumentException("Invalid command argument.");
                if (c == '\\') { slashes++; continue; }
                text.Append('\\', c == '"' ? slashes * 2 + 1 : slashes); text.Append(c); slashes = 0;
            }
            text.Append('\\', slashes * 2); return text.Append('"').ToString();
        }
        private static void ReadOutput()
        {
            try
            {
                string line;
                while ((line = ReadBounded(process.StandardOutput, 256 * 1024)) != null)
                {
                    JObject message;
                    using (var reader = new JsonTextReader(new StringReader(line)) { MaxDepth = 12 })
                    { message = JObject.Load(reader); if (reader.Read()) throw new InvalidDataException("Unexpected friends update."); }
                    string kind = (string)message["type"];
                    if (kind == "state")
                    {
                        if ((int?)message["v"] != 2) throw new InvalidDataException("The friends helper version is incompatible. Update the mod.");
                        string issue = (string)message["error"];
                        bool report = issue != null && issue != LastError;
                        lock (Gate)
                        {
                            ready = (bool)message["ready"]; connected = (bool)message["connected"];
                            address = (string)message["address"] ?? ""; error = (string)message["error"];
                            friends = (JArray)message["friends"]; requests = (JArray)message["requests"]; invites = (JArray)message["invites"];
                            blocked = message["blocked"] as JArray ?? new JArray();
                        }
                        if (report) Report(issue);
                        Signal();
                    }
                    else if (kind == "paired" || kind == "verified")
                    {
                        string key = (string)message["key"], nonce = (string)message["nonce"];
                        Post(delegate { Action<string, string> handler = kind == "paired" ? PairVerified : PeerVerified; if (handler != null) handler(key, nonce); });
                    }
                    else if (kind == "reply")
                    {
                        TaskCompletionSource<JToken> completion;
                        lock (Gate) { long id = (long)message["id"]; if (!Pending.TryGetValue(id, out completion)) continue; Pending.Remove(id); }
                        if ((bool?)message["ok"] == true) completion.TrySetResult(message["result"]);
                        else completion.TrySetException(new InvalidOperationException((string)message["error"] ?? "The friends operation failed."));
                    }
                }
                if (!stopping) throw new IOException(LastError ?? "The friends helper stopped. Restart the game to reconnect; saved contacts are retained.");
            }
            catch (Exception failure) { if (!stopping) Fail(failure); }
            finally { FailPending(new OperationCanceledException("The friends connection closed.")); }
        }
        private static void ReadErrors()
        {
            try { string line; while ((line = ReadBounded(process.StandardError, 4096)) != null) if (!stopping) SetError(new IOException(line)); }
            catch (Exception) { }
        }
        private static string ReadBounded(TextReader reader, int limit)
        {
            var text = new System.Text.StringBuilder();
            for (int c; (c = reader.Read()) != -1;)
            {
                if (c == '\n') return text.ToString();
                if (text.Length >= limit) throw new InvalidDataException("The friends helper exceeded its message limit.");
                if (c != '\r') text.Append((char)c);
            }
            return text.Length == 0 ? null : text.ToString();
        }
        private static Task<JToken> Send(string action, JObject arguments)
        {
            var completion = new TaskCompletionSource<JToken>();
            long id;
            lock (Gate)
            {
                if (!ready || stopping || Pending.Count >= 100) { completion.SetException(new InvalidOperationException(error ?? "The friends network is starting. Try again shortly.")); return completion.Task; }
                id = ++serial; Pending.Add(id, completion);
            }
            try
            {
                arguments["action"] = action; arguments["id"] = id;
                lock (WriteGate) { process.StandardInput.WriteLine(arguments.ToString(Formatting.None)); process.StandardInput.Flush(); }
            }
            catch (Exception failure) { lock (Gate) Pending.Remove(id); completion.TrySetException(failure); Fail(failure); }
            return completion.Task;
        }
        internal static Task RequestFriendAsync(string code) { return Send("request", new JObject { { "code", code } }); }
        internal static Task AcceptRequestAsync(string key) { return Send("accept-request", new JObject { { "key", key } }); }
        internal static Task RemoveFriendAsync(string key) { return Send("remove", new JObject { { "key", key } }); }
        internal static Task BlockPeerAsync(string code, string name) { return Send("block", new JObject { { "code", code }, { "name", name } }); }
        internal static Task UnblockPeerAsync(string key) { return Send("unblock", new JObject { { "key", key } }); }
        internal static Task VerifyPeerAsync(string code, string nonce, long expires) { return Send("verify", new JObject { { "code", code }, { "nonce", nonce }, { "expires", expires } }); }
        internal static Task PairCardAsync(string code, string name, string nonce, long expires) { return Send("pair", new JObject { { "code", code }, { "name", name }, { "nonce", nonce }, { "expires", expires } }); }
        internal static Task SendInviteAsync(string key, ServerEntry server)
        {
            return Send("invite", new JObject { { "key", key }, { "server", JObject.FromObject(new { server.Name, server.Host, server.Kind, server.GamePort, server.AuthPort, server.Private }) } });
        }
        internal static async Task<ServerEntry> AcceptInviteAsync(string id) { return (await Send("accept-invite", new JObject { { "invite_id", id } })).ToObject<ServerEntry>(); }
        internal static Task DismissInviteAsync(string id) { return Send("dismiss", new JObject { { "invite_id", id } }); }
        internal static Task<JArray> GetFriendsAsync() { lock (Gate) return Task.FromResult((JArray)friends.DeepClone()); }
        internal static Task<JArray> GetRequestsAsync() { lock (Gate) return Task.FromResult((JArray)requests.DeepClone()); }
        internal static Task<JArray> GetInvitesAsync() { lock (Gate) return Task.FromResult((JArray)invites.DeepClone()); }
        internal static Task<JArray> GetBlockedAsync() { lock (Gate) return Task.FromResult((JArray)blocked.DeepClone()); }
        internal static void RequestRefresh() { Signal(); }
        internal static void Post(Action action) { lock (Gate) { if (Main.Count < 512) Main.Enqueue(action); } }
        private static void Signal() { Post(delegate { Action handler = Changed; if (handler != null) handler(); }); }
        private static void Report(string message) { Post(delegate { Action<string> handler = Diagnostic; if (handler != null) handler(message); }); }
        internal static void SetError(Exception failure)
        {
            bool changed; lock (Gate) { changed = error != failure.Message; error = failure.Message; }
            if (changed) Report(failure.Message); Signal();
        }
        private static void Fail(Exception failure)
        {
            lock (Gate) { ready = connected = false; foreach (JObject friend in friends) friend["online"] = false; }
            SetError(failure); FailPending(failure);
        }
        private static void FailPending(Exception failure)
        {
            TaskCompletionSource<JToken>[] completions;
            lock (Gate) { completions = new List<TaskCompletionSource<JToken>>(Pending.Values).ToArray(); Pending.Clear(); }
            foreach (var completion in completions) completion.TrySetException(failure);
        }
        internal static void Tick()
        {
            MeshSocialTransport.Tick();
            for (int n = 0; n < 64; n++)
            {
                Action action; lock (Gate) { if (Main.Count == 0) break; action = Main.Dequeue(); }
                try { action(); } catch (Exception failure) { SetError(failure); }
            }
        }
        internal static void Shutdown()
        {
            stopping = true; ready = connected = false;
            try
            {
                if (process != null && !process.HasExited)
                {
                    lock (WriteGate) process.StandardInput.Close();
                    if (!process.WaitForExit(4000)) process.Kill();
                }
            }
            catch (Exception) { }
            FailPending(new OperationCanceledException("The game closed."));
        }
        internal static string ValidateAddress(string code)
        {
            code = (code ?? "").Trim().ToUpperInvariant();
            if (code.Length != 76) throw new ArgumentException("Use the full 76-character friend code.");
            byte a = 0, b = 0; bool nonzero = false;
            for (int i = 0; i < 38; i++)
            {
                byte value;
                if (!Uri.IsHexDigit(code[i * 2]) || !Uri.IsHexDigit(code[i * 2 + 1]) || !Byte.TryParse(code.Substring(i * 2, 2), System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out value)) throw new ArgumentException("The friend code contains invalid characters.");
                if (i < 32 && value != 0) nonzero = true;
                if (i < 36) { if (i % 2 == 0) a ^= value; else b ^= value; }
                else if (value != (i == 36 ? a : b)) throw new ArgumentException("The friend code checksum is invalid.");
            }
            if (!nonzero) throw new ArgumentException("The friend code has an invalid public key.");
            return code;
        }
    }
}
