using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace TavernNativeMenu
{
    // The existing launcher uses one UTF-8 JSON object per TCP connection.
    // This layer deliberately has no game dependencies and never logs payloads.
    internal static class TavernWire
    {
        private const int AuthLimit = 64 * 1024;
        private const int DirectoryLimit = 2 * 1024 * 1024;
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);

        internal static async Task<JObject> ExchangeAsync(string host, int authPort, JObject payload)
        {
            host = ValidateHost(host);
            if (authPort < 1 || authPort > 65535)
                throw new ArgumentOutOfRangeException("authPort", "Server port must be between 1 and 65535.");
            if (payload == null) throw new ArgumentNullException("payload");
            byte[] request = Utf8.GetBytes(payload.ToString(Formatting.None));
            if (request.Length > AuthLimit)
                throw new ArgumentException("The authentication request is too large.", "payload");

            using (var timeout = new CancellationTokenSource())
            using (var client = new TcpClient())
            {
                timeout.CancelAfter(5000);
                using (timeout.Token.Register(delegate { client.Close(); }))
                {
                    try
                    {
                        await client.ConnectAsync(host, authPort).ConfigureAwait(false);
                        using (NetworkStream stream = client.GetStream())
                        {
                            await stream.WriteAsync(request, 0, request.Length, timeout.Token).ConfigureAwait(false);
                            byte[] buffer = new byte[4096];
                            char[] characters = new char[4096];
                            Decoder decoder = Utf8.GetDecoder();
                            var text = new StringBuilder();
                            int total = 0;
                            while (true)
                            {
                                int read = await stream.ReadAsync(buffer, 0, Math.Min(buffer.Length, AuthLimit - total + 1), timeout.Token).ConfigureAwait(false);
                                total += read;
                                if (total > AuthLimit) throw new InvalidDataException("The server response is too large.");
                                int count = decoder.GetChars(buffer, 0, read, characters, 0, read == 0);
                                text.Append(characters, 0, count);
                                JObject response;
                                if (TryReadObject(text.ToString(), out response)) return response;
                                if (read == 0) throw new InvalidDataException("The server returned an incomplete or invalid JSON response.");
                            }
                        }
                    }
                    catch (Exception)
                    {
                        if (timeout.IsCancellationRequested)
                            throw new TimeoutException("The server did not respond within 5 seconds.");
                        throw;
                    }
                }
            }
        }

        internal static async Task<JArray> GetDirectoryAsync(string url)
        {
            Uri address;
            if (!Uri.TryCreate(url, UriKind.Absolute, out address) ||
                (address.Scheme != Uri.UriSchemeHttp && address.Scheme != Uri.UriSchemeHttps) ||
                !String.IsNullOrEmpty(address.UserInfo))
                throw new ArgumentException("The server directory must be an HTTP or HTTPS address.", "url");

            var request = (HttpWebRequest)WebRequest.Create(address);
            request.Method = "GET";
            request.Accept = "application/json";
            request.UserAgent = "TavernNativeMenu/1.0";
            request.Timeout = 10000;
            request.ReadWriteTimeout = 10000;
            request.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
            using (var timeout = new CancellationTokenSource())
            {
                timeout.CancelAfter(10000);
                using (timeout.Token.Register(request.Abort))
                {
                    try
                    {
                        using (var response = (HttpWebResponse)await request.GetResponseAsync().ConfigureAwait(false))
                        using (Stream stream = response.GetResponseStream())
                        using (var body = new MemoryStream())
                        {
                            if (response.ContentLength > DirectoryLimit)
                                throw new InvalidDataException("The server directory response is too large.");
                            byte[] buffer = new byte[8192];
                            while (true)
                            {
                                int read = await stream.ReadAsync(buffer, 0, buffer.Length, timeout.Token).ConfigureAwait(false);
                                if (read == 0) break;
                                if (body.Length + read > DirectoryLimit)
                                    throw new InvalidDataException("The server directory response is too large.");
                                body.Write(buffer, 0, read);
                            }
                            JToken root = JToken.Parse(Utf8.GetString(body.ToArray()));
                            JArray servers = root as JArray;
                            if (servers == null && root.Type == JTokenType.Object)
                                servers = root["servers"] as JArray;
                            if (servers == null)
                                throw new InvalidDataException("The directory did not return a server list.");
                            foreach (JToken server in servers)
                                if (server.Type != JTokenType.Object)
                                    throw new InvalidDataException("The directory contains an invalid server entry.");
                            return servers;
                        }
                    }
                    catch (Exception)
                    {
                        if (timeout.IsCancellationRequested)
                            throw new TimeoutException("The server directory did not respond within 10 seconds.");
                        throw;
                    }
                }
            }
        }

        private static string ValidateHost(string host)
        {
            if (String.IsNullOrWhiteSpace(host)) throw new ArgumentException("Enter a server address.", "host");
            host = host.Trim();
            if (host.StartsWith("[", StringComparison.Ordinal) && host.EndsWith("]", StringComparison.Ordinal))
                host = host.Substring(1, host.Length - 2);
            if (Uri.CheckHostName(host) == UriHostNameType.Unknown)
                throw new ArgumentException("Use a server IP address or hostname, without a URL or port.", "host");
            return host;
        }

        private static bool TryReadObject(string text, out JObject value)
        {
            value = null;
            string trimmed = text.Trim();
            if (trimmed.Length < 2 || trimmed[0] != '{' || trimmed[trimmed.Length - 1] != '}') return false;
            try
            {
                using (var reader = new JsonTextReader(new StringReader(trimmed)))
                {
                    reader.MaxDepth = 64;
                    JObject candidate = JObject.Load(reader);
                    if (reader.Read()) return false;
                    value = candidate;
                    return true;
                }
            }
            catch (JsonException)
            {
                return false;
            }
        }
    }
}
