using System;
using System.Collections.Generic;
using UnityEngine;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// One level's place in the PROJECT CATALOG (M16): identity — the id typed references
    /// wire to (a portal's target, the session tree's level states, a save file) — and ONE
    /// field, the <see cref="LevelContent"/> asset that IS the level. Everything a level
    /// consists of (scene, entry params, tag vocabulary, object manifest) lives in that file,
    /// so the catalog stays a list of names no matter how big the levels get.
    ///
    /// The properties below read through to the content: callers say <c>level.scenePath</c>
    /// and never care which asset stores it.
    /// </summary>
    [Serializable]
    public sealed class LevelDef : StateTreeRegistryEntry
    {
        /// <summary>THE level — see <see cref="LevelContent"/>. A row without one is an
        /// unfinished catalog entry: it names a level that has no content yet, and the load
        /// that tries it says so.</summary>
        public LevelContent content;

        public string scenePath => content != null ? content.scenePath : "";

        public List<GraphTaskParameter> parameters =>
            content != null ? content.parameters : null;

        public LevelObjectRegistry objects => content != null ? content.objects : null;

        /// <summary>The tag vocabularies this level speaks — listed on its manifest, see
        /// <see cref="LevelObjectRegistry.tags"/>. Empty when the level has no manifest.</summary>
        public IReadOnlyList<WorldTagRegistry> tags => content != null && content.objects != null
            ? content.objects.tags
            : System.Array.Empty<WorldTagRegistry>();

        public string Label => content != null && !string.IsNullOrEmpty(content.displayName)
            ? content.displayName
            : name;

        /// <summary>What lives in this level, derived from its manifest — see
        /// <see cref="LevelContent.CollectTags"/>.</summary>
        public void CollectTags(List<string> into)
        {
            if (content != null)
                content.CollectTags(into);
        }
    }

    /// <summary>One placed object of a level, as a REGISTRY ROW (it lives in the level's
    /// <see cref="LevelObjectRegistry"/>, so it is searchable and groupable like every other
    /// entry): the base carries its id, its own name — the placement's handle, which is what
    /// the spawned view is called — and its group. Then what it is (<see cref="kind"/> — a row
    /// of the project's <see cref="LevelObjectKindRegistry"/>, picked not typed), which
    /// definition row it is an instance of, where it stands and which placement tags it
    /// carries. Everything a placement IS lives in its definition row — every kind has a
    /// table, so nothing needs a per-placement bag of loose settings.</summary>
    [Serializable]
    public sealed class LevelObjectDef : StateTreeRegistryEntry
    {
        /// <summary>What to spawn — a kind row of the project's
        /// <see cref="LevelObjectKindRegistry"/> (see <see cref="LevelRegistry.kinds"/>),
        /// id-wired like every other typed reference. The game's spawner maps the kind to a
        /// view.</summary>
        public StateTreeEntryRef<LevelObjectKindDef> kind =
            new StateTreeEntryRef<LevelObjectKindDef>();

        /// <summary>The definition row this object is an instance OF (unit row, item row) —
        /// id-wired, and picked from the registry the <see cref="kind"/>'s row names in
        /// <see cref="LevelObjectKindDef.definitions"/>.</summary>
        public LevelObjectEntryRef entry = new LevelObjectEntryRef();

        /// <summary>The definition row's name — the spawner's question, unchanged by the
        /// reference moving into <see cref="entry"/>.</summary>
        public string entryName => entry != null ? entry.entryName : "";

        /// <summary>
        /// The MIND this placement gets — the tree the spawned object runs, or null to take
        /// whatever the spawner's default for the kind is.
        ///
        /// WHY IT IS PER PLACEMENT. A spawner that hard-codes one tree per kind gives every
        /// unit in the level the same behaviour, so "the keeper who has a second conversation"
        /// and "the guard who patrols" have to become different KINDS — a project-wide concept
        /// invented to express a per-placement difference. A tree here says it where it belongs:
        /// this one, in this level, behaves like that.
        ///
        /// Null is the ordinary case and stays cheap: the spawner keeps its default, so a level
        /// full of identical grunts names nothing.
        /// </summary>
        [StateTreePick]
        public StateTreeAsset tree;

        /// <summary>
        /// The ARGUMENTS of <see cref="tree"/> — this placement's values for the parameters
        /// the tree declares (<see cref="StateTreeAsset.parameters"/>), as the same id-bound
        /// override rows every other caller of a tree uses
        /// (<see cref="StateTreeContextHost.parameterOverrides"/>, <c>RunSubTreeTask</c>).
        /// The spawner copies them onto the spawned object's host, and the executor seeds the
        /// effective values into the blackboard under the parameters' names before anything
        /// ticks — so a task reads a plain key and never knows who supplied it.
        ///
        /// This is what makes a per-placement tree more than a per-placement COPY: one "exit"
        /// tree serves every way out because each row hands it a different destination, the
        /// Blueprint instance model the M7h rows exist for. Empty is the ordinary case: the
        /// tree's declared defaults stand.
        /// </summary>
        public List<GraphTaskParameterOverride> parameterOverrides =
            new List<GraphTaskParameterOverride>();

        public Vector2 position;

        /// <summary>
        /// Which way it faces, in degrees about the up axis.
        ///
        /// A 2D level does not need this and leaves it at zero. A 3D one does: an NPC placed
        /// facing away from the path the player walks in on reads as scenery, and "turn him
        /// round" is the most ordinary edit there is. It is one float on the row rather than a
        /// rotation the spawner guesses, because guessing is wrong exactly when it matters.
        /// </summary>
        public float facing;

        /// <summary>Placement-only tags this instance carries — where a level's own
        /// vocabulary (see <see cref="LevelContent.tags"/>) lands on an object. Picked from
        /// that vocabulary, never typed: see <see cref="LevelObjectTagRef"/>.</summary>
        public List<LevelObjectTagRef> tags = new List<LevelObjectTagRef>();

    }

    /// <summary>The catalog of levels — a registry kind like any other: list it in a tree's
    /// Data section and level states pick their level with ⛃; the dev picker overlay lists
    /// it; the dashboard edits it. It also carries the project's spawnable
    /// <see cref="kinds"/>, so every level's manifest picks from one known list.</summary>
    [CreateAssetMenu(menuName = "Draw To Play/Levels/Level Registry", fileName = "LevelRegistry")]
    public sealed class LevelRegistry : StateTreeRegistry<LevelDef>
    {
        /// <summary>The known list of object kinds a level manifest may use — project-wide,
        /// because a kind is a project definition (what the spawner can build), not a
        /// per-level one.</summary>
        public LevelObjectKindRegistry kinds;
    }
}
