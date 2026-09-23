using System;
using System.IO;
using System.Reflection;
using TavernNativeMeshServer;

internal static class TabletPolicyTests
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
    private static bool Admitted(TabletStore store, int requestId, int claimId, string name, string token)
    { return TabletPolicy.AllowCredentials(requestId, claimId, "alice", name, "server-held-token", token, store.IsBanned(claimId)); }
    private static void CloneIsIndependent(string managed)
    {
        AppDomain.CurrentDomain.AssemblyResolve += delegate(object sender, ResolveEventArgs eventArgs)
        {
            string candidate = Path.Combine(managed, new AssemblyName(eventArgs.Name).Name + ".dll");
            return File.Exists(candidate) ? Assembly.Load(File.ReadAllBytes(candidate)) : null;
        };
        Assembly serialization = Assembly.Load(File.ReadAllBytes(Path.Combine(managed, "Alta.Serialization.Runtime.dll")));
        Type writerType = serialization.GetType("Alta.Serialization.StreamWriter", true);
        Type readerType = serialization.GetType("Alta.Serialization.StreamReader", true);
        using (var writer = (IDisposable)Activator.CreateInstance(writerType, new object[] { new uint[32] }))
        {
            MethodInfo write = writerType.GetMethod("SerializeBits", new[] { typeof(uint).MakeByRefType(), typeof(int) });
            write.Invoke(writer, new object[] { (uint)17, 5 });
            write.Invoke(writer, new object[] { (uint)12345678, 31 });
            uint count = (uint)writerType.GetMethod("AlignAndFlush").Invoke(writer, null);
            uint[] data = (uint[])writerType.GetProperty("Data").GetValue(writer, null);
            using (var reader = (IDisposable)Activator.CreateInstance(readerType, new object[] { data, (int)count }))
            {
                MethodInfo read = readerType.GetMethod("SerializeBits", new[] { typeof(uint).MakeByRefType(), typeof(int) });
                object[] prefix = { (uint)0, 5 }; read.Invoke(reader, prefix);
                Check((uint)prefix[0] == 17, "real game stream prefix read");
                using (var clone = (IDisposable)readerType.GetMethod("Clone").Invoke(reader, null))
                {
                    object[] copied = { (uint)0, 31 }; read.Invoke(clone, copied);
                    Check((uint)copied[0] == 12345678, "clone begins at original reader bit offset");
                    Check((uint)readerType.GetProperty("BitIndex").GetValue(reader, null) == 5, "clone consumption does not advance original reader");
                }
                object[] original = { (uint)0, 31 }; read.Invoke(reader, original);
                Check((uint)original[0] == 12345678, "disposing clone leaves original join reader usable");
            }
        }
    }
    private static int Main(string[] args)
    {
        string scratch = Path.Combine(Path.GetTempPath(), "tavern-tablet-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            Check(TabletPolicy.Rank(new[] { "OWNER" }) == 2 && TabletPolicy.Rank(new[] { "moderator" }) == 1, "server owner and moderator roles recognized case insensitively");
            Check(TabletPolicy.Rank(new[] { "admin", "server_owner", "database_admin" }) == 0, "unknown roles and offline JWT policy names grant no moderation");
            Check(TabletPolicy.Rank(null) == 0, "missing roles grant no permission");
            Check(!TabletPolicy.CanTarget(1, 0, 2, 0), "ordinary player cannot moderate");
            Check(TabletPolicy.CanTarget(1, 1, 2, 0), "moderator can target ordinary player");
            Check(!TabletPolicy.CanTarget(1, 1, 2, 1), "moderator cannot target equal role");
            Check(!TabletPolicy.CanTarget(1, 1, 2, 2) && !TabletPolicy.CanTarget(1, 2, 2, 2), "owners protected from all moderation");
            Check(TabletPolicy.CanTarget(1, 2, 2, 1), "owner can target moderator");
            Check(!TabletPolicy.CanTarget(1, 2, 1, 0) && !TabletPolicy.CanTarget(1, 2, 0, 0), "self and invalid identifiers protected");
            Check(TabletPolicy.ValidRequestId(Guid.NewGuid().ToString("N")) && !TabletPolicy.ValidRequestId("bad") && !TabletPolicy.ValidRequestId(null), "request IDs require GUID32 format");
            Directory.CreateDirectory(scratch); string path = Path.Combine(scratch, "state.json");
            var store = new TabletStore(path); string key = store.ServerKey;
            Check(Admitted(store, 2, 2, "ALICE", "server-held-token"), "valid server-held credential accepted");
            Check(!Admitted(store, 3, 2, "alice", "server-held-token"), "token cannot authenticate a different requested player identifier");
            Check(!Admitted(store, 2, 2, "owner", "server-held-token") && !Admitted(store, 2, 2, "alice", "forged"), "name impersonation and token forgery rejected");
            store.Ban(2, "Alice", 1);
            Check(!Admitted(store, 2, 2, "alice", "server-held-token"), "banned player cannot rejoin using their previously valid credential");
            var restarted = new TabletStore(path);
            Check(restarted.ServerKey == key && restarted.IsBanned(2), "server key and ban survive process restart");
            Check(!Admitted(restarted, 2, 2, "alice", "server-held-token"), "restarted server rejects reused banned credential");
            Check(restarted.Unban(2) && !restarted.Unban(2), "unban reports whether persisted record existed");
            Check(Admitted(new TabletStore(path), 2, 2, "alice", "server-held-token"), "unban persists and permits the authenticated account");
            File.Delete(path); Directory.CreateDirectory(path); bool failed = false;
            try { restarted.Ban(3, "Bob", 1); } catch (IOException) { failed = true; }
            Check(failed && !restarted.IsBanned(3), "failed durable write does not publish a successful ban");
            Directory.Delete(path); File.WriteAllText(path, "malformed"); failed = false;
            try { new TabletStore(path); } catch { failed = true; }
            Check(failed && File.ReadAllText(path) == "malformed", "corrupt moderation state fails closed without erasing bans");
            var proofRegistry = new PairRegistry(); var cardRegistry = new PairRegistry();
            var left = new PairPeer { Connection = new object(), NativeId = 1, Name = "A", Address = Address(1) };
            var right = new PairPeer { Connection = new object(), NativeId = 2, Name = "B", Address = Address(2) };
            DateTime now = DateTime.UtcNow; PendingPair complete;
            PendingPair proof = proofRegistry.Create(left, right, "verify", now);
            Check(proof != null && cardRegistry.Count == 0, "identity proofs are independent of friendship-card registry");
            Check(proofRegistry.Confirm(new object(), proof.Nonce, now, out complete) == PairConfirmation.Rejected, "unrelated connection cannot acknowledge identity proof");
            Check(proofRegistry.Confirm(left.Connection, proof.Nonce, now, out complete) == PairConfirmation.Waiting, "one identity acknowledgement is insufficient");
            Check(proofRegistry.Confirm(left.Connection, proof.Nonce, now, out complete) == PairConfirmation.Waiting, "replayed acknowledgement cannot stand for the other player");
            Check(proofRegistry.Confirm(right.Connection, proof.Nonce, now, out complete) == PairConfirmation.Complete && cardRegistry.Count == 0, "both exact sessions complete proof without friendship mutation");
            proof = proofRegistry.Create(left, right, "verify", now);
            Check(proofRegistry.RemovePeer(right.Connection).Length == 1 && proofRegistry.Confirm(left.Connection, proof.Nonce, now, out complete) == PairConfirmation.Rejected, "disconnect cancels pending identity proof");
            proofRegistry.Create(left, right, "verify", now);
            Check(proofRegistry.Expire(now.AddSeconds(120)).Length == 1 && proofRegistry.Count == 0, "identity proof expires within bounded lifetime");
            CloneIsIndependent(args[0]);
            Console.WriteLine("PASS " + checks + " tablet authority, durable ban, proof and stream checks."); return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally { if (Directory.Exists(scratch)) Directory.Delete(scratch, true); }
    }
}
