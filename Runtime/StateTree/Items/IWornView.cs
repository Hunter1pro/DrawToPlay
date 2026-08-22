namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// A VIEW ON THE BODY OF WHAT IS WORN IN ONE SLOT — a hand, a back, a head. The bag finds
    /// these on the body that binds to it and TELLS them what is worn, now and after every
    /// equip or take-off (meta-rule 1); a view never asks the bag and never polls.
    /// </summary>
    public interface IWornView
    {
        /// <summary>The slot this view shows.</summary>
        string slotId { get; }

        /// <summary>Show this row, or nothing when null.</summary>
        void Show(ItemDef worn);
    }
}
