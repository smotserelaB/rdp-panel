using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace RdpPanel
{
    // a newer release exists on GitHub
    class UpdateInfo
    {
        public string Version;     // e.g. 1.0.1
        public string Url;         // page of the release
        public string Notes;       // release notes (markdown) from GitHub
        public string SetupName;   // RdpPanel-Setup-1.0.1.exe (empty: not uploaded yet)
        public string SetupUrl;    // direct download link of the installer
        public string SumsUrl;     // direct download link of SHA256SUMS.txt

        // the installer and its checksum file are online: the update can be downloaded and installed from the app
        public bool FilesReady { get { return !string.IsNullOrEmpty(SetupUrl) && !string.IsNullOrEmpty(SumsUrl); } }
    }

    enum CheckStatus
    {
        UpToDate,    // you have the newest version
        Available,   // a newer version exists
        Building,    // a newer version exists, but GitHub Actions is still building the files
        Error        // the check failed (offline, rate limit, ...)
    }

    class CheckResult
    {
        public CheckStatus Status;
        public UpdateInfo Info;      // set when Available or Building
        public string Message = "";  // reason when Error
    }

    // asks GitHub for the latest release of this app and compares it with AppInfo.Version
    static class UpdateChecker
    {
        // how long we wait for the installer to appear on a new release before we stop waiting for it
        // (e.g. a release that was made by hand, without the workflow)
        static readonly TimeSpan BuildGrace = TimeSpan.FromHours(2);

        // never throws
        public static async Task<CheckResult> CheckAsync()
        {
            try
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                var req = (HttpWebRequest)WebRequest.Create(AppInfo.LatestReleaseApi);
                req.Method = "GET";
                req.UserAgent = "RdpPanel/" + AppInfo.Version;   // GitHub requires a user agent
                req.Accept = "application/vnd.github+json";
                req.Timeout = 15000;
                req.ReadWriteTimeout = 15000;

                string body;
                using (var resp = (HttpWebResponse)await req.GetResponseAsync())
                using (var sr = new StreamReader(resp.GetResponseStream()))
                    body = await sr.ReadToEndAsync();

                var o = new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.Deserialize<Dictionary<string, object>>(body);
                object tagObj, urlObj, bodyObj, draft, pre, pubObj, assetsObj;
                if (!o.TryGetValue("tag_name", out tagObj)) return Fail("GitHub sent no release information.");
                if (o.TryGetValue("draft", out draft) && draft is bool && (bool)draft) return Up();
                if (o.TryGetValue("prerelease", out pre) && pre is bool && (bool)pre) return Up();
                o.TryGetValue("html_url", out urlObj);
                o.TryGetValue("body", out bodyObj);
                o.TryGetValue("published_at", out pubObj);
                o.TryGetValue("assets", out assetsObj);

                string tag = Convert.ToString(tagObj);
                Version latest, current;
                if (!TryParse(tag, out latest) || !TryParse(AppInfo.Version, out current)) return Fail("Could not read the version number (" + tag + ").");
                if (latest <= current) return Up();

                string url = Convert.ToString(urlObj);
                if (string.IsNullOrEmpty(url)) url = AppInfo.ReleasesUrl;
                var info = new UpdateInfo { Version = latest.ToString(), Url = url, Notes = Convert.ToString(bodyObj) };

                // the files of the release: GitHub Actions uploads them a bit after it creates the release
                var assets = assetsObj as IEnumerable;
                if (assets != null)
                {
                    foreach (var a in assets)
                    {
                        var d = a as IDictionary<string, object>;
                        if (d == null) continue;
                        object n, u;
                        if (!d.TryGetValue("name", out n) || !d.TryGetValue("browser_download_url", out u)) continue;
                        string name = Convert.ToString(n), link = Convert.ToString(u);
                        if (Regex.IsMatch(name, @"^RdpPanel-Setup-[0-9][0-9A-Za-z\.\-]*\.exe$")) { info.SetupName = name; info.SetupUrl = link; }
                        else if (string.Equals(name, "SHA256SUMS.txt", StringComparison.OrdinalIgnoreCase)) info.SumsUrl = link;
                    }
                }

                if (!info.FilesReady)
                {
                    DateTime published;
                    bool known = DateTime.TryParse(Convert.ToString(pubObj), CultureInfo.InvariantCulture,
                        DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out published);
                    // fresh release: the installer is probably still being built, so do not show the pop-up yet
                    if (known && DateTime.UtcNow - published < BuildGrace)
                        return new CheckResult { Status = CheckStatus.Building, Info = info };
                    // old release without installer: still tell the user, with a link to the page
                }
                return new CheckResult { Status = CheckStatus.Available, Info = info };
            }
            catch (WebException wex)
            {
                var r = wex.Response as HttpWebResponse;
                if (r != null && r.StatusCode == HttpStatusCode.NotFound) return Up();   // no release published yet
                if (r != null && (int)r.StatusCode == 403) return Fail("GitHub is limiting requests right now. Try again in a while.");
                return Fail(wex.Message);
            }
            catch (Exception ex) { return Fail(ex.Message); }   // offline, timeout, ...
        }

        static CheckResult Up() { return new CheckResult { Status = CheckStatus.UpToDate }; }
        static CheckResult Fail(string msg) { return new CheckResult { Status = CheckStatus.Error, Message = msg }; }

        static bool TryParse(string s, out Version v)
        {
            s = (s ?? "").Trim().TrimStart('v', 'V');
            int dash = s.IndexOfAny(new[] { '-', '+' });
            if (dash > 0) s = s.Substring(0, dash);
            return Version.TryParse(s, out v);
        }
    }

    class DownloadResult
    {
        public string Path;    // the verified installer (null on failure)
        public string Error;   // null if it worked
    }

    // downloads the installer, checks it against SHA256SUMS.txt and starts it
    static class UpdateInstaller
    {
        const long MaxSize = 250L * 1024 * 1024;

        // only an installed copy (made by RdpPanel-Setup-x.y.z.exe, it has an uninstaller next to the exe) can update itself;
        // a loose RdpPanel.exe from the zip is just a file somewhere and gets the download page instead
        public static bool CanSelfInstall
        {
            get
            {
                try { return File.Exists(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "unins000.exe")); }
                catch { return false; }
            }
        }

        static bool TrustedUrl(string u)
        {
            Uri uri;
            if (!Uri.TryCreate(u, UriKind.Absolute, out uri) || uri.Scheme != Uri.UriSchemeHttps) return false;
            string h = uri.Host.ToLowerInvariant();
            return h == "github.com" || h.EndsWith(".github.com") || h.EndsWith(".githubusercontent.com");
        }

        static HttpWebRequest Open(string url)
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.UserAgent = "RdpPanel/" + AppInfo.Version;
            req.Timeout = 30000;
            req.ReadWriteTimeout = 30000;
            req.AllowAutoRedirect = true;   // GitHub forwards downloads to its file server
            return req;
        }

        // progress: 0..100. Runs on the calling thread (the UI thread), so the callback may touch controls.
        public static async Task<DownloadResult> DownloadAsync(UpdateInfo info, Action<int> progress, CancellationToken ct)
        {
            string target = null;
            try
            {
                if (!info.FilesReady) return new DownloadResult { Error = "The installer is not on GitHub (yet)." };
                if (!TrustedUrl(info.SetupUrl) || !TrustedUrl(info.SumsUrl)) return new DownloadResult { Error = "The download link is not a GitHub link." };
                if (!Regex.IsMatch(info.SetupName ?? "", @"^RdpPanel-Setup-[0-9][0-9A-Za-z\.\-]*\.exe$")) return new DownloadResult { Error = "Unexpected file name." };

                string dir = Path.Combine(Path.GetTempPath(), "RdpPanel-update");
                Directory.CreateDirectory(dir);
                foreach (var old in Directory.GetFiles(dir)) { try { File.Delete(old); } catch { } }   // leftovers of earlier updates
                target = Path.Combine(dir, info.SetupName);

                // 1. the expected checksum
                string expected = await FetchExpectedHash(info, ct);
                if (ct.IsCancellationRequested) return new DownloadResult { Error = "Cancelled." };
                if (expected == null) return new DownloadResult { Error = "SHA256SUMS.txt has no checksum for " + info.SetupName + "." };

                // 2. the installer itself
                var req = Open(info.SetupUrl);
                using (ct.Register(() => { try { req.Abort(); } catch { } }))
                using (var resp = (HttpWebResponse)await req.GetResponseAsync())
                using (var inp = resp.GetResponseStream())
                using (var outp = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    long total = resp.ContentLength;
                    if (total > MaxSize) return new DownloadResult { Error = "The file is much bigger than expected." };
                    var buf = new byte[81920];
                    long got = 0;
                    int last = -1;
                    int n;
                    while ((n = await inp.ReadAsync(buf, 0, buf.Length, ct)) > 0)
                    {
                        outp.Write(buf, 0, n);
                        got += n;
                        if (got > MaxSize) return new DownloadResult { Error = "The file is much bigger than expected." };
                        if (total > 0 && progress != null)
                        {
                            int pct = (int)(got * 100 / total);
                            if (pct != last) { last = pct; progress(pct); }
                        }
                    }
                }
                if (ct.IsCancellationRequested) { TryDelete(target); return new DownloadResult { Error = "Cancelled." }; }

                // 3. check it: a broken or changed file is never started
                string actual;
                using (var sha = SHA256.Create())
                using (var fs = File.OpenRead(target))
                    actual = BitConverter.ToString(sha.ComputeHash(fs)).Replace("-", "").ToLowerInvariant();
                if (actual != expected)
                {
                    TryDelete(target);
                    return new DownloadResult { Error = "The downloaded file does not match its checksum, so it was not installed." };
                }
                return new DownloadResult { Path = target };
            }
            catch (Exception ex)
            {
                if (target != null) TryDelete(target);
                if (ct.IsCancellationRequested) return new DownloadResult { Error = "Cancelled." };
                return new DownloadResult { Error = ex.Message };
            }
        }

        // line format of SHA256SUMS.txt: "<64 hex>  <file name>"
        static async Task<string> FetchExpectedHash(UpdateInfo info, CancellationToken ct)
        {
            var req = Open(info.SumsUrl);
            using (ct.Register(() => { try { req.Abort(); } catch { } }))
            using (var resp = (HttpWebResponse)await req.GetResponseAsync())
            using (var sr = new StreamReader(resp.GetResponseStream()))
            {
                string text = await sr.ReadToEndAsync();
                foreach (var raw in text.Replace("\r", "").Split('\n'))
                {
                    var line = raw.Trim().TrimStart('﻿');
                    var m = Regex.Match(line, @"^([0-9a-fA-F]{64})\s+\*?(.+)$");
                    if (m.Success && string.Equals(m.Groups[2].Value.Trim(), info.SetupName, StringComparison.OrdinalIgnoreCase))
                        return m.Groups[1].Value.ToLowerInvariant();
                }
            }
            return null;
        }

        static void TryDelete(string p) { try { if (p != null && File.Exists(p)) File.Delete(p); } catch { } }

        // starts the installer silently; it replaces the exe and starts the panel again (STARTAFTER, see installer.iss).
        // The caller closes the panel right after this. Returns null if it started, otherwise the error.
        public static string Launch(string installerPath)
        {
            try
            {
                var psi = new ProcessStartInfo(installerPath,
                    "/SILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS /NORESTARTAPPLICATIONS /STARTAFTER=1")
                { UseShellExecute = true };
                Process.Start(psi);
                return null;
            }
            catch (Exception ex) { return ex.Message; }
        }
    }
}
