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
        [Tooltip("The zone's title on screen while its stack is asked.")]
        public string displayName = "";

        [Tooltip("The stack, IN ORDER — picked objective rows. The list is the chain: "
            + "completing one asks the next, and per-row nextOnComplete is ignored inside "
            + "a stack. Add the next task by appending a row.")]
        public List<StateTreeEntryRef<ObjectiveDef>> stack =
            new List<StateTreeEntryRef<ObjectiveDef>>();

        public override string Describe()
        {
            if (stack.Count == 0)
                return "empty zone — never competes";
            var line = new System.Text.StringBuilder("stack: ");
            for (int i = 0; i < stack.Count; i++)
            {
                if (i > 0)
                    line.Append(" → ");
                line.Append(stack[i] != null ? stack[i].entryName : "?");
            }
            return line.ToString();
        }
    }
}
