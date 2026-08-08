using UnityEngine;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// One service verb as an atom: <see cref="LevelService.ReturnFromExpedition"/> — travel
    /// back to wherever the expedition was entered from, spending the service's memory of it.
    /// Fails when there is nothing to return to, so a tree can branch on "not on an
    /// expedition". Instant — the transition itself is the travel state's job.
    /// </summary>
    [CreateAssetMenu(menuName = "Draw To Play/Tasks/Return From Expedition",
        fileName = "ReturnFromExpedition")]
    [StateTreeCategory("Tasks/Levels", "Travel back to where the expedition was entered from")]
    public sealed class ReturnFromExpeditionTask : StateTreeTaskAsset
    {
        public StateTreeServiceRef<LevelService> service = new StateTreeServiceRef<LevelService>();

        public override StateTreeStatus OnTick(StateTreeContext context, float deltaTime)
        {
            LevelService levelService = service.service
                ?? StateTreeContextHost.FindService<LevelService>(
                    context != null ? context.owner : null);
            if (levelService == null)
                return StateTreeStatus.Failure;

            return levelService.ReturnFromExpedition()
                ? StateTreeStatus.Success
                : StateTreeStatus.Failure;
        }
    }
}
