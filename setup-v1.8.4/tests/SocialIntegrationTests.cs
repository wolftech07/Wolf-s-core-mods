using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TavernNativeMenu;
using TavernNativeSocial;

// Separate AppDomains give the two production clients independent static
// state and credential files, while both use the real HTTP relay listener.
public sealed class SocialTestClient : MarshalByRefObject
{
    public string Configure(string folder, string name, string url)
    { TavernSocialClient.Initialize(folder, name); TavernSocialClient.ConfigureAsync(url).GetAwaiter().GetResult(); return TavernSocialClient.SocialId; }
    public string Ticket(string server, string relay) { return TavernSocialClient.GetSessionTicketAsync(server, relay).GetAwaiter().GetResult(); }
    public string Friends() { return TavernSocialClient.GetFriendsAsync().GetAwaiter().GetResult().ToString(); }
    public string Invites() { return TavernSocialClient.GetInvitesAsync().GetAwaiter().GetResult().ToString(); }
    public void Invite(string recipient) { TavernSocialClient.SendInviteAsync(recipient, new ServerEntry { Name = "Private Home", Host = "private.example.org", GamePort = 1857, AuthPort = 1862, Kind = "official", Private = true }).GetAwaiter().GetResult(); }
    public string Accept(string id) { return JsonConvert.SerializeObject(TavernSocialClient.AcceptInviteAsync(id).GetAwaiter().GetResult()); }
    public void Remove(string friend) { TavernSocialClient.RemoveFriendAsync(friend).GetAwaiter().GetResult(); }
    public override object InitializeLifetimeService() { return null; }
}

internal static class SocialIntegrationTests
{
    private static int checks;
    private static void Check(bool condition, string label) { if (!condition) throw new Exception(label); checks++; Console.WriteLine("PASS " + label); }
    private static SocialTestClient Client(AppDomain domain) { return (SocialTestClient)domain.CreateInstanceAndUnwrap(typeof(SocialTestClient).Assembly.FullName, typeof(SocialTestClient).FullName); }
    private static JObject Post(string url, string token, string route, JObject body)
    {
        var request = (HttpWebRequest)WebRequest.Create(url + route); request.Method = "POST"; request.ContentType = "application/json"; request.Timeout = 5000; request.ReadWriteTimeout = 5000; request.Proxy = null;
        request.Headers[HttpRequestHeader.Authorization] = "Bearer " + token;
        byte[] bytes = Encoding.UTF8.GetBytes(body.ToString(Formatting.None)); request.ContentLength = bytes.Length;
        using (var stream = request.GetRequestStream()) stream.Write(bytes, 0, bytes.Length);
        using (var response = request.GetResponse()) using (var reader = new StreamReader(response.GetResponseStream())) return JObject.Parse(reader.ReadToEnd());
    }
    public static int Main(string[] args)
    {
        AppDomain first = null, second = null, reload = null;
        try
        {
            string root = args[0];
            var relay = new SocialRelay(Path.Combine(root, "relay.json"));
            JObject server = JObject.Parse(SocialRelay.Json().Serialize(relay.CreateServer("Card server")));
            string serverId = (string)server["server_id"], credential = (string)server["server_token"];
            using (var listener = new RelayListener(relay, delegate { }))
            {
                listener.Start(0); string url = "http://127.0.0.1:" + listener.Port;
                first = AppDomain.CreateDomain("Alice"); second = AppDomain.CreateDomain("Bob");
                SocialTestClient alice = Client(first), bob = Client(second);
                string aid = alice.Configure(Path.Combine(root, "Alice"), "Same display name", url);
                string bid = bob.Configure(Path.Combine(root, "Bob"), "Same display name", url);
                Check(aid != bid, "two production clients with the same name retain distinct identities");
                string left = (string)Post(url, credential, "/v1/server/resolve-ticket", new JObject { { "ticket", alice.Ticket(serverId, url) } })["social_id"];
                string right = (string)Post(url, credential, "/v1/server/resolve-ticket", new JObject { { "ticket", bob.Ticket(serverId, url) } })["social_id"];
                Check(left == aid && right == bid, "real client tickets resolve through server HTTP authentication");
                Post(url, credential, "/v1/server/friendship", new JObject { { "left_id", left }, { "right_id", right } });
                Check((string)JArray.Parse(alice.Friends())[0]["social_id"] == bid && (string)JArray.Parse(bob.Friends())[0]["social_id"] == aid, "reciprocal friendship appears in both production clients");
                alice.Invite(bid); JArray inbox = JArray.Parse(bob.Invites());
                Check(inbox.Count == 1 && JArray.Parse(alice.Invites()).Count == 0, "real relay delivers a private invitation only to its recipient");
                JObject accepted = JObject.Parse(bob.Accept((string)inbox[0]["invite_id"]));
                Check((string)accepted["Host"] == "private.example.org" && (int)accepted["GamePort"] == 1857 && (int)accepted["AuthPort"] == 1862 && (bool)accepted["Private"], "accepted invite preserves private join endpoint and ports");
                Check(JArray.Parse(bob.Invites()).Count == 0, "accepted invite disappears from the live inbox");
                AppDomain.Unload(first); first = null; reload = AppDomain.CreateDomain("Alice restarted"); alice = Client(reload);
                Check(alice.Configure(Path.Combine(root, "Alice"), "Renamed Alice", url) == aid && JArray.Parse(alice.Friends()).Count == 1, "client restart and rename preserve identity and friendship");
                alice.Remove(bid);
                Check(JArray.Parse(bob.Friends()).Count == 0, "unfriend reaches the other client through the real relay");
            }
            Console.WriteLine("Social integration: " + checks + " checks passed with two isolated production clients and the real loopback relay."); return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        finally { if (first != null) AppDomain.Unload(first); if (second != null) AppDomain.Unload(second); if (reload != null) AppDomain.Unload(reload); }
    }
}

namespace TavernNativeMenu
{
    internal static class TavernSocialTransport { internal static void Initialize() { } }
    internal sealed class ServerEntry { public string Name, Host, Kind; public int GamePort = 1757, AuthPort = 1762; public bool Private, Favorite; }
    internal static class ServerCatalog
    {
        internal static bool ValidEntry(ServerEntry entry)
        { return entry != null && !String.IsNullOrWhiteSpace(entry.Host) && entry.GamePort > 0 && entry.GamePort < 65536 && entry.AuthPort > 0 && entry.AuthPort < 65536 && (Uri.CheckHostName(entry.Host) == UriHostNameType.Dns || Uri.CheckHostName(entry.Host) == UriHostNameType.IPv4); }
    }
}
