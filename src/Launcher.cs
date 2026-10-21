using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

namespace NixLanConsoleLauncher
{
    // Stable entry point. It never replaces itself; it only chooses a version,
    // starts it, verifies startup, and rolls back to previous.txt if needed.
    internal static class Program
    {
        private static readonly string DataDir = ResolveDataDir();
        private static readonly string ReadyFile = Path.Combine(DataDir, "startup-ready.txt");
        private static readonly string LogFile = Path.Combine(DataDir, "update.log");

        private static string ResolveDataDir()
        {
            string custom = Environment.GetEnvironmentVariable("NIXLAN_DATA_DIR");
            if (!String.IsNullOrWhiteSpace(custom)) return Path.GetFullPath(custom);
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NixLanConsole");
        }

        [STAThread]
        private static int Main(string[] args)
        {
            try
            {
                bool created;
                using (var mutex = new Mutex(true, "Local\\NixLanConsole.Launcher", out created))
                {
                    if (!created) return 0;
                    return Run(args);
                }
            }
            catch (Exception ex)
            {
                Log("launcher fatal: " + ex);
                MessageBox.Show("启动器发生错误，详情见 update.log。\r\n" + ex.Message, "Nix LAN Console", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }
        }

        private static int Run(string[] args)
        {
            string root = AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            int waitPid = 0;
            var passThrough = new List<string>();
            for (int index = 0; index < args.Length; index++)
            {
                if (args[index] == "--wait-pid" && index + 1 < args.Length)
                {
                    Int32.TryParse(args[++index], out waitPid);
                    continue;
                }
                passThrough.Add(args[index]);
            }
            string role = passThrough.Count > 0 ? passThrough[0] : "";
            if (waitPid > 0 && !WaitForPid(waitPid, 6000))
            {
                Log("launcher: wait-pid timeout, terminating " + waitPid);
                try
                {
                    using (var old = Process.GetProcessById(waitPid))
                    {
                        old.Kill();
                        if (!old.WaitForExit(10000))
                        {
                            Log("launcher: old process did not exit after termination pid=" + waitPid);
                            return 4;
                        }
                    }
                }
                catch (ArgumentException) { }
                catch (Exception ex) { Log("launcher: old process termination failed: " + ex.Message); return 4; }
            }
            if (ExistingAppRunning(root, role))
            {
                Log("launcher: an existing instance is already running; nothing to do");
                return 0;
            }
            string current = ReadVersion(Path.Combine(root, "current.txt"));
            if (current == null) current = ReadVersion(Path.Combine(root, "version.txt"));
            string appPath = AppPath(root, current);
            if (appPath == null)
            {
                Log("launcher: no valid current version below " + root);
                MessageBox.Show("安装目录中找不到可启动的程序版本。请使用完整安装包修复。", "Nix LAN Console", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 2;
            }
            if (StartAndVerify(root, appPath, current, role, passThrough)) return 0;
            WriteFailed(current);

            string previous = ReadVersion(Path.Combine(root, "previous.txt"));
            string previousPath = previous == null ? null : AppPath(root, previous);
            if (previous != null && previousPath != null && !String.Equals(previous, current, StringComparison.OrdinalIgnoreCase))
            {
                Log("launcher: rollback " + current + " -> " + previous);
                WriteTextAtomic(Path.Combine(root, "current.txt"), previous);
                if (StartAndVerify(root, previousPath, previous, role, passThrough))
                {
                    DeleteVersionDir(root, current);
                    return 0;
                }
            }
            Log("launcher: rollback failed; no usable version");
            MessageBox.Show("新版本没有成功启动，旧版本也无法启动。\r\n请使用完整安装包修复，详情见 update.log。", "Nix LAN Console", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 3;
        }

        private static bool StartAndVerify(string root, string appPath, string version, string role, List<string> args)
        {
            bool needsReady = role == "--sensor" || role == "--display";
            try { if (File.Exists(ReadyFile)) File.Delete(ReadyFile); } catch { }
            string commandLine = String.Join(" ", args.ToArray());
            Log("launcher: start " + version + " " + commandLine + " at " + appPath);
            Process process;
            try
            {
                process = Process.Start(new ProcessStartInfo(appPath, commandLine)
                {
                    UseShellExecute = true,
                    WorkingDirectory = Path.GetDirectoryName(appPath)
                });
            }
            catch (Exception ex)
            {
                Log("launcher: start failed: " + ex);
                return false;
            }
            if (process == null)
            {
                Log("launcher: Process.Start returned null");
                return false;
            }
            using (process)
            {
                if (!needsReady) return true;
                DateTime deadline = DateTime.UtcNow.AddSeconds(30);
                while (DateTime.UtcNow < deadline)
                {
                    process.Refresh();
                    if (process.HasExited)
                    {
                        if (process.ExitCode == 3)
                        {
                            if (ExistingAppRunning(root, role))
                            {
                                Log("launcher: app reports an existing instance under this root; treating as ready");
                                return true;
                            }
                            Log("launcher: app refused to start because another installation holds the role mutex");
                            return false;
                        }
                        Log("launcher: app exited before ready, code=" + process.ExitCode);
                        return false;
                    }
                    if (File.Exists(ReadyFile))
                    {
                        string[] parts = File.ReadAllText(ReadyFile, Encoding.UTF8).Trim().TrimStart('\uFEFF').Split('|');
                        if (parts.Length >= 3 && parts[0] == process.Id.ToString() && parts[1] == version && parts[2] == role)
                        {
                            Thread.Sleep(2000);
                            process.Refresh();
                            if (process.HasExited)
                            {
                                Log("launcher: app exited shortly after ready, code=" + process.ExitCode);
                                return false;
                            }
                            Log("launcher: ready " + version + " pid=" + process.Id);
                            return true;
                        }
                    }
                    Thread.Sleep(250);
                }
                Log("launcher: ready timeout for " + version);
                try { if (!process.HasExited) process.Kill(); } catch { }
                return false;
            }
        }

        private static string AppPath(string root, string version)
        {
            if (String.IsNullOrEmpty(version)) return null;
            string path = Path.Combine(root, "versions", version, "ControlConsole.exe");
            string marker = Path.Combine(root, "versions", version, ".complete");
            if (!File.Exists(path) || !File.Exists(marker)) return null;
            if (String.Equals(Path.GetFullPath(path), Path.GetFullPath(Path.Combine(root, "ControlConsole.exe")), StringComparison.OrdinalIgnoreCase)) return null;
            return path;
        }

        private static bool ExistingAppRunning(string root, string role)
        {
            // A process left in versions/ can be exiting, hung, or belong to the other
            // role. Only the instance which reported readiness for this role counts.
            string[] ready;
            try { ready = File.ReadAllText(ReadyFile, Encoding.UTF8).Trim().TrimStart('\uFEFF').Split('|'); }
            catch { return false; }
            if (ready.Length < 3 || !String.Equals(ready[2], role, StringComparison.Ordinal)) return false;
            int readyPid;
            if (!Int32.TryParse(ready[0], out readyPid)) return false;
            string prefix = Path.GetFullPath(Path.Combine(root, "versions")).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            try
            {
                using (Process process = Process.GetProcessById(readyPid))
                {
                    if (process.HasExited) return false;
                    string path = Path.GetFullPath(process.MainModule.FileName);
                    return path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
                }
            }
            catch { return false; }
        }

        private static string ReadVersion(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;
                string text = File.ReadAllText(path, Encoding.UTF8).Trim().TrimStart('\uFEFF');
                return Regex.IsMatch(text, @"\A\d+\.\d+\.\d+\.\d+\z") ? text : null;
            }
            catch { return null; }
        }

        private static void WriteTextAtomic(string path, string text)
        {
            string pending = path + ".pending";
            File.WriteAllText(pending, text, Encoding.UTF8);
            if (File.Exists(path)) File.Replace(pending, path, null, true);
            else File.Move(pending, path);
        }

        private static void WriteFailed(string version)
        {
            try
            {
                Directory.CreateDirectory(DataDir);
                File.WriteAllText(Path.Combine(DataDir, "failed-update.txt"), version, Encoding.UTF8);
            }
            catch (Exception ex) { Log("launcher: failed marker: " + ex.Message); }
        }

        private static void DeleteVersionDir(string root, string version)
        {
            try
            {
                string dir = Path.Combine(root, "versions", version);
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
            catch (Exception ex) { Log("launcher: cleanup failed: " + ex.Message); }
        }

        private static bool WaitForPid(int pid, int milliseconds)
        {
            try
            {
                using (var process = Process.GetProcessById(pid))
                    return process.WaitForExit(milliseconds);
            }
            catch (ArgumentException) { return true; }
            catch (Exception ex) { Log("launcher: wait-pid error: " + ex.Message); return true; }
        }

        private static void Log(string message)
        {
            try
            {
                Directory.CreateDirectory(DataDir);
                File.AppendAllText(LogFile, DateTime.Now.ToString("s") + " " + message + Environment.NewLine, Encoding.UTF8);
            }
            catch { }
        }
    }
}
