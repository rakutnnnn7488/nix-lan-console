using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace NixLanConsole
{
    // Hosts Folia's own native Electron window as a child of a draggable desktop card.
    // This keeps Folia responsible for playback and animation rendering.
    internal sealed class FoliaWindowHost : IDisposable
    {
        private readonly Panel Surface;
        private readonly Label Placeholder;
        private readonly Timer PollTimer;
        private string ExecutablePath;
        private IntPtr EmbeddedWindow;
        private IntPtr OriginalStyle;
        private NativeRect OriginalBounds;
        private bool HasOriginalStyle;
        private bool HasOriginalBounds;

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

        public FoliaWindowHost(Panel surface)
        {
            Surface = surface;
            Placeholder = new Label
            {
                Dock = DockStyle.Fill,
                Text = "Folia 动效窗口\r\n\r\n从桌面点击「Folia」启动并嵌入播放器。\r\n首次使用前，请先在设置中选择 Folia.exe。",
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = Color.FromArgb(224, 207, 241),
                BackColor = Color.FromArgb(27, 20, 43),
                Font = new Font("Microsoft YaHei UI", 12F, FontStyle.Regular)
            };
            Surface.Controls.Add(Placeholder);
            Surface.Resize += delegate { ResizeEmbeddedWindow(); };
            PollTimer = new Timer { Interval = 1800 };
            PollTimer.Tick += delegate { FindAndAttachWindow(); };
        }

        public void Launch(string executablePath)
        {
            ExecutablePath = (executablePath ?? "").Trim().Trim('"');
            if (ExecutablePath.Length == 0 || !File.Exists(ExecutablePath))
                throw new FileNotFoundException("请先在「开始 → 设置 → Folia」中选择有效的 Folia.exe。", ExecutablePath);

            if (EmbeddedWindow != IntPtr.Zero && IsWindow(EmbeddedWindow))
            {
                Surface.Visible = true;
                ShowWindow(EmbeddedWindow, SwShownormal);
                ResizeEmbeddedWindow();
                return;
            }

            Process.Start(new ProcessStartInfo { FileName = ExecutablePath, WorkingDirectory = Path.GetDirectoryName(ExecutablePath), UseShellExecute = true });
            Placeholder.Text = "正在启动 Folia…\r\n\r\n如果没有出现，请确认播放器已安装并能正常启动。";
            Placeholder.Visible = true;
            PollTimer.Start();
            FindAndAttachWindow();
        }

        private void FindAndAttachWindow()
        {
            if (EmbeddedWindow != IntPtr.Zero && IsWindow(EmbeddedWindow))
            {
                return;
            }
            if (EmbeddedWindow != IntPtr.Zero)
            {
                EmbeddedWindow = IntPtr.Zero;
                HasOriginalStyle = false;
                Placeholder.Text = "Folia 播放器窗口已关闭。\r\n\r\n点击桌面「Folia」图标可再次打开。";
                Placeholder.Visible = true;
            }
            if (String.IsNullOrWhiteSpace(ExecutablePath) || !File.Exists(ExecutablePath)) return;

            try
            {
                string expected = Path.GetFullPath(ExecutablePath);
                var matchingIdList = new List<int>();
                foreach (Process process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(expected)))
                {
                    try
                    {
                        if (String.Equals(Path.GetFullPath(process.MainModule.FileName), expected, StringComparison.OrdinalIgnoreCase)) matchingIdList.Add(process.Id);
                    }
                    catch { }
                    finally { process.Dispose(); }
                }
                int[] matchingIds = matchingIdList.ToArray();
                if (matchingIds.Length == 0) return;

                IntPtr best = IntPtr.Zero;
                long bestArea = 0;
                EnumWindows(delegate(IntPtr window, IntPtr state)
                {
                    uint processId;
                    GetWindowThreadProcessId(window, out processId);
                    if (!matchingIds.Contains((int)processId) || !IsWindowVisible(window) || GetWindow(window, 4) != IntPtr.Zero) return true;
                    var rect = new NativeRect();
                    if (!GetWindowRect(window, out rect)) return true;
                    long area = (long)Math.Max(0, rect.Right - rect.Left) * Math.Max(0, rect.Bottom - rect.Top);
                    if (area < 30000) return true;
                    int titleLength = GetWindowTextLength(window);
                    var title = new StringBuilder(Math.Max(2, titleLength + 1));
                    GetWindowText(window, title, title.Capacity);
                    if (title.ToString().IndexOf("Remote", StringComparison.OrdinalIgnoreCase) >= 0) return true;
                    if (area > bestArea) { best = window; bestArea = area; }
                    return true;
                }, IntPtr.Zero);
                if (best != IntPtr.Zero) Attach(best);
            }
            catch (Exception ex) { StartupDiagnostics.Write("Folia window lookup: " + ex.Message); }
        }

        private void Attach(IntPtr window)
        {
            if (window == IntPtr.Zero || !IsWindow(window)) return;
            if (!GetWindowRect(window, out OriginalBounds)) throw new InvalidOperationException("无法读取 Folia 窗口的原始位置。");
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
                SetWindowPos(window, IntPtr.Zero, OriginalBounds.Left, OriginalBounds.Top, OriginalBounds.Right - OriginalBounds.Left, OriginalBounds.Bottom - OriginalBounds.Top, SwpNoZOrder | SwpFrameChanged | SwpShowWindow);
                HasOriginalStyle = HasOriginalBounds = false;
                Placeholder.Text = "无法嵌入 Folia 窗口。\r\n\r\n请确认控制台和 Folia 以相同权限运行。";
                Placeholder.Visible = true;
                PollTimer.Stop();
                return;
            }
            EmbeddedWindow = window;
            Placeholder.Visible = false;
            ResizeEmbeddedWindow();
        }

        private void ResizeEmbeddedWindow()
        {
            if (EmbeddedWindow == IntPtr.Zero || !IsWindow(EmbeddedWindow) || Surface.IsDisposed || !Surface.IsHandleCreated) return;
            SetWindowPos(EmbeddedWindow, IntPtr.Zero, 0, 0, Math.Max(1, Surface.ClientSize.Width), Math.Max(1, Surface.ClientSize.Height), SwpNoZOrder | SwpFrameChanged | SwpShowWindow);
        }

        private void Detach()
        {
            IntPtr window = EmbeddedWindow;
            EmbeddedWindow = IntPtr.Zero;
            if (window == IntPtr.Zero || !IsWindow(window)) { HasOriginalStyle = HasOriginalBounds = false; return; }
            try
            {
                SetParent(window, IntPtr.Zero);
                if (HasOriginalStyle) SetWindowStyle(window, GwlStyle, OriginalStyle);
                if (HasOriginalBounds)
                    SetWindowPos(window, IntPtr.Zero, OriginalBounds.Left, OriginalBounds.Top, Math.Max(1, OriginalBounds.Right - OriginalBounds.Left), Math.Max(1, OriginalBounds.Bottom - OriginalBounds.Top), SwpNoZOrder | SwpFrameChanged | SwpShowWindow);
                ShowWindow(window, SwShownormal);
            }
            catch { }
            HasOriginalStyle = HasOriginalBounds = false;
        }

        public void Dispose()
        {
            PollTimer.Stop();
            PollTimer.Dispose();
            Detach();
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
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr window, StringBuilder text, int maxCount);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowTextLength(IntPtr window);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
        [DllImport("user32.dll")] private static extern IntPtr SetParent(IntPtr child, IntPtr parent);
        [DllImport("user32.dll")] private static extern IntPtr GetParent(IntPtr window);
        [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr window, IntPtr insertAfter, int x, int y, int width, int height, uint flags);
        [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr window, int command);
        [DllImport("user32.dll", EntryPoint = "GetWindowLong", SetLastError = true)] private static extern int GetWindowLong32(IntPtr window, int index);
        [DllImport("user32.dll", EntryPoint = "SetWindowLong", SetLastError = true)] private static extern int SetWindowLong32(IntPtr window, int index, int value);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr", SetLastError = true)] private static extern IntPtr GetWindowLongPtr64(IntPtr window, int index);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr", SetLastError = true)] private static extern IntPtr SetWindowLongPtr64(IntPtr window, int index, IntPtr value);
    }
}
