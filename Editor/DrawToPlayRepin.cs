using System;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;

namespace PowerOfFire.DrawToPlay.Editor
{
    /// <summary>
    /// ONE CLICK for "take the plugin's current tag": UPM pins a git package to the commit
    /// the tag pointed at when it was first resolved (packages-lock.json), and re-pointing
    /// the tag upstream moves nothing here — by design. Re-ADDING the same git URL is the
    /// official way to re-fetch it, and it rewrites the lock itself: no file surgery, and a
    /// designer needs no terminal.
    /// </summary>
    internal static class DrawToPlayRepin
    {
        internal const string PackageName = "com.powerofire.drawtoplay";

        private static AddRequest s_Request;

        [MenuItem("Tools/Draw To Play/Update Draw To Play (re-fetch the tag)")]
        private static void Update()
        {
            UpdateNow();
        }

        internal static void UpdateNow()
        {
            if (s_Request != null)
            {
                Debug.Log("[Draw To Play] an update is already running.");
                return;
            }
            if (!TryGetGitSource(out string source, loud: true))
                return;

            var installed = UnityEditor.PackageManager.PackageInfo.FindForPackageName(PackageName);
            string was = installed != null && installed.git != null
                ? installed.version + " (" + Short(installed.git.hash) + ")"
                : "unknown";
            Debug.Log("[Draw To Play] was at " + was + " — re-fetching " + source + " …");

            s_Request = Client.Add(source);
            EditorApplication.update += Poll;
        }

        /// <summary>The manifest's own git source for this package — the tag choice stays the
        /// project's. False (and, when loud, a console line) for an embedded, local or
        /// registry install: there is no tag to re-fetch.</summary>
        internal static bool TryGetGitSource(out string source, bool loud = false)
        {
            source = null;
            string manifestPath = Path.Combine("Packages", "manifest.json");
            string manifest = File.Exists(manifestPath) ? File.ReadAllText(manifestPath) : "";
            Match entry = Regex.Match(manifest,
                "\"" + Regex.Escape(PackageName) + "\"\\s*:\\s*\"([^\"]+)\"");
            if (!entry.Success)
            {
                if (loud)
                    Debug.LogError("[Draw To Play] '" + PackageName + "' is not in "
                        + "Packages/manifest.json — an embedded or local package updates itself.");
                return false;
            }
            source = entry.Groups[1].Value;
            if (source.Contains("://") || source.StartsWith("git@"))
                return true;
            if (loud)
                Debug.LogError("[Draw To Play] the manifest names '" + source + "', which is "
                    + "not a git URL — there is no tag to re-fetch.");
            return false;
        }

        /// <summary>The commit the lockfile pins this package to, first 12 hex — what "a new
        /// version exists" is measured against.</summary>
        internal static string PinnedHash()
        {
            string path = Path.Combine("Packages", "packages-lock.json");
            string text = File.Exists(path) ? File.ReadAllText(path) : "";
            int at = text.IndexOf('"' + PackageName + '"', StringComparison.Ordinal);
            if (at < 0)
                return null;
            int h = text.IndexOf("\"hash\"", at, StringComparison.Ordinal);
            if (h < 0)
                return null;
            int open = text.IndexOf('"', text.IndexOf(':', h) + 1);
            int close = open >= 0 ? text.IndexOf('"', open + 1) : -1;
            if (open < 0 || close <= open)
                return null;
            string hash = text.Substring(open + 1, close - open - 1);
            return hash.Length >= 12 ? hash.Substring(0, 12) : hash;
        }

        private static void Poll()
        {
            if (s_Request == null || !s_Request.IsCompleted)
                return;
            EditorApplication.update -= Poll;

            if (s_Request.Status == StatusCode.Success)
            {
                var now = s_Request.Result;
                Debug.Log("[Draw To Play] now at " + now.version
                    + (now.git != null ? " (" + Short(now.git.hash) + ")" : "")
                    + " — packages-lock.json updated.");
            }
            else
            {
                Debug.LogError("[Draw To Play] update failed: "
                    + (s_Request.Error != null ? s_Request.Error.message : "unknown error"));
            }
            s_Request = null;
        }

        private static string Short(string hash)
        {
            return string.IsNullOrEmpty(hash) ? "?" : hash.Substring(0, Mathf.Min(12, hash.Length));
        }
    }

    /// <summary>
    /// THE DESIGNER'S HALF: nobody presses a menu after every pull, so the editor checks by
    /// itself — on load, and whenever Unity regains focus (a pull happens in a terminal or a
    /// git client, and coming back to Unity is when it lands). A background `git ls-remote`
    /// asks where the pinned ref stands now; a moved ref runs the same update the menu does.
    /// Cooldown so a focus flurry is one check, a menu off-switch, never during play mode,
    /// and a check that cannot reach the remote does nothing at all.
    /// </summary>
    [InitializeOnLoad]
    internal static class DrawToPlayAutoUpdate
    {
        private const string PrefKey = "PowerOfFire.DrawToPlay.AutoUpdate";
        internal const string LastResultKey = "PowerOfFire.DrawToPlay.AutoUpdate.last";
        private const string ToggleMenu = "Tools/Draw To Play/Auto-Update on Focus";
        private const double CooldownSeconds = 60d;

        private static double s_LastCheck = -1e9;
        private static System.Threading.Thread s_Worker;
        private static volatile string s_Remote;
        private static volatile bool s_Done;

        static DrawToPlayAutoUpdate()
        {
            EditorApplication.focusChanged += focused =>
            {
                if (focused)
                    Check();
            };
            // On load too — but let the import settle first.
            EditorApplication.delayCall += Check;
        }

        private static bool Enabled => EditorPrefs.GetBool(PrefKey, true);

        [MenuItem(ToggleMenu, true)]
        private static bool ToggleValidate()
        {
            Menu.SetChecked(ToggleMenu, Enabled);
            return true;
        }

        [MenuItem(ToggleMenu)]
        private static void Toggle()
        {
            EditorPrefs.SetBool(PrefKey, !Enabled);
        }

        private static void Check()
        {
            if (!Enabled || s_Worker != null
                || EditorApplication.isPlayingOrWillChangePlaymode
                || EditorApplication.timeSinceStartup - s_LastCheck < CooldownSeconds)
                return;
            if (!DrawToPlayRepin.TryGetGitSource(out string source))
                return;
            if (string.IsNullOrEmpty(DrawToPlayRepin.PinnedHash()))
                return;
            s_LastCheck = EditorApplication.timeSinceStartup;

            Split(source, out string url, out string reference);
            s_Remote = null;
            s_Done = false;
            s_Worker = new System.Threading.Thread(() =>
            {
                s_Remote = LsRemote(url, reference);
                s_Done = true;
            }) { IsBackground = true };
            s_Worker.Start();
            EditorApplication.update += Pump;
        }

        private static void Pump()
        {
            if (!s_Done)
                return;
            EditorApplication.update -= Pump;
            s_Worker = null;

            string remote = s_Remote;
            string pinned = DrawToPlayRepin.PinnedHash();
            if (string.IsNullOrEmpty(remote) || string.IsNullOrEmpty(pinned))
            {
                SessionState.SetString(LastResultKey, "check failed (offline, or no git)");
                return;
            }
            if (remote.StartsWith(pinned, StringComparison.Ordinal))
            {
                SessionState.SetString(LastResultKey, "up to date at " + pinned);
                return;
            }
            SessionState.SetString(LastResultKey,
                "updating " + pinned + " -> " + remote.Substring(0, 12));
            Debug.Log("[Draw To Play] the pinned ref moved (" + pinned + " → "
                + remote.Substring(0, 12) + ") — updating.");
            DrawToPlayRepin.UpdateNow();
        }

        private static void Split(string source, out string url, out string reference)
        {
            reference = "";
            url = source;
            int hash = source.IndexOf('#');
            if (hash >= 0)
            {
                reference = source.Substring(hash + 1);
                url = source.Substring(0, hash);
            }
            // UPM's ?path=/sub syntax is not part of the git URL.
            int query = url.IndexOf('?');
            if (query >= 0)
                url = url.Substring(0, query);
        }

        private static string LsRemote(string url, string reference)
        {
            try
            {
                // The peeled line (^{}) is the commit an annotated tag means; a lightweight
                // tag or a branch answers directly.
                string refs = string.IsNullOrEmpty(reference)
                    ? "HEAD"
                    : "refs/tags/" + reference + " refs/tags/" + reference + "^{} refs/heads/"
                        + reference;
                var info = new System.Diagnostics.ProcessStartInfo(
                    "git", "ls-remote \"" + url + "\" " + refs)
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                using (var process = System.Diagnostics.Process.Start(info))
                {
                    string output = process.StandardOutput.ReadToEnd();
                    if (!process.WaitForExit(8000))
                    {
                        process.Kill();
                        return null;
                    }
                    string best = null;
                    foreach (string line in output.Split('\n'))
                    {
                        string trimmed = line.Trim();
                        if (trimmed.Length < 40)
                            continue;
                        string hash = trimmed.Substring(0, 40);
                        if (trimmed.EndsWith("^{}", StringComparison.Ordinal))
                            return hash;
                        best = best ?? hash;
                    }
                    return best;
                }
            }
            catch
            {
                return null;
            }
        }
    }
}
