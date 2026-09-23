using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;

namespace TavernNativeMeshServer
{
    // This registry records physical consent, not global friendship. Clients must
    // verify the same nonce over their authenticated peer transport before ACKing.
    internal sealed class PairPeer
    {
        internal object Connection;
        internal int NativeId;
        internal string Address, Name;
    }
    internal sealed class PendingPair
    {
        internal string Nonce;
        internal PairPeer Left, Right;
        internal DateTime Expires;
        internal object Tag;
        internal bool LeftConfirmed, RightConfirmed;
    }
    internal enum PairConfirmation { Rejected, Waiting, Complete }
    internal sealed class PairRegistry
    {
        internal const int MaximumPairs = 128;
        private readonly Dictionary<string, PendingPair> pairs = new Dictionary<string, PendingPair>(StringComparer.Ordinal);
        internal int Count { get { return pairs.Count; } }
        internal PendingPair Create(PairPeer left, PairPeer right, object tag, DateTime now)
        {
            if (!Valid(left) || !Valid(right) || ReferenceEquals(left.Connection, right.Connection) || left.NativeId == right.NativeId || left.Address.Substring(0, 64) == right.Address.Substring(0, 64) || pairs.Count >= MaximumPairs)
                return null;
            if (pairs.Values.Any(x => HasPeer(x, left.Connection) || HasPeer(x, right.Connection))) return null;
            string nonce;
            using (var random = RandomNumberGenerator.Create())
            {
                byte[] bytes = new byte[32];
                do { random.GetBytes(bytes); nonce = Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_'); }
                while (pairs.ContainsKey(nonce));
            }
            var pair = new PendingPair { Nonce = nonce, Left = left, Right = right, Tag = tag, Expires = now.AddSeconds(120) };
            pairs.Add(nonce, pair); return pair;
        }
        private static bool Valid(PairPeer peer)
        { return peer != null && peer.Connection != null && peer.NativeId > 0 && peer.Address != null && AddressCodec.Normalize(peer.Address) == peer.Address; }
        private static bool HasPeer(PendingPair pair, object connection)
        { return ReferenceEquals(pair.Left.Connection, connection) || ReferenceEquals(pair.Right.Connection, connection); }
        internal PairConfirmation Confirm(object connection, string nonce, DateTime now, out PendingPair pair)
        {
            pair = null; PendingPair candidate;
            if (String.IsNullOrEmpty(nonce) || nonce.Length != 43 || !pairs.TryGetValue(nonce, out candidate) || now >= candidate.Expires) return PairConfirmation.Rejected;
            if (ReferenceEquals(connection, candidate.Left.Connection)) candidate.LeftConfirmed = true;
            else if (ReferenceEquals(connection, candidate.Right.Connection)) candidate.RightConfirmed = true;
            else return PairConfirmation.Rejected;
            if (!candidate.LeftConfirmed || !candidate.RightConfirmed) return PairConfirmation.Waiting;
            pairs.Remove(nonce); pair = candidate; return PairConfirmation.Complete;
        }
        internal PendingPair[] RemovePeer(object connection)
        { return RemoveWhere(x => HasPeer(x, connection)); }
        internal PendingPair[] Expire(DateTime now)
        { return RemoveWhere(x => now >= x.Expires); }
        internal PendingPair[] RemoveAll()
        { return RemoveWhere(x => true); }
        private PendingPair[] RemoveWhere(Func<PendingPair, bool> predicate)
        {
            PendingPair[] removed = pairs.Values.Where(predicate).ToArray();
            foreach (PendingPair pair in removed) pairs.Remove(pair.Nonce);
            return removed;
        }
    }
    internal static class AddressCodec
    {
        internal static string Normalize(string address)
        {
            if (address == null || address.Length != 76) return null;
            byte[] bytes = new byte[38]; int nonzero = 0;
            for (int i = 0; i < bytes.Length; i++)
            {
                int high = Hex(address[2 * i]), low = Hex(address[2 * i + 1]);
                if (high < 0 || low < 0) return null;
                bytes[i] = (byte)((high << 4) | low); if (i < 32) nonzero |= bytes[i];
            }
            byte even = 0, odd = 0;
            for (int i = 0; i < 36; i += 2) { even ^= bytes[i]; odd ^= bytes[i + 1]; }
            return nonzero == 0 || bytes[36] != even || bytes[37] != odd ? null : address.ToUpperInvariant();
        }
        private static int Hex(char value)
        {
            if (value >= '0' && value <= '9') return value - '0';
            if (value >= 'a' && value <= 'f') return value - 'a' + 10;
            if (value >= 'A' && value <= 'F') return value - 'A' + 10;
            return -1;
        }
    }
}
