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

        [InjectService] private LevelService m_Service;

        public override StateTreeStatus OnTick(StateTreeContext context, float deltaTime)
        {
            string levelName = expedition.entry != null
                ? expedition.entry.name
                : ((IStateTreeEntryRef)expedition).EntryName;
            if (string.IsNullOrEmpty(levelName))
            {
                Debug.LogError("[EnterExpedition] no expedition level named.", this);
                return StateTreeStatus.Failure;
            }

            m_Service.EnterExpedition(levelName);
            return StateTreeStatus.Success;
        }
    }
}
