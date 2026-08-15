using System;
using UnityEngine;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// ONE LINE OF THE BALANCE SHEET (M23 progression) — an attribute and its LEVEL → VALUE
    /// curve, the Unreal CurveTable idea in this toolset's shape: the attribute is a picked
    /// row (the table lists the attribute registry in dependsOn), the curve is the meaning.
    /// "An enemy at level 5" has hit points because THIS row says what health IS at 5 — one
    /// authored page instead of numbers scattered across seeds, defaults and prefab fields.
    ///
    /// Being a registry row, it can itself be PICKED: an effect that scales with its source's
    /// level names one of these (<see cref="EffectDef.scaleByLevel"/>) — the ScalableFloat
    /// half of the same idea, through the same provenance chain as every other reference.
    /// </summary>
    [Serializable]
    public sealed class ProgressionRow : StateTreeRegistryEntry
    {
        [Tooltip("Which attribute this curve gives meaning to — a picked row of the "
            + "attribute registry this table depends on.")]
        public StateTreeEntryRef<AttributeDef> attribute = new StateTreeEntryRef<AttributeDef>();

        [Tooltip("The value at each level. Keys at whole levels read as a table; the curve "
            + "between them is how in-between levels interpolate.")]
        public AnimationCurve valueByLevel = AnimationCurve.Linear(1f, 100f, 10f, 300f);

        [Tooltip("Round the evaluated value — 40 hit points, not 39.7. Off for attributes "
            + "that are genuinely continuous (a speed multiplier).")]
        public bool wholeNumbers = true;

        /// <summary>The curve read every consumer uses, rounding applied. PAST THE LAST
        /// KEY the curve clamps — level 50 of a sheet that ends at 10 answers with the
        /// level-10 value, which is why the span below is readable: the silence would
        /// otherwise be the only signal.</summary>
        public float Evaluate(int level)
        {
            if (valueByLevel == null || valueByLevel.length == 0)
                return 0f;
            float value = valueByLevel.Evaluate(level);
            return wholeNumbers ? Mathf.Round(value) : value;
        }

        /// <summary>The first level the curve speaks for.</summary>
        public int firstLevel => valueByLevel != null && valueByLevel.length > 0
            ? Mathf.RoundToInt(valueByLevel[0].time) : 0;

        /// <summary>The last level the curve speaks for — beyond it, values hold flat.</summary>
        public int lastLevel => valueByLevel != null && valueByLevel.length > 0
            ? Mathf.RoundToInt(valueByLevel[valueByLevel.length - 1].time) : 0;

        public override string Describe()
        {
            if (valueByLevel == null || valueByLevel.length == 0)
                return "no curve — always 0";
            return "levels " + firstLevel + "–" + lastLevel + " · "
                + Evaluate(firstLevel).ToString("0.##") + " → "
                + Evaluate(lastLevel).ToString("0.##")
                + " (holds past " + lastLevel + ")";
        }
    }
}
