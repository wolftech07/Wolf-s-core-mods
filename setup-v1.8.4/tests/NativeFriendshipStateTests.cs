using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Alta.Api.DataTransferModels.Models.Responses;
using Alta.Networking.Scripts.Player;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using TavernNativeMenu;

internal static class NativeFriendshipStateTests
{
    private static int checks;
    private static void Check(bool okay, string name)
    { if (!okay) throw new Exception(name); checks++; Console.WriteLine("PASS " + name); }
    private static string Code(char value) { return new string(value, 72) + "0000"; }
    private static void Accept(string address)
    { MeshSocialClient.Friends.Add(new JObject { { "social_id", address.Substring(0, 64) }, { "name", "Friend" } }); MeshSocialClient.Change(); NativeFriendshipState.Tick(); }
    private static void Sync() { MeshSocialClient.Change(); NativeFriendshipState.Tick(); }
    private static int Main()
    {
        var harmony = new Harmony("tavern.native-friendship-fixture");
        try
        {
            var local = new Player(10, "Local"); var peer = new Player(20, "Peer"); var pending = new Player(30, "Pending"); var unverified = new Player(40, "Unverified"); Player.Current = local;
            NativeFriendshipState.Install(harmony);
            peer.FriendshipManager.UpdateFromNetwork(local);
            local.FriendshipManager.GetFriends(true).GetAwaiter().GetResult();
            Check(peer.FriendshipManager.NetworkFetches == 1 && local.FriendshipManager.FriendsFetches == 1, "unsupported server retains original native refresh behavior");
            MeshSocialTransport.CurrentServerKey = "serverA";
            MeshSocialTransport.Proven[20] = Code('A'); MeshSocialTransport.Proven[30] = Code('B');
            Accept(Code('A'));
            Check(peer.FriendshipManager.IsFriend && local.FriendshipManager.IsFriendsWith(20), "verified player with an accepted peer key becomes a native friend");
            Check(local.FriendshipManager.Friends.Count(x => x.Identifier == 20) == 1, "projection creates one native friend row");
            peer.FriendshipManager.UpdateFromNetwork(local);
            Check(peer.FriendshipManager.NetworkFetches == 1, "verified player refresh skips the official social API");
            peer.FriendshipManager.UpdateStatus(false);
            local.FriendshipManager.UpdateStatusWithPlayer(false, 20);
            Check(peer.FriendshipManager.IsFriend && local.FriendshipManager.IsFriendsWith(20), "late official false status cannot erase verified accepted friendship");
            local.FriendshipManager.UpdateStatusWithPlayer(true, 20);
            local.FriendshipManager.GetFriends(true).GetAwaiter().GetResult();
            Check(local.FriendshipManager.FriendsFetches == 1 && local.FriendshipManager.Friends.Count(x => x.Identifier == 20) == 1, "native bulk refresh uses peer projection and does not duplicate accepted rows");
            pending.FriendshipManager.UpdateStatus(true);
            local.FriendshipManager.UpdateStatusWithPlayer(true, 30);
            Check(!pending.FriendshipManager.IsFriend && !local.FriendshipManager.IsFriendsWith(30), "identity verification alone never supplies friend consent");
            Accept(Code('C'));
            Check(!unverified.FriendshipManager.IsFriend && !local.FriendshipManager.IsFriendsWith(40), "accepted peer graph cannot infer an unverified native player identity");
            MeshSocialClient.Configured = false; Sync();
            Check(peer.FriendshipManager.IsFriend && local.FriendshipManager.IsFriendsWith(20), "temporary helper outage retains cached accepted friendship");
            MeshSocialClient.Configured = true;
            local.FriendshipManager.LateBulkClear(); peer.FriendshipManager.UpdateStatus(false);
            Check(peer.FriendshipManager.IsFriend && !local.FriendshipManager.IsFriendsWith(20), "late pre-verification bulk request can clear local list but cannot erase guarded remote status");
            Sync();
            Check(local.FriendshipManager.IsFriendsWith(20) && local.FriendshipManager.Friends.Count(x => x.Identifier == 20) == 1, "next sync repairs a bulk request that began before verification");
            MeshSocialClient.Friends.Clear(); Sync();
            Check(!peer.FriendshipManager.IsFriend && !local.FriendshipManager.IsFriendsWith(20), "unfriending clears both native projections");
            Accept(Code('A'));
            MeshSocialTransport.Proven.Remove(20); Sync();
            Check(!peer.FriendshipManager.IsFriend && !local.FriendshipManager.IsFriendsWith(20), "lost current-server proof clears native projection");
            MeshSocialTransport.Proven[20] = Code('A'); Sync();
            Check(peer.FriendshipManager.IsFriend, "fresh current-server proof restores an accepted contact");
            MeshSocialTransport.CurrentServerKey = "serverB"; MeshSocialTransport.Proven[20] = Code('B'); Sync();
            Check(!peer.FriendshipManager.IsFriend && !local.FriendshipManager.IsFriendsWith(20), "reused native player number cannot inherit another server's accepted key");
            Accept(Code('B'));
            Check(peer.FriendshipManager.IsFriend && local.FriendshipManager.Friends.Count(x => x.Identifier == 20) == 1, "new scoped identity needs its own accepted peer contact");
            MeshSocialTransport.CurrentServerKey = null; MeshSocialTransport.Proven.Clear(); Sync();
            Check(!peer.FriendshipManager.IsFriend && !local.FriendshipManager.IsFriendsWith(20), "disconnect clears native server-local friendship without deleting global contacts");
            Check(MeshSocialClient.Friends.Count == 2, "server disconnect preserves accepted peer contacts");
            MeshSocialTransport.CurrentServerKey = "serverC"; MeshSocialTransport.Proven[20] = Code('A'); NetworkSceneManager.IsServer = true;
            peer.FriendshipManager.UpdateFromNetwork(local); local.FriendshipManager.GetFriends(true).GetAwaiter().GetResult(); Sync();
            Check(peer.FriendshipManager.NetworkFetches == 2 && local.FriendshipManager.FriendsFetches == 2 && !local.FriendshipManager.IsFriendsWith(20), "server process retains original authority and is never projected from a client graph");
            Console.WriteLine("Passed " + checks + " native friendship state checks using real Harmony patches."); return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        finally { NativeFriendshipState.Shutdown(); harmony.UnpatchSelf(); }
    }
}
namespace TavernNativeMenu
{
    internal static class MeshSocialTransport
    {
        internal static string CurrentServerKey;
        internal static readonly Dictionary<int, string> Proven = new Dictionary<int, string>();
        internal static int[] VerifiedPlayerIds { get { return Proven.Keys.ToArray(); } }
        internal static string VerifiedAddress(int id) { string value; return Proven.TryGetValue(id, out value) ? value : null; }
    }
    internal static class MeshSocialClient
    {
        internal static bool Configured = true;
        internal static JArray Friends = new JArray();
        internal static event Action Changed;
        internal static Task<JArray> GetFriendsAsync() { return Task.FromResult((JArray)Friends.DeepClone()); }
        internal static void Change() { if (Changed != null) Changed(); }
    }
}
namespace Alta.Api.DataTransferModels.Models.Responses
{
    public enum FriendshipType { Accepted }
    public sealed class FriendshipInfo : UserInfo { public FriendshipType Type; public DateTime CreatedAt; }
}
namespace Alta.Networking.Scripts.Player
{
    public interface IPlayer { UserInfo UserInfo { get; } FriendshipManager FriendshipManager { get; } }
}
public class UserInfo { public int Identifier; public string Username; }
public static class NetworkSceneManager { public static bool IsServer; }
public sealed class Player : IPlayer
{
    public static IPlayer Current;
    private static readonly Dictionary<int, Player> Players = new Dictionary<int, Player>();
    public UserInfo UserInfo { get; private set; }
    public FriendshipManager FriendshipManager { get; private set; }
    public Player(int id, string name) { UserInfo = new UserInfo { Identifier = id, Username = name }; FriendshipManager = new FriendshipManager(id); Players[id] = this; }
    public static bool SafeGetPlayer(int id, out Player player) { return Players.TryGetValue(id, out player); }
}
public sealed class FriendshipManager
{
    private readonly int player;
    private readonly HashSet<int> friendsIDs = new HashSet<int>();
    public readonly List<UserInfo> Friends = new List<UserInfo>();
    public bool IsFriend;
    public int NetworkFetches, FriendsFetches;
    public FriendshipManager(int id) { player = id; }
    public bool IsFriendsWith(int id) { return friendsIDs.Contains(id); }
    public void AddFriend(FriendshipInfo info) { friendsIDs.Add(info.Identifier); Friends.Add(info); }
    [MethodImpl(MethodImplOptions.NoInlining)]
    public void UpdateFromNetwork(IPlayer player) { NetworkFetches++; UpdateStatus(false); player.FriendshipManager.UpdateStatusWithPlayer(false, this.player); }
    [MethodImpl(MethodImplOptions.NoInlining)]
    public void UpdateStatus(bool isFriend) { IsFriend = isFriend; }
    [MethodImpl(MethodImplOptions.NoInlining)]
    public void UpdateStatusWithPlayer(bool isFriend, int playerId)
    {
        if (isFriend) { friendsIDs.Add(playerId); Friends.Add(new UserInfo { Identifier = playerId }); }
        else { friendsIDs.Remove(playerId); int index = Friends.FindIndex(x => x.Identifier == playerId); if (index >= 0) Friends.RemoveAt(index); }
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    public Task GetFriends(bool isLocalPlayer) { FriendsFetches++; LateBulkClear(); return Task.FromResult(0); }
    public void LateBulkClear() { friendsIDs.Clear(); Friends.Clear(); }
}
