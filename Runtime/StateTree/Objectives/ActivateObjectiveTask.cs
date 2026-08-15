using UnityEngine;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>Point the quest line somewhere — a tree state activating a picked objective
    /// row (a level's opening beat, a dialog's reward step). The chain carries on from the
    /// row's own nextOnComplete wires.</summary>
    [StateTreeCategory("Tasks/Objectives", "Activate a picked objective row")]
    public sealed class ActivateObjectiveTask : StateTreeTaskAsset
    {
        [Tooltip("The row to pursue — picked from the objective registry.")]
        public StateTreeEntryRef<ObjectiveDef> objective = new StateTreeEntryRef<ObjectiveDef>();

        public override StateTreeStatus OnTick(StateTreeContext context, float deltaTime)
        {
            if (context == null || context.owner == null)
                return StateTreeStatus.Failure;
            ObjectiveService service =
                StateTreeContextHost.FindService<ObjectiveService>(context.owner);
            if (service == null)
            {
                Debug.LogError("[Objective] no ObjectiveService reachable from '"
                    + context.owner.name + "'.", context.owner);
                return StateTreeStatus.Failure;
            }
            ObjectiveDef row = service.Find(objective.entryName);
            if (row == null)
            {
                Debug.LogError("[Objective] no objective row named '" + objective.entryName
                    + "' in the service's catalog.", context.owner);
                return StateTreeStatus.Failure;
            }
            service.Activate(row);
            return StateTreeStatus.Success;
        }
    }
}
