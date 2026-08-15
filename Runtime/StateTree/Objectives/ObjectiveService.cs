using System;
using System.Collections.Generic;
using UnityEngine;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// THE QUEST LINE'S KEEPER (M24) — one current objective, watched four ways. The
    /// toolset side owns the SEMANTICS: what each kind means, when it completes, what the
    /// chain does next, and where the arrow should point (the nearest citizen carrying the
    /// row's tag — nearest wins, always). The GAME owns the FACTS and reports them through
    /// the small surface here — <see cref="ReportKill"/>, <see cref="ReportPickupCount"/>,
    /// <see cref="ReportDialogFinished"/> — so this service references no game system, and
    /// a different game wires a different bridge. MoveTo needs no report at all: the world
    /// registry and the player's position are toolset facts, checked here.
    /// </summary>
    [AddComponentMenu("Draw To Play/Services/Objective Service")]
    public sealed class ObjectiveService : StateTreeServiceBehaviour
    {
        [Tooltip("The declaration this service runs: scope and the objective registry.")]
        public ServiceDef definition;

        [Tooltip("Activated once when the service starts, empty = nothing runs until an "
            + "ActivateObjectiveTask (or code) asks. The chain takes it from there.")]
        public StateTreeEntryRef<ObjectiveDef> startingObjective = new StateTreeEntryRef<ObjectiveDef>();

        /// <summary>The objective being pursued, or null between chains.</summary>
        public ObjectiveDef current { get; private set; }

        /// <summary>Kills landed / items carried toward <see cref="ObjectiveDef.count"/>.</summary>
        public int progress { get; private set; }

        /// <summary>Current or progress moved — what the HUD widget redraws on.</summary>
        public event Action changed;

        /// <summary>An objective completed — fired before the chain activates the next.</summary>
        public event Action<ObjectiveDef> completedObjective;

        private bool m_AutoStarted;
        private readonly List<WorldObjectBehaviour> m_Buffer = new List<WorldObjectBehaviour>();

        public ObjectiveRegistry catalog =>
            definition != null ? definition.registry as ObjectiveRegistry : null;

        public ObjectiveDef Find(string objectiveName)
        {
            ObjectiveRegistry registry = catalog;
            return registry != null && !string.IsNullOrEmpty(objectiveName)
                ? registry.FindByName(objectiveName) as ObjectiveDef
                : null;
        }

        public void Activate(ObjectiveDef objective)
        {
            current = objective;
            progress = 0;
            changed?.Invoke();
        }

        /// <summary>Complete the CURRENT objective and let the chain speak: its
        /// nextOnComplete activates, or the line ends with nothing current.</summary>
        public void Complete()
        {
            ObjectiveDef done = current;
            if (done == null)
                return;
            current = null;
            progress = 0;
            completedObjective?.Invoke(done);
            ObjectiveDef next = Find(done.nextOnComplete.entryName);
            current = next;
            changed?.Invoke();
        }

        // ---- the report surface: the game's facts arrive here -------------------------

        /// <summary>Somebody the game counts as an enemy went down. Filtered by the row's
        /// tag when it has one; the victim rides along so the filter can ask it.</summary>
        public void ReportKill(WorldObjectBehaviour victim)
        {
            if (current == null || current.kind != ObjectiveKind.EnemyKill)
                return;
            if (!string.IsNullOrEmpty(current.targetTag)
                && (victim == null || !victim.HasTag(current.targetTag)))
                return;
            progress += 1;
            changed?.Invoke();
            if (progress >= Mathf.Max(1, current.count))
                Complete();
        }

        /// <summary>How many of an item the player carries NOW — absolute, not a delta, so
        /// dropping an item honestly un-progresses the objective.</summary>
        public void ReportPickupCount(string itemName, int carried)
        {
            if (current == null || current.kind != ObjectiveKind.Pickup)
                return;
            if (!string.Equals(current.target.entryName, itemName, StringComparison.Ordinal))
                return;
            int goal = Mathf.Max(1, current.count);
            int clamped = Mathf.Clamp(carried, 0, goal);
            if (clamped != progress)
            {
                progress = clamped;
                changed?.Invoke();
            }
            if (carried >= goal)
                Complete();
        }

        /// <summary>A conversation finished — by the dialog ROW's name, the registry key.</summary>
        public void ReportDialogFinished(string dialogRowName)
        {
            if (current == null || current.kind != ObjectiveKind.Dialog)
                return;
            if (string.Equals(current.target.entryName, dialogRowName, StringComparison.Ordinal))
                Complete();
        }

        // ---- what the toolset checks for itself ---------------------------------------

        private void Update()
        {
            if (!m_AutoStarted)
            {
                if (string.IsNullOrEmpty(startingObjective.entryName))
                {
                    m_AutoStarted = true;
                }
                else
                {
                    ObjectiveDef row = Find(startingObjective.entryName);
                    if (row != null)
                    {
                        m_AutoStarted = true;
                        if (current == null)
                            Activate(row);
                    }
                }
            }

            if (current != null && current.kind == ObjectiveKind.MoveTo)
                CheckArrival();
        }

        /// <summary>The MoveTo watcher: nearest zone carrying the row's tag, arrived when
        /// the player stands within its radius (the zone's own wins over the row's).
        /// Public so tests drive it without a frame.</summary>
        public void CheckArrival()
        {
            if (current == null || current.kind != ObjectiveKind.MoveTo)
                return;
            StateTreeContextHost player =
                StateTreeContextHost.Resolve(gameObject, StateTreeContextKind.Player);
            if (player == null)
                return;
            WorldObjectBehaviour zone = Nearest(current.targetTag, player.transform.position);
            if (zone == null)
                return;
            float arrive = current.radius;
            var zoneBehaviour = zone.GetComponent<ObjectiveZoneBehaviour>();
            if (zoneBehaviour != null && zoneBehaviour.radius > 0f)
                arrive = zoneBehaviour.radius;
            Vector3 offset = zone.transform.position - player.transform.position;
            offset.y = 0f;
            if (offset.magnitude <= arrive)
                Complete();
        }

        /// <summary>Where the arrow points: the nearest citizen carrying the current row's
        /// tag, from the player's position. Null when the row is silent about place.</summary>
        public Vector3? CurrentTargetPosition()
        {
            if (current == null || string.IsNullOrEmpty(current.targetTag))
                return null;
            StateTreeContextHost player =
                StateTreeContextHost.Resolve(gameObject, StateTreeContextKind.Player);
            Vector3 from = player != null ? player.transform.position : transform.position;
            WorldObjectBehaviour nearest = Nearest(current.targetTag, from);
            return nearest != null ? nearest.transform.position : (Vector3?)null;
        }

        private WorldObjectBehaviour Nearest(string tag, Vector3 from)
        {
            WorldService world = StateTreeContextHost.FindService<WorldService>(gameObject);
            if (world == null || string.IsNullOrEmpty(tag))
                return null;
            m_Buffer.Clear();
            world.CollectByTag(tag, m_Buffer);
            WorldObjectBehaviour best = null;
            var bestDistance = float.MaxValue;
            for (int i = 0; i < m_Buffer.Count; i++)
            {
                if (m_Buffer[i] == null)
                    continue;
                Vector3 offset = m_Buffer[i].transform.position - from;
                offset.y = 0f;
                float distance = offset.sqrMagnitude;
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = m_Buffer[i];
                }
            }
            return best;
        }
    }
}
