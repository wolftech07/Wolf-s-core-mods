using System;
using TavernNativeMeshServer;

internal static class PairTests
{
    private static int assertions;
    private static void Check(bool condition, string name)
    { if (!condition) throw new Exception(name); assertions++; Console.WriteLine("PASS " + name); }
    private static string Address(int seed, int noSpam)
    {
        byte[] bytes = new byte[38];
        for (int i = 0; i < 32; i++) bytes[i] = (byte)(seed + i);
        bytes[32] = (byte)noSpam;
        for (int i = 0; i < 36; i += 2) { bytes[36] ^= bytes[i]; bytes[37] ^= bytes[i + 1]; }
        return BitConverter.ToString(bytes).Replace("-", "");
    }
    private static PairPeer Peer(int id)
    { return new PairPeer { Connection = new object(), NativeId = id, Name = "Player " + id, Address = Address(id, 0) }; }
    private static int Main()
    {
        try
        {
            DateTime now = new DateTime(2026, 9, 17, 12, 0, 0, DateTimeKind.Utc);
            var consent = new CardConsent();
            consent.Owner(1, 10, 20, now); consent.Owner(1, 10, 20, now);
            Check(!consent.Consume(1, 10, 20, now), "repeated owner packets cannot impersonate peer consent");
            consent.Owner(1, 10, 20, now); consent.Peer(1, 10, 20, now);
            Check(consent.Consume(1, 10, 20, now), "owner then peer reciprocal consent works");
            Check(!consent.Consume(1, 10, 20, now), "physical consent cannot be replayed");
            consent.Peer(2, 10, 20, now); consent.Owner(2, 10, 20, now);
            Check(consent.Consume(2, 10, 20, now), "peer then owner reciprocal consent works");
            consent.Peer(3, 10, 30, now); consent.Owner(3, 10, 20, now);
            Check(!consent.Consume(3, 10, 20, now), "mismatched peer rejected");
            consent.Peer(4, 10, 20, now); consent.Owner(4, 10, 20, now);
            Check(!consent.Consume(4, 10, 20, now.AddSeconds(3)), "expired physical consent rejected");
            consent.Peer(5, 10, 20, now); consent.Owner(5, 10, 20, now);
            Check(!consent.Consume(5, 10, 20, now.AddSeconds(-1)), "future physical consent rejected");
            string address = Address(30, 2);
            Check(AddressCodec.Normalize(address.ToLowerInvariant()) == address, "valid Tox address normalized");
            Check(AddressCodec.Normalize(address.Substring(0, 74) + "FF") == null, "invalid checksum rejected");
            Check(AddressCodec.Normalize(new string('0', 76)) == null, "zero public key rejected");
            Check(AddressCodec.Normalize(new string('Z', 76)) == null, "non hexadecimal address rejected");
            Check(AddressCodec.Normalize(null) == null && AddressCodec.Normalize("short") == null, "missing and short addresses rejected");

            var registry = new PairRegistry(); PairPeer left = Peer(10), right = Peer(20); PendingPair completed;
            Check(registry.Create(new PairPeer { Connection = new object(), NativeId = 1 }, right, null, now) == null, "null peer address rejected");
            PendingPair pair = registry.Create(left, right, new object(), now);
            Check(pair != null && pair.Nonce.Length == 43 && pair.Expires == now.AddSeconds(120), "pair gets random nonce and two minute lifetime");
            Check(registry.Confirm(new object(), pair.Nonce, now, out completed) == PairConfirmation.Rejected, "unrelated connection cannot acknowledge pair");
            Check(registry.Confirm(left.Connection, pair.Nonce, now, out completed) == PairConfirmation.Waiting && completed == null, "one participant cannot complete friendship");
            Check(registry.Confirm(left.Connection, pair.Nonce, now, out completed) == PairConfirmation.Waiting, "duplicate acknowledgement is not second participant");
            Check(registry.Create(left, Peer(40), null, now) == null, "one pending pair per participant");
            Check(registry.Confirm(right.Connection, pair.Nonce, now, out completed) == PairConfirmation.Complete && ReferenceEquals(pair, completed), "both original connections complete pair");
            Check(registry.Count == 0 && registry.Confirm(right.Connection, pair.Nonce, now, out completed) == PairConfirmation.Rejected, "completion consumes nonce once");
            pair = registry.Create(left, right, null, now);
            Check(registry.Confirm(left.Connection, pair.Nonce, now.AddSeconds(120), out completed) == PairConfirmation.Rejected, "acknowledgement at expiry rejected");
            Check(registry.Expire(now.AddSeconds(120)).Length == 1 && registry.Count == 0, "expired pair removed for cleanup");
            pair = registry.Create(left, right, null, now);
            Check(registry.RemovePeer(right.Connection).Length == 1 && registry.Count == 0, "disconnect removes pair");
            Check(registry.Confirm(left.Connection, pair.Nonce, now, out completed) == PairConfirmation.Rejected, "disconnected pair cannot complete");
            PairPeer sameKey = Peer(30); sameKey.Address = Address(10, 9);
            Check(registry.Create(left, sameKey, null, now) == null, "same public key with different nospam cannot friend itself");
            PairPeer sameNative = Peer(10);
            Check(registry.Create(left, sameNative, null, now) == null, "same native player cannot friend itself");
            string previous = null;
            for (int i = 0; i < PairRegistry.MaximumPairs; i++)
            {
                PairPeer a = Peer(i + 1000), b = Peer(i + 2000); b.Address = Address(i + 101, 0);
                PendingPair added = registry.Create(a, b, null, now);
                if (added == null || added.Nonce == previous) throw new Exception("capacity or nonce uniqueness failure");
                previous = added.Nonce;
            }
            Check(registry.Count == PairRegistry.MaximumPairs, "registry supports bounded maximum concurrent pairs");
            Check(registry.Create(Peer(4000), Peer(5000), null, now) == null, "excess pairs refused at capacity");
            Check(registry.RemoveAll().Length == PairRegistry.MaximumPairs && registry.Count == 0, "shutdown cleanup returns every pending pair");
            Console.WriteLine("Passed " + assertions + " mesh card checks."); return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
