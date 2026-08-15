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
            + "own regardless, so an empty list is a plain actor, not a broken one. A seed "
            + "OVERRIDES the table for its attribute — an authored exception the level "
            + "never touches.")]
        public List<Seed> seeds = new List<Seed>();

        [Tooltip("The balance sheet this actor reads its bases from — level → value per "
            + "attribute, one page for the whole world scale. Empty = seeds and domain "
            + "defaults only.")]
        public ProgressionTable table;

        [Tooltip("Where this actor stands on the table's scale. One int is the whole "
            + "authored difference between a fresh raider and a veteran.")]
        public int level = 1;

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

        /// <summary>Attributes an explicit seed authored — the table never re-bases these,
        /// at creation or on a level change: the exception outranks the sheet.</summary>
        private readonly HashSet<string> m_SeededNames = new HashSet<string>(StringComparer.Ordinal);

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
                if (seed == null || string.IsNullOrEmpty(seed.attribute.entryName))
                    continue;
                m_SeededNames.Add(seed.attribute.entryName);
                Ensure(seed.attribute.entryName, seed.baseValue);
            }
            // The table speaks after the seeds (Ensure is a no-op where a seed already did)
            // and before any domain fallback ever runs — the precedence chain in one place:
            // seed > table > AttributeDef default > domain component (maxHP).
            ApplyTable(rebase: false);
        }

        /// <summary>Derive bases from the table at the current level. <paramref name="rebase"/>
        /// false only creates missing entries (first seeding); true moves existing bases too
        /// (a level change). Seeded attributes are never touched either way.</summary>
        private void ApplyTable(bool rebase)
        {
            if (table == null)
                return;
            for (int i = 0; i < table.entries.Count; i++)
            {
                ProgressionRow row = table.entries[i];
                if (row == null || string.IsNullOrEmpty(row.attribute.entryName))
                    continue;
                string attributeName = row.attribute.entryName;
                if (m_SeededNames.Contains(attributeName))
                    continue;
                float value = row.Evaluate(level);
                if (!m_Entries.TryGetValue(attributeName, out Entry entry))
                {
                    m_Entries[attributeName] = new Entry
                    {
                        baseValue = value,
                        current = value
                    };
                }
                else if (rebase)
                {
                    // Re-asserting the same level is silence, not a re-announcement — a
                    // state re-entered may set its level every time.
                    if (Mathf.Approximately(entry.baseValue, value))
                        continue;
                    // A pool that was FULL follows its cap — levelling a fresh spawn or an
                    // unhurt actor lands them full. A wounded pool keeps its wound (a
                    // level-up heal is an EFFECT the game applies, not a side effect here).
                    bool wasFull = entry.current >= EffectiveOf(entry)
                        || Mathf.Approximately(entry.current, EffectiveOf(entry));
                    entry.baseValue = value;
                    if (wasFull)
                    {
                        float previous = entry.current;
                        entry.current = EffectiveOf(entry);
                        if (!Mathf.Approximately(previous, entry.current))
                            changed?.Invoke(attributeName, previous, entry.current);
                    }
                    else
                    {
                        ClampToCap(attributeName, entry);
                    }
                    effectiveChanged?.Invoke(attributeName);
                }
            }
        }

        /// <summary>Move this actor on the table's scale: every table-owned base re-derives
        /// at the new level. Modifiers survive (they reshape whatever the base is); full
        /// pools follow their caps, wounded ones keep their wounds.</summary>
        public void SetLevel(int newLevel)
        {
            EnsureSeeds();
            level = Mathf.Max(1, newLevel);
            ApplyTable(rebase: true);
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
