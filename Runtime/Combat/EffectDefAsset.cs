using UnityEngine;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>The level-5 rule from effect_def.gd: at max level the effect ESCAPES its victim.
    /// Port of <c>@export_enum("none", "jump_on_kill", "spread_on_death", "chain_on_apply")</c> —
    /// a real enum here, because a typo'd string is a silently dead effect.</summary>
    public enum EffectSpecial
    {
        None,
        /// <summary>On the victim's death, re-applies once per stack to the nearest peer.</summary>
        JumpOnKill,
        /// <summary>On the victim's death, applies once to the nearest peer.</summary>
        SpreadOnDeath,
        /// <summary>On apply, also applies to the nearest peer at half duration/stun.</summary>
        ChainOnApply
    }

    /// <summary>
    /// One status effect — port of hunter's <c>effect_def.gd</c> (draw-tool-port-brief.md §6.2
    /// "weapon/effect defs (weapon_def/effect_def ports)"). Every magnitude is a PER-LEVEL LADDER:
    /// index 0 is level 1, and <see cref="At"/> clamps a level into the array, so a one-entry
    /// ladder means "same at every level" and an empty ladder means "this effect does not do that
    /// at all". Freeze is a stun ladder with no dps; poison is a dps ladder with an
    /// <see cref="outgoingMult"/> penalty.
    ///
    /// M5 is DATA ONLY: there is no runtime EffectStack port yet (effect_stack.gd owns application,
    /// ticking, tint and the escape rules). What ships here is the authoring surface those systems
    /// will read, plus the level lookup, which is the one piece of behaviour that lives on the
    /// definition itself in Godot too.
    ///
    /// No unit conversion applies to any field: dps is damage/second, the ladders are seconds or
    /// unitless fractions, and none of them is a length.
    /// </summary>
    [CreateAssetMenu(menuName = "Draw To Play/Effect Def", fileName = "EffectDef")]
    public sealed class EffectDefAsset : ScriptableObject
    {
        /// <summary>effect_def.gd <c>MAX_STACKS</c>. Re-applying past this refreshes duration
        /// without adding a stack.</summary>
        public const int maxStacks = 5;

        /// <summary>The level at which <see cref="special"/> triggers. Godot hardcodes
        /// <c>level >= 5</c> in effect_stack.gd; named here so the rule is greppable.</summary>
        public const int escapeLevel = 5;

        /// <summary>Stable key. Godot uses a StringName so the stack can dictionary-key on it;
        /// a plain string is the Unity equivalent (effect_stack.gd also special-cases the ids
        /// "freeze" and "poison" for body tint).</summary>
        public string id = "";

        public string displayName = "";

        /// <summary>Godot exports a Texture2D; a Sprite is the Unity-idiomatic icon reference
        /// (UI Image, SpriteRenderer) and carries the same texture.</summary>
        public Sprite icon;

        /// <summary>Tint for the icon, duration bar, stack pips and hit burst.</summary>
        public Color color = Color.white;

        /// <summary>Damage per second PER STACK, by level.</summary>
        public float[] dps = System.Array.Empty<float>();

        /// <summary>Effect duration in seconds, by level (refreshes on re-apply).</summary>
        public float[] duration = System.Array.Empty<float>();

        /// <summary>Move-speed cut 0..1, by level (0.3 = 30% slower).</summary>
        public float[] slow = System.Array.Empty<float>();

        /// <summary>Full-stop seconds applied on (re)apply, by level — freeze/stun.</summary>
        public float[] stun = System.Array.Empty<float>();

        /// <summary>The victim's OUTGOING damage multiplier while this is active (poison 0.8).
        /// Not a ladder in Godot either.</summary>
        public float outgoingMult = 1f;

        /// <summary>Extra INCOMING damage multiplier while stun-frozen at <see cref="escapeLevel"/>
        /// (freeze 0.5 = +50% damage taken). 0 disables shatter.</summary>
        public float shatterBonus = 0f;

        public EffectSpecial special = EffectSpecial.None;

        /// <summary>Ladder lookup — port of effect_def.gd <c>at()</c>: empty ladder reads 0, and a
        /// level past the end clamps to the last entry (so a 3-entry ladder is legal on a 5-level
        /// weapon and simply plateaus).</summary>
        public static float At(float[] ladder, int level)
        {
            if (ladder == null || ladder.Length == 0)
                return 0f;
            return ladder[Mathf.Clamp(level, 1, ladder.Length) - 1];
        }

        public float DpsAt(int level) => At(dps, level);

        public float DurationAt(int level) => At(duration, level);

        public float SlowAt(int level) => At(slow, level);

        public float StunAt(int level) => At(stun, level);
    }
}
