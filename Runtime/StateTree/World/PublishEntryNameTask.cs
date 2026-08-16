using UnityEngine;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// WHICH ROW IS THAT THING AN INSTANCE OF? — the placer pattern, read back.
    ///
    /// A placement gives its citizen the name of the catalog row it was built from (this zone,
    /// this dialog, this recipe). Everything downstream then wants that name as a STRING: a
    /// request value, a dialog to open, a recipe to make. Without this the only way to get it
    /// was a component reference and a cast, which is the thing the whole placer pattern exists
    /// to avoid — and it would have to be written again for every kind of citizen.
    ///
    /// Reads the object held under <see cref="fromKey"/> (or the owner itself when that is
    /// empty) and writes its entry name under <see cref="intoKey"/>. Fails and clears when
    /// there is nothing to read, because a stale row name is worse than none: it would craft
    /// the last station's recipe at this one.
    /// </summary>
    [CreateAssetMenu(menuName = "Draw To Play/AI/Tasks/Publish Entry Name",
        fileName = "PublishEntryName")]
    [StateTreeCategory("Tasks/World", "Publish which catalog row an object is an instance of")]
    public sealed class PublishEntryNameTask : StateTreeTaskAsset
    {
        [Tooltip("The key holding the object to ask. Empty = the owner of this tree.")]
        [StateTreeKey(StateTreeKeyKind.Object, any: true)]
        public StateTreeKeyField fromKey = new StateTreeKeyField();

        [Tooltip("Where the row name lands.")]
        [StateTreeKey(StateTreeKeyKind.String, any: true)]
        public StateTreeKeyField intoKey = new StateTreeKeyField("entry");

        public override StateTreeStatus OnTick(StateTreeContext context, float deltaTime)
        {
            if (context == null || string.IsNullOrEmpty(intoKey))
                return StateTreeStatus.Failure;

            GameObject subject = context.owner;
            string source = fromKey;
            if (!string.IsNullOrEmpty(source))
            {
                subject = context.blackboard.TryGetValue(source, out object held)
                    ? held as GameObject ?? (held as Component)?.gameObject
                    : null;
            }

            // ANY of the citizens on the object, because a composed object carries several and
            // only one of them was given a row name (the OutpostPlacement lesson: asking for
            // "a WorldObjectBehaviour" returns whichever was added first).
            string entry = "";
            if (subject != null)
            {
                WorldObjectBehaviour[] citizens = subject.GetComponents<WorldObjectBehaviour>();
                for (int i = 0; i < citizens.Length; i++)
                {
                    if (citizens[i] == null || string.IsNullOrEmpty(citizens[i].entryName))
                        continue;
                    entry = citizens[i].entryName;
                    break;
                }
            }

            if (string.IsNullOrEmpty(entry))
            {
                context.blackboard.Remove(intoKey);
                return StateTreeStatus.Failure;
            }

            context.blackboard[intoKey] = entry;
            return StateTreeStatus.Success;
        }
    }
}
