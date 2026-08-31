using UnityEngine;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// A state that RUNS A ZONE: the zone's stack — an ordered, reorderable list of objective
    /// rows — is the sequence, so a tree carries one state per zone instead of one state per
    /// objective. Asks on enter (the tree, not distance, says whose stack asks; no volume
    /// needed), resumes the zone's own cursor, completes when the stack runs past its end.
    /// Pre-empted, it releases the ask and KEEPS the cursor: an ancestor interrupt runs its
    /// side quest and re-entering resumes.
    /// </summary>
    [StateTreeCategory("Tasks/Objectives", "Run a zone's objective stack as this state")]
    public sealed class RunZoneTask : StateTreeTaskAsset
    {
        [Tooltip("The zone whose stack this state runs — picked from the zone registry.")]
        public StateTreeEntryRef<ZoneDef> zone = new StateTreeEntryRef<ZoneDef>();

        private ObjectiveService m_Service;
        private ZoneDef m_Row;

        public override void OnEnter(StateTreeContext context)
        {
            m_Service = context != null && context.owner != null
                ? StateTreeContextHost.FindService<ObjectiveService>(context.owner)
                : null;
            m_Row = m_Service != null ? m_Service.FindZone(zone.entryName) : null;
            if (m_Service != null && m_Row == null)
                Debug.LogError("[Objective] no zone row named '" + zone.entryName
                    + "' in the service's zone catalog.", context.owner);
            if (m_Row != null)
                m_Service.AskZone(m_Row);
        }

        public override StateTreeStatus OnTick(StateTreeContext context, float deltaTime)
        {
            if (m_Service == null || m_Row == null)
                return StateTreeStatus.Failure;
            return m_Service.ZoneDone(m_Row)
                ? StateTreeStatus.Success
                : StateTreeStatus.Running;
        }

        public override void OnExit(StateTreeContext context, StateTreeStatus status)
        {
            m_Service?.ReleaseZone(m_Row);
            m_Row = null;
            m_Service = null;
        }
    }
}
