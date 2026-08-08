using System;
using Cysharp.Threading.Tasks;
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
                // Injected when this atom is a STATE task; looked up when it runs INSIDE a
                // task graph — the VM's tasks never pass through the executor's injection,
                // so a graph-hosted atom falls back to the spine at tick time.
                LevelService levelService = service.service
                    ?? StateTreeContextHost.FindService<LevelService>(
                        context != null ? context.owner : null);
                if (levelService == null)
                    return StateTreeStatus.Failure;

                // Fire-and-observe: the tick model polls the flags, so the UniTask's only
                // job here is to land them. Forget() is safe — LoadAsync owns its own
                // cancellation (the service's destruction) and never throws past it.
                Await(levelService.LoadAsync(target)).Forget();
            }

            if (!m_Done)
                return StateTreeStatus.Running;
            return m_Ok ? StateTreeStatus.Success : StateTreeStatus.Failure;
        }

        private async UniTask Await(UniTask<bool> load)
        {
            m_Ok = await load;
            m_Done = true;
        }

        private LevelDef ResolveTarget(StateTreeContext context)
        {
            string nameKey = levelNameKey;
            if (context != null && !string.IsNullOrEmpty(nameKey)
                && context.blackboard.TryGetValue(nameKey, out object held)
                && held is string levelName && !string.IsNullOrEmpty(levelName))
            {
                LevelDef dynamic = FindByName(context, levelName);
                if (dynamic != null)
                    return dynamic;
                Debug.LogWarning($"[LoadLevel] '{levelName}' (from key '{nameKey}') names no "
                    + "level in any reachable registry — falling back to the authored entry.",
                    this);
            }

            if (level.entry != null)
                return level.entry;

            // The free-typed / graph-hosted path: a name-only reference the injection pass
            // never saw resolves here, against the same registries.
            var reference = (IStateTreeEntryRef)level;
            return string.IsNullOrEmpty(reference.EntryName)
                ? null
                : FindByName(context, reference.EntryName);
        }

        /// <summary>The injected registry when this is a state task, else the nearest host
        /// chain's tree registries — the tick-time mirror of the executor's resolution, for
        /// the graph-hosted case.</summary>
        private LevelDef FindByName(StateTreeContext context, string levelName)
        {
            if (levels.TryGet(levelName, out LevelDef fromInjected))
                return fromInjected;

            StateTreeContextHost host = StateTreeContextHost.ResolveNearest(
                context != null ? context.owner : null);
            int guard = 0;
            while (host != null && ++guard < 32)
            {
                var registries = host.tree != null ? host.tree.registries : null;
                for (int i = 0; registries != null && i < registries.Count; i++)
                {
                    if (registries[i] != null
                        && registries[i].FindByName(levelName) is LevelDef found)
                        return found;
                }
                host = host.ParentHost;
            }
            return null;
        }
    }
}
