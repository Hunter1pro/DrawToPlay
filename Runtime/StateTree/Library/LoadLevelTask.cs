using System;
using UnityEngine;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// One level-state's task (M16): run the transition to a level and hold Running until it
    /// lands. The session tree built of these IS the level graph — portals and expeditions
    /// are its transitions, so "how do I get back" is a visible wire, not saved state.
    ///
    /// All three wire fields on one atom, each doing its own job: <see cref="level"/> is the
    /// authored destination (⛃ from the tree's LevelDef registry);
    /// <see cref="levelNameKey"/> optionally makes the destination DYNAMIC — it names a
    /// blackboard key holding a level's name at entry time (the dev picker's route, a
    /// portal-with-a-destination-parameter's route), resolved through
    /// <see cref="levels"/>; and <see cref="service"/> is the capability, injected from the
    /// spine. A dynamic name that resolves nothing falls back to the authored entry, stated
    /// once.
    /// </summary>
    [CreateAssetMenu(menuName = "Draw To Play/Tasks/Load Level", fileName = "LoadLevel")]
    [StateTreeCategory("Tasks/Levels", "Transition to a level; Running until it lands")]
    public sealed class LoadLevelTask : StateTreeTaskAsset
    {
        public StateTreeEntryRef<LevelDef> level = new StateTreeEntryRef<LevelDef>();

        [StateTreeKey(StateTreeKeyKind.String)]
        public StateTreeKeyField levelNameKey = new StateTreeKeyField();

        public StateTreeRegistryRef<LevelDef> levels = new StateTreeRegistryRef<LevelDef>();

        public StateTreeServiceRef<LevelService> service = new StateTreeServiceRef<LevelService>();

        [NonSerialized] private bool m_Started;
        [NonSerialized] private bool m_Done;
        [NonSerialized] private bool m_Ok;

        public override void OnEnter(StateTreeContext context)
        {
            m_Started = false;
            m_Done = false;
            m_Ok = false;
        }

        public override StateTreeStatus OnTick(StateTreeContext context, float deltaTime)
        {
            if (!m_Started)
            {
                m_Started = true;

                LevelDef target = ResolveTarget(context);
                if (target == null)
                {
                    Debug.LogError("[LoadLevel] no destination: neither the entry reference "
                        + "nor the dynamic name resolved a level.", this);
                    return StateTreeStatus.Failure;
                }
                if (service.service == null)
                    return StateTreeStatus.Failure;

                service.service.Load(target, ok => { m_Done = true; m_Ok = ok; });
            }

            if (!m_Done)
                return StateTreeStatus.Running;
            return m_Ok ? StateTreeStatus.Success : StateTreeStatus.Failure;
        }

        private LevelDef ResolveTarget(StateTreeContext context)
        {
            string nameKey = levelNameKey;
            if (context != null && !string.IsNullOrEmpty(nameKey)
                && context.blackboard.TryGetValue(nameKey, out object held)
                && held is string levelName && !string.IsNullOrEmpty(levelName))
            {
                if (levels.TryGet(levelName, out LevelDef dynamic))
                    return dynamic;
                Debug.LogWarning($"[LoadLevel] '{levelName}' (from key '{nameKey}') names no "
                    + "level in the tree's registry — falling back to the authored entry.",
                    this);
            }

            return level.entry;
        }
    }
}
