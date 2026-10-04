using System;
using System.Reflection;
using TavernNativeMenu;
internal static class FriendCardPickupTests
{
    private static int checks;
    private static bool Grab(int owner, bool local)
    {
        return (bool)typeof(NativeFriendCard).GetMethod("BeforeGrabbed", BindingFlags.Static | BindingFlags.NonPublic)
            .Invoke(null, new object[] { new FriendRequestToken { Owner = owner }, new Interactor { IsLocal = local,
                Controller = new Controller { PlayerController = new PlayerController { NetworkPlayer = new Player { UserInfo = new UserInfo { Identifier = 20 } } } } } });
    }
    private static void Check(bool result, string name)
    { if (!result) throw new Exception(name); checks++; Console.WriteLine("PASS " + name); }
    public static int Main()
    {
        try
        {
            Player.Current = new Player { UserInfo = new UserInfo { Identifier = 10 } };
            MeshSocialTransport.CardUnavailableReason = "Ask the host to install server support.";
            Check(Grab(10, true), "owner pickup keeps native behavior");
            Check(Grab(20, false), "unrelated remote pickup keeps native behavior");
            Check(!Grab(20, true), "unsupported recipient cannot set optimistic friendship");
            Check(MeshSocialClient.Error == MeshSocialTransport.CardUnavailableReason && MeshSocialTransport.NotifiedId == 20,
                "unsupported handoff reports visible reason for correct player");
            Check(!Grab(10, false) && MeshSocialTransport.NotifiedId == 20,
                "unsupported server also warns the modded card owner when another player takes it");
            MeshSocialTransport.CardUnavailableReason = "Friends helper is starting.";
            Check(!Grab(20, true) && MeshSocialClient.Error.Contains("starting"), "starting helper leaves card retryable");
            MeshSocialTransport.CardUnavailableReason = null;
            Check(Grab(20, true), "ready recipient preserves native handoff packets");
            Check(Grab(10, false), "ready card owner preserves native handoff packets");
            Player.Current = null;
            Check(Grab(20, true), "pickup outside a local player leaves native behavior alone");
            Console.WriteLine("Passed " + checks + " friend-card pickup checks."); return 0;
        }
        catch (Exception error) { Console.WriteLine(error); return 1; }
    }
}
public class FriendRequestToken { public int Owner; }
public class Interactor { public bool IsLocal; public Controller Controller; }
public class Controller { public PlayerController PlayerController; }
public class PlayerController { public Player NetworkPlayer; }
public class UserInfo { public int Identifier; }
public class Player { public static Player Current; public UserInfo UserInfo; }
namespace TavernNativeMenu
{
    internal static class MeshSocialTransport
    {
        internal static string CardUnavailableReason;
        internal static int NotifiedId;
        internal static void NotifyCard(int id, string message) { NotifiedId = id; }
    }
    internal static class MeshSocialClient
    {
        internal static string Error;
        internal static void SetError(Exception error) { Error = error.Message; }
    }
}
namespace HarmonyLib
{
    public class Harmony { public void Patch(object original, HarmonyMethod prefix) { } }
    public class HarmonyMethod { public HarmonyMethod(Type type, string name) { } }
    public static class AccessTools { public static object Method(Type type, string name) { return null; } }
}
