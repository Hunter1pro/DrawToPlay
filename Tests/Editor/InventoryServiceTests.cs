using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace PowerOfFire.DrawToPlay.Tests
{
    /// <summary>
    /// M25 (brief §10.4, "the boring third"): the inventory as a ServiceDef-declared
    /// subsystem over the spine's item counts, where the item ROWS say what an item does —
    /// USE spends one and applies the picked effect to the player, WEAR occupies a slot row
    /// and holds Modifier effects as revertible attribute grants until unequipped or
    /// swapped. One item per slot; a save captures the loadout and re-wears it through the
    /// same Equip path.
    ///
    /// EditMode ground rules as everywhere: objects stay inactive or message-free, hosts
    /// register explicitly, the service connects explicitly.
    /// </summary>
    [TestFixture]
    public sealed class InventoryServiceTests
    {
        private readonly List<GameObject> m_Objects = new List<GameObject>();
        private readonly List<ScriptableObject> m_Assets = new List<ScriptableObject>();
        private readonly List<StateTreeContextHost> m_Hosts = new List<StateTreeContextHost>();

        private StateTreeContextHost m_Root;
        private StateTreeContextHost m_Player;
        private AttributeComponent m_Vitals;
        private Knocks m_Knocks;

        /// <summary>The save, as far as the bag can tell: something to knock on.</summary>
        private sealed class Knocks : IAutosave
        {
            public int count;
            public void MarkDirty() => count++;
        }
        private InventoryService m_Service;
        private ItemRegistry m_Items;
        private EffectRegistry m_Effects;
        private EquipmentSlotRegistry m_Slots;

        [SetUp]
        public void SetUp()
        {
            m_Slots = ScriptableObject.CreateInstance<EquipmentSlotRegistry>();
            m_Slots.entries.Add(new EquipmentSlotDef
            {
                id = "slot.trinket", name = "trinket", displayName = "Trinket"
            });
            m_Assets.Add(m_Slots);

            m_Effects = ScriptableObject.CreateInstance<EffectRegistry>();
            var heal = new EffectDef
            {
                id = "effect.heal", name = "heal",
                operation = EffectOperation.Delta, magnitude = 2f,
                duration = AbilityEffectDuration.Instant
            };
            heal.attribute.entryName = AttributeNames.Health;
            m_Effects.entries.Add(heal);
            var fortune = new EffectDef
            {
                id = "effect.fortune", name = "fortune",
                operation = EffectOperation.Modifier, magnitude = 1f
            };
            fortune.attribute.entryName = AttributeNames.Health;
            m_Effects.entries.Add(fortune);
            var charmGlow = new EffectDef
            {
                id = "effect.charm-glow", name = "charm-glow",
                operation = EffectOperation.Modifier, magnitude = 3f
            };
            charmGlow.attribute.entryName = AttributeNames.Health;
            m_Effects.entries.Add(charmGlow);
            m_Assets.Add(m_Effects);

            m_Items = ScriptableObject.CreateInstance<ItemRegistry>();
            m_Items.dependsOn.Add(m_Effects);
            m_Items.dependsOn.Add(m_Slots);
            var ration = new ItemDef { id = "item.ration", name = "ration" };
            ration.useEffect.entryName = "heal";
            m_Items.entries.Add(ration);
            m_Items.entries.Add(new ItemDef { id = "item.keycard", name = "keycard" });
            var relic = new ItemDef { id = "item.relic", name = "relic" };
            relic.slot.entryId = "slot.trinket";
            relic.slot.entryName = "trinket";
            var relicWorn = new StateTreeEntryRef<EffectDef>();
            relicWorn.entryName = "fortune";
            relic.wornEffects.Add(relicWorn);
            m_Items.entries.Add(relic);
            var charm = new ItemDef { id = "item.charm", name = "charm" };
            charm.slot.entryId = "slot.trinket";
            charm.slot.entryName = "trinket";
            var charmWorn = new StateTreeEntryRef<EffectDef>();
            charmWorn.entryName = "charm-glow";
            charm.wornEffects.Add(charmWorn);
            m_Items.entries.Add(charm);
            m_Assets.Add(m_Items);

            var def = ScriptableObject.CreateInstance<ServiceDef>();
            def.serviceName = "inventory";
            def.scope = StateTreeContextKind.Root;
            def.registry = m_Items;
            m_Assets.Add(def);

            var rootGo = new GameObject("Root");
            rootGo.hideFlags = HideFlags.HideAndDontSave;
            m_Objects.Add(rootGo);
            m_Root = rootGo.AddComponent<StateTreeContextHost>();
            m_Root.kind = StateTreeContextKind.Root;
            m_Root.autoStart = false;
            m_Root.Register();
            m_Hosts.Add(m_Root);

            // BUILT, NOT ADDED (M33): a subsystem is a class its scope owns, so the fixture
            // does exactly what the installer does in play — one constructor, one Provide.
            m_Service = new InventoryService(m_Root, def);
            m_Root.Provide(m_Service);

            // ACTIVE, unlike the ability tests' actors: the service resolves the player
            // through the registered-host walk, which skips disabled hosts by design.
            var playerGo = new GameObject("Player");
            playerGo.hideFlags = HideFlags.HideAndDontSave;
            m_Objects.Add(playerGo);
            m_Vitals = playerGo.AddComponent<AttributeComponent>();
            m_Vitals.Ensure(AttributeNames.Health, 5f);
            playerGo.AddComponent<AbilityHost>();
            m_Player = playerGo.AddComponent<StateTreeContextHost>();
            m_Player.kind = StateTreeContextKind.Player;
            m_Player.autoStart = false;
            m_Player.Register();
            m_Hosts.Add(m_Player);

            // THE FILE, as the bag sees it: a capability it knocks on after every write —
            // provided before the first tick, which is when the bag takes its collaborators.
            m_Knocks = new Knocks();
            m_Root.Provide(typeof(IAutosave), m_Knocks);

            // The runtime contract, honored: injected fields are valid from the first
            // tick — the heartbeat fills them — so the fixture ticks once, exactly as
            // play mode's first Update would. The player host REGISTERED above, under a root
            // that provides the bag, so the scope has already bound it (IBindsBody, M40.3c).
            m_Service.Tick(0f);
        }

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < m_Hosts.Count; i++)
            {
                if (!ReferenceEquals(m_Hosts[i], null))
                    m_Hosts[i].Unregister();
            }
            for (int i = 0; i < m_Objects.Count; i++)
            {
                if (m_Objects[i] != null)
                    Object.DestroyImmediate(m_Objects[i]);
            }
            for (int i = 0; i < m_Assets.Count; i++)
            {
                if (m_Assets[i] != null)
                    Object.DestroyImmediate(m_Assets[i]);
            }
            m_Objects.Clear();
            m_Assets.Clear();
            m_Hosts.Clear();
        }

        // ------------------------------------------------------------------ carrying

        [Test]
        public void Remove_IsAllOrNothing()
        {
            m_Service.Add("ration", 2);
            Assert.IsFalse(m_Service.Remove("ration", 3),
                "asking for more than held refuses");
            Assert.AreEqual(2, m_Service.Count("ration"),
                "and leaves the bag untouched");
            Assert.IsTrue(m_Service.Remove("ration", 2));
            Assert.AreEqual(0, m_Service.Count("ration"));
        }

        [Test]
        public void Stacks_ListInRegistryOrder()
        {
            m_Service.Add("relic");
            m_Service.Add("ration", 3);
            IReadOnlyList<ItemStack> stacks = m_Service.Stacks();
            Assert.AreEqual(2, stacks.Count);
            Assert.AreEqual("ration", stacks[0].definition.name, "registry order, not add order");
            Assert.AreEqual(3, stacks[0].count);
            Assert.AreEqual("relic", stacks[1].definition.name);
        }

        // ------------------------------------------------------------------ use

        [Test]
        public void Use_SpendsOne_AndAppliesTheRowsEffect()
        {
            m_Vitals.Consume(AttributeNames.Health, 3f);   // wounded: 2 of 5
            m_Service.Add("ration", 2);
            int knocksBefore = m_Knocks.count;

            Assert.IsTrue(m_Service.Use("ration").used);
            Assert.AreEqual(1, m_Service.Count("ration"), "one spent");
            Assert.AreEqual(4f, m_Vitals.Value(AttributeNames.Health), 0.001f,
                "the picked heal landed on the player's attribute");
            Assert.AreEqual(knocksBefore + 1, m_Knocks.count,
                "the spend knocked on the save — a call from the write, not an event");
        }

        [Test]
        public void Use_RefusesAnEmptyBag_AndANonUsableRow()
        {
            Assert.IsFalse(m_Service.Use("ration").used, "not carried");
            Assert.AreEqual(5f, m_Vitals.Value(AttributeNames.Health), 0.001f,
                "nothing applied");

            m_Service.Add("keycard");
            Assert.IsFalse(m_Service.Use("keycard").used, "a row with no use effect is not usable");
            Assert.AreEqual(1, m_Service.Count("keycard"),
                "and nothing was spent");
        }

        // ------------------------------------------------------------------ wear

        [Test]
        public void Equip_GrantsWornModifiers_WhileWorn()
        {
            m_Service.Add("relic");
            int knocksBefore = m_Knocks.count;

            Assert.IsTrue(m_Service.Equip("relic"));
            Assert.IsTrue(m_Service.IsEquipped("relic"));
            Assert.AreEqual("relic", m_Service.EquippedIn("slot.trinket"));
            Assert.AreEqual(6f, m_Vitals.Effective(AttributeNames.Health), 0.001f,
                "+1 to the cap while the relic sits in its slot");
            Assert.AreEqual(knocksBefore + 1, m_Knocks.count, "wearing is worth saving");

            m_Service.Unequip("slot.trinket");
            Assert.IsFalse(m_Service.IsEquipped("relic"));
            Assert.AreEqual("", m_Service.EquippedIn("slot.trinket"));
            Assert.AreEqual(5f, m_Vitals.Effective(AttributeNames.Health), 0.001f,
                "taking it off reverts the grant");
            Assert.AreEqual(knocksBefore + 2, m_Knocks.count);
        }

        [Test]
        public void Equip_IntoAnOccupiedSlot_Swaps()
        {
            m_Service.Add("relic");
            m_Service.Add("charm");
            m_Service.Equip("relic");

            Assert.IsTrue(m_Service.Equip("charm"));
            Assert.IsFalse(m_Service.IsEquipped("relic"), "the old occupant came off");
            Assert.AreEqual("charm", m_Service.EquippedIn("slot.trinket"));
            Assert.AreEqual(8f, m_Vitals.Effective(AttributeNames.Health), 0.001f,
                "only the charm's +3 stands — the relic's +1 reverted in the same act");
        }

        [Test]
        public void Equip_RefusesWhatIsNotCarried_AndWhatHasNoSlot()
        {
            Assert.IsFalse(m_Service.Equip("relic"), "not carried");
            m_Service.Add("keycard");
            Assert.IsFalse(m_Service.Equip("keycard"), "no slot declared");
            Assert.AreEqual(5f, m_Vitals.Effective(AttributeNames.Health), 0.001f);
        }

        // ------------------------------------------------------------------ reload

        [Test]
        public void SaveRoundtrip_ReWearsTheLoadout()
        {
            m_Service.Add("relic");
            m_Service.Equip("relic");
            InventoryService.SaveState state = m_Service.CaptureState();

            m_Service.Unequip("slot.trinket");
            Assert.AreEqual(5f, m_Vitals.Effective(AttributeNames.Health), 0.001f);

            m_Service.RestoreState(state);
            Assert.IsTrue(m_Service.IsEquipped("relic"), "the saved loadout is worn again");
            Assert.AreEqual(6f, m_Vitals.Effective(AttributeNames.Health), 0.001f,
                "and its grant re-applied through the same Equip path");
        }

        // ------------------------------------------------------------------ request keys

        [Test]
        public void Request_TypedValues_AreValidatedAgainstTheRegistry()
        {
            // §4d: a request row that names a registry refuses values that name no row —
            // the typo is refused at the door, not discovered as a button doing nothing.
            m_Service.definition.requests.Add(new ServiceRequest
            {
                key = "test.use", namesRowOf = m_Items
            });

            m_Service.Request("test.use", "ration");
            Assert.AreEqual("ration", m_Root.Context.blackboard["test.use"],
                "a value naming a real row writes");

            UnityEngine.TestTools.LogAssert.Expect(LogType.Error,
                new System.Text.RegularExpressions.Regex("names none of them"));
            m_Service.Request("test.use", "raton");
            Assert.AreEqual("ration", m_Root.Context.blackboard["test.use"],
                "the typo was refused — the board still holds the last good value");
        }

        [Test]
        public void DefServedRequest_RunsTheDomainHook_AndConsumes()
        {
            // §4g: no state tree anywhere — the def row IS the handler. Pending key →
            // domain hook (add) → consume, in one tick. "show me" is served the same way and
            // lands on the screen the bag holds (none in this fixture — a quiet no-op).
            m_Service.definition.requests.Add(new ServiceRequest
            {
                key = "test.add", action = InventoryService.AddAction, namesRowOf = m_Items,
                description = "serve directly"
            });
            m_Service.definition.requests.Add(new ServiceRequest
            {
                key = "test.open", action = InventoryService.OpenAction
            });
            m_Root.Context.blackboard["test.add"] = "ration";
            m_Root.Context.blackboard["test.open"] = "1";
            m_Service.Tick(0.02f);

            Assert.AreEqual(1, m_Service.Count("ration"), "the domain verb ran");
            Assert.IsFalse(m_Root.Context.blackboard.ContainsKey("test.add"),
                "and the request was consumed");
            Assert.IsFalse(m_Root.Context.blackboard.ContainsKey("test.open"),
                "as was 'show me'");
        }

        [Test]
        public void Use_AnswersWithTheContract_AndAnnouncesIt()
        {
            // The screen calls Use and shows the answer it gets back; a graph reads the same
            // answer off the board under the declared key. One call, both audiences.
            m_Vitals.Consume(AttributeNames.Health, 3f);
            m_Service.Add("ration", 2);

            ItemUseResult result = m_Service.Use("ration");
            Assert.IsTrue(result.used);
            Assert.AreSame(m_Service.Row("ration"), result.item,
                "the contract carries the DEFINITION, not a name to re-resolve");
            Assert.AreEqual(1, m_Service.Count("ration"));
            Assert.AreEqual(4f, m_Vitals.Value(AttributeNames.Health), 0.001f);

            var payload = m_Root.Context.blackboard[ItemUseResult.Key] as ItemUseResult;
            Assert.AreSame(result, payload, "the announcement IS the answer");
        }

        [Test]
        public void ANewBody_WearsTheLoadout_AndTheOldOneIsNotReverted()
        {
            // THE LEVEL SWAP (M39): the bag keeps what it holds and what it wears; the body
            // changes. The old body's modifiers die with its components (nothing to revert),
            // and the new body is granted the same loadout the moment the bag notices it.
            m_Service.Add("relic");
            m_Service.Equip("relic");
            Assert.AreEqual(6f, m_Vitals.Effective(AttributeNames.Health), 0.001f);

            GameObject oldBody = m_Player.gameObject;
            m_Hosts.Remove(m_Player);
            m_Objects.Remove(oldBody);
            m_Player.Unregister();
            Object.DestroyImmediate(oldBody);

            var nextGo = new GameObject("Player2") { hideFlags = HideFlags.HideAndDontSave };
            m_Objects.Add(nextGo);
            var nextVitals = nextGo.AddComponent<AttributeComponent>();
            nextVitals.Ensure(AttributeNames.Health, 5f);
            nextGo.AddComponent<AbilityHost>();
            var next = nextGo.AddComponent<StateTreeContextHost>();
            next.kind = StateTreeContextKind.Player;
            next.autoStart = false;
            next.Register();
            m_Hosts.Add(next);
            m_Player = next;
            m_Vitals = nextVitals;

            Assert.IsTrue(m_Service.IsEquipped("relic"), "the bag still wears it");
            Assert.AreEqual(1, m_Service.Count("relic"), "and still carries it");
            // next.Register() above IS the birth: the scope bound itself to the bag.
            Assert.AreEqual(6f, nextVitals.Effective(AttributeNames.Health), 0.001f,
                "the new body is granted the worn modifier when its scope registers");

            m_Service.Unequip("slot.trinket");
            Assert.AreEqual(5f, nextVitals.Effective(AttributeNames.Health), 0.001f,
                "and the revert lands on the body that was granted");
        }

        [Test]
        public void ALoadout_RestoredBeforeAnyBody_IsGrantedWhenOneArrives()
        {
            // Load happens before the level spawns the player. The snapshot is adopted in
            // full — counts and worn — and the grant waits for a body: the next body's SCOPE
            // binds itself to the bag when it registers (IBindsBody), nothing watches.
            m_Service.Add("relic");
            m_Service.Equip("relic");
            InventoryService.SaveState state = m_Service.CaptureState();
            Assert.AreEqual(new[] { "relic" }, state.wornItems);

            GameObject oldBody = m_Player.gameObject;
            m_Hosts.Remove(m_Player);
            m_Objects.Remove(oldBody);
            m_Player.Unregister();
            Object.DestroyImmediate(oldBody);

            // A fresh bag takes the root's slot before any body exists, and adopts the file.
            var fresh = new InventoryService(m_Root, m_Service.definition);
            m_Root.Provide(fresh);
            fresh.RestoreState(state);
            Assert.AreEqual(1, fresh.Count("relic"));
            Assert.IsTrue(fresh.IsEquipped("relic"), "worn, with nobody to grant to yet");

            var nextGo = new GameObject("Player2") { hideFlags = HideFlags.HideAndDontSave };
            m_Objects.Add(nextGo);
            nextGo.transform.SetParent(m_Root.transform);
            var nextVitals = nextGo.AddComponent<AttributeComponent>();
            nextVitals.Ensure(AttributeNames.Health, 5f);
            nextGo.AddComponent<AbilityHost>();
            var next = nextGo.AddComponent<StateTreeContextHost>();
            next.kind = StateTreeContextKind.Player;
            next.autoStart = false;
            next.Register();   // THE BIRTH: the scope binds itself to the bag above it
            m_Hosts.Add(next);
            m_Player = next;
            m_Vitals = nextVitals;

            Assert.AreEqual(6f, nextVitals.Effective(AttributeNames.Health), 0.001f,
                "granted when the body's scope registers");
            fresh.Dispose();
        }

    }
}
