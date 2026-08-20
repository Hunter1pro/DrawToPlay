using Unity.Properties;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// ONE CELL'S SENTENCE (M34) — what a bag cell says, as a thing to bind to.
    ///
    /// The bag used to rebuild every cell and every button on every change: spend one ration and
    /// the whole grid was destroyed and remade, buttons included. A cell that lives as long as
    /// the item is carried can bind its count instead, so a quantity change writes one string
    /// and nothing else moves — which is also what stops a press landing on an element that was
    /// replaced underneath the finger.
    ///
    /// [CreateProperty] because these are PROPERTIES: UI Toolkit picks up fields by itself and
    /// silently ignores unmarked properties (the lesson from the quest banner).
    /// </summary>
    public sealed class BagCellModel
    {
        /// <summary>The item's name, as the cell shows it.</summary>
        [CreateProperty]
        public string label { get; internal set; } = "";

        /// <summary>"x3", or empty for a single.</summary>
        [CreateProperty]
        public string count { get; internal set; } = "";

        /// <summary>What its one button offers right now — "use", "wear", "worn ✓" — which
        /// changes without the cell being rebuilt.</summary>
        [CreateProperty]
        public string verb { get; internal set; } = "";
    }

    /// <summary>One equipment line's sentence — "Hand:  Iron Sword" — and the caption of the
    /// button beside it, which changes with what is worn.</summary>
    public sealed class BagSlotModel
    {
        [CreateProperty]
        public string line { get; internal set; } = "";

        [CreateProperty]
        public string verb { get; internal set; } = "";
    }
}
