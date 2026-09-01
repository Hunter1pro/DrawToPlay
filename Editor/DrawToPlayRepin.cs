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
        private const string PackageName = "com.powerofire.drawtoplay";

        private static AddRequest s_Request;

        [MenuItem("Tools/Draw To Play/Update Draw To Play (re-fetch the tag)")]
        private static void Update()
        {
            if (s_Request != null)
            {
                Debug.Log("[Draw To Play] an update is already running.");
                return;
            }

            string manifestPath = Path.Combine("Packages", "manifest.json");
            string manifest = File.Exists(manifestPath) ? File.ReadAllText(manifestPath) : "";
            Match entry = Regex.Match(manifest,
                "\"" + Regex.Escape(PackageName) + "\"\\s*:\\s*\"([^\"]+)\"");
            if (!entry.Success)
            {
                Debug.LogError("[Draw To Play] '" + PackageName + "' is not in "
                    + "Packages/manifest.json — an embedded or local package updates itself.");
                return;
            }

            string source = entry.Groups[1].Value;
            if (!source.Contains("://") && !source.StartsWith("git@"))
            {
                Debug.LogError("[Draw To Play] the manifest names '" + source + "', which is "
                    + "not a git URL — there is no tag to re-fetch.");
                return;
            }

            var installed = UnityEditor.PackageManager.PackageInfo.FindForPackageName(PackageName);
            string was = installed != null && installed.git != null
                ? installed.version + " (" + Short(installed.git.hash) + ")"
                : "unknown";
            Debug.Log("[Draw To Play] was at " + was + " — re-fetching " + source + " …");

            s_Request = Client.Add(source);
            EditorApplication.update += Poll;
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
}
