using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace RdpPanel
{
    // result of a successful check: a newer release exists on GitHub
    class UpdateInfo
    {
        public string Version;   // e.g. 1.0.1
        public string Url;       // page of the release
        public string Notes;     // release notes (markdown) from GitHub
    }

    // asks GitHub for the latest release of this app and compares it with AppInfo.Version
    static class UpdateChecker
    {
        // returns the newer release, or null if you are up to date or the check failed (never throws)
        public static async Task<UpdateInfo> CheckAsync()
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

                var o = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(body);
                object tagObj, urlObj, bodyObj, draft, pre;
                if (!o.TryGetValue("tag_name", out tagObj)) return null;
                if (o.TryGetValue("draft", out draft) && draft is bool && (bool)draft) return null;
                if (o.TryGetValue("prerelease", out pre) && pre is bool && (bool)pre) return null;
                o.TryGetValue("html_url", out urlObj);
                o.TryGetValue("body", out bodyObj);

                string tag = Convert.ToString(tagObj);
                Version latest, current;
                if (!TryParse(tag, out latest) || !TryParse(AppInfo.Version, out current)) return null;
                if (latest <= current) return null;

                string url = Convert.ToString(urlObj);
                if (string.IsNullOrEmpty(url)) url = AppInfo.ReleasesUrl;
                return new UpdateInfo { Version = latest.ToString(), Url = url, Notes = Convert.ToString(bodyObj) };
            }
            catch { return null; }   // offline, rate limit, no release yet: simply no banner
        }

        static bool TryParse(string s, out Version v)
        {
            s = (s ?? "").Trim().TrimStart('v', 'V');
            int dash = s.IndexOfAny(new[] { '-', '+' });
            if (dash > 0) s = s.Substring(0, dash);
            return Version.TryParse(s, out v);
        }
    }
}
