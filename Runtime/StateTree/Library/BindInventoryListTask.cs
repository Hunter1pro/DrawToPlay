using System.Collections.Generic;
using UnityEngine;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// Pushes the Player scope's inventory into a screen's list — the §3.7 "BindList" atom.
    /// Contents live on the Player context blackboard as <c>item:&lt;id&gt;</c> counts
    /// (<see cref="StateTreeInventoryUtil"/>), definitions come from the registry asset, and
    /// the screen just receives rows: id for the wiring, label + count for the human. Runs
    /// once and Succeeds — put it before <see cref="ShowScreenTask"/> in the state's task
    /// list, and re-entering the state naturally re-binds fresh content.
    /// </summary>
    [CreateAssetMenu(menuName = "Draw To Play/UI/Bind Inventory List", fileName = "BindInventoryList")]
    [StateTreeCategory("Tasks/UI", "Fill a screen's list from the Player-scope inventory")]
    public sealed class BindInventoryListTask : StateTreeTaskAsset
    {
        public string screenId = "";

        public ItemRegistryAsset registry;

        public string scopeId = "";

        private readonly List<UIListEntry> m_Rows = new List<UIListEntry>();

        public override StateTreeStatus OnTick(StateTreeContext context, float deltaTime)
        {
            if (context == null || registry == null)
                return StateTreeStatus.Failure;

            UIService service = StateTreeContextHost.FindService<UIService>(context.owner);
            UIScreenBehaviour screen = service != null ? service.Find(screenId) : null;
            StateTreeContextHost player = StateTreeContextHost.Resolve(context.owner,
                StateTreeContextKind.Player, scopeId);
            if (screen == null || player == null)
                return StateTreeStatus.Failure;

            m_Rows.Clear();
            for (int i = 0; i < registry.items.Count; i++)
            {
                ItemDefAsset def = registry.items[i];
                if (def == null)
                    continue;
                int count = StateTreeInventoryUtil.Count(player.Context, def.id);
                if (count <= 0)
                    continue;
                m_Rows.Add(new UIListEntry
                {
                    itemId = def.id,
                    label = def.displayName,
                    count = count
                });
            }

            screen.BindList(m_Rows);
            return StateTreeStatus.Success;
        }
    }
}
