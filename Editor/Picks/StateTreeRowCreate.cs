using System;
using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace PowerOfFire.DrawToPlay.Editor
{
    /// <summary>
    /// "+ New …" for the row pickers: a row made in the one registry the field resolves from, named, with
    /// a fresh id (a manifest's placement also stands at the Scene view's centre), and picked. Offered only
    /// for targets the field can actually resolve from — never on a project-wide guess.
    /// </summary>
    public static class StateTreeRowCreate
    {
        public const string UndoName = "New Row";

        /// <summary>A row called <paramref name="name"/> in <paramref name="registry"/> — the existing one
        /// when the name is taken (a pick, never a twin). Undo-recorded; null when the registry keeps no
        /// <c>entries</c> list.</summary>
        public static StateTreeRegistryEntry Add(StateTreeRegistryAsset registry, string name, string group = "")
        {
            if (registry == null || string.IsNullOrWhiteSpace(name))
                return null;
            name = name.Trim();
            StateTreeRegistryEntry existing = registry.FindByName(name);
            if (existing != null)
                return existing;
            if (!(registry.GetType().GetField("entries")?.GetValue(registry) is IList list)
                || registry.entryType == null || registry.entryType.IsAbstract)
                return null;

            Undo.RecordObject(registry, UndoName);
            var entry = (StateTreeRegistryEntry)Activator.CreateInstance(registry.entryType);
            entry.id = Guid.NewGuid().ToString("N");
            entry.name = name;
            entry.group = group ?? "";
            if (entry is LevelObjectDef placement && registry is LevelObjectRegistry manifest)
                placement.position = SceneCentre(manifest);
            list.Add(entry);
            EditorUtility.SetDirty(registry);
            return entry;
        }

        /// <summary>Where a new placement stands: the Scene view's pivot on the manifest's plane, else the origin.</summary>
        public static Vector2 SceneCentre(LevelObjectRegistry manifest)
        {
            SceneView view = SceneView.lastActiveSceneView;
            return view != null && manifest != null ? manifest.ToPlan(view.pivot) : Vector2.zero;
        }

        /// <summary>One "+ New … in X…" per target. <paramref name="byId"/>: the field keeps the row's id
        /// (a placement), else its name. A registry with its own creator panel (a level is a scene too) is
        /// only revealed — its panel makes the row.</summary>
        public static StateTreePickMenu OfferNew(this StateTreePickMenu menu, IEnumerable<StateTreeRegistryAsset> targets,
            bool byId = false)
        {
            if (menu == null || targets == null)
                return menu;
            var seen = new HashSet<StateTreeRegistryAsset>();
            foreach (StateTreeRegistryAsset registry in targets)
            {
                if (registry == null || !seen.Add(registry))
                    continue;
                string kind = KindName(registry.entryType);
                if (RegistryCreators.Has(registry))
                {
                    menu.Create("+ New " + kind + " in " + registry.name + "… (its creator)", null, null, registry);
                    continue;
                }
                StateTreeRegistryAsset target = registry;
                menu.Create("+ New " + kind + " in " + registry.name + "…", "A new " + kind + " in " + registry.name,
                    name =>
                    {
                        StateTreeRegistryEntry row = Add(target, name);
                        return row == null ? null : new StateTreePickItem(byId ? row.id : row.name, row.name) { payload = row };
                    }, registry);
            }
            return menu;
        }

        /// <summary>The single target a picker narrowed to, else the catalogs it declares, else none.</summary>
        public static List<StateTreeRegistryAsset> Targets(StateTreeRegistryAsset narrowTo,
            List<StateTreeRegistryAsset> declared, bool isDeclared)
        {
            var targets = new List<StateTreeRegistryAsset>();
            if (narrowTo != null)
                targets.Add(narrowTo);
            else if (isDeclared && declared != null)
                targets.AddRange(declared);
            return targets;
        }

        internal static string KindName(Type entryType)
        {
            if (entryType == null)
                return "row";
            string name = entryType.Name;
            if (name.Length > 3 && name.EndsWith("Def", StringComparison.Ordinal))
                name = name.Substring(0, name.Length - 3);
            return ObjectNames.NicifyVariableName(name);
        }
    }
}
