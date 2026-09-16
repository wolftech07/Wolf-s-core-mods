using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

[assembly: AssemblyTitle("Tavern Native Menu Setup")]
[assembly: AssemblyDescription("Graphical setup for Tavern Native Menu and Tavern Launcher Client 1.8.3")]
[assembly: AssemblyVersion("1.1.1.0")]
[assembly: AssemblyFileVersion("1.1.1.0")]

namespace TavernNativeMenuSetup
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            try
            {
                if (args.Length == 2 && args[0] == "--render")
                {
                    using (var form = new SetupForm())
                    {
                        form.StartPosition = FormStartPosition.Manual;
                        form.Location = new Point(-30000, -30000);
                        form.Show();
                        Application.DoEvents();
                        using (var bitmap = new Bitmap(form.Width, form.Height))
                        {
                            form.DrawToBitmap(bitmap, form.ClientRectangleWithBorder());
                            bitmap.Save(Path.GetFullPath(args[1]));
                        }
                        form.Close();
                    }
                    return 0;
                }
                if (args.Length == 2 && args[0] == "--self-test")
                    return SetupChecks.Run(Path.GetFullPath(args[1]));
                Application.Run(new SetupForm());
                return 0;
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Tavern Native Menu Setup", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }
        }
    }

    internal static class Worker
    {
        internal static string PowerShell
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"WindowsPowerShell\v1.0\powershell.exe"); }
        }

        // Windows argv quoting. Paths never pass through cmd.exe or a script expression.
        internal static string Quote(string value)
        {
            var output = new StringBuilder("\"");
            int slashes = 0;
            foreach (char c in value)
            {
                if (c == '\\') { slashes++; continue; }
                if (c == '"') { output.Append('\\', slashes * 2 + 1); output.Append(c); }
                else { output.Append('\\', slashes); output.Append(c); }
                slashes = 0;
            }
            output.Append('\\', slashes * 2);
            return output.Append('"').ToString();
        }

        internal static int Run(string script, string[] arguments, Action<string, bool> onLine)
        {
            var command = new StringBuilder("-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File ");
            command.Append(Quote(script));
            foreach (string arg in arguments) command.Append(' ').Append(Quote(arg));
            var info = new ProcessStartInfo(PowerShell, command.ToString());
            info.UseShellExecute = false;
            info.CreateNoWindow = true;
            info.RedirectStandardOutput = true;
            info.RedirectStandardError = true;
            info.StandardOutputEncoding = new UTF8Encoding(false);
            info.StandardErrorEncoding = new UTF8Encoding(false);
            using (var process = new Process())
            {
                process.StartInfo = info;
                process.OutputDataReceived += delegate(object sender, DataReceivedEventArgs e) { if (e.Data != null) onLine(e.Data, false); };
                process.ErrorDataReceived += delegate(object sender, DataReceivedEventArgs e) { if (e.Data != null) onLine(e.Data, true); };
                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                process.WaitForExit(); // Called on a worker thread; also drains both asynchronous readers.
                return process.ExitCode;
            }
        }

        internal static string ExtractPayload()
        {
            string root = Path.Combine(Path.GetTempPath(), "TavernNativeMenuSetup-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                var assembly = Assembly.GetExecutingAssembly();
                int count = 0;
                foreach (string name in assembly.GetManifestResourceNames())
                {
                    if (!name.StartsWith("payload/", StringComparison.Ordinal)) continue;
                    string path = Path.GetFullPath(Path.Combine(root, name.Substring(8).Replace('/', Path.DirectorySeparatorChar)));
                    if (!path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("Invalid embedded payload path.");
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    using (Stream resource = assembly.GetManifestResourceStream(name))
                    using (var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write)) resource.CopyTo(file);
                    count++;
                }
                if (count == 0 || !File.Exists(Path.Combine(root, "Setup-Worker.ps1")))
                    throw new InvalidDataException("This setup build is missing its embedded payload.");
                return root;
            }
            catch { Cleanup(root); throw; }
        }

        internal static void Cleanup(string root)
        {
            string full = Path.GetFullPath(root);
            string parent = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar);
            string name = Path.GetFileName(full);
            Guid id;
            if (!String.Equals(Path.GetDirectoryName(full), parent, StringComparison.OrdinalIgnoreCase) ||
                !name.StartsWith("TavernNativeMenuSetup-", StringComparison.Ordinal) ||
                !Guid.TryParseExact(name.Substring("TavernNativeMenuSetup-".Length), "N", out id))
                throw new IOException("Refusing to clean an unrecognized temporary folder.");
            if (Directory.Exists(full)) RemoveOwnTree(full);
        }

        private static void RemoveOwnTree(string path)
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Temporary folder contains a redirected path; cleanup skipped.");
            foreach (string file in Directory.GetFiles(path))
            {
                if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("Temporary folder contains a redirected file; cleanup skipped.");
                File.Delete(file);
            }
            foreach (string child in Directory.GetDirectories(path)) RemoveOwnTree(child);
            Directory.Delete(path);
        }
    }

    internal sealed class SetupForm : Form
    {
        private readonly TextBox game = new TextBox();
        private readonly TextBox launcher = new TextBox();
        private readonly TextBox log = new TextBox();
        private readonly Label status = new Label();
        private readonly ProgressBar progress = new ProgressBar();
        private readonly List<Control> locked = new List<Control>();
        private readonly Button open = new Button();
        private bool busy;
        private string checkedLauncher;
        private readonly Color ink = Color.FromArgb(30, 43, 54);
        private readonly Color accent = Color.FromArgb(25, 112, 91);

        internal Rectangle ClientRectangleWithBorder() { return new Rectangle(0, 0, Width, Height); }

        internal SetupForm()
        {
            Text = "Tavern Native Menu Setup";
            Font = new Font("Segoe UI", 10F);
            ForeColor = ink;
            BackColor = Color.FromArgb(246, 248, 249);
            ClientSize = new Size(820, 650);
            MinimumSize = new Size(760, 670);
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.Dpi;

            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(26, 20, 26, 18), ColumnCount = 1, RowCount = 8 };
            foreach (int height in new[] { 70, 80, 80, 52, 34, 0, 36, 45 })
                layout.RowStyles.Add(height == 0 ? new RowStyle(SizeType.Percent, 100) : new RowStyle(SizeType.Absolute, height));
            Controls.Add(layout);
            var heading = new Panel { Dock = DockStyle.Fill };
            heading.Controls.Add(new Label { Text = "Choose your server in the game.", AutoSize = true, Location = new Point(0, 0), Font = new Font("Segoe UI", 20F, FontStyle.Bold) });
            heading.Controls.Add(new Label { Text = "Tavern Native Menu  /  Setup for Tavern Launcher Client 1.8.3", AutoSize = true, Location = new Point(2, 43), ForeColor = Color.FromArgb(84, 100, 112) });
            layout.Controls.Add(heading, 0, 0);
            layout.Controls.Add(PathRow("1   A Township Tale game", "Select A Township Tale.exe in your patched game folder.", game, "A Township Tale.exe"), 0, 1);
            layout.Controls.Add(PathRow("2   Tavern Launcher client", "Select TavernLauncher - Client.exe from version 1.8.3.", launcher, "TavernLauncher - Client.exe"), 0, 2);

            var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Padding = new Padding(0, 6, 0, 0) };
            var install = Button("Install / Update", 150, true);
            var check = Button("Check setup", 130, false);
            var undo = Button("Undo setup", 125, false);
            install.Click += async delegate { await RunOperation("Install"); };
            check.Click += async delegate { await RunOperation("Validate"); };
            undo.Click += async delegate { await RunOperation("Uninstall"); };
            locked.AddRange(new Control[] { install, check, undo });
            actions.Controls.AddRange(new Control[] { install, check, undo });
            layout.Controls.Add(actions, 0, 3);

            var logHeader = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
            logHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            logHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 108));
            logHeader.Controls.Add(new Label { Text = "LIVE SETUP LOG", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Font = new Font("Segoe UI", 9F, FontStyle.Bold) }, 0, 0);
            var save = Button("Save log...", 100, false);
            save.Height = 29;
            save.Click += SaveLog;
            logHeader.Controls.Add(save, 1, 0);
            layout.Controls.Add(logHeader, 0, 4);
            log.Dock = DockStyle.Fill;
            log.Multiline = true;
            log.ReadOnly = true;
            log.BackColor = Color.FromArgb(24, 32, 41);
            log.ForeColor = Color.FromArgb(220, 228, 233);
            log.BorderStyle = BorderStyle.None;
            log.Font = new Font("Consolas", 9F);
            log.WordWrap = false;
            log.ScrollBars = ScrollBars.Both;
            log.AccessibleName = "Live setup log";
            layout.Controls.Add(log, 0, 5);
            status.Text = "Ready. Choose your files, then click Install / Update.";
            status.Dock = DockStyle.Fill;
            status.TextAlign = ContentAlignment.MiddleLeft;
            layout.Controls.Add(status, 0, 6);

            var footer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 154));
            progress.Dock = DockStyle.Top;
            progress.Height = 6;
            progress.Margin = new Padding(0, 15, 20, 0);
            footer.Controls.Add(progress, 0, 0);
            open.Text = "Open launcher";
            open.Size = new Size(152, 34);
            open.FlatStyle = FlatStyle.Flat;
            open.Enabled = false;
            open.Click += OpenLauncher;
            footer.Controls.Add(open, 1, 0);
            layout.Controls.Add(footer, 0, 7);
            locked.Add(game); locked.Add(launcher);
            FormClosing += delegate(object sender, FormClosingEventArgs e) {
                if (!busy) return;
                e.Cancel = true;
                status.Text = "Setup is still working. Please wait for it to finish before closing.";
            };
            DetectPaths();
            Append("[READY] Select the two EXE files above. No commands are needed.", false);
            Append("[INFO] Your game needs MelonLoader and the Tavern client patch installed first.", false);
            Append("[INFO] Install / Update keeps your settings and original Undo backups.", false);
            Append("[INFO] Close the game and Tavern Launcher before Install or Undo.", false);
        }

        private Button Button(string text, int width, bool primary)
        {
            var b = new Button { Text = text, Width = width, Height = 36, FlatStyle = FlatStyle.Flat, Margin = new Padding(0, 0, 10, 0) };
            b.FlatAppearance.BorderColor = primary ? accent : Color.FromArgb(185, 196, 202);
            if (primary) { b.BackColor = accent; b.ForeColor = Color.White; }
            else b.BackColor = Color.White;
            return b;
        }

        private Control PathRow(string title, string hint, TextBox input, string expected)
        {
            var row = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 3, Margin = new Padding(0, 0, 0, 8) };
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 106));
            row.RowStyles.Add(new RowStyle(SizeType.Absolute, 23));
            row.RowStyles.Add(new RowStyle(SizeType.Absolute, 29));
            row.RowStyles.Add(new RowStyle(SizeType.Absolute, 20));
            var label = new Label { Text = title, Dock = DockStyle.Fill, Font = new Font("Segoe UI", 10F, FontStyle.Bold) };
            row.Controls.Add(label, 0, 0); row.SetColumnSpan(label, 2);
            input.Dock = DockStyle.Fill; input.AccessibleName = title; input.Margin = new Padding(0, 0, 10, 0);
            row.Controls.Add(input, 0, 1);
            var browse = Button("Browse...", 102, false); browse.Height = 28;
            browse.Click += delegate {
                using (var picker = new OpenFileDialog { Title = "Select " + expected, Filter = "Windows applications (*.exe)|*.exe", CheckFileExists = true, FileName = expected })
                {
                    try { if (File.Exists(input.Text)) picker.InitialDirectory = Path.GetDirectoryName(input.Text); } catch { }
                    if (picker.ShowDialog(this) == DialogResult.OK) input.Text = picker.FileName;
                }
            };
            locked.Add(browse);
            input.TextChanged += delegate { checkedLauncher = null; open.Enabled = false; };
            row.Controls.Add(browse, 1, 1);
            var note = new Label { Text = hint, Dock = DockStyle.Fill, Font = new Font("Segoe UI", 8F), ForeColor = Color.FromArgb(84, 100, 112) };
            row.Controls.Add(note, 0, 2); row.SetColumnSpan(note, 2);
            return row;
        }

        private void DetectPaths()
        {
            try
            {
                string config = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"TheModdingTavern\tavern_launcher.json");
                if (File.Exists(config) && new FileInfo(config).Length < 1024 * 1024)
                {
                    var data = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(File.ReadAllText(config));
                    object exe;
                    if (data != null && data.TryGetValue("game_exe", out exe) && exe is string && File.Exists((string)exe)) game.Text = (string)exe;
                }
            }
            catch { /* Optional discovery must not prevent manual selection. */ }
            string adjacent = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "TavernLauncher - Client.exe");
            if (File.Exists(adjacent)) launcher.Text = adjacent;
        }

        private async Task RunOperation(string operation)
        {
            if (busy) return;
            string gamePath, launcherPath;
            try
            {
                gamePath = Path.GetFullPath(Environment.ExpandEnvironmentVariables(game.Text.Trim().Trim('"')));
                launcherPath = Path.GetFullPath(Environment.ExpandEnvironmentVariables(launcher.Text.Trim().Trim('"')));
                if (!File.Exists(gamePath) || !File.Exists(launcherPath)) throw new IOException("Choose the game EXE and the Tavern Launcher client EXE first.");
            }
            catch (Exception ex) { Append("[ERROR] " + ex.Message, true); status.Text = "Choose valid files and try again."; return; }
            busy = true;
            foreach (Control c in locked) c.Enabled = false;
            open.Enabled = false;
            progress.Style = ProgressBarStyle.Marquee;
            status.Text = operation == "Validate" ? "Checking compatibility..." : operation == "Install" ? "Installing. Live progress appears below..." : "Restoring the files saved by setup...";
            Append("[START] " + operation, false);
            string payload = null;
            try
            {
                int result = await Task.Run(delegate {
                    payload = Worker.ExtractPayload();
                    return Worker.Run(Path.Combine(payload, "Setup-Worker.ps1"), new[] { "-Operation", operation, "-GameExe", gamePath, "-LauncherExe", launcherPath, "-PayloadRoot", payload }, PostLine);
                });
                if (result == 0)
                {
                    checkedLauncher = launcherPath;
                    open.Enabled = true;
                    status.Text = operation == "Install" ? "Up to date. Open Tavern Launcher and click Play Game." : operation == "Validate" ? "Checks passed. Ready to install or update." : "Setup undone. Previous files restored.";
                    Append("[DONE] " + status.Text, false);
                }
                else
                {
                    status.Text = "Setup did not complete. Read the error in the log and try again.";
                    Append("[ERROR] Worker exited with code " + result + ".", true);
                }
            }
            catch (Exception ex)
            {
                status.Text = "Setup did not complete. See the log for details.";
                Append("[ERROR] " + ex.Message, true);
            }
            finally
            {
                if (payload != null)
                {
                    try { Worker.Cleanup(payload); }
                    catch (Exception ex) { Append("[WARN] Temporary payload cleanup: " + ex.Message, false); }
                }
                busy = false;
                progress.Style = ProgressBarStyle.Blocks;
                progress.Value = 0;
                foreach (Control c in locked) c.Enabled = true;
            }
        }

        private void PostLine(string line, bool error)
        {
            if (!IsDisposed && IsHandleCreated) BeginInvoke((Action)(delegate { Append(line, error); }));
        }

        private void Append(string line, bool error)
        {
            if (log.TextLength > 2 * 1024 * 1024) { log.Select(0, log.TextLength / 2); log.SelectedText = "[Older log lines trimmed]\r\n"; }
            log.SelectionStart = log.TextLength;
            log.SelectionLength = 0;
            log.AppendText(DateTime.Now.ToString("HH:mm:ss") + "  " + line + Environment.NewLine);
            log.ScrollToCaret();
        }

        private void SaveLog(object sender, EventArgs e)
        {
            using (var picker = new SaveFileDialog { Title = "Save setup log", Filter = "Text file (*.txt)|*.txt", FileName = "TavernNativeMenu-setup-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".txt" })
            {
                if (picker.ShowDialog(this) != DialogResult.OK) return;
                try { File.WriteAllText(picker.FileName, log.Text, new UTF8Encoding(false)); }
                catch (Exception ex) { Append("[ERROR] Could not save log: " + ex.Message, true); }
            }
        }

        private void OpenLauncher(object sender, EventArgs e)
        {
            if (busy || checkedLauncher == null) return;
            try { Process.Start(new ProcessStartInfo(checkedLauncher) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(checkedLauncher) }); }
            catch (Exception ex) { Append("[ERROR] Could not open launcher: " + ex.Message, true); }
        }
    }

    internal static class SetupChecks
    {
        // Exercises the actual redirected-process path without touching a game or launcher.
        internal static int Run(string report)
        {
            var messages = new List<string>();
            string payload = null;
            try
            {
                payload = Worker.ExtractPayload();
                string fixture = Path.Combine(payload, "log fixture ' & [sample].ps1");
                string expected = "space ' & [bracket] " + '\u00e9' + " \\";
                File.WriteAllText(fixture, "param([string]$Value)\r\n[Console]::OutputEncoding = New-Object Text.UTF8Encoding($false)\r\n[Console]::WriteLine('FIRST:' + $Value)\r\nStart-Sleep -Milliseconds 500\r\n[Console]::Error.WriteLine('ERR-LIVE')\r\n[Console]::Write('LAST-NO-NEWLINE')\r\nexit 7\r\n", new UTF8Encoding(true));
                bool live = false;
                long firstAt = 0;
                var watch = Stopwatch.StartNew();
                int code = Worker.Run(fixture, new[] { "-Value", expected }, delegate(string line, bool error) {
                    lock (messages) {
                        messages.Add((error ? "stderr " : "stdout ") + line);
                        if (line == "FIRST:" + expected) { live = true; firstAt = watch.ElapsedMilliseconds; }
                    }
                });
                if (code != 7 || !live || watch.ElapsedMilliseconds - firstAt < 300 || !messages.Contains("stderr ERR-LIVE") || !messages.Contains("stdout LAST-NO-NEWLINE"))
                    throw new Exception("Live log, argv, exit code, or trailing output assertion failed.");
                messages.Add("PASS: embedded payload, quoted paths/Unicode, stdout/stderr, trailing line and failure status (" + watch.ElapsedMilliseconds + " ms).");
                // A second process demonstrates that a successful exit is retained independently of log text.
                File.WriteAllText(fixture, "[Console]::OutputEncoding = New-Object Text.UTF8Encoding($false)\r\nWrite-Output '[OK] Done'\r\nexit 0\r\n", new UTF8Encoding(true));
                if (Worker.Run(fixture, new string[0], delegate(string line, bool error) { messages.Add(line); }) != 0)
                    throw new Exception("Success exit-code assertion failed.");
                messages.Add("PASS: success exit code.");
                File.WriteAllLines(report, messages.ToArray(), new UTF8Encoding(false));
                return 0;
            }
            catch (Exception ex) { messages.Add("FAIL: " + ex); File.WriteAllLines(report, messages.ToArray()); return 1; }
            finally { if (payload != null) Worker.Cleanup(payload); }
        }
    }
}
