using System;
using System.Collections.Generic;
using UnityEngine;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>A fact an object of a preset writes when it is used — a key on the level board, and what it
    /// means to the person picking it ("Opened"). The first one a preset lists is its primary fact.</summary>
    [Serializable]
    public sealed class LevelObjectPresetFact
    {
        [Tooltip("The key the object writes — its placement id, on the level board.")]
        [StateTreeKey(StateTreeKeyKind.String, any: true)]
        public StateTreeKeyField key = new StateTreeKeyField();

        [Tooltip("What the fact means, in the words a picker shows (\"Opened\").")]
        [HumanText]
        public string meaning = "";

        /// <summary>The meaning, else the key.</summary>
        public string Label => string.IsNullOrEmpty(meaning) ? key.text : meaning;
    }

    /// <summary>
    /// A LEVEL OBJECT PRESET — the same model as a step's template: a CORE every object made from it shares
    /// (its kind, the tree it runs and that tree's arguments, its tags, the facts it writes and the steps that
    /// fit it) and a few SETTINGS (parameters of that tree) each object gives its own value.
    ///
    /// Objects reference the preset, so an edit to the core reaches every object made from it. An object keeps
    /// its own values for the settings only; one it never touched follows the preset's default. Its name, place,
    /// facing, definition row and attributes stay its own.
    /// </summary>
    [Serializable]
    public sealed class LevelObjectPresetDef : StateTreeRegistryEntry
    {
        [Tooltip("The kind an object of this preset is — the palette lists the preset under it.")]
        public StateTreeEntryRef<LevelObjectKindDef> kind = new StateTreeEntryRef<LevelObjectKindDef>();

        [TextArea]
        public string description = "";

        [Tooltip("The tree every object of this preset runs.")]
        [StateTreePick]
        public StateTreeAsset tree;

        [Tooltip("The tree's arguments — the core's values, and the defaults of the settings.")]
        [GraphTaskParameters(nameof(tree))]
        public GraphTaskParameterSet parameters = new GraphTaskParameterSet();

        [Tooltip("Which of the tree's parameters each object sets for itself.")]
        [PresetSettings(nameof(tree))]
        public List<string> settings = new List<string>();

        [Tooltip("The tags every object of this preset carries.")]
        public List<LevelObjectTagRef> tags = new List<LevelObjectTagRef>();

        [Tooltip("What an object of this preset writes when it is used; the first is its primary fact.")]
        public List<LevelObjectPresetFact> facts = new List<LevelObjectPresetFact>();

        [Tooltip("The mission steps an object of this preset fits, the first preferred.")]
        [StepKind]
        public List<string> steps = new List<string>();

        public override string Describe() => description;

        /// <summary>Whether the parameter with this id is a setting — a value each object gives itself.</summary>
        public bool IsSetting(string parameterId)
        {
            return !string.IsNullOrEmpty(parameterId) && settings != null && settings.Contains(parameterId);
        }

        /// <summary>The first fact, or null.</summary>
        public LevelObjectPresetFact primaryFact => facts != null && facts.Count > 0 ? facts[0] : null;

        /// <summary>
        /// The row as it runs: a copy wearing this preset's kind, tree and tags, with the tree's arguments the
        /// core's — then the row's own value for each setting it set. The row itself is never changed.
        /// </summary>
        public LevelObjectDef Apply(LevelObjectDef row)
        {
            if (row == null)
                return null;
            LevelObjectDef made = row.Copy();
            if (kind != null && !string.IsNullOrEmpty(kind.entryName))
                made.kind = new StateTreeEntryRef<LevelObjectKindDef> { entryId = kind.entryId, entryName = kind.entryName };
            made.tree = tree;
            made.parameters = Arguments(row);
            made.tags = new List<LevelObjectTagRef>();
            for (int i = 0; tags != null && i < tags.Count; i++)
            {
                if (tags[i] != null)
                    made.tags.Add(new LevelObjectTagRef { tag = tags[i].tag, tagId = tags[i].tagId });
            }
            return made;
        }

        /// <summary>The core's arguments, then each setting the row set — copied, never aliased.</summary>
        public GraphTaskParameterSet Arguments(LevelObjectDef row)
        {
            var set = new GraphTaskParameterSet();
            for (int i = 0; parameters != null && parameters.values != null && i < parameters.values.Count; i++)
            {
                GraphTaskParameterOverride core = parameters.values[i];
                if (core != null && core.enabled)
                    set.values.Add(Copy(core));
            }
            List<GraphTaskParameterOverride> own = row != null && row.parameters != null ? row.parameters.values : null;
            for (int i = 0; own != null && i < own.Count; i++)
            {
                GraphTaskParameterOverride value = own[i];
                if (value == null || !value.enabled || !IsSetting(value.id))
                    continue;
                set.values.RemoveAll(held => held.id == value.id);
                set.values.Add(Copy(value));
            }
            return set;
        }

        /// <summary>A copy of this preset under another id and name — what "a new preset from this" starts from.</summary>
        public LevelObjectPresetDef Copy(string newId, string newName)
        {
            var made = new LevelObjectPresetDef
            {
                id = newId ?? "",
                name = newName ?? "",
                group = group,
                kind = new StateTreeEntryRef<LevelObjectKindDef> { entryId = kind.entryId, entryName = kind.entryName },
                description = description,
                tree = tree,
                settings = new List<string>(settings),
                steps = new List<string>(steps)
            };
            for (int i = 0; parameters != null && i < parameters.values.Count; i++)
            {
                if (parameters.values[i] != null)
                    made.parameters.values.Add(Copy(parameters.values[i]));
            }
            foreach (LevelObjectTagRef tag in tags)
            {
                if (tag != null)
                    made.tags.Add(new LevelObjectTagRef { tag = tag.tag, tagId = tag.tagId });
            }
            foreach (LevelObjectPresetFact fact in facts)
            {
                if (fact != null)
                    made.facts.Add(new LevelObjectPresetFact
                    {
                        key = new StateTreeKeyField(fact.key.text) { keyId = fact.key.keyId },
                        meaning = fact.meaning
                    });
            }
            return made;
        }

        public static GraphTaskParameterOverride Copy(GraphTaskParameterOverride value)
        {
            return new GraphTaskParameterOverride
            {
                name = value.name,
                enabled = value.enabled,
                floatValue = value.floatValue,
                stringValue = value.stringValue,
                id = value.id,
                keyId = value.keyId,
                sourceParameterId = value.sourceParameterId,
                entryId = value.entryId
            };
        }
    }

    /// <summary>
    /// The game's ONE catalogue of level object presets. Every level's manifest lists it
    /// (<see cref="LevelObjectRegistry.presets"/>), so every level's palette shows the same presets under
    /// their kinds, and an object anywhere names its preset by id.
    /// </summary>
    [CreateAssetMenu(menuName = "Draw To Play/Levels/Level Object Presets", fileName = "LevelObjectPresets")]
    public sealed class LevelObjectPresetRegistry : StateTreeRegistry<LevelObjectPresetDef>
    {
        [Tooltip("The tag vocabularies the presets' tags are picked from.")]
        public List<WorldTagRegistry> tags = new List<WorldTagRegistry>();
    }
}
