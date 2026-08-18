using System;
using UnityEngine;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>What SHAPE a declared value has — the four answers this toolset can act on.</summary>
    public enum StateTreeValueKind
    {
        /// <summary>A number, a word, a flag — the primitives the blackboard already holds.</summary>
        Primitive = 0,

        /// <summary>A ROW of a named registry: an item, an ability, a level, a cutscene. Rides as
        /// the row's NAME, which is what every runtime lookup already uses.</summary>
        Row = 1,

        /// <summary>A serialized payload class — what an announcement carries.</summary>
        Payload = 2,

        /// <summary>Something that KEEPS A CONTRACT (M30.2) — "anything damageable", rather than
        /// "a row of that one catalog". Rides as the name of the row that defines it, exactly as
        /// a Row does, because that is how every reference in this toolset travels.</summary>
        Object = 3
    }

    /// <summary>
    /// ONE TYPE MODEL for everything an author declares (M30.1) — tree keys and graph parameters
    /// alike, which until now were two vocabularies of different richness: a key could mean seven
    /// things, a graph parameter could mean three, and neither could say "an item row".
    ///
    /// THE RUNTIME DOES NOT CHANGE, and that is the point of the design. A richer type is an
    /// AUTHORING refinement of a primitive that is already stored: a Row rides in the string that
    /// held the typed name, a Payload rides in the object slot the announcement already used. So a
    /// value gains a picker, a validity rule and a place in the dependency map without a single
    /// blackboard read moving — <see cref="Storage"/> is the whole compatibility story.
    ///
    /// WHERE THE CHOICES COME FROM is the other half: a Row type names the REGISTRY whose rows it
    /// means, and that registry has to be one this asset already declares (dependsOn). "Available
    /// from linked dependencies, not global" is the rule the row pickers have always followed, and
    /// this is what lets parameters follow it too.
    /// </summary>
    [Serializable]
    public sealed class StateTreeValueType
    {
        [Tooltip("What shape this value has.")]
        public StateTreeValueKind kind = StateTreeValueKind.Primitive;

        [Tooltip("Primitive: which one. Also the STORAGE for the richer kinds — see Storage.")]
        public StateTreeKeyKind primitive = StateTreeKeyKind.Float;

        [Tooltip("Row: whose rows. Must be a registry this asset declares in Depends On, so the "
            + "offer is the declared neighbourhood rather than everything in the project.")]
        public StateTreeRegistryAsset rows;

        [Tooltip("Payload: the serialized class this value carries, by full type name — the same "
            + "string an announcement already declares.")]
        public string payloadTypeName = "";

        [Tooltip("Object: the contract the object must satisfy (M30.2). Empty means any object.")]
        public string contract = "";

        /// <summary>
        /// WHICH PRIMITIVE THIS ACTUALLY RIDES IN — the reason nothing at runtime has to learn the
        /// new vocabulary. A Row is its name (a string), a Payload and an Object are objects, and a
        /// Primitive is itself.
        /// </summary>
        public StateTreeKeyKind Storage
        {
            get
            {
                switch (kind)
                {
                    case StateTreeValueKind.Row: return StateTreeKeyKind.String;
                    case StateTreeValueKind.Payload: return StateTreeKeyKind.Object;
                    // A CONTRACT-TYPED VALUE IS A NAME, like a row: what changes is which names
                    // are offered — anything that keeps the promise, from any declared catalog,
                    // instead of one catalog's rows. A key that holds a live object is a
                    // different thing and already has a primitive of its own.
                    case StateTreeValueKind.Object: return StateTreeKeyKind.String;
                    default: return primitive;
                }
            }
        }

        /// <summary>True when this says no more than the primitive it rides in — the state every
        /// value declared before M30.1 is in, and the one a migration leaves alone.</summary>
        public bool IsPlain => kind == StateTreeValueKind.Primitive;

        /// <summary>What an author reads in a list: "item row", "float", "CutsceneResult".</summary>
        public string Describe()
        {
            switch (kind)
            {
                case StateTreeValueKind.Row:
                    return rows != null ? rows.name + " row" : "row (no registry named)";
                case StateTreeValueKind.Payload:
                    return string.IsNullOrEmpty(payloadTypeName)
                        ? "payload (no type named)"
                        : ShortName(payloadTypeName);
                case StateTreeValueKind.Object:
                    return string.IsNullOrEmpty(contract) ? "object" : "keeps " + contract;
                default:
                    return primitive.ToString().ToLowerInvariant();
            }
        }

        /// <summary>A type that means the same thing as the primitive it is given — how anything
        /// declared before this existed is read.</summary>
        public static StateTreeValueType Of(StateTreeKeyKind primitive)
        {
            return new StateTreeValueType
            {
                kind = StateTreeValueKind.Primitive,
                primitive = primitive
            };
        }

        /// <summary>Rows of a registry.</summary>
        public static StateTreeValueType RowsOf(StateTreeRegistryAsset registry)
        {
            return new StateTreeValueType
            {
                kind = StateTreeValueKind.Row,
                primitive = StateTreeKeyKind.String,
                rows = registry
            };
        }

        /// <summary>Anything that keeps a contract — the type that asks by promise instead of by
        /// catalog, and the reason a field can accept a kind of thing nobody had invented when the
        /// field was written.</summary>
        public static StateTreeValueType Keeping(string contract)
        {
            return new StateTreeValueType
            {
                kind = StateTreeValueKind.Object,
                primitive = StateTreeKeyKind.String,
                contract = contract ?? ""
            };
        }

        private static string ShortName(string typeName)
        {
            int comma = typeName.IndexOf(',');
            string bare = comma > 0 ? typeName.Substring(0, comma) : typeName;
            int dot = bare.LastIndexOf('.');
            return dot >= 0 ? bare.Substring(dot + 1) : bare;
        }
    }
}
