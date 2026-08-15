using UnityEngine;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// SHOW A CUE ROW, from a tree (M23 tree-nesting) — the 'cue' state's atom, for feedback
    /// that belongs to a MOMENT of the ability rather than to an effect landing: a windup
    /// flash, a recovery shimmer. The row is picked (list the cue registry in the tree's
    /// Data); the prefab spawns at the owner — or at whoever a blackboard key names — and
    /// dies on the row's own clock. Observation only, like every cue: nothing here may touch
    /// combat state.
    /// </summary>
    [StateTreeCategory("Tasks/Abilities", "Spawn a picked cue row at self or a named target")]
    public sealed class ShowCueTask : StateTreeTaskAsset
    {
        [Tooltip("The cue row — picked from the registry the tree lists in Data.")]
        public StateTreeEntryRef<CueDef> cue = new StateTreeEntryRef<CueDef>();

        [Tooltip("Empty: the cue shows on the owner. Set: the blackboard key holding the "
            + "target (a GameObject or a component on it).")]
        [StateTreeKey(StateTreeKeyKind.String, any: true)]
        public StateTreeKeyField targetKey = new StateTreeKeyField();

        [InjectOwner] private AbilityHost m_Owner;

        public override StateTreeStatus OnTick(StateTreeContext context, float deltaTime)
        {
            if (m_Owner == null)
                return StateTreeStatus.Failure;

            AbilityService service = m_Owner.service;
            CueDef row = service != null ? service.FindCue(cue.entryName) : null;
            if (row == null)
            {
                Debug.LogError("[ShowCue] no cue row named '" + cue.entryName
                    + "' in the service's registries.", m_Owner);
                return StateTreeStatus.Failure;
            }

            Transform where = ResolveTarget(context);
            if (where != null && row.prefab != null)
            {
                GameObject shown = Object.Instantiate(row.prefab, where.position,
                    Quaternion.identity, row.attachToTarget ? where : null);
                if (Application.isPlaying)
                    Object.Destroy(shown, row.secondsAlive > 0f ? row.secondsAlive : 2f);
            }
            return StateTreeStatus.Success;
        }

        private Transform ResolveTarget(StateTreeContext context)
        {
            string key = (string)targetKey;
            if (string.IsNullOrEmpty(key))
                return m_Owner.transform;
            if (context == null || !context.blackboard.TryGetValue(key, out object held))
                return null;   // a named target that is absent is a miss, and a miss is quiet
            switch (held)
            {
                case GameObject go:
                    return go.transform;
                case Component component when component != null:
                    return component.transform;
                default:
                    return null;
            }
        }
    }
}
