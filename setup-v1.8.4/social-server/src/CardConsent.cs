using System;
using System.Collections.Generic;
using System.Linq;

namespace TavernNativeSocial
{
    // The native owner callback can be repeated without a peer callback.
    // Require both authenticated callback paths before accepting a card.
    internal sealed class CardConsent
    {
        private sealed class Proof
        {
            internal int Owner, Target = -1, Peer = -1;
            internal DateTime OwnerAt = DateTime.MinValue, PeerAt = DateTime.MinValue;
        }
        private readonly Dictionary<int, Proof> proofs = new Dictionary<int, Proof>();
        internal void Owner(int card, int owner, int target, DateTime now)
        { Proof proof = Get(card, owner, now); proof.Target = target; proof.OwnerAt = now; }
        internal void Peer(int card, int owner, int peer, DateTime now)
        { Proof proof = Get(card, owner, now); proof.Peer = peer; proof.PeerAt = now; }
        private Proof Get(int card, int owner, DateTime now)
        {
            foreach (int stale in proofs.Where(x => (now - x.Value.OwnerAt).TotalSeconds > 2 && (now - x.Value.PeerAt).TotalSeconds > 2).Select(x => x.Key).ToArray()) proofs.Remove(stale);
            Proof proof;
            if (!proofs.TryGetValue(card, out proof) || proof.Owner != owner) proofs[card] = proof = new Proof { Owner = owner };
            return proof;
        }
        internal bool Consume(int card, int owner, int target, DateTime now)
        {
            Proof proof;
            if (!proofs.TryGetValue(card, out proof)) return false;
            proofs.Remove(card);
            return target > 0 && target != owner && proof.Owner == owner && proof.Target == target && proof.Peer == target &&
                now >= proof.OwnerAt && now >= proof.PeerAt && (now - proof.OwnerAt).TotalSeconds <= 2 && (now - proof.PeerAt).TotalSeconds <= 2;
        }
    }
}
