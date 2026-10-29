using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;

namespace PowerOfFire.DrawToPlay.Editor
{
    /// <summary>
    /// The asset a row stands for — a film row's clip, a level row's content — so a picker can show it
    /// (◎) and open it (▶). The row's first object field that is set, else what a game's resolver says
    /// (a row that streams by address names its asset only through the game's content system).
    /// </summary>
    public static class StateTreeRowAssets
    {
        /// <summary>A game's answer for rows the default cannot read; first non-null wins.</summary>
        public static readonly List<Func<StateTreeRegistryEntry, UnityEngine.Object>> resolvers =
            new List<Func<StateTreeRegistryEntry, UnityEngine.Object>>();

        public static UnityEngine.Object AssetOf(StateTreeRegistryEntry row)
        {
            if (row == null)
                return null;
            foreach (FieldInfo field in row.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                if (typeof(UnityEngine.Object).IsAssignableFrom(field.FieldType)
                    && field.GetValue(row) is UnityEngine.Object asset && asset != null)
                    return asset;
            }
            foreach (Func<StateTreeRegistryEntry, UnityEngine.Object> resolve in resolvers)
            {
                UnityEngine.Object asset = resolve?.Invoke(row);
                if (asset != null)
                    return asset;
            }
            return null;
        }

        /// <summary>◎ selects the row's asset, ▶ opens it (a clip plays in its preview) — or ◎ the
        /// catalog when the row stands for no asset.</summary>
        public static StateTreePickField WithRowButtons(this StateTreePickField field, StateTreeRegistryEntry row,
            StateTreeRegistryAsset catalog)
        {
            UnityEngine.Object asset = AssetOf(row);
            if (asset == null)
                return field.WithPing(catalog, catalog != null ? "the catalog " + catalog.name : null);
            field.WithPing(asset);
            return field.WithButton("▶", "Open " + asset.name, () => AssetDatabase.OpenAsset(asset));
        }
    }
}
