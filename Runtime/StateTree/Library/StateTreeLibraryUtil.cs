using UnityEngine;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// The wiring every seed component would otherwise repeat — draw-tool-port-brief.md §7.2:
    /// "50%+ of ability/AI code is connection, filtering, and rewiring". Blackboard key
    /// constants, null-safe target resolution, component lookup and the planar-offset maths
    /// live here exactly once so each component in this folder stays ONE job with serialized
    /// params. The Godot equivalent is the shared <c>ai_state_tree_task.gd</c> /
    /// <c>ai_state_tree_condition.gd</c> base pair; a static utility rather than a base class
    /// because M6 conventions pin every component's base type to
    /// <see cref="StateTreeTaskAsset"/> / <see cref="StateTreeConditionAsset"/> directly.
    ///
    /// NULL-SAFETY CONTRACT (M6, non-negotiable): no target =&gt; condition false / task
    /// Failure; no component =&gt; condition false / task Failure. Every accessor here returns
    /// null or false rather than throwing, so a component's own body reads as a straight
    /// line of guards.
    ///
    /// BLACKBOARD KEYS ARE LOCKED. The four constants below are the only cross-component
    /// keys; everything else a component needs is a serialized field on the component (the
    /// Godot pattern — <c>@export var max_range</c>, not a blackboard read). The blackboard
    /// carries cross-task state, and "target" is the canonical example: the detector writes
    /// it, the chase/attack/knockback tasks read it.
    /// </summary>
    public static class StateTreeLibraryUtil
    {
        /// <summary>GameObject — the current chase/attack target. Written by
        /// <see cref="TargetDetectedCondition"/>, read by nearly everything else.</summary>
        public const string TargetKey = "target";

        /// <summary>float, world units/second.</summary>
        public const string MoveSpeedKey = "moveSpeed";

        /// <summary>float, world units.</summary>
        public const string AttackRangeKey = "attackRange";

        /// <summary>float, world units.</summary>
        public const string DetectRangeKey = "detectRange";

        // --- target ------------------------------------------------------------------------

        /// <summary>The blackboard target, or null. A DESTROYED GameObject still sits in the
        /// dictionary as a non-null boxed reference (the dictionary holds
        /// <see cref="object"/>, which does not go through Unity's overloaded
        /// <c>operator ==</c>), so the entry is erased on the way out — that is the Unity
        /// hazard Godot's <c>is_instance_valid</c> guard covers.</summary>
        public static GameObject GetTarget(StateTreeContext context)
        {
            if (context == null)
                return null;
            if (!context.blackboard.TryGetValue(TargetKey, out object value))
                return null;

            // The cast is what re-enters Unity's null semantics: a destroyed object compares
            // equal to null once it is typed as a UnityEngine.Object again.
            GameObject target = value as GameObject;
            if (target != null)
                return target;

            context.blackboard.Remove(TargetKey);
            return null;
        }

        public static void SetTarget(StateTreeContext context, GameObject target)
        {
            if (context == null)
                return;
            if (target == null)
                context.blackboard.Remove(TargetKey);
            else
                context.blackboard[TargetKey] = target;
        }

        public static void ClearTarget(StateTreeContext context)
        {
            context?.blackboard.Remove(TargetKey);
        }

        /// <summary>Port of <c>_is_target_valid</c>: alive, active, and not ourselves. A
        /// target with no <see cref="HealthComponent"/> counts as valid (Godot's
        /// <c>_is_target_alive</c> falls through to true when the node cannot be asked), so
        /// scenery props and dummies without health can still be chased.</summary>
        public static bool IsTargetValid(GameObject target, GameObject owner)
        {
            if (target == null || owner == null)
                return false;
            if (target == owner)
                return false;
            if (!target.activeInHierarchy)
                return false;

            HealthComponent health = ResolveComponent<HealthComponent>(target);
            return health == null || health.isAlive;
        }

        /// <summary>The blackboard target when it is still valid for
        /// <paramref name="owner"/>, else null — the guard nine of these components open
        /// with.</summary>
        public static GameObject GetValidTarget(StateTreeContext context, GameObject owner)
        {
            GameObject target = GetTarget(context);
            return IsTargetValid(target, owner) ? target : null;
        }

        // --- geometry ----------------------------------------------------------------------

        /// <summary>Offset from <paramref name="from"/> to <paramref name="to"/> in the XY
        /// plane. This is a 2D toolset living in 3D Transforms; Z is authoring depth and must
        /// never enter a range test, or a sprite sorted 5 units back reads as out of
        /// range.</summary>
        public static Vector2 PlanarOffset(GameObject from, GameObject to)
        {
            if (from == null || to == null)
                return Vector2.zero;
            Vector3 delta = to.transform.position - from.transform.position;
            return new Vector2(delta.x, delta.y);
        }

        public static float PlanarDistance(GameObject from, GameObject to)
        {
            return PlanarOffset(from, to).magnitude;
        }

        // --- components --------------------------------------------------------------------

        /// <summary>Find a component "on the owner" at authoring scale: the GameObject first,
        /// then its subtree (including inactive children — a disabled visual child still
        /// carries its animator), then its ancestors. The widening order matters: the
        /// health-beside-the-shape layout and Godot's health-as-a-CHILD layout both resolve,
        /// and a nested rig root is found from the entity root without the author having to
        /// wire a reference. Returns null when nothing matches; callers translate that into
        /// Failure / false.</summary>
        public static T ResolveComponent<T>(GameObject owner) where T : Component
        {
            if (owner == null)
                return null;

            T found = owner.GetComponent<T>();
            if (found != null)
                return found;

            found = owner.GetComponentInChildren<T>(true);
            if (found != null)
                return found;

            return owner.GetComponentInParent<T>();
        }

        // --- blackboard numbers --------------------------------------------------------------

        /// <summary>Read a numeric blackboard entry. The dictionary is
        /// <c>&lt;string, object&gt;</c>, so a value authored as an int (or arriving from a
        /// deserialised source as a double) must still read as a float — anything else and a
        /// preset that seeds <c>3</c> instead of <c>3f</c> silently stops matching.</summary>
        public static bool TryGetFloat(StateTreeContext context, string key, out float value)
        {
            value = 0f;
            if (context == null || string.IsNullOrEmpty(key))
                return false;
            if (!context.blackboard.TryGetValue(key, out object boxed) || boxed == null)
                return false;

            switch (boxed)
            {
                case float f:
                    value = f;
                    return true;
                case int i:
                    value = i;
                    return true;
                case double d:
                    value = (float)d;
                    return true;
                default:
                    return false;
            }
        }

        public static void SetFloat(StateTreeContext context, string key, float value)
        {
            if (context == null || string.IsNullOrEmpty(key))
                return;
            context.blackboard[key] = value;
        }

        /// <summary>Serialized value unless the component opted into the locked blackboard
        /// key, in which case the blackboard wins — and still falls back to the serialized
        /// value when the key is absent, so a half-configured preset degrades to the authored
        /// default instead of to zero.</summary>
        public static float ResolveFloat(StateTreeContext context, string key,
            float serializedValue, bool fromBlackboard)
        {
            if (!fromBlackboard)
                return serializedValue;
            return TryGetFloat(context, key, out float value) ? value : serializedValue;
        }
    }
}
