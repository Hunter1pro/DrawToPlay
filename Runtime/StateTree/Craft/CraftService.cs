using System.Collections.Generic;
using UnityEngine;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// CRAFTING AS A SUBSYSTEM (M26) — HT's shipyard, as the shape §4g describes: a ServiceDef
    /// declares the request, this class says what the request MEANS, and the answer leaves as
    /// one announced contract. Nothing else in the project learns that crafting exists.
    ///
    /// DEF-ONLY: no flow tree, no view of its own. The whole of crafting is single-frame —
    /// check the costs, spend them, grant the result, say what happened — so a state tree would
    /// be ceremony around one method. The WAITING that a craft appears to need (the hammering,
    /// the duration) belongs to the ability that asked, which is a much better place for it: it
    /// already owns an animation, a body and a way to be interrupted.
    ///
    /// ALL OR NOTHING. Costs are checked first and spent second, because a craft that ate two
    /// of the three things it needed and then refused is the one bug in a crafting system a
    /// player will never forgive.
    /// </summary>
    [AddComponentMenu("Draw To Play/Services/Craft Service")]
    [ServiceActionContract(CraftAction, "value = recipe name")]
    public sealed class CraftService : StateTreeServiceBehaviour
    {
        /// <summary>The one verb, as a symbol — the attribute above, the switch below and the
        /// demo's def all reference this, so declaration and dispatch cannot drift.</summary>
        public const string CraftAction = "craft";

        [Tooltip("The declaration this service runs: scope and the recipe registry (whose "
            + "dependsOn names the item registry its costs and results pick from).")]
        public ServiceDef definition;

        protected override ServiceDef FlowSource => definition;

        public CraftRecipeRegistry recipes =>
            definition != null ? definition.registry as CraftRecipeRegistry : null;

        /// <summary>The bag the costs come out of and the result goes into. Injected and
        /// self-healing like every other service field — a level swap refills it.</summary>
        [InjectService] private InventoryService m_Inventory;

        /// <summary>THE DOMAIN HOOK (§4g): what the request's action means.</summary>
        protected override void OnRequest(ServiceRequest request, string value)
        {
            switch (request.action)
            {
                case CraftAction:
                    Craft(value);
                    break;
            }
        }

        /// <summary>
        /// Make one of <paramref name="recipeName"/>, if the player is carrying what it takes.
        /// The verb, callable directly (a test, a shop) as well as through a request.
        /// </summary>
        /// <returns>What happened — announced under <see cref="CraftResult.Key"/> as well, so
        /// a caller that ignores the return still leaves the story on the board.</returns>
        public CraftResult Craft(string recipeName)
        {
            var result = new CraftResult { recipeName = recipeName ?? "" };

            CraftRecipeRegistry catalog = recipes;
            result.recipe = catalog != null && !string.IsNullOrEmpty(recipeName)
                ? catalog.FindByName(recipeName) as CraftRecipeDef
                : null;
            if (result.recipe == null)
            {
                result.refusal = "no recipe named '" + result.recipeName + "'";
                Announce(CraftResult.Key, result);
                return result;
            }

            StateTreeContextHost carrier = StateTreeContextHost.Resolve(gameObject,
                StateTreeContextKind.Player);
            if (m_Inventory == null || carrier == null || carrier.Context == null)
            {
                result.refusal = "nobody is carrying anything here";
                Announce(CraftResult.Key, result);
                return result;
            }

            // CHECKED IN FULL FIRST. The refusal names the FIRST thing missing rather than
            // "you cannot make that": a station that says what it wants is a station a player
            // can act on.
            List<CraftRecipeDef.Cost> costs = result.recipe.costs;
            for (int i = 0; i < costs.Count; i++)
            {
                CraftRecipeDef.Cost cost = costs[i];
                if (cost == null || string.IsNullOrEmpty(cost.item.entryName))
                    continue;
                int need = Mathf.Max(1, cost.count);
                int held = m_Inventory.Count(carrier.Context, cost.item.entryName);
                if (held >= need)
                    continue;
                result.refusal = "needs " + need + " " + cost.item.entryName
                    + " (carrying " + held + ")";
                Announce(CraftResult.Key, result);
                return result;
            }

            for (int i = 0; i < costs.Count; i++)
            {
                CraftRecipeDef.Cost cost = costs[i];
                if (cost != null && !string.IsNullOrEmpty(cost.item.entryName))
                    m_Inventory.Remove(carrier.Context, cost.item.entryName,
                        Mathf.Max(1, cost.count));
            }

            result.itemName = result.recipe.result.entryName ?? "";
            result.item = m_Inventory.Row(result.itemName);
            result.count = Mathf.Max(1, result.recipe.resultCount);
            m_Inventory.Add(carrier.Context, result.itemName, result.count);
            result.made = true;

            Announce(CraftResult.Key, result);
            return result;
        }
    }
}
