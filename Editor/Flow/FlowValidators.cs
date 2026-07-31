using System;
using System.Collections.Generic;
using UnityEditor.SceneManagement;
using UnityEngine;

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

        /// <summary>Terrain flow, stage 2 — mask painting (§6.4).</summary>
        public const string TerrainPaintStageId = "terrain.paint";

        /// <summary>Terrain flow, stage 3 — stamp scatter (§6.4).</summary>
        public const string TerrainDecorateStageId = "terrain.decorate";

        /// <summary>Terrain flow, stage 4 — collision derivation (§6.4).</summary>
        public const string TerrainCollisionStageId = "terrain.collision";

        /// <summary>Terrain flow, stage 5 — spawn markers and zones (§6.4). Deliberately NOT in
        /// the registry: nothing authors this stage yet, so it must draw the neutral badge rather
        /// than claim to be empty (an unknown stage never claims a state).</summary>
        public const string TerrainGameplayStageId = "terrain.gameplay";

        /// <summary>Character flow, stage 1 — body-part shapes (§6.1).</summary>
        public const string CharacterDrawStageId = "character.draw";

        /// <summary>Character flow, stage 2 — bone chains per part (§6.1).</summary>
        public const string CharacterRigStageId = "character.rig";

        /// <summary>Character flow, stage 3 — skin binding and weights (§6.1).</summary>
        public const string CharacterSkinStageId = "character.skin";

        /// <summary>Character flow, stage 4 — pose clips (§6.1).</summary>
        public const string CharacterAnimateStageId = "character.animate";

        private static readonly Dictionary<string, Func<StageStatus>> s_Validators =
            new Dictionary<string, Func<StageStatus>>
            {
                { TerrainSculptStageId, ValidateTerrainSculpt },
                { TerrainPaintStageId, ValidateTerrainPaint },
                { TerrainDecorateStageId, ValidateTerrainDecorate },
                { TerrainCollisionStageId, ValidateTerrainCollision },
                { CharacterDrawStageId, ValidateCharacterDraw },
                { CharacterRigStageId, ValidateCharacterRig },
                { CharacterSkinStageId, ValidateCharacterSkin },
                { CharacterAnimateStageId, ValidateCharacterAnimate }
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

        /// <summary>Paint: Complete needs BOTH halves of the stage's output — a mask that has
        /// actually been painted (non-empty maskPng on the asset) AND a second material to blend
        /// it against (paintTexture2 or paintTexture3), because a mask with one texture behind it
        /// paints nothing visible. A ShapePaint component with no committed mask yet is
        /// InProgress; no ShapePaint anywhere is Empty.</summary>
        private static StageStatus ValidateTerrainPaint()
        {
            var renderers = FindComponents<DrawnShapeRenderer>();
            for (var i = 0; i < renderers.Length; ++i)
            {
                var asset = renderers[i] != null ? renderers[i].asset : null;
                if (asset == null || asset.maskPng == null || asset.maskPng.Length == 0)
                    continue;

                if (asset.paintTexture2 != null || asset.paintTexture3 != null)
                    return StageStatus.Complete;
            }

            return FindComponents<ShapePaint>().Length > 0
                ? StageStatus.InProgress
                : StageStatus.Empty;
        }

        /// <summary>Decorate: presence-based, because "enough decoration" is a judgement call and
        /// no count would be honest. One scattered stamp — recognised by the
        /// <see cref="StampMarker"/> the stamp tool leaves behind, never by name or parentage —
        /// completes the stage.</summary>
        private static StageStatus ValidateTerrainDecorate()
        {
            return FindComponents<StampMarker>().Length > 0
                ? StageStatus.Complete
                : StageStatus.Empty;
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

        // --- built-in Character flow probes -----------------------------------------------

        /// <summary>Draw: the same question as terrain.sculpt — "is there a drawn shape with
        /// geometry?" (§6.1 "≥1 shape"). Registered as its own entry rather than pointing the
        /// character id at the terrain probe, so the Character stage can tighten later (e.g.
        /// "every part parented under one entity root") without silently redefining what
        /// Complete means for Terrain. "Style set" is not probed: every asset has a style, so
        /// there is nothing honest to measure.</summary>
        private static StageStatus ValidateCharacterDraw()
        {
            return ValidateTerrainSculpt();
        }

        /// <summary>Rig: Complete as soon as one skeleton in the scene owns a RigAsset with at
        /// least one bone. A ShapeRig with no asset (or an empty one) is InProgress — the
        /// component is usually added a moment before the first chain is committed. §6.1's real
        /// bar, "every part bound or explicitly marked static", needs a per-part static flag that
        /// nothing authors yet, so the badge deliberately measures less than the checklist asks
        /// for rather than guessing.</summary>
        private static StageStatus ValidateCharacterRig()
        {
            var rigs = FindComponents<ShapeRig>();
            if (rigs.Length == 0)
                return StageStatus.Empty;

            for (var i = 0; i < rigs.Length; ++i)
            {
                var rig = rigs[i];
                if (rig == null || rig.rig == null || rig.rig.bones == null)
                    continue;

                if (rig.rig.bones.Count > 0)
                    return StageStatus.Complete;
            }

            return StageStatus.InProgress;
        }

        /// <summary>Skin: Complete when a bound part is actually skinned right now — isSkinned
        /// means the generated SkinnedMeshRenderer exists and has taken over from the flat mesh.
        /// That is a live check, not an authoring record: the skin layer is HideFlags.DontSave,
        /// so a freshly reopened scene reads InProgress until something regenerates it (the next
        /// bind, "Regenerate Skins In Scene", or entering play mode). Reporting that honestly is
        /// the point — it is exactly the state the user is looking at.</summary>
        private static StageStatus ValidateCharacterSkin()
        {
            var skins = FindComponents<DrawnShapeSkin>();
            if (skins.Length == 0)
                return StageStatus.Empty;

            for (var i = 0; i < skins.Length; ++i)
            {
                if (skins[i] != null && skins[i].isSkinned)
                    return StageStatus.Complete;
            }

            return StageStatus.InProgress;
        }

        /// <summary>Animate: Complete when an animator holds a clip with at least two pose
        /// columns — one column is a rest snapshot, two is the first thing that can be called an
        /// animation. The whole clips list is scanned rather than PoseAnimator.Clip(), so the
        /// badge does not depend on <c>current</c> being set (and cannot throw out of a probe
        /// that runs on every hierarchy change). §6.1's required clip NAMES (idle/run/hit/death)
        /// are a per-game contract nothing declares yet, so they are not checked.</summary>
        private static StageStatus ValidateCharacterAnimate()
        {
            var animators = FindComponents<PoseAnimator>();
            if (animators.Length == 0)
                return StageStatus.Empty;

            for (var i = 0; i < animators.Length; ++i)
            {
                var clips = animators[i] != null ? animators[i].clips : null;
                if (clips == null)
                    continue;

                for (var j = 0; j < clips.Count; ++j)
                {
                    if (clips[j] != null && clips[j].poseCount >= 2)
                        return StageStatus.Complete;
                }
            }

            return StageStatus.InProgress;
        }

        /// <summary>Every drawn shape in the stage the user is looking at.</summary>
        private static DrawnShapeRenderer[] FindRenderers()
        {
            return FindComponents<DrawnShapeRenderer>();
        }

        /// <summary>Every component of a type in the stage the user is looking at: all open
        /// scenes in the main stage, or the prefab contents while a prefab stage is open.
        /// Documented as "all active loaded objects", so a deactivated object may not be counted —
        /// badges are advisory and nothing gates on them.</summary>
        private static T[] FindComponents<T>() where T : Component
        {
            var stage = StageUtility.GetCurrentStageHandle();
            return stage.IsValid()
                ? stage.FindComponentsOfType<T>()
                : Array.Empty<T>();
        }
    }
}
