using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace PowerOfFire.DrawToPlay.Editor
{
    /// <summary>
    /// Copy and paste of Level Manifest rows. The copy is the rows' serialized data on the system clipboard, so it
    /// survives a scene switch and a domain reload. A paste is a copy of the row, never a reference to it: a fresh id
    /// (and name) in the target, everything else kept; offset a little when pasted back into the manifest it came from,
    /// the exact pose anywhere else. A one-per-level kind the target holds already, or a kind its catalog does not know,
    /// is refused and said.
    /// </summary>
    internal static class LevelManifestClipboard
    {
        private const string k_Marker = "DrawToPlay.LevelRows:";

        /// <summary>How far, on the plan, a paste into its own manifest moves from the row it copies.</summary>
        internal static readonly Vector2 Offset = new Vector2(0.5f, 0.5f);

        [Serializable]
        internal sealed class Clip
        {
            public string source = "";
            public string levelKey = "";
            public List<LevelObjectDef> rows = new List<LevelObjectDef>();
        }

        /// <summary>The rows, serialized onto the system clipboard. The text written is returned.</summary>
        internal static string Copy(LevelObjectRegistry from, string levelKey, IEnumerable<LevelObjectDef> rows)
        {
            var clip = new Clip { source = SourceOf(from), levelKey = levelKey ?? "" };
            foreach (LevelObjectDef row in rows)
            {
                if (row != null)
                    clip.rows.Add(row);
            }
            if (clip.rows.Count == 0)
                return null;
            string text = k_Marker + EditorJsonUtility.ToJson(clip);
            EditorGUIUtility.systemCopyBuffer = text;
            return text;
        }

        /// <summary>The rows on the clipboard, or null when it holds none.</summary>
        internal static Clip Read()
        {
            string text = EditorGUIUtility.systemCopyBuffer;
            if (string.IsNullOrEmpty(text) || !text.StartsWith(k_Marker, StringComparison.Ordinal))
                return null;
            var clip = new Clip();
            try
            {
                EditorJsonUtility.FromJsonOverwrite(text.Substring(k_Marker.Length), clip);
            }
            catch (ArgumentException)
            {
                return null;
            }
            clip.rows.RemoveAll(row => row == null);
            return clip.rows.Count > 0 ? clip : null;
        }

        internal static bool HasRows() => Read() != null;

        /// <summary>
        /// The clip's rows added to <paramref name="target"/> in one undo step and returned; the ones refused are
        /// said in <paramref name="refused"/>.
        /// </summary>
        internal static List<LevelObjectDef> Paste(LevelObjectRegistry target, LevelObjectKindRegistry kinds,
            string levelKey, Clip clip, List<string> refused)
        {
            var added = new List<LevelObjectDef>();
            if (target == null || clip == null || clip.rows.Count == 0)
                return added;
            bool home = clip.source == SourceOf(target);

            var standing = new List<LevelObjectDef>();
            target.CollectRows(standing);
            var taken = new HashSet<string>(StringComparer.Ordinal);
            foreach (LevelObjectDef row in target.entries)
                Take(taken, row);
            if (target.isVisit)
            {
                foreach (LevelObjectDef row in target.visitOf.entries)
                    Take(taken, row);
            }

            Undo.IncrementCurrentGroup();
            int undo = Undo.GetCurrentGroup();
            Undo.RecordObject(target, "Paste Level Objects");
            foreach (LevelObjectDef copied in clip.rows)
            {
                string kindName = copied.kind != null ? copied.kind.entryName : "";
                string label = string.IsNullOrEmpty(copied.name) ? copied.id : copied.name;
                if (!string.IsNullOrEmpty(kindName) && kinds != null && kinds.FindByName(kindName) == null)
                {
                    refused?.Add($"'{label}' not pasted: '{kindName}' is not a kind of this level.");
                    continue;
                }
                LevelObjectKindDef kind = LevelManifestEdits.KindOf(kinds, kindName);
                if (kind != null && kind.onePerLevel && standing.Exists(row => row != null && row.kind.entryName == kindName))
                {
                    refused?.Add($"'{label}' not pasted: a level holds one '{kindName}', and this one has it already.");
                    continue;
                }

                LevelObjectDef row = Clone(copied);
                string oldId = row.id ?? "";
                row.id = Fresh(Reprefix(oldId, clip.levelKey, levelKey), oldId, taken);
                taken.Add(row.id);
                if (string.IsNullOrEmpty(copied.name) || copied.name == oldId)
                    row.name = row.id;
                else
                {
                    row.name = Fresh(Reprefix(copied.name, clip.levelKey, levelKey), copied.name, taken);
                    taken.Add(row.name);
                }
                if (home)
                    row.position = FreeSpot(standing, copied.position);
                target.entries.Add(row);
                standing.Add(row);
                added.Add(row);
            }
            if (added.Count > 0)
                EditorUtility.SetDirty(target);
            Undo.SetCurrentGroupName(added.Count == 1 ? "Paste " + added[0].name : "Paste " + added.Count + " Level Objects");
            Undo.CollapseUndoOperations(undo);
            return added;
        }

        /// <summary>The first spot one <see cref="Offset"/> or more away from <paramref name="from"/> that no row
        /// stands on — a second paste does not land on the first.</summary>
        private static Vector2 FreeSpot(List<LevelObjectDef> standing, Vector2 from)
        {
            for (int step = 1; step < 64; step++)
            {
                Vector2 spot = from + Offset * step;
                if (!standing.Exists(row => row != null && (row.position - spot).sqrMagnitude < 1e-4f))
                    return spot;
            }
            return from + Offset;
        }

        /// <summary>A deep copy, through serialization: no list or reference object shared with the original.</summary>
        private static LevelObjectDef Clone(LevelObjectDef row)
        {
            var copy = new LevelObjectDef();
            EditorJsonUtility.FromJsonOverwrite(EditorJsonUtility.ToJson(row), copy);
            return copy;
        }

        private static void Take(HashSet<string> taken, LevelObjectDef row)
        {
            if (row == null)
                return;
            taken.Add(row.id ?? "");
            taken.Add(row.name ?? "");
        }

        /// <summary>The source level's key swapped for the target's, when the text starts with it.</summary>
        internal static string Reprefix(string text, string fromKey, string toKey)
        {
            text ??= "";
            if (string.IsNullOrEmpty(fromKey) || string.IsNullOrEmpty(toKey) || fromKey == toKey
                || !text.StartsWith(fromKey + ".", StringComparison.Ordinal))
                return text;
            return toKey + text.Substring(fromKey.Length);
        }

        private static readonly Regex s_Numbered = new Regex(@"^(.*)\.(\d+)$");

        /// <summary>
        /// <paramref name="wanted"/> when it is free and not the original's; else its stem with the first free
        /// <c>.N</c> — <c>hall.looks.pink-bed</c> → <c>hall.looks.pink-bed.1</c>, <c>hall.door.3</c> → the first free
        /// <c>hall.door.N</c>, as the palette numbers them.
        /// </summary>
        internal static string Fresh(string wanted, string original, HashSet<string> taken)
        {
            if (!string.IsNullOrEmpty(wanted) && wanted != original && !taken.Contains(wanted))
                return wanted;
            Match numbered = s_Numbered.Match(wanted ?? "");
            string stem = numbered.Success ? numbered.Groups[1].Value : wanted ?? "";
            int n = 1;
            while (taken.Contains(stem + "." + n) || stem + "." + n == original)
                n++;
            return stem + "." + n;
        }

        /// <summary>Which manifest a copy came from: its asset's GUID, or the instance for one not saved.</summary>
        internal static string SourceOf(LevelObjectRegistry manifest)
        {
            if (manifest == null)
                return "";
            return AssetDatabase.TryGetGUIDAndLocalFileIdentifier(manifest, out string guid, out long _)
                && !string.IsNullOrEmpty(guid)
                ? guid
                : "instance:" + manifest.GetEntityId();
        }
    }
}
