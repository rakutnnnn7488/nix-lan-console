using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace NixLanConsole
{
    // Renders the host Folia browser source inside a native game-style card.
    // Edge is only a rendering process; no separate browser window is exposed.
    internal sealed class FoliaWindowHost : IDisposable
    {
        private readonly Panel Surface;
        private readonly Label Placeholder;
        private readonly Timer PollTimer;
        private readonly string ProfilePath;
        private Process StartedProcess;
        private string CurrentUrl;
        private IntPtr EmbeddedWindow;
        private IntPtr OriginalStyle;
        private NativeRect OriginalBounds;
        private bool HasOriginalStyle;
        private bool HasOriginalBounds;
        private DateTime LaunchStarted;
        private Size LastSize;

        private const int GwlStyle = -16;
        private const long WsChild = 0x40000000L;
        private const long WsPopup = unchecked((long)0x80000000);
        private const long WsCaption = 0x00C00000L;
        private const long WsThickFrame = 0x00040000L;
        private const long WsMinimizeBox = 0x00020000L;
        private const long WsMaximizeBox = 0x00010000L;
        private const long WsSysMenu = 0x00080000L;
        private const uint SwpNoZOrder = 0x0004;
        private const uint SwpFrameChanged = 0x0020;
        private const uint SwpShowWindow = 0x0040;
        private const int SwShownormal = 1;
        private const int WmClose = 0x0010;

        public FoliaWindowHost(Panel surface)
        {
            Surface = surface;
            ProfilePath = Path.Combine(NixPaths.DataDir, "FoliaCardBrowser");
            Placeholder = new Label
            {
                Dock = DockStyle.Fill,
                Text = "Folia 歌词动效\r\n\r\n等待主机播放音乐…",
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = Color.FromArgb(224, 207, 241),
                BackColor = Color.FromArgb(27, 20, 43),
                Font = new Font("Microsoft YaHei UI", 12F, FontStyle.Regular)
            };
            Surface.Controls.Add(Placeholder);
            Surface.Resize += delegate { ResizeEmbeddedWindow(); };
            PollTimer = new Timer { Interval = 1200 };
            PollTimer.Tick += delegate { FindAndAttachWindow(); };
        }

        public void Launch(string url)
        {
            Uri target;
            if (!Uri.TryCreate(url, UriKind.Absolute, out target) || target.Scheme != "http" ||
                target.AbsolutePath != "/display" || String.IsNullOrWhiteSpace(target.Query))
                throw new ArgumentException("主机尚未提供 Folia 歌词显示地址。");

            if (EmbeddedWindow != IntPtr.Zero && IsWindow(EmbeddedWindow) && String.Equals(url, CurrentUrl, StringComparison.Ordinal))
            {
                ShowWindow(EmbeddedWindow, SwShownormal);
                ResizeEmbeddedWindow();
                return;
            }
            Close();

            string edge = FindEdge();
            if (edge == null) throw new FileNotFoundException("副机缺少 Microsoft Edge，无法显示 Folia 原生歌词动效。");
            Directory.CreateDirectory(ProfilePath);
            CurrentUrl = url;
            LaunchStarted = DateTime.UtcNow;
            Placeholder.Text = "正在连接主机 Folia 歌词动效…";
            Placeholder.Visible = true;
            string arguments = "--app=\"" + url + "\" --user-data-dir=\"" + ProfilePath +
                "\" --no-first-run --disable-features=Translate,msEdgeTranslate --window-size=1000,700 --window-position=-20000,-20000";
            StartedProcess = Process.Start(new ProcessStartInfo(edge, arguments)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Normal
            });
            PollTimer.Start();
            FindAndAttachWindow();
        }

        private static string FindEdge()
        {
            string[] paths = new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Microsoft", "Edge", "Application", "msedge.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Microsoft", "Edge", "Application", "msedge.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "Edge", "Application", "msedge.exe")
            };
            return paths.FirstOrDefault(File.Exists);
        }

        private void FindAndAttachWindow()
        {
            if (EmbeddedWindow != IntPtr.Zero && IsWindow(EmbeddedWindow)) { PollTimer.Stop(); return; }
            if (CurrentUrl == null) { PollTimer.Stop(); return; }
            if ((DateTime.UtcNow - LaunchStarted).TotalSeconds > 25)
            {
                PollTimer.Stop();
                Placeholder.Text = "无法打开歌词动效。请确认主机 Folia 正在运行，并在 Folia 中启用了 OBS 浏览器源。";
                return;
            }
            try
            {
                var matchingIds = new HashSet<int>();
                if (StartedProcess != null)
                {
                    try
                    {
                        if (!StartedProcess.HasExited)
                        {
                            matchingIds.Add(StartedProcess.Id);
                            StartedProcess.Refresh();
                            if (StartedProcess.MainWindowHandle != IntPtr.Zero)
                            {
                                Attach(StartedProcess.MainWindowHandle);
                                return;
                            }
                        }
                    }
                    catch { }
                }
                try
                {
                    using (var searcher = new ManagementObjectSearcher("root\\cimv2", "SELECT ProcessId,CommandLine FROM Win32_Process WHERE Name='msedge.exe'"))
                    using (var results = searcher.Get())
                        foreach (ManagementObject item in results)
                            using (item)
                            {
                                string commandLine = Convert.ToString(item["CommandLine"]);
                                if (!String.IsNullOrEmpty(commandLine) && commandLine.IndexOf(ProfilePath, StringComparison.OrdinalIgnoreCase) >= 0)
                                    matchingIds.Add(Convert.ToInt32(item["ProcessId"]));
                            }
                }
                catch (Exception ex) { StartupDiagnostics.Write("Folia Edge process query: " + ex.Message); }
                if (matchingIds.Count == 0) return;

                IntPtr best = IntPtr.Zero;
                long bestArea = 0;
                EnumWindows(delegate(IntPtr window, IntPtr state)
                {
                    uint processId;
                    GetWindowThreadProcessId(window, out processId);
                    if (!matchingIds.Contains((int)processId) || !IsWindowVisible(window) || GetWindow(window, 4) != IntPtr.Zero) return true;
                    NativeRect rect;
                    if (!GetWindowRect(window, out rect)) return true;
                    long area = (long)Math.Max(0, rect.Right - rect.Left) * Math.Max(0, rect.Bottom - rect.Top);
                    if (area > bestArea && area > 30000) { best = window; bestArea = area; }
                    return true;
                }, IntPtr.Zero);
                if (best != IntPtr.Zero) Attach(best);
            }
            catch (Exception ex) { StartupDiagnostics.Write("Folia display window lookup: " + ex.Message); }
        }

        private void Attach(IntPtr window)
        {
            if (window == IntPtr.Zero || !IsWindow(window)) return;
            if (!GetWindowRect(window, out OriginalBounds)) return;
            HasOriginalBounds = true;
            OriginalStyle = GetWindowStyle(window, GwlStyle);
            HasOriginalStyle = true;
            long style = OriginalStyle.ToInt64();
            style &= ~(WsPopup | WsCaption | WsThickFrame | WsMinimizeBox | WsMaximizeBox | WsSysMenu);
            style |= WsChild | 0x10000000L;
            SetWindowStyle(window, GwlStyle, new IntPtr(style));
            SetParent(window, Surface.Handle);
            if (GetParent(window) != Surface.Handle)
            {
                SetParent(window, IntPtr.Zero);
                SetWindowStyle(window, GwlStyle, OriginalStyle);
                HasOriginalStyle = HasOriginalBounds = false;
                Placeholder.Text = "无法嵌入歌词动效窗口。请确认控制台和 Edge 以相同权限运行。";
                PollTimer.Stop();
                return;
            }
            EmbeddedWindow = window;
            LastSize = Size.Empty;
            Placeholder.Visible = false;
            ResizeEmbeddedWindow();
            PollTimer.Stop();
        }

        private void ResizeEmbeddedWindow()
        {
            if (EmbeddedWindow == IntPtr.Zero || !IsWindow(EmbeddedWindow) || Surface.IsDisposed || !Surface.IsHandleCreated) return;
            Size size = new Size(Math.Max(1, Surface.ClientSize.Width), Math.Max(1, Surface.ClientSize.Height));
            if (size == LastSize) return;
            LastSize = size;
            SetWindowPos(EmbeddedWindow, IntPtr.Zero, 0, 0, size.Width, size.Height, SwpNoZOrder | SwpFrameChanged | SwpShowWindow);
        }

        public void Close()
        {
            PollTimer.Stop();
            IntPtr window = EmbeddedWindow;
            EmbeddedWindow = IntPtr.Zero;
            if (window != IntPtr.Zero && IsWindow(window))
            {
                try
                {
                    SetParent(window, IntPtr.Zero);
                    if (HasOriginalStyle) SetWindowStyle(window, GwlStyle, OriginalStyle);
                    if (HasOriginalBounds) SetWindowPos(window, IntPtr.Zero, OriginalBounds.Left, OriginalBounds.Top,
                        Math.Max(1, OriginalBounds.Right - OriginalBounds.Left), Math.Max(1, OriginalBounds.Bottom - OriginalBounds.Top),
                        SwpNoZOrder | SwpFrameChanged | SwpShowWindow);
                    PostMessage(window, WmClose, IntPtr.Zero, IntPtr.Zero);
                }
                catch { }
            }
            HasOriginalStyle = HasOriginalBounds = false;
            LastSize = Size.Empty;
            CurrentUrl = null;
            if (StartedProcess != null)
            {
                try
                {
                    if (!StartedProcess.HasExited && !StartedProcess.WaitForExit(500))
                    {
                        StartedProcess.Kill();
                        StartedProcess.WaitForExit(500);
                    }
                }
                catch { }
                StartedProcess.Dispose();
                StartedProcess = null;
            }
            if (!Surface.IsDisposed) { Placeholder.Visible = true; Placeholder.Text = "Folia 歌词动效\r\n\r\n等待主机播放音乐…"; }
        }

        public void Dispose()
        {
            Close();
            PollTimer.Dispose();
        }

        private static IntPtr GetWindowStyle(IntPtr window, int index) { return IntPtr.Size == 8 ? GetWindowLongPtr64(window, index) : new IntPtr(GetWindowLong32(window, index)); }
        private static IntPtr SetWindowStyle(IntPtr window, int index, IntPtr value) { return IntPtr.Size == 8 ? SetWindowLongPtr64(window, index, value) : new IntPtr(SetWindowLong32(window, index, value.ToInt32())); }

        [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
        private delegate bool EnumWindowsProc(IntPtr window, IntPtr state);

        [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr state);
        [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr window);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
        [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr window, uint command);
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out NativeRect rect);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
        [DllImport("user32.dll")] private static extern IntPtr SetParent(IntPtr child, IntPtr parent);
        [DllImport("user32.dll")] private static extern IntPtr GetParent(IntPtr window);
        [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr window, IntPtr insertAfter, int x, int y, int width, int height, uint flags);
        [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr window, int command);
        [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr window, int message, IntPtr wparam, IntPtr lparam);
        [DllImport("user32.dll", EntryPoint = "GetWindowLong", SetLastError = true)] private static extern int GetWindowLong32(IntPtr window, int index);
        [DllImport("user32.dll", EntryPoint = "SetWindowLong", SetLastError = true)] private static extern int SetWindowLong32(IntPtr window, int index, int value);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr", SetLastError = true)] private static extern IntPtr GetWindowLongPtr64(IntPtr window, int index);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr", SetLastError = true)] private static extern IntPtr SetWindowLongPtr64(IntPtr window, int index, IntPtr value);
    }

    // Migrate the previous secondary-PC fullscreen watcher to this console.
    // Keep its files and startup shortcut in a local backup so the change is reversible.
    internal static class LegacyFoliaDisplayMigration
    {
        internal static void Run()
        {
            string legacyDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FoliaAutoDisplay");
            if (!Directory.Exists(legacyDir)) return;
            string shortcut = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), "FoliaAutoDisplay.lnk");
            if (File.Exists(shortcut) && File.Exists(Path.Combine(legacyDir, "start.vbs")))
            {
                try
                {
                    string backup = Path.Combine(NixPaths.DataDir, "FoliaAutoDisplay-startup.backup.lnk");
                    Directory.CreateDirectory(NixPaths.DataDir);
                    if (!File.Exists(backup)) File.Move(shortcut, backup);
                    else File.Delete(shortcut);
                    StartupDiagnostics.Write("Legacy Folia fullscreen startup disabled; shortcut backed up.");
                }
                catch (Exception ex) { StartupDiagnostics.Write("Legacy Folia startup migration: " + ex.Message); }
            }
            try
            {
                using (var searcher = new ManagementObjectSearcher("root\\cimv2", "SELECT ProcessId,Name,CommandLine FROM Win32_Process WHERE Name='powershell.exe' OR Name='msedge.exe'"))
                using (var results = searcher.Get())
                    foreach (ManagementObject item in results)
                        using (item)
                        {
                            string name = Convert.ToString(item["Name"]);
                            string command = Convert.ToString(item["CommandLine"]);
                            bool oldWatcher = String.Equals(name, "powershell.exe", StringComparison.OrdinalIgnoreCase) &&
                                command.IndexOf(legacyDir, StringComparison.OrdinalIgnoreCase) >= 0 &&
                                (command.IndexOf("watch.ps1", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                 command.IndexOf("updater.ps1", StringComparison.OrdinalIgnoreCase) >= 0);
                            bool oldKiosk = String.Equals(name, "msedge.exe", StringComparison.OrdinalIgnoreCase) &&
                                command.IndexOf(Path.Combine(legacyDir, "Browser"), StringComparison.OrdinalIgnoreCase) >= 0 &&
                                command.IndexOf("--kiosk", StringComparison.OrdinalIgnoreCase) >= 0;
                            if (!oldWatcher && !oldKiosk) continue;
                            int pid = Convert.ToInt32(item["ProcessId"]);
                            using (Process process = Process.GetProcessById(pid))
                            {
                                process.Kill();
                                process.WaitForExit(1500);
                            }
                        }
            }
            catch (Exception ex) { StartupDiagnostics.Write("Legacy Folia process migration: " + ex.Message); }
        }
    }
}
