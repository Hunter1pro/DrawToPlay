using System;
using System.Collections.Generic;
using UnityEngine;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// ONE ACTOR'S ABILITY STATE (M23) — the per-actor half of the ability service, the
    /// component both HT generations converged on: the shared catalog answers what an ability
    /// IS, this host owns what is RUNNING here. It runs the active ability's tree on its own
    /// executor (own context — two parallel executors must not share a parameter scope),
    /// applies the row's effect parts, carries statuses and runtime tags, counts cooldowns,
    /// and drives the two FSM behaviours. A WORLD CITIZEN, because [InjectOwner] is a world
    /// contract: a task's owner facets resolve through the registry, and an ability-bearing
    /// actor is a unit the world knows — the M21 composed-object pattern (body citizen +
    /// mind citizen + this) rather than a loose component only GetComponent can find.
    ///
    /// It drives the two FSM behaviours the Godot side proved and the Unity side listed as
    /// missing:
    ///
    /// THE FLOOR — when nothing is active and <see cref="defaultAbility"/> names a row, it
    /// activates. "Nothing running" means something instead of nothing, exactly the
    /// ability-host idle floor (and the same move as M22's implicit flow, one level up).
    ///
    /// THE CONTINUATION — when the active ability's tree FINISHES (M22's treeFinished; a
    /// cancelled ability returns nothing), <see cref="AbilityDef.nextOnFinish"/> activates:
    /// a combo is a wire between two rows. One hop per frame, the M22 discipline, so a cycle
    /// of instant abilities is a visible spin rather than a same-frame hang.
    /// </summary>
    [AddComponentMenu("Draw To Play/Services/Ability Host")]
    public sealed class AbilityHost : WorldObjectBehaviour
    {
        [Tooltip("The idle floor — activated whenever nothing else is. Empty = no floor: "
            + "the host only runs what something explicitly starts.")]
        public StateTreeEntryRef<AbilityDef> defaultAbility = new StateTreeEntryRef<AbilityDef>();

        /// <summary>The active ability's row, or null between abilities.</summary>
        public AbilityDef active { get; private set; }

        /// <summary>An ability ended: the row and how — Success for a tree that finished (or
        /// an ability with no tree, which finishes on arrival), Cancelled for a pre-emption.
        /// Fired before the continuation decides what runs next, so a listener sees the gap.</summary>
        public event Action<AbilityDef, StateTreeStatus> abilityFinished;

        /// <summary>(previous, current) — current null between abilities.</summary>
        public event Action<AbilityDef, AbilityDef> activeAbilityChanged;

        /// <summary>A cue part fired: its name, the ability it belongs to, the part row. A cue
        /// OBSERVES — listeners do the flash and the sound; nothing here mutates state.</summary>
        public event Action<string, AbilityDef, AbilityPartDef> cueFired;

        /// <summary>An effect landed on an attribute this host does not know natively
        /// (anything but 'health') — the game's hook: (attribute, signed magnitude, part).</summary>
        public event Action<string, float, AbilityPartDef> attributeApplied;

        private AbilityService m_Service;
        private StateTreeExecutor m_Executor;
        private bool m_TreeFinished;
        private AbilityDef m_PendingNext;
        private readonly Dictionary<string, float> m_ReadyAt = new Dictionary<string, float>();
        private readonly List<ActiveStatus> m_Statuses = new List<ActiveStatus>();

        /// <summary>One running Duration/Infinite effect: the authored part plus this
        /// application's clock. A class, not a struct — the tick loop mutates in place.</summary>
        private sealed class ActiveStatus
        {
            public AbilityPartDef part;
            public AbilityDef source;
            public int stacks;
            public float remaining;
            public float tickAccumulated;
        }

        /// <summary>The service, resolved lazily — a service resolved in Awake is a race
        /// (the M20 lesson), and one resolved per call is a dictionary hit.</summary>
        public AbilityService service
        {
            get
            {
                if (m_Service == null)
                    m_Service = StateTreeContextHost.FindService<AbilityService>(gameObject);
                return m_Service;
            }
        }

        /// <summary>Whether the owner currently holds a tag — the active ability's activation
        /// tags plus every active status's granted tags. The single "is this happening to me"
        /// read conditions gate on.</summary>
        public bool HasTag(string tag)
        {
            if (string.IsNullOrEmpty(tag))
                return false;
            if (active != null && active.activationTags != null
                && active.activationTags.Contains(tag))
                return true;
            for (int i = 0; i < m_Statuses.Count; i++)
            {
                List<string> granted = m_Statuses[i].part.grantedTags;
                if (granted != null && granted.Contains(tag))
                    return true;
            }
            return false;
        }

        /// <summary>Stack count of the named Duration effect, 0 when it is not running.</summary>
        public int StacksOf(string effectName)
        {
            for (int i = 0; i < m_Statuses.Count; i++)
            {
                if (string.Equals(m_Statuses[i].part.name, effectName, StringComparison.Ordinal))
                    return m_Statuses[i].stacks;
            }
            return 0;
        }

        public bool CooldownReady(AbilityDef def)
        {
            return def == null
                || !m_ReadyAt.TryGetValue(def.name, out float readyAt)
                || Time.time >= readyAt;
        }

        /// <summary>Activate by row name — the string flavor for callers without a wire.</summary>
        public bool Activate(string abilityName)
        {
            AbilityService resolved = service;
            return resolved != null && Activate(resolved.Find(abilityName));
        }

        /// <summary>
        /// Activate a row: gate through the service (cooldown, block tags, one-at-a-time),
        /// cancel the active ability when the incoming one's cancel tags say so, apply the
        /// row's effect parts, and start its tree — or finish immediately for a row without
        /// one. False = refused, and the caller decides whether that is worth a log.
        /// </summary>
        public bool Activate(AbilityDef def)
        {
            AbilityService resolved = service;
            if (def == null || resolved == null)
                return false;
            if (!resolved.CanActivate(this, def))
                return false;

            if (active != null)
                EndActive(StateTreeStatus.Cancelled, runContinuation: false);

            // Previous is always null here — a cancelled predecessor announced its own
            // (ended → null) transition inside EndActive, so this is the (null → def) half.
            active = def;
            activeAbilityChanged?.Invoke(null, def);

            ApplyParts(def);

            if (def.tree == null)
            {
                // An ability that is only its payload: applied, done, and the continuation
                // gets its turn next frame (one hop per frame — see m_PendingNext).
                FinishActive(StateTreeStatus.Success);
                return true;
            }

            m_TreeFinished = false;
            m_Executor = new StateTreeExecutor
            {
                data = def.tree,
                owner = gameObject,
                logLabel = "Ability '" + def.name + "'",
                logContext = this,
                parameterOverrides = def.parameters != null ? def.parameters.values : null
            };
            m_Executor.treeFinished += OnTreeFinished;
            m_Executor.StartTree();
            if (!m_Executor.isRunning)
            {
                // A tree that refused to start (bad data — already logged) must not leave the
                // host claiming an ability is active.
                FinishActive(m_TreeFinished ? StateTreeStatus.Success : StateTreeStatus.Failure);
            }
            return true;
        }

        /// <summary>Cancel the active ability, if any — a pre-emption, so no continuation.</summary>
        public void Cancel()
        {
            if (active != null)
                EndActive(StateTreeStatus.Cancelled, runContinuation: false);
        }

        private void OnTreeFinished()
        {
            m_TreeFinished = true;
        }

        private void Update()
        {
            Tick(Time.deltaTime);
        }

        /// <summary>One frame of the host, callable headless — the executor's TickTree rule
        /// applied to the component that wraps one, so EditMode tests own the clock.</summary>
        public void Tick(float deltaTime)
        {
            TickStatuses(deltaTime);

            if (m_Executor != null)
            {
                m_Executor.TickTree(Time.deltaTime);
                if (m_TreeFinished)
                    FinishActive(StateTreeStatus.Success);
                else if (m_Executor != null && !m_Executor.isRunning)
                    FinishActive(StateTreeStatus.Cancelled);
            }

            // The continuation's hop, one per frame; then the floor. Order matters: a pending
            // next outranks the default, or every combo would detour through idle.
            if (active == null && m_PendingNext != null)
            {
                AbilityDef next = m_PendingNext;
                m_PendingNext = null;
                Activate(next);
            }
            if (active == null && m_PendingNext == null
                && !string.IsNullOrEmpty(defaultAbility.entryName))
                Activate(defaultAbility.entryName);
        }

        protected override void OnDisable()
        {
            // A deactivated actor tears its run down like any pre-empted task would.
            Cancel();
            m_Statuses.Clear();
            m_PendingNext = null;
            base.OnDisable();
        }

        private void FinishActive(StateTreeStatus status)
        {
            EndActive(status, runContinuation: status == StateTreeStatus.Success);
        }

        private void EndActive(StateTreeStatus status, bool runContinuation)
        {
            AbilityDef ended = active;
            if (ended == null)
                return;

            if (m_Executor != null)
            {
                m_Executor.treeFinished -= OnTreeFinished;
                if (m_Executor.isRunning)
                    m_Executor.StopTree();
                m_Executor = null;
            }

            if (ended.cooldownSeconds > 0f)
                m_ReadyAt[ended.name] = Time.time + ended.cooldownSeconds;

            active = null;
            abilityFinished?.Invoke(ended, status);
            activeAbilityChanged?.Invoke(ended, null);

            if (runContinuation && !string.IsNullOrEmpty(ended.nextOnFinish.entryName))
            {
                AbilityService resolved = service;
                m_PendingNext = resolved != null
                    ? resolved.Find(ended.nextOnFinish.entryName)
                    : null;
            }
        }

        // ---- the payload -------------------------------------------------------------------

        private void ApplyParts(AbilityDef def)
        {
            if (def.parts == null)
                return;
            for (int i = 0; i < def.parts.Count; i++)
            {
                AbilityPartDef part = def.parts[i];
                if (part == null || part.kind != AbilityPartDef.EffectKind)
                    continue;
                ApplyEffect(part, def);
            }
        }

        private void ApplyEffect(AbilityPartDef part, AbilityDef source)
        {
            if (part.duration == AbilityEffectDuration.Instant)
            {
                ApplyMagnitude(part, part.magnitude, firstApplication: true);
            }
            else
            {
                StackStatus(part, source);
                // The application itself still lands once, up front — a poison's first tick
                // is the bite.
                if (part.tickInterval > 0f)
                    ApplyMagnitude(part, part.magnitude * StacksOf(part.name),
                        firstApplication: true);
            }
            FireCues(part, source);
        }

        private void StackStatus(AbilityPartDef part, AbilityDef source)
        {
            ActiveStatus held = null;
            for (int i = 0; i < m_Statuses.Count && held == null; i++)
            {
                if (string.Equals(m_Statuses[i].part.name, part.name, StringComparison.Ordinal))
                    held = m_Statuses[i];
            }

            if (held == null)
            {
                m_Statuses.Add(new ActiveStatus
                {
                    part = part, source = source, stacks = 1, remaining = part.seconds
                });
                return;
            }

            switch (part.stacking)
            {
                case AbilityStacking.Replace:
                    held.part = part;
                    held.stacks = 1;
                    held.remaining = part.seconds;
                    break;
                case AbilityStacking.RefreshDuration:
                    held.remaining = part.seconds;
                    break;
                case AbilityStacking.AddStacksKeepDuration:
                    held.stacks = Mathf.Min(held.stacks + 1, Mathf.Max(1, part.maxStacks));
                    break;
                default:   // AddStacksRefreshDuration — the HT default
                    held.stacks = Mathf.Min(held.stacks + 1, Mathf.Max(1, part.maxStacks));
                    held.remaining = part.seconds;
                    break;
            }
        }

        private void TickStatuses(float deltaTime)
        {
            for (int i = m_Statuses.Count - 1; i >= 0; i--)
            {
                ActiveStatus status = m_Statuses[i];
                if (status.part.duration == AbilityEffectDuration.Duration)
                    status.remaining -= deltaTime;

                if (status.part.tickInterval > 0f)
                {
                    status.tickAccumulated += deltaTime;
                    while (status.tickAccumulated >= status.part.tickInterval)
                    {
                        status.tickAccumulated -= status.part.tickInterval;
                        ApplyMagnitude(status.part, status.part.magnitude * status.stacks,
                            firstApplication: false);
                    }
                }

                if (status.part.duration == AbilityEffectDuration.Duration
                    && status.remaining <= 0f)
                    m_Statuses.RemoveAt(i);
            }
        }

        /// <summary>One signed delta onto an attribute. 'health' reaches the owner's
        /// <see cref="HealthComponent"/> natively — through i-frames on a first application
        /// (a hit is a hit) and past them on periodic ticks (a poison that i-frames could
        /// shrug off would never tick). Anything else is the game's business via
        /// <see cref="attributeApplied"/>.</summary>
        private void ApplyMagnitude(AbilityPartDef part, float magnitude, bool firstApplication)
        {
            if (Mathf.Approximately(magnitude, 0f))
                return;

            if (string.Equals(part.attribute, "health", StringComparison.Ordinal))
            {
                var health = GetComponent<HealthComponent>();
                if (health != null)
                {
                    if (magnitude > 0f)
                        health.Heal(magnitude);
                    else if (firstApplication)
                        health.TakeDamage(-magnitude);
                    else
                        health.TickDamage(-magnitude);
                    return;
                }
            }
            attributeApplied?.Invoke(part.attribute, magnitude, part);
        }

        private void FireCues(AbilityPartDef effect, AbilityDef source)
        {
            if (effect.children == null)
                return;
            for (int i = 0; i < effect.children.Count; i++)
            {
                AbilityPartDef child = effect.children[i];
                if (child != null && child.kind == AbilityPartDef.CueKind
                    && !string.IsNullOrEmpty(child.cueName))
                    cueFired?.Invoke(child.cueName, source, child);
            }
        }
    }
}
