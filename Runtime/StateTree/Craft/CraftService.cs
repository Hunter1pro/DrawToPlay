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
    [ServiceActionContract(CraftAction, "value = recipe name", typeof(CraftResult))]
    [ServiceActionContract(StartAction, "value = recipe name — the player performs it")]
    public sealed class CraftService : StateTreeService
    {

        /// <summary>Built by its scope's installer (M33): the rulebook about what three timber
        /// are worth, with the def whose recipes it runs.</summary>
        public CraftService(StateTreeContextHost scope, ServiceDef definition)
            : base(scope, definition)
        {
            // The base constructor has applied the def's settings by now (M36), so this is the
            // final answer — and an empty one means no bench can ever be found.
            if (string.IsNullOrEmpty(stationTag))
            {
                Debug.LogError("[Craft] '" + (definition != null ? definition.name : "?")
                    + "' sets no 'stationTag' — the bench cannot be found. Pick the tag a "
                    + "station wears on the def's Settings.");
            }
        }

        [ServiceSetting(2.4f, "How close the player must be for a station's panel to open, in "
            + "metres. Slightly wider than the craft ability's own reach, so the offer is on "
            + "screen before the button would work rather than after.")]
        public float benchRange;

        /// <summary>NO DEFAULT, on purpose (M36): a tag written in code is the pattern M31
        /// destroyed everywhere else — invisible to the map, unrenameable, unpicked. The def
        /// picks it from the vocabulary it declares, and a def that forgot is told so below.</summary>
        [ServiceSetting("", "What a station is — the tag a bench wears.")]
        [WorldTag("World")]
        public string stationTag;

        /// <summary>The one verb, as a symbol — the attribute above, the switch below and the
        /// demo's def all reference this, so declaration and dispatch cannot drift.</summary>
        public const string CraftAction = "craft";

        /// <summary>The panel button's action: START the player crafting rather than crafting
        /// for them. Both roads end at <see cref="CraftAction"/> — one after a swing.</summary>
        public const string StartAction = "craft-start";



        public CraftRecipeRegistry recipes =>
            definition != null ? definition.registry as CraftRecipeRegistry : null;

        /// <summary>The bag the costs come out of and the result goes into. Injected and
        /// self-healing like every other service field — a level swap refills it.</summary>
        [InjectService] private IBag m_Inventory;

        /// <summary>
        /// WHAT THE PLAYER IS STANDING AT, pushed to the panel each tick.
        ///
        /// The service polls and the view does not, which is the doctrine's whole shape: a
        /// skin that asked "am I near a bench" would need the world, the player and the
        /// recipe catalog — three references a piece of screen has no business holding. Here
        /// it is one distance check on the subsystem that already owns all three.
        /// </summary>
        protected override void OnTick(float deltaTime)
        {
            PushOffer();
        }

        private void PushOffer()
        {
            UiService ui = Ui;
            if (ui == null)
                return;
            GameObject view = ui.ShownView("craft");
            CraftPanelView panel = view != null
                ? view.GetComponentInChildren<CraftPanelView>(true)
                : null;
            if (panel == null)
                return;

            CraftOffer offer = OfferAt(StateTreeContextHost.Resolve(scope.gameObject,
                StateTreeContextKind.Player));
            if (offer == null)
            {
                if (m_Shown.Length > 0)
                    panel.Close();
                m_Shown = "";
                return;
            }

            // ONLY WHEN IT CHANGED. Pushing every frame would rebuild a handful of labels
            // sixty times a second to say the same sentence — the panel is a statement, not an
            // animation, and its own contents are the cheapest possible description of it.
            string signature = Signature(offer);
            if (signature == m_Shown)
                return;
            m_Shown = signature;
            panel.Show(offer);
        }

        private static string Signature(CraftOffer offer)
        {
            string signature = offer.recipeName + "|" + offer.affordable;
            for (int i = 0; i < offer.costs.Count; i++)
            {
                CraftCostLine cost = offer.costs[i];
                if (cost != null)
                    signature += "|" + cost.itemName + cost.held + "/" + cost.need;
            }
            return signature;
        }

        /// <summary>What the panel is currently saying, so it is only told again when the
        /// sentence would differ. Empty means nothing is on screen.</summary>
        private string m_Shown = "";

        /// <summary>
        /// The nearest station within reach, as a finished read model — or null when there is
        /// none. Both numbers per cost are counted HERE, so the skin adds nothing to them.
        /// </summary>
        public CraftOffer OfferAt(StateTreeContextHost player)
        {
            if (player == null || player.Context == null)
                return null;
            WorldService world = StateTreeContextHost.FindService<WorldService>(player.gameObject);
            CraftRecipeRegistry catalog = recipes;
            if (world == null || catalog == null || m_Inventory == null)
                return null;

            s_Benches.Clear();
            world.CollectByTag(stationTag, s_Benches);
            WorldObjectBehaviour bench = null;
            float best = benchRange;
            for (int i = 0; i < s_Benches.Count; i++)
            {
                WorldObjectBehaviour candidate = s_Benches[i];
                if (candidate == null || !candidate.isActiveAndEnabled)
                    continue;
                Vector3 offset = candidate.transform.position - player.transform.position;
                offset.y = 0f;
                if (offset.magnitude >= best)
                    continue;
                bench = candidate;
                best = offset.magnitude;
            }
            if (bench == null)
                return null;

            // WHICH recipe is the placer pattern's answer: the citizen's entry name is the row
            // it was placed as. The same fact the craft ability reads off it.
            var recipe = catalog.FindByName(bench.entryName) as CraftRecipeDef;
            if (recipe == null)
                return null;

            var offer = new CraftOffer
            {
                stationName = bench.name,
                recipeName = recipe.name,
                displayName = recipe.displayName,
                affordable = true
            };
            for (int i = 0; i < recipe.costs.Count; i++)
            {
                CraftRecipeDef.Cost cost = recipe.costs[i];
                if (cost == null || string.IsNullOrEmpty(cost.item.entryName))
                    continue;
                var line = new CraftCostLine
                {
                    item = m_Inventory.Row(cost.item.entryName),
                    itemName = cost.item.entryName,
                    need = Mathf.Max(1, cost.count),
                    held = m_Inventory.Count(cost.item.entryName)
                };
                offer.costs.Add(line);
                if (line.met)
                    continue;
                offer.affordable = false;
                if (offer.blocker.Length == 0)
                    offer.blocker = "needs " + line.need + " " + line.itemName;
            }
            return offer;
        }

        private static readonly List<WorldObjectBehaviour> s_Benches =
            new List<WorldObjectBehaviour>();

        /// <summary>THE DOMAIN HOOK (§4g): what the request's action means.</summary>
        protected override void OnRequest(ServiceRequest request, string value)
        {
            switch (request.action)
            {
                case CraftAction:
                    Craft(value);
                    break;

                // THE PANEL'S BUTTON IS THE WORLD PRESS. It could have crafted directly — the
                // verb is right here — and that would have been a second way to spend three
                // timber, with its own timing, its own animation (none) and its own bugs. It
                // asks the PLAYER to do it instead, so the bench answers the same way however
                // you asked, hammering included.
                case StartAction:
                    StartCrafting();
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
                return Refuse(result);
            }

            if (m_Inventory == null)
            {
                result.refusal = "no bag to craft from";
                return Refuse(result);
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
                int held = m_Inventory.Count(cost.item.entryName);
                if (held >= need)
                    continue;
                result.refusal = "needs " + need + " " + cost.item.entryName
                    + " (carrying " + held + ")";
                return Refuse(result);
            }

            for (int i = 0; i < costs.Count; i++)
            {
                CraftRecipeDef.Cost cost = costs[i];
                if (cost != null && !string.IsNullOrEmpty(cost.item.entryName))
                    m_Inventory.Remove(cost.item.entryName,
                        Mathf.Max(1, cost.count));
            }

            result.itemName = result.recipe.result.entryName ?? "";
            result.item = m_Inventory.Row(result.itemName);
            result.count = Mathf.Max(1, result.recipe.resultCount);
            m_Inventory.Add(result.itemName, result.count);
            result.made = true;
            // The sentence, written where the outcome is known — see CraftResult.line.
            string made = result.recipe != null && !string.IsNullOrEmpty(result.recipe.displayName)
                ? result.recipe.displayName
                : result.itemName;
            result.line = result.count > 1 ? made + " ×" + result.count : made;

            Announce(CraftResult.Key, result);
            return result;
        }

        /// <summary>
        /// Ask the player to perform its craft ability. The ability finds the bench itself,
        /// holds the pose for its clip, and sends the public request when it lands — so this
        /// method knows nothing about recipes, and the panel button and the action button are
        /// genuinely one flow.
        /// </summary>
        private void StartCrafting()
        {
            StateTreeContextHost player = StateTreeContextHost.Resolve(scope.gameObject,
                StateTreeContextKind.Player);
            var host = player != null ? player.GetComponent<AbilityHost>() : null;
            if (host != null && host.Activate("craft"))
                return;

            // A press that does nothing is the bug this whole pass is about, so say why.
            Announce(CraftResult.Key, new CraftResult
            {
                refusal = "not now", line = "not now"
            });
        }

        /// <summary>A refusal, announced like a success. Every way of not crafting leaves the
        /// same contract on the board, so a skin that shows craft outcomes shows this one too
        /// without a second channel or a second beat.</summary>
        private CraftResult Refuse(CraftResult result)
        {
            result.line = result.refusal;
            Announce(CraftResult.Key, result);
            return result;
        }
    }
}
