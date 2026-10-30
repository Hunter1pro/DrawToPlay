using System.Collections.Generic;
using UnityEngine;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// ONE LEVEL'S OBJECTS, as a registry in its own file — the same asset kind, dashboard and
    /// discipline every other entry list gets: search across every field, group sections
    /// ("enemies", "props", "doors"), ids minted on add and never edited, renames safe because
    /// references are id-wired.
    ///
    /// A level's <see cref="LevelContent.objects"/> points at one of these. Separate file
    /// because a level's object list is the part that GROWS: hundreds of rows belong in their
    /// own asset, not inline in the level header, and two people can edit two levels'
    /// placements without meeting in the same file.
    /// </summary>
    [CreateAssetMenu(menuName = "Draw To Play/Levels/Level Objects",
        fileName = "LevelObjects")]
    public sealed class LevelObjectRegistry : StateTreeRegistry<LevelObjectDef>, IStateTreeNeighbourhood
    {
        /// <summary>
        /// The tag vocabularies these placements may use — the project's global registry and
        /// whatever else this level speaks, LISTED HERE. A placement's tag picker reads
        /// exactly this list: no project scan, no walk from level to catalog to tree, no
        /// guessing which registry is "global". If a vocabulary is not in this list, it is
        /// not offered — which is what makes the list worth keeping honest.
        /// </summary>
        public List<WorldTagRegistry> tags = new List<WorldTagRegistry>();

        /// <summary>Which two axes this level's positions are drawn on — see
        /// <see cref="LevelGroundPlane"/>. On the manifest rather than on the level header
        /// because the manifest IS the positions, and this says what they mean.</summary>
        public LevelGroundPlane plane = LevelGroundPlane.XY;

        /// <summary>The game's preset catalogue — what this level's palette offers under each kind, and where
        /// a row's <see cref="LevelObjectDef.preset"/> is found. Listed here, like <see cref="tags"/>: the level
        /// says what it speaks.</summary>
        [Tooltip("The game's catalogue of level object presets: offered in the palette, and what a row's preset names.")]
        public LevelObjectPresetRegistry presets;

        IReadOnlyList<StateTreeRegistryAsset> IStateTreeNeighbourhood.DeclaredCatalogs =>
            presets != null ? new StateTreeRegistryAsset[] { presets } : System.Array.Empty<StateTreeRegistryAsset>();

        /// <summary>The preset a row is made from, or null — by id, else by name.</summary>
        public LevelObjectPresetDef PresetOf(LevelObjectDef row)
        {
            if (row == null || row.preset == null || presets == null)
                return null;
            return (presets.FindById(row.preset.entryId) ?? presets.FindByName(row.preset.entryName))
                as LevelObjectPresetDef;
        }

        /// <summary>The row as it runs: dressed in its preset's core, or the row itself when it has none.</summary>
        public LevelObjectDef Resolve(LevelObjectDef row)
        {
            LevelObjectPresetDef preset = PresetOf(row);
            return preset != null ? preset.Apply(row) : row;
        }

        /// <summary>A placement's ground plan as a world position.</summary>
        /// <param name="position">The row's position.</param>
        /// <param name="up">How far off the ground it sits — a person stands at zero, a pickup
        /// hovers. The row's own height when it sets one, the kind's otherwise
        /// (<see cref="LevelObjectDef.HeightOr"/>); the plane decides which axis it is.</param>
        /// <returns>Where the object goes.</returns>
        public Vector3 ToWorld(Vector2 position, float up = 0f)
        {
            return plane == LevelGroundPlane.XZ
                ? new Vector3(position.x, up, position.y)
                : new Vector3(position.x, position.y, up);
        }

        /// <summary>A world position back as a ground plan — the inverse of
        /// <see cref="ToWorld"/> for the two plan axes. The third axis is
        /// <see cref="HeightOf"/>.</summary>
        /// <param name="world">A position in the level.</param>
        /// <returns>The row's two numbers.</returns>
        public Vector2 ToPlan(Vector3 world)
        {
            return plane == LevelGroundPlane.XZ
                ? new Vector2(world.x, world.z)
                : new Vector2(world.x, world.y);
        }

        /// <summary>How far off this level's ground plane a world position is — the
        /// <c>up</c> that <see cref="ToWorld"/> took.</summary>
        /// <param name="world">A position in the level.</param>
        public float HeightOf(Vector3 world)
        {
            return plane == LevelGroundPlane.XZ ? world.y : world.z;
        }

        /// <summary>
        /// What a moved handle writes to a row: the plan axes to <see cref="LevelObjectDef.position"/>,
        /// the up axis to <see cref="LevelObjectDef.height"/>. A row still on its kind's height is
        /// left on it unless the drag actually changed the height — a sideways drag must not pin a
        /// row to today's kind height and stop it following the kind.
        /// </summary>
        /// <param name="row">The row being moved.</param>
        /// <param name="world">Where the handle now is.</param>
        /// <param name="kindHeight">The row's kind's body height — where it stood before.</param>
        public void Place(LevelObjectDef row, Vector3 world, float kindHeight)
        {
            row.position = ToPlan(world);
            float up = HeightOf(world);
            if (!row.overrideHeight && Mathf.Abs(up - kindHeight) <= k_HeightSlack)
                return;
            row.overrideHeight = true;
            row.height = up;
        }

        /// <summary>Float noise a sideways drag can put on the up axis — far below anything an
        /// author means as a height.</summary>
        private const float k_HeightSlack = 1e-4f;

        /// <summary>A row's <see cref="LevelObjectDef.facing"/> as a rotation: about the plane's
        /// own normal, so a degree means the same turn in either plane.</summary>
        /// <param name="degrees">The row's facing.</param>
        /// <returns>The rotation to spawn with.</returns>
        public Quaternion Facing(float degrees)
        {
            return Quaternion.AngleAxis(degrees, plane == LevelGroundPlane.XZ
                ? Vector3.up
                : Vector3.forward);
        }

        /// <summary>Which way a row at that facing is LOOKING, as a direction in the level — what
        /// an editor draws and what a spawner asks when it wants to know whether the guard can see
        /// the door. At zero degrees it is the plane's own forward: +Z in XZ, which is what a
        /// prefab's untouched rotation already points at, and +X in XY.</summary>
        /// <param name="degrees">The row's facing.</param>
        /// <returns>A unit direction.</returns>
        public Vector3 Forward(float degrees)
        {
            return Facing(degrees) * (plane == LevelGroundPlane.XZ
                ? Vector3.forward
                : Vector3.right);
        }
    }
}
