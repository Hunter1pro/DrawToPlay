using System;
using System.Collections.Generic;
using UnityEngine;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// A ZONE, DECLARED ONCE (the HT container, as a row): its stack is an ORDERED LIST of
    /// picked objective rows — adding the next task to a zone is appending to the list, no
    /// chain wiring — and its place in the world is a manifest placement that picks THIS
    /// row as its entry (the placer pattern), tagging the volume with the row's id so the
    /// orchestrator and every MoveTo find it. The row is the one authored source: the
    /// stack's order, the screen title, and the identity the world speaks.
    /// </summary>
    [Serializable]
    public sealed class ZoneDef : StateTreeRegistryEntry
    {
        [Tooltip("The zone itself — ONE ASSET holding its objectives as its own rows, in "
            + "order, fully editable in the registry dashboard. This row is only the "
            + "identity: what the placer picks and what the world tag says.")]
        public ZoneAsset asset;

        public override string Describe()
        {
            if (asset == null)
                return "no zone asset — never competes";
            if (asset.entries.Count == 0)
                return "'" + asset.name + "' is empty — never competes";
            var line = new System.Text.StringBuilder("'" + asset.name + "': ");
            for (int i = 0; i < asset.entries.Count; i++)
            {
                if (i > 0)
                    line.Append(" → ");
                line.Append(asset.entries[i] != null ? asset.entries[i].name : "?");
            }
            return line.ToString();
        }
    }
}
