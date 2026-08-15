using System;
using System.Collections.Generic;
using UnityEngine;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// ONE ACTOR'S ATTRIBUTES — the GAS step: named values with a BASE, a consumable CURRENT,
    /// and MODIFIERS that adjust the effective value and REVERT when whatever granted them
    /// ends (the thing the old health-only model could not say: +2 max health while a status
    /// runs, a slow that undoes itself).
    ///
    /// The model, deliberately small:
    /// - EFFECTIVE(name) = (base + Σ additive) × Π multiplicative — the derived read a STAT
    ///   uses (speed, armor) and the CAP a POOL clamps against (health, stamina).
    /// - CURRENT is the consumable state a pool spends and restores. Consume never clamps
    ///   from below (overkill is information a damage number wants); Restore clamps to the
    ///   effective cap. A stat simply never consumes.
    ///
    /// Names come from <see cref="AttributeDef"/> rows where authored (seeds, effects picking
    /// attributes) and are plain strings at runtime — the RegistryEntry convention. Domain
    /// RULES stay on domain components: <see cref="HealthComponent"/> is the health
    /// attribute's rulekeeper (guard window, death, destruction), running on the number that
    /// lives here.
    /// </summary>
    [AddComponentMenu("Draw To Play/Combat/Attributes")]
    public sealed class AttributeComponent : MonoBehaviour
    {
        /// <summary>One authored starting value — the row picked, the base it starts at.</summary>
        [Serializable]
        public sealed class Seed
        {
            public StateTreeEntryRef<AttributeDef> attribute = new StateTreeEntryRef<AttributeDef>();

            [Tooltip("The starting base for this actor.")]
            public float baseValue = 100f;
        }

        [Tooltip("This actor's starting attributes. Domain components (health) ensure their "
            + "own regardless, so an empty list is a plain actor, not a broken one.")]
        public List<Seed> seeds = new List<Seed>();

        /// <summary>CURRENT changed: (name, previous, current). Consumes and restores only —
        /// modifier changes announce through <see cref="effectiveChanged"/>.</summary>
        public event Action<string, float, float> changed;

        /// <summary>The EFFECTIVE value changed — a modifier came or went, or the base moved.</summary>
        public event Action<string> effectiveChanged;

        /// <summary>A granted modifier's receipt — hold it to revert what you granted.</summary>
        public sealed class ModifierHandle
        {
            internal string attribute;
            internal float additive;
            internal float multiplicative = 1f;
        }

        private sealed class Entry
        {
            public float baseValue;
            public float current;
            public readonly List<ModifierHandle> modifiers = new List<ModifierHandle>();
        }

        private readonly Dictionary<string, Entry> m_Entries =
            new Dictionary<string, Entry>(StringComparer.Ordinal);

        private bool m_Seeded;

        private void Awake()
        {
            EnsureSeeds();
        }

        private void EnsureSeeds()
        {
            if (m_Seeded)
                return;
            m_Seeded = true;
            for (int i = 0; i < seeds.Count; i++)
            {
                Seed seed = seeds[i];
                if (seed != null && !string.IsNullOrEmpty(seed.attribute.entryName))
                    Ensure(seed.attribute.entryName, seed.baseValue);
            }
        }

        /// <summary>The attribute exists from here on — created at <paramref name="baseValue"/>
        /// when new, left untouched when already present (a domain component ensuring after a
        /// seed must not reset the seed).</summary>
        public void Ensure(string attributeName, float baseValue)
        {
            EnsureSeeds();
            if (string.IsNullOrEmpty(attributeName) || m_Entries.ContainsKey(attributeName))
                return;
            m_Entries[attributeName] = new Entry
            {
                baseValue = baseValue,
                current = baseValue
            };
        }

        public bool Has(string attributeName)
        {
            EnsureSeeds();
            return !string.IsNullOrEmpty(attributeName) && m_Entries.ContainsKey(attributeName);
        }

        /// <summary>The consumable CURRENT — a pool's state. Zero for an unknown name.</summary>
        public float Value(string attributeName)
        {
            EnsureSeeds();
            return m_Entries.TryGetValue(attributeName, out Entry entry) ? entry.current : 0f;
        }

        /// <summary>(base + Σ add) × Π mult — the derived read and the pool cap. Zero for an
        /// unknown name.</summary>
        public float Effective(string attributeName)
        {
            EnsureSeeds();
            return m_Entries.TryGetValue(attributeName, out Entry entry) ? EffectiveOf(entry) : 0f;
        }

        public float BaseOf(string attributeName)
        {
            EnsureSeeds();
            return m_Entries.TryGetValue(attributeName, out Entry entry) ? entry.baseValue : 0f;
        }

        /// <summary>Move the BASE itself — a permanent change (levelling, equipment that is
        /// not a revertible modifier). Current re-clamps to the new cap.</summary>
        public void SetBase(string attributeName, float baseValue)
        {
            EnsureSeeds();
            if (!m_Entries.TryGetValue(attributeName, out Entry entry))
                return;
            entry.baseValue = baseValue;
            ClampToCap(attributeName, entry);
            effectiveChanged?.Invoke(attributeName);
        }

        /// <summary>Spend from the pool. NOT clamped from below — overkill is information —
        /// and never gated here: guard windows are a domain rule (health's), applied by the
        /// component that owns them before calling this.</summary>
        public void Consume(string attributeName, float amount)
        {
            EnsureSeeds();
            if (!m_Entries.TryGetValue(attributeName, out Entry entry))
                return;
            float previous = entry.current;
            entry.current -= amount;
            if (!Mathf.Approximately(previous, entry.current))
                changed?.Invoke(attributeName, previous, entry.current);
        }

        /// <summary>Give back to the pool, clamped to the effective cap.</summary>
        public void Restore(string attributeName, float amount)
        {
            EnsureSeeds();
            if (!m_Entries.TryGetValue(attributeName, out Entry entry))
                return;
            float previous = entry.current;
            entry.current = Mathf.Min(entry.current + amount, EffectiveOf(entry));
            if (!Mathf.Approximately(previous, entry.current))
                changed?.Invoke(attributeName, previous, entry.current);
        }

        /// <summary>Set the pool outright — resets, loads.</summary>
        public void SetCurrent(string attributeName, float value)
        {
            EnsureSeeds();
            if (!m_Entries.TryGetValue(attributeName, out Entry entry))
                return;
            float previous = entry.current;
            entry.current = value;
            if (!Mathf.Approximately(previous, entry.current))
                changed?.Invoke(attributeName, previous, entry.current);
        }

        /// <summary>
        /// Grant a revertible modifier: effective value gains <paramref name="additive"/> and
        /// multiplies by <paramref name="multiplicative"/> until the handle is removed — the
        /// GAS contract a duration effect needs (apply on application, revert on expiry,
        /// nothing drifts). A cap that RISES does not fill the pool; one that falls clamps it.
        /// </summary>
        public ModifierHandle AddModifier(string attributeName, float additive,
            float multiplicative = 1f)
        {
            EnsureSeeds();
            if (!m_Entries.TryGetValue(attributeName, out Entry entry))
                return null;
            var handle = new ModifierHandle
            {
                attribute = attributeName,
                additive = additive,
                multiplicative = Mathf.Approximately(multiplicative, 0f) ? 1f : multiplicative
            };
            entry.modifiers.Add(handle);
            ClampToCap(attributeName, entry);
            effectiveChanged?.Invoke(attributeName);
            return handle;
        }

        /// <summary>Revert a granted modifier. Null-safe; a handle already removed is quiet.</summary>
        public void RemoveModifier(ModifierHandle handle)
        {
            if (handle == null || !m_Entries.TryGetValue(handle.attribute, out Entry entry))
                return;
            if (!entry.modifiers.Remove(handle))
                return;
            ClampToCap(handle.attribute, entry);
            effectiveChanged?.Invoke(handle.attribute);
        }

        private void ClampToCap(string attributeName, Entry entry)
        {
            float cap = EffectiveOf(entry);
            if (entry.current > cap)
            {
                float previous = entry.current;
                entry.current = cap;
                changed?.Invoke(attributeName, previous, entry.current);
            }
        }

        private static float EffectiveOf(Entry entry)
        {
            float additive = entry.baseValue;
            float multiplicative = 1f;
            for (int i = 0; i < entry.modifiers.Count; i++)
            {
                additive += entry.modifiers[i].additive;
                multiplicative *= entry.modifiers[i].multiplicative;
            }
            return additive * multiplicative;
        }
    }
}
