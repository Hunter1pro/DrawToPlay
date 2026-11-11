using System;
using System.Collections.Generic;
using UnityEditor;
using Object = UnityEngine.Object;

namespace PowerOfFire.DrawToPlay.Editor
{
    /// <summary>
    /// A declared parameter's value where a CALLER sets it — a manifest row, a preset's setting — as a pick, never a
    /// text box. A row-typed parameter offers its catalog from every asset that may declare it: the tree that
    /// declares the parameter (its Data is where the type came from), then whatever else the caller passes (the
    /// kind's def, the asset being edited). A placement-in-level parameter
    /// (<see cref="GraphTaskParameter.placementInLevelOf"/>) offers the placements of the level its sibling names, and
    /// those of its visits, each under the visit's name.
    /// </summary>
    public static class GraphTaskParameterPicks
    {
        /// <summary>A declaration's current value for this caller: the text that runs and the row's id.</summary>
        public delegate (string text, string entryId) ValueOf(GraphTaskParameter declaration);

        /// <summary>The pick for a parameter whose declaration says what it names; null for a plain number, flag
        /// or word.</summary>
        /// <param name="label">The field's label; null when the host draws its own.</param>
        /// <param name="declared">Every declaration of the same tree — where a placement's level parameter is.</param>
        /// <param name="owners">The assets whose declared catalogs a row may come from, the declaring tree first.</param>
        /// <param name="preferKind">Placements of this kind are offered first (the kind of the row being edited:
        /// a way out arrives at a way in).</param>
        /// <param name="write">Called with the picked text and the row's id (empty for a placement).</param>
        public static StateTreePickField For(string label, GraphTaskParameter parameter,
            IReadOnlyList<GraphTaskParameter> declared, IReadOnlyList<Object> owners, ValueOf valueOf,
            string preferKind, Action<string, string> write)
        {
            if (parameter == null || valueOf == null)
                return null;
            (string text, string entryId) = valueOf(parameter);
            if (parameter.IsPlacementInLevel)
            {
                GraphTaskParameter source = LevelSource(parameter, declared);
                (string level, string levelId) = source != null ? valueOf(source) : ("", "");
                return PlacementField(label, source, level, levelId, text, preferKind, id => write(id, ""));
            }
            StateTreeValueType type = parameter.TypeOf();
            if (type.IsPlain || type.Storage != StateTreeKeyKind.String)
                return null;
            return RowField(label, type, owners, text, entryId, write);
        }

        // --- rows ------------------------------------------------------------------------------

        /// <summary>The rows a value of <paramref name="type"/> may name, from any of the owners.</summary>
        public static void Rows(StateTreeValueType type, IReadOnlyList<Object> owners, List<StateTreeRegistryEntry> into)
        {
            if (into == null)
                return;
            into.Clear();
            if (type == null || owners == null)
                return;
            var found = new List<StateTreeRegistryEntry>();
            foreach (Object owner in owners)
            {
                if (owner == null)
                    continue;
                StateTreeOffers.RowsFor(type, owner, found);
                foreach (StateTreeRegistryEntry row in found)
                {
                    if (!into.Contains(row))
                        into.Add(row);
                }
            }
        }

        /// <summary>Whether any owner declares the type's catalog.</summary>
        public static bool Declared(StateTreeValueType type, IReadOnlyList<Object> owners)
        {
            if (type == null || type.rows == null || owners == null)
                return false;
            foreach (Object owner in owners)
            {
                if (owner != null && StateTreeOffers.Declares(owner, type.rows))
                    return true;
            }
            return false;
        }

        /// <summary>Why nothing is offered, naming who would have to declare the catalog.</summary>
        public static string WhyEmpty(StateTreeValueType type, IReadOnlyList<Object> owners)
        {
            Object first = null;
            var names = new List<string>();
            for (int i = 0; owners != null && i < owners.Count; i++)
            {
                if (owners[i] == null)
                    continue;
                first = first != null ? first : owners[i];
                names.Add(owners[i].name);
            }
            if (type != null && type.kind == StateTreeValueKind.Row && type.rows != null && !Declared(type, owners))
                return "'" + type.rows.name + "' is not declared by " + (names.Count > 0 ? string.Join(" or ", names) : "anything here");
            return StateTreeOffers.WhyEmpty(type, first);
        }

        /// <summary>The row a value names: by the wire's id, else by name.</summary>
        public static StateTreeRegistryEntry Find(List<StateTreeRegistryEntry> rows, string name, string entryId)
        {
            if (rows == null)
                return null;
            if (!string.IsNullOrEmpty(entryId))
            {
                StateTreeRegistryEntry wired = rows.Find(r => r.id == entryId);
                if (wired != null)
                    return wired;
            }
            return string.IsNullOrEmpty(name) ? null : rows.Find(r => r.name == name);
        }

        /// <summary>The picker's menu: the rows, and "+ New" in the catalog when one owner declares it.</summary>
        public static StateTreePickMenu RowMenu(StateTreeValueType type, IReadOnlyList<Object> owners)
        {
            var rows = new List<StateTreeRegistryEntry>();
            Rows(type, owners, rows);
            var menu = new StateTreePickMenu
            {
                title = type != null ? type.Describe() : "Row",
                empty = rows.Count == 0 ? WhyEmpty(type, owners) : "Nothing to pick here"
            };
            foreach (StateTreeRegistryEntry row in rows)
                menu.items.Add(new StateTreePickItem(row.name, row.name, StateTreePickItems.Meaning(row), row.group ?? "") { payload = row });
            if (type != null && type.kind == StateTreeValueKind.Row && Declared(type, owners))
                menu.OfferNew(new[] { type.rows });
            return menu;
        }

        public static StateTreePickField RowField(string label, StateTreeValueType type, IReadOnlyList<Object> owners,
            string name, string entryId, Action<string, string> write)
        {
            var rows = new List<StateTreeRegistryEntry>();
            Rows(type, owners, rows);
            StateTreeRegistryEntry found = Find(rows, name, entryId);
            bool missing = found == null && !string.IsNullOrEmpty(name);
            string catalog = type.rows != null ? type.rows.name : "its catalog";
            string tooltip = found != null ? "A row of " + catalog + ". " + found.Describe()
                : !missing ? type.Describe()
                : rows.Count == 0 ? WhyEmpty(type, owners)
                : "'" + name + "' is not a row of " + catalog + ".";
            string current = found != null ? found.name : name;
            var field = new StateTreePickField(label, current, missing, tooltip,
                at => RowMenu(type, owners).Show(at, current,
                    picked => write(picked.value ?? "", (picked.payload as StateTreeRegistryEntry)?.id ?? "")));
            field.WithRowButtons(found, type.rows);
            return field;
        }

        // --- a placement in another level ---------------------------------------------------------

        /// <summary>The sibling a placement-in-level parameter takes its level from, or null.</summary>
        public static GraphTaskParameter LevelSource(GraphTaskParameter parameter, IReadOnlyList<GraphTaskParameter> declared)
        {
            if (parameter == null || !parameter.IsPlacementInLevel || declared == null)
                return null;
            foreach (GraphTaskParameter each in declared)
            {
                if (each != null && each != parameter && each.id == parameter.placementInLevelOf)
                    return each;
            }
            return null;
        }

        /// <summary>The level a level parameter's value names, in its type's catalog: by the wire's id, else by name.</summary>
        public static LevelDef LevelOf(GraphTaskParameter source, string name, string entryId)
        {
            StateTreeRegistryAsset catalog = source?.TypeOf().rows;
            if (catalog == null)
                return null;
            StateTreeRegistryEntry row = !string.IsNullOrEmpty(entryId) ? catalog.FindById(entryId) : null;
            if (row == null && !string.IsNullOrEmpty(name))
                row = catalog.FindByName(name);
            return row as LevelDef;
        }

        /// <summary>The visit manifests of a level's manifest (<see cref="LevelObjectRegistry.visitOf"/>): the project's,
        /// found once and dropped on a project change. A test hands its own.</summary>
        internal static Func<LevelObjectRegistry, IReadOnlyList<LevelObjectRegistry>> visitsOf = ProjectVisitsOf;

        private static List<LevelObjectRegistry> s_Manifests;

        private static IReadOnlyList<LevelObjectRegistry> ProjectVisitsOf(LevelObjectRegistry level)
        {
            var visits = new List<LevelObjectRegistry>();
            if (level == null)
                return visits;
            if (s_Manifests == null)
            {
                s_Manifests = new List<LevelObjectRegistry>();
                foreach (string guid in AssetDatabase.FindAssets("t:" + nameof(LevelObjectRegistry)))
                {
                    var manifest = AssetDatabase.LoadAssetAtPath<LevelObjectRegistry>(AssetDatabase.GUIDToAssetPath(guid));
                    if (manifest != null)
                        s_Manifests.Add(manifest);
                }
            }
            foreach (LevelObjectRegistry manifest in s_Manifests)
            {
                if (manifest != null && manifest.isVisit && manifest.visitOf == level)
                    visits.Add(manifest);
            }
            visits.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            return visits;
        }

        [InitializeOnLoadMethod]
        private static void Hook()
        {
            EditorApplication.projectChanged -= Invalidate;
            EditorApplication.projectChanged += Invalidate;
        }

        private static void Invalidate() => s_Manifests = null;

        /// <summary>A visit as a picker groups it: its manifest's name without the trailing "Objects" (Hall_2).</summary>
        public static string VisitName(LevelObjectRegistry visit)
        {
            string name = visit != null ? visit.name ?? "" : "";
            return name.EndsWith("Objects", StringComparison.Ordinal) && name.Length > "Objects".Length
                ? name.Substring(0, name.Length - "Objects".Length)
                : name;
        }

        /// <summary>
        /// Every placement a level-scoped pick may name, with the visit it belongs to (null for the level's own): the
        /// level's rows, then each visit's own rows. A visit's copy of a level row (its own start look) carries the
        /// level row's id and is the same place, so it is not offered twice; the level's rows a visit hides stand on
        /// the level and on its other visits, so they stay.
        /// </summary>
        public static List<(LevelObjectDef row, LevelObjectRegistry visit)> Placements(LevelDef level)
        {
            var placements = new List<(LevelObjectDef, LevelObjectRegistry)>();
            LevelObjectRegistry manifest = level?.objects;
            if (manifest == null)
                return placements;
            var ids = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < manifest.Count; i++)
            {
                if (manifest.EntryAt(i) is LevelObjectDef row && !string.IsNullOrEmpty(row.id) && ids.Add(row.id))
                    placements.Add((row, null));
            }
            foreach (LevelObjectRegistry visit in visitsOf?.Invoke(manifest) ?? Array.Empty<LevelObjectRegistry>())
            {
                for (int i = 0; visit != null && i < visit.Count; i++)
                {
                    if (visit.EntryAt(i) is LevelObjectDef row && !string.IsNullOrEmpty(row.id) && ids.Add(row.id))
                        placements.Add((row, visit));
                }
            }
            return placements;
        }

        /// <summary>The placement of that id in the level's manifest or one of its visits', or null.</summary>
        public static LevelObjectDef PlacementOf(LevelDef level, string id)
        {
            return PlacementOf(level, id, out _);
        }

        /// <summary>The same, and the visit it is in (null for the level's own).</summary>
        public static LevelObjectDef PlacementOf(LevelDef level, string id, out LevelObjectRegistry visit)
        {
            visit = null;
            if (level?.objects == null || string.IsNullOrEmpty(id))
                return null;
            foreach ((LevelObjectDef row, LevelObjectRegistry on) in Placements(level))
            {
                if (row.id != id)
                    continue;
                visit = on;
                return row;
            }
            return null;
        }

        /// <summary>The level's placements and its visits', written by id: the level's grouped by kind, each visit's
        /// under its name ("Hall_2/find"); those of <paramref name="preferKind"/> first, the rest under "Other
        /// placements". Without a level, it says to pick the level first.</summary>
        public static StateTreePickMenu PlacementMenu(LevelDef level, string sourceLabel, string preferKind)
        {
            LevelObjectRegistry manifest = level?.objects;
            var menu = new StateTreePickMenu
            {
                title = level != null ? "Placement in " + level.Label : "Placement",
                other = "Other placements",
                empty = level == null ? "Pick the " + sourceLabel + " first"
                    : manifest == null ? "'" + level.name + "' has no manifest yet"
                    : "'" + manifest.name + "' has no placements yet"
            };
            foreach ((LevelObjectDef row, LevelObjectRegistry visit) in Placements(level))
            {
                string kind = row.kind != null ? row.kind.entryName ?? "" : "";
                string where = visit != null ? VisitName(visit).Replace("/", "∕") : "";
                menu.items.Add(new StateTreePickItem(row.id, string.IsNullOrEmpty(row.name) ? row.id : row.name,
                    (string.IsNullOrEmpty(kind) ? "placement" : kind) + " · " + row.id + (visit != null ? " · on " + where : ""),
                    string.IsNullOrEmpty(where) ? kind : string.IsNullOrEmpty(kind) ? where : where + "/" + kind)
                {
                    fits = string.IsNullOrEmpty(preferKind) || kind == preferKind,
                    payload = row
                });
            }
            return menu;
        }

        public static StateTreePickField PlacementField(string label, GraphTaskParameter source, string levelName,
            string levelEntryId, string current, string preferKind, Action<string> write)
        {
            LevelDef level = LevelOf(source, levelName, levelEntryId);
            string sourceLabel = source != null ? ObjectNames.NicifyVariableName(source.name).ToLowerInvariant() : "level";
            LevelObjectDef row = PlacementOf(level, current, out LevelObjectRegistry visit);
            bool missing = row == null && !string.IsNullOrEmpty(current);
            string tooltip = source == null ? "The parameter that names its level is not declared any more."
                : level == null ? (string.IsNullOrEmpty(levelName) ? "Pick the " + sourceLabel + " first: this is a place in its level."
                    : "'" + levelName + "' names no level, so there is nothing to pick from.")
                : row != null ? "A placement of " + level.Label + (visit != null ? " on its visit " + VisitName(visit) : "")
                    + " (" + row.id + ")."
                : missing ? "'" + current + "' is no placement of " + level.Label + "."
                : "A placement in " + level.Label + ".";
            var field = new StateTreePickField(label, row != null ? row.name : current, missing, tooltip,
                at => PlacementMenu(level, sourceLabel, preferKind).Show(at, current, picked => write(picked.value ?? "")));
            LevelObjectRegistry manifest = visit != null ? visit : level?.objects;
            field.WithPing(manifest, manifest == null ? null
                : visit != null ? "the manifest of " + level.Label + "'s visit " + VisitName(visit)
                : "the manifest of " + level.Label);
            return field;
        }
    }
}
