using System;
using System.Collections.Generic;
using UnityEditor.SceneManagement;

namespace PowerOfFire.DrawToPlay.Editor
{
    /// <summary>
    /// State of one flow stage, drawn as the tab's badge (draw-tool-port-brief.md §6:
    /// "empty / in-progress / complete / invalidated upstream").
    /// </summary>
    public enum StageStatus
    {
        /// <summary>Nothing authored for this stage yet.</summary>
        Empty = 0,

        /// <summary>Started, but the stage's outputs are not all there.</summary>
        InProgress = 1,

        /// <summary>Every required output exists.</summary>
        Complete = 2,

        // TODO(§6, post-M1): the upstream-invalidated state — e.g. a limb redrawn after a
        // ragdoll capsule was authored on the old proportions. It needs per-stage output
        // fingerprints to detect, which nothing produces yet, so no built-in validator ever
        // returns this value in M1. The slot exists so adding it later does not renumber the
        // enum (serialized nowhere today, but the badge mapping already handles it).
        /// <summary>Reserved: authored, but an upstream stage changed underneath it.</summary>
        Invalidated = 3
    }

    /// <summary>
    /// Stage id → status probe. The flow window asks this registry what badge to draw; a
    /// stage id with no entry gets a neutral badge instead of a wrong one, which keeps
    /// FlowDefinition assets free to describe stages whose validators do not exist yet.
    ///
    /// Validators are cheap scene scans run on demand (tab click, hierarchy/selection change,
    /// manual refresh) — never per frame.
    /// </summary>
    public static class FlowValidators
    {
        /// <summary>Terrain flow, stage 1 — blob sculpting (§6.4).</summary>
        public const string TerrainSculptStageId = "terrain.sculpt";

        /// <summary>Terrain flow, stage 4 — collision derivation (§6.4).</summary>
        public const string TerrainCollisionStageId = "terrain.collision";

        private static readonly Dictionary<string, Func<StageStatus>> s_Validators =
            new Dictionary<string, Func<StageStatus>>
            {
                { TerrainSculptStageId, ValidateTerrainSculpt },
                { TerrainCollisionStageId, ValidateTerrainCollision }
            };

        /// <summary>Register (or replace) the probe for a stage id. Later flows register from
        /// their own editor code; nothing in the window needs to change.</summary>
        public static void Register(string stageId, Func<StageStatus> validator)
        {
            if (string.IsNullOrEmpty(stageId) || validator == null)
                return;
            s_Validators[stageId] = validator;
        }

        /// <summary>Drop a stage's probe. Returns false when nothing was registered.</summary>
        public static bool Unregister(string stageId)
        {
            return !string.IsNullOrEmpty(stageId) && s_Validators.Remove(stageId);
        }

        /// <summary>True when a probe exists for the id.</summary>
        public static bool IsRegistered(string stageId)
        {
            return !string.IsNullOrEmpty(stageId) && s_Validators.ContainsKey(stageId);
        }

        /// <summary>Run the probe for a stage id. Returns false (neutral badge) when the id is
        /// unregistered — an unknown stage must never claim to be complete.</summary>
        public static bool TryEvaluate(string stageId, out StageStatus status)
        {
            status = StageStatus.Empty;
            if (string.IsNullOrEmpty(stageId) || !s_Validators.TryGetValue(stageId, out var validator))
                return false;

            status = validator();
            return true;
        }

        // --- built-in Terrain flow probes -------------------------------------------------

        /// <summary>Sculpt: Empty with no drawn shapes at all, InProgress once shape objects
        /// exist but none carries geometry, Complete as soon as one has a curve.</summary>
        private static StageStatus ValidateTerrainSculpt()
        {
            var renderers = FindRenderers();
            if (renderers == null || renderers.Length == 0)
                return StageStatus.Empty;

            for (var i = 0; i < renderers.Length; ++i)
            {
                if (renderers[i] != null && renderers[i].hasShape)
                    return StageStatus.Complete;
            }

            return StageStatus.InProgress;
        }

        /// <summary>Collision: measured over the shapes that actually have geometry — Complete
        /// when every one of them owns a TerrainBlob, InProgress when only some do, Empty when
        /// none do (or when there is nothing to give collision to yet).</summary>
        private static StageStatus ValidateTerrainCollision()
        {
            var renderers = FindRenderers();
            if (renderers == null || renderers.Length == 0)
                return StageStatus.Empty;

            var shaped = 0;
            var withBlob = 0;
            for (var i = 0; i < renderers.Length; ++i)
            {
                var renderer = renderers[i];
                if (renderer == null || !renderer.hasShape)
                    continue;

                ++shaped;
                if (renderer.GetComponent<TerrainBlob>() != null)
                    ++withBlob;
            }

            if (shaped == 0 || withBlob == 0)
                return StageStatus.Empty;

            return withBlob < shaped ? StageStatus.InProgress : StageStatus.Complete;
        }

        /// <summary>Every drawn shape in the stage the user is looking at: all open scenes in
        /// the main stage, or the prefab contents while a prefab stage is open. Documented as
        /// "all active loaded objects", so a deactivated blob may not be counted — badges are
        /// advisory and nothing gates on them.</summary>
        private static DrawnShapeRenderer[] FindRenderers()
        {
            var stage = StageUtility.GetCurrentStageHandle();
            return stage.IsValid()
                ? stage.FindComponentsOfType<DrawnShapeRenderer>()
                : Array.Empty<DrawnShapeRenderer>();
        }
    }
}
