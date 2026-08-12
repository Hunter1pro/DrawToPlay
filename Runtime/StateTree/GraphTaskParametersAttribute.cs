using System;
using UnityEngine;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// Marks a <see cref="GraphTaskParameterSet"/> field and names the sibling field holding the
    /// <see cref="GraphTaskAsset"/> whose parameters it configures:
    ///
    /// <code>
    /// public GraphTaskAsset program;
    ///
    /// [GraphTaskParameters(nameof(program))]
    /// public GraphTaskParameterSet parameters = new GraphTaskParameterSet();
    /// </code>
    ///
    /// The pairing is DECLARED rather than guessed because a row may hold more than one program
    /// one day, and a set that silently attached itself to the wrong one would show the right rows
    /// against the wrong knobs — the failure nobody would look for.
    ///
    /// <c>nameof</c> keeps the string honest through a rename; the drawer reports a name that
    /// resolves to nothing rather than drawing an empty section that looks like "no parameters".
    /// </summary>
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class GraphTaskParametersAttribute : PropertyAttribute
    {
        /// <summary>Name of the sibling field holding the program. Serialized-property name, so
        /// the field must be one Unity serializes.</summary>
        public readonly string programField;

        /// <param name="programField">The sibling field holding the
        /// <see cref="GraphTaskAsset"/> — pass it with <c>nameof</c>.</param>
        public GraphTaskParametersAttribute(string programField)
        {
            this.programField = programField;
        }
    }
}
