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

            m_Service = rootGo.AddComponent<InventoryService>();
            m_Service.definition = def;
            m_Service.Connect();

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

            // The runtime contract, honored: injected fields are valid from the first
            // tick — the heartbeat fills them — so the fixture ticks once, exactly as
            // play mode's first Update would.
            m_Service.TickFlows(0f);
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
            m_Service.Add(m_Player.Context, "ration", 2);
            Assert.IsFalse(m_Service.Remove(m_Player.Context, "ration", 3),
                "asking for more than held refuses");
            Assert.AreEqual(2, m_Service.Count(m_Player.Context, "ration"),
                "and leaves the bag untouched");
            Assert.IsTrue(m_Service.Remove(m_Player.Context, "ration", 2));
            Assert.AreEqual(0, m_Service.Count(m_Player.Context, "ration"));
        }

        [Test]
        public void Stacks_ListInRegistryOrder()
        {
            m_Service.Add(m_Player.Context, "relic");
            m_Service.Add(m_Player.Context, "ration", 3);
            IReadOnlyList<ItemStack> stacks = m_Service.Stacks(m_Player.Context);
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
            m_Service.Add(m_Player.Context, "ration", 2);
            int changes = 0;
            m_Service.changed += () => changes++;

            Assert.IsTrue(m_Service.Use("ration"));
            Assert.AreEqual(1, m_Service.Count(m_Player.Context, "ration"), "one spent");
            Assert.AreEqual(4f, m_Vitals.Value(AttributeNames.Health), 0.001f,
                "the picked heal landed on the player's attribute");
            Assert.AreEqual(1, changes, "the spend announced itself");
        }

        [Test]
        public void Use_RefusesAnEmptyBag_AndANonUsableRow()
        {
            Assert.IsFalse(m_Service.Use("ration"), "not carried");
            Assert.AreEqual(5f, m_Vitals.Value(AttributeNames.Health), 0.001f,
                "nothing applied");

            m_Service.Add(m_Player.Context, "keycard");
            Assert.IsFalse(m_Service.Use("keycard"), "a row with no use effect is not usable");
            Assert.AreEqual(1, m_Service.Count(m_Player.Context, "keycard"),
                "and nothing was spent");
        }

        // ------------------------------------------------------------------ wear

        [Test]
        public void Equip_GrantsWornModifiers_WhileWorn()
        {
            m_Service.Add(m_Player.Context, "relic");
            int equipChanges = 0;
            m_Service.equipmentChanged += () => equipChanges++;

            Assert.IsTrue(m_Service.Equip("relic"));
            Assert.IsTrue(m_Service.IsEquipped("relic"));
            Assert.AreEqual("relic", m_Service.EquippedIn("slot.trinket"));
            Assert.AreEqual(6f, m_Vitals.Effective(AttributeNames.Health), 0.001f,
                "+1 to the cap while the relic sits in its slot");
            Assert.AreEqual(1, equipChanges);

            m_Service.Unequip("slot.trinket");
            Assert.IsFalse(m_Service.IsEquipped("relic"));
            Assert.AreEqual("", m_Service.EquippedIn("slot.trinket"));
            Assert.AreEqual(5f, m_Vitals.Effective(AttributeNames.Health), 0.001f,
                "taking it off reverts the grant");
            Assert.AreEqual(2, equipChanges);
        }

        [Test]
        public void Equip_IntoAnOccupiedSlot_Swaps()
        {
            m_Service.Add(m_Player.Context, "relic");
            m_Service.Add(m_Player.Context, "charm");
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
            m_Service.Add(m_Player.Context, "keycard");
            Assert.IsFalse(m_Service.Equip("keycard"), "no slot declared");
            Assert.AreEqual(5f, m_Vitals.Effective(AttributeNames.Health), 0.001f);
        }

        // ------------------------------------------------------------------ reload

        [Test]
        public void SaveRoundtrip_ReWearsTheLoadout()
        {
            m_Service.Add(m_Player.Context, "relic");
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
        public void ItemTasks_ServeTheBagsRequestKeys()
        {
            // The UI wiring brief's edge: a press writes a key, the flow's tasks read it.
            m_Vitals.Consume(AttributeNames.Health, 3f);
            m_Service.Add(m_Player.Context, "ration", 1);
            m_Service.Add(m_Player.Context, "relic", 1);

            var use = ScriptableObject.CreateInstance<UseItemTask>();
            m_Assets.Add(use);
            use.itemKey = new StateTreeKeyField(InventoryWidgetView.UseKey);
            m_Root.Context.blackboard[InventoryWidgetView.UseKey] = "ration";
            Assert.AreEqual(StateTreeStatus.Success, use.OnTick(m_Root.Context, 0f));
            Assert.AreEqual(4f, m_Vitals.Value(AttributeNames.Health), 0.001f,
                "the key named the ration; the heal landed");

            var wear = ScriptableObject.CreateInstance<EquipItemTask>();
            m_Assets.Add(wear);
            wear.itemKey = new StateTreeKeyField(InventoryWidgetView.WearKey);
            m_Root.Context.blackboard[InventoryWidgetView.WearKey] = "relic";
            Assert.AreEqual(StateTreeStatus.Success, wear.OnTick(m_Root.Context, 0f));
            Assert.IsTrue(m_Service.IsEquipped("relic"));

            var takeoff = ScriptableObject.CreateInstance<UnequipItemTask>();
            m_Assets.Add(takeoff);
            takeoff.slotKey = new StateTreeKeyField(InventoryWidgetView.TakeoffKey);
            m_Root.Context.blackboard[InventoryWidgetView.TakeoffKey] = "slot.trinket";
            Assert.AreEqual(StateTreeStatus.Success, takeoff.OnTick(m_Root.Context, 0f));
            Assert.IsFalse(m_Service.IsEquipped("relic"),
                "the key named the slot; the relic came off");
        }

        [Test]
        public void ItemTasks_FallBackToTheAuthoredRow_WhenNoKeyResolves()
        {
            m_Service.Add(m_Player.Context, "relic", 1);
            var wear = ScriptableObject.CreateInstance<EquipItemTask>();
            m_Assets.Add(wear);
            wear.item.entryName = "relic";
            wear.itemKey = new StateTreeKeyField(InventoryWidgetView.WearKey);   // key absent
            Assert.AreEqual(StateTreeStatus.Success, wear.OnTick(m_Root.Context, 0f),
                "no request on the board — the authored row is the target");
            Assert.IsTrue(m_Service.IsEquipped("relic"));
        }

        [Test]
        public void Request_TypedValues_AreValidatedAgainstTheRegistry()
        {
            // §4d: a request row that names a registry refuses values that name no row —
            // the typo is refused at the door, not discovered as a button doing nothing.
            m_Service.definition.requests.Add(new ServiceRequest
            {
                key = "test.use", stateId = "any", namesRowOf = m_Items
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
        public void UseItemTask_PublishesTheResultContract()
        {
            // §4d's highlighted want: the verb hands its WHOLE story forward as one typed
            // payload — item def included — so a growing contract grows the class, never
            // the key count or the downstream wiring.
            m_Service.Add(m_Player.Context, "ration", 1);
            var use = ScriptableObject.CreateInstance<UseItemTask>();
            m_Assets.Add(use);
            use.item.entryName = "ration";

            use.OnEnter(m_Root.Context);
            Assert.AreEqual(StateTreeStatus.Success, use.OnTick(m_Root.Context, 0f));

            var outputs = new List<TaskOutputValue>();
            Assert.IsTrue(((IStateTreeOutputSource)use).TryCollectOutputs(outputs));
            Assert.AreEqual("result", outputs[0].name);
            var result = outputs[0].objectValue as ItemUseResult;
            Assert.IsNotNull(result, "the payload rides the output channel whole");
            Assert.AreEqual("ration", result.itemName);
            Assert.IsTrue(result.used);
            Assert.AreSame(m_Service.Row("ration"), result.item,
                "the contract carries the DEFINITION, not a name to re-resolve");
            Assert.AreEqual("ration", outputs[0].stringValue,
                "with the name in the string slot as the degraded scalar view");
        }

        [Test]
        public void DefServedRequest_RunsTheDomainHook_AndConsumes()
        {
            // §4g: no state tree anywhere — the def row IS the handler. Pending key →
            // domain hook (use + announce) → consume, in one tick.
            m_Vitals.Consume(AttributeNames.Health, 3f);
            m_Service.Add(m_Player.Context, "ration", 2);
            m_Service.definition.requests.Add(new ServiceRequest
            {
                key = "test.use", action = "use", namesRowOf = m_Items,
                description = "serve directly"
            });

            m_Root.Context.blackboard["test.use"] = "ration";
            m_Service.TickFlows(0.02f);

            Assert.AreEqual(1, m_Service.Count(m_Player.Context, "ration"),
                "the domain verb ran");
            Assert.AreEqual(4f, m_Vitals.Value(AttributeNames.Health), 0.001f);
            Assert.IsFalse(m_Root.Context.blackboard.ContainsKey("test.use"),
                "and the request was consumed");

            var payload = m_Root.Context.blackboard[ItemUseResult.Key] as ItemUseResult;
            Assert.IsNotNull(payload, "the announcement landed as its contract class");
            Assert.IsTrue(payload.used);
            Assert.AreSame(m_Service.Row("ration"), payload.item);
        }

        [Test]
        public void UseItemTask_DeclaresItsContract_ForTheTypedOffer()
        {
            // §4e: a runtime-built output is DECLARED by attribute, so the route editor can
            // offer "result : ItemUseResult" instead of a text field into a dictionary.
            var contracts = (TaskOutputContractAttribute[])typeof(UseItemTask)
                .GetCustomAttributes(typeof(TaskOutputContractAttribute), true);
            Assert.AreEqual(1, contracts.Length);
            Assert.AreEqual("result", contracts[0].name);
            Assert.AreEqual(typeof(ItemUseResult), contracts[0].payloadType);
        }

        [Test]
        public void ForgetWornOnPlayerChange_DropsRecordsWithoutReverting()
        {
            // The level-swap moment: the OLD body's modifiers die with its components, so
            // the worn list is forgotten, never reverted into whoever comes next.
            m_Service.Add(m_Player.Context, "relic");
            m_Service.Equip("relic");
            m_Service.ForgetWornOnPlayerChange();
            Assert.IsFalse(m_Service.IsEquipped("relic"));
            Assert.AreEqual("", m_Service.EquippedIn("slot.trinket"));
        }
    }
}
