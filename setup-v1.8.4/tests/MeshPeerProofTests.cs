using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TavernNativeMenu;

internal sealed class PeerProcess : IDisposable
{
    private readonly object gate = new object();
    private readonly Dictionary<long, JObject> replies = new Dictionary<long, JObject>();
    private readonly List<JObject> verified = new List<JObject>();
    private readonly Process process;
    private JObject state = new JObject();
    private long next;
    internal readonly string Folder;
    internal PeerProcess(string folder, string native, string helper, JObject seed)
    {
        Folder = folder;
        string lib = Path.Combine(folder, "TavernNativeMenu", "native"); Directory.CreateDirectory(lib);
        foreach (string name in new[] { "TavernMeshPeer.exe", "Newtonsoft.Json.dll" }) File.Copy(Path.Combine(helper, name), Path.Combine(lib, name), true);
        File.Copy(Path.Combine(native, "libtoxcore.dll"), Path.Combine(lib, "libtoxcore.dll"), true);
        File.WriteAllText(Path.Combine(lib, "bootstrap-nodes.json"), new JArray(seed.DeepClone()).ToString());
        process = new Process { StartInfo = new ProcessStartInfo(Path.Combine(lib, "TavernMeshPeer.exe"), "--stdio-v2 \"" + folder + "\" Test") {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true } };
        process.OutputDataReceived += delegate(object sender, DataReceivedEventArgs args) {
            if (args.Data == null) return;
            lock (gate)
            {
                JObject value = JObject.Parse(args.Data);
                if ((string)value["type"] == "state") state = value;
                else if ((string)value["type"] == "verified") verified.Add(value);
                else if ((string)value["type"] == "reply") replies[(long)value["id"]] = value;
                Monitor.PulseAll(gate);
            }
        };
        process.ErrorDataReceived += delegate(object sender, DataReceivedEventArgs args) { if (args.Data != null) Console.Error.WriteLine(args.Data); };
        process.Start(); process.BeginOutputReadLine(); process.BeginErrorReadLine();
        MeshPeerProofTests.Wait(delegate { JObject snapshot = State(); return (bool?)snapshot["ready"] == true || snapshot["error"] != null && snapshot["error"].Type != JTokenType.Null; }, "helper starts " + Path.GetFileName(folder), 15);
        if ((bool?)State()["ready"] != true) throw new Exception("Helper failed: " + State());
    }
    internal JObject State() { lock (gate) return (JObject)state.DeepClone(); }
    internal string Address { get { return (string)State()["address"]; } }
    internal string Key { get { return Address.Substring(0, 64); } }
    internal int VerifiedCount { get { lock (gate) return verified.Count; } }
    internal bool Verified(string key, string nonce) { lock (gate) return verified.Any(x => (string)x["key"] == key && (string)x["nonce"] == nonce); }
    internal JArray List(string name) { return State()[name] as JArray ?? new JArray(); }
    internal JObject Send(string action, params object[] values)
    {
        long id; lock (gate) id = ++next;
        var command = new JObject { { "id", id }, { "action", action } };
        for (int n = 0; n < values.Length; n += 2) command[(string)values[n]] = JToken.FromObject(values[n + 1]);
        process.StandardInput.WriteLine(command.ToString(Formatting.None)); process.StandardInput.Flush();
        lock (gate)
        {
            DateTime until = DateTime.UtcNow.AddSeconds(20);
            while (!replies.ContainsKey(id) && DateTime.UtcNow < until) Monitor.Wait(gate, 100);
            JObject reply;
            if (!replies.TryGetValue(id, out reply)) throw new Exception("Timed out: " + action);
            replies.Remove(id);
            if ((bool?)reply["ok"] != true) throw new InvalidOperationException((string)reply["error"]);
            return reply;
        }
    }
    internal void Verify(string code, string nonce, long expires) { Send("verify", "code", code, "nonce", nonce, "expires", expires); }
    internal void Invite(string key) { Send("invite", "key", key, "server", new JObject { { "Name", "Private" }, { "Host", "private.example.org" }, { "GamePort", 1757 }, { "AuthPort", 1762 }, { "Kind", "official" } }); }
    internal JObject Profile() { return JObject.Parse(File.ReadAllText(Path.Combine(Folder, "UserData", "TavernMesh.json"))); }
    public void Dispose()
    {
        if (process.HasExited) return;
        try { Send("shutdown"); } catch { }
        if (!process.WaitForExit(5000)) { process.StandardInput.Close(); if (!process.WaitForExit(5000)) process.Kill(); }
        process.WaitForExit();
    }
}

internal static class MeshPeerProofTests
{
    private static int checks;
    private static volatile bool stopSeeds;
    private static Exception seedError;
    private static readonly ManualResetEvent SeedReady = new ManualResetEvent(false);
    private static readonly List<PeerProcess> Clients = new List<PeerProcess>();
    private static JArray seeds;
    [DllImport("libtoxcore.dll", CallingConvention = CallingConvention.Cdecl)] private static extern void tox_self_get_dht_id(IntPtr tox, byte[] key);
    [DllImport("libtoxcore.dll", CallingConvention = CallingConvention.Cdecl)] private static extern ushort tox_self_get_udp_port(IntPtr tox, out int error);
    internal static long Now { get { return (long)(DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds; } }
    internal static void Check(bool value, string label) { if (!value) throw new Exception(label); Console.WriteLine("PASS " + label); checks++; }
    internal static void Wait(Func<bool> condition, string label, int seconds)
    {
        DateTime until = DateTime.UtcNow.AddSeconds(seconds);
        while (!condition() && DateTime.UtcNow < until) Thread.Sleep(100);
        Check(condition(), label);
    }
    private static void Fails(Action action, string label) { bool failed = false; try { action(); } catch (InvalidOperationException) { failed = true; } Check(failed, label); }
    private static void Seeds(string native)
    {
        var cores = new List<MeshNative>();
        try
        {
            seeds = new JArray();
            for (int i = 0; i < 4; i++)
            {
                var core = new MeshNative(native, null, "Local proof-test bootstrap"); cores.Add(core);
                IntPtr handle = (IntPtr)typeof(MeshNative).GetField("handle", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(core);
                byte[] key = new byte[32]; tox_self_get_dht_id(handle, key); int error; ushort port = tox_self_get_udp_port(handle, out error);
                if (error != 0 || port == 0) throw new Exception("Local seed port unavailable.");
                seeds.Add(new JObject { { "host", "127.0.0.1" }, { "port", port }, { "public_key", MeshNative.Hex(key) }, { "tcp_ports", new JArray() } });
            }
            foreach (MeshNative core in cores) foreach (JObject seed in seeds) core.Bootstrap("127.0.0.1", (ushort)seed["port"], (string)seed["public_key"], new ushort[0]);
            SeedReady.Set();
            while (!stopSeeds) { foreach (MeshNative core in cores) core.Iterate(); Thread.Sleep(20); }
        }
        catch (Exception error) { seedError = error; SeedReady.Set(); }
        finally { foreach (MeshNative core in cores) core.Dispose(); }
    }
    private static PeerProcess Start(string root, string name, string native, string helper, int seed)
    {
        var client = new PeerProcess(Path.Combine(root, name), native, helper, (JObject)seeds[seed]); Clients.Add(client); return client;
    }
    private static string[] SavedNativeFriends(PeerProcess client, string native)
    {
        using (var core = new MeshNative(native, Convert.FromBase64String((string)client.Profile()["Savedata"]), "Offline state inspection")) return core.FriendKeys();
    }
    public static int Main(string[] args)
    {
        Thread seedThread = null;
        try
        {
            string root = args[0], native = args[1], helper = args[2];
            seedThread = new Thread(delegate() { Seeds(native); }) { IsBackground = true }; seedThread.Start();
            if (!SeedReady.WaitOne(15000) || seedError != null) throw new Exception("Local seed startup failed.", seedError);
            PeerProcess alice = Start(root, "Alice", native, helper, 0), bob = Start(root, "Bob", native, helper, 1), other = Start(root, "Other", native, helper, 2);
            string aCode = alice.Address, bCode = bob.Address, oCode = other.Address, aid = alice.Key, bid = bob.Key, oid = other.Key;
            string proof = new string('A', 43), wrong = new string('B', 43), alternate = new string('C', 43);
            alice.Verify(bCode, proof, Now + 120); bob.Verify(aCode, proof, Now + 120);
            alice.Verify(oCode, alternate, Now + 120); other.Verify(aCode, alternate, Now + 120);
            Wait(delegate { return alice.Verified(bid, proof) && bob.Verified(aid, proof); }, "matching authenticated identity proof emits verified on both helpers", 100);
            Check(alice.List("friends").Count == 0 && bob.List("friends").Count == 0 && alice.List("requests").Count == 0 && bob.List("requests").Count == 0, "identity proof creates neither friendships nor incoming requests");
            Check(((JArray)alice.Profile()["Contacts"]).Count == 0 && ((JArray)bob.Profile()["Contacts"]).Count == 0, "identity proof does not create persistent social contacts");
            Wait(delegate { return alice.Verified(oid, alternate) && other.Verified(aid, alternate); }, "independent peer proof connects through another local bootstrap", 100);
            int aProofs = alice.VerifiedCount, bProofs = bob.VerifiedCount;
            alice.Verify(bCode, wrong, Now + 120); bob.Verify(aCode, alternate, Now + 120);
            other.Verify(aCode, wrong, Now + 120); alice.Verify(oCode, proof, Now + 120);
            Thread.Sleep(7000);
            Check(alice.VerifiedCount == aProofs && bob.VerifiedCount == bProofs && !alice.Verified(bid, wrong), "wrong nonce and matching nonce from the wrong peer cannot satisfy identity proof");
            Fails(delegate { alice.Verify(bCode, proof, Now - 1); }, "expired verification rejected");
            Fails(delegate { alice.Verify(bCode, "invalid", Now + 120); }, "malformed verification nonce rejected");
            alice.Send("request", "code", bCode);
            Wait(delegate { return bob.List("requests").Count == 1; }, "friend request after ephemeral verification reaches recipient", 30);
            Check(alice.List("friends").Count == 0 && bob.List("friends").Count == 0, "verified identity still requires explicit friendship acceptance");
            bob.Send("accept-request", "key", aid);
            Wait(delegate { return alice.List("friends").Count == 1 && bob.List("friends").Count == 1; }, "explicit acceptance after verification creates both friendships", 30);
            bob.Invite(aid);
            Wait(delegate { return alice.List("invites").Count == 1; }, "accepted peer invitation arrives before blocking", 30);
            bob.Dispose();
            alice.Invite(bid);
            Check(((JArray)alice.Profile()["Outbox"]).Count == 1, "offline outgoing invitation is persisted before blocking");
            alice.Send("block", "code", bCode, "name", "Blocked Bob");
            Check(alice.List("friends").Count == 0 && alice.List("requests").Count == 0 && alice.List("invites").Count == 0 && ((JArray)alice.Profile()["Outbox"]).Count == 0, "blocking clears friendship, requests, inbox and outgoing invitations");
            Check(alice.List("blocked").Count == 1 && (string)alice.List("blocked")[0]["address"] == bCode, "blocked snapshot exposes only saved public identity and label");
            Fails(delegate { alice.Send("request", "code", bCode); }, "explicit block prevents local friend-code request");
            Fails(delegate { alice.Send("pair", "code", bCode, "name", "Bob", "nonce", proof, "expires", Now + 120); }, "explicit block prevents physical-card pairing");
            Fails(delegate { alice.Verify(bCode, proof, Now + 120); }, "explicit block prevents peer verification");
            Fails(delegate { alice.Invite(bid); }, "explicit block prevents outgoing invitations");
            alice.Verify(oCode, new string('F', 43), Now + 120);
            alice.Dispose();
            Check(SavedNativeFriends(alice, native).Contains(oid), "saved tox state includes the pre-restart transient peer fixture");
            alice = Start(root, "Alice", native, helper, 0);
            Check(alice.List("blocked").Count == 1 && alice.List("friends").Count == 0 && alice.Address == aCode, "explicit block and identity survive helper restart");
            alice.Dispose();
            Check(SavedNativeFriends(alice, native).Length == 0, "restart removes orphan verification and blocked native peers");
            alice = Start(root, "Alice", native, helper, 0); bob = Start(root, "Bob", native, helper, 1);
            bob.Send("remove", "key", aid); bob.Send("request", "code", aCode);
            Thread.Sleep(5500);
            Check(alice.List("requests").Count == 0 && alice.List("friends").Count == 0, "explicit block suppresses incoming re-add attempt");
            alice.Send("unblock", "key", bid);
            Check(alice.List("blocked").Count == 0 && alice.List("friends").Count == 0, "unblocking does not restore friendship or invitations");
            Wait(delegate { return alice.List("requests").Count == 1; }, "unblock permits a remote incoming request without initiating one locally", 100);
            Check(alice.List("friends").Count == 0, "incoming request after unblock still requires explicit acceptance");
            alice.Send("accept-request", "key", bid);
            Wait(delegate { return alice.List("friends").Count == 1 && bob.List("friends").Count == 1; }, "unblocked incoming request reaches mutual acceptance", 100);
            alice.Send("block", "code", bCode, "name", "Bob");
            alice.Send("unblock", "key", bid);
            Check(alice.List("friends").Count == 0 && bob.List("friends").Count == 1, "one-sided unblock leaves the caller unaccepted and preserves the peer's existing consent");
            alice.Send("request", "code", bCode);
            Wait(delegate { return alice.List("friends").Count == 1 && bob.List("friends").Count == 1; }, "fresh re-add completes when the peer still has the original accepted contact", 100);
            PeerProcess expiry = Start(root, "Expiry", native, helper, 3);
            expiry.Verify(oCode, proof, Now + 2); Thread.Sleep(3000); expiry.Dispose();
            Check(SavedNativeFriends(expiry, native).Length == 0 && ((JArray)expiry.Profile()["Contacts"]).Count == 0, "expired transient verification removes native connection without saving a contact");
            expiry = Start(root, "Expiry", native, helper, 3);
            for (int n = 0; n < 64; n++) expiry.Verify(oCode, n.ToString().PadLeft(43, 'D'), Now + 120);
            Fails(delegate { expiry.Verify(oCode, new string('E', 43), Now + 120); }, "pending identity verification count is bounded");
            Check(expiry.List("friends").Count == 0 && expiry.List("requests").Count == 0, "bounded verification load cannot populate friends board");
            Console.WriteLine("Peer proof/block integration: " + checks + " checks passed using real helper processes and local native networking.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        finally { foreach (PeerProcess client in Clients) try { client.Dispose(); } catch { }; stopSeeds = true; if (seedThread != null) seedThread.Join(2000); }
    }
}
