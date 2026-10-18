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
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.ThreadException += delegate(object sender, ThreadExceptionEventArgs e) { StartupDiagnostics.Write("UI crash: " + e.Exception); Environment.Exit(1); };
                AppDomain.CurrentDomain.UnhandledException += delegate(object sender, UnhandledExceptionEventArgs e) { StartupDiagnostics.Write("Unhandled crash: " + e.ExceptionObject); };
                StartupDiagnostics.Write("launch version=" + BuildInfo.Version + " role=" + (args.Length == 0 ? "picker" : args[0]) + " pid=" + Process.GetCurrentProcess().Id);
                if (args.Length > 0 && args[0] == "--sensor")
                {
                    bool created;
                    using (var mutex = new Mutex(true, "Local\\NixLanConsole.Host", out created))
                        if (created) HostSensor.Run();
                    return;
                }
                if (args.Length > 0 && args[0] == "--display")
                {
                    bool created;
                    using (var mutex = new Mutex(true, "Local\\NixLanConsole.Display", out created))
                        if (created) DisplayServer.Run();
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

    internal static class StartupDiagnostics
    {
        private static readonly string DataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NixLanConsole");
        public static void Write(string message)
        {
            try { Directory.CreateDirectory(DataDir); File.AppendAllText(Path.Combine(DataDir, "update.log"), DateTime.Now.ToString("s") + " " + message + Environment.NewLine, Encoding.UTF8); } catch { }
        }
        public static void Ready(string role)
        {
            try { Directory.CreateDirectory(DataDir); File.WriteAllText(Path.Combine(DataDir, "startup-ready.txt"), Process.GetCurrentProcess().Id + "|" + BuildInfo.Version + "|" + role, Encoding.UTF8); } catch { }
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
    }

    internal sealed class ControlPacket
    {
        public string[] excludedGames;
        public bool hostAutoUpdate = true;
    }

    internal sealed class ConsoleSettings
    {
        public bool autoUpdate = true;
        public bool hostAutoUpdate = true;
        public string[] excludedGames = new[] { "Wallpaper Engine" };
        public string hostAddress = "192.168.1.7";
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
        private static readonly string DataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NixLanConsole");
        private static readonly JavaScriptSerializer Json = new JavaScriptSerializer();
        private static readonly string BootId = Guid.NewGuid().ToString("N");
        private static PerformanceCounter CpuCounter;
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
        private static volatile bool RemoteAutoUpdate = true;
        private static System.Threading.Timer UpdateTimer;
        private static volatile bool GameRunning;
        private static readonly object MetricGate = new object();
        private static MetricPacket LatestPacket;
        internal static bool HasGameRunning { get { return GameRunning; } }
        internal static bool AutoUpdateEnabled { get { return RemoteAutoUpdate; } }

        public static void Run()
        {
            Directory.CreateDirectory(DataDir);
            StartTray();
            DiscoverGames();
            LoadCustomGames();
            UpdateTimer = new System.Threading.Timer(DisplayServer.CheckForUpdate, "--sensor", TimeSpan.FromSeconds(20), TimeSpan.FromMinutes(5));
            try { CpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total"); CpuCounter.NextValue(); } catch { }
            var tcpThread = new Thread(TcpServerLoop) { IsBackground = true };
            tcpThread.Start();
            while (true)
            {
                var p = new MetricPacket { host = Environment.MachineName, bootId = BootId, version = BuildInfo.Version, utc = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), cpu = ReadCpu(), memoryUsed = 0, memoryTotal = 0, gpu = ReadGpuUsage(), cpuTemp = -1, gpuTemp = -1, games = ReadGames(), catalog = GetGameCatalog() };
                ReadMemory(out p.memoryUsed, out p.memoryTotal);
                if ((DateTime.UtcNow - LastTemperatureRead).TotalSeconds >= 5) { ReadTemperatures(); LastTemperatureRead = DateTime.UtcNow; }
                p.cpuTemp = CpuTemperature; p.gpuTemp = GpuTemperature;
                lock (MetricGate) LatestPacket = p;
                Thread.Sleep(1000);
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
                    listener.Start();
                    StartupDiagnostics.Write("TCP listener ready port=" + TcpPort);
                    StartupDiagnostics.Ready("--sensor");
                    while (true)
                    {
                        TcpClient client = listener.AcceptTcpClient();
                        ThreadPool.QueueUserWorkItem(delegate(object state) { ServeClient((TcpClient)state); }, client);
                    }
                }
                catch (Exception ex) { StartupDiagnostics.Write("TCP listener retry: " + ex.Message); Thread.Sleep(5000); }
                finally { if (listener != null) try { listener.Stop(); } catch { } }
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
                        if (String.IsNullOrEmpty(line) || line.Length > 16384) return;
                        var control = Json.Deserialize<ControlPacket>(line);
                        if (control != null)
                        {
                            lock (ControlGate) RemoteExcludedGames = new HashSet<string>((control.excludedGames ?? new string[0]).Where(x => !String.IsNullOrWhiteSpace(x)), StringComparer.OrdinalIgnoreCase);
                            RemoteAutoUpdate = control.hostAutoUpdate;
                        }
                        MetricPacket current; lock (MetricGate) current = LatestPacket;
                        if (current != null) writer.WriteLine(Json.Serialize(current));
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
            if ((DateTime.UtcNow - LastGameDiscovery).TotalMinutes >= 5) DiscoverGames();
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            HashSet<string> excluded;
            lock (ControlGate) excluded = new HashSet<string>(RemoteExcludedGames, StringComparer.OrdinalIgnoreCase);
            foreach (var p in Process.GetProcesses())
            {
                try
                {
                    string path = null;
                    try { path = Path.GetFullPath(p.MainModule.FileName); } catch { }
                    string title;
                    if (TryResolveGame(path, p.ProcessName, out title) && !excluded.Contains(title)) names.Add(title);
                }
                catch { }
                finally { p.Dispose(); }
            }
            GameRunning = names.Count > 0;
            return names.ToArray();
        }
        private static bool TryResolveGame(string path, string processName, out string title)
        {
            title = null;
            if (!String.IsNullOrWhiteSpace(path) && (CustomGameExecutables.TryGetValue(path, out title) || GameExecutables.TryGetValue(path, out title)))
                return !IsSystemUtility(path, title);
            if (String.IsNullOrWhiteSpace(processName)) return false;
            string exe = Path.GetFileName(processName);
            if (!exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) exe += ".exe";
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

    internal static class BuildInfo { public const string Version = "1.6.3.2"; }

    internal static class DisplayServer
    {
        private const int TcpPort = 46702;
        private static readonly JavaScriptSerializer Json = new JavaScriptSerializer();
        private static readonly object Gate = new object();
        private static readonly List<GameSession> Sessions = new List<GameSession>();
        private static readonly Dictionary<string, GameSession> ActiveSessions = new Dictionary<string, GameSession>(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> KnownGames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
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
        private static readonly object UpdateProgressGate = new object();
        private static string UpdateProgressState = "idle";
        private static string UpdateProgressMessage = "每 30 分钟检查 GitHub 最新版本。";
        private static string UpdateProgressVersion = "";
        private static int UpdateProgressPercent = -1;
        private static readonly string BaseDir = AppDomain.CurrentDomain.BaseDirectory;
        private static readonly string DataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NixLanConsole");

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
            Directory.CreateDirectory(DataDir); LoadSettings(); LoadSessions();
            UpdateTimer = new System.Threading.Timer(CheckForUpdate, null, TimeSpan.FromSeconds(20), TimeSpan.FromMinutes(30));
            Tray = new TrayContext();
            var tcpThread = new Thread(ReadTcp) { IsBackground = true }; tcpThread.Start();
            StartupDiagnostics.Ready("--display");
            Application.Run(Tray);
        }
        internal static void CheckForUpdate(object ignored)
        {
            if (Interlocked.CompareExchange(ref UpdateStarted, 1, 0) != 0) return;
            try
            {
                bool hostMode = String.Equals(ignored as string, "--sensor", StringComparison.Ordinal);
                bool force = String.Equals(ignored as string, "--display-force", StringComparison.Ordinal);
                if (hostMode)
                {
                    if (HostSensor.HasGameRunning) { SetUpdateProgress("waiting", -1, "", "主机游戏运行中，自动更新检查暂缓。"); return; }
                    if (!HostSensor.AutoUpdateEnabled) { SetUpdateProgress("disabled", -1, "", "主机自动更新已关闭。"); return; }
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
                if (sourceZip == null) { SetUpdateProgress("latest", 100, current.ToString(), "当前版本已是最新版本 " + current + "。"); return; }
                string failedUpdate = Path.Combine(DataDir, "failed-update.txt");
                if (!force && File.Exists(failedUpdate) && String.Equals(File.ReadAllText(failedUpdate, Encoding.UTF8).Trim(), available.ToString(), StringComparison.OrdinalIgnoreCase) && DateTime.UtcNow - File.GetLastWriteTimeUtc(failedUpdate) < TimeSpan.FromHours(24))
                {
                    SetUpdateProgress("failed", -1, available.ToString(), "版本 " + available + " 上次安装失败，已暂停自动重试。可在设置中手动重试。");
                    return;
                }
                SetUpdateProgress("preparing", 96, available.ToString(), "更新包已下载，正在验证并准备安装…");
                string updateDir = Path.Combine(DataDir, "updates"); Directory.CreateDirectory(updateDir);
                string localZip = Path.Combine(updateDir, "pending-" + available + "-" + Guid.NewGuid().ToString("N") + ".zip");
                File.Copy(sourceZip, localZip, false);
                string updater = Path.Combine(BaseDir, "Updater.exe");
                if (!File.Exists(updater)) throw new FileNotFoundException("更新程序不存在", updater);
                string localUpdater = Path.Combine(updateDir, "Updater-" + Guid.NewGuid().ToString("N") + ".exe");
                File.Copy(updater, localUpdater);
                string args = "--apply " + Quote(BaseDir) + " " + Quote(localZip) + " " + Process.GetCurrentProcess().Id + " " + (hostMode ? "--sensor" : "--display");
                SetUpdateProgress("installing", 99, available.ToString(), "正在打开可见的更新窗口…");
                Process started = Process.Start(new ProcessStartInfo(localUpdater, args) { UseShellExecute = true, WorkingDirectory = updateDir });
                if (started == null) throw new InvalidOperationException("Windows 未能启动更新窗口。");
                started.Dispose();
                Environment.Exit(0);
            }
            catch (Exception ex)
            {
                SetUpdateProgress("failed", -1, "", "更新失败：" + ex.Message);
                try { File.AppendAllText(Path.Combine(DataDir, "update.log"), DateTime.Now.ToString("s") + " update check: " + ex.Message + Environment.NewLine, Encoding.UTF8); } catch { }
            }
            finally { Interlocked.Exchange(ref UpdateStarted, 0); }
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
                    if (archive.GetEntry("ControlConsole.exe") == null || archive.GetEntry("ApplyUpdate.ps1") == null || archive.GetEntry("Updater.exe") == null) return false;
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
        private static void ReadTcp()
        {
            DateTime lastErrorLog = DateTime.MinValue;
            while (true)
            {
                string address;
                lock (Gate) address = Settings.hostAddress;
                try
                {
                    IPAddress ip;
                    if (!IPAddress.TryParse(address, out ip)) ip = Dns.GetHostAddresses(address).FirstOrDefault(x => x.AddressFamily == AddressFamily.InterNetwork);
                    if (ip == null) throw new InvalidOperationException("主机地址无效：" + address);
                    using (var client = new TcpClient(AddressFamily.InterNetwork))
                    {
                        client.ReceiveTimeout = 1800; client.SendTimeout = 1800;
                        var pending = client.BeginConnect(ip, TcpPort, null, null);
                        if (!pending.AsyncWaitHandle.WaitOne(1800)) throw new TimeoutException("连接主机超时");
                        client.EndConnect(pending);
                        using (var stream = client.GetStream())
                        using (var reader = new StreamReader(stream, Encoding.UTF8))
                        using (var writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true })
                        {
                            ControlPacket control;
                            lock (Gate) control = new ControlPacket { excludedGames = Settings.excludedGames, hostAutoUpdate = Settings.hostAutoUpdate };
                            writer.WriteLine(Json.Serialize(control));
                            string line = reader.ReadLine();
                            if (String.IsNullOrEmpty(line) || line.Length > 65536) throw new IOException("主机返回的数据无效");
                            MetricPacket p = Json.Deserialize<MetricPacket>(line);
                            if (p != null) lock (Gate) Update(p);
                        }
                    }
                }
                catch (Exception ex) { if ((DateTime.UtcNow - lastErrorLog).TotalSeconds >= 30) { StartupDiagnostics.Write("TCP host connection: " + ex.Message); lastErrorLog = DateTime.UtcNow; } }
                Thread.Sleep(1000);
            }
        }
        private static void Update(MetricPacket p)
        {
            bool openForNewHost = ShouldOpenForHost(Last, p, DateTime.UtcNow - LastSeen);
            Last = p; LastSeen = DateTime.UtcNow;
            foreach (string name in p.catalog ?? new string[0]) if (!String.IsNullOrWhiteSpace(name)) KnownGames.Add(name);
            foreach (string name in p.games ?? new string[0]) if (!String.IsNullOrWhiteSpace(name)) KnownGames.Add(name);
            var excluded = new HashSet<string>(Settings.excludedGames ?? new string[0], StringComparer.OrdinalIgnoreCase);
            excluded.Add("Wallpaper Engine");
            var current = new HashSet<string>((p.games ?? new string[0]).Where(x => !excluded.Contains(x)), StringComparer.OrdinalIgnoreCase);
            foreach (string name in current)
                if (!ActiveSessions.ContainsKey(name))
                {
                    ActiveSessions[name] = new GameSession { id = Guid.NewGuid().ToString("N"), game = name, start = p.utc, end = 0 };
                    Sessions.Insert(0, ActiveSessions[name]);
                    // Keep detailed curves for recent sessions without growing sessions.json indefinitely.
                    foreach (var old in Sessions.Skip(50)) old.samples = null;
                }
            foreach (var session in ActiveSessions.Values)
            {
                var samples = session.samples == null ? new List<GameMetricSample>() : session.samples.ToList();
                if (samples.Count == 0 || p.utc - samples[samples.Count - 1].utc >= 20000)
                {
                    samples.Add(new GameMetricSample { utc = p.utc, cpu = p.cpu, gpu = p.gpu, cpuTemp = p.cpuTemp, gpuTemp = p.gpuTemp });
                    if (samples.Count > 240) samples = samples.Where((sample, index) => index % 2 == 0).ToList();
                    session.samples = samples.ToArray();
                }
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
            if (openForNewHost) RestoreDashboard();
            SaveSessions();
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
        private static void LoadSettings()
        {
            try
            {
                if (File.Exists(SettingsFile)) Settings = Json.Deserialize<ConsoleSettings>(File.ReadAllText(SettingsFile)) ?? new ConsoleSettings();
                if (String.IsNullOrWhiteSpace(Settings.hostAddress)) Settings.hostAddress = "192.168.1.7";
                var excluded = new HashSet<string>(Settings.excludedGames ?? new string[0], StringComparer.OrdinalIgnoreCase);
                excluded.Add("Wallpaper Engine");
                Settings.excludedGames = excluded.ToArray();
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
            var proposed = new ConsoleSettings { autoUpdate = autoUpdate, hostAutoUpdate = hostAutoUpdate, excludedGames = names.ToArray(), hostAddress = hostAddress };
            lock (Gate)
            {
                File.WriteAllText(SettingsFile, Json.Serialize(proposed), Encoding.UTF8);
                Settings = proposed;
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
                SaveSessions();
            }
        }
        internal static void PurgeNativeGame(string game)
        {
            if (String.IsNullOrWhiteSpace(game) || game.Length > 100) throw new ArgumentException("游戏名称无效");
            lock (Gate)
            {
                if (ActiveSessions.ContainsKey(game)) throw new InvalidOperationException("游戏仍在运行，请先退出游戏");
                BackupSessions();
                Sessions.RemoveAll(s => String.Equals(s.game, game, StringComparison.OrdinalIgnoreCase));
                SaveSessions();
            }
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
                    ActiveSessions.Clear(); AnyGame = false; ReturnAt = DateTime.UtcNow.AddMinutes(10); SaveSessions();
                    ThreadPool.QueueUserWorkItem(delegate { Thread.Sleep(TimeSpan.FromMinutes(10)); lock (Gate) { if (!AnyGame && ReturnAt != DateTime.MinValue && DateTime.UtcNow >= ReturnAt) { Role = "ame"; ReturnAt = DateTime.MinValue; EventId++; } } });
                }
                return new DashboardState { online = online, host = Last == null ? "等待主机连接" : Last.host, hostAddress = Settings.hostAddress, cpu = !online || Last == null ? -1 : Last.cpu, memoryUsed = !online || Last == null ? 0 : Last.memoryUsed, memoryTotal = !online || Last == null ? 0 : Last.memoryTotal, gpu = !online || Last == null ? -1 : Last.gpu, cpuTemp = !online || Last == null ? -1 : Last.cpuTemp, gpuTemp = !online || Last == null ? -1 : Last.gpuTemp, games = !online ? new string[0] : ActiveSessions.Keys.ToArray(), role = Role, roleEvent = EventId, returnInSeconds = ReturnAt == DateTime.MinValue ? 0 : Math.Max(0, (long)(ReturnAt - DateTime.UtcNow).TotalSeconds), sessions = Sessions.Take(1000).Select(s => new GameSession { id = s.id, game = s.game, start = s.start, end = s.end, samples = s.samples }).ToArray(), version = BuildInfo.Version, hostVersion = Last == null ? "—" : Last.version, knownGames = KnownGames.OrderBy(x => x).ToArray(), excludedGames = (Settings.excludedGames ?? new string[0]).ToArray(), autoUpdate = Settings.autoUpdate, hostAutoUpdate = Settings.hostAutoUpdate };
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
                if (CloseInterruptedSessions(Sessions, lastSaved)) SaveSessions();
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
        private static void SaveSessions()
        {
            string pending = SessionFile + ".pending";
            try
            {
                File.WriteAllText(pending, Json.Serialize(Sessions.Take(1000).ToArray()), Encoding.UTF8);
                if (File.Exists(SessionFile)) File.Replace(pending, SessionFile, SessionFile + ".previous", true);
                else File.Move(pending, SessionFile);
            }
            catch (Exception ex) { Log("session save: " + ex.Message); }
            finally { try { if (File.Exists(pending)) File.Delete(pending); } catch { } }
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
            menu.Items.Add("退出程序", null, delegate { Environment.Exit(0); });
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
        public void CloseDashboard() { Environment.Exit(0); }
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

