using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TavernNativeMenu;

public sealed class MeshTestClient : MarshalByRefObject
{
    private readonly List<string> verified = new List<string>();
    private string folder;
    public void Start(string folder, string name) { this.folder = folder; MeshSocialClient.PeerVerified += delegate(string key, string nonce) { verified.Add(key + ":" + nonce); }; MeshSocialClient.Initialize(folder, name); }
    public string Diagnostic()
    {
        var result = new JObject { { "ready", MeshSocialClient.Configured }, { "connected", MeshSocialClient.Connected }, { "error", MeshSocialClient.LastError } };
        string file = Path.Combine(folder, "UserData", "TavernMesh.json");
        if (File.Exists(file))
        {
            var saved = JObject.Parse(File.ReadAllText(file));
            result["contacts"] = new JArray(((JArray)saved["Contacts"]).OfType<JObject>().Select(contact => new JObject {
                { "key", ((string)contact["Key"] ?? "").Substring(0, 8) }, { "state", contact["State"] }, { "kind", contact["PairKind"] },
                { "expires_in", ((long?)contact["Expires"] ?? 0) - MeshSocialClient.Now }, { "nonce", contact["Nonce"] } }));
        }
        return result.ToString(Formatting.None);
    }
    public bool Ready() { MeshSocialClient.Tick(); return MeshSocialClient.Configured; }
    public string Error() { return MeshSocialClient.LastError; }
    public string Address() { return MeshSocialClient.Address; }
    public string Friends() { MeshSocialClient.Tick(); return MeshSocialClient.GetFriendsAsync().Result.ToString(); }
    public string Requests() { return MeshSocialClient.GetRequestsAsync().Result.ToString(); }
    public string Invites() { return MeshSocialClient.GetInvitesAsync().Result.ToString(); }
    public void Request(string code) { MeshSocialClient.RequestFriendAsync(code).GetAwaiter().GetResult(); }
    public void Accept(string key) { MeshSocialClient.AcceptRequestAsync(key).GetAwaiter().GetResult(); }
    public void Remove(string key) { MeshSocialClient.RemoveFriendAsync(key).GetAwaiter().GetResult(); }
    public void Block(string code) { MeshSocialClient.BlockPeerAsync(code, "Blocked player").GetAwaiter().GetResult(); }
    public void Unblock(string key) { MeshSocialClient.UnblockPeerAsync(key).GetAwaiter().GetResult(); }
    public string Blocked() { return MeshSocialClient.GetBlockedAsync().Result.ToString(); }
    public void Verify(string code, string nonce) { MeshSocialClient.VerifyPeerAsync(code, nonce, MeshSocialClient.Now + 120).GetAwaiter().GetResult(); }
    public bool Verified(string key, string nonce) { MeshSocialClient.Tick(); return verified.Contains(key + ":" + nonce); }
    public void Pair(string code, string nonce) { MeshSocialClient.PairCardAsync(code, "Card Friend", nonce, MeshSocialClient.Now + 120).GetAwaiter().GetResult(); }
    public void Invite(string key) { MeshSocialClient.SendInviteAsync(key, new ServerEntry { Name = "Private home", Host = "private.example.org", GamePort = 1857, AuthPort = 1862, Kind = "official", Private = true, Password = "DO-NOT-SEND-THIS" }).GetAwaiter().GetResult(); }
    public string Join(string id) { return JsonConvert.SerializeObject(MeshSocialClient.AcceptInviteAsync(id).GetAwaiter().GetResult()); }
    public void Stop() { MeshSocialClient.Shutdown(); }
    public override object InitializeLifetimeService() { return null; }
}

internal static class MeshIntegrationTests
{
    private static int checks;
    private static volatile bool stopSeeds;
    private static Exception seedError;
    private static readonly ManualResetEvent SeedReady = new ManualResetEvent(false);
    private static JArray seeds;
    private static readonly List<AppDomain> Domains = new List<AppDomain>();
    private static readonly List<MeshTestClient> Clients = new List<MeshTestClient>();
    [DllImport("libtoxcore.dll", CallingConvention = CallingConvention.Cdecl)] private static extern void tox_self_get_dht_id(IntPtr tox, byte[] key);
    [DllImport("libtoxcore.dll", CallingConvention = CallingConvention.Cdecl)] private static extern ushort tox_self_get_udp_port(IntPtr tox, out int error);
    private static void Check(bool value, string label) { if (!value) throw new Exception(label); Console.WriteLine("PASS " + label); checks++; }
    private static void Wait(Func<bool> condition, string label, int seconds, Func<string> diagnostic = null)
    {
        DateTime until = DateTime.UtcNow.AddSeconds(seconds);
        while (!condition() && DateTime.UtcNow < until) Thread.Sleep(100);
        bool success = condition();
        if (!success && diagnostic != null) Console.Error.WriteLine("DIAGNOSTIC " + label + ": " + diagnostic());
        Check(success, label);
    }
    private static JArray Friends(MeshTestClient client) { return JArray.Parse(client.Friends()); }
    private static JArray Inbox(MeshTestClient client) { return JArray.Parse(client.Invites()); }
    private static string Key(MeshTestClient client) { return client.Address().Substring(0, 64); }
    private static void RunSeeds(string native)
    {
        var nodes = new List<MeshNative>();
        try
        {
            seeds = new JArray();
            for (int i = 0; i < 6; i++)
            {
                var core = new MeshNative(native, null, "Local test bootstrap"); nodes.Add(core);
                IntPtr handle = (IntPtr)typeof(MeshNative).GetField("handle", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(core);
                byte[] key = new byte[32]; tox_self_get_dht_id(handle, key); int error;
                ushort port = tox_self_get_udp_port(handle, out error);
                if (error != 0 || port == 0) throw new Exception("Local bootstrap port unavailable.");
                seeds.Add(new JObject { { "host", "127.0.0.1" }, { "port", port }, { "public_key", MeshNative.Hex(key) }, { "tcp_ports", new JArray() } });
            }
            foreach (MeshNative core in nodes) foreach (JObject node in seeds) core.Bootstrap("127.0.0.1", (ushort)node["port"], (string)node["public_key"], new ushort[0]);
            SeedReady.Set();
            while (!stopSeeds) { foreach (MeshNative core in nodes) core.Iterate(); Thread.Sleep(20); }
        }
        catch (Exception error) { seedError = error; SeedReady.Set(); }
        finally { foreach (MeshNative core in nodes) core.Dispose(); }
    }
    private static MeshTestClient Start(string root, string label, string native, string helper, int seed, bool expectReady = true)
    {
        string folder = Path.Combine(root, label), lib = Path.Combine(folder, "TavernNativeMenu", "native"); Directory.CreateDirectory(lib);
        foreach (string file in new[] { "TavernMeshPeer.exe", "Newtonsoft.Json.dll" }) File.Copy(Path.Combine(helper, file), Path.Combine(lib, file), true);
        File.Copy(Path.Combine(native, "libtoxcore.dll"), Path.Combine(lib, "libtoxcore.dll"), true);
        File.WriteAllText(Path.Combine(lib, "bootstrap-nodes.json"), new JArray(seeds[seed].DeepClone()).ToString());
        AppDomain domain = AppDomain.CreateDomain(label + Guid.NewGuid()); Domains.Add(domain);
        var client = (MeshTestClient)domain.CreateInstanceAndUnwrap(typeof(MeshTestClient).Assembly.FullName, typeof(MeshTestClient).FullName); Clients.Add(client);
        client.Start(folder, label == "Alice" || label == "Bob" ? "Same display name" : label);
        Wait(delegate { return client.Ready() || client.Error() != null; }, label + " helper reports startup", 15);
        if (expectReady) Check(client.Ready(), label + " helper ready: " + (client.Error() ?? "no error"));
        return client;
    }
    public static int Main(string[] args)
    {
        Thread seedThread = null;
        try
        {
            string root = args[0], native = args[1], helper = args[2];
            seedThread = new Thread(delegate() { RunSeeds(native); }) { IsBackground = true }; seedThread.Start();
            if (!SeedReady.WaitOne(15000) || seedError != null) throw new Exception("Seed startup failed.", seedError);
            MeshTestClient alice = Start(root, "Alice", native, helper, 0), bob = Start(root, "Bob", native, helper, 1);
            string aid = Key(alice), bid = Key(bob), aCode = alice.Address(), bCode = bob.Address();
            Check(aid != bid && Friends(alice).Count == 0 && Friends(bob).Count == 0, "same names do not merge identities; bootstrap users are not friends");
            string identityNonce = new string('V', 43);
            alice.Verify(bCode, identityNonce); bob.Verify(aCode, identityNonce);
            Wait(delegate { return alice.Verified(bid, identityNonce) && bob.Verified(aid, identityNonce); }, "authenticated identity verification reaches both production client IPC events", 100);
            Check(Friends(alice).Count == 0 && Friends(bob).Count == 0 && JArray.Parse(alice.Requests()).Count == 0 && JArray.Parse(bob.Requests()).Count == 0, "client verification creates no friend rows or requests");
            alice.Request(bCode);
            Wait(delegate { return JArray.Parse(bob.Requests()).Count == 1; }, "friend-code request after verification crosses different bootstrap points", 100);
            Check(Friends(alice).Count == 0 && Friends(bob).Count == 0, "pending requests never appear as accepted friends");
            bob.Accept(aid);
            Wait(delegate { return Friends(alice).Count == 1 && Friends(bob).Count == 1; }, "mutual acceptance appears on both native-client snapshots", 100);
            Wait(delegate { return (bool)Friends(alice)[0]["online"] && (bool)Friends(bob)[0]["online"]; }, "accepted peers report online presence", 30);
            alice.Invite(bid);
            Wait(delegate { return Inbox(bob).Count == 1; }, "private invitation arrives over the real peer channel", 30);
            Check(!bob.Invites().Contains("DO-NOT-SEND-THIS") && Inbox(alice).Count == 0, "invitation omits passwords and is recipient-only");
            JObject joined = JObject.Parse(bob.Join((string)Inbox(bob)[0]["invite_id"]));
            Check((string)joined["Host"] == "private.example.org" && (int)joined["GamePort"] == 1857 && (int)joined["AuthPort"] == 1862 && (bool)joined["Private"], "accepted invite preserves endpoint and private join settings");
            Check(Inbox(bob).Count == 0, "accepted invitation is removed before reply");
            bob.Stop();
            alice.Invite(bid); alice.Stop();
            alice = Start(root, "Alice", native, helper, 2);
            Check(alice.Address() == aCode && Friends(alice).Count == 1 && !(bool)Friends(alice)[0]["online"], "restart retains identity and accepted offline friend without a relay");
            bob = Start(root, "Bob", native, helper, 3);
            Check(bob.Address() == bCode && Friends(bob).Count == 1, "recipient restart retains identity and contacts");
            Wait(delegate { return Inbox(bob).Count == 1; }, "saved offline outbox delivers after both clients restart", 100);
            bob.Join((string)Inbox(bob)[0]["invite_id"]);
            Thread.Sleep(5500); Check(Inbox(bob).Count == 0, "acknowledged invitations do not reappear on retransmission");
            MeshTestClient card = Start(root, "Card", native, helper, 4);
            string cid = Key(card), nonce = new string('A', 43);
            // Separate network discovery from the negative nonce assertion.
            // Without this precondition, no packets might arrive during the
            // seven-second mismatch window and that assertion proves nothing.
            string cardTransportNonce = new string('T', 43);
            alice.Verify(card.Address(), cardTransportNonce); card.Verify(alice.Address(), cardTransportNonce);
            Wait(delegate { return alice.Verified(cid, cardTransportNonce) && card.Verified(aid, cardTransportNonce); }, "card test peers authenticate transport before exercising mismatched consent", 100,
                delegate { return "Alice=" + alice.Diagnostic() + " Card=" + card.Diagnostic(); });
            Check(Friends(card).Count == 0 && Friends(alice).Count == 1, "card transport warmup does not grant friendship consent");
            alice.Pair(card.Address(), nonce); card.Pair(alice.Address(), new string('B', 43));
            Thread.Sleep(7000); Check(Friends(card).Count == 0 && Friends(alice).Count == 1, "mismatched card confirmation cannot create a friendship");
            card.Pair(alice.Address(), nonce);
            Wait(delegate { return Friends(card).Count == 1 && Friends(alice).Count == 2; }, "matching authenticated card confirmation adds both peers", 100,
                delegate { return "Alice=" + alice.Diagnostic() + " Card=" + card.Diagnostic(); });
            bob.Remove(aid);
            alice.Invite(bid);
            Thread.Sleep(6500); Check(Friends(bob).Count == 0 && Inbox(bob).Count == 0, "removed peer cannot inject a new invitation or reappear as a friend");
            MeshTestClient dana = Start(root, "Dana", native, helper, 5);
            bob.Request(dana.Address()); dana.Request(bob.Address());
            Wait(delegate { return Friends(bob).Count == 1 && Friends(dana).Count == 1; }, "simultaneous explicit friend-code requests converge to mutual acceptance", 100);
            string invalid = aCode.Substring(0, 74) + (aCode.EndsWith("FF") ? "00" : "FF");
            bool rejected = false; try { dana.Request(invalid); } catch { rejected = true; }
            Check(rejected && Friends(dana).Count == 1, "invalid checksum is rejected without changing accepted contacts");
            string danaCode = dana.Address();
            dana.Block(bCode);
            Check(Friends(dana).Count == 0 && JArray.Parse(dana.Blocked()).Count == 1, "client block removes accepted friend and returns blocked snapshot before reply");
            dana.Stop(); dana = Start(root, "Dana", native, helper, 5);
            Check(dana.Address() == danaCode && JArray.Parse(dana.Blocked()).Count == 1 && Friends(dana).Count == 0, "client block and peer identity persist through restart");
            rejected = false; try { dana.Request(bCode); } catch { rejected = true; }
            Check(rejected && Friends(dana).Count == 0, "blocked client cannot re-add by friend code");
            dana.Unblock(bid);
            Check(JArray.Parse(dana.Blocked()).Count == 0 && Friends(dana).Count == 0, "client unblock leaves friendship unaccepted");
            dana.Request(bCode);
            Wait(delegate { return Friends(dana).Count == 1 && Friends(bob).Count == 1; }, "fresh request after unblocking uses the other peer's existing accepted consent", 100);
            string damaged = Path.Combine(root, "Damaged", "UserData", "TavernMesh.json"); Directory.CreateDirectory(Path.GetDirectoryName(damaged));
            File.WriteAllText(damaged, "{\"Version\":2,\"Savedata\":\"invalid-base64\"}"); string original = File.ReadAllText(damaged);
            MeshTestClient failed = Start(root, "Damaged", native, helper, 0, false);
            Check(!failed.Ready() && File.ReadAllText(damaged) == original, "damaged saved identity fails safely and is never replaced by a fresh identity");
            Check(File.ReadAllText(Path.Combine(root, "Alice", "UserData", "TavernFriendCode.txt")).Contains(aCode), "desktop share file contains the public friend code");
            Console.WriteLine("Mesh integration: " + checks + " checks passed using real native cores and automatically started production helpers."); return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); foreach (MeshTestClient client in Clients) try { Console.Error.WriteLine(client.Error()); } catch { } return 1; }
        finally
        {
            foreach (MeshTestClient client in Clients) try { client.Stop(); } catch { }
            foreach (AppDomain domain in Domains) try { AppDomain.Unload(domain); } catch { }
            stopSeeds = true; if (seedThread != null) seedThread.Join(2000);
        }
    }
}
namespace TavernNativeMenu
{
    internal static class MeshSocialTransport { internal static void Initialize() { } internal static void Tick() { } }
    internal sealed class ServerEntry { public string Name, Host, Kind, Password; public int GamePort, AuthPort; public bool Private, Favorite; }
}
