using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace PowerOfFire.DrawToPlay.Editor
{
    /// <summary>The two hands a generator (a demo's Verify, a game's level template) needs
    /// most: a manifest row, and a scope's installer wiped once per pass.</summary>
    public static class LevelAuthoring
    {
        /// <summary>A placement row: the kind by name, the definition row it is an instance of
        /// (empty for a character whose difference is its tree), where, which way, which mind.</summary>
        public static LevelObjectDef Placement(string id, string name, string kind, string entry,
            Vector2 position, float facing, StateTreeAsset tree, string group = "Cast",
            string entryIdPrefix = "item.")
        {
            var row = new LevelObjectDef
            {
                id = id, name = name, group = group,
                position = position,
                facing = facing,
                tree = tree
            };
            row.kind.entryId = "kind." + kind;
            row.kind.entryName = kind;
            if (!string.IsNullOrEmpty(entry))
            {
                row.entry.entryId = entryIdPrefix + entry;
                row.entry.entryName = entry;
            }
            return row;
        }

        /// <summary>The scope's installer — cleared the FIRST time a pass touches a host, so a
        /// generator adds its defs without stacking them on a previous run's.</summary>
        public static StateTreeServiceInstaller Subsystems(GameObject host)
        {
            var installer = host.GetComponent<StateTreeServiceInstaller>()
                ?? host.AddComponent<StateTreeServiceInstaller>();
            if (s_Installed.Add(host))
            {
                installer.install.Clear();
                installer.undeclared.Clear();
                GameObjectUtility.RemoveMonoBehavioursWithMissingScript(host);
            }
            return installer;
        }

        private static readonly HashSet<GameObject> s_Installed = new HashSet<GameObject>();
    }
}
