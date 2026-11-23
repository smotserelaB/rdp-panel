using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Management;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;
using System.Threading.Tasks;

namespace RdpPanel
{
    // ---- log (CSV with ; ) ----
    static class LogService
    {
        static readonly object Gate = new object();

        static string Dir
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RdpPanel"); }
        }

        public static string FilePath
        {
            get { return Path.Combine(Dir, "log.csv"); }
        }

        public static string ShotDir
        {
            get { return Path.Combine(Dir, "screenshots"); }
        }

        public class Entry
        {
            public DateTime Time;
            public string Session = "";
            public string Event = "";
            public string Detail = "";
            public int Seconds;
        }

        static string Clean(string s)
        {
            return (s ?? "").Replace(";", ",").Replace("\r", " ").Replace("\n", " ");
        }

        public static void Write(string session, string evt, string detail, int seconds)
        {
            try
            {
                lock (Gate)
                {
                    Directory.CreateDirectory(Dir);
                    var line = string.Join(";", new[]
                    {
                        DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                        Clean(session), Clean(evt), Clean(detail),
                        seconds.ToString(CultureInfo.InvariantCulture)
                    });
                    File.AppendAllText(FilePath, line + Environment.NewLine, Encoding.UTF8);
                }
            }
            catch { }
        }

        public static List<Entry> ReadAll()
        {
            var list = new List<Entry>();
            try
            {
                if (!File.Exists(FilePath)) return list;
                foreach (var line in File.ReadAllLines(FilePath, Encoding.UTF8))
                {
                    var p = line.Split(';');
                    if (p.Length < 5) continue;
                    DateTime t;
                    int sec;
                    if (!DateTime.TryParseExact(p[0], "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out t)) continue;
                    int.TryParse(p[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out sec);
                    list.Add(new Entry { Time = t, Session = p[1], Event = p[2], Detail = p[3], Seconds = sec });
                }
            }
            catch { }
            return list;
        }
    }

    // ---- error codes in plain language ----
    static class DiscReasons
    {
        public const string ByUser = "Disconnected by you";

        public static string Describe(int code)
        {
            switch (code)
            {
                case 0: return "No reason known";
                case 1: return ByUser;
                case 2: return "Logged off or disconnected on the other PC";
                case 3: return "Disconnected by the PC (e.g. because the same user logged in elsewhere)";
                case 260: return "PC name not found (DNS)";
                case 264: return "Timeout: the PC is not responding";
                case 516: return "Could not connect (RDP not reachable or port closed)";
                case 520: return "PC not found";
                case 772: return "Network error while sending";
                case 2308: return "Connection lost (network or the other PC went away)";
                default: return "Unknown reason (code " + code + ")";
            }
        }
    }

    // ---- network and PC management ----
    public static class NetTools
    {
        public class Found
        {
            public string Ip = "";
            public string Name = "";
        }

        static HashSet<string> localAddrs;
        static DateTime localAddrsAt = DateTime.MinValue;

        // all IP addresses of this PC (refreshed every 30 s)
        static HashSet<string> LocalAddresses()
        {
            if (localAddrs == null || (DateTime.Now - localAddrsAt).TotalSeconds > 30)
            {
                var set = new HashSet<string>();
                try
                {
                    foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                        foreach (var ua in ni.GetIPProperties().UnicastAddresses)
                            set.Add(ua.Address.ToString());
                }
                catch { }
                localAddrs = set;
                localAddrsAt = DateTime.Now;
            }
            return localAddrs;
        }

        // true if the host is this PC itself: localhost, 127.x.x.x, the PC name or an own IP address
        public static bool IsLocal(string host)
        {
            if (string.IsNullOrWhiteSpace(host)) return false;
            host = host.Trim();
            if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
                || host.Equals(Environment.MachineName, StringComparison.OrdinalIgnoreCase)) return true;
            IPAddress ip;
            if (IPAddress.TryParse(host, out ip))
            {
                if (IPAddress.IsLoopback(ip)) return true;
                return LocalAddresses().Contains(ip.ToString());
            }
            return false;
        }

        // ---- CPU/RAM of this PC itself (no WMI needed) ----
        [DllImport("kernel32.dll")]
        static extern bool GetSystemTimes(out long idle, out long kernel, out long user);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        class MEMORYSTATUSEX
        {
            public uint dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        static extern bool GlobalMemoryStatusEx([In, Out] MEMORYSTATUSEX buffer);

        static readonly object CpuGate = new object();
        static long prevIdle, prevTotal;
        static bool havePrev;
        static double[] lastLocal;
        static DateTime lastLocalAt = DateTime.MinValue;

        // [cpu%, ram%, free MB] of this PC, or null
        public static double[] LocalStats()
        {
            try
            {
                lock (CpuGate)
                {
                    // several sessions on the same PC ask at once: reuse a fresh measurement
                    if (lastLocal != null && (DateTime.Now - lastLocalAt).TotalSeconds < 3) return lastLocal;

                    long idle, kernel, user;
                    if (!GetSystemTimes(out idle, out kernel, out user)) return null;
                    long total = kernel + user;   // kernel time includes the idle time
                    double cpu = 0;
                    if (havePrev && total > prevTotal)
                        cpu = 100.0 * (1.0 - (double)(idle - prevIdle) / (total - prevTotal));
                    prevIdle = idle;
                    prevTotal = total;
                    havePrev = true;

                    var m = new MEMORYSTATUSEX();
                    if (!GlobalMemoryStatusEx(m)) return null;

                    lastLocal = new[] { Math.Max(0, Math.Min(100, cpu)), (double)m.dwMemoryLoad, m.ullAvailPhys / 1048576.0 };
                    lastLocalAt = DateTime.Now;
                    return lastLocal;
                }
            }
            catch { return null; }
        }

        public static async Task<bool> PortOpenAsync(string host, int port, int timeoutMs)
        {
            try
            {
                using (var c = new TcpClient())
                {
                    var t = c.ConnectAsync(host, port);
                    var done = await Task.WhenAny(t, Task.Delay(timeoutMs));
                    return done == t && c.Connected;
                }
            }
            catch { return false; }
        }

        [DllImport("iphlpapi.dll", ExactSpelling = true)]
        static extern int SendARP(int destIp, int srcIp, byte[] mac, ref int len);

        public static string GetMac(string host)
        {
            try
            {
                IPAddress ip;
                if (!IPAddress.TryParse(host, out ip))
                    ip = Dns.GetHostAddresses(host).FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork);
                if (ip == null) return "";
                var mac = new byte[6];
                int len = 6;
                if (SendARP(BitConverter.ToInt32(ip.GetAddressBytes(), 0), 0, mac, ref len) != 0 || len < 6) return "";
                return string.Join(":", mac.Take(6).Select(b => b.ToString("X2")));
            }
            catch { return ""; }
        }

        static byte[] ParseMac(string mac)
        {
            var hex = new string((mac ?? "").Where(Uri.IsHexDigit).ToArray());
            if (hex.Length != 12) return null;
            var b = new byte[6];
            for (int i = 0; i < 6; i++) b[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
            return b;
        }

        public static void Wake(string mac)
        {
            var bytes = ParseMac(mac);
            if (bytes == null) throw new ArgumentException("Invalid MAC address");
            var packet = new byte[6 + 16 * 6];
            for (int i = 0; i < 6; i++) packet[i] = 0xFF;
            for (int i = 0; i < 16; i++) Array.Copy(bytes, 0, packet, 6 + i * 6, 6);
            using (var udp = new UdpClient())
            {
                udp.EnableBroadcast = true;
                udp.Send(packet, packet.Length, new IPEndPoint(IPAddress.Broadcast, 9));
            }
        }

        // returns null if it worked, otherwise the error message
        public static string PowerAction(string host, string user, string pass, bool reboot)
        {
            try
            {
                var opts = new ConnectionOptions { EnablePrivileges = true, Timeout = TimeSpan.FromSeconds(8) };
                string path;
                if (IsLocal(host))
                {
                    path = @"\\.\root\cimv2";
                }
                else
                {
                    path = @"\\" + host + @"\root\cimv2";
                    opts.Username = (user.Contains("\\") || user.Contains("@")) ? user : host + "\\" + user;
                    opts.Password = pass;
                }
                var scope = new ManagementScope(path, opts);
                scope.Connect();
                using (var s = new ManagementObjectSearcher(scope, new ObjectQuery("SELECT * FROM Win32_OperatingSystem")))
                {
                    foreach (ManagementObject os in s.Get())
                    {
                        var args = os.GetMethodParameters("Win32Shutdown");
                        args["Flags"] = reboot ? 6 : 12;   // 6 = forced restart, 12 = forced power off
                        args["Reserved"] = 0;
                        os.InvokeMethod("Win32Shutdown", args, null);
                    }
                }
                return null;
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
        }

        static long IpKey(string ip)
        {
            var b = IPAddress.Parse(ip).GetAddressBytes();
            return ((long)b[0] << 24) | ((long)b[1] << 16) | ((long)b[2] << 8) | b[3];
        }

        // searches every /24 network of this PC for open RDP ports (3389)
        public static async Task<List<Found>> ScanAsync(IProgress<int> progress)
        {
            var result = new List<Found>();
            var prefixes = new List<string>();
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up || ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                foreach (var ua in ni.GetIPProperties().UnicastAddresses)
                {
                    if (ua.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                    var b = ua.Address.GetAddressBytes();
                    if (b[0] == 169) continue;
                    string prefix = b[0] + "." + b[1] + "." + b[2] + ".";
                    if (!prefixes.Contains(prefix)) prefixes.Add(prefix);
                }
            }

            int done = 0;
            int total = Math.Max(1, prefixes.Count * 254);
            foreach (var prefix in prefixes)
            {
                string pf = prefix;
                var tasks = Enumerable.Range(1, 254).Select(async i =>
                {
                    string ip = pf + i;
                    bool open = await PortOpenAsync(ip, 3389, 600);
                    int d = System.Threading.Interlocked.Increment(ref done);
                    if (progress != null) progress.Report(100 * d / total);
                    if (open) { lock (result) result.Add(new Found { Ip = ip }); }
                }).ToArray();
                await Task.WhenAll(tasks);
            }

            await Task.WhenAll(result.Select(async f =>
            {
                try { f.Name = (await Dns.GetHostEntryAsync(f.Ip)).HostName; }
                catch { f.Name = ""; }
            }));

            return result.OrderBy(f => IpKey(f.Ip)).ToList();
        }
    }

    // ---- starting with Windows ----
    static class StartupHelper
    {
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string ValueName = "RdpPanel";

        public static bool IsEnabled()
        {
            try
            {
                using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKey))
                    return k != null && k.GetValue(ValueName) != null;
            }
            catch { return false; }
        }

        public static bool Set(bool on)
        {
            try
            {
                using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKey, true))
                {
                    if (k == null) return false;
                    if (on)
                    {
                        string exe = System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName;
                        k.SetValue(ValueName, "\"" + exe + "\" --autostart");
                    }
                    else
                    {
                        k.DeleteValue(ValueName, false);
                    }
                }
                return true;
            }
            catch { return false; }
        }
    }

    // ---- Discord webhook: sending text and images ----
    static class Discord
    {
        static readonly Regex UrlOk = new Regex(
            @"^https://(?:(?:ptb|canary)\.)?discord(?:app)?\.com/api/webhooks/\d+/[\w\-]+/?(?:\?.*)?$",
            RegexOptions.IgnoreCase);

        static readonly SemaphoreSlim Gate = new SemaphoreSlim(1, 1);

        public static bool IsValid(string url)
        {
            return !string.IsNullOrWhiteSpace(url) && UrlOk.IsMatch(url.Trim());
        }

        public static byte[] ToJpeg(Bitmap bmp, long quality)
        {
            using (var ms = new MemoryStream())
            {
                var codec = ImageCodecInfo.GetImageEncoders().First(c => c.MimeType == "image/jpeg");
                using (var ps = new EncoderParameters(1))
                {
                    ps.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, quality);
                    bmp.Save(ms, codec, ps);
                }
                return ms.ToArray();
            }
        }

        static string SafeName(string n)
        {
            return new string((n ?? "image").Select(c => char.IsLetterOrDigit(c) || c == '.' || c == '_' || c == '-' ? c : '_').ToArray());
        }

        // returns null if it worked, otherwise the error message
        public static async Task<string> SendAsync(string url, string content, List<KeyValuePair<string, byte[]>> files)
        {
            if (!IsValid(url)) return "invalid webhook link";
            await Gate.WaitAsync();
            try
            {
                string boundary = "----RdpPanel" + Guid.NewGuid().ToString("N");
                var req = (HttpWebRequest)WebRequest.Create(url.Trim());
                req.Method = "POST";
                req.ContentType = "multipart/form-data; boundary=" + boundary;
                req.UserAgent = "RdpPanel/" + AppInfo.Version;
                req.Timeout = 30000;
                req.ReadWriteTimeout = 30000;

                using (var ms = new MemoryStream())
                {
                    var enc = new UTF8Encoding(false);
                    Action<string> w = t => { var b = enc.GetBytes(t); ms.Write(b, 0, b.Length); };

                    string text = content ?? "";
                    if (text.Length > 1900) text = text.Substring(0, 1900);
                    string json = new JavaScriptSerializer().Serialize(
                        new Dictionary<string, object> { { "username", "RDP Panel" }, { "content", text } });

                    w("--" + boundary + "\r\nContent-Disposition: form-data; name=\"payload_json\"\r\nContent-Type: application/json\r\n\r\n" + json + "\r\n");
                    int i = 0;
                    if (files != null)
                        foreach (var f in files.Take(10))
                        {
                            w("--" + boundary + "\r\nContent-Disposition: form-data; name=\"files[" + i + "]\"; filename=\"" + SafeName(f.Key) + "\"\r\nContent-Type: image/jpeg\r\n\r\n");
                            ms.Write(f.Value, 0, f.Value.Length);
                            w("\r\n");
                            i++;
                        }
                    w("--" + boundary + "--\r\n");

                    req.ContentLength = ms.Length;
                    using (var rs = await req.GetRequestStreamAsync())
                    {
                        ms.Position = 0;
                        await ms.CopyToAsync(rs);
                    }
                }

                using (var resp = (HttpWebResponse)await req.GetResponseAsync())
                    return null;
            }
            catch (WebException ex)
            {
                string body = "";
                try
                {
                    if (ex.Response != null)
                        using (var sr = new StreamReader(ex.Response.GetResponseStream())) body = sr.ReadToEnd();
                }
                catch { }
                if (body.Length > 200) body = body.Substring(0, 200);
                return ex.Message + (body.Length > 0 ? " – " + body : "");
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
            finally
            {
                Gate.Release();
            }
        }

        // send in the background; an error goes to the log
        public static async void Post(string url, string session, string content, List<KeyValuePair<string, byte[]>> files)
        {
            string err = await SendAsync(url, content, files);
            if (err != null) LogService.Write(session, "Discord", "sending failed: " + err, 0);
        }
    }
}
