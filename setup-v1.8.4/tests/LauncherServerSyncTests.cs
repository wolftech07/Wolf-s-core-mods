using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using TavernNativeMenu;

internal static class LauncherServerSyncTests
{
    private static int count;
    private static void Check(bool condition, string text) { if (!condition) throw new Exception(text); count++; Console.WriteLine("PASS " + text); }
    public static void Main(string[] args)
    {
        string folder = args[0], profilePath = Path.Combine(folder, "profile.json");
        var config = new JObject { { "username", "Alice" }, { "game_exe", "original-game.exe" }, { "saved_servers", new JArray() }, { "recent_servers", new JArray() } };
        File.WriteAllText(profilePath, config.ToString());
        string tokenFolder = Path.Combine(folder, "tokens"); Directory.CreateDirectory(tokenFolder);
        string token = Path.Combine(tokenFolder, "keep.json"); File.WriteAllText(token, "private-token-baseline");
        var profile = LauncherProfile.Load(profilePath);
        Check(profile.Servers.Count == 0 && !profile.ReloadServers(), "unchanged PC profile does not trigger a refresh");
        config["saved_servers"] = JArray.Parse("[{'name':'Private server','ip':'private.example.org','port':1777,'auth_port':1782,'private':true,'kind':'headless'}]");
        File.WriteAllText(profilePath, config.ToString());
        Check(profile.ReloadServers(), "PC Add is detected without restarting the game");
        var entry = profile.Servers.Single();
        Check(entry.Name == "Private server" && entry.GamePort == 1777 && entry.AuthPort == 1782 && entry.Kind == "headless" && entry.Private && entry.Favorite, "custom PC fields reach the game profile");
        Check(!profile.ReloadServers(), "repeated refresh keeps the same snapshot");
        config["username"] = "Bob"; config["game_exe"] = "other-game.exe";
        ((JObject)((JArray)config["saved_servers"])[0])["name"] = "Renamed server";
        File.WriteAllText(profilePath, config.ToString()); profile.ReloadServers();
        Check(profile.Username == "Alice" && profile.GameExe == "original-game.exe" && profile.TokenDirectory == tokenFolder, "live refresh cannot change the session identity or token path");
        Check(profile.Servers.Single().Name == "Renamed server", "PC name edits reach the next VR list");
        File.WriteAllText(profilePath, "{\"saved_servers\":[");
        bool rejected = false; try { profile.ReloadServers(); } catch (InvalidDataException) { rejected = true; }
        Check(rejected && profile.Servers.Single().Name == "Renamed server", "interrupted PC save retains the last usable bookmarks");
        config["saved_servers"] = new JArray();
        File.WriteAllText(profilePath, config.ToString());
        Check(profile.ReloadServers() && profile.Servers.Count == 0, "PC removal updates imported bookmarks");
        config["saved_servers"] = JArray.Parse("[{'name':'Legacy','ip':'EXAMPLE.org','port':1757},{'name':'Other auth','ip':'example.org','port':1757,'auth_port':1763}]");
        config["recent_servers"] = JArray.Parse("[{'ip':'example.org','port':1757}]");
        File.WriteAllText(profilePath, config.ToString()); profile.ReloadServers();
        Check(profile.Servers.Count == 2 && profile.Servers[0].AuthPort == 1762 && profile.Servers[0].Favorite && profile.Servers[0].Kind == "official", "legacy fields default correctly and separate auth endpoints survive deduplication");
        string settings = Path.Combine(folder, "UserData", "TavernNativeMenu.json");
        Directory.CreateDirectory(Path.GetDirectoryName(settings));
        File.WriteAllText(settings, new JObject { { "LauncherConfig", profilePath }, { "Servers", new JArray() } }.ToString());
        var catalog = new ServerCatalog(folder);
        TavernWire.BlockDirectory = true;
        var favorites = catalog.GetServers(ServerBoardType.MyServers);
        Check(favorites.Wait(1000), "saved servers load while the community directory is unavailable");
        Check(favorites.Result.OfType<MenuServer>().Count(x => x.Entry != null) == 2, "PC favorites produce native VR server entries");
        Check(catalog.Settings.EnableFriendsNetworking, "existing settings keep peer friends enabled");
        Check(File.ReadAllText(token) == "private-token-baseline", "bookmark refresh never rewrites token files");
        config["saved_servers"] = new JObject();
        File.WriteAllText(profilePath, config.ToString());
        rejected = false; try { profile.ReloadServers(); } catch (InvalidDataException) { rejected = true; }
        Check(rejected && profile.Servers.Count == 2, "wrong server-list type cannot erase good entries");
        Console.WriteLine("PASS " + count + " PC-to-VR server sync checks against production classes.");
    }
}
namespace HarmonyLib { }
namespace TavernNativeMenu
{
    internal static class NativeFriendsMenu { public static bool ChoosingInvitation; public static string InvitationName; }
    internal static class MenuMod { public static string SafeText(string s) { return (s ?? "").Replace('<', '\u2039').Replace('>', '\u203a'); } }
    internal static class TavernWire
    {
        internal static bool BlockDirectory;
        public static Task<JArray> GetDirectoryAsync(string url) { return BlockDirectory ? new TaskCompletionSource<JArray>().Task : Task.FromResult(new JArray()); }
    }
}
public enum ServerBoardType { PublicServer, DiscoverServers, MyServers, OpenServers }
namespace Alta.Api.DataTransferModels.Models.Responses
{
    public class UserInfo { }
    public class ConnectionInfo { public IPAddress Address; public int GamePort; }
    public class GameServerInfo { public int Identifier, Target, SceneIndex; public string Name, Description; public UserInfo[] OnlinePlayers; public float Playability; public ConnectionInfo ConnectionInfo; }
    public class DevGameServerInfo : GameServerInfo { }
}
