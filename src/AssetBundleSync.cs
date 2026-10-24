using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace NixLanConsole
{
    // GitHub distributes the application; the user's own host supplies local game assets.
    internal static class AssetBundleSync
    {
        internal const string ManifestSha256 = "D3015414F8082265709B3B7E1B19A4D089C94A9786AC69068967C9CD8F6B0F11";
        internal const string AssetTreeSha256 = "EF648BCB76990B33E41BE5ECF48B628E7FE82E6646DCB7FB17A08D436504369C";
        private static readonly object BundleGate = new object();
        internal static string CacheDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NixLanConsole");

        internal static bool HasCurrentAssets(string installRoot, string expectedTree = null)
        {
            string assets = Path.Combine(installRoot, "game-assets");
            return Directory.Exists(assets) && String.Equals(HashTree(assets), expectedTree ?? AssetTreeSha256, StringComparison.OrdinalIgnoreCase);
        }

        private static string HashTree(string root)
        {
            var text = new StringBuilder();
            foreach (string file in Directory.GetFiles(root, "*", SearchOption.AllDirectories)
                .OrderBy(x => x.Substring(root.Length).Replace('\\', '/'), StringComparer.Ordinal))
            {
                text.Append(file.Substring(root.Length).TrimStart(Path.DirectorySeparatorChar).Replace('\\', '/'));
                text.Append('\n').Append(HashFile(file)).Append('\n');
            }
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(text.ToString()))).Replace("-", "");
        }

        private static string HashFile(string file)
        {
            using (var input = File.OpenRead(file))
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(input)).Replace("-", "");
        }

        private static void SendText(Stream stream, int code, string reason, string text)
        {
            byte[] data = Encoding.UTF8.GetBytes(text);
            byte[] header = Encoding.ASCII.GetBytes("HTTP/1.1 " + code + " " + reason + "\r\nContent-Type: text/plain; charset=utf-8\r\nContent-Length: " + data.Length + "\r\nConnection: close\r\n\r\n");
            stream.Write(header, 0, header.Length);
            stream.Write(data, 0, data.Length);
        }

        internal static void Serve(TcpClient client, NetworkStream stream, StreamReader reader, string requestLine)
        {
            client.ReceiveTimeout = 15000;
            client.SendTimeout = 30000;
            try
            {
                string[] parts = requestLine.Split(' ');
                if (parts.Length < 3 || parts[0] != "GET") { SendText(stream, 400, "Bad Request", "Invalid request."); return; }
                int total = requestLine.Length;
                bool ended = false;
                for (int i = 0; i < 64; i++)
                {
                    string line = reader.ReadLine();
                    if (line == null) return;
                    total += line.Length;
                    if (total > 16384) { SendText(stream, 431, "Request Header Fields Too Large", "Headers too large."); return; }
                    if (line.Length == 0) { ended = true; break; }
                }
                if (!ended) { SendText(stream, 400, "Bad Request", "Incomplete headers."); return; }
                Uri uri = new Uri(new Uri("http://127.0.0.1/"), parts[1]);
                if (uri.AbsolutePath != "/asset-bundle") { SendText(stream, 404, "Not Found", "Unknown path."); return; }
                string key = null, expectedVersion = null;
                foreach (string part in uri.Query.TrimStart('?').Split('&'))
                {
                    int equals = part.IndexOf('=');
                    if (equals < 1) continue;
                    string name = part.Substring(0, equals);
                    string value = Uri.UnescapeDataString(part.Substring(equals + 1));
                    if (name == "key") key = value;
                    if (name == "version") expectedVersion = value;
                }
                if (String.IsNullOrEmpty(key) || !String.Equals(key, FoliaLanRelay.AccessKey, StringComparison.Ordinal))
                { SendText(stream, 403, "Forbidden", "Access denied."); return; }
                if (!String.Equals(expectedVersion, BuildInfo.Version, StringComparison.Ordinal))
                { SendText(stream, 409, "Conflict", "Host program version does not match update."); return; }
                if (!HasCurrentAssets(BuildInfo.InstallDir))
                { SendText(stream, 409, "Conflict", "Host assets are older than this release."); return; }
                string bundle = PrepareBundle(BuildInfo.InstallDir);
                string digest = HashFile(bundle);
                using (var file = File.OpenRead(bundle))
                {
                    byte[] header = Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Type: application/zip\r\nContent-Length: " + file.Length + "\r\nX-Asset-SHA256: " + digest + "\r\nCache-Control: no-store\r\nConnection: close\r\n\r\n");
                    stream.Write(header, 0, header.Length);
                    file.CopyTo(stream);
                }
            }
            catch (Exception ex) { StartupDiagnostics.Write("asset bundle relay: " + ex.Message); }
        }

        private static string PrepareBundle(string installRoot)
        {
            Directory.CreateDirectory(CacheDir);
            string cache = Path.Combine(CacheDir, "asset-bundle-" + BuildInfo.Version + "-" + ManifestSha256 + ".zip");
            lock (BundleGate)
            {
                if (File.Exists(cache)) return cache;
                string source = Path.Combine(installRoot, "game-assets");
                string temp = cache + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    using (var output = ZipFile.Open(temp, ZipArchiveMode.Create))
                    {
                        foreach (string file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
                        {
                            string relative = file.Substring(source.Length).TrimStart(Path.DirectorySeparatorChar).Replace('\\', '/');
                            output.CreateEntryFromFile(file, "game-assets/" + relative, CompressionLevel.Optimal);
                        }
                    }
                    File.Move(temp, cache);
                }
                finally { if (File.Exists(temp)) File.Delete(temp); }
                return cache;
            }
        }

        internal static string Download(string address, string key, string version, Action<int, string> progress, int port = 46702)
        {
            if (String.IsNullOrWhiteSpace(address) || String.IsNullOrWhiteSpace(key)) throw new IOException("主机未连接，无法同步角色素材。");
            Directory.CreateDirectory(CacheDir);
            string destination = Path.Combine(CacheDir, "asset-download-" + Guid.NewGuid().ToString("N") + ".zip");
            try
            {
                var url = new UriBuilder("http", address, port, "asset-bundle") { Query = "version=" + Uri.EscapeDataString(version) + "&key=" + Uri.EscapeDataString(key) }.Uri;
                var request = (HttpWebRequest)WebRequest.Create(url);
                request.Timeout = 15000;
                request.ReadWriteTimeout = 30000;
                request.KeepAlive = false;
                using (var response = (HttpWebResponse)request.GetResponse())
                using (var input = response.GetResponseStream())
                using (var output = File.Create(destination))
                {
                    if (response.ContentLength <= 0 || response.ContentLength > 200L * 1024 * 1024) throw new InvalidDataException("主机素材包大小无效。");
                    string expected = response.Headers["X-Asset-SHA256"];
                    if (String.IsNullOrEmpty(expected) || expected.Length != 64) throw new InvalidDataException("主机素材包缺少校验值。");
                    byte[] buffer = new byte[65536];
                    long received = 0;
                    int size;
                    using (var sha = SHA256.Create())
                    {
                        while ((size = input.Read(buffer, 0, buffer.Length)) > 0)
                        {
                            output.Write(buffer, 0, size);
                            sha.TransformBlock(buffer, 0, size, buffer, 0);
                            received += size;
                            if (received > response.ContentLength) throw new InvalidDataException("主机素材包超过声明大小。");
                            if (progress != null) progress((int)(received * 100 / response.ContentLength), "正在从主机同步角色素材… " + (received / 1048576d).ToString("F1") + " MB");
                        }
                        sha.TransformFinalBlock(new byte[0], 0, 0);
                        if (received != response.ContentLength || !String.Equals(BitConverter.ToString(sha.Hash).Replace("-", ""), expected, StringComparison.OrdinalIgnoreCase))
                            throw new InvalidDataException("主机素材包校验失败。");
                    }
                }
                return destination;
            }
            catch { if (File.Exists(destination)) File.Delete(destination); throw; }
        }

        internal static string Install(string archivePath, string installRoot)
        {
            string stage = StageInstall(archivePath, installRoot);
            string backup = Path.Combine(installRoot, "game-assets-backup-" + Guid.NewGuid().ToString("N"));
            string current = Path.Combine(installRoot, "game-assets");
            bool moved = false;
            try
            {
                if (Directory.Exists(current)) { Directory.Move(current, backup); moved = true; }
                Directory.Move(stage, current);
                return moved ? backup : null;
            }
            catch
            {
                if (moved && Directory.Exists(backup) && !Directory.Exists(current)) Directory.Move(backup, current);
                throw;
            }
            finally { if (Directory.Exists(stage)) Directory.Delete(stage, true); }
        }

        internal static string StageInstall(string archivePath, string installRoot, string expectedTree = null, string expectedManifest = null)
        {
            string stage = Path.Combine(installRoot, "game-assets-stage-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(stage);
            try
            {
                string prefix = Path.GetFullPath(stage).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                long total = 0;
                using (var archive = ZipFile.OpenRead(archivePath))
                {
                    foreach (var entry in archive.Entries)
                    {
                        string name = entry.FullName.Replace('\\', '/');
                        if (!name.StartsWith("game-assets/", StringComparison.Ordinal) || name.Contains("//")) throw new InvalidDataException("主机素材包目录无效。");
                        string relative = name.Substring("game-assets/".Length);
                        if (relative.Length == 0) continue;
                        if (relative.Split('/').Any(x => x == "." || x == ".." || x.Length == 0)) throw new InvalidDataException("主机素材包包含无效路径。");
                        string target = Path.GetFullPath(Path.Combine(stage, relative.Replace('/', Path.DirectorySeparatorChar)));
                        if (!target.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("主机素材包路径越界。");
                        if (name.EndsWith("/", StringComparison.Ordinal)) Directory.CreateDirectory(target);
                        else
                        {
                            total += entry.Length;
                            if (total > 300L * 1024 * 1024) throw new InvalidDataException("主机素材包解压后过大。");
                            Directory.CreateDirectory(Path.GetDirectoryName(target));
                            using (var input = entry.Open()) using (var output = File.Create(target)) input.CopyTo(output);
                        }
                    }
                }
                string manifest = Path.Combine(stage, "native-role", "角色动画.json");
                if (!File.Exists(manifest) || !String.Equals(HashFile(manifest), expectedManifest ?? ManifestSha256, StringComparison.OrdinalIgnoreCase) ||
                    !String.Equals(HashTree(stage), expectedTree ?? AssetTreeSha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("主机角色素材版本与程序不匹配。");
                return stage;
            }
            catch { if (Directory.Exists(stage)) Directory.Delete(stage, true); throw; }
        }

        internal static void Complete(string backup)
        {
            if (backup != null && Directory.Exists(backup)) try { Directory.Delete(backup, true); } catch { }
        }

        internal static void Rollback(string backup, string installRoot)
        {
            if (backup == null || !Directory.Exists(backup)) return;
            string current = Path.Combine(installRoot, "game-assets");
            if (Directory.Exists(current)) Directory.Delete(current, true);
            Directory.Move(backup, current);
        }
    }
}
