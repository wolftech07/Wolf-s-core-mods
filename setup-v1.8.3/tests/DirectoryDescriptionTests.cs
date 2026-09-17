using System;
using System.Net;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using TavernNativeMenu;

class DirectoryDescriptionTests
{
    static void Check(bool condition, string message) { if (!condition) throw new Exception(message); Console.WriteLine("PASS " + message); }
    static void Main()
    {
        var first = DirectoryParser.Parse(JArray.Parse("[{ 'address':'example.org:1757', 'name':'Test', 'description':'First description', 'player_count':1 }]"))[0];
        var old = ServerCatalog.ToMenuServer(first);
        Check(old.Description.StartsWith("First description\n\n"), "Directory description reaches native server details");
        var updated = DirectoryParser.Parse(JArray.Parse("[{ 'address':'example.org:1757', 'name':'Test', 'description':'New <color=red>description', 'player_count':3, 'has_password':true }]"))[0];
        var next = ServerCatalog.ToMenuServer(updated);
        Check(old.Identifier == next.Identifier && next.Description.Contains("New ") && !next.Description.Contains("First description"), "Same server keeps its identity with fresh description");
        Check(!next.Description.Contains("<color") && next.Description.Contains("Players: 3") && next.Description.Contains("Password required"), "Description escapes markup and retains live status");
        var missing = DirectoryParser.Parse(JArray.Parse("[{ 'address':'example.org:1757', 'name':'Test' }]"))[0];
        Check(ServerCatalog.ToMenuServer(missing).Description.StartsWith("example.org:1757"), "Missing directory description keeps usable connection details");
        var local = new ServerEntry { Host="private.example.org", Description="Private world", Private=true };
        Check(ServerCatalog.ToMenuServer(local).Description.Contains("Private world"), "Private saved descriptions are displayed");
    }
}
namespace HarmonyLib { }
namespace TavernNativeMenu
{
    static class NativeFriendsMenu { public static bool ChoosingInvitation; public static string InvitationName; }
    static class MenuMod { public static string SafeText(string s) { s=(s??"").Replace('<','\u2039').Replace('>','\u203a'); return s.Length>700?s.Substring(0,700):s; } }
    static class TavernWire { public static Task<JArray> GetDirectoryAsync(string url) { return Task.FromResult(new JArray()); } }
}
public enum ServerBoardType { PublicServer, DiscoverServers, MyServers, OpenServers }
namespace Alta.Api.DataTransferModels.Models.Responses
{
    public class UserInfo { }
    public class ConnectionInfo { public IPAddress Address; public int GamePort; }
    public class GameServerInfo { public int Identifier, Target, SceneIndex; public string Name, Description; public UserInfo[] OnlinePlayers; public float Playability; public ConnectionInfo ConnectionInfo; }
    public class DevGameServerInfo : GameServerInfo { }
}
