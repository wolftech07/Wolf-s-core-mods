// Exercises production TavernSocialClient against loopback-only HTTP fixtures.
// No game is started, no external relay is contacted, and all settings are temp.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using TavernNativeMenu;

internal static class SocialClientTests
{
    private static int passed;
    private static void Check(bool result,string name) { if(!result)throw new Exception(name); passed++; Console.WriteLine("PASS "+name); }
    private static void Fails(Action action,string name) { bool failed=false;try{action();}catch{failed=true;}Check(failed,name); }
    private static void Await(Task task) { task.GetAwaiter().GetResult(); }
    public static int Main(string[] args)
    {
        try
        {
            using(var a=new Relay("first"))
            using(var b=new Relay("second"))
            {
                string root=args[0];
                Check(TavernSocialClient.NormalizeRelayUrl("HTTPS://example.com:443/")=="https://example.com","normalize canonical HTTPS origin");
                foreach(string bad in new[]{"http://example.com","http://localhost:90","file:///x","https://user:password@example.com","https://example.com?token=secret","https://example.com/#fragment"})
                    Fails(delegate{TavernSocialClient.NormalizeRelayUrl(bad);},"reject unsafe relay URL "+bad.Split(':')[0]);
                TavernSocialClient.Initialize(root,"Test Player");
                int changes=0; int callbackThread=0;
                TavernSocialClient.Changed+=delegate{changes++;callbackThread=Thread.CurrentThread.ManagedThreadId;};
                Await(TavernSocialClient.ConfigureAsync(a.Url));
                Check(TavernSocialClient.Configured && TavernSocialClient.SocialId=="first-id","register usable relay identity");
                Check(changes==0,"HTTP completion does not invoke UI event off-thread");
                int mainThread=Thread.CurrentThread.ManagedThreadId;
                TavernSocialClient.Tick();
                Check(changes>0 && callbackThread==mainThread,"Tick dispatches Changed on caller main thread");
                JObject saved=JObject.Parse(File.ReadAllText(Path.Combine(root,"UserData","TavernSocial.json")));
                Check((string)saved["identities"][a.Url]["token"]=="first-private-token","credential saved for exact relay");
                int originalRegistrations=a.Registrations;
                Await(TavernSocialClient.ConfigureAsync(b.Url));
                Check(TavernSocialClient.SocialId=="second-id","second relay gets separate global identity");
                Await(TavernSocialClient.ConfigureAsync(a.Url+"/"));
                Check(TavernSocialClient.SocialId=="first-id" && a.Registrations==originalRegistrations,"return to existing relay reuses saved identity");
                Check(a.Requests.Where(x=>x.Path!="/v1/register").All(x=>x.Auth=="Bearer first-private-token") && b.Requests.Where(x=>x.Path!="/v1/register").All(x=>x.Auth=="Bearer second-private-token"),"bearer credential remains scoped to its relay");
                Check(TavernSocialClient.GetFriendsAsync().GetAwaiter().GetResult().Count==1,"authenticated friends list parsed");
                var server=new ServerEntry{Name="Hidden home",Host="192.168.1.10",GamePort=1757,AuthPort=1762,HasPassword=true,Private=true,Favorite=true,Kind="official"};
                Await(TavernSocialClient.SendInviteAsync("friend-id",server));
                JObject sent=JObject.Parse(a.Requests.Last(x=>x.Path=="/v1/invites" && x.Method=="POST").Body);
                Check(((JObject)sent["server"]).Properties().Select(x=>x.Name).OrderBy(x=>x).SequenceEqual(new[]{"auth_port","game_port","host","kind","name"}),"invite shares address and join mode without credentials or password");
                var accepted=TavernSocialClient.AcceptInviteAsync("invite-1").GetAwaiter().GetResult();
                Check(accepted.Host=="192.168.1.10" && accepted.Private && accepted.Favorite,"accepted private invite becomes validated local server");
                Await(TavernSocialClient.DismissInviteAsync("invite-1"));
                Await(TavernSocialClient.RemoveFriendAsync("friend-id"));
                int before=a.Requests.Count;
                Fails(delegate{TavernSocialClient.GetSessionTicketAsync("server-id",b.Url).GetAwaiter().GetResult();},"server cannot request ticket from unmatched relay");
                Check(a.Requests.Count==before,"unmatched server relay triggers no HTTP request");
                Check(TavernSocialClient.GetSessionTicketAsync("server-id",a.Url).GetAwaiter().GetResult()=="opaque-one-use-ticket","matching server receives opaque ticket from relay");
                a.Mode="redirect"; a.Redirect=b.Url;
                int foreignBefore=b.Requests.Count;
                Fails(delegate{Await(TavernSocialClient.ConfigureAsync(a.Url));},"redirect rejected without following credentials");
                Check(b.Requests.Count==foreignBefore && TavernSocialClient.SocialId=="first-id","redirect neither leaks bearer nor changes active account");
                a.Mode="large";
                Fails(delegate{TavernSocialClient.GetFriendsAsync().GetAwaiter().GetResult();},"oversized relay response rejected before parse");
                a.Mode="json";
                Fails(delegate{TavernSocialClient.GetFriendsAsync().GetAwaiter().GetResult();},"invalid JSON rejected");
                a.Mode="normal";
                Fails(delegate{Await(TavernSocialClient.SendInviteAsync("friend/../id",server));},"path-shaped identity rejected");
                Await(TavernSocialClient.ConfigureAsync(""));
                Check(!TavernSocialClient.Configured && TavernSocialClient.RelayUrl=="","disable relay preserves offline menu use");
                saved=JObject.Parse(File.ReadAllText(Path.Combine(root,"UserData","TavernSocial.json")));
                Check(((JObject)saved["identities"]).Count==2,"disabling relay preserves both identities and friends ownership");
                Check(!Directory.GetFiles(Path.Combine(root,"UserData"),"*.tmp").Any(),"atomic settings writes leave no temporary credential files");
                Check(TavernSocialClient.LastError==null,"successful configuration clears stale error");
            }
            Console.WriteLine("Social client: "+passed+" checks passed."); return 0;
        }
        catch(Exception error){Console.Error.WriteLine("FAIL "+error);return 1;}
    }
    private sealed class Request
    {
        internal string Path,Method,Auth,Body;
    }
    private sealed class Relay : IDisposable
    {
        private readonly TcpListener listener;
        private readonly string id;
        private readonly List<Request> requests=new List<Request>();
        private volatile bool disposed;
        internal string Mode="normal",Redirect="";
        internal int Registrations;
        internal string Url;
        internal List<Request> Requests {get{lock(requests)return requests.ToList();}}
        internal Relay(string id)
        {
            this.id=id;listener=new TcpListener(IPAddress.Loopback,0);listener.Start();
            Url="http://127.0.0.1:"+((IPEndPoint)listener.LocalEndpoint).Port;
            Task.Run((Action)Accept);
        }
        private void Accept()
        {
            while(!disposed)
            {
                try{var client=listener.AcceptTcpClient();Task.Run(delegate{Serve(client);});}
                catch(SocketException){if(!disposed)throw;}
                catch(ObjectDisposedException){return;}
            }
        }
        private void Serve(TcpClient client)
        {
            using(client)
            {
                try
                {
                    var stream=client.GetStream();stream.ReadTimeout=10000;
                    var header=new List<byte>();
                    while(header.Count<16384)
                    {
                        int next=stream.ReadByte();if(next<0)return;header.Add((byte)next);
                        int n=header.Count;
                        if(n>=4 && header[n-4]==13 && header[n-3]==10 && header[n-2]==13 && header[n-1]==10)break;
                    }
                    string[] lines=Encoding.ASCII.GetString(header.ToArray()).Split(new[]{"\r\n"},StringSplitOptions.None);
                    string[] first=lines[0].Split(' ');
                    var request=new Request{Method=first[0],Path=first[1],Auth="",Body=""};
                    int length=0;
                    foreach(string line in lines)
                    {
                        if(line.StartsWith("Content-Length:",StringComparison.OrdinalIgnoreCase))length=Int32.Parse(line.Substring(15).Trim());
                        if(line.StartsWith("Authorization:",StringComparison.OrdinalIgnoreCase))request.Auth=line.Substring(14).Trim();
                        if(line.StartsWith("Expect: 100-continue",StringComparison.OrdinalIgnoreCase)){byte[] interim=Encoding.ASCII.GetBytes("HTTP/1.1 100 Continue\r\n\r\n");stream.Write(interim,0,interim.Length);}
                    }
                    byte[] body=new byte[length];int offset=0;
                    while(offset<length){int read=stream.Read(body,offset,length-offset);if(read==0)return;offset+=read;}
                    request.Body=Encoding.UTF8.GetString(body);lock(requests)requests.Add(request);
                    int status=200;string extra="",json="{}";
                    if(request.Path=="/v1/register"){Interlocked.Increment(ref Registrations);json="{\"social_id\":\""+id+"-id\",\"token\":\""+id+"-private-token\",\"name\":\"Test Player\"}";}
                    else if(request.Auth!="Bearer "+id+"-private-token"){status=401;}
                    else if(request.Path=="/v1/me" && Mode=="redirect"){status=302;extra="Location: "+Redirect+"/stolen\r\n";}
                    else if(request.Path=="/v1/me")json="{\"social_id\":\""+id+"-id\",\"name\":\"Test Player\"}";
                    else if(request.Path=="/v1/friends" && Mode=="large")json=new string('x',300000);
                    else if(request.Path=="/v1/friends" && Mode=="json")json="{bad";
                    else if(request.Path=="/v1/friends")json="{\"friends\":[{\"social_id\":\"friend-id\",\"name\":\"Friend\",\"online\":true}]}";
                    else if(request.Path=="/v1/invites" && request.Method=="GET")json="{\"invites\":[]}";
                    else if(request.Path=="/v1/invites" && request.Method=="POST")json="{\"invite_id\":\"invite-1\"}";
                    else if(request.Path=="/v1/invites/invite-1/accept")json="{\"server\":{\"name\":\"Home\",\"host\":\"192.168.1.10\",\"game_port\":1757,\"auth_port\":1762}}";
                    else if(request.Path=="/v1/session-ticket")json="{\"ticket\":\"opaque-one-use-ticket\"}";
                    byte[] response=Encoding.UTF8.GetBytes(json);
                    byte[] headers=Encoding.ASCII.GetBytes("HTTP/1.1 "+status+" Test\r\nContent-Type: application/json\r\nContent-Length: "+response.Length+"\r\nConnection: close\r\n"+extra+"\r\n");
                    stream.Write(headers,0,headers.Length);stream.Write(response,0,response.Length);
                }
                catch(IOException){ }
                catch(ObjectDisposedException){ }
            }
        }
        public void Dispose(){disposed=true;listener.Stop();}
    }
}
namespace TavernNativeMenu
{
    internal static class TavernSocialTransport{internal static void Initialize(){}}
    internal sealed class ServerEntry{public string Name,Host,Kind;public int GamePort=1757,AuthPort=1762;public bool HasPassword,Private,Favorite;}
    internal static class ServerCatalog{internal static bool ValidEntry(ServerEntry entry){return entry!=null && !String.IsNullOrWhiteSpace(entry.Host) && entry.GamePort>0 && entry.GamePort<65536 && entry.AuthPort>0 && entry.AuthPort<65536;}}
}
