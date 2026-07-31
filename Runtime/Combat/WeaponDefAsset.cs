using UnityEngine;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// One weapon, data-driven — port of hunter's <c>weapon_def.gd</c> (draw-tool-port-brief.md
    /// §6.2). Melee weapons sweep a strike volume along <see cref="hitTrack"/> over
    /// <see cref="activeTime"/>: that single mechanism is why a claw can pull far-to-near and a
    /// lance thrust near-to-far from the same code. A weapon with a <see cref="projectile"/> fires
    /// instead of swinging.
    ///
    /// UNITS: every length is converted from Godot pixels at the project's 1 world unit = 32 px
    /// (m0 conventions), so the defaults below are the Godot defaults ÷ 32 — knockback 90 px/s →
    /// 2.8125 wu/s, the hit track's (10, -2) px → (0.3125, 0.0625) wu, hit size 14x12 px →
    /// 0.4375 x 0.375 wu, projectile speed 220 px/s → 6.875 wu/s, range 140 px → 4.375 wu. Times
    /// (cooldown, activeTime, comboWindow) and the unitless damage/combo counts are unchanged.
    ///
    /// Y SIGN: Godot 2D is Y-DOWN, so its default track offset (10, -2) sits slightly ABOVE the
    /// body. Unity is Y-up, so the same offset is (+0.3125, +0.0625) here. Offsets authored from
    /// Godot data must have their Y negated; magnitudes are unaffected.
    ///
    /// M5 is DATA ONLY — there is no Hitbox/Projectile runtime port yet. <see cref="TrackPoint"/>
    /// and <see cref="DamageAt"/> ship because they live on the definition in Godot too, and they
    /// are what a <see cref="HitboxWindow"/> consumer will call.
    /// </summary>
    [CreateAssetMenu(menuName = "Draw To Play/Weapon Def", fileName = "WeaponDef")]
    public sealed class WeaponDefAsset : ScriptableObject
    {
        /// <summary>Damage multiplier per weapon level (gold/green/blue/purple/red) —
        /// weapon_def.gd <c>TIER_MULT</c>. Private so the ladder cannot be mutated through the
        /// asset; read it with <see cref="TierMultiplier"/>.</summary>
        private static readonly float[] k_TierMultipliers = { 1f, 1.3f, 1.7f, 2.2f, 3f };

        /// <summary>Fallback strike offset when <see cref="hitTrack"/> is empty — weapon_def.gd's
        /// <c>Vector2(10.0 * facing, -2.0)</c>, converted and Y-flipped.</summary>
        private const float k_FallbackTrackX = 10f / 32f;
        private const float k_FallbackTrackY = 2f / 32f;

        /// <summary>Stable key (Godot StringName). weapon_catalog.gd looks weapons up by it.</summary>
        public string id = "";

        public string displayName = "";

        /// <summary>Godot exports a Texture2D; Sprite is the Unity-idiomatic icon reference.</summary>
        public Sprite icon;

        public float damage = 1f;

        /// <summary>Seconds between swings.</summary>
        public float cooldown = 0.35f;

        /// <summary>Knockback speed applied to the victim, world units/second (Godot 90 px/s).</summary>
        public float knockback = 90f / 32f;

        /// <summary>Signature status effect stacked on every hit; the stack level is the weapon
        /// level.</summary>
        public EffectDefAsset effect;

        [Header("Melee")]

        /// <summary>Seconds the strike volume stays armed.</summary>
        public float activeTime = 0.12f;

        /// <summary>Strike-volume centre offsets in BODY space, facing right (world units), that
        /// the strike moves through over <see cref="activeTime"/>. A single point = a static hit.</summary>
        public Vector2[] hitTrack = { new Vector2(10f / 32f, 2f / 32f) };

        /// <summary>Strike-volume size in world units (Godot 14 x 12 px).</summary>
        public Vector2 hitSize = new Vector2(14f / 32f, 12f / 32f);

        /// <summary>&gt; 1 = combo: consecutive swings alternate visual offset (twin daggers).</summary>
        public int comboSteps = 1;

        /// <summary>Seconds a combo stays open before the step counter resets.</summary>
        public float comboWindow = 0.8f;

        [Header("Ranged")]

        /// <summary>Godot PackedScene → a Unity prefab. Non-null = this weapon fires instead of
        /// swinging.</summary>
        public GameObject projectile;

        /// <summary>World units/second (Godot 220 px/s).</summary>
        public float projectileSpeed = 220f / 32f;

        /// <summary>World units before the projectile expires (Godot 140 px).</summary>
        public float projectileRange = 140f / 32f;

        /// <summary>Number of authored weapon levels — the length of the tier ladder.</summary>
        public static int tierCount => k_TierMultipliers.Length;

        /// <summary>Tier multiplier for a 1-based weapon level, clamped into the ladder.</summary>
        public static float TierMultiplier(int level)
        {
            return k_TierMultipliers[Mathf.Clamp(level, 1, k_TierMultipliers.Length) - 1];
        }

        /// <summary>weapon_def.gd <c>damage_at</c>: base damage scaled by the level's tier.</summary>
        public float DamageAt(int level)
        {
            return damage * TierMultiplier(level);
        }

        /// <summary>Strike-volume offset at progress <paramref name="p"/> in [0,1], mirrored for
        /// facing (+1 right, -1 left) — port of weapon_def.gd <c>track_point</c>. Only X mirrors:
        /// a downward swing stays downward when the body turns around.</summary>
        public Vector2 TrackPoint(float p, float facing)
        {
            if (hitTrack == null || hitTrack.Length == 0)
                return new Vector2(k_FallbackTrackX * facing, k_FallbackTrackY);
            if (hitTrack.Length == 1)
                return new Vector2(hitTrack[0].x * facing, hitTrack[0].y);

            // Godot: f = clampf(p,0,1) * (n-1); i = mini(int(f), n-2); lerp(track[i], track[i+1],
            // f - i). p is clamped non-negative first, so the C# truncating cast matches int().
            float f = Mathf.Clamp01(p) * (hitTrack.Length - 1);
            int i = Mathf.Min((int)f, hitTrack.Length - 2);
            Vector2 v = Vector2.Lerp(hitTrack[i], hitTrack[i + 1], f - i);
            return new Vector2(v.x * facing, v.y);
        }
    }
}
