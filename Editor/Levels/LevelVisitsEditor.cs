using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace PowerOfFire.DrawToPlay.Editor
{
    /// <summary>
    /// A level's visits as the editor works with them: its Level nodes in the episode, the one picked (shared by the
    /// Level Manifest window, the Scene view's Visit overlay and "Open level"), a new visit made, and a level's row
    /// hidden or shown on one — each an undo step.
    /// </summary>
    public static class LevelVisitsEditor
    {
        /// <summary>One visit: its Level node and what it adds and hides (null: the plain level).</summary>
        public sealed class Visit
        {
            public StateTreeAsset episode;
            public StateTreeNodeAsset node;
            public IStateTreeLevelVisit context;
            public LevelObjectRegistry manifest;
            public int index;

            public string label => string.IsNullOrEmpty(node.displayName) ? node.nodeId : node.displayName;

            /// <summary>Not the first visit, and no manifest of its own yet: it stands as the plain level.</summary>
            public bool lacksManifest => index > 0 && manifest == null;
        }

        /// <summary>The visits of <paramref name="level"/> in its episode, in story order; empty when it is in none.</summary>
        public static List<Visit> Of(LevelContent level) => Of(LevelLinks.Of(level));

        public static List<Visit> Of(LevelLinks links)
        {
            var visits = new List<Visit>();
            if (links == null || links.episode == null || links.row == null)
                return visits;
            foreach (StateTreeNodeAsset node in StateTreeEditorOps.CollectNodes(links.episode))
            {
                if (LevelVisits.LevelNameOf(node) != links.row.name)
                    continue;
                IStateTreeLevelVisit context = null;
                foreach (StateTreeTaskAsset task in node.tasks)
                    context ??= task as IStateTreeLevelVisit;
                visits.Add(new Visit
                {
                    episode = links.episode, node = node, context = context, manifest = LevelVisits.ManifestOf(node),
                    index = visits.Count
                });
            }
            return visits;
        }

        /// <summary>The level's own manifest, by its catalogue row's name — what a visit with none of its own stands as.</summary>
        public static LevelObjectRegistry PlainManifest(string levelName)
        {
            LevelRegistry levels = LevelSetups.current?.levels;
            return !string.IsNullOrEmpty(levelName) && levels != null && levels.FindByName(levelName) is LevelDef row
                ? row.objects
                : null;
        }

        // ---- the picked visit ------------------------------------------------------------------

        private const string k_PickKey = "PowerOfFire.DrawToPlay.LevelVisit.";

        /// <summary>The Level node id picked for <paramref name="level"/> this Editor session; empty: the first visit.</summary>
        public static string Picked(LevelContent level)
        {
            return level != null ? SessionState.GetString(k_PickKey + KeyOf(level), "") : "";
        }

        /// <summary>The picked visit among <paramref name="visits"/>: the one picked, else the first; null when none.</summary>
        public static Visit PickedOf(LevelContent level, List<Visit> visits)
        {
            if (visits == null || visits.Count == 0)
                return null;
            string picked = Picked(level);
            return visits.Find(v => v.node.nodeId == picked) ?? visits[0];
        }

        /// <summary>Pick the visit <paramref name="visitNode"/> (a Level node id) of <paramref name="level"/>: the Level
        /// Manifest and the Scene view follow it.</summary>
        public static void Pick(LevelContent level, string visitNode)
        {
            if (level == null)
                return;
            SessionState.SetString(k_PickKey + KeyOf(level), visitNode ?? "");
            LevelManifestWindow.VisitPicked();
            LevelVisitOverlay.Refresh();
            SceneView.RepaintAll();
        }

        /// <summary>Per level, its pick's key — asked on every Scene view repaint, so found once.</summary>
        private static readonly Dictionary<LevelContent, string> s_Keys = new Dictionary<LevelContent, string>();

        private static string KeyOf(LevelContent level)
        {
            if (s_Keys.TryGetValue(level, out string key))
                return key;
            string guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(level));
            return s_Keys[level] = string.IsNullOrEmpty(guid) ? "unsaved:" + level.name : guid;
        }

        // ---- making ------------------------------------------------------------------------------

        /// <summary>
        /// "+ New visit": a manifest for it beside the level's own (none of its rows hidden, nothing added), and a
        /// Level node for it in the episode made by the game's setup. Picked. Null, with why, when nothing is made.
        /// </summary>
        public static Visit NewVisit(LevelContent level, out string report)
        {
            report = "";
            ILevelSetup setup = LevelSetups.current;
            LevelLinks links = LevelLinks.Of(level);
            List<Visit> visits = Of(links);
            if (setup == null || level == null || level.objects == null || visits.Count == 0)
            {
                report = "the level has no Level node in an episode to visit again";
                return null;
            }
            string label = visits[0].label + "_" + (visits.Count + 1);
            LevelObjectRegistry manifest = MakeManifest(level.objects, label);
            int group = StateTreeEditorOps.BeginUndoGroup("New visit " + label);
            StateTreeNodeAsset node = setup.AddVisit(level, manifest, label);
            StateTreeEditorOps.EndUndoGroup(group);
            if (node == null)
            {
                AssetDatabase.DeleteAsset(AssetDatabase.GetAssetPath(manifest));
                report = "the game's level setup makes no visits";
                return null;
            }
            report = "made " + label + ": " + AssetDatabase.GetAssetPath(manifest) + ", and its Level node in " + links.episode.name;
            Pick(level, node.nodeId);
            return Of(level).Find(v => v.node == node);
        }

        /// <summary>A visit with no manifest of its own gets one, beside the level's, named after it. One undo step for the
        /// Level node's change (the file stays).</summary>
        public static LevelObjectRegistry MakeOwnManifest(LevelContent level, Visit visit)
        {
            if (level == null || level.objects == null || visit == null || visit.context == null || visit.manifest != null)
                return visit?.manifest;
            LevelObjectRegistry manifest = MakeManifest(level.objects, visit.label);
            var task = (Object)visit.context;
            Undo.RecordObject(task, "Make " + visit.label + "'s manifest");
            visit.context.visitManifest = manifest;
            EditorUtility.SetDirty(task);
            if (EditorUtility.IsPersistent(visit.episode))
                AssetDatabase.SaveAssetIfDirty(visit.episode);
            visit.manifest = manifest;
            LevelManifestWindow.VisitPicked();
            LevelVisitOverlay.Refresh();
            return manifest;
        }

        /// <summary>A visit's manifest of <paramref name="level"/>: its tags, plane, presets and declared catalogues,
        /// nothing hidden, nothing added — saved beside the level's when that is an asset.</summary>
        public static LevelObjectRegistry MakeManifest(LevelObjectRegistry level, string label)
        {
            var manifest = ScriptableObject.CreateInstance<LevelObjectRegistry>();
            manifest.name = Safe(label) + "Objects";
            manifest.visitOf = level;
            manifest.tags = new List<WorldTagRegistry>(level.tags);
            manifest.plane = level.plane;
            manifest.presets = level.presets;
            manifest.dependsOn = new List<StateTreeRegistryAsset>(level.dependsOn);
            string beside = AssetDatabase.GetAssetPath(level);
            if (!string.IsNullOrEmpty(beside))
            {
                string path = AssetDatabase.GenerateUniqueAssetPath(
                    System.IO.Path.GetDirectoryName(beside) + "/" + manifest.name + ".asset");
                AssetDatabase.CreateAsset(manifest, path);
                AssetDatabase.SaveAssetIfDirty(manifest);
            }
            return manifest;
        }

        private static string Safe(string label)
        {
            var chars = new System.Text.StringBuilder();
            foreach (char c in label ?? "")
            {
                if (char.IsLetterOrDigit(c) || c == '_' || c == '-')
                    chars.Append(c);
            }
            return chars.Length > 0 ? chars.ToString() : "Visit";
        }

        // ---- hiding --------------------------------------------------------------------------------

        /// <summary>The level's row <paramref name="id"/> gone on the visit (<paramref name="hide"/>), or back. One undo
        /// step; nothing when it already is.</summary>
        public static bool SetHidden(LevelObjectRegistry visit, string id, bool hide)
        {
            if (visit == null || !visit.isVisit || string.IsNullOrEmpty(id) || visit.Hides(id) == hide)
                return false;
            Undo.IncrementCurrentGroup();
            Undo.RecordObject(visit, (hide ? "Hide " : "Show ") + id + " in this visit");
            if (hide)
                visit.hidden.Add(id);
            else
                visit.hidden.RemoveAll(h => h == id);
            EditorUtility.SetDirty(visit);
            return true;
        }
    }
}
