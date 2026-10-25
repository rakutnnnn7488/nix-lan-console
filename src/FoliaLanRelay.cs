using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace NixLanConsole
{
    // Carries the existing Folia OBS browser source over the console's TCP port.
    // Folia runs on the host; the display only renders its original animation.
    internal static class FoliaLanRelay
    {
        private sealed class FoliaConfig
        {
            public bool OBS_BROWSER_SOURCE_ENABLED = false;
            public string OBS_BROWSER_SOURCE_TOKEN = null;
            public int OBS_BROWSER_SOURCE_PORT = 0;
        }

        private sealed class FoliaClock { public string playerState = null; }
        private sealed class FoliaSong { public string name = null; }
        private sealed class FoliaMediaConfig { public FoliaSong song = null; public string songArtist = null; public string coverUrl = null; }
        private static readonly JavaScriptSerializer Json = new JavaScriptSerializer();
        private static readonly object KeyGate = new object();
        private static string AccessKeyValue;
        private static volatile bool ClockConnected;
        private static volatile bool ClockPlaying;
        private static volatile bool SourceProcessRunning;
        private static long LastClockTicks;
        private static DateTime LastMonitorErrorLog = DateTime.MinValue;
        private static int Started;
        private static readonly object MediaGate = new object();
        private static string SongValue = "";
        private static string ArtistValue = "";
        private static string CoverUrlValue = "";
        private static byte[] CoverBytes;
        private static string CoverType;

        internal static string Song { get { lock (MediaGate) return SongValue; } }
        internal static string Artist { get { lock (MediaGate) return ArtistValue; } }
        internal static bool HasCover { get { lock (MediaGate) return CoverBytes != null; } }

        internal static string AccessKey
        {
            get { lock (KeyGate) { if (AccessKeyValue == null) AccessKeyValue = LoadOrCreateKey(); return AccessKeyValue; } }
        }

        internal static bool Connected { get { return ClockConnected && DateTime.UtcNow.Ticks - Interlocked.Read(ref LastClockTicks) < TimeSpan.FromSeconds(10).Ticks; } }
        internal static bool Playing { get { return Connected && ClockPlaying; } }
        internal static bool SourceRunning { get { return SourceProcessRunning; } }

        internal static void Start()
        {
            if (Interlocked.Exchange(ref Started, 1) != 0) return;
            string ignored = AccessKey;
            var thread = new Thread(Monitor) { IsBackground = true, Name = "Folia playback monitor" };
            thread.Start();
        }

        private static string LoadOrCreateKey()
        {
            string path = Path.Combine(NixPaths.DataDir, "folia-relay-key.txt");
            try
            {
                if (File.Exists(path))
                {
                    string previous = File.ReadAllText(path, Encoding.UTF8).Trim();
                    if (System.Text.RegularExpressions.Regex.IsMatch(previous, "^[a-f0-9]{48}$")) return previous;
                }
            }
            catch { }
            var bytes = new byte[24];
            using (var random = RandomNumberGenerator.Create()) random.GetBytes(bytes);
            string key = BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
            try { Directory.CreateDirectory(NixPaths.DataDir); File.WriteAllText(path, key, Encoding.UTF8); }
            catch (Exception ex) { StartupDiagnostics.Write("Folia relay key persistence: " + ex.Message); }
            return key;
        }

        private static FoliaConfig ReadConfig()
        {
            try
            {
                string file = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Folia", "config.json");
                return Json.Deserialize<FoliaConfig>(File.ReadAllText(file, Encoding.UTF8));
            }
            catch { return null; }
        }

        private static int ObsPort(FoliaConfig config)
        {
            return config.OBS_BROWSER_SOURCE_PORT > 0 && config.OBS_BROWSER_SOURCE_PORT <= 65535 ? config.OBS_BROWSER_SOURCE_PORT : 32108;
        }

        private static bool FoliaIsRunning()
        {
            try
            {
                Process[] processes = Process.GetProcessesByName("Folia");
                bool running = processes.Length > 0;
                foreach (Process process in processes) process.Dispose();
                return running;
            }
            catch { return false; }
        }

        private static void Monitor()
        {
            while (true)
            {
                SourceProcessRunning = FoliaIsRunning();
                if (!SourceProcessRunning) ClearMedia();
                FoliaConfig config = ReadConfig();
                if (config == null || !config.OBS_BROWSER_SOURCE_ENABLED || String.IsNullOrWhiteSpace(config.OBS_BROWSER_SOURCE_TOKEN))
                {
                    ClockConnected = false; ClockPlaying = false;
                    ClearMedia();
                    Thread.Sleep(3000);
                    continue;
                }
                try
                {
                    string uri = "http://127.0.0.1:" + ObsPort(config) + "/obs/events?token=" + Uri.EscapeDataString(config.OBS_BROWSER_SOURCE_TOKEN);
                    var request = (HttpWebRequest)WebRequest.Create(uri);
                    request.Timeout = 5000;
                    request.ReadWriteTimeout = 15000;
                    request.KeepAlive = false;
                    using (var response = (HttpWebResponse)request.GetResponse())
                    using (var stream = response.GetResponseStream())
                    using (var reader = new StreamReader(stream, Encoding.UTF8))
                    {
                        string eventName = "";
                        string line;
                        while ((line = reader.ReadLine()) != null)
                        {
                            // Folia embeds artwork and stage settings in its config event. A real
                            // playing session can exceed 64 KiB before the first clock event.
                            if (line.Length > 1048576) throw new InvalidDataException("Folia event line too large.");
                            if (line.StartsWith("event:", StringComparison.Ordinal)) eventName = line.Substring(6).Trim();
                            else if (eventName == "config" && line.StartsWith("data:", StringComparison.Ordinal))
                            {
                                FoliaMediaConfig media = Json.Deserialize<FoliaMediaConfig>(line.Substring(5));
                                string song = media == null || media.song == null ? "" : media.song.name ?? "";
                                string artist = media == null ? "" : media.songArtist ?? "";
                                string cover = media == null ? "" : media.coverUrl ?? "";
                                lock (MediaGate)
                                {
                                    SongValue = song.Length > 180 ? song.Substring(0, 180) : song;
                                    ArtistValue = artist.Length > 180 ? artist.Substring(0, 180) : artist;
                                    if (!String.Equals(cover, CoverUrlValue, StringComparison.Ordinal))
                                    {
                                        CoverUrlValue = cover; CoverBytes = null; CoverType = null;
                                        string requested = cover;
                                        ThreadPool.QueueUserWorkItem(delegate { LoadCover(requested); });
                                    }
                                }
                            }
                            else if (eventName == "clock" && line.StartsWith("data:", StringComparison.Ordinal))
                            {
                                FoliaClock clock = Json.Deserialize<FoliaClock>(line.Substring(5));
                                ClockPlaying = clock != null && String.Equals(clock.playerState, "PLAYING", StringComparison.OrdinalIgnoreCase);
                                Interlocked.Exchange(ref LastClockTicks, DateTime.UtcNow.Ticks);
                                ClockConnected = true;
                            }
                            else if (line.Length == 0) eventName = "";
                            if ((DateTime.UtcNow.Ticks - Interlocked.Read(ref LastClockTicks)) > TimeSpan.FromSeconds(3).Ticks)
                                SourceProcessRunning = FoliaIsRunning();
                        }
                    }
                }
                catch (Exception ex)
                {
                    if (DateTime.UtcNow - LastMonitorErrorLog > TimeSpan.FromMinutes(1))
                    {
                        LastMonitorErrorLog = DateTime.UtcNow;
                        StartupDiagnostics.Write("Folia event monitor: " + ex.Message);
                    }
                }
                ClockConnected = false; ClockPlaying = false;
                SourceProcessRunning = FoliaIsRunning();
                Thread.Sleep(3000);
            }
        }

        private static void ClearMedia()
        {
            lock (MediaGate) { SongValue = ""; ArtistValue = ""; CoverUrlValue = ""; CoverBytes = null; CoverType = null; }
        }

        private static void LoadCover(string url)
        {
            byte[] bytes = null;
            string type = null;
            try
            {
                if (url.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase))
                {
                    int comma = url.IndexOf(',');
                    if (comma > 0 && url.Substring(0, comma).IndexOf(";base64", StringComparison.OrdinalIgnoreCase) >= 0)
                    { type = url.Substring(5, url.IndexOf(';') - 5); bytes = Convert.FromBase64String(url.Substring(comma + 1)); }
                }
                else
                {
                    Uri uri;
                    if (Uri.TryCreate(url, UriKind.Absolute, out uri) && (uri.Scheme == "http" || uri.Scheme == "https"))
                    {
                        var request = (HttpWebRequest)WebRequest.Create(uri); request.Timeout = 5000; request.ReadWriteTimeout = 5000;
                        using (var response = (HttpWebResponse)request.GetResponse())
                        using (var stream = response.GetResponseStream())
                        using (var memory = new MemoryStream())
                        {
                            type = response.ContentType;
                            var buffer = new byte[8192]; int read;
                            while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
                            { if (memory.Length + read > 2097152) throw new InvalidDataException("Folia cover exceeds 2 MB."); memory.Write(buffer, 0, read); }
                            bytes = memory.ToArray();
                        }
                    }
                }
                if (bytes == null || bytes.Length == 0 || bytes.Length > 2097152 || String.IsNullOrWhiteSpace(type) || !type.StartsWith("image/", StringComparison.OrdinalIgnoreCase)) return;
                lock (MediaGate) if (String.Equals(url, CoverUrlValue, StringComparison.Ordinal)) { CoverBytes = bytes; CoverType = type.Split(';')[0]; }
            }
            catch (Exception ex) { StartupDiagnostics.Write("Folia cover: " + ex.Message); }
        }

        internal static void ServeHttp(TcpClient client, NetworkStream stream, StreamReader reader, string requestLine)
        {
            client.ReceiveTimeout = 15000; client.SendTimeout = 15000;
            try
            {
                string[] parts = requestLine.Split(' ');
                if (parts.Length < 3 || parts[0] != "GET" || !parts[1].StartsWith("/", StringComparison.Ordinal) || parts[1].StartsWith("//", StringComparison.Ordinal))
                { SendText(stream, 400, "Bad Request", "Invalid request.", null); return; }
                var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                int total = requestLine.Length;
                for (int i = 0; i < 64; i++)
                {
                    string line = reader.ReadLine();
                    if (line == null) return;
                    total += line.Length;
                    if (total > 16384) { SendText(stream, 431, "Request Header Fields Too Large", "Headers too large.", null); return; }
                    if (line.Length == 0) break;
                    int colon = line.IndexOf(':');
                    if (colon > 0) headers[line.Substring(0, colon).Trim()] = line.Substring(colon + 1).Trim();
                }
                Uri uri = new Uri(new Uri("http://127.0.0.1/"), parts[1]);
                string queryKey = QueryValue(uri.Query, "key");
                bool keyInUrl = String.Equals(queryKey, AccessKey, StringComparison.Ordinal);
                string cookie = headers.ContainsKey("Cookie") ? headers["Cookie"] : "";
                bool cookieValid = (";" + cookie + ";").Contains(";foliaLan=" + AccessKey + ";") ||
                    (";" + cookie + ";").Contains("; foliaLan=" + AccessKey + ";");
                if (!keyInUrl && !cookieValid) { SendText(stream, 403, "Forbidden", "Folia display access denied.", null); return; }
                string setCookie = keyInUrl ? "Set-Cookie: foliaLan=" + AccessKey + "; HttpOnly; SameSite=Lax; Path=/\r\n" : null;
                if (uri.AbsolutePath == "/display" || uri.AbsolutePath == "/")
                {
                    WriteAscii(stream, "HTTP/1.1 302 Found\r\nLocation: /neon?key=" + AccessKey + "\r\n" + setCookie + "Content-Length: 0\r\nConnection: close\r\n\r\n");
                    return;
                }
                if (uri.AbsolutePath == "/neon")
                {
                    SendText(stream, 200, "OK", ReadEmbedded("NeonPage"), setCookie, "text/html; charset=utf-8");
                    return;
                }
                if (uri.AbsolutePath == "/neon/client.mjs")
                {
                    SendText(stream, 200, "OK", ReadEmbedded("NeonClient"), setCookie, "text/javascript; charset=utf-8");
                    return;
                }                if (uri.AbsolutePath == "/playback")
                {
                    SendText(stream, 200, "OK", Json.Serialize(new { connected = Connected, playing = Playing, sourceRunning = SourceRunning, song = Song, artist = Artist, cover = HasCover }), setCookie, "application/json; charset=utf-8");
                    return;
                }
                if (uri.AbsolutePath == "/media-cover")
                {
                    byte[] cover; string type;
                    lock (MediaGate) { cover = CoverBytes; type = CoverType; }
                    if (cover == null) { SendText(stream, 404, "Not Found", "Cover unavailable.", setCookie); return; }
                    WriteAscii(stream, "HTTP/1.1 200 OK\r\nContent-Type: " + type + "\r\nCache-Control: no-store\r\nContent-Length: " + cover.Length + "\r\nConnection: close\r\n\r\n");
                    stream.Write(cover, 0, cover.Length);
                    return;
                }
                if (uri.AbsolutePath == "/obs/health") { SendText(stream, 403, "Forbidden", "Forbidden.", null); return; }
                FoliaConfig config = ReadConfig();
                if (config == null || !config.OBS_BROWSER_SOURCE_ENABLED || String.IsNullOrWhiteSpace(config.OBS_BROWSER_SOURCE_TOKEN))
                { SendText(stream, 503, "Service Unavailable", "Folia OBS browser source is not enabled on the host.", setCookie); return; }

                string path = uri.AbsolutePath;
                var preserved = new List<string>();
                foreach (string pair in uri.Query.TrimStart('?').Split('&'))
                    if (pair.Length > 0 && !pair.StartsWith("key=", StringComparison.OrdinalIgnoreCase) && !pair.StartsWith("token=", StringComparison.OrdinalIgnoreCase)) preserved.Add(pair);
                if (path == "/obs" || path == "/obs/events") preserved.Add("token=" + Uri.EscapeDataString(config.OBS_BROWSER_SOURCE_TOKEN));
                string upstreamUrl = "http://127.0.0.1:" + ObsPort(config) + path + (preserved.Count == 0 ? "" : "?" + String.Join("&", preserved));
                var upstream = (HttpWebRequest)WebRequest.Create(upstreamUrl);
                upstream.Timeout = 8000;
                upstream.ReadWriteTimeout = 15000;
                upstream.KeepAlive = false;
                try
                {
                    using (var response = (HttpWebResponse)upstream.GetResponse())
                    using (var body = response.GetResponseStream())
                    {
                        string type = response.ContentType ?? "application/octet-stream";
                        WriteAscii(stream, "HTTP/1.1 200 OK\r\nContent-Type: " + type + "\r\nCache-Control: " + (path == "/obs/events" ? "no-store" : "public, max-age=60") + "\r\n" + setCookie + "Connection: close\r\n\r\n");
                        var buffer = new byte[16384];
                        int read;
                        while ((read = body.Read(buffer, 0, buffer.Length)) > 0) stream.Write(buffer, 0, read);
                    }
                }
                catch (WebException ex)
                {
                    if (!stream.CanWrite) return;
                    var response = ex.Response as HttpWebResponse;
                    if (response != null)
                    {
                        using (response) SendText(stream, (int)response.StatusCode, response.StatusDescription, "Folia source returned " + (int)response.StatusCode + ".", setCookie);
                    }
                    else SendText(stream, 502, "Bad Gateway", "Folia is not serving its browser source yet.", setCookie);
                }
            }
            catch (Exception ex) { StartupDiagnostics.Write("Folia HTTP relay: " + ex.Message); }
        }

        private static string QueryValue(string query, string name)
        {
            foreach (string pair in query.TrimStart('?').Split('&'))
            {
                int equals = pair.IndexOf('=');
                if (equals > 0 && String.Equals(pair.Substring(0, equals), name, StringComparison.OrdinalIgnoreCase))
                    return Uri.UnescapeDataString(pair.Substring(equals + 1));
            }
            return null;
        }

        private static string ReadEmbedded(string name)
        {
            using (var body = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream(name))
            {
                if (body == null) throw new FileNotFoundException("Missing embedded Folia visualizer resource: " + name);
                using (var reader = new StreamReader(body, Encoding.UTF8)) return reader.ReadToEnd();
            }
        }
        private static void WriteAscii(Stream stream, string text)
        {
            byte[] bytes = Encoding.ASCII.GetBytes(text);
            stream.Write(bytes, 0, bytes.Length);
        }

        private static void SendText(Stream stream, int code, string status, string body, string extraHeader, string contentType = "text/plain; charset=utf-8")
        {
            byte[] bytes = Encoding.UTF8.GetBytes(body);
            WriteAscii(stream, "HTTP/1.1 " + code + " " + status + "\r\nContent-Type: " + contentType + "\r\nCache-Control: no-store\r\n" + extraHeader + "Content-Length: " + bytes.Length + "\r\nConnection: close\r\n\r\n");
            stream.Write(bytes, 0, bytes.Length);
        }
    }
}


