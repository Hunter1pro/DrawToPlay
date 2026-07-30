using System;
using System.Collections.Generic;
using UnityEngine;

namespace PowerOfFire.DrawToPlay.Editor
{
    /// <summary>
    /// One tab of a creation flow (draw-tool-port-brief.md §6): a name, the tool it puts you
    /// in, and the checklist that says what "done" looks like. Stages are pure data — the
    /// window never special-cases a stage by type, only by <see cref="id"/> (which is what a
    /// <see cref="FlowValidators"/> entry keys off).
    /// </summary>
    [Serializable]
    public sealed class FlowStage
    {
        /// <summary>Stable identifier, e.g. "terrain.sculpt". Keys the validator registry and
        /// any stage-specific window behaviour; renaming it silently drops the badge to
        /// neutral rather than breaking anything.</summary>
        public string id;

        /// <summary>Tab caption.</summary>
        public string title;

        /// <summary>One or two sentences describing what this stage is for.</summary>
        [TextArea(2, 6)]
        public string description;

        /// <summary>Full type name of the <c>EditorTool</c> this stage activates
        /// (e.g. "PowerOfFire.DrawToPlay.Editor.DrawShapeTool"). Empty = the stage changes no
        /// tool, it is just a checklist + overlay context.</summary>
        public string toolTypeName;

        /// <summary>Hint lines shown under the description. Purely advisory — §6: "Nothing is
        /// modal ... badges just tell the truth."</summary>
        public List<string> checklist = new List<string>();
    }

    /// <summary>
    /// A creation flow: the ordered stage tabs shown across the top of the
    /// <see cref="FlowWindow"/>. Flows are data-driven per draw-tool-port-brief.md §6 —
    /// adding a content type (character, enemy, prop, terrain) is a new FlowDefinition asset,
    /// not new window code. Editor-only: it lives in the editor assembly and is never
    /// referenced by runtime content.
    /// </summary>
    [CreateAssetMenu(fileName = "Flow", menuName = "Draw To Play/Flow Definition")]
    public sealed class FlowDefinition : ScriptableObject
    {
        /// <summary>Display name of the flow ("Terrain", "Character", ...).</summary>
        public string flowName = "Flow";

        /// <summary>Stage tabs, left to right. Order is authoring order, never an enforced
        /// sequence — the user can work out of order at any time.</summary>
        public List<FlowStage> stages = new List<FlowStage>();

        /// <summary>Bounds-checked stage lookup; null when the index is out of range or the
        /// list has a hole in it.</summary>
        public FlowStage GetStage(int index)
        {
            if (stages == null || index < 0 || index >= stages.Count)
                return null;
            return stages[index];
        }

        /// <summary>Number of stage tabs, tolerant of a null list.</summary>
        public int stageCount => stages != null ? stages.Count : 0;
    }
}
