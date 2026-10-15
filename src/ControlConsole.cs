using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Linq;
using System.Management;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using System.Web.Script.Serialization;

namespace NixLanConsole
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            try
            {
                if (args.Length > 0 && args[0] == "--sensor") { HostSensor.Run(); return; }
                if (args.Length > 0 && args[0] == "--display") { DisplayServer.Run(); return; }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new RolePicker());
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "局域网桌面控制台", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }

    internal sealed class RolePicker : Form
    {
        public RolePicker()
        {
            Text = "局域网桌面控制台 · 首次设置";
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false;
            ClientSize = new Size(560, 300); BackColor = Color.FromArgb(250, 236, 250);
            var title = new Label { Text = "选择这台电脑的角色", Font = new Font("Microsoft YaHei", 18, FontStyle.Bold), ForeColor = Color.FromArgb(64, 49, 142), AutoSize = true, Location = new Point(28, 20) };
            var info = new Label { Text = "主机负责发送硬件状态和游戏进程；副机显示控制台并记录游戏时长。\r\n两台电脑都选择一次，之后会在登录 Windows 时自动运行。", Font = new Font("Microsoft YaHei", 10), AutoSize = true, Location = new Point(32, 65) };
            var host = new Button { Text = "设置为主机", Location = new Point(34, 145), Size = new Size(230, 48), Font = new Font("Microsoft YaHei", 12, FontStyle.Bold), BackColor = Color.FromArgb(210, 184, 231) };
            var display = new Button { Text = "设置为副机", Location = new Point(294, 145), Size = new Size(230, 48), Font = new Font("Microsoft YaHei", 12, FontStyle.Bold), BackColor = Color.FromArgb(178, 225, 233) };
            host.Click += delegate { Configure("--sensor", "NixLanConsoleHost"); };
            display.Click += delegate { Configure("--display", "NixLanConsoleDisplay"); };
            var addGame = new Button { Text = "为主机添加直接运行的游戏 exe…", Location = new Point(34, 210), Size = new Size(490, 38), Font = new Font("Microsoft YaHei", 10), BackColor = Color.FromArgb(245, 225, 247) };
            addGame.Click += delegate { AddGame(); };
            var updateLabel = new Label { Text = "副机自动更新：在副机打开此对话并下载新版，程序会自动安装。", Location = new Point(34, 264), Size = new Size(490, 25), Font = new Font("Microsoft YaHei", 9), ForeColor = Color.FromArgb(64, 49, 142) };
            Controls.Add(title); Controls.Add(info); Controls.Add(host); Controls.Add(display); Controls.Add(addGame); Controls.Add(updateLabel);
        }
        private void AddGame()
        {
            using (var dialog = new OpenFileDialog { Filter = "Windows 程序 (*.exe)|*.exe", Title = "选择要追踪的游戏程序", CheckFileExists = true })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                string label = Path.GetFileNameWithoutExtension(dialog.FileName);
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NixLanConsole");
                Directory.CreateDirectory(dir); string file = Path.Combine(dir, "tracked-games.txt");
                string executable = Path.GetFullPath(dialog.FileName);
                var lines = File.Exists(file) ? File.ReadAllLines(file).Where(x => !x.TrimStart().StartsWith(executable + "|", StringComparison.OrdinalIgnoreCase)).ToList() : new List<string>();
                lines.Add(executable + "|" + label); File.WriteAllLines(file, lines, Encoding.UTF8);
                MessageBox.Show(this, "已添加“" + label + "”。主机后台会自动读取新设置。", "游戏追踪", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }
        private void Configure(string role, string taskName)
        {
            string exe = Application.ExecutablePath;
            try
            {
                using (var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run"))
                    key.SetValue(taskName, "\"" + exe + "\" " + role, Microsoft.Win32.RegistryValueKind.String);
                var run = new ProcessStartInfo(exe, role) { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
                Process.Start(run);
                Hide(); Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "无法保存或启动后台程序。请确认程序文件夹可读，并尝试将文件夹移到当前用户有权限的位置。\r\n\r\n详细信息：" + ex.Message, "设置失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }

    internal sealed class MetricPacket
    {
        public string host;
        public long utc;
        public double cpu;
        public double memoryUsed;
        public double memoryTotal;
        public double gpu;
        public double cpuTemp;
        public double gpuTemp;
        public string[] games;
    }

    internal static class HostSensor
    {
        private const int Port = 46701;
        private static readonly string DataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NixLanConsole");
        private static readonly JavaScriptSerializer Json = new JavaScriptSerializer();
        private static PerformanceCounter CpuCounter;
        private static double GpuTemperature = -1;
        private static double CpuTemperature = -1;
        private static DateTime LastTemperatureRead = DateTime.MinValue;
        private static DateTime LastGameConfigRead = DateTime.MinValue;
        private static readonly Dictionary<string, string> GameExecutables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, string> CustomGameExecutables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static System.Threading.Timer UpdateTimer;
        private static volatile bool GameRunning;
        internal static bool HasGameRunning { get { return GameRunning; } }

        public static void Run()
        {
            Directory.CreateDirectory(DataDir);
            StartTray();
            DiscoverGames();
            LoadCustomGames();
            UpdateTimer = new System.Threading.Timer(DisplayServer.CheckForUpdate, "--sensor", TimeSpan.FromSeconds(20), TimeSpan.FromMinutes(5));
            try { CpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total"); CpuCounter.NextValue(); } catch { }
            using (var udp = new UdpClient())
            {
                udp.EnableBroadcast = true;
                while (true)
                {
                    var p = new MetricPacket { host = Environment.MachineName, utc = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), cpu = ReadCpu(), memoryUsed = 0, memoryTotal = 0, gpu = ReadGpuUsage(), cpuTemp = -1, gpuTemp = -1, games = ReadGames() };
                    ReadMemory(out p.memoryUsed, out p.memoryTotal);
                    if ((DateTime.UtcNow - LastTemperatureRead).TotalSeconds >= 5) { ReadTemperatures(); LastTemperatureRead = DateTime.UtcNow; }
                    p.cpuTemp = CpuTemperature; p.gpuTemp = GpuTemperature;
                    byte[] bytes = Encoding.UTF8.GetBytes(Json.Serialize(p));
                    try { udp.Send(bytes, bytes.Length, new IPEndPoint(IPAddress.Broadcast, Port)); } catch { }
                    Thread.Sleep(1000);
                }
            }
        }
        private static void StartTray()
        {
            var trayThread = new Thread(delegate()
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new HostTrayContext());
            });
            trayThread.IsBackground = true;
            trayThread.SetApartmentState(ApartmentState.STA);
            trayThread.Start();
        }
        private static double ReadCpu() { try { return Math.Round(CpuCounter.NextValue(), 1); } catch { return -1; } }
        private static void ReadMemory(out double used, out double total)
        {
            used = total = 0;
            try { using (var s = new ManagementObjectSearcher("SELECT TotalVisibleMemorySize,FreePhysicalMemory FROM Win32_OperatingSystem")) foreach (ManagementObject m in s.Get()) { total = Convert.ToDouble(m["TotalVisibleMemorySize"]) / 1024.0 / 1024.0; used = total - Convert.ToDouble(m["FreePhysicalMemory"]) / 1024.0 / 1024.0; } } catch { }
        }
        private static double ReadGpuUsage()
        {
            double sum = 0;
            try { using (var s = new ManagementObjectSearcher("root\\cimv2", "SELECT UtilizationPercentage FROM Win32_PerfFormattedData_GPUPerformanceCounters_GPUEngine")) foreach (ManagementObject m in s.Get()) sum += Convert.ToDouble(m["UtilizationPercentage"]); } catch { return -1; }
            return Math.Min(100, Math.Round(sum, 1));
        }
        private static void ReadTemperatures()
        {
            try { using (var s = new ManagementObjectSearcher("root\\wmi", "SELECT CurrentTemperature FROM MSAcpi_ThermalZoneTemperature")) foreach (ManagementObject m in s.Get()) { double t = Convert.ToDouble(m["CurrentTemperature"]) / 10.0 - 273.15; if (t > 0 && t < 120) { CpuTemperature = Math.Round(t, 0); break; } } } catch { }
            try
            {
                var psi = new ProcessStartInfo("nvidia-smi.exe", "--query-gpu=temperature.gpu --format=csv,noheader,nounits") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
                using (var p = Process.Start(psi)) { string result = p.StandardOutput.ReadToEnd(); p.WaitForExit(1500); double t; if (double.TryParse(result.Trim().Split('\n')[0], out t)) GpuTemperature = t; }
            }
            catch { }
        }
        private static string[] ReadGames()
        {
            LoadCustomGames();
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in Process.GetProcesses())
            {
                try
                {
                    string path = Path.GetFullPath(p.MainModule.FileName);
                    string title;
                    if (CustomGameExecutables.TryGetValue(path, out title) || GameExecutables.TryGetValue(path, out title)) names.Add(title);
                }
                catch { }
                finally { p.Dispose(); }
            }
            GameRunning = names.Count > 0;
            return names.ToArray();
        }
        private static void DiscoverGames()
        {
            var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            roots.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam", "steamapps", "common"));
            roots.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Steam", "steamapps", "common"));
            try { using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey("Software\\Valve\\Steam")) { var v = k == null ? null : k.GetValue("SteamPath") as string; if (!String.IsNullOrEmpty(v)) roots.Add(Path.Combine(v, "steamapps", "common")); } } catch { }
            foreach (var drive in DriveInfo.GetDrives())
            {
                if (drive.DriveType != DriveType.Fixed || !drive.IsReady) continue;
                roots.Add(Path.Combine(drive.RootDirectory.FullName, "Steam", "steamapps", "common"));
                roots.Add(Path.Combine(drive.RootDirectory.FullName, "SteamLibrary", "steamapps", "common"));
                string endfield = Path.Combine(drive.RootDirectory.FullName, "Hypergryph Launcher", "games", "Endfield Game", "Endfield.exe");
                if (File.Exists(endfield)) GameExecutables[Path.GetFullPath(endfield)] = "明日方舟：终末地";
            }
            foreach (string root in roots.ToArray())
            {
                try
                {
                    string libraries = Path.Combine(Directory.GetParent(Directory.GetParent(root).FullName).FullName, "steamapps", "libraryfolders.vdf");
                    if (!File.Exists(libraries)) continue;
                    foreach (Match match in Regex.Matches(File.ReadAllText(libraries), "\\\"path\\\"\\s+\\\"([^\\\"]+)\\\""))
                        roots.Add(Path.Combine(match.Groups[1].Value.Replace("\\\\", "\\"), "steamapps", "common"));
                }
                catch { }
            }
            foreach (string root in roots)
            {
                try
                {
                    string manifests = Path.Combine(Directory.GetParent(root).FullName, "appmanifest_*.acf");
                    foreach (string file in Directory.GetFiles(Path.GetDirectoryName(manifests), Path.GetFileName(manifests)))
                    {
                        string data = File.ReadAllText(file); var im = Regex.Match(data, "\\\"name\\\"\\s+\\\"([^\\\"]+)\\\""); var dm = Regex.Match(data, "\\\"installdir\\\"\\s+\\\"([^\\\"]+)\\\"");
                        if (!im.Success || !dm.Success) continue;
                        string dir = Path.Combine(root, dm.Groups[1].Value); if (!Directory.Exists(dir)) continue;
                        foreach (string exe in Directory.GetFiles(dir, "*.exe", SearchOption.AllDirectories))
                        {
                            string lower = exe.ToLowerInvariant(); if (lower.Contains("\\redist\\") || lower.Contains("\\_commonredist\\") || lower.Contains("\\easyanticheat\\") || lower.Contains("\\battleye\\") || lower.Contains("\\anticheat\\")) continue;
                            string proc = Path.GetFileNameWithoutExtension(exe); if (proc.Length < 3 || Regex.IsMatch(proc, "crash|report|unins|setup|updater|cef|webengine|platformprocess", RegexOptions.IgnoreCase)) continue;
                            GameExecutables[Path.GetFullPath(exe)] = im.Groups[1].Value;
                        }
                    }
                }
                catch { }
            }
        }
        private static void LoadCustomGames()
        {
            string f = Path.Combine(DataDir, "tracked-games.txt");
            try
            {
                DateTime write = File.Exists(f) ? File.GetLastWriteTimeUtc(f) : DateTime.MinValue;
                if (write <= LastGameConfigRead) return;
                LastGameConfigRead = write;
                CustomGameExecutables.Clear();
                if (!File.Exists(f)) return;
                foreach (string line in File.ReadAllLines(f))
                {
                    string[] p = line.Split(new[] { '|' }, 2);
                    if (p.Length != 2 || !File.Exists(p[0].Trim())) continue;
                    string path = Path.GetFullPath(p[0].Trim());
                    CustomGameExecutables[path] = String.IsNullOrWhiteSpace(p[1]) ? Path.GetFileNameWithoutExtension(path) : p[1].Trim();
                }
            }
            catch { }
        }
    }

    internal sealed class GameSession
    {
        public string id;
        public string game;
        public long start;
        public long end;
    }
    internal sealed class DashboardState
    {
        public bool online;
        public string host;
        public double cpu, memoryUsed, memoryTotal, gpu, cpuTemp, gpuTemp;
        public string[] games = new string[0];
        public string role = "ame";
        public long roleEvent;
        public long returnInSeconds;
        public GameSession[] sessions;
    }

    internal static class BuildInfo { public const string Version = "1.4.4.0"; }

    internal static class DisplayServer
    {
        private const int UdpPort = 46701;
        private const int HttpPort = 46700;
        private static readonly JavaScriptSerializer Json = new JavaScriptSerializer();
        private static readonly object Gate = new object();
        private static readonly List<GameSession> Sessions = new List<GameSession>();
        private static readonly Dictionary<string, GameSession> ActiveSessions = new Dictionary<string, GameSession>(StringComparer.OrdinalIgnoreCase);
        private static MetricPacket Last;
        private static DateTime LastSeen = DateTime.MinValue;
        private static bool AnyGame;
        private static bool UserClosed;
        private static DateTime ReturnAt = DateTime.MinValue;
        private static long EventId;
        private static string Role = "ame";
        private static Process Browser;
        private static TrayContext Tray;
        private static System.Threading.Timer UpdateTimer;
        private static int UpdateStarted;
        private static readonly string BaseDir = AppDomain.CurrentDomain.BaseDirectory;
        private static readonly string DataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NixLanConsole");

        public static void Run()
        {
            Directory.CreateDirectory(DataDir); LoadSessions();
            UpdateTimer = new System.Threading.Timer(CheckForUpdate, null, TimeSpan.FromSeconds(20), TimeSpan.FromMinutes(5));
            var trayThread = new Thread(new ThreadStart(delegate { Tray = new TrayContext(); Application.Run(Tray); })) { IsBackground = true, Name = "Console tray" };
            trayThread.SetApartmentState(ApartmentState.STA); trayThread.Start();
            var udpThread = new Thread(ReadUdp) { IsBackground = true }; udpThread.Start();
            var listener = new HttpListener(); listener.Prefixes.Add("http://127.0.0.1:" + HttpPort + "/"); listener.Start();
            while (true) { HttpListenerContext c = null; try { c = listener.GetContext(); Handle(c); } catch { if (c != null) try { c.Response.Close(); } catch { } } }
        }
        internal static void CheckForUpdate(object ignored)
        {
            if (Interlocked.CompareExchange(ref UpdateStarted, 1, 0) != 0) return;
            try
            {
                bool hostMode = String.Equals(ignored as string, "--sensor", StringComparison.Ordinal);
                if (hostMode) { if (HostSensor.HasGameRunning) return; }
                else { lock (Gate) if (ActiveSessions.Count > 0) return; }
                string downloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
                Version current; if (!Version.TryParse(BuildInfo.Version, out current)) return;
                Version available = null; string sourceZip = null;
                foreach (string candidate in Directory.Exists(downloads) ? Directory.GetFiles(downloads, "ControlConsole*.zip") : new string[0])
                {
                    Version candidateVersion;
                    if (!ReadPackageVersion(candidate, out candidateVersion) || candidateVersion <= current) continue;
                    if (available == null || candidateVersion > available) { available = candidateVersion; sourceZip = candidate; }
                }
                string githubZip = FindGitHubUpdate(current, available);
                if (githubZip != null)
                {
                    Version githubVersion;
                    if (ReadPackageVersion(githubZip, out githubVersion) && (available == null || githubVersion > available))
                    { available = githubVersion; sourceZip = githubZip; }
                }
                if (sourceZip == null) return;
                string updateDir = Path.Combine(DataDir, "updates"); Directory.CreateDirectory(updateDir);
                string localZip = Path.Combine(updateDir, Path.GetFileName(sourceZip));
                File.Copy(sourceZip, localZip, true);
                string script = Path.Combine(BaseDir, "ApplyUpdate.ps1");
                if (!File.Exists(script)) throw new FileNotFoundException("更新脚本不存在", script);
                string localScript = Path.Combine(updateDir, "ApplyUpdate.ps1"); File.Copy(script, localScript, true);
                string powershell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell\\v1.0\\powershell.exe");
                string args = "-NoLogo -NoProfile -ExecutionPolicy Bypass -File " + Quote(localScript) + " -TargetDir " + Quote(BaseDir) + " -Archive " + Quote(localZip) + " -ParentPid " + Process.GetCurrentProcess().Id + " -RestartRole " + (hostMode ? "--sensor" : "--display");
                Process.Start(new ProcessStartInfo(powershell, args) { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden });
                Environment.Exit(0);
            }
            catch (Exception ex)
            {
                try { File.AppendAllText(Path.Combine(DataDir, "update.log"), DateTime.Now.ToString("s") + " update check: " + ex.Message + Environment.NewLine, Encoding.UTF8); } catch { }
            }
            finally { Interlocked.Exchange(ref UpdateStarted, 0); }
        }
        private static string FindGitHubUpdate(Version current, Version localAvailable)
        {
            string config = Path.Combine(BaseDir, "update-source.txt");
            if (!File.Exists(config)) return null;
            string repository = File.ReadAllText(config, Encoding.UTF8).Trim();
            if (!Regex.IsMatch(repository, @"\A[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+\z")) return null;
            try
            {
                ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;
                using (var client = new WebClient())
                {
                    client.Headers[HttpRequestHeader.UserAgent] = "NixLanConsole/" + BuildInfo.Version;
                    client.Headers[HttpRequestHeader.Accept] = "application/vnd.github+json";
                    string endpoint = "https://api.github.com/repos/" + repository + "/releases/latest";
                    var release = Json.Deserialize<Dictionary<string, object>>(client.DownloadString(endpoint));
                    Version version;
                    if (release == null || !Version.TryParse(Convert.ToString(release["tag_name"]).TrimStart('v', 'V'), out version) || version <= current || (localAvailable != null && version <= localAvailable)) return null;
                    var assets = release["assets"] as System.Collections.ArrayList;
                    if (assets == null) return null;
                    foreach (object item in assets)
                    {
                        var asset = item as Dictionary<string, object>;
                        if (asset == null || Convert.ToString(asset["name"]) != "ControlConsole-update-" + version + ".zip") continue;
                        Uri url;
                        if (!Uri.TryCreate(Convert.ToString(asset["browser_download_url"]), UriKind.Absolute, out url) || url.Scheme != "https" || !url.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)) return null;
                        string updateDir = Path.Combine(DataDir, "updates"); Directory.CreateDirectory(updateDir);
                        string path = Path.Combine(updateDir, "ControlConsole-update-" + version + ".zip");
                        string temp = path + ".download";
                        try
                        {
                            client.DownloadFile(url, temp);
                            string digest = asset.ContainsKey("digest") ? Convert.ToString(asset["digest"]) : "";
                            if (!string.IsNullOrEmpty(digest))
                            {
                                if (!digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)) return null;
                                using (var file = File.OpenRead(temp)) using (var sha = SHA256.Create())
                                {
                                    string actual = BitConverter.ToString(sha.ComputeHash(file)).Replace("-", "");
                                    if (!actual.Equals(digest.Substring(7), StringComparison.OrdinalIgnoreCase)) return null;
                                }
                            }
                            Version embedded;
                            if (!ReadPackageVersion(temp, out embedded) || embedded != version) return null;
                            if (File.Exists(path)) File.Delete(path);
                            File.Move(temp, path);
                            return path;
                        }
                        finally { if (File.Exists(temp)) File.Delete(temp); }
                    }
                }
            }
            catch (Exception ex)
            {
                try { File.AppendAllText(Path.Combine(DataDir, "update.log"), DateTime.Now.ToString("s") + " GitHub update check: " + ex.Message + Environment.NewLine, Encoding.UTF8); } catch { }
            }
            return null;
        }
        private static bool ReadPackageVersion(string file, out Version version)
        {
            version = null;
            try
            {
                using (var archive = ZipFile.OpenRead(file))
                {
                    if (archive.GetEntry("ControlConsole.exe") == null || archive.GetEntry("ApplyUpdate.ps1") == null) return false;
                    var entry = archive.GetEntry("version.txt"); if (entry == null) return false;
                    using (var reader = new StreamReader(entry.Open()))
                    {
                        Version embedded; if (!Version.TryParse(reader.ReadToEnd().Trim(), out embedded)) return false;
                        version = embedded; return true;
                    }
                }
            }
            catch { return false; }
        }
        private static string Quote(string s) { return "\"" + (s ?? "").Replace("\"", "\\\"") + "\""; }
        private static void ReadUdp()
        {
            using (var udp = new UdpClient(UdpPort))
            {
                while (true)
                {
                    try
                    {
                        IPEndPoint remote = new IPEndPoint(IPAddress.Any, 0);
                        byte[] data = udp.Receive(ref remote);
                        MetricPacket p = Json.Deserialize<MetricPacket>(Encoding.UTF8.GetString(data));
                        if (p == null) continue;
                        lock (Gate) Update(p);
                    }
                    catch { }
                }
            }
        }
        private static void Update(MetricPacket p)
        {
            bool wasOffline = LastSeen == DateTime.MinValue || (DateTime.UtcNow - LastSeen).TotalSeconds > 5;
            Last = p; LastSeen = DateTime.UtcNow;
            var current = new HashSet<string>(p.games ?? new string[0], StringComparer.OrdinalIgnoreCase);
            foreach (string name in current)
                if (!ActiveSessions.ContainsKey(name))
                {
                    ActiveSessions[name] = new GameSession { id = Guid.NewGuid().ToString("N"), game = name, start = p.utc, end = 0 };
                    Sessions.Insert(0, ActiveSessions[name]);
                }
            foreach (string name in ActiveSessions.Keys.ToArray())
                if (!current.Contains(name)) { ActiveSessions[name].end = p.utc; ActiveSessions.Remove(name); }
            bool running = current.Count > 0;
            if (running)
            {
                ReturnAt = DateTime.MinValue;
                if (Role == "ame") BeginTransformation();
            }
            else if (AnyGame)
            {
                ReturnAt = DateTime.UtcNow.AddMinutes(10);
                    ThreadPool.QueueUserWorkItem(delegate { Thread.Sleep(TimeSpan.FromMinutes(10)); lock (Gate) { if (ReturnAt != DateTime.MinValue && DateTime.UtcNow >= ReturnAt && ActiveSessions.Count == 0) { Role = "ame"; ReturnAt = DateTime.MinValue; EventId++; } } });
            }
            AnyGame = running;
            if (wasOffline) UserClosed = false;
            if (!UserClosed && wasOffline) { LaunchBrowser(); RestoreBrowser(); }
            SaveSessions();
        }
        private static void BeginTransformation()
        {
            Role = "kangel"; EventId++;
        }
        private static void Handle(HttpListenerContext c)
        {
            string path = c.Request.Url.AbsolutePath.Trim('/');
            if (path == "state") { WriteJson(c, Snapshot()); return; }
            if (path == "session/edit") { EditSession(c); return; }
            if (path.StartsWith("window/", StringComparison.OrdinalIgnoreCase))
            {
                string action = path.Substring("window/".Length);
                if (action == "minimize") MinimizeBrowser(); else if (action == "close") { UserClosed = true; CloseBrowser(); c.Response.StatusCode = 204; c.Response.Close(); ThreadPool.QueueUserWorkItem(delegate { Thread.Sleep(250); Environment.Exit(0); }); return; } else if (action == "restore") RestoreBrowser();
                c.Response.StatusCode = 204; c.Response.Close(); return;
            }
            string file = path.Length == 0 ? Path.Combine(BaseDir, "console-game-faithful.html") : Path.Combine(BaseDir, path.Replace('/', Path.DirectorySeparatorChar));
            string full = Path.GetFullPath(file);
                string basePath = Path.GetFullPath(BaseDir).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                if (!full.StartsWith(basePath, StringComparison.OrdinalIgnoreCase) && !String.Equals(full, Path.GetFullPath(BaseDir), StringComparison.OrdinalIgnoreCase) || !File.Exists(full)) { c.Response.StatusCode = 404; c.Response.Close(); return; }
            string ext = Path.GetExtension(full).ToLowerInvariant();
            c.Response.ContentType = ext == ".html" ? "text/html; charset=utf-8" : ext == ".png" ? "image/png" : ext == ".jpg" || ext == ".jpeg" ? "image/jpeg" : ext == ".gif" ? "image/gif" : ext == ".css" ? "text/css" : "application/octet-stream";
            byte[] bytes = File.ReadAllBytes(full); c.Response.ContentLength64 = bytes.Length; c.Response.OutputStream.Write(bytes, 0, bytes.Length); c.Response.Close();
        }
        private static void EditSession(HttpListenerContext c)
        {
            if (c.Request.HttpMethod != "POST" || String.IsNullOrEmpty(c.Request.ContentType) || !c.Request.ContentType.StartsWith("application/json", StringComparison.OrdinalIgnoreCase) || c.Request.ContentLength64 < 0 || c.Request.ContentLength64 > 4096)
            { c.Response.StatusCode = 400; c.Response.Close(); return; }
            string origin = c.Request.Headers["Origin"];
            if (origin != null && origin != "http://127.0.0.1:" + HttpPort)
            { c.Response.StatusCode = 403; c.Response.Close(); return; }
            try
            {
                string body;
                using (var reader = new StreamReader(c.Request.InputStream, Encoding.UTF8)) body = reader.ReadToEnd();
                var edit = Json.Deserialize<Dictionary<string, object>>(body);
                string id = Convert.ToString(edit["id"]);
                lock (Gate)
                {
                    var session = Sessions.FirstOrDefault(s => s.id == id);
                    if (session == null) { c.Response.StatusCode = 404; c.Response.Close(); return; }
                    if (ActiveSessions.Values.Contains(session)) { c.Response.StatusCode = 409; c.Response.Close(); return; }
                    if (edit.ContainsKey("delete") && Convert.ToBoolean(edit["delete"])) Sessions.Remove(session);
                    else
                    {
                        string game = Convert.ToString(edit["game"]).Trim();
                        long end = Convert.ToInt64(edit["end"]);
                        if (game.Length == 0 || game.Length > 100 || end < session.start || end > DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + 60000)
                        { c.Response.StatusCode = 400; c.Response.Close(); return; }
                        session.game = game; session.end = end;
                    }
                    SaveSessions();
                }
                c.Response.StatusCode = 204; c.Response.Close();
            }
            catch { c.Response.StatusCode = 400; c.Response.Close(); }
        }
        private static DashboardState Snapshot()
        {
            lock (Gate)
            {
                bool online = Last != null && (DateTime.UtcNow - LastSeen).TotalSeconds < 5;
                if (!online && (ActiveSessions.Count > 0 || AnyGame))
                {
                    long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                    foreach (var s in ActiveSessions.Values) s.end = now;
                    ActiveSessions.Clear(); AnyGame = false; ReturnAt = DateTime.UtcNow.AddMinutes(10); SaveSessions();
                    ThreadPool.QueueUserWorkItem(delegate { Thread.Sleep(TimeSpan.FromMinutes(10)); lock (Gate) { if (!AnyGame && ReturnAt != DateTime.MinValue && DateTime.UtcNow >= ReturnAt) { Role = "ame"; ReturnAt = DateTime.MinValue; EventId++; } } });
                }
                return new DashboardState { online = online, host = Last == null ? "等待主机连接" : Last.host, cpu = !online || Last == null ? -1 : Last.cpu, memoryUsed = !online || Last == null ? 0 : Last.memoryUsed, memoryTotal = !online || Last == null ? 0 : Last.memoryTotal, gpu = !online || Last == null ? -1 : Last.gpu, cpuTemp = !online || Last == null ? -1 : Last.cpuTemp, gpuTemp = !online || Last == null ? -1 : Last.gpuTemp, games = !online || Last == null ? new string[0] : Last.games, role = Role, roleEvent = EventId, returnInSeconds = ReturnAt == DateTime.MinValue ? 0 : Math.Max(0, (long)(ReturnAt - DateTime.UtcNow).TotalSeconds), sessions = Sessions.Take(1000).ToArray() };
            }
        }
        private static void WriteJson(HttpListenerContext c, object value)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(Json.Serialize(value)); c.Response.ContentType = "application/json; charset=utf-8"; c.Response.Headers.Add("Access-Control-Allow-Origin", "*"); c.Response.ContentLength64 = bytes.Length; c.Response.OutputStream.Write(bytes, 0, bytes.Length); c.Response.Close();
        }
        private static string SessionFile { get { return Path.Combine(DataDir, "sessions.json"); } }
        private static void LoadSessions()
        {
            try
            {
                if (!File.Exists(SessionFile)) return;
                long lastSaved = new DateTimeOffset(File.GetLastWriteTimeUtc(SessionFile)).ToUnixTimeMilliseconds();
                Sessions.AddRange(Json.Deserialize<GameSession[]>(File.ReadAllText(SessionFile)) ?? new GameSession[0]);
                if (CloseInterruptedSessions(Sessions, lastSaved)) SaveSessions();
            }
            catch { }
        }
        private static bool CloseInterruptedSessions(IEnumerable<GameSession> sessions, long lastSaved)
        {
            bool repaired = false;
            foreach (var session in sessions)
            {
                if (String.IsNullOrEmpty(session.id)) { session.id = Guid.NewGuid().ToString("N"); repaired = true; }
                if (session.end == 0) { session.end = Math.Max(session.start, lastSaved); repaired = true; }
            }
            return repaired;
        }
        private static void SaveSessions() { try { File.WriteAllText(SessionFile, Json.Serialize(Sessions.Take(1000).ToArray()), Encoding.UTF8); } catch { } }
        private static void LaunchBrowser()
        {
            if (Browser != null && !Browser.HasExited) return;
            string[] paths = { Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Microsoft", "Edge", "Application", "msedge.exe"), Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Microsoft", "Edge", "Application", "msedge.exe") };
            string edge = paths.FirstOrDefault(File.Exists); if (edge == null) return;
            try { Browser = Process.Start(new ProcessStartInfo(edge, "--app=http://127.0.0.1:" + HttpPort + "/ --start-fullscreen --no-first-run") { UseShellExecute = false, CreateNoWindow = true }); } catch { }
        }
        private static void RestoreBrowser() { try { LaunchBrowser(); IntPtr h = FindConsoleWindow(); if (h != IntPtr.Zero) { ShowWindow(h, 9); SetForegroundWindow(h); } } catch { } }
        private static void MinimizeBrowser() { try { IntPtr h = FindConsoleWindow(); if (h != IntPtr.Zero) ShowWindow(h, 6); } catch { } }
        private static void CloseBrowser() { try { IntPtr h = FindConsoleWindow(); if (h != IntPtr.Zero) PostMessage(h, 0x0010, IntPtr.Zero, IntPtr.Zero); } catch { } }
        private static IntPtr FindConsoleWindow()
        {
            IntPtr found = IntPtr.Zero;
            EnumWindows(delegate(IntPtr h, IntPtr l)
            {
                int pid; GetWindowThreadProcessId(h, out pid); if (pid == 0) return true;
                try { using (var p = Process.GetProcessById(pid)) { if (!String.Equals(p.ProcessName, "msedge", StringComparison.OrdinalIgnoreCase)) return true; } } catch { return true; }
                var title = new StringBuilder(512); GetWindowText(h, title, title.Capacity);
                if (title.ToString().IndexOf("副机控制台", StringComparison.OrdinalIgnoreCase) >= 0 || title.ToString().IndexOf("游戏界面还原稿", StringComparison.OrdinalIgnoreCase) >= 0) { found = h; return false; }
                return true;
            }, IntPtr.Zero);
            return found;
        }
        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr extraData);
        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int maxCount);
        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out int processId);
        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
    }

    internal sealed class TrayContext : ApplicationContext
    {
        private readonly NotifyIcon Icon;
        public TrayContext()
        {
            var menu = new ContextMenuStrip();
            menu.Items.Add("打开控制台", null, delegate { typeof(DisplayServer).GetMethod("RestoreBrowser", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static).Invoke(null, null); });
            menu.Items.Add("退出程序", null, delegate { Environment.Exit(0); });
            Icon = new NotifyIcon { Text = "局域网桌面控制台", Icon = SystemIcons.Application, ContextMenuStrip = menu, Visible = true };
            Icon.DoubleClick += delegate { typeof(DisplayServer).GetMethod("RestoreBrowser", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static).Invoke(null, null); };
        }
        protected override void ExitThreadCore() { Icon.Visible = false; Icon.Dispose(); base.ExitThreadCore(); }
    }

    internal sealed class HostTrayContext : ApplicationContext
    {
        private readonly NotifyIcon Icon;
        public HostTrayContext()
        {
            var menu = new ContextMenuStrip();
            var status = new ToolStripMenuItem("主机监控运行中") { Enabled = false };
            menu.Items.Add(status);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("退出主机监控", null, delegate { Environment.Exit(0); });
            Icon = new NotifyIcon { Text = "局域网桌面控制台 · 主机监控", Icon = SystemIcons.Application, ContextMenuStrip = menu, Visible = true };
        }
        protected override void ExitThreadCore() { Icon.Visible = false; Icon.Dispose(); base.ExitThreadCore(); }
    }
}

