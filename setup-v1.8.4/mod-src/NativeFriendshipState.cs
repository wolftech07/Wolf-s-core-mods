using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Alta.Api.DataTransferModels.Models.Responses;
using Alta.Networking.Scripts.Player;
using HarmonyLib;
using Newtonsoft.Json.Linq;

namespace TavernNativeMenu
{
    // The native friendship manager is a projection of accepted peer contacts.
    // A server-local number alone never establishes a friendship: its current
    // server mapping must already have passed the authenticated peer proof.
    internal static class NativeFriendshipState
    {
        private sealed class Projection
        {
            internal string Scope, Key;
            internal FriendshipManager Local, Remote;
        }
        private static readonly Dictionary<int, Projection> projections = new Dictionary<int, Projection>();
        private static readonly FieldInfo ManagerPlayer = AccessTools.Field(typeof(FriendshipManager), "player");
        private static bool dirty = true;
        private static DateTime nextSync;

        internal static void Install(HarmonyLib.Harmony harmony)
        {
            if (ManagerPlayer == null) throw new MissingFieldException(typeof(FriendshipManager).FullName, "player");
            Patch(harmony, "UpdateFromNetwork", "BeforeNetworkRefresh");
            Patch(harmony, "UpdateStatus", "BeforeStatus");
            Patch(harmony, "UpdateStatusWithPlayer", "BeforeLocalStatus");
            Patch(harmony, "GetFriends", "BeforeFriendsRefresh");
            MeshSocialClient.Changed += Changed;
        }
        private static void Patch(HarmonyLib.Harmony harmony, string method, string prefix)
        {
            MethodInfo original = AccessTools.Method(typeof(FriendshipManager), method);
            if (original == null) throw new MissingMethodException(typeof(FriendshipManager).FullName, method);
            harmony.Patch(original, new HarmonyMethod(typeof(NativeFriendshipState), prefix));
        }
        private static void Changed() { dirty = true; }
        internal static void Shutdown()
        {
            MeshSocialClient.Changed -= Changed;
            projections.Clear(); dirty = true; nextSync = DateTime.MinValue;
        }
        internal static void Tick()
        {
            if (!dirty && DateTime.UtcNow < nextSync) return;
            dirty = false; nextSync = DateTime.UtcNow.AddSeconds(1);
            Synchronize();
        }
        private static bool ClientScope()
        { return !NetworkSceneManager.IsServer && Player.Current != null && !String.IsNullOrEmpty(MeshSocialTransport.CurrentServerKey); }
        private static string VerifiedKey(int id)
        {
            if (!ClientScope() || id <= 0 || id == Player.Current.UserInfo.Identifier) return null;
            string address = MeshSocialTransport.VerifiedAddress(id);
            return String.IsNullOrEmpty(address) || address.Length != 76 ? null : address.Substring(0, 64);
        }
        private static HashSet<string> AcceptedKeys()
        {
            // This API only copies the helper's cached public snapshot. It does
            // no I/O or waiting, including during a temporary helper outage.
            return new HashSet<string>(MeshSocialClient.GetFriendsAsync().GetAwaiter().GetResult().OfType<JObject>()
                .Select(x => (string)x["social_id"]).Where(x => !String.IsNullOrEmpty(x)), StringComparer.Ordinal);
        }
        private static bool Resolve(int id, out bool accepted)
        {
            string key = VerifiedKey(id); accepted = false;
            if (key == null) return false;
            accepted = AcceptedKeys().Contains(key); return true;
        }
        private static bool Resolve(FriendshipManager manager, out int id, out bool accepted)
        {
            id = (int)ManagerPlayer.GetValue(manager); accepted = false;
            Player remote;
            return Player.SafeGetPlayer(id, out remote) && ReferenceEquals(remote.FriendshipManager, manager) && Resolve(id, out accepted);
        }
        private static bool BeforeNetworkRefresh(FriendshipManager __instance, IPlayer player)
        {
            int id; bool accepted;
            if (!ReferenceEquals(player, Player.Current) || !Resolve(__instance, out id, out accepted)) return true;
            Apply(Player.Current.FriendshipManager, __instance, id, accepted);
            return false;
        }
        private static void BeforeStatus(FriendshipManager __instance, ref bool isFriend)
        {
            int id; bool accepted;
            // Also guards an official async request that began before the peer
            // mapping was established and completes after the card exchange.
            if (Resolve(__instance, out id, out accepted)) isFriend = accepted;
        }
        private static bool BeforeLocalStatus(FriendshipManager __instance, ref bool isFriend, int playerId)
        {
            bool accepted;
            if (Player.Current == null || !ReferenceEquals(__instance, Player.Current.FriendshipManager) || !Resolve(playerId, out accepted)) return true;
            isFriend = accepted;
            // Native UpdateStatusWithPlayer appends a duplicate row on every
            // true update. Keep its identifier/list projection idempotent.
            return !accepted || !__instance.IsFriendsWith(playerId);
        }
        private static bool BeforeFriendsRefresh(FriendshipManager __instance, bool isLocalPlayer, ref Task __result)
        {
            if (!isLocalPlayer || !ClientScope() || !ReferenceEquals(__instance, Player.Current.FriendshipManager)) return true;
            Synchronize(); __result = Task.FromResult(0); return false;
        }
        private static void Apply(FriendshipManager local, FriendshipManager remote, int id, bool accepted)
        {
            if (accepted)
            {
                if (!local.IsFriendsWith(id))
                {
                    Player player;
                    if (Player.SafeGetPlayer(id, out player))
                        local.AddFriend(new FriendshipInfo { Identifier = id, Username = player.UserInfo.Username,
                            Type = FriendshipType.Accepted, CreatedAt = DateTime.UtcNow });
                }
            }
            else
            {
                local.UpdateStatusWithPlayer(false, id);
                local.Friends.RemoveAll(x => x.Identifier == id);
            }
            if (remote != null) remote.UpdateStatus(accepted);
        }
        private static void Synchronize()
        {
            string scope = ClientScope() ? MeshSocialTransport.CurrentServerKey : null;
            int[] ids = scope == null ? new int[0] : MeshSocialTransport.VerifiedPlayerIds;
            foreach (var item in projections.ToArray())
            {
                Projection previous = item.Value; Player remote;
                if (previous.Scope == scope && ids.Contains(item.Key) && VerifiedKey(item.Key) == previous.Key &&
                    Player.Current != null && ReferenceEquals(previous.Local, Player.Current.FriendshipManager) &&
                    Player.SafeGetPlayer(item.Key, out remote) && ReferenceEquals(previous.Remote, remote.FriendshipManager)) continue;
                // A new verified mapping can reuse a native player number. Its
                // current graph state is applied below; clearing through the
                // patched methods here would fight that newer projection.
                bool currentMapping = VerifiedKey(item.Key) != null;
                if (Player.Current == null || !ReferenceEquals(previous.Local, Player.Current.FriendshipManager) || !currentMapping)
                {
                    previous.Local.UpdateStatusWithPlayer(false, item.Key);
                    previous.Local.Friends.RemoveAll(x => x.Identifier == item.Key);
                }
                if (!currentMapping && Player.SafeGetPlayer(item.Key, out remote) && ReferenceEquals(previous.Remote, remote.FriendshipManager)) previous.Remote.UpdateStatus(false);
                projections.Remove(item.Key);
            }
            if (scope == null) return;
            HashSet<string> accepted = AcceptedKeys();
            foreach (int id in ids)
            {
                string key = VerifiedKey(id); Player remote;
                if (key == null || !Player.SafeGetPlayer(id, out remote)) continue;
                projections[id] = new Projection { Scope = scope, Key = key, Local = Player.Current.FriendshipManager, Remote = remote.FriendshipManager };
                Apply(Player.Current.FriendshipManager, remote.FriendshipManager, id, accepted.Contains(key));
            }
        }
    }
}
