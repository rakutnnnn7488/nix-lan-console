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
using System.Runtime.InteropServices;
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
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.ThreadException += delegate(object sender, ThreadExceptionEventArgs e) { StartupDiagnostics.Write("UI crash: " + e.Exception); Environment.Exit(1); };
                AppDomain.CurrentDomain.UnhandledException += delegate(object sender, UnhandledExceptionEventArgs e) { StartupDiagnostics.Write("Unhandled crash: " + e.ExceptionObject); };
                StartupDiagnostics.Write("launch version=" + BuildInfo.Version + " role=" + (args.Length == 0 ? "picker" : args[0]) + " pid=" + Process.GetCurrentProcess().Id);
                if (args.Length > 0 && args[0] == "--sensor")
                {
                    bool created;
                    using (var mutex = new Mutex(true, "Local\\NixLanConsole.Host", out created))
                        if (created) HostSensor.Run(); else Environment.ExitCode = 3;
                    return;
                }
                if (args.Length > 0 && args[0] == "--display")
                {
                    bool created;
                    using (var mutex = new Mutex(true, "Local\\NixLanConsole.Display", out created))
                        if (created) DisplayServer.Run(); else Environment.ExitCode = 3;
                    return;
                }
                if (args.Length > 0 && args[0] == "--native-preview")
                {
                    Application.Run(new NativeConsoleWindow());
                    return;
                }
                Application.Run(new RolePicker());
            }
            catch (Exception ex)
            {
                StartupDiagnostics.Write("startup failure: " + ex);
                MessageBox.Show(ex.Message, "局域网桌面控制台", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }

    internal static class NixPaths
    {
        internal static string DataDir
        {
            get
            {
                string custom = Environment.GetEnvironmentVariable("NIXLAN_DATA_DIR");
                if (!String.IsNullOrWhiteSpace(custom)) return Path.GetFullPath(custom);
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NixLanConsole");
            }
        }
    }

    internal static class StartupDiagnostics
    {
        private static readonly string DataDir = NixPaths.DataDir;
        public static void Write(string message)
        {
            try { Directory.CreateDirectory(DataDir); File.AppendAllText(Path.Combine(DataDir, "update.log"), DateTime.Now.ToString("s") + " " + message + Environment.NewLine, Encoding.UTF8); } catch { }
        }
        public static void Ready(string role)
        {
            try
            {
                Directory.CreateDirectory(DataDir);
                File.WriteAllText(Path.Combine(DataDir, "startup-ready.txt"), Process.GetCurrentProcess().Id + "|" + BuildInfo.Version + "|" + role, Encoding.UTF8);
                // A running new version proves the last update succeeded; clear the 24h retry gate.
                string failed = Path.Combine(DataDir, "failed-update.txt");
                if (File.Exists(failed) && String.Equals(File.ReadAllText(failed, Encoding.UTF8).Trim(), BuildInfo.Version, StringComparison.OrdinalIgnoreCase)) File.Delete(failed);
            }
            catch { }
            Write("ready version=" + BuildInfo.Version + " role=" + role + " pid=" + Process.GetCurrentProcess().Id);
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
            var gameInfo = new Label { Text = "游戏时长会自动识别 Steam、Epic 和常见游戏进程，无需填写 exe。", Location = new Point(34, 214), Size = new Size(500, 35), Font = new Font("Microsoft YaHei", 10), ForeColor = Color.FromArgb(64, 49, 142) };
            var updateLabel = new Label { Text = "副机自动更新：在副机打开此对话并下载新版，程序会自动安装。", Location = new Point(34, 264), Size = new Size(490, 25), Font = new Font("Microsoft YaHei", 9), ForeColor = Color.FromArgb(64, 49, 142) };
            Controls.Add(title); Controls.Add(info); Controls.Add(host); Controls.Add(display); Controls.Add(gameInfo); Controls.Add(updateLabel);
        }
        private void AddGame()
        {
            using (var dialog = new OpenFileDialog { Filter = "Windows 程序 (*.exe)|*.exe", Title = "选择要追踪的游戏程序", CheckFileExists = true })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                string label = Path.GetFileNameWithoutExtension(dialog.FileName);
                string dir = NixPaths.DataDir;
                Directory.CreateDirectory(dir); string file = Path.Combine(dir, "tracked-games.txt");
                string executable = Path.GetFullPath(dialog.FileName);
                var lines = File.Exists(file) ? File.ReadAllLines(file).Where(x => !x.TrimStart().StartsWith(executable + "|", StringComparison.OrdinalIgnoreCase)).ToList() : new List<string>();
                lines.Add(executable + "|" + label); File.WriteAllLines(file, lines, Encoding.UTF8);
                MessageBox.Show(this, "已添加“" + label + "”。主机后台会自动读取新设置。", "游戏追踪", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }
        private void Configure(string role, string taskName)
        {
            // Autostart must point at the stable launcher, not this versioned app.
            string exe = BuildInfo.LauncherPath;
            if (!File.Exists(exe)) exe = Application.ExecutablePath;
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
        public string bootId;
        public string version;
        public long utc;
        public double cpu;
        public double memoryUsed;
        public double memoryTotal;
        public double gpu;
        public double cpuTemp;
        public double gpuTemp;
        public string[] games;
        public string[] catalog;
        public GameDetection[] detections;
        public string foliaKey;
        public bool foliaConnected;
        public bool foliaPlaying;
        public bool foliaRunning;
        public string foliaSong;
        public string foliaArtist;
        public bool foliaCover;
    }

    internal sealed class GameDetection
    {
        public string key;
        public string process;
        public string game;
        public string source;
        public long seen;
    }

    internal sealed class ControlPacket
    {
        public string[] excludedGames;
        public bool hostAutoUpdate = true;
        public Dictionary<string, string> gameOverrides;
    }

    internal sealed class ConsoleSettings
    {
        public bool autoUpdate = true;
        public bool hostAutoUpdate = true;
        public string[] excludedGames = new[] { "Wallpaper Engine" };
        public string hostAddress = "192.168.1.7";
        public string foliaExePath = "";
        public Dictionary<string, string> gameOverrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public string[] reviewedGameKeys = new string[0];
    }

    internal sealed class HostSettings
    {
        public bool autoUpdate = true;
        public string[] excludedGames = new string[0];
    }

    internal sealed class HostSettingsState
    {
        public bool autoUpdate;
        public string[] excludedGames = new string[0];
        public string[] catalog = new string[0];
        public string[] currentGames = new string[0];
        public string version;
        public string machine;
        public bool startWithWindows;
    }

    internal sealed class UpdateStatus
    {
        public string state;
        public int percent;
        public string version;
        public string message;
    }

    internal static class HostSensor
    {
        private const int TcpPort = 46702;
        private static readonly string DataDir = NixPaths.DataDir;
        private static readonly JavaScriptSerializer Json = new JavaScriptSerializer();
        private static readonly string BootId = Guid.NewGuid().ToString("N");
        private static PerformanceCounter CpuCounter;
        private static readonly object SlowSensorGate = new object();
        private static double GpuUsage = -1;
        private static double GpuTemperature = -1;
        private static double CpuTemperature = -1;
        private static DateTime LastTemperatureRead = DateTime.MinValue;
        private static DateTime LastGameConfigRead = DateTime.MinValue;
        private static DateTime LastGameDiscovery = DateTime.MinValue;
        private static readonly Dictionary<string, string> GameExecutables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, string> CustomGameExecutables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        // Only distinctive game process names belong here. Generic engine and launcher names are deliberately absent.
        private static readonly Dictionary<string, string> KnownGameProcesses = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "Endfield.exe", "明日方舟：终末地" }, { "GenshinImpact.exe", "原神" }, { "YuanShen.exe", "原神" },
            { "StarRail.exe", "崩坏：星穹铁道" }, { "ZenlessZoneZero.exe", "绝区零" },
            { "Wuthering Waves.exe", "鸣潮" },
            { "VALORANT-Win64-Shipping.exe", "无畏契约" }, { "League of Legends.exe", "英雄联盟" },
            { "Overwatch.exe", "守望先锋" }, { "Diablo IV.exe", "暗黑破坏神 IV" },
            { "FortniteClient-Win64-Shipping.exe", "堡垒之夜" }, { "Minecraft.Windows.exe", "我的世界" },
            { "Warframe.x64.exe", "Warframe" }, { "r5apex.exe", "Apex Legends" }, { "r5apex_dx12.exe", "Apex Legends" },
            { "TslGame.exe", "绝地求生" }, { "Cyberpunk2077.exe", "赛博朋克 2077" },
            { "eldenring.exe", "艾尔登法环" }, { "RDR2.exe", "荒野大镖客 2" },
            { "GTA5.exe", "GTA V" }, { "MonsterHunterWorld.exe", "怪物猎人：世界" },
            { "MonsterHunterRise.exe", "怪物猎人：崛起" }, { "MonsterHunterWilds.exe", "怪物猎人：荒野" },
            { "Windose.exe", "NEEDY GIRL OVERDOSE" }, { "cs2.exe", "Counter-Strike 2" },
            { "SlayTheSpire2.exe", "Slay the Spire 2" }, { "P3R.exe", "Persona 3 Reload" }
        };
        private static readonly object ControlGate = new object();
        private static HashSet<string> RemoteExcludedGames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static Dictionary<string, string> RemoteGameOverrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static volatile bool RemoteAutoUpdate = true;
        private static System.Threading.Timer UpdateTimer;
        private static volatile bool GameRunning;
        private static readonly object MetricGate = new object();
        private static MetricPacket LatestPacket;
        private static readonly string HostSettingsFile = Path.Combine(DataDir, "host-settings.json");
        private static readonly object HostSettingsGate = new object();
        private static volatile bool LocalAutoUpdate = true;
        private static volatile string[] LocalExcludedGames = new string[0];
        private static TcpListener ActiveListener;
        internal static bool HasGameRunning { get { return GameRunning; } }
        internal static bool AutoUpdateEnabled { get { return LocalAutoUpdate && RemoteAutoUpdate; } }

        public static void Run()
        {
            Directory.CreateDirectory(DataDir);
            LoadHostSettings();
            AppDomain.CurrentDomain.ProcessExit += delegate
            {
                try { TcpListener listener = ActiveListener; if (listener != null) listener.Stop(); } catch { }
            };
            StartTray();
            FoliaLanRelay.Start();
            UpdateTimer = new System.Threading.Timer(DisplayServer.CheckForUpdate, "--sensor", TimeSpan.FromSeconds(20), TimeSpan.FromMinutes(5));
            try { CpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total"); CpuCounter.NextValue(); } catch { }
            lock (MetricGate)
                LatestPacket = new MetricPacket { host = Environment.MachineName, bootId = BootId, version = BuildInfo.Version,
                    utc = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), cpu = -1, gpu = -1, cpuTemp = -1, gpuTemp = -1,
                    games = new string[0], catalog = new string[0], foliaKey = FoliaLanRelay.AccessKey };
            var tcpThread = new Thread(TcpServerLoop) { IsBackground = true };
            tcpThread.Start();
            var slowSensorThread = new Thread(SlowSensorLoop) { IsBackground = true };
            slowSensorThread.Start();
            DiscoverGames();
            LoadCustomGames();
            while (true)
            {
                double gpu, cpuTemp, gpuTemp;
                lock (SlowSensorGate) { gpu = GpuUsage; cpuTemp = CpuTemperature; gpuTemp = GpuTemperature; }
                GameDetection[] detections;
                string[] games = ReadGames(out detections);
                var p = new MetricPacket { host = Environment.MachineName, bootId = BootId, version = BuildInfo.Version, utc = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), cpu = ReadCpu(), memoryUsed = 0, memoryTotal = 0, gpu = gpu, cpuTemp = cpuTemp, gpuTemp = gpuTemp, games = games, detections = detections, catalog = GetGameCatalog(), foliaKey = FoliaLanRelay.AccessKey, foliaConnected = FoliaLanRelay.Connected, foliaPlaying = FoliaLanRelay.Playing, foliaRunning = FoliaLanRelay.SourceRunning, foliaSong = FoliaLanRelay.Song, foliaArtist = FoliaLanRelay.Artist, foliaCover = FoliaLanRelay.HasCover };
                ReadMemory(out p.memoryUsed, out p.memoryTotal);
                lock (MetricGate) LatestPacket = p;
                Thread.Sleep(1000);
            }
        }
        private static void SlowSensorLoop()
        {
            while (true)
            {
                double gpu = ReadGpuUsage();
                if ((DateTime.UtcNow - LastTemperatureRead).TotalSeconds >= 5)
                {
                    ReadTemperatures();
                    LastTemperatureRead = DateTime.UtcNow;
                }
                lock (SlowSensorGate) GpuUsage = gpu;
                Thread.Sleep(2000);
            }
        }
        private static void TcpServerLoop()
        {
            while (true)
            {
                TcpListener listener = null;
                try
                {
                    listener = new TcpListener(IPAddress.Any, TcpPort);
                    // Allow binding while the previous app's listening socket is in TIME_WAIT
                    // (or inherited by the short-lived launcher during an update).
                    listener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                    listener.Start();
                    ActiveListener = listener;
                    StartupDiagnostics.Write("TCP listener ready port=" + TcpPort);
                    StartupDiagnostics.Ready("--sensor");
                    while (true)
                    {
                        TcpClient client = listener.AcceptTcpClient();
                        ThreadPool.QueueUserWorkItem(delegate(object state) { ServeClient((TcpClient)state); }, client);
                    }
                }
                catch (Exception ex) { StartupDiagnostics.Write("TCP listener retry: " + ex.Message); Thread.Sleep(5000); }
                finally { if (listener != null) { if (Object.ReferenceEquals(ActiveListener, listener)) ActiveListener = null; try { listener.Stop(); } catch { } } }
            }
        }
        private static void ServeClient(TcpClient client)
        {
            using (client)
            {
                try
                {
                    client.ReceiveTimeout = 2500; client.SendTimeout = 2500;
                    using (var stream = client.GetStream())
                    using (var reader = new StreamReader(stream, Encoding.UTF8))
                    using (var writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true })
                    {
                        string line = reader.ReadLine();
                        if (line != null && line.StartsWith("GET /", StringComparison.Ordinal))
                        {
                            if (line.StartsWith("GET /asset-bundle?", StringComparison.Ordinal))
                                AssetBundleSync.Serve(client, stream, reader, line);
                            else FoliaLanRelay.ServeHttp(client, stream, reader, line);
                            return;
                        }
                        if (String.IsNullOrEmpty(line) || line.Length > 65536) return;
                        var control = Json.Deserialize<ControlPacket>(line);
                        if (control != null)
                        {
                            lock (ControlGate)
                            {
                                RemoteExcludedGames = new HashSet<string>((control.excludedGames ?? new string[0]).Where(x => !String.IsNullOrWhiteSpace(x)), StringComparer.OrdinalIgnoreCase);
                                RemoteGameOverrides = new Dictionary<string, string>((control.gameOverrides ?? new Dictionary<string, string>()).Where(x => !String.IsNullOrWhiteSpace(x.Key)).ToDictionary(x => x.Key, x => x.Value ?? "", StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase);
                            }
                            RemoteAutoUpdate = control.hostAutoUpdate;
                        }
                        string packet = null;
                        lock (MetricGate)
                        {
                            MetricPacket current = LatestPacket;
                            if (current != null)
                            {
                                current.foliaKey = FoliaLanRelay.AccessKey;
                                current.foliaConnected = FoliaLanRelay.Connected;
                                current.foliaPlaying = FoliaLanRelay.Playing;
                                current.foliaRunning = FoliaLanRelay.SourceRunning;
                                current.foliaSong = FoliaLanRelay.Song;
                                current.foliaArtist = FoliaLanRelay.Artist;
                                current.foliaCover = FoliaLanRelay.HasCover;
                                packet = Json.Serialize(current);
                            }
                        }
                        if (packet != null) writer.WriteLine(packet);
                    }
                }
                catch (Exception ex) { StartupDiagnostics.Write("TCP client error: " + ex.Message); }
            }
        }
        private static bool IsSystemUtility(string path, string title)
        {
            string exe = Path.GetFileNameWithoutExtension(path);
            return String.Equals(title, "Wallpaper Engine", StringComparison.OrdinalIgnoreCase) ||
                Regex.IsMatch(exe, @"wallpaper|launcher|updat|patch|crash|report|unins|setup|install|anticheat|easyanticheat|battleye|cef|webengine|webhelper|platformprocess|unitycrashhandler|bootstrap|redistributable|dedicatedserver|cleanup|touchup|vconsole|createdump", RegexOptions.IgnoreCase);
        }
        private static string[] GetGameCatalog()
        {
            return GameExecutables.Concat(CustomGameExecutables).Where(x => !IsSystemUtility(x.Key, x.Value)).Select(x => x.Value).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToArray();
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
            try
            {
                var status = new MemoryStatusEx { Length = (uint)Marshal.SizeOf(typeof(MemoryStatusEx)) };
                if (!GlobalMemoryStatusEx(ref status) || status.TotalPhysical == 0) return;
                total = status.TotalPhysical / 1048576.0;
                used = (status.TotalPhysical - Math.Min(status.AvailablePhysical, status.TotalPhysical)) / 1048576.0;
            }
            catch { }
        }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct MemoryStatusEx
        {
            public uint Length;
            public uint MemoryLoad;
            public ulong TotalPhysical;
            public ulong AvailablePhysical;
            public ulong TotalPageFile;
            public ulong AvailablePageFile;
            public ulong TotalVirtual;
            public ulong AvailableVirtual;
            public ulong AvailableExtendedVirtual;
        }
        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);
        private static double ReadGpuUsage()
        {
            double sum = 0;
            try
            {
                using (var s = new ManagementObjectSearcher("root\\cimv2", "SELECT UtilizationPercentage FROM Win32_PerfFormattedData_GPUPerformanceCounters_GPUEngine"))
                using (var results = s.Get())
                    foreach (ManagementObject m in results) using (m) sum += Convert.ToDouble(m["UtilizationPercentage"]);
            }
            catch { return -1; }
            return Math.Min(100, Math.Round(sum, 1));
        }
        private static void ReadTemperatures()
        {
            double cpuTemperature = -1;
            try
            {
                using (var s = new ManagementObjectSearcher("root\\wmi", "SELECT CurrentTemperature FROM MSAcpi_ThermalZoneTemperature"))
                using (var results = s.Get())
                    foreach (ManagementObject m in results) using (m) { double t = Convert.ToDouble(m["CurrentTemperature"]) / 10.0 - 273.15; if (t > 0 && t < 120) { cpuTemperature = Math.Round(t, 0); break; } }
            }
            catch { }
            if (cpuTemperature >= 0) lock (SlowSensorGate) CpuTemperature = cpuTemperature;
            try
            {
                var psi = new ProcessStartInfo("nvidia-smi.exe", "--query-gpu=temperature.gpu --format=csv,noheader,nounits") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = false };
                using (var p = Process.Start(psi))
                {
                    var output = p.StandardOutput.ReadToEndAsync();
                    if (!p.WaitForExit(1500)) { p.Kill(); p.WaitForExit(); }
                    string result = output.GetAwaiter().GetResult();
                    double t;
                    if (p.ExitCode == 0 && double.TryParse(result.Trim().Split('\n')[0], out t)) lock (SlowSensorGate) GpuTemperature = t;
                }
            }
            catch { }
        }
        private static string[] ReadGames(out GameDetection[] detections)
        {
            LoadCustomGames();
            if ((DateTime.UtcNow - LastGameDiscovery).TotalMinutes >= 5) DiscoverGames();
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var found = new Dictionary<string, GameDetection>(StringComparer.OrdinalIgnoreCase);
            HashSet<string> excluded;
            Dictionary<string, string> overrides;
            lock (ControlGate)
            {
                excluded = new HashSet<string>(RemoteExcludedGames, StringComparer.OrdinalIgnoreCase);
                overrides = new Dictionary<string, string>(RemoteGameOverrides, StringComparer.OrdinalIgnoreCase);
            }
            foreach (string name in LocalExcludedGames ?? new string[0]) if (!String.IsNullOrWhiteSpace(name)) excluded.Add(name);
            excluded.Add("Wallpaper Engine");
            foreach (var p in Process.GetProcesses())
            {
                try
                {
                    string path = null;
                    try { path = Path.GetFullPath(p.MainModule.FileName); } catch { }
                    string title, source;
                    string key = path ?? (p.ProcessName + ".exe");
                    if (!TryResolveGame(path, p.ProcessName, out title, out source)) continue;
                    string corrected;
                    if (overrides.TryGetValue(key, out corrected)) { title = corrected; source = "用户规则"; }
                    if (String.IsNullOrWhiteSpace(title) || excluded.Contains(title)) continue;
                    names.Add(title);
                    if (found.Count < 64 && !found.ContainsKey(key)) found[key] = new GameDetection { key = key, process = p.ProcessName + ".exe", game = title, source = source, seen = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() };
                }
                catch { }
                finally { p.Dispose(); }
            }
            GameRunning = names.Count > 0;
            detections = found.Values.ToArray();
            return names.ToArray();
        }
        private static bool TryResolveGame(string path, string processName, out string title, out string source)
        {
            title = source = null;
            if (!String.IsNullOrWhiteSpace(path) && CustomGameExecutables.TryGetValue(path, out title)) { source = "手动添加"; return !IsSystemUtility(path, title); }
            if (!String.IsNullOrWhiteSpace(path) && GameExecutables.TryGetValue(path, out title)) { source = "安装目录自动发现"; return !IsSystemUtility(path, title); }
            if (String.IsNullOrWhiteSpace(processName)) return false;
            string exe = Path.GetFileName(processName);
            if (!exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) exe += ".exe";
            source = "内置名单";
            return KnownGameProcesses.TryGetValue(exe, out title) && !IsSystemUtility(exe, title);
        }
        private static void DiscoverGames()
        {
            LastGameDiscovery = DateTime.UtcNow;
            GameExecutables.Clear();
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
                        string appId = Regex.Match(Path.GetFileName(file), @"appmanifest_(\d+)\.acf", RegexOptions.IgnoreCase).Groups[1].Value;
                        if (appId == "431960" || appId == "1812620" || appId == "228980") continue;
                        string data = File.ReadAllText(file); var im = Regex.Match(data, "\\\"name\\\"\\s+\\\"([^\\\"]+)\\\""); var dm = Regex.Match(data, "\\\"installdir\\\"\\s+\\\"([^\\\"]+)\\\"");
                        if (!im.Success || !dm.Success) continue;
                        string dir = Path.Combine(root, dm.Groups[1].Value); if (!Directory.Exists(dir)) continue;
                        foreach (string exe in Directory.GetFiles(dir, "*.exe", SearchOption.AllDirectories))
                        {
                            string lower = exe.ToLowerInvariant(); if (lower.Contains("\\redist\\") || lower.Contains("\\_commonredist\\") || lower.Contains("\\__installer\\") || lower.Contains("\\engine\\binaries\\") || lower.Contains("\\easyanticheat\\") || lower.Contains("\\battleye\\") || lower.Contains("\\anticheat\\") || lower.Contains("\\tools\\")) continue;
                            string proc = Path.GetFileNameWithoutExtension(exe); if (proc.Length < 3 || IsSystemUtility(exe, im.Groups[1].Value)) continue;
                            GameExecutables[Path.GetFullPath(exe)] = im.Groups[1].Value.Trim();
                        }
                    }
                }
                catch { }
            }
            DiscoverEpicGames();
        }
        private static void DiscoverEpicGames()
        {
            try
            {
                string manifests = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Epic", "EpicGamesLauncher", "Data", "Manifests");
                if (!Directory.Exists(manifests)) return;
                foreach (string file in Directory.GetFiles(manifests, "*.item"))
                {
                    try
                    {
                        var item = Json.Deserialize<Dictionary<string, object>>(File.ReadAllText(file));
                        string title = Convert.ToString(item["DisplayName"]), dir = Convert.ToString(item["InstallLocation"]), launch = Convert.ToString(item["LaunchExecutable"]);
                        if (String.IsNullOrWhiteSpace(title) || String.IsNullOrWhiteSpace(dir) || String.IsNullOrWhiteSpace(launch) || Path.IsPathRooted(launch)) continue;
                        string root = Path.GetFullPath(dir), exe = Path.GetFullPath(Path.Combine(root, launch));
                        if (!exe.StartsWith(root.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase) || !File.Exists(exe) || IsSystemUtility(exe, title)) continue;
                        GameExecutables[exe] = title;
                    }
                    catch { }
                }
            }
            catch { }
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

        private static void LoadHostSettings()
        {
            try
            {
                if (!File.Exists(HostSettingsFile)) return;
                var settings = Json.Deserialize<HostSettings>(File.ReadAllText(HostSettingsFile, Encoding.UTF8));
                if (settings == null) return;
                LocalAutoUpdate = settings.autoUpdate;
                LocalExcludedGames = (settings.excludedGames ?? new string[0]).Where(x => !String.IsNullOrWhiteSpace(x)).ToArray();
            }
            catch (Exception ex) { StartupDiagnostics.Write("host settings load: " + ex.Message); }
        }

        internal static void SaveHostSettings(bool autoUpdate, IEnumerable<string> excludedGames)
        {
            var names = new HashSet<string>(excludedGames ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            names.Remove("Wallpaper Engine");
            if (names.Count > 256 || names.Any(x => String.IsNullOrWhiteSpace(x) || x.Length > 100)) throw new ArgumentException("游戏排除列表无效");
            var settings = new HostSettings { autoUpdate = autoUpdate, excludedGames = names.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray() };
            lock (HostSettingsGate)
            {
                Directory.CreateDirectory(DataDir);
                File.WriteAllText(HostSettingsFile, Json.Serialize(settings), Encoding.UTF8);
                LocalAutoUpdate = autoUpdate;
                LocalExcludedGames = settings.excludedGames;
            }
        }

        internal static HostSettingsState GetSettingsState()
        {
            MetricPacket packet;
            lock (MetricGate) packet = LatestPacket;
            return new HostSettingsState
            {
                autoUpdate = LocalAutoUpdate,
                excludedGames = (LocalExcludedGames ?? new string[0]).ToArray(),
                catalog = packet == null || packet.catalog == null ? new string[0] : (string[])packet.catalog.Clone(),
                currentGames = packet == null || packet.games == null ? new string[0] : (string[])packet.games.Clone(),
                version = BuildInfo.Version,
                machine = Environment.MachineName,
                startWithWindows = IsHostAutostartEnabled()
            };
        }

        internal static string AddCustomGame(string path)
        {
            path = Path.GetFullPath(path);
            if (!File.Exists(path) || !path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("请选择有效的游戏 exe。");
            string label = Path.GetFileNameWithoutExtension(path);
            string file = Path.Combine(DataDir, "tracked-games.txt");
            Directory.CreateDirectory(DataDir);
            var lines = File.Exists(file) ? File.ReadAllLines(file).ToList() : new List<string>();
            lines.RemoveAll(delegate(string line)
            {
                int bar = line.IndexOf('|');
                if (bar <= 0) return false;
                try { return String.Equals(Path.GetFullPath(line.Substring(0, bar).Trim()), path, StringComparison.OrdinalIgnoreCase); }
                catch { return false; }
            });
            lines.Add(path + "|" + label);
            File.WriteAllLines(file, lines, Encoding.UTF8);
            LastGameConfigRead = DateTime.MinValue;
            LoadCustomGames();
            return label;
        }

        internal static bool IsHostAutostartEnabled()
        {
            try
            {
                using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run"))
                {
                    string value = Convert.ToString(key == null ? null : key.GetValue("NixLanConsoleHost"));
                    return !String.IsNullOrWhiteSpace(value) && value.IndexOf("--sensor", StringComparison.OrdinalIgnoreCase) >= 0;
                }
            }
            catch { return false; }
        }

        internal static void SetHostAutostart(bool enabled)
        {
            using (var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run"))
            {
                if (enabled)
                {
                    string exe = BuildInfo.LauncherPath;
                    if (!File.Exists(exe)) exe = Application.ExecutablePath;
                    key.SetValue("NixLanConsoleHost", "\"" + exe + "\" --sensor", Microsoft.Win32.RegistryValueKind.String);
                }
                else key.DeleteValue("NixLanConsoleHost", false);
            }
        }

        internal static void OpenFirewallPort()
        {
            string script = Path.Combine(BuildInfo.InstallDir, "OpenHostTcpPort.ps1");
            if (!File.Exists(script)) throw new FileNotFoundException("找不到 OpenHostTcpPort.ps1", script);
            Process.Start(new ProcessStartInfo("powershell.exe", "-NoProfile -ExecutionPolicy Bypass -File \"" + script + "\"")
            {
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden
            });
        }

        internal static void InstallUpdate()
        {
            if (GameRunning) throw new InvalidOperationException("游戏仍在运行，请退出后再安装更新。");
            ThreadPool.QueueUserWorkItem(delegate { DisplayServer.CheckForUpdate("--sensor-force"); });
        }
    }

    internal sealed class GameSession
    {
        public string id;
        public string game;
        public long start;
        public long end;
        public GameMetricSample[] samples;
    }
    internal sealed class GameMetricSample
    {
        public long utc;
        public double cpu, gpu, cpuTemp, gpuTemp;
    }
    internal sealed class DashboardState
    {
        public bool online;
        public string host;
        public string hostAddress;
        public double cpu, memoryUsed, memoryTotal, gpu, cpuTemp, gpuTemp;
        public string[] games = new string[0];
        public string foliaKey;
        public bool foliaConnected;
        public bool foliaPlaying;
        public bool foliaRunning;
        public string foliaSong;
        public string foliaArtist;
        public bool foliaCover;
        public GameDetection[] reviewItems = new GameDetection[0];
        public string[] reviewedGameKeys = new string[0];
        public string connectionError;
        public long connectionAttemptUtc;
        public long connectionSuccessUtc;
        public long packetAgeMs;
        public long connectionRttMs;
        public string resolvedAddress;
        public string role = "ame";
        public long roleEvent;
        public long returnInSeconds;
        public GameSession[] sessions;
        public string version;
        public string hostVersion;
        public string[] knownGames;
        public string[] excludedGames;
        public bool autoUpdate;
        public bool hostAutoUpdate;
    }

    internal static class BuildInfo
    {
        public const string Version = "1.8.3.3";
        // The app lives in <install>\versions\<version>\ControlConsole.exe.
        // Assets, update-source.txt and the launcher are shared at <install>.
        internal static string InstallDir
        {
            get
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                try
                {
                    var dir = new DirectoryInfo(baseDir);
                    if (dir.Parent != null && dir.Parent.Parent != null && String.Equals(dir.Parent.Name, "versions", StringComparison.OrdinalIgnoreCase))
                        return dir.Parent.Parent.FullName;
                }
                catch { }
                return baseDir;
            }
        }
        internal static string LauncherPath { get { return Path.Combine(InstallDir, "ControlConsole.exe"); } }
    }

    internal static class DisplayServer
    {
        private const int TcpPort = 46702;
        private static readonly JavaScriptSerializer Json = new JavaScriptSerializer();
        private static readonly object Gate = new object();
        private static readonly List<GameSession> Sessions = new List<GameSession>();
        private static readonly Dictionary<string, GameSession> ActiveSessions = new Dictionary<string, GameSession>(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> KnownGames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, GameDetection> ReviewItems = new Dictionary<string, GameDetection>(StringComparer.OrdinalIgnoreCase);
        private static DateTime LastReviewSaveUtc = DateTime.MinValue;
        private static string LastTcpError = "尚未连接";
        private static string LastResolvedAddress = "";
        private static long LastConnectionAttemptUtc;
        private static long LastConnectionSuccessUtc;
        private static long LastConnectionRttMs = -1;
        private static ConsoleSettings Settings = new ConsoleSettings();
        private static MetricPacket Last;
        private static DateTime LastSeen = DateTime.MinValue;
        private static bool AnyGame;
        private static DateTime ReturnAt = DateTime.MinValue;
        private static long EventId;
        private static string Role = "ame";
        private static TrayContext Tray;
        private static System.Threading.Timer UpdateTimer;
        private static int UpdateStarted;
        private static int LegacyFoliaMigrationStarted;
        private static readonly object UpdateProgressGate = new object();
        private static string UpdateProgressState = "idle";
        private static string UpdateProgressMessage = "定时检查 GitHub 最新版本。";
        private static string UpdateProgressVersion = "";
        private static int UpdateProgressPercent = -1;
        private static readonly string BaseDir = BuildInfo.InstallDir;
        private static readonly string DataDir = NixPaths.DataDir;
        // Session persistence is deliberately throttled and performed outside Gate:
        // serializing 1000 sessions with samples costs ~140 ms, which must never block the UI thread.
        private static readonly object SaveGate = new object();
        private const int SessionSaveSeconds = 30;
        private static bool SessionsDirty;
        private static DateTime NextSessionSaveUtc = DateTime.MinValue;

        private static void SetUpdateProgress(string state, int percent, string version, string message)
        {
            lock (UpdateProgressGate)
            {
                UpdateProgressState = state;
                UpdateProgressPercent = percent;
                UpdateProgressVersion = version ?? "";
                UpdateProgressMessage = message ?? "";
            }
        }

        internal static UpdateStatus GetUpdateProgress()
        {
            lock (UpdateProgressGate)
                return new UpdateStatus { state = UpdateProgressState, percent = UpdateProgressPercent, version = UpdateProgressVersion, message = UpdateProgressMessage };
        }

        public static void Run()
        {
            Directory.CreateDirectory(DataDir); LoadSettings(); LoadReviewItems(); LoadSessions();
            UpdateTimer = new System.Threading.Timer(CheckForUpdate, null, TimeSpan.FromSeconds(20), TimeSpan.FromMinutes(5));
            Tray = new TrayContext();
            var tcpThread = new Thread(ReadTcp) { IsBackground = true }; tcpThread.Start();
            StartupDiagnostics.Ready("--display");
            Application.Run(Tray);
        }
        internal static void CheckForUpdate(object ignored)
        {
            if (Interlocked.CompareExchange(ref UpdateStarted, 1, 0) != 0) return;
            Version attempted = null;
            try
            {
                bool hostMode = String.Equals(ignored as string, "--sensor", StringComparison.Ordinal) || String.Equals(ignored as string, "--sensor-force", StringComparison.Ordinal);
                bool force = String.Equals(ignored as string, "--display-force", StringComparison.Ordinal) || String.Equals(ignored as string, "--sensor-force", StringComparison.Ordinal);
                if (hostMode)
                {
                    if (HostSensor.HasGameRunning) { SetUpdateProgress("waiting", -1, "", "主机游戏运行中，自动更新检查暂缓。"); return; }
                    if (!force && !HostSensor.AutoUpdateEnabled) { SetUpdateProgress("disabled", -1, "", "主机自动更新已关闭。"); return; }
                }
                else
                {
                    lock (Gate)
                    {
                        if (ActiveSessions.Count > 0) { SetUpdateProgress("waiting", -1, "", "副机检测到游戏仍在运行，更新会在游戏退出后进行。"); return; }
                        if (!force && !Settings.autoUpdate) { SetUpdateProgress("disabled", -1, "", "副机自动更新已关闭。"); return; }
                    }
                }
                SetUpdateProgress("checking", 3, "", "正在检查更新版本…");
                Version current; if (!Version.TryParse(BuildInfo.Version, out current)) return;
                Version available = null; string sourceZip = null;
                string githubZip = FindGitHubUpdate(current, null);
                if (githubZip != null)
                {
                    Version githubVersion;
                    if (ReadPackageVersion(githubZip, out githubVersion) && (available == null || githubVersion > available))
                    { available = githubVersion; sourceZip = githubZip; }
                }
                if (sourceZip == null)
                {
                    if (!hostMode && !AssetBundleSync.HasCurrentAssets(BuildInfo.InstallDir))
                    {
                        RepairCurrentAssets(current.ToString());
                        return;
                    }
                    SetUpdateProgress("latest", 100, current.ToString(), "当前版本已是最新版本 " + current + "。");
                    return;
                }
                string expectedTree, expectedManifest;
                ReadAssetHashes(sourceZip, available, out expectedTree, out expectedManifest);
                string failedUpdate = Path.Combine(DataDir, "failed-update.txt");
                if (!force && File.Exists(failedUpdate) && String.Equals(File.ReadAllText(failedUpdate, Encoding.UTF8).Trim(), available.ToString(), StringComparison.OrdinalIgnoreCase) && DateTime.UtcNow - File.GetLastWriteTimeUtc(failedUpdate) < TimeSpan.FromHours(24))
                {
                    SetUpdateProgress("failed", -1, available.ToString(), "版本 " + available + " 上次安装失败，已暂停自动重试。可在设置中手动重试。");
                    return;
                }
                SetUpdateProgress("preparing", 96, available.ToString(), "更新包已下载，正在校验并准备安装…");
                string updateDir = Path.Combine(DataDir, "updates"); Directory.CreateDirectory(updateDir);
                string localZip = Path.Combine(updateDir, "pending-" + available + "-" + Guid.NewGuid().ToString("N") + ".zip");
                File.Copy(sourceZip, localZip, false);
                string previousVersion = null;
                string assetStage = null;
                string assetPending = Path.Combine(BuildInfo.InstallDir, "asset-pending.txt");
                try
                {
                    if (!hostMode && !AssetBundleSync.HasCurrentAssets(BuildInfo.InstallDir, expectedTree))
                        assetStage = StageMissingAssets(available.ToString(), expectedTree, expectedManifest);
                    attempted = available;
                    previousVersion = InstallVersionPackage(localZip, available.ToString());
                    if (assetStage != null)
                        File.WriteAllText(assetPending, available + "|" + Path.GetFileName(assetStage), Encoding.UTF8);
                    SetUpdateProgress("installing", 99, available.ToString(), "更新已就绪，正在切换到新版本…");
                    try { File.Delete(localZip); } catch { }
                    RestartThroughLauncher(hostMode ? "--sensor" : "--display");
                    Environment.Exit(0);
                }
                catch
                {
                    RestoreCurrentVersion(previousVersion);
                    try { if (assetStage != null && File.Exists(assetPending)) File.Delete(assetPending); } catch { }
                    try { if (assetStage != null && Directory.Exists(assetStage)) Directory.Delete(assetStage, true); } catch { }
                    throw;
                }
                finally { try { if (File.Exists(localZip)) File.Delete(localZip); } catch { } }
            }
            catch (Exception ex)
            {
                SetUpdateProgress("failed", -1, "", "更新失败：" + ex.Message);
                if (attempted != null)
                {
                    try { Directory.CreateDirectory(DataDir); File.WriteAllText(Path.Combine(DataDir, "failed-update.txt"), attempted.ToString(), Encoding.UTF8); } catch { }
                }
                try { File.AppendAllText(Path.Combine(DataDir, "update.log"), DateTime.Now.ToString("s") + " update check: " + ex.Message + Environment.NewLine, Encoding.UTF8); } catch { }
            }
            finally { Interlocked.Exchange(ref UpdateStarted, 0); }
        }
        private static void ReadAssetHashes(string archivePath, Version version, out string tree, out string manifest)
        {
            tree = AssetBundleSync.AssetTreeSha256;
            manifest = AssetBundleSync.ManifestSha256;
            using (var archive = ZipFile.OpenRead(archivePath))
            {
                var treeEntry = archive.GetEntry("asset-tree.sha256");
                var manifestEntry = archive.GetEntry("role-manifest.sha256");
                if (treeEntry == null || manifestEntry == null)
                {
                    if (version == new Version(1, 8, 3, 0)) return;
                    throw new InvalidDataException("GitHub 更新包缺少素材版本校验信息。");
                }
                using (var reader = new StreamReader(treeEntry.Open(), Encoding.UTF8)) tree = reader.ReadToEnd().Trim().TrimStart('\uFEFF');
                using (var reader = new StreamReader(manifestEntry.Open(), Encoding.UTF8)) manifest = reader.ReadToEnd().Trim().TrimStart('\uFEFF');
                if (!Regex.IsMatch(tree, @"\A[0-9a-fA-F]{64}\z") || !Regex.IsMatch(manifest, @"\A[0-9a-fA-F]{64}\z"))
                    throw new InvalidDataException("GitHub 更新包的素材哈希无效。");
            }
        }

        private static string StageMissingAssets(string version, string expectedTree, string expectedManifest)
        {
            string address, key, hostVersion;
            lock (Gate)
            {
                address = Settings.hostAddress;
                key = Last == null ? null : Last.foliaKey;
                hostVersion = Last == null ? null : Last.version;
            }
            if (!String.Equals(hostVersion, version, StringComparison.Ordinal))
                throw new IOException("主机尚未更新到 " + version + "，等待主机更新后再同步素材。");
            SetUpdateProgress("downloading", 0, version, "正在从主机同步角色素材…");
            string zip = AssetBundleSync.Download(address, key, version, delegate(int percent, string message)
            { SetUpdateProgress("downloading", percent, version, message); });
            try
            {
                SetUpdateProgress("installing", 96, version, "正在校验并安装角色素材…");
                return AssetBundleSync.StageInstall(zip, BuildInfo.InstallDir, expectedTree, expectedManifest);
            }
            finally { try { File.Delete(zip); } catch { } }
        }

        private static void RepairCurrentAssets(string version)
        {
            string stage = StageMissingAssets(version, AssetBundleSync.AssetTreeSha256, AssetBundleSync.ManifestSha256);
            string marker = Path.Combine(BuildInfo.InstallDir, "asset-pending.txt");
            try
            {
                File.WriteAllText(marker, version + "|" + Path.GetFileName(stage), Encoding.UTF8);
                SetUpdateProgress("installing", 99, version, "角色素材已就绪，正在重新打开控制台…");
                RestartThroughLauncher("--display");
                Environment.Exit(0);
            }
            catch
            {
                try { if (File.Exists(marker)) File.Delete(marker); } catch { }
                try { if (Directory.Exists(stage)) Directory.Delete(stage, true); } catch { }
                throw;
            }
        }
        private static string FindGitHubUpdate(Version current, Version localAvailable)
        {
            string config = Path.Combine(BaseDir, "update-source.txt");
            if (!File.Exists(config)) throw new FileNotFoundException("未配置 GitHub 更新地址。", config);
            string repository = File.ReadAllText(config, Encoding.UTF8).Trim();
            if (!Regex.IsMatch(repository, @"\A[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+\z")) throw new InvalidDataException("GitHub 更新地址格式无效。");
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
                            SetUpdateProgress("downloading", -1, version.ToString(), "正在连接 GitHub 下载更新包…");
                            Exception downloadError = null;
                            bool downloadCancelled = false;
                            using (var done = new ManualResetEvent(false))
                            using (var downloadClient = new WebClient())
                            {
                                downloadClient.DownloadProgressChanged += delegate(object sender, DownloadProgressChangedEventArgs e)
                                {
                                    string amount = e.TotalBytesToReceive > 0 ? e.ProgressPercentage + "%" : (e.BytesReceived / 1048576d).ToString("F1") + " MB";
                                    int percent = e.TotalBytesToReceive > 0 ? e.ProgressPercentage : -1;
                                    SetUpdateProgress("downloading", percent, version.ToString(), "正在下载更新包… " + amount);
                                };
                                downloadClient.DownloadFileCompleted += delegate(object sender, System.ComponentModel.AsyncCompletedEventArgs e) { downloadError = e.Error; downloadCancelled = e.Cancelled; done.Set(); };
                                downloadClient.DownloadFileAsync(url, temp);
                                if (!done.WaitOne(TimeSpan.FromMinutes(10))) { downloadClient.CancelAsync(); done.WaitOne(5000); throw new TimeoutException("更新包下载超时。"); }
                            }
                            if (downloadCancelled) throw new IOException("更新包下载已取消。");
                            if (downloadError != null) throw downloadError;
                            SetUpdateProgress("verifying", 97, version.ToString(), "下载完成，正在校验更新包…");
                            string digest = asset.ContainsKey("digest") ? Convert.ToString(asset["digest"]) : "";
                            if (!string.IsNullOrEmpty(digest))
                            {
                                if (!digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("GitHub 更新包校验格式无效。");
                                using (var file = File.OpenRead(temp)) using (var sha = SHA256.Create())
                                {
                                    string actual = BitConverter.ToString(sha.ComputeHash(file)).Replace("-", "");
                                    if (!actual.Equals(digest.Substring(7), StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("更新包校验失败，文件可能已损坏。");
                                }
                            }
                            else VerifyPublishedChecksum(repository, version, temp);
                            Version embedded;
                            if (!ReadPackageVersion(temp, out embedded) || embedded != version) throw new InvalidDataException("更新包中的版本信息无效。");
                            if (File.Exists(path)) File.Delete(path);
                            File.Move(temp, path);
                            return path;
                        }
                        finally { if (File.Exists(temp)) File.Delete(temp); }
                    }
                    throw new FileNotFoundException("GitHub Release 未附带对应版本的更新包。");
                }
            }
            catch (Exception ex)
            {
                try { File.AppendAllText(Path.Combine(DataDir, "update.log"), DateTime.Now.ToString("s") + " GitHub update check: " + ex.Message + Environment.NewLine, Encoding.UTF8); } catch { }
                try
                {
                    Version fallback;
                    if (Version.TryParse(LatestReleaseVersionFromWeb(repository), out fallback) && fallback > current && (localAvailable == null || fallback > localAvailable))
                    {
                        string assetUrl = "https://github.com/" + repository + "/releases/download/v" + fallback + "/ControlConsole-update-" + fallback + ".zip";
                        string updateDir = Path.Combine(DataDir, "updates"); Directory.CreateDirectory(updateDir);
                        string path = Path.Combine(updateDir, "ControlConsole-update-" + fallback + ".zip");
                        string temp = path + ".download";
                        try
                        {
                            SetUpdateProgress("downloading", -1, fallback.ToString(), "正在连接 GitHub 下载更新包…");
                            using (var client = new WebClient())
                            {
                                client.Headers[HttpRequestHeader.UserAgent] = "NixLanConsole/" + BuildInfo.Version;
                                client.DownloadProgressChanged += delegate(object sender, DownloadProgressChangedEventArgs e)
                                {
                                    SetUpdateProgress("downloading", e.TotalBytesToReceive > 0 ? e.ProgressPercentage : -1, fallback.ToString(), "正在下载更新包… " + (e.TotalBytesToReceive > 0 ? e.ProgressPercentage + "%" : (e.BytesReceived / 1048576d).ToString("F1") + " MB"));
                                };
                                using (var done = new ManualResetEvent(false))
                                {
                                    Exception downloadError = null;
                                    bool downloadCancelled = false;
                                    client.DownloadFileCompleted += delegate(object sender, System.ComponentModel.AsyncCompletedEventArgs e) { downloadError = e.Error; downloadCancelled = e.Cancelled; done.Set(); };
                                    client.DownloadFileAsync(new Uri(assetUrl), temp);
                                    if (!done.WaitOne(TimeSpan.FromMinutes(10))) { client.CancelAsync(); done.WaitOne(5000); throw new TimeoutException("更新包下载超时。"); }
                                    if (downloadCancelled) throw new IOException("更新包下载已取消。");
                                    if (downloadError != null) throw downloadError;
                                }
                            }
                            VerifyPublishedChecksum(repository, fallback, temp);
                            Version embedded;
                            if (!ReadPackageVersion(temp, out embedded) || embedded != fallback) throw new InvalidDataException("更新包中的版本信息无效。");
                            if (File.Exists(path)) File.Delete(path);
                            File.Move(temp, path);
                            return path;
                        }
                        finally { if (File.Exists(temp)) File.Delete(temp); }
                    }
                }
                catch (Exception fallbackError)
                {
                    try { File.AppendAllText(Path.Combine(DataDir, "update.log"), DateTime.Now.ToString("s") + " GitHub web fallback: " + fallbackError.Message + Environment.NewLine, Encoding.UTF8); } catch { }
                    throw new IOException("GitHub 更新检查失败，API 与网页备用路径均不可用。", fallbackError);
                }
            }
            return null;
        }
        private static void VerifyPublishedChecksum(string repository, Version version, string archivePath)
        {
            string fileName = "ControlConsole-update-" + version + ".zip";
            string checksumUrl = "https://github.com/" + repository + "/releases/download/v" + version + "/" + fileName + ".sha256";
            string expected;
            using (var client = new WebClient())
            {
                client.Headers[HttpRequestHeader.UserAgent] = "NixLanConsole/" + BuildInfo.Version;
                expected = client.DownloadString(checksumUrl).Trim().Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)[0];
            }
            if (!Regex.IsMatch(expected, @"\A[0-9a-fA-F]{64}\z")) throw new InvalidDataException("更新包校验文件格式无效。");
            using (var file = File.OpenRead(archivePath)) using (var sha = SHA256.Create())
            {
                string actual = BitConverter.ToString(sha.ComputeHash(file)).Replace("-", "");
                if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("更新包 SHA-256 校验失败，已拒绝安装。");
            }
        }
        private static string LatestReleaseVersionFromWeb(string repository)
        {
            try
            {
                var request = (HttpWebRequest)WebRequest.Create("https://github.com/" + repository + "/releases/latest");
                request.UserAgent = "NixLanConsole/" + BuildInfo.Version;
                request.AllowAutoRedirect = false;
                request.Timeout = 8000;
                using (var response = (HttpWebResponse)request.GetResponse())
                {
                    string location = response.Headers[HttpResponseHeader.Location];
                    Uri target;
                    if (String.IsNullOrWhiteSpace(location) || !Uri.TryCreate(new Uri("https://github.com"), location, out target) || target.Host != "github.com") throw new InvalidDataException("GitHub 最新版本跳转地址无效。");
                    Match match = Regex.Match(target.AbsolutePath, @"/releases/tag/v(\d+\.\d+\.\d+\.\d+)\z", RegexOptions.IgnoreCase);
                    if (!match.Success) throw new InvalidDataException("GitHub 最新版本号无效。");
                    return match.Groups[1].Value;
                }
            }
            catch (Exception ex)
            {
                StartupDiagnostics.Write("GitHub release page unavailable; trying raw version: " + ex.Message);
                using (var client = new WebClient())
                {
                    client.Headers[HttpRequestHeader.UserAgent] = "NixLanConsole/" + BuildInfo.Version;
                    string version = client.DownloadString("https://raw.githubusercontent.com/" + repository + "/main/version.txt").Trim().TrimStart('\uFEFF');
                    Version parsed;
                    if (!Regex.IsMatch(version, @"\A\d+\.\d+\.\d+\.\d+\z") || !Version.TryParse(version, out parsed)) throw new InvalidDataException("GitHub 版本文件无效。");
                    return version;
                }
            }
        }
        private static bool ReadPackageVersion(string file, out Version version)
        {
            version = null;
            try
            {
                using (var archive = ZipFile.OpenRead(file))
                {
                    var entry = archive.GetEntry("version.txt");
                    if (entry == null) return false;
                    string text;
                    using (var reader = new StreamReader(entry.Open(), Encoding.UTF8))
                        text = reader.ReadToEnd().Trim().TrimStart('\uFEFF');
                    Version embedded;
                    if (!Version.TryParse(text, out embedded)) return false;
                    // 1.7 packages carry versions/<version>/ControlConsole.exe; reject legacy layouts.
                    if (!archive.Entries.Any(e => String.Equals(e.FullName.Replace('\\', '/'), "versions/" + embedded + "/ControlConsole.exe", StringComparison.OrdinalIgnoreCase))) return false;
                    version = embedded;
                    return true;
                }
            }
            catch { return false; }
        }

        private static string InstallVersionPackage(string archivePath, string version)
        {
            string root = BuildInfo.InstallDir;
            string versionsDir = Path.Combine(root, "versions");
            Directory.CreateDirectory(versionsDir);
            string target = Path.Combine(versionsDir, version);
            try
            {
                // Extract into the final directory and mark it complete last. No directory
                // rename is involved, so antivirus/indexer file handles cannot block the switch.
                if (Directory.Exists(target)) Directory.Delete(target, true);
                Directory.CreateDirectory(target);
                string targetPrefix = Path.GetFullPath(target).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                string packagePrefix = "versions/" + version + "/";
                using (var archive = ZipFile.OpenRead(archivePath))
                {
                    foreach (var entry in archive.Entries)
                    {
                        string name = entry.FullName.Replace('\\', '/');
                        if (!name.StartsWith(packagePrefix, StringComparison.OrdinalIgnoreCase)) continue;
                        string relative = name.Substring(packagePrefix.Length);
                        if (relative.Length == 0) continue;
                        if (relative.Split('/').Any(x => x == ".." || x == ".")) throw new InvalidDataException("更新包包含无效路径。");
                        string output = Path.GetFullPath(Path.Combine(target, relative.Replace('/', Path.DirectorySeparatorChar)));
                        if (!output.StartsWith(targetPrefix, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("更新包路径超出目录。");
                        if (name.EndsWith("/", StringComparison.Ordinal)) Directory.CreateDirectory(output);
                        else
                        {
                            Directory.CreateDirectory(Path.GetDirectoryName(output));
                            using (var input = entry.Open()) using (var file = File.Create(output)) input.CopyTo(file);
                        }
                    }
                }
                if (!File.Exists(Path.Combine(target, "ControlConsole.exe"))) throw new InvalidDataException("更新包中缺少新版本程序。");
                WriteVersionFile(Path.Combine(target, ".complete"), version);
                string previous = ReadVersionFile(Path.Combine(root, "current.txt"));
                if (String.IsNullOrEmpty(previous)) previous = BuildInfo.Version;
                WriteVersionFile(Path.Combine(root, "previous.txt"), previous);
                WriteVersionFile(Path.Combine(root, "current.txt"), version);
                WriteVersionFile(Path.Combine(root, "version.txt"), version);
                Log("update installed version=" + version + " previous=" + previous);
                return previous;
            }
            catch (Exception ex)
            {
                try { if (Directory.Exists(target)) Directory.Delete(target, true); } catch { }
                try { Directory.CreateDirectory(DataDir); File.WriteAllText(Path.Combine(DataDir, "failed-update.txt"), version, Encoding.UTF8); } catch { }
                throw new IOException("安装更新失败：" + ex.Message, ex);
            }
        }

        private static void RestartThroughLauncher(string role)
        {
            string launcher = BuildInfo.LauncherPath;
            if (!File.Exists(launcher) || String.Equals(Path.GetFullPath(launcher), Path.GetFullPath(Application.ExecutablePath), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("当前安装不是 1.7 版本目录布局，请使用完整安装包升级。");
            UpgradeLauncherIfBundled(launcher);
            string args = "--wait-pid " + Process.GetCurrentProcess().Id + " " + role;
            Process started = Process.Start(new ProcessStartInfo(launcher, args) { UseShellExecute = true, WorkingDirectory = BuildInfo.InstallDir });
            if (started == null) throw new InvalidOperationException("Windows 未能启动更新启动器。");
            using (started)
            {
                if (started.WaitForExit(1500)) throw new InvalidOperationException("更新启动器启动后立即退出，请查看 update.log。");
            }
        }

        private static void UpgradeLauncherIfBundled(string launcher)
        {
            string bundled = Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "Launcher.exe");
            if (!File.Exists(bundled)) return;
            using (var sha = SHA256.Create())
            using (var source = File.OpenRead(bundled))
            using (var current = File.OpenRead(launcher))
                if (sha.ComputeHash(source).SequenceEqual(sha.ComputeHash(current))) return;
            string pending = launcher + ".pending-" + Process.GetCurrentProcess().Id;
            try
            {
                File.Copy(bundled, pending, true);
                File.Replace(pending, launcher, null, true);
                StartupDiagnostics.Write("launcher upgraded from versioned package " + BuildInfo.Version);
            }
            finally { try { if (File.Exists(pending)) File.Delete(pending); } catch { } }
        }

        private static void RestoreCurrentVersion(string version)
        {
            if (String.IsNullOrEmpty(version)) return;
            try
            {
                WriteVersionFile(Path.Combine(BuildInfo.InstallDir, "current.txt"), version);
                WriteVersionFile(Path.Combine(BuildInfo.InstallDir, "version.txt"), version);
            }
            catch (Exception ex) { Log("restore current version: " + ex.Message); }
        }

        private static string ReadVersionFile(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;
                string text = File.ReadAllText(path, Encoding.UTF8).Trim().TrimStart('\uFEFF');
                Version parsed;
                return Version.TryParse(text, out parsed) ? text : null;
            }
            catch { return null; }
        }

        private static void WriteVersionFile(string path, string text)
        {
            string pending = path + ".pending";
            File.WriteAllText(pending, text, Encoding.UTF8);
            if (File.Exists(path)) File.Replace(pending, path, null, true);
            else File.Move(pending, path);
        }

        private static void ReadTcp()
        {
            DateTime lastErrorLog = DateTime.MinValue;
            while (true)
            {
                string address;
                lock (Gate) { address = Settings.hostAddress; LastConnectionAttemptUtc = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(); }
                var watch = Stopwatch.StartNew();
                try
                {
                    IPAddress ip;
                    if (!IPAddress.TryParse(address, out ip)) ip = Dns.GetHostAddresses(address).FirstOrDefault(x => x.AddressFamily == AddressFamily.InterNetwork);
                    if (ip == null) throw new InvalidOperationException("主机地址无效：" + address);
                    lock (Gate) LastResolvedAddress = ip.ToString();
                    using (var client = new TcpClient(AddressFamily.InterNetwork))
                    {
                        client.ReceiveTimeout = 1800; client.SendTimeout = 1800;
                        var pending = client.BeginConnect(ip, TcpPort, null, null);
                        bool connected;
                        using (WaitHandle waitHandle = pending.AsyncWaitHandle) connected = waitHandle.WaitOne(1800);
                        if (!connected)
                        {
                            client.Close();
                            try { client.EndConnect(pending); } catch { }
                            throw new TimeoutException("连接主机超时");
                        }
                        client.EndConnect(pending);
                        using (var stream = client.GetStream())
                        using (var reader = new StreamReader(stream, Encoding.UTF8))
                        using (var writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true })
                        {
                            ControlPacket control;
                            lock (Gate) control = new ControlPacket { excludedGames = Settings.excludedGames, hostAutoUpdate = Settings.hostAutoUpdate, gameOverrides = Settings.gameOverrides };
                            writer.WriteLine(Json.Serialize(control));
                            string line = reader.ReadLine();
                            if (String.IsNullOrEmpty(line) || line.Length > 65536) throw new IOException("主机返回的数据无效");
                            MetricPacket p = Json.Deserialize<MetricPacket>(line);
                            if (p == null) throw new IOException("主机状态数据无效");
                            watch.Stop();
                            lock (Gate) { LastConnectionRttMs = watch.ElapsedMilliseconds; LastConnectionSuccessUtc = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(); LastTcpError = ""; Update(p); }
                        }
                    }
                }
                catch (Exception ex) { lock (Gate) LastTcpError = ex.Message; if ((DateTime.UtcNow - lastErrorLog).TotalSeconds >= 30) { StartupDiagnostics.Write("TCP host connection: " + ex.Message); lastErrorLog = DateTime.UtcNow; } }
                SaveSessionsIfDue();
                Thread.Sleep(1000);
            }
        }
        private static void Update(MetricPacket p)
        {
            if (!String.IsNullOrWhiteSpace(p.foliaKey) && Interlocked.Exchange(ref LegacyFoliaMigrationStarted, 1) == 0)
                ThreadPool.QueueUserWorkItem(delegate { LegacyFoliaDisplayMigration.Run(); });
            bool openForNewHost = ShouldOpenForHost(Last, p, DateTime.UtcNow - LastSeen);
            Last = p; LastSeen = DateTime.UtcNow;
            bool newReview = false;
            foreach (GameDetection detection in p.detections ?? new GameDetection[0])
            {
                if (detection == null || String.IsNullOrWhiteSpace(detection.key) || String.IsNullOrWhiteSpace(detection.game)) continue;
                GameDetection existing;
                if (!ReviewItems.TryGetValue(detection.key, out existing)) newReview = true;
                ReviewItems[detection.key] = detection;
            }
            if (ReviewItems.Count > 100)
                foreach (string key in ReviewItems.Values.OrderBy(x => x.seen).Take(ReviewItems.Count - 100).Select(x => x.key).ToArray()) ReviewItems.Remove(key);
            if (newReview || ((p.detections ?? new GameDetection[0]).Length > 0 && DateTime.UtcNow - LastReviewSaveUtc > TimeSpan.FromMinutes(5)))
            { SaveReviewItems(); LastReviewSaveUtc = DateTime.UtcNow; }
            foreach (string name in p.catalog ?? new string[0]) if (!String.IsNullOrWhiteSpace(name)) KnownGames.Add(name);
            foreach (string name in p.games ?? new string[0]) if (!String.IsNullOrWhiteSpace(name)) KnownGames.Add(name);
            var effectiveGames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var reported = p.detections ?? new GameDetection[0];
            var detectedNames = new HashSet<string>(reported.Where(x => x != null && !String.IsNullOrWhiteSpace(x.game)).Select(x => x.game), StringComparer.OrdinalIgnoreCase);
            foreach (GameDetection item in reported)
            {
                if (item == null || String.IsNullOrWhiteSpace(item.game)) continue;
                string corrected;
                if (Settings.gameOverrides != null && Settings.gameOverrides.TryGetValue(item.key ?? "", out corrected))
                { if (!String.IsNullOrWhiteSpace(corrected)) effectiveGames.Add(corrected); }
                else effectiveGames.Add(item.game);
            }
            foreach (string name in p.games ?? new string[0]) if (!detectedNames.Contains(name)) effectiveGames.Add(name);
            var excluded = new HashSet<string>(Settings.excludedGames ?? new string[0], StringComparer.OrdinalIgnoreCase);
            excluded.Add("Wallpaper Engine");
            var current = new HashSet<string>(effectiveGames.Where(x => !excluded.Contains(x)), StringComparer.OrdinalIgnoreCase);
            foreach (string name in current)
                if (!ActiveSessions.ContainsKey(name))
                {
                    ActiveSessions[name] = new GameSession { id = Guid.NewGuid().ToString("N"), game = name, start = p.utc, end = 0 };
                    Sessions.Insert(0, ActiveSessions[name]);
                    // Keep detailed curves for recent sessions without growing sessions.json indefinitely.
                    foreach (var old in Sessions.Skip(50)) old.samples = null;
                    MarkSessionsDirty(true);
                }
            foreach (var session in ActiveSessions.Values)
            {
                var samples = session.samples == null ? new List<GameMetricSample>() : session.samples.ToList();
                if (samples.Count == 0 || p.utc - samples[samples.Count - 1].utc >= 20000)
                {
                    samples.Add(new GameMetricSample { utc = p.utc, cpu = p.cpu, gpu = p.gpu, cpuTemp = p.cpuTemp, gpuTemp = p.gpuTemp });
                    if (samples.Count > 240) samples = samples.Where((sample, index) => index % 2 == 0).ToList();
                    session.samples = samples.ToArray();
                    MarkSessionsDirty(false);
                }
            }
            foreach (string name in ActiveSessions.Keys.ToArray())
                if (!current.Contains(name)) { ActiveSessions[name].end = p.utc; ActiveSessions.Remove(name); MarkSessionsDirty(true); }
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
            if (openForNewHost) RestoreDashboard();
        }
        private static bool ShouldOpenForHost(MetricPacket previous, MetricPacket current, TimeSpan gap)
        {
            return previous == null || (!String.IsNullOrEmpty(current.bootId) && !String.Equals(previous.bootId, current.bootId, StringComparison.Ordinal)) || (String.IsNullOrEmpty(current.bootId) && gap.TotalSeconds > 30);
        }
        private static void BeginTransformation()
        {
            Role = "kangel"; EventId++;
        }
        private static string SettingsFile { get { return Path.Combine(DataDir, "settings.json"); } }
        private static string ReviewFile { get { return Path.Combine(DataDir, "game-review.json"); } }
        private static void LoadReviewItems()
        {
            try
            {
                if (!File.Exists(ReviewFile)) return;
                foreach (GameDetection item in Json.Deserialize<GameDetection[]>(File.ReadAllText(ReviewFile, Encoding.UTF8)) ?? new GameDetection[0])
                    if (item != null && !String.IsNullOrWhiteSpace(item.key)) ReviewItems[item.key] = item;
            }
            catch (Exception ex) { Log("game review load: " + ex.Message); }
        }
        private static void SaveReviewItems()
        {
            try { File.WriteAllText(ReviewFile, Json.Serialize(ReviewItems.Values.OrderByDescending(x => x.seen).Take(100).ToArray()), Encoding.UTF8); }
            catch (Exception ex) { Log("game review save: " + ex.Message); }
        }
        internal static string GetFoliaExecutablePath()
        {
            lock (Gate) return Settings == null ? "" : (Settings.foliaExePath ?? "");
        }
        private static void LoadSettings()
        {
            try
            {
                if (File.Exists(SettingsFile)) Settings = Json.Deserialize<ConsoleSettings>(File.ReadAllText(SettingsFile)) ?? new ConsoleSettings();
                if (String.IsNullOrWhiteSpace(Settings.hostAddress)) Settings.hostAddress = "192.168.1.7";
                var excluded = new HashSet<string>(Settings.excludedGames ?? new string[0], StringComparer.OrdinalIgnoreCase);
                excluded.Add("Wallpaper Engine");
                Settings.excludedGames = excluded.ToArray();
                Settings.gameOverrides = Settings.gameOverrides == null ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) : new Dictionary<string, string>(Settings.gameOverrides, StringComparer.OrdinalIgnoreCase);
                Settings.reviewedGameKeys = Settings.reviewedGameKeys ?? new string[0];
            }
            catch { Settings = new ConsoleSettings(); }
        }
        private static string LatestReleaseVersion()
        {
            string config = Path.Combine(BaseDir, "update-source.txt");
            if (!File.Exists(config)) throw new FileNotFoundException("未配置 GitHub 更新地址");
            string repository = File.ReadAllText(config, Encoding.UTF8).Trim();
            if (!Regex.IsMatch(repository, @"\A[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+\z")) throw new ArgumentException("GitHub 更新地址无效");
            ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;
            var request = (HttpWebRequest)WebRequest.Create("https://api.github.com/repos/" + repository + "/releases/latest");
            request.UserAgent = "NixLanConsole/" + BuildInfo.Version;
            request.Accept = "application/vnd.github+json";
            request.Timeout = 8000;
            try
            {
                using (var response = request.GetResponse()) using (var reader = new StreamReader(response.GetResponseStream()))
                {
                    var release = Json.Deserialize<Dictionary<string, object>>(reader.ReadToEnd());
                    return Convert.ToString(release["tag_name"]).TrimStart('v', 'V');
                }
            }
            catch (WebException ex)
            {
                StartupDiagnostics.Write("GitHub API unavailable; trying release page: " + ex.Message);
                return LatestReleaseVersionFromWeb(repository);
            }
        }
        internal static void SaveNativeSettings(bool autoUpdate, bool hostAutoUpdate, IEnumerable<string> excludedGames, string hostAddress)
        {
            hostAddress = (hostAddress ?? "").Trim();
            if (hostAddress.Length < 1 || hostAddress.Length > 255 || Uri.CheckHostName(hostAddress) == UriHostNameType.Unknown) throw new ArgumentException("请输入有效的主机 IPv4 地址或计算机名。");
            var names = new HashSet<string>(excludedGames ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            names.Add("Wallpaper Engine");
            if (names.Count > 256 || names.Any(x => String.IsNullOrWhiteSpace(x) || x.Length > 100)) throw new ArgumentException("游戏排除列表无效");
            lock (Gate)
            {
                var proposed = new ConsoleSettings { autoUpdate = autoUpdate, hostAutoUpdate = hostAutoUpdate, excludedGames = names.ToArray(), hostAddress = hostAddress, gameOverrides = Settings.gameOverrides, reviewedGameKeys = Settings.reviewedGameKeys };
                File.WriteAllText(SettingsFile, Json.Serialize(proposed), Encoding.UTF8);
                Settings = proposed;
            }
        }
        internal static void ReviewDetection(string key, string title, bool ignore)
        {
            lock (Gate)
            {
                GameDetection detection;
                if (String.IsNullOrWhiteSpace(key) || !ReviewItems.TryGetValue(key, out detection)) throw new ArgumentException("找不到这条游戏识别记录。");
                if (!ignore && (String.IsNullOrWhiteSpace(title) || title.Trim().Length > 100)) throw new ArgumentException("请输入有效的游戏名称。");
                var overrides = new Dictionary<string, string>(Settings.gameOverrides ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase);
                bool changed = ignore || !String.Equals(detection.game, title.Trim(), StringComparison.OrdinalIgnoreCase);
                if (changed) overrides[key] = ignore ? "" : title.Trim();
                if (overrides.Count > 64) throw new InvalidOperationException("游戏纠错规则最多保存 64 条。");
                var reviewed = new HashSet<string>(Settings.reviewedGameKeys ?? new string[0], StringComparer.OrdinalIgnoreCase) { key };
                bool renameActive = changed && !ignore && ActiveSessions.ContainsKey(detection.game);
                if (renameActive) BackupSessions();
                Settings.gameOverrides = overrides;
                Settings.reviewedGameKeys = reviewed.ToArray();
                File.WriteAllText(SettingsFile, Json.Serialize(Settings), Encoding.UTF8);
                if (renameActive)
                {
                    var session = ActiveSessions[detection.game]; ActiveSessions.Remove(detection.game);
                    GameSession target;
                    if (ActiveSessions.TryGetValue(title.Trim(), out target))
                    {
                        target.start = Math.Min(target.start, session.start);
                        target.samples = (target.samples ?? new GameMetricSample[0]).Concat(session.samples ?? new GameMetricSample[0]).Where(x => x != null).OrderBy(x => x.utc).Take(240).ToArray();
                        Sessions.Remove(session);
                    }
                    else { session.game = title.Trim(); ActiveSessions[session.game] = session; }
                    KnownGames.Add(title.Trim()); MarkSessionsDirty(true);
                }
            }
        }
        internal static void ResetGameReview(string key)
        {
            lock (Gate)
            {
                if (String.IsNullOrWhiteSpace(key) || !ReviewItems.ContainsKey(key)) throw new ArgumentException("找不到这条游戏识别记录。");
                var overrides = new Dictionary<string, string>(Settings.gameOverrides ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase);
                overrides.Remove(key);
                Settings.gameOverrides = overrides;
                Settings.reviewedGameKeys = (Settings.reviewedGameKeys ?? new string[0]).Where(x => !String.Equals(x, key, StringComparison.OrdinalIgnoreCase)).ToArray();
                File.WriteAllText(SettingsFile, Json.Serialize(Settings), Encoding.UTF8);
            }
        }
        internal static void EditNativeSession(string id, string game, long end, bool delete)
        {
            lock (Gate)
            {
                var session = Sessions.FirstOrDefault(s => s.id == id);
                if (session == null) throw new InvalidOperationException("找不到这条记录");
                if (ActiveSessions.Values.Contains(session)) throw new InvalidOperationException("游戏仍在运行，请先退出游戏");
                if (!delete && (String.IsNullOrWhiteSpace(game) || game.Length > 100 || end < session.start || end > DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + 60000)) throw new ArgumentException("记录内容无效");
                BackupSessions();
                if (delete) Sessions.Remove(session);
                else { session.game = game.Trim(); session.end = end; KnownGames.Add(session.game); }
            }
            SaveSessionsNow();
        }
        internal static void PurgeNativeGame(string game)
        {
            if (String.IsNullOrWhiteSpace(game) || game.Length > 100) throw new ArgumentException("游戏名称无效");
            lock (Gate)
            {
                if (ActiveSessions.ContainsKey(game)) throw new InvalidOperationException("游戏仍在运行，请先退出游戏");
                BackupSessions();
                Sessions.RemoveAll(s => String.Equals(s.game, game, StringComparison.OrdinalIgnoreCase));
            }
            SaveSessionsNow();
        }
        private static void BackupSessions()
        {
            if (File.Exists(SessionFile)) File.Copy(SessionFile, Path.Combine(DataDir, "sessions-backup-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + ".json"));
        }
        internal static string CheckNativeRelease()
        {
            SetUpdateProgress("checking", 3, "", "正在查询 GitHub 最新版本…");
            try
            {
                string latest = LatestReleaseVersion();
                Version newest, current;
                bool available = Version.TryParse(latest, out newest) && Version.TryParse(BuildInfo.Version, out current) && newest > current;
                SetUpdateProgress(available ? "available" : "latest", 100, latest, available ? "发现可用更新 " + latest + "。" : "当前已是最新版本 " + BuildInfo.Version + "。");
                return latest;
            }
            catch (Exception ex)
            {
                SetUpdateProgress("failed", -1, "", "检查更新失败：" + ex.Message);
                throw;
            }
        }
        internal static void InstallNativeUpdate()
        {
            lock (Gate) if (ActiveSessions.Count > 0) throw new InvalidOperationException("游戏仍在运行，请退出游戏后安装更新");
            ThreadPool.QueueUserWorkItem(delegate { CheckForUpdate("--display-force"); });
        }
        internal static DashboardState Snapshot()
        {
            lock (Gate)
            {
                bool online = Last != null && (DateTime.UtcNow - LastSeen).TotalSeconds < 5;
                if (!online && (ActiveSessions.Count > 0 || AnyGame))
                {
                    long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                    foreach (var s in ActiveSessions.Values) s.end = now;
                    ActiveSessions.Clear(); AnyGame = false; ReturnAt = DateTime.UtcNow.AddMinutes(10); MarkSessionsDirty(true);
                    ThreadPool.QueueUserWorkItem(delegate { Thread.Sleep(TimeSpan.FromMinutes(10)); lock (Gate) { if (!AnyGame && ReturnAt != DateTime.MinValue && DateTime.UtcNow >= ReturnAt) { Role = "ame"; ReturnAt = DateTime.MinValue; EventId++; } } });
                }
                return new DashboardState { online = online, host = Last == null ? "等待主机连接" : Last.host, hostAddress = Settings.hostAddress, cpu = !online || Last == null ? -1 : Last.cpu, memoryUsed = !online || Last == null ? 0 : Last.memoryUsed, memoryTotal = !online || Last == null ? 0 : Last.memoryTotal, gpu = !online || Last == null ? -1 : Last.gpu, cpuTemp = !online || Last == null ? -1 : Last.cpuTemp, gpuTemp = !online || Last == null ? -1 : Last.gpuTemp, games = !online ? new string[0] : ActiveSessions.Keys.ToArray(), foliaKey = online && Last != null ? Last.foliaKey : null, foliaConnected = online && Last != null && Last.foliaConnected, foliaPlaying = online && Last != null && Last.foliaPlaying, foliaRunning = online && Last != null && Last.foliaRunning, foliaSong = online && Last != null ? Last.foliaSong : null, foliaArtist = online && Last != null ? Last.foliaArtist : null, foliaCover = online && Last != null && Last.foliaCover, reviewItems = ReviewItems.Values.OrderByDescending(x => x.seen).Take(100).ToArray(), reviewedGameKeys = (Settings.reviewedGameKeys ?? new string[0]).ToArray(), connectionError = LastTcpError, connectionAttemptUtc = LastConnectionAttemptUtc, connectionSuccessUtc = LastConnectionSuccessUtc, connectionRttMs = LastConnectionRttMs, resolvedAddress = LastResolvedAddress, packetAgeMs = LastSeen == DateTime.MinValue ? -1 : (long)(DateTime.UtcNow - LastSeen).TotalMilliseconds, role = Role, roleEvent = EventId, returnInSeconds = ReturnAt == DateTime.MinValue ? 0 : Math.Max(0, (long)(ReturnAt - DateTime.UtcNow).TotalSeconds), sessions = Sessions.Take(1000).Select(s => new GameSession { id = s.id, game = s.game, start = s.start, end = s.end, samples = s.samples }).ToArray(), version = BuildInfo.Version, hostVersion = Last == null ? "—" : Last.version, knownGames = KnownGames.OrderBy(x => x).ToArray(), excludedGames = (Settings.excludedGames ?? new string[0]).ToArray(), autoUpdate = Settings.autoUpdate, hostAutoUpdate = Settings.hostAutoUpdate };
            }
        }
        private static string SessionFile { get { return Path.Combine(DataDir, "sessions.json"); } }
        private static void LoadSessions()
        {
            try
            {
                string source = SessionFile;
                GameSession[] loaded;
                if (!TryReadSessions(source, out loaded))
                {
                    source = SessionFile + ".previous";
                    if (!TryReadSessions(source, out loaded)) return;
                    Log("session recovery: restored previous valid copy");
                    try { File.Copy(source, SessionFile, true); } catch (Exception ex) { Log("session recovery copy: " + ex.Message); }
                }
                long lastSaved = new DateTimeOffset(File.GetLastWriteTimeUtc(source)).ToUnixTimeMilliseconds();
                Sessions.AddRange(loaded);
                foreach (var session in Sessions) if (!String.IsNullOrWhiteSpace(session.game)) KnownGames.Add(session.game);
                if (CloseInterruptedSessions(Sessions, lastSaved)) SaveSessionsNow();
            }
            catch (Exception ex) { Log("session load: " + ex.Message); }
        }
        private static bool TryReadSessions(string path, out GameSession[] sessions)
        {
            sessions = null;
            if (!File.Exists(path)) return false;
            try
            {
                sessions = Json.Deserialize<GameSession[]>(File.ReadAllText(path, Encoding.UTF8));
                return sessions != null;
            }
            catch (Exception ex) { Log("session read " + Path.GetFileName(path) + ": " + ex.Message); return false; }
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
        private static GameSession CloneSession(GameSession session)
        {
            return new GameSession
            {
                id = session.id,
                game = session.game,
                start = session.start,
                end = session.end,
                samples = session.samples == null ? null : (GameMetricSample[])session.samples.Clone()
            };
        }

        private static void MarkSessionsDirty(bool urgent)
        {
            SessionsDirty = true;
            DateTime due = urgent ? DateTime.UtcNow : DateTime.UtcNow.AddSeconds(SessionSaveSeconds);
            if (NextSessionSaveUtc == DateTime.MinValue || due < NextSessionSaveUtc) NextSessionSaveUtc = due;
        }

        // Called from the TCP loop once per second. It only takes Gate long enough to
        // clone the session list; serialization and disk I/O happen after the lock is released.
        private static void SaveSessionsIfDue()
        {
            SaveSessionsSnapshot(false);
        }

        internal static void FlushSessions()
        {
            SaveSessionsSnapshot(true);
        }

        private static void SaveSessionsNow()
        {
            SaveSessionsSnapshot(true);
        }

        private static void SaveSessionsSnapshot(bool force)
        {
            GameSession[] snapshot;
            lock (Gate)
            {
                if (!SessionsDirty && !force) return;
                if (!force && DateTime.UtcNow < NextSessionSaveUtc) return;
                SessionsDirty = false;
                NextSessionSaveUtc = DateTime.MinValue;
                snapshot = Sessions.Take(1000).Select(CloneSession).ToArray();
            }
            lock (SaveGate)
            {
                string pending = SessionFile + ".pending";
                try
                {
                    File.WriteAllText(pending, Json.Serialize(snapshot), Encoding.UTF8);
                    if (File.Exists(SessionFile)) File.Replace(pending, SessionFile, SessionFile + ".previous", true);
                    else File.Move(pending, SessionFile);
                }
                catch (Exception ex)
                {
                    Log("session save: " + ex.Message);
                    lock (Gate) { SessionsDirty = true; NextSessionSaveUtc = DateTime.UtcNow.AddSeconds(5); }
                }
                finally { try { if (File.Exists(pending)) File.Delete(pending); } catch { } }
            }
        }
        private static void Log(string message)
        {
            try { File.AppendAllText(Path.Combine(DataDir, "update.log"), DateTime.Now.ToString("s") + " " + message + Environment.NewLine, Encoding.UTF8); } catch { }
        }
        private static void RestoreDashboard()
        {
            try { if (Tray != null) Tray.OpenDashboard(); } catch { }
        }
    }

    internal sealed class TrayContext : ApplicationContext
    {
        private readonly NotifyIcon Icon;
        private readonly Control Dispatcher;
        private NativeConsoleWindow Dashboard;
        public TrayContext()
        {
            Dispatcher = new Control();
            IntPtr dispatcherHandle = Dispatcher.Handle;
            Dashboard = new NativeConsoleWindow();
            var menu = new ContextMenuStrip();
            menu.Items.Add("打开控制台", null, delegate { OpenDashboard(); });
            menu.Items.Add("退出程序", null, delegate { DisplayServer.FlushSessions(); Environment.Exit(0); });
            Icon = new NotifyIcon { Text = "局域网桌面控制台", Icon = SystemIcons.Application, ContextMenuStrip = menu, Visible = true };
            Icon.DoubleClick += delegate { OpenDashboard(); };
        }
        public void OpenDashboard()
        {
            if (Dispatcher.InvokeRequired) { try { Dispatcher.BeginInvoke(new MethodInvoker(OpenDashboard)); } catch { } return; }
            if (Dashboard == null || Dashboard.IsDisposed) Dashboard = new NativeConsoleWindow();
            if (!Dashboard.Visible) Dashboard.Show();
            Dashboard.Bounds = Screen.PrimaryScreen.Bounds;
            Dashboard.WindowState = FormWindowState.Normal;
            Dashboard.Activate();
        }
        public void MinimizeDashboard()
        {
            if (Dispatcher.InvokeRequired) { try { Dispatcher.BeginInvoke(new MethodInvoker(MinimizeDashboard)); } catch { } return; }
            if (Dashboard != null && !Dashboard.IsDisposed) Dashboard.Hide();
        }
        public void CloseDashboard() { DisplayServer.FlushSessions(); Environment.Exit(0); }
        protected override void ExitThreadCore() { DisplayServer.FlushSessions(); Icon.Visible = false; Icon.Dispose(); base.ExitThreadCore(); }
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
            menu.Items.Add("设置…", null, delegate { using (var dialog = new NativeHostSettingsDialog()) dialog.ShowDialog(); });
            menu.Items.Add("退出主机监控", null, delegate { Environment.Exit(0); });
            Icon = new NotifyIcon { Text = "局域网桌面控制台 · 主机监控", Icon = SystemIcons.Application, ContextMenuStrip = menu, Visible = true };
        }
        protected override void ExitThreadCore() { Icon.Visible = false; Icon.Dispose(); base.ExitThreadCore(); }
    }
}



