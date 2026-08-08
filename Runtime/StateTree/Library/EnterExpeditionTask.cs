using UnityEngine;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// One service verb as an atom: <see cref="LevelService.EnterExpedition"/> — travel to an
    /// expedition level, the service remembering the way back. THIN on purpose (the
    /// re-brief's rule): policy lives in the service, this only lets a tree or a task graph
    /// speak the verb. Instant — the actual transition is served by the session tree's travel
    /// state, like every other request.
    /// </summary>
    [CreateAssetMenu(menuName = "Draw To Play/Tasks/Enter Expedition",
        fileName = "EnterExpedition")]
    [StateTreeCategory("Tasks/Levels", "Travel to an expedition, remembering the way back")]
    public sealed class EnterExpeditionTask : StateTreeTaskAsset
    {
        /// <summary>The expedition level (⛃ from the tree's LevelDef registry; a name-only
        /// reference when authored on a canvas).</summary>
        public StateTreeEntryRef<LevelDef> expedition = new StateTreeEntryRef<LevelDef>();

        public StateTreeServiceRef<LevelService> service = new StateTreeServiceRef<LevelService>();

        public override StateTreeStatus OnTick(StateTreeContext context, float deltaTime)
        {
            LevelService levelService = service.service
                ?? StateTreeContextHost.FindService<LevelService>(
                    context != null ? context.owner : null);
            if (levelService == null)
                return StateTreeStatus.Failure;

            string levelName = expedition.entry != null
                ? expedition.entry.name
                : ((IStateTreeEntryRef)expedition).EntryName;
            if (string.IsNullOrEmpty(levelName))
            {
                Debug.LogError("[EnterExpedition] no expedition level named.", this);
                return StateTreeStatus.Failure;
            }

            levelService.EnterExpedition(levelName);
            return StateTreeStatus.Success;
        }
    }
}
