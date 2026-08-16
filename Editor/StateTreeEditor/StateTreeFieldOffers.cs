using System;
using System.Collections.Generic;
using System.Reflection;

namespace PowerOfFire.DrawToPlay.Editor
{
    /// <summary>
    /// WHAT A PLAIN STRING FIELD MAY SAY — the one place both authoring surfaces ask.
    ///
    /// Typed references have had pickers since M13; a bare string never did, so a value
    /// that IS a known list (a subsystem request, a dialog result, whatever the next game
    /// declares) was retyped from memory in the tree inspector and on the graph canvas
    /// alike. This is the registry of sources that can answer "what may this field hold?",
    /// consulted by the state-tree inspector AND by the graph's port builder, so a
    /// vocabulary registered once shows up in both.
    ///
    /// A game's own vocabulary registers here (the demo's dialog results do); the core
    /// registers what the core knows (subsystem requests). A field nobody claims stays the
    /// text box it always was, which is the honest default for a name nobody has declared.
    /// </summary>
    public static class StateTreeFieldOffers
    {
        /// <summary>One source: given a field, the values it may hold — or null when this
        /// source has nothing to say about it. First non-empty answer wins.</summary>
        public static event Func<FieldInfo, List<string>> sources;

        /// <summary>The offers for a field, or null when nobody claims it.</summary>
        public static List<string> For(FieldInfo field)
        {
            if (field == null)
                return null;
            Func<FieldInfo, List<string>> registered = sources;
            if (registered == null)
                return null;
            foreach (Delegate source in registered.GetInvocationList())
            {
                var offered = ((Func<FieldInfo, List<string>>)source)(field);
                if (offered != null && offered.Count > 0)
                    return offered;
            }
            return null;
        }

        /// <summary>The offers for a named field of an object, or null — the inspector's
        /// question, which knows a target and a path rather than a FieldInfo.</summary>
        public static List<string> For(object target, string fieldName)
        {
            if (target == null || string.IsNullOrEmpty(fieldName))
                return null;
            FieldInfo field = target.GetType().GetField(fieldName,
                BindingFlags.Public | BindingFlags.Instance);
            return field != null && field.FieldType == typeof(string) ? For(field) : null;
        }
    }
}
