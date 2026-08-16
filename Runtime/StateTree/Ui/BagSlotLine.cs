namespace PowerOfFire.DrawToPlay
{
    /// <summary>One slot's line as DATA — what the bag is HANDED to draw (the UI wiring
    /// brief: the flow's redraw task builds these from the domain; the view never asks).</summary>
    public readonly struct BagSlotLine
    {
        /// <summary>The slot row's id — how the domain addresses the slot.</summary>
        public readonly string slotId;

        /// <summary>The slot row's registry NAME — what a typed take-off request sends
        /// (§4d: request values name rows).</summary>
        public readonly string slotName;

        public readonly string slotLabel;

        /// <summary>The worn item's registry name, or empty for an open slot.</summary>
        public readonly string wornItemName;

        /// <summary>The worn item's human label, or empty.</summary>
        public readonly string wornItemLabel;

        public BagSlotLine(string slotId, string slotName, string slotLabel,
            string wornItemName, string wornItemLabel)
        {
            this.slotId = slotId;
            this.slotName = slotName;
            this.slotLabel = slotLabel;
            this.wornItemName = wornItemName;
            this.wornItemLabel = wornItemLabel;
        }
    }
}
