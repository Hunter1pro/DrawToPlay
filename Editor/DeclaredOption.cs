using System;
using System.Collections.Generic;

namespace PowerOfFire.DrawToPlay.Editor
{
    /// <summary>What kind of value an option holds — what the panel draws for it.</summary>
    internal enum DeclaredOptionKind { Float, Int, Bool, Enum, String, Tag }

    /// <summary>
    /// ONE DECLARED KNOB, as the panel sees it (M36.2) — whoever declares it, whatever layer
    /// overrides it. A kind's attribute with the body's seed as its fallback, a service's
    /// setting with the attribute's default, an install's override with the def's value: the
    /// panel does not know which, and that is the point of having one panel.
    /// </summary>
    internal sealed class DeclaredOption
    {
        public string name = "";
        public string description = "";
        public DeclaredOptionKind kind = DeclaredOptionKind.Float;

        /// <summary>The enum type, for <see cref="DeclaredOptionKind.Enum"/>.</summary>
        public Type enumType;

        /// <summary>What the option is worth when this layer does not override it — the layer
        /// below's value. Null means the layer below has nothing to say either, and the panel
        /// writes <see cref="fallbackLabel"/> instead of a number it cannot stand behind.</summary>
        public object fallback;

        /// <summary>Shown dimmed when <see cref="fallback"/> is null.</summary>
        public string fallbackLabel = "—";

        /// <summary>What a Tag option may be set to — the vocabulary its owner declares.
        /// Asked lazily, because the offers are an asset walk.</summary>
        public Func<List<WorldTagDef>> tagOffers;

        public static DeclaredOptionKind KindOf(Type type, bool isTag)
        {
            if (isTag)
                return DeclaredOptionKind.Tag;
            if (type == typeof(float))
                return DeclaredOptionKind.Float;
            if (type == typeof(int))
                return DeclaredOptionKind.Int;
            if (type == typeof(bool))
                return DeclaredOptionKind.Bool;
            if (type != null && type.IsEnum)
                return DeclaredOptionKind.Enum;
            return DeclaredOptionKind.String;
        }
    }

    /// <summary>
    /// HOW ONE LAYER'S ROWS ARE SHAPED — which serialized field holds the name, the number, the
    /// text, the picked row's id. A placement attribute is (attribute, value); a service setting
    /// is (name, floatValue, stringValue, entryId). The panel reads both through this.
    /// </summary>
    internal sealed class DeclaredOptionRowShape
    {
        public string nameField = "name";
        public string floatField = "floatValue";
        public string stringField = "stringValue";
        public string idField = "entryId";

        public static readonly DeclaredOptionRowShape PlacementAttribute =
            new DeclaredOptionRowShape
            {
                nameField = "attribute", floatField = "value", stringField = null, idField = null
            };

        public static readonly DeclaredOptionRowShape ServiceSetting =
            new DeclaredOptionRowShape();
    }
}
