using System;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace TavernNativeMenu
{
    internal static partial class MeshSocialTransport
    {
        private sealed class TabletCall
        {
            internal string Action;
            internal int Target;
            internal DateTime Deadline;
            internal TaskCompletionSource<JObject> Completion = new TaskCompletionSource<JObject>();
        }
        private static Binding tabletBinding;
        private static string serverKey;
        internal static string CurrentServerKey { get { return tabletBinding != null && Current(tabletBinding) ? serverKey : null; } }
        internal static string VerifiedAddress(int nativeId)
        {
            string value;
            return tabletBinding != null && Current(tabletBinding) && tabletBinding.Verified.TryGetValue(nativeId, out value) ? value : null;
        }
        // All connection access stays on the Unity thread. Task continuations
        // may originate in the helper reader, so callers always enter via Post.
        internal static async Task<JObject> TabletRequestAsync(string action, int targetId, string expectedServerKey = null)
        {
            long generation = System.Threading.Interlocked.Read(ref connectionGeneration);
            JObject result = await TabletSingleAsync(action, targetId, 0, 0, generation, expectedServerKey).ConfigureAwait(false);
            if (action != "roster") return result;
            int playerOffset = (int?)result["next_player_offset"] ?? -1, banOffset = (int?)result["next_ban_offset"] ?? -1;
            JArray players = (JArray)result["players"], bans = result["bans"] as JArray ?? new JArray();
            string scope = (string)result["server_key"];
            int pages = 0;
            while (playerOffset >= 0 || banOffset >= 0)
            {
                if (++pages > 100 || players.Count > 10000 || bans.Count > 10000) throw new InvalidOperationException("The server player list exceeds the tablet's display limit.");
                await Task.Delay(500).ConfigureAwait(false);
                JObject page = await TabletSingleAsync("roster", 0, Math.Max(0, playerOffset), Math.Max(0, banOffset), generation, scope).ConfigureAwait(false);
                if ((string)page["server_key"] != scope) throw new InvalidOperationException("The server changed while loading the tablet. Refresh its list.");
                if (playerOffset >= 0)
                {
                    foreach (JObject item in ((JArray)page["players"]).OfType<JObject>()) if (!players.OfType<JObject>().Any(x => (int?)x["id"] == (int?)item["id"])) players.Add(item.DeepClone());
                    int next = (int?)page["next_player_offset"] ?? -1;
                    if (next >= 0 && next <= playerOffset) throw new InvalidOperationException("The server returned an invalid roster page.");
                    playerOffset = next;
                }
                if (banOffset >= 0)
                {
                    foreach (JObject item in (page["bans"] as JArray ?? new JArray()).OfType<JObject>()) if (!bans.OfType<JObject>().Any(x => (int?)x["id"] == (int?)item["id"])) bans.Add(item.DeepClone());
                    int next = (int?)page["next_ban_offset"] ?? -1;
                    if (next >= 0 && next <= banOffset) throw new InvalidOperationException("The server returned an invalid bans page.");
                    banOffset = next;
                }
            }
            result["bans"] = bans;
            var completion = new TaskCompletionSource<JObject>();
            MeshSocialClient.Post(delegate
            {
                if (generation != System.Threading.Interlocked.Read(ref connectionGeneration) || tabletBinding == null || !Current(tabletBinding) || serverKey != scope) { completion.TrySetException(new InvalidOperationException("You left the server while loading the tablet.")); return; }
                foreach (int id in tabletBinding.Verified.Keys.ToArray())
                    if (!players.OfType<JObject>().Any(x => (int?)x["id"] == id && (string)x["address"] == tabletBinding.Verified[id])) tabletBinding.Verified.Remove(id);
                completion.TrySetResult(result);
            });
            return await completion.Task.ConfigureAwait(false);
        }
        private static Task<JObject> TabletSingleAsync(string action, int targetId, int playerOffset, int banOffset, long generation, string expectedServerKey)
        {
            var call = new TabletCall { Action = action, Target = targetId };
            MeshSocialClient.Post(delegate
            {
                try
                {
                    if (generation != System.Threading.Interlocked.Read(ref connectionGeneration) || expectedServerKey != null && expectedServerKey != CurrentServerKey) throw new InvalidOperationException("The server connection changed. Select the player again.");
                    if (!new[] { "roster", "verify", "kick", "ban", "unban" }.Contains(action)) throw new ArgumentException("Unknown tablet action.");
                    Binding binding = Bindings.Values.FirstOrDefault(x => Current(x) && x.MeshServer && x.Tablet);
                    if (binding == null) throw new InvalidOperationException("This server needs the updated tablet companion. Its host can install it using Setup's server support button.");
                    if (action != "roster" && targetId <= 0) throw new ArgumentException("Select a player first.");
                    if (action == "verify" && !binding.Bound) throw new InvalidOperationException("Your friends connection is still starting. Try again shortly.");
                    if (binding.Calls.Count >= 24) throw new InvalidOperationException("The tablet is busy. Wait for its current request.");
                    string id = Guid.NewGuid().ToString("N");
                    call.Deadline = DateTime.UtcNow.AddSeconds(action == "verify" ? 125 : 12);
                    binding.Calls.Add(id, call);
                    tabletBinding = binding;
                    try { Send(binding, new JObject { { "v", 2 }, { "kind", "tablet_request" }, { "request_id", id }, { "action", action }, { "target_id", targetId }, { "player_offset", playerOffset }, { "ban_offset", banOffset } }); }
                    catch { binding.Calls.Remove(id); throw; }
                }
                catch (Exception error) { call.Completion.TrySetException(error); }
            });
            return call.Completion.Task;
        }
        private static async void PrimeTablet(Binding binding)
        {
            if (!binding.Tablet || ReferenceEquals(tabletBinding, binding) && serverKey != null || binding.Calls.Values.Any(x => x.Action == "roster")) return;
            try { await TabletRequestAsync("roster", 0).ConfigureAwait(false); }
            catch (Exception) { /* The tablet reports request failures when opened. */ }
        }
        private static void EndTabletBinding(Binding binding)
        {
            foreach (TabletCall call in binding.Calls.Values.ToArray()) call.Completion.TrySetException(new InvalidOperationException("You left the server. Open the tablet again after joining."));
            binding.Calls.Clear(); binding.Proofs.Clear(); binding.Verified.Clear();
            if (ReferenceEquals(tabletBinding, binding)) { tabletBinding = null; serverKey = null; }
        }
        private static void TickTablet(Binding binding)
        {
            foreach (var item in binding.Calls.Where(x => x.Value.Deadline <= DateTime.UtcNow).ToArray())
            {
                binding.Calls.Remove(item.Key);
                item.Value.Completion.TrySetException(new TimeoutException(item.Value.Action == "verify" ? "The player's friends connection did not verify. Both players need the updated mod and a working peer connection." : "The server did not answer the tablet. Try refreshing it."));
            }
            foreach (Pair proof in binding.Proofs.Values.Where(x => x.Expires <= MeshSocialClient.Now).ToArray()) binding.Proofs.Remove(proof.Nonce);
        }
        private static bool HandleTablet(Binding binding, string kind, JObject packet)
        {
            if (!binding.Tablet) return false;
            if (kind == "mesh_verify") { if (binding.Bound) BeginVerification(binding, packet); return true; }
            if (kind != "tablet_reply") return false;
            string id = (string)packet["request_id"];
            TabletCall call;
            if (id == null || !binding.Calls.TryGetValue(id, out call)) return true;
            binding.Calls.Remove(id);
            try
            {
                if (call.Deadline <= DateTime.UtcNow) throw new TimeoutException("The tablet response arrived too late. Please try again.");
                if ((bool?)packet["ok"] != true) throw new InvalidOperationException(SafeReply((string)packet["message"] ?? "The server rejected that tablet action."));
                JObject data = packet["data"] as JObject ?? new JObject();
                if (call.Action == "verify")
                {
                    string nonce = (string)data["pair_nonce"], address = MeshSocialClient.ValidateAddress((string)data["address"]);
                    Pair proof;
                    if ((bool?)data["verified"] != true || (int?)data["id"] != call.Target || nonce == null ||
                        !binding.Proofs.TryGetValue(nonce, out proof) || proof.NativeId != call.Target || proof.Address != address || !proof.Acknowledged || proof.Expires <= MeshSocialClient.Now)
                        throw new InvalidOperationException("The player's identity was not confirmed by your peer connection.");
                    binding.Verified[call.Target] = address;
                    // Keep the proof until its short expiry so duplicate network
                    // confirmations cannot open a fresh unauthenticated mapping.
                }
                else if (call.Action == "roster")
                {
                    JArray players = data["players"] as JArray;
                    string scope = (string)data["server_key"];
                    if (players == null || players.Count > 512 || String.IsNullOrEmpty(scope) || scope.Length > 128) throw new InvalidOperationException("The server sent an invalid player list.");
                    if (serverKey != null && serverKey != scope) binding.Verified.Clear();
                    serverKey = scope; tabletBinding = binding;
                }
                call.Completion.TrySetResult((JObject)data.DeepClone());
            }
            catch (Exception error) { call.Completion.TrySetException(error); }
            return true;
        }
        private static string SafeReply(string value) { return new string(value.Where(c => !Char.IsControl(c) && c != '<' && c != '>').Take(240).ToArray()); }
        private static async void BeginVerification(Binding binding, JObject packet)
        {
            Pair proof = null;
            try
            {
                string address = MeshSocialClient.ValidateAddress((string)packet["address"]), nonce = (string)packet["pair_nonce"];
                int nativeId = (int?)packet["native_id"] ?? 0;
                long expires = (long?)packet["expires_unix"] ?? 0;
                string key = address.Substring(0, 64);
                if (!ValidNonce(nonce) || nativeId <= 0 || key == MeshSocialClient.SocialId || expires <= MeshSocialClient.Now || expires > MeshSocialClient.Now + 125 || binding.Proofs.Count >= 32 || binding.Proofs.ContainsKey(nonce)) return;
                proof = new Pair { Nonce = nonce, Address = address, Key = key, NativeId = nativeId, Name = (string)packet["name"], Expires = expires };
                binding.Proofs.Add(nonce, proof);
                await MeshSocialClient.VerifyPeerAsync(address, nonce, expires).ConfigureAwait(false);
            }
            catch (Exception error)
            {
                Pair failed = proof;
                MeshSocialClient.Post(delegate { if (Current(binding) && failed != null) binding.Proofs.Remove(failed.Nonce); MeshSocialClient.SetError(error); });
            }
        }
        private static void PeerVerified(string key, string nonce)
        {
            foreach (Binding binding in Bindings.Values.ToArray())
            {
                Pair proof;
                if (!Current(binding) || nonce == null || !binding.Proofs.TryGetValue(nonce, out proof) || proof.Key != key || proof.Expires <= MeshSocialClient.Now || proof.Acknowledged) continue;
                // Set first: a synchronous loopback or test transport may reply
                // immediately while Send is still on the stack.
                proof.Acknowledged = true;
                Send(binding, new JObject { { "v", 2 }, { "kind", "mesh_verify_confirm" }, { "pair_nonce", nonce } });
            }
        }
    }
}
