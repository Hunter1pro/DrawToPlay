using System.Collections.Generic;
using UnityEngine;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// Raises <c>context.EmitCueFired(name, payload)</c> and Succeeds — brief §7.2's
    /// "FireCue", and the whole of the Cue layer in M6. Cues are LEAF PAYLOADS by design
    /// (§7.1): the tree says "a footstep happened here, facing this way" and knows nothing
    /// about VFX prefabs, audio mixers or camera shake. Whatever listens on
    /// <c>StateTreeContext.cueFired</c> owns that half, which is what lets the same preset
    /// tree run headless in a test with no presentation layer at all.
    ///
    /// PAYLOAD CONTRACT (keys a listener can rely on):
    /// <list type="bullet">
    /// <item>"owner" — the GameObject that fired the cue. Always present.</item>
    /// <item>"position" — Vector2, the owner's world XY plus <see cref="offset"/>, already
    /// mirrored by the owner's facing so a hit spark on a flipped body lands on the correct
    /// side.</item>
    /// <item>"facing" — float, +1 or -1, the sign of the owner's localScale.x.</item>
    /// <item>"target" — the blackboard target, present only when one is held and valid.</item>
    /// <item><see cref="payloadKey"/> — <see cref="payloadValue"/>, when the key is
    /// non-empty. The per-cue parameter (intensity, variant index) that saves authoring a
    /// component per cue flavour.</item>
    /// </list>
    ///
    /// A FRESH DICTIONARY PER FIRE. Pooling one would be cheaper and wrong: the payload is
    /// handed to arbitrary subscribers that may hold it (a cue that spawns a delayed effect
    /// reads it next frame), and a reused dictionary would rewrite their data underneath them.
    /// One small allocation per cue is the correct trade at authoring scale.
    ///
    /// Stateless and instant, so an interrupt can never leave a half-fired cue behind.
    /// </summary>
    [CreateAssetMenu(menuName = "Draw To Play/AI/Tasks/Fire Cue", fileName = "FireCue")]
    [StateTreeCategory("Tasks/Cues", "Emit a named cue with a payload")]
    public sealed class FireCueTask : StateTreeTaskAsset
    {
        /// <summary>Cue identifier the listener switches on ("hit_spark", "footstep",
        /// "roar").</summary>
        public string cueName = "";

        /// <summary>Spawn offset in the owner's local axes (world units); X is mirrored by
        /// facing.</summary>
        public Vector2 offset = Vector2.zero;

        /// <summary>Optional extra payload entry. Empty = omitted.</summary>
        [StateTreeKey(StateTreeKeyKind.Object)]
        public StateTreeKeyField payloadKey = new StateTreeKeyField();

        public float payloadValue;

        public override StateTreeStatus OnTick(StateTreeContext context, float deltaTime)
        {
            if (context == null || string.IsNullOrEmpty(cueName))
                return StateTreeStatus.Failure;

            GameObject owner = context.owner;
            if (owner == null)
                return StateTreeStatus.Failure;

            Transform ownerTransform = owner.transform;
            float facing = ownerTransform.localScale.x < 0f ? -1f : 1f;
            Vector3 position = ownerTransform.position;

            Dictionary<string, object> payload = new Dictionary<string, object>
            {
                { "owner", owner },
                {
                    "position",
                    new Vector2(position.x + offset.x * facing, position.y + offset.y)
                },
                { "facing", facing }
            };

            GameObject target = StateTreeLibraryUtil.GetValidTarget(context, owner);
            if (target != null)
                payload["target"] = target;

            if (!string.IsNullOrEmpty(payloadKey))
                payload[payloadKey] = payloadValue;

            context.EmitCueFired(cueName, payload);
            return StateTreeStatus.Success;
        }
    }
}
