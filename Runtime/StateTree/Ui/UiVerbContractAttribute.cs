using System;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// A view's VERB, DECLARED (§4g) — the TaskOutputContract twin for skins: what
    /// <see cref="UiViewBehaviour.Call"/> answers to, readable by tools (the def inspector's
    /// screen surface, reaction pickers) without running the view.
    ///
    /// It is also what makes an unanswered call legible: a beat aimed at a shown row whose
    /// skins speak none of it is reported with the vocabulary they DO speak, so a typo reads
    /// as a typo instead of as a UI that quietly does nothing.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = true)]
    public sealed class UiVerbContractAttribute : Attribute
    {
        public readonly string verb;

        /// <summary>What the argument or payload means — "item name", "ItemUseResult".</summary>
        public readonly string argumentHint;

        public UiVerbContractAttribute(string verb, string argumentHint = "")
        {
            this.verb = verb;
            this.argumentHint = argumentHint;
        }
    }
}
