using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace PowerOfFire.DrawToPlay.Editor
{
    /// <summary>
    /// What the Level Manifest window does to a manifest, apart from the window: a new row of a kind
    /// (standard name, fresh id, the kind's group, one undo step), where a drop lands, and the rows
    /// grouped by kind for the list.
    /// </summary>
    internal static class LevelManifestEdits
    {
        /// <summary>A row of the list's grouping: one kind, its rows' indices sorted by name.</summary>
        internal sealed class KindGroup
        {
            public string kind;
            public readonly List<int> rows = new List<int>();
        }

        /// <summary>What a drop sees under the pointer — a rendered surface or a collider.</summary>
        internal delegate bool SurfaceProbe(Ray ray, out Vector3 point);

        /// <summary>The group a row with no kind is listed under.</summary>
        internal const string NoKind = "(no kind)";

        /// <summary>
        /// A new row of <paramref name="kindName"/> at <paramref name="world"/>, appended and returned.
        /// Named and id'd <c>level.kind.N</c> with the first free N, in the group the level already
        /// files that kind under. One undo step; the manifest is dirtied.
        /// </summary>
        /// <param name="onSurface">The point is on something in the scene rather than the ground plane.</param>
        /// <param name="preset">The preset it is made from, or null for a row of the kind alone.</param>
        internal static LevelObjectDef CreateRow(LevelObjectRegistry manifest, LevelObjectKindRegistry kinds,
            string kindName, string levelKey, Vector3 world, bool onSurface, LevelObjectPresetDef preset = null)
        {
            if (manifest == null || string.IsNullOrEmpty(kindName))
                return null;

            Undo.IncrementCurrentGroup();
            int undo = Undo.GetCurrentGroup();
            Undo.RecordObject(manifest, "Place " + kindName);

            string name = StandardName(manifest, levelKey, kindName);
            var row = new LevelObjectDef { id = name, name = name, group = GroupFor(manifest, kindName) };
            var kind = kinds != null ? kinds.FindByName(kindName) as LevelObjectKindDef : null;
            row.kind.entryId = kind != null ? kind.id : "";
            row.kind.entryName = kindName;
            if (preset != null)
                row.preset = new StateTreeEntryRef<LevelObjectPresetDef> { entryId = preset.id, entryName = preset.name };
            Stand(manifest, row, world, onSurface);

            manifest.entries.Add(row);
            EditorUtility.SetDirty(manifest);
            Undo.SetCurrentGroupName("Place " + kindName);
            Undo.CollapseUndoOperations(undo);
            return row;
        }

        /// <summary>
        /// Writes where a dropped row stands: the plan from the point, and — when it landed on a surface
        /// raised off the ground plane (a desk, a landing) — that surface's height as the row's own.
        /// On the floor, or at the view's centre, it keeps standing at its kind's height.
        /// </summary>
        internal static void Stand(LevelObjectRegistry manifest, LevelObjectDef row, Vector3 world, bool onSurface)
        {
            row.position = manifest.ToPlan(world);
            if (!onSurface)
                return;
            float up = manifest.HeightOf(world);
            if (Mathf.Abs(up) <= RaisedAbove)
                return;
            row.overrideHeight = true;
            row.height = up;
        }

        /// <summary>How far off the ground plane a surface must be to count as raised — above a floor
        /// mesh's own few centimetres, below a step.</summary>
        internal const float RaisedAbove = 0.15f;

        /// <summary><c>level.kind.N</c> with the lowest N no row of the manifest uses as id or name.</summary>
        internal static string StandardName(LevelObjectRegistry manifest, string levelKey, string kindName)
        {
            string prefix = (string.IsNullOrEmpty(levelKey) ? "" : levelKey + ".") + kindName + ".";
            var taken = new HashSet<string>(StringComparer.Ordinal);
            if (manifest != null)
            {
                foreach (LevelObjectDef row in manifest.entries)
                {
                    if (row == null)
                        continue;
                    taken.Add(row.id ?? "");
                    taken.Add(row.name ?? "");
                }
            }
            int n = 1;
            while (taken.Contains(prefix + n))
                n++;
            return prefix + n;
        }

        /// <summary>The group the manifest files most of this kind's rows under; the kind's name when
        /// it has none yet.</summary>
        internal static string GroupFor(LevelObjectRegistry manifest, string kindName)
        {
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            string best = null;
            var bestCount = 0;
            if (manifest != null)
            {
                foreach (LevelObjectDef row in manifest.entries)
                {
                    if (row == null || row.kind.entryName != kindName || string.IsNullOrEmpty(row.group))
                        continue;
                    counts.TryGetValue(row.group, out int count);
                    counts[row.group] = ++count;
                    if (count > bestCount)
                    {
                        best = row.group;
                        bestCount = count;
                    }
                }
            }
            return best ?? kindName;
        }

        /// <summary>The level's short name for row names: its catalog row's name ("hall"), else the
        /// content asset's name without "Content", lower case.</summary>
        internal static string LevelKey(LevelContent level, string catalogName)
        {
            string key = !string.IsNullOrWhiteSpace(catalogName)
                ? catalogName
                : level != null ? level.name : "";
            if (key.EndsWith("Content", StringComparison.Ordinal) && key.Length > "Content".Length)
                key = key.Substring(0, key.Length - "Content".Length);
            return key.Trim().Replace(' ', '-').ToLowerInvariant();
        }

        /// <summary>
        /// Where a drop along <paramref name="ray"/> lands: on what <paramref name="surface"/> sees,
        /// else on the manifest's ground plane. False when the ray never meets the plane.
        /// </summary>
        internal static bool DropPoint(LevelObjectRegistry manifest, Ray ray, SurfaceProbe surface,
            out Vector3 world, out bool onSurface)
        {
            if (surface != null && surface(ray, out world))
            {
                onSurface = true;
                return true;
            }

            onSurface = false;
            Vector3 origin = manifest.ToWorld(Vector2.zero);
            var ground = new Plane(manifest.ToWorld(Vector2.zero, 1f) - origin, origin);
            if (ground.Raycast(ray, out float distance) && distance >= 0f)
            {
                world = ray.GetPoint(distance);
                return true;
            }
            world = default;
            return false;
        }

        /// <summary>
        /// The rows by kind: groups in the kind registry's order, kinds it does not list after them by
        /// name, rows without a kind last; inside a group, rows by name (natural order, so 2 before 10).
        /// </summary>
        internal static List<KindGroup> GroupByKind(IReadOnlyList<LevelObjectDef> rows, LevelObjectKindRegistry kinds)
        {
            var byKind = new Dictionary<string, KindGroup>(StringComparer.Ordinal);
            for (int i = 0; i < rows.Count; i++)
            {
                LevelObjectDef row = rows[i];
                if (row == null)
                    continue;
                string kind = string.IsNullOrEmpty(row.kind.entryName) ? NoKind : row.kind.entryName;
                if (!byKind.TryGetValue(kind, out KindGroup group))
                    byKind[kind] = group = new KindGroup { kind = kind };
                group.rows.Add(i);
            }

            var ordered = new List<KindGroup>(byKind.Count);
            if (kinds != null)
            {
                foreach (LevelObjectKindDef kind in kinds.entries)
                {
                    if (kind != null && byKind.TryGetValue(kind.name, out KindGroup group) && !ordered.Contains(group))
                        ordered.Add(group);
                }
            }
            var rest = new List<KindGroup>();
            foreach (KindGroup group in byKind.Values)
            {
                if (!ordered.Contains(group) && group.kind != NoKind)
                    rest.Add(group);
            }
            rest.Sort((a, b) => EditorUtility.NaturalCompare(a.kind, b.kind));
            ordered.AddRange(rest);
            if (byKind.TryGetValue(NoKind, out KindGroup none))
                ordered.Add(none);

            foreach (KindGroup group in ordered)
            {
                group.rows.Sort((a, b) =>
                {
                    int byName = EditorUtility.NaturalCompare(rows[a].name ?? "", rows[b].name ?? "");
                    return byName != 0 ? byName : a.CompareTo(b);
                });
            }
            return ordered;
        }

        /// <summary>Whether a row answers the list's search: its name, id, kind or definition entry.</summary>
        internal static bool Matches(LevelObjectDef row, string filter)
        {
            if (string.IsNullOrWhiteSpace(filter))
                return true;
            filter = filter.Trim();
            return Has(row.name, filter) || Has(row.id, filter) || Has(row.kind.entryName, filter)
                || Has(row.entryName, filter);
        }

        private static bool Has(string text, string filter)
        {
            return !string.IsNullOrEmpty(text) && text.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// The colour a kind is drawn in — its ghost, its palette tile, its group. From the kind's place
        /// in the registry, stepped by the golden ratio so neighbours stay far apart on the wheel; a kind
        /// the registry does not list falls back to an FNV hash of its name (<c>string.GetHashCode</c> is
        /// randomised per process, so it would change colour every restart).
        /// </summary>
        internal static Color KindColour(LevelObjectKindRegistry registry, string kind)
        {
            if (string.IsNullOrEmpty(kind) || kind == NoKind)
                return NoKindTint;

            if (registry != null)
            {
                for (int i = 0; i < registry.entries.Count; i++)
                {
                    if (registry.entries[i] != null
                        && string.Equals(registry.entries[i].name, kind, StringComparison.Ordinal))
                        return Hue(i * 0.618034f % 1f);
                }
            }

            uint hash = 2166136261u;
            for (int i = 0; i < kind.Length; i++)
                hash = (hash ^ kind[i]) * 16777619u;
            return Hue(hash % 3600u / 3600f);
        }

        private static Color Hue(float hue)
        {
            return Color.HSVToRGB(hue, 0.7f, 1f);
        }

        /// <summary>A placement with no kind — a neutral off the wheel, so "unset" never looks like a kind.</summary>
        internal static readonly Color NoKindTint = new Color(0.7f, 0.7f, 0.72f);
    }
}
