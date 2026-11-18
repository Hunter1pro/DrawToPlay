using System.Collections.Generic;
using UnityEngine;

namespace PowerOfFire.DrawToPlay.Editor
{
    /// <summary>
    /// What a manifest row looks like when its kind's prefab is not the whole answer — a row that picks its model
    /// from its kind's definitions table (a prop, a find). The Level Manifest's ghost draws the answer in the kind's
    /// colour. Registered from the game's <c>[InitializeOnLoad]</c> class (meta-rule 5: the package defines the seam,
    /// the game implements it). Asked on every Scene repaint: answer from a cache, never walk the project.
    /// </summary>
    public interface ILevelRowGhost
    {
        /// <summary>The prefab to ghost for <paramref name="row"/>, and where it sits relative to the placement (the
        /// kind prefab's socket). Null: not this game's row — the next one is asked, then the kind's prefab.</summary>
        GameObject GhostOf(LevelObjectKindDef kind, LevelObjectDef row, out Matrix4x4 local);

        /// <summary>The switched-off child of the ghost to draw for this row — the variant it picks; null: none.</summary>
        string VariantOf(LevelObjectKindDef kind, LevelObjectDef row) => null;
    }

    /// <summary>The ghosts a game registered; a domain reload clears them, and the game's class registers again.</summary>
    public static class LevelRowGhosts
    {
        private static readonly List<ILevelRowGhost> s_Ghosts = new List<ILevelRowGhost>();

        public static void Register(ILevelRowGhost ghost)
        {
            if (ghost != null && !s_Ghosts.Contains(ghost))
                s_Ghosts.Add(ghost);
        }

        public static void Unregister(ILevelRowGhost ghost) => s_Ghosts.Remove(ghost);

        /// <summary>The first registered answer for <paramref name="row"/>, else its kind's prefab where it stands.</summary>
        public static GameObject Of(LevelObjectKindDef kind, LevelObjectDef row, out Matrix4x4 local) =>
            Of(kind, row, out local, out _);

        /// <summary>The same, and the variant the answer names (null for the kind's own prefab).</summary>
        public static GameObject Of(LevelObjectKindDef kind, LevelObjectDef row, out Matrix4x4 local, out string variant)
        {
            local = Matrix4x4.identity;
            variant = null;
            if (kind == null)
                return null;
            if (row != null)
            {
                foreach (ILevelRowGhost ghost in s_Ghosts)
                {
                    GameObject own = ghost.GhostOf(kind, row, out Matrix4x4 at);
                    if (own != null)
                    {
                        local = at;
                        variant = ghost.VariantOf(kind, row);
                        return own;
                    }
                }
            }
            return kind.prefab;
        }
    }
}
