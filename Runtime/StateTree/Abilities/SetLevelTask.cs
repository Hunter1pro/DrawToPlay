using UnityEngine;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// GIVE THE OWNER ITS LEVEL — the task end of "enemy at level 5 means something": the
    /// tree declares a level parameter, the placement row supplies the value (the same
    /// declared-parameter channel every other placement argument uses), the executor seeds
    /// it onto the blackboard, and this task hands it to the owner's
    /// <see cref="AttributeComponent"/> — whose table turns the one int into every base.
    /// Nothing here knows what level 5 IS; the table does.
    /// </summary>
    [StateTreeCategory("Tasks/Abilities", "Set the owner's attribute level from the declared level parameter")]
    public sealed class SetLevelTask : StateTreeTaskAsset
    {
        [Tooltip("Where the level is read — seeded by the tree's declared parameter of the "
            + "same name, overridden per placement row. Empty/absent falls back below.")]
        [StateTreeKey(StateTreeKeyKind.Float, any: true)]
        public StateTreeKeyField levelKey = new StateTreeKeyField("level");

        [Tooltip("Used when the key holds nothing — a tree run without the parameter still "
            + "means something.")]
        public int fallbackLevel = 1;

        public override StateTreeStatus OnTick(StateTreeContext context, float deltaTime)
        {
            if (context == null || context.owner == null)
                return StateTreeStatus.Failure;

            var attributes = context.owner.GetComponent<AttributeComponent>();
            if (attributes == null)
            {
                Debug.LogWarning("[SetLevel] owner '" + context.owner.name + "' has no "
                    + "AttributeComponent — a level with no numbers to mean.", context.owner);
                return StateTreeStatus.Success;
            }

            int level = fallbackLevel;
            if (context.blackboard.TryGetValue((string)levelKey, out object held))
            {
                switch (held)
                {
                    case float f:
                        level = Mathf.RoundToInt(f);
                        break;
                    case int i:
                        level = i;
                        break;
                }
            }
            attributes.SetLevel(level);
            return StateTreeStatus.Success;
        }
    }
}
