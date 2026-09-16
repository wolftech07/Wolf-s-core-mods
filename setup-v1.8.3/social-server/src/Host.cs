using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace TavernNativeSocial
{
    internal sealed class HostForm : Form
    {
        private readonly TextBox publicUrl = new TextBox();
        private readonly NumericUpDown port = new NumericUpDown();
        private readonly RichTextBox output = new RichTextBox();
        private readonly Button start = new Button();
        private readonly Label status = new Label();
        private readonly SocialRelay relay;
        private RelayListener listener;
        private readonly string dataPath;
        internal HostForm(string dataFolder)
        {
            Text = "Tavern Native Social — Relay and server setup";
            ClientSize = new Size(810, 595); MinimumSize = new Size(720, 560);
            Font = new Font("Segoe UI", 10); StartPosition = FormStartPosition.CenterScreen;
            dataPath = Path.Combine(dataFolder, "social-data.json");
            relay = new SocialRelay(dataPath);
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(18), ColumnCount = 3, RowCount = 8 };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 135));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 74));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 45));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            Controls.Add(layout);
            var intro = new Label { Dock = DockStyle.Fill, Text = "Host one small shared relay for your group. Players keep their own social identity and are online only while their client sends heartbeats. Internet access needs an HTTPS reverse proxy; the relay itself listens on this computer only." };
            layout.Controls.Add(intro, 0, 0); layout.SetColumnSpan(intro, 3);
            layout.Controls.Add(new Label { Text = "Public relay URL", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 1);
            publicUrl.Text = "http://127.0.0.1:1764"; publicUrl.Dock = DockStyle.Fill; publicUrl.Margin = new Padding(3, 8, 6, 3);
            layout.Controls.Add(publicUrl, 1, 1); layout.SetColumnSpan(publicUrl, 2);
            layout.Controls.Add(new Label { Text = "Local port", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 2);
            port.Minimum = 1024; port.Maximum = 65535; port.Value = 1764; port.Dock = DockStyle.Left; port.Width = 100; port.Margin = new Padding(3, 8, 3, 3);
            int previousPort = (int)port.Value;
            port.ValueChanged += delegate
            {
                if (publicUrl.Text.TrimEnd('/') == "http://127.0.0.1:" + previousPort) publicUrl.Text = "http://127.0.0.1:" + (int)port.Value;
                previousPort = (int)port.Value;
            };
            layout.Controls.Add(port, 1, 2);
            start.Text = "Start relay"; start.Dock = DockStyle.Fill; start.Click += Toggle;
            layout.Controls.Add(start, 2, 2);
            var install = new Button { Text = "Install / update server companion…", Dock = DockStyle.Fill };
            install.Click += Install; layout.Controls.Add(install, 0, 3); layout.SetColumnSpan(install, 2);
            var docs = new Button { Text = "Hosting guide", Dock = DockStyle.Fill }; docs.Click += delegate { OpenGuide(); }; layout.Controls.Add(docs, 2, 3);
            var note = new Label { Text = "Install the companion in the GAME SERVER folder (with Mods and MelonLoader). Players use TavernNativeMenuSetup for the client. Close this app to stop the relay; friendships and invitations stay saved.", Dock = DockStyle.Fill, ForeColor = Color.FromArgb(75, 75, 75) };
            layout.Controls.Add(note, 0, 4); layout.SetColumnSpan(note, 3);
            status.Text = "Stopped — data saved in your local application data folder"; status.Dock = DockStyle.Fill; status.TextAlign = ContentAlignment.MiddleLeft;
            layout.Controls.Add(status, 0, 5); layout.SetColumnSpan(status, 3);
            output.ReadOnly = true; output.Dock = DockStyle.Fill; output.Font = new Font("Consolas", 9); output.BackColor = Color.FromArgb(24, 28, 34); output.ForeColor = Color.FromArgb(225, 235, 230);
            layout.Controls.Add(output, 0, 6); layout.SetColumnSpan(output, 3);
            var save = new Button { Text = "Save log…", Dock = DockStyle.Fill }; save.Click += delegate { using (var dialog = new SaveFileDialog { Filter = "Text log|*.txt", FileName = "TavernNativeSocial.log" }) if (dialog.ShowDialog(this) == DialogResult.OK) File.WriteAllText(dialog.FileName, output.Text); }; layout.Controls.Add(save, 2, 7);
            var check = new Button { Text = "Test connection", Dock = DockStyle.Fill }; check.Click += async delegate { check.Enabled = false; try { await TestConnection(); } finally { check.Enabled = true; } }; layout.Controls.Add(check, 0, 7);
            var folder = new Button { Text = "Open data folder", Dock = DockStyle.Fill }; folder.Click += delegate { Directory.CreateDirectory(Path.GetDirectoryName(dataPath)); Process.Start(new ProcessStartInfo(Path.GetDirectoryName(dataPath)) { UseShellExecute = true }); }; layout.Controls.Add(folder, 1, 7);
            FormClosed += delegate { if (listener != null) listener.Dispose(); };
            Log("Ready. This host stores private friendships and recipient-only invitations, never community server listings.");
            Log("Automatic diagnostic log: " + DiagnosticLog.PathName);
        }
        private void Toggle(object sender, EventArgs e)
        {
            try
            {
                if (listener != null) { listener.Dispose(); listener = null; start.Text = "Start relay"; port.Enabled = true; status.Text = "Stopped"; Log("Relay stopped."); return; }
                ValidateUrl(publicUrl.Text);
                listener = new RelayListener(relay, Log); listener.Start((int)port.Value);
                start.Text = "Stop relay"; port.Enabled = false; status.Text = "Running — friends become offline after 60 seconds without client heartbeats";
            }
            catch (Exception ex)
            {
                if (listener != null) listener.Dispose(); listener = null; start.Text = "Start relay"; port.Enabled = true;
                string explanation = ex is SocketException && ((SocketException)ex).SocketErrorCode == SocketError.AddressAlreadyInUse
                    ? "The local port is already in use. Another relay may be running. Close that relay or choose a different local port, then update the proxy and server companion to match."
                    : ex.Message;
                status.Text = "Could not start — see live log";
                DiagnosticLog.Error(ex); Log("[ERROR] " + explanation);
                MessageBox.Show(this, explanation + "\n\nDetails were saved to:\n" + DiagnosticLog.PathName, "Relay could not start", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        private async Task TestConnection()
        {
            try
            {
                string address = ValidateUrl(publicUrl.Text);
                int localPort = (int)port.Value;
                Log("Checking local listener...");
                await Task.Run(delegate { CheckHealth("http://127.0.0.1:" + localPort); });
                Log("[OK] Local relay health check passed.");
                if (address != "http://127.0.0.1:" + localPort)
                {
                    Log("Checking public HTTPS endpoint...");
                    await Task.Run(delegate { CheckHealth(address); });
                    Log("[OK] Public endpoint reaches a Tavern friends relay. A friend on another network should also test this address.");
                }
                else Log("Local test only: other computers need the public HTTPS address described in Hosting guide.");
            }
            catch (Exception error) { DiagnosticLog.Error(error); Log("[ERROR] Connection test: " + error.Message + " Start the relay first; for internet use, check the HTTPS proxy, certificate and public URL."); }
        }
        private static void CheckHealth(string url)
        {
            var request = (HttpWebRequest)WebRequest.Create(url + "/v1/health");
            request.Timeout = 8000; request.ReadWriteTimeout = 8000; request.AllowAutoRedirect = false;
            using (var response = (HttpWebResponse)request.GetResponse())
            using (var stream = response.GetResponseStream())
            using (var reader = new StreamReader(stream))
            {
                if (response.StatusCode != HttpStatusCode.OK) throw new InvalidDataException("The address did not return HTTP 200.");
                char[] buffer = new char[8193]; int count = reader.ReadBlock(buffer, 0, buffer.Length);
                if (count > 8192) throw new InvalidDataException("The address returned an unexpected response.");
                var result = SocialRelay.Json().DeserializeObject(new string(buffer, 0, count)) as IDictionary<string, object>;
                if (result == null || SocialRelay.Text(result, "service") != "TavernNativeSocial") throw new InvalidDataException("This URL does not point to a Tavern friends relay.");
            }
        }
        private void SelfTest(string folder)
        {
            var probe = new TcpListener(IPAddress.Loopback, 0); probe.Start(); int chosen = ((IPEndPoint)probe.LocalEndpoint).Port; probe.Stop();
            port.Value = chosen;
            Toggle(this, EventArgs.Empty);
            if (listener == null) throw new InvalidOperationException("Host start button did not start the listener.");
            Task check = TestConnection();
            DateTime deadline = DateTime.UtcNow.AddSeconds(20);
            while (!check.IsCompleted && DateTime.UtcNow < deadline) { Application.DoEvents(); Thread.Sleep(10); }
            Application.DoEvents();
            if (!check.IsCompleted || !output.Text.Contains("[OK] Local relay health check passed.")) throw new InvalidOperationException("Host connection check failed.");
            Toggle(this, EventArgs.Empty);
            if (listener != null || start.Text != "Start relay") throw new InvalidOperationException("Host stop button did not reset state.");
            Log("[OK] Host startup, connection check, live log and stop smoke test passed.");
            if (!File.ReadAllText(DiagnosticLog.PathName).Contains("smoke test passed")) throw new IOException("Automatic host logging failed.");
            File.WriteAllText(Path.Combine(folder, "host-self-test.txt"), "PASS host startup, local HTTP health, connection button, live log, automatic file log and stop.\r\n");
        }
        private void Install(object sender, EventArgs e)
        {
            try
            {
                string url = ValidateUrl(publicUrl.Text);
                using (var dialog = new FolderBrowserDialog { Description = "Select the patched A Township Tale GAME SERVER folder (contains MelonLoader, Mods and the game executable).", ShowNewFolderButton = false })
                {
                    if (dialog.ShowDialog(this) != DialogResult.OK) return;
                    string folder = Path.GetFullPath(dialog.SelectedPath);
                    if (!Directory.Exists(Path.Combine(folder, "MelonLoader", "net472")) || !File.Exists(Path.Combine(folder, "A Township Tale_Data", "Managed", "Root.Township.dll")) || !File.Exists(Path.Combine(folder, "Plugins", "TavernLib.dll")))
                        throw new InvalidOperationException("Select a patched Tavern game server with MelonLoader net472 and TavernLib installed.");
                    foreach (Process process in Process.GetProcesses())
                    {
                        try { if (process.Id != Process.GetCurrentProcess().Id && process.MainModule.FileName.StartsWith(folder.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new ApiError(409, "Close the game server before updating its companion."); }
                        catch (ApiError) { throw; } catch { } finally { process.Dispose(); }
                    }
                    string configPath = Path.Combine(folder, "UserData", "TavernNativeSocialServer.json");
                    string modPath = Path.Combine(folder, "Mods", "TavernNativeSocialServer.dll");
                    IDictionary<string, object> config = null;
                    if (File.Exists(configPath))
                    {
                        config = SocialRelay.Json().DeserializeObject(File.ReadAllText(configPath)) as IDictionary<string, object>;
                        if (config == null) throw new InvalidDataException("Existing companion configuration is invalid. Restore it before updating.");
                        relay.Dispatch("POST", "/v1/server/session-heartbeat", SocialRelay.Text(config, "server_token"), new Dictionary<string, object> { { "ids", new string[0] } }, "host-gui");
                        if (SocialRelay.Text(config, "public_relay_url") != url)
                            if (MessageBox.Show(this, "This changes the relay address announced to players. Continue with the new public URL?", "Update relay address", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                        Log("Preserving the existing server identity and credential.");
                    }
                    else
                    {
                        object created = relay.CreateServer(Path.GetFileName(folder));
                        config = SocialRelay.Json().DeserializeObject(SocialRelay.Json().Serialize(created)) as IDictionary<string, object>;
                        Log("Created a separate trusted server identity. Its credential is saved only in the server configuration.");
                    }
                    config["relay_url"] = "http://127.0.0.1:" + ((int)port.Value).ToString();
                    config["public_relay_url"] = url;
                    byte[] oldMod = File.Exists(modPath) ? File.ReadAllBytes(modPath) : null;
                    byte[] oldConfig = File.Exists(configPath) ? File.ReadAllBytes(configPath) : null;
                    Directory.CreateDirectory(Path.GetDirectoryName(modPath)); Directory.CreateDirectory(Path.GetDirectoryName(configPath));
                    try
                    {
                        using (System.IO.Stream resource = Assembly.GetExecutingAssembly().GetManifestResourceStream("TavernNativeSocialServer.dll"))
                        {
                            if (resource == null) throw new FileNotFoundException("The companion payload is missing from this host executable.");
                            using (var memory = new MemoryStream()) { resource.CopyTo(memory); WriteVerified(modPath, memory.ToArray()); }
                        }
                        WriteVerified(configPath, Encoding.UTF8.GetBytes(SocialRelay.Json().Serialize(config)));
                    }
                    catch
                    {
                        if (oldMod != null) File.WriteAllBytes(modPath, oldMod); else if (File.Exists(modPath)) File.Delete(modPath);
                        if (oldConfig != null) File.WriteAllBytes(configPath, oldConfig); else if (File.Exists(configPath)) File.Delete(configPath);
                        throw;
                    }
                    Log("[OK] Companion installed. Restart the game server, start this relay, and give players the public relay URL.");
                    MessageBox.Show(this, "The server companion is ready. Start the relay and restart the game server. Each player connects the same public relay URL in Native Menu's Friends settings before exchanging cards.\n\nThis computer must host both the game server and relay with this configuration. See Hosting guide for a separate host.", "Server companion installed", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
            catch (Exception ex) { DiagnosticLog.Error(ex); Log("[ERROR] " + ex.Message); MessageBox.Show(this, ex.Message + "\n\nDetails were saved to:\n" + DiagnosticLog.PathName, "Companion setup could not finish", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }
        private static void WriteVerified(string path, byte[] bytes)
        {
            string temp = path + ".new-" + Guid.NewGuid().ToString("N");
            try
            {
                File.WriteAllBytes(temp, bytes);
                if (File.Exists(path)) File.Replace(temp, path, path + ".previous"); else File.Move(temp, path);
                if (!File.ReadAllBytes(path).SequenceEqual(bytes)) throw new IOException("The saved file did not match the setup payload.");
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
        private static string ValidateUrl(string value)
        {
            Uri uri; IPAddress address;
            if (!Uri.TryCreate((value ?? "").Trim().TrimEnd('/'), UriKind.Absolute, out uri) || uri.AbsolutePath != "/" || uri.UserInfo.Length > 0 || uri.Query.Length > 0 || uri.Fragment.Length > 0)
                throw new InvalidDataException("Enter an HTTPS relay origin, such as https://friends.example.org.");
            bool local = IPAddress.TryParse(uri.DnsSafeHost.Trim('[', ']'), out address) && IPAddress.IsLoopback(address);
            if (uri.Scheme != "https" && !(uri.Scheme == "http" && local)) throw new InvalidDataException("Public relay addresses need HTTPS. Plain HTTP is accepted only on this computer.");
            return uri.GetLeftPart(UriPartial.Authority).TrimEnd('/');
        }
        private void OpenGuide()
        {
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "SOCIAL-HOSTING.md");
            if (File.Exists(path)) Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            else MessageBox.Show(this, "The relay listens on 127.0.0.1. To reach it from other computers, configure an HTTPS reverse proxy with your public domain pointing to http://127.0.0.1:" + port.Value + ". Do not expose this HTTP port directly. Keep SOCIAL-HOSTING.md next to this EXE for complete setup details.", "Hosting guide");
        }
        private void Log(string message)
        {
            if (IsDisposed) return;
            if (InvokeRequired) { try { BeginInvoke((Action)(delegate { Log(message); })); } catch { } return; }
            DiagnosticLog.Write(message);
            if (output.TextLength > 150000) output.Text = output.Text.Substring(output.TextLength - 100000);
            output.AppendText(DateTime.Now.ToString("HH:mm:ss") + "  " + message + Environment.NewLine); output.SelectionStart = output.TextLength; output.ScrollToCaret();
        }
        [STAThread]
        private static int Main(string[] args)
        {
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            bool render = args.Length == 2 && args[0] == "--render";
            bool selfTest = args.Length == 2 && args[0] == "--self-test";
            string folder = selfTest ? Path.GetFullPath(args[1]) : render ? Path.Combine(Path.GetDirectoryName(Path.GetFullPath(args[1])), "host-render-data")
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TavernNativeSocialRelay");
            DiagnosticLog.Initialize(folder);
            Application.ThreadException += delegate(object sender, ThreadExceptionEventArgs e) { DiagnosticLog.Error(e.Exception); MessageBox.Show(e.Exception.Message + "\n\nDetails saved to " + DiagnosticLog.PathName, "Tavern social host"); };
            try
            {
                string mutexName;
                using (var hash = SHA256.Create()) mutexName = "Local\\TavernNativeSocial-" + BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(Path.GetFullPath(folder).ToUpperInvariant()))).Replace("-", "");
                bool first;
                using (var mutex = new Mutex(true, mutexName, out first))
                {
                    if (!first) { MessageBox.Show("The social host is already open for this data folder. Use its existing window.", "Tavern social host"); return 1; }
                    using (var form = new HostForm(folder))
                    {
                        if (render || selfTest)
                        {
                            form.StartPosition = FormStartPosition.Manual; form.Location = new Point(-30000, -30000);
                            form.Show(); Application.DoEvents();
                            if (selfTest) form.SelfTest(folder);
                            else using (var bitmap = new Bitmap(form.Width, form.Height)) { form.DrawToBitmap(bitmap, new Rectangle(0, 0, form.Width, form.Height)); bitmap.Save(Path.GetFullPath(args[1])); }
                            form.Close();
                        }
                        else Application.Run(form);
                    }
                    mutex.ReleaseMutex();
                }
                return 0;
            }
            catch (Exception ex)
            {
                DiagnosticLog.Error(ex);
                if (!render && !selfTest) MessageBox.Show(ex.Message + "\n\nDetails saved to:\n" + DiagnosticLog.PathName, "Tavern social host", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }
        }
    }
    internal static class DiagnosticLog
    {
        private static readonly object Gate = new object();
        internal static string PathName;
        internal static void Initialize(string folder) { PathName = Path.Combine(folder, "host.log"); }
        internal static void Write(string text)
        {
            lock (Gate) try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(PathName));
                if (File.Exists(PathName) && new FileInfo(PathName).Length > 1024 * 1024) { File.Copy(PathName, PathName + ".previous", true); File.WriteAllText(PathName, ""); }
                File.AppendAllText(PathName, DateTime.Now.ToString("o") + " " + text + Environment.NewLine);
            }
            catch { /* The original error remains visible even when the folder is unwritable. */ }
        }
        internal static void Error(Exception error) { Write(error.GetType().FullName + ": " + error.Message + Environment.NewLine + error.StackTrace); }
    }
}
