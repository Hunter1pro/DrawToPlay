using System;
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
    [ServiceActionContract(CraftAction, "value = recipe name, or empty for the station you are at",
        typeof(CraftResult))]
    public sealed class CraftService : StateTreeService, IBindsBody
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

        [ServiceSetting(2.4f, "How close the player must be to be AT a station, in metres — "
            + "the one reach: the panel opens at it and the swing lands at it.")]
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

        /// <summary>What the nearest station offers right now, or null — the panel's whole
        /// model. Recomputed each tick; the panel is told only when the sentence would differ.</summary>
        public CraftOffer offer { get; private set; }

        /// <summary>
        /// THE STATION THE PLAYER IS AT (M39.4), or null — the ONE place a bench is found.
        /// The panel shows its offer, and <see cref="CraftAction"/> with an empty value makes
        /// its recipe, so the craft ability needs no search of its own: two searches with two
        /// ranges that happened to agree are one search with one setting.
        /// </summary>
        public WorldObjectBehaviour at { get; private set; }

        /// <summary>The panel this bench showed (its def's spawn), held from the moment it
        /// was shown and CALLED — no events (M39.2b). Null when the def spawns none.</summary>
        private CraftPanelView m_Panel;

        protected override void OnStarted()
        {
            m_Panel = Spawned<CraftPanelView>();
        }



        public CraftRecipeRegistry recipes =>
            definition != null ? definition.registry as CraftRecipeRegistry : null;

        /// <summary>The bag the costs come out of and the result goes into. Injected and
        /// self-healing like every other service field — a level swap refills it.</summary>
        [InjectService] private IBag m_Inventory;

        /// <summary>
        /// WHAT THE PLAYER IS STANDING AT, recomputed each tick. The service polls and the
        /// panel does not: a skin that asked "am I near a bench" would need the world, the
        /// player and the recipe catalog — three references a piece of screen has no business
        /// holding. Here it is one distance check on the subsystem that already owns all three,
        /// and the panel hears about it only when the answer changed.
        /// </summary>
        /// <summary>THE BODY BINDS ITSELF (M40.3): the player, at its start, tells the bench
        /// who is walking up to stations; nothing here looks for a player per tick.</summary>
        /// <summary>The player is this service's body — the scope binds it (IBindsBody).</summary>
        public StateTreeContextKind bodyKind => StateTreeContextKind.Player;

        public void Bind(StateTreeContextHost body) { m_Player = body; }

        public void Unbind(StateTreeContextHost body)
        {
            if (ReferenceEquals(body, m_Player))
                m_Player = null;
        }

        private StateTreeContextHost m_Player;

        protected override void OnTick(float deltaTime)
        {
            StateTreeContextHost player = m_Player != null ? m_Player : null;
            at = StationAt(player);
            CraftOffer next = OfferOf(at);
            string signature = next == null ? "" : Signature(next);
            if (signature == m_Shown)
                return;
            m_Shown = signature;
            offer = next;
            m_Panel?.Show(next);
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

        /// <summary>The offer as last announced, so it is only announced again when the
        /// sentence would differ. Empty means "no bench".</summary>
        private string m_Shown = "";

        /// <summary>The nearest station within <see cref="benchRange"/> of the player, or null.</summary>
        public WorldObjectBehaviour StationAt(StateTreeContextHost player)
        {
            if (player == null || player.Context == null)
                return null;
            WorldService world = StateTreeContextHost.FindService<WorldService>(player.gameObject);
            if (world == null)
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
            return bench;
        }

        /// <summary>
        /// What a station offers, as a finished read model — or null when there is no station
        /// or it offers nothing. Both numbers per cost are counted HERE, so the panel adds
        /// nothing to them. WHICH recipe is the placer pattern's answer: the citizen's entry
        /// name is the row it was placed as.
        /// </summary>
        public CraftOffer OfferOf(WorldObjectBehaviour bench)
        {
            CraftRecipeRegistry catalog = recipes;
            if (bench == null || catalog == null || m_Inventory == null)
                return null;
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
            if (request.action == CraftAction)
                Craft(value);
        }

        /// <summary>
        /// Make one of <paramref name="recipeName"/>, if the player is carrying what it takes.
        /// The verb, callable directly (a test, a shop) as well as through a request.
        /// </summary>
        /// <returns>What happened — announced under <see cref="CraftResult.Key"/> as well, so
        /// a caller that ignores the return still leaves the story on the board.</returns>
        public CraftResult Craft(string recipeName)
        {
            // AN EMPTY NAME IS "WHAT I AM STANDING AT" — the declared meaning of the row's
            // empty value, so the ability that swings at a bench names no recipe and the one
            // search above decides which.
            if (string.IsNullOrEmpty(recipeName))
            {
                if (at == null || offer == null)
                    return Refuse(new CraftResult { refusal = "no station here" });
                recipeName = offer.recipeName;
            }
            var result = new CraftResult { recipeName = recipeName };

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
            return Tell(result);
        }

        /// <summary>
        /// THE PANEL'S BUTTON IS THE WORLD PRESS. It could have crafted directly — the verb is
        /// right here — and that would have been a second way to spend three timber, with its
        /// own timing, its own animation (none) and its own bugs. It asks the PLAYER to perform
        /// the craft ability instead: the ability holds the pose for its clip and asks
        /// <see cref="CraftAction"/> when it lands, so the bench answers the same way however
        /// you asked, hammering included.
        /// </summary>
        public void StartCrafting()
        {
            StateTreeContextHost player = m_Player != null ? m_Player : null;
            var host = player != null ? player.GetComponent<AbilityHost>() : null;
            if (host != null && host.Activate("craft"))
                return;
            // A press that does nothing is the bug this whole pass is about, so say why.
            Tell(new CraftResult { refusal = "not now", line = "not now" });
        }

        /// <summary>A refusal, told like a success. Every way of not crafting leaves the same
        /// contract on the board, so whoever shows craft outcomes shows this one too.</summary>
        private CraftResult Refuse(CraftResult result)
        {
            result.line = result.refusal;
            return Tell(result);
        }

        /// <summary>One outcome, everywhere it goes: the board (a graph's `craft.last`), and
        /// the panel this bench showed — told, in the same method.</summary>
        private CraftResult Tell(CraftResult result)
        {
            Announce(CraftResult.Key, result);
            m_Panel?.Announce(result);
            return result;
        }
    }
}
