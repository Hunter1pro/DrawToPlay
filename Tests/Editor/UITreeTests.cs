using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace PowerOfFire.DrawToPlay.Tests
{
    /// <summary>
    /// The notebook's Inventory flow (brief §3.5) driven end to end in EditMode — the proof
    /// that "the state tree controls the state of UI": closed → (trigger) → inventory →
    /// (click a weapon) → equip → back; (click a consumable) → tooltip → close → back;
    /// (close) → closed — with the click carried by the ShowScreenTask result key, the
    /// tooltip content by an entry-time ⚑ binding, and every screen hidden by state exit,
    /// Cancelled included. No view exists anywhere in this file: tests ARE the view, calling
    /// the same Report* methods a real one would.
    /// </summary>
    [TestFixture]
    public sealed class UITreeTests
    {
        private const string k_OpenKey = "ui:openInventory";
        private const string k_ClickedKey = "clickedItem";

        private readonly List<GameObject> m_Objects = new List<GameObject>();
        private readonly List<ScriptableObject> m_Assets = new List<ScriptableObject>();
        private readonly List<StateTreeContextHost> m_Hosts = new List<StateTreeContextHost>();

        private StateTreeContextHost m_Player;
        private UIScreenBehaviour m_InvScreen;
        private UIScreenBehaviour m_TipScreen;
        private ItemRegistry m_Registry;

        [SetUp]
        public void SetUp()
        {
            m_Objects.Clear();
            m_Assets.Clear();
            m_Hosts.Clear();
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

        // ------------------------------------------------------------ the whole flow

        [Test]
        public void InventoryFlow_EndToEnd()
        {
            StateTreeRunner runner = BuildExample();
            runner.StartTree();
            Tick(runner, 1);
            Assert.AreEqual("closed", runner.activeNodeId, "the UI starts closed");
            Assert.IsFalse(m_InvScreen.isVisible);

            // Trigger — a button, a level, any tree: one context key.
            m_Player.Context.blackboard[k_OpenKey] = 1f;
            Tick(runner, 1);
            Assert.AreEqual("inventory", runner.activeNodeId, "the open trigger interrupts in");
            Assert.IsTrue(m_InvScreen.isVisible, "the inventory screen is open");
            Tick(runner, 1);
            Assert.AreEqual(2, m_InvScreen.entries.Count,
                "both held items were bound on the first task tick");
            Assert.AreEqual("sword", m_InvScreen.entries[0].itemId);
            Assert.AreEqual(2, m_InvScreen.entries[1].count, "two potions, one row");
            Assert.IsFalse(m_Player.Context.blackboard.ContainsKey(k_OpenKey),
                "opening CONSUMED the trigger — the key is the event, the handler eats it");

            // Weapon click → equip → back, with the id delivered by the ⚑ entry binding.
            m_InvScreen.ReportItemClick("sword");
            Tick(runner, 4);
            Assert.AreEqual("inventory", runner.activeNodeId, "equip flowed back to inventory");
            Assert.AreEqual("sword", m_Player.Context.blackboard["equipped"],
                "the clicked weapon landed on the Player scope through the bound field");
            Assert.IsTrue(m_InvScreen.isVisible, "and the inventory reopened");

            // Consumable click → tooltip (content from the routed click), close → back.
            string detail = null;
            m_TipScreen.detailBound += text => detail = text;
            m_InvScreen.ReportItemClick("potion");
            Tick(runner, 3);
            Assert.AreEqual("tooltip", runner.activeNodeId, "a consumable opens the tooltip");
            Assert.IsTrue(m_TipScreen.isVisible, "tooltip screen shown");
            Assert.IsFalse(m_InvScreen.isVisible, "inventory hid when its state exited");
            Assert.AreEqual("Health Potion — Consumable", detail,
                "the tooltip bound the CLICKED item's detail — even a tooltip is a state");

            m_TipScreen.ReportClose();
            Tick(runner, 2);
            Assert.AreEqual("inventory", runner.activeNodeId, "close returns to the previous screen");
            Assert.IsTrue(m_InvScreen.isVisible);
            Assert.IsFalse(m_TipScreen.isVisible);

            // Close → everything shut, trigger cleared, ready for the next open.
            m_InvScreen.ReportClose();
            Tick(runner, 2);
            Assert.AreEqual("closed", runner.activeNodeId);
            Assert.IsFalse(m_InvScreen.isVisible, "closing the inventory hid it");

            // And it opens again — the flow is a loop, not a one-shot.
            m_Player.Context.blackboard[k_OpenKey] = 1f;
            Tick(runner, 1);
            Assert.AreEqual("inventory", runner.activeNodeId);
            Assert.IsTrue(m_InvScreen.isVisible);
        }

        [Test]
        public void StoppingTheTree_HidesTheOpenScreen_Cancelled()
        {
            StateTreeRunner runner = BuildExample();
            runner.StartTree();
            m_Player.Context.blackboard[k_OpenKey] = 1f;
            Tick(runner, 2);
            Assert.IsTrue(m_InvScreen.isVisible);

            runner.StopTree();
            Assert.IsFalse(m_InvScreen.isVisible,
                "Cancelled teardown reached the screen — an interrupt anywhere closes the UI");
        }

        // ------------------------------------------------------------ atom units

        [Test]
        public void InventoryAtoms_AddRemoveCountKind()
        {
            BuildSpine();
            GameObject unit = MakeUnit("Unit", m_Player);
            var context = new StateTreeContext(unit);

            var add = ScriptableObject.CreateInstance<InventoryAddTask>();
            SetItem(add.item, "sword");
            add.count = 2;
            m_Assets.Add(add);
            Assert.AreEqual(StateTreeStatus.Success, add.OnTick(context, 0.1f));
            // The spine seeds sword=1, so adding 2 lands on 3 — the test respects the
            // fixture instead of imagining an empty inventory.
            Assert.AreEqual(3, StateTreeInventoryUtil.Count(m_Player.Context, "sword"));

            var remove = ScriptableObject.CreateInstance<InventoryRemoveTask>();
            SetItem(remove.item, "sword");
            remove.count = 4;
            m_Assets.Add(remove);
            Assert.AreEqual(StateTreeStatus.Failure, remove.OnTick(context, 0.1f),
                "removing more than held FAILS all-or-nothing");
            Assert.AreEqual(3, StateTreeInventoryUtil.Count(m_Player.Context, "sword"));
            remove.count = 3;
            Assert.AreEqual(StateTreeStatus.Success, remove.OnTick(context, 0.1f));
            Assert.AreEqual(0, StateTreeInventoryUtil.Count(m_Player.Context, "sword"));
            Assert.IsFalse(m_Player.Context.blackboard.ContainsKey(
                StateTreeInventoryUtil.Key("sword")), "zero removes the key entirely");

            var count = ScriptableObject.CreateInstance<InventoryCountCondition>();
            SetItem(count.item, "potion");
            count.atLeast = 3;
            m_Assets.Add(count);
            Assert.IsFalse(count.Evaluate(context), "seeded 2 potions are not 3");
            StateTreeInventoryUtil.SetCount(m_Player.Context, "potion", 3);
            Assert.IsTrue(count.Evaluate(context));

            var kind = ScriptableObject.CreateInstance<ItemKindCondition>();
            ((IStateTreeRegistryRef)kind.items).Bind(m_Registry);
            kind.kind = ItemKind.Weapon;
            m_Assets.Add(kind);
            context.blackboard[k_ClickedKey] = "sword";
            Assert.IsTrue(kind.Evaluate(context));
            context.blackboard[k_ClickedKey] = "potion";
            Assert.IsFalse(kind.Evaluate(context));
            context.blackboard[k_ClickedKey] = "no-such-item";
            Assert.IsFalse(kind.Evaluate(context), "an unknown id matches no kind");
        }

        // ------------------------------------------------------------ fixtures

        /// <summary>Root → Level → Player hosts; UIService on the Player object (per-player
        /// UI); two screens; the item registry; a seeded inventory.</summary>
        private void BuildSpine()
        {
            StateTreeContextHost root = MakeHost("Root", StateTreeContextKind.Root, null);
            StateTreeContextHost level = MakeHost("Level", StateTreeContextKind.Level, root);
            m_Player = MakeHost("P1", StateTreeContextKind.Player, level);

            var ui = m_Player.gameObject.AddComponent<UIService>();
            ui.Connect();

            m_InvScreen = MakeScreen("inv", m_Player);
            m_TipScreen = MakeScreen("tooltip", m_Player);
            ui.AdoptStrays();
            m_InvScreen.RegisterToUI();
            m_TipScreen.RegisterToUI();

            m_Registry = ScriptableObject.CreateInstance<ItemRegistry>();
            m_Registry.entries.Add(MakeItem("sword", "Iron Sword", ItemKind.Weapon));
            m_Registry.entries.Add(MakeItem("potion", "Health Potion", ItemKind.Consumable));
            m_Assets.Add(m_Registry);

            StateTreeInventoryUtil.SetCount(m_Player.Context, "sword", 1);
            StateTreeInventoryUtil.SetCount(m_Player.Context, "potion", 2);

            // THE BAG AS A SUBSYSTEM (M32): the atoms ask it now, so the spine mounts one —
            // seeding stays direct because these tests are about the ENCODING, which is the
            // one thing still allowed to speak it.
            var bagObject = new GameObject("Bag");
            bagObject.hideFlags = HideFlags.HideAndDontSave;
            m_Objects.Add(bagObject);
            var def = ScriptableObject.CreateInstance<ServiceDef>();
            def.name = "inventory";
            def.serviceName = "inventory";
            def.registry = m_Registry;
            m_Assets.Add(def);
            var bag = new InventoryService(m_Player, def);
            m_Player.Provide(bag);
        }

        /// <summary>The §3.5 tree, wired exactly as the State Tree window would wire it.</summary>
        private StateTreeRunner BuildExample()
        {
            BuildSpine();

            // closed: just parked; the open trigger interrupts in.
            var park = ScriptableObject.CreateInstance<StubRecordingTask>();
            park.taskId = "closed";
            m_Assets.Add(park);
            StateTreeNodeAsset closed = MakeNode("closed", park);

            // inventory: CONSUME the trigger (clearing it in `closed` would lose a race
            // forever — interrupts run before task ticks, so the still-set trigger re-fires
            // into inventory before a clearing task there could run), bind, then hold the
            // screen open.
            var consumeTrigger = ScriptableObject.CreateInstance<SetContextValueTask>();
            consumeTrigger.scope = StateTreeContextKind.Player;
            consumeTrigger.key.text = k_OpenKey;
            consumeTrigger.kind = SetBlackboardTask.ValueKind.Clear;
            m_Assets.Add(consumeTrigger);
            var bind = ScriptableObject.CreateInstance<BindInventoryListTask>();
            bind.screenId.text = "inv";
            m_Assets.Add(bind);
            var showInv = ScriptableObject.CreateInstance<ShowScreenTask>();
            showInv.screenId.text = "inv";
            m_Assets.Add(showInv);
            StateTreeNodeAsset inventory = MakeNode("inventory", consumeTrigger, bind, showInv);

            // itemFlow: a beat that exists to BRANCH on the clicked item's kind.
            var flowMark = ScriptableObject.CreateInstance<SetBlackboardTask>();
            flowMark.key.text = "ui:flow";
            flowMark.kind = SetBlackboardTask.ValueKind.Float;
            flowMark.floatValue = 1f;
            m_Assets.Add(flowMark);
            StateTreeNodeAsset itemFlow = MakeNode("itemFlow", flowMark);

            // equip: publish the clicked weapon on the Player scope — stringValue is ⚑-bound
            // to the click, the M7k entry-time binding doing the delivery.
            var equip = ScriptableObject.CreateInstance<SetContextValueTask>();
            equip.scope = StateTreeContextKind.Player;
            equip.key.text = "equipped";
            equip.kind = SetBlackboardTask.ValueKind.String;
            m_Assets.Add(equip);
            StateTreeNodeAsset equipNode = MakeNode("equip", equip);
            equipNode.bindings.Add(new StateTreeFieldBinding
            {
                targetKind = StateTreeFieldBinding.TargetKind.Task,
                targetIndex = 0,
                fieldName = "stringValue",
                sourceKind = StateTreeFieldBinding.SourceKind.BlackboardKey,
                blackboardKey = k_ClickedKey
            });

            // tooltip: content from the click, then hold the screen open.
            var detail = ScriptableObject.CreateInstance<BindItemDetailTask>();
            detail.screenId.text = "tooltip";
            m_Assets.Add(detail);
            var showTip = ScriptableObject.CreateInstance<ShowScreenTask>();
            showTip.screenId.text = "tooltip";
            showTip.resultKey.text = "tooltipClicked";
            m_Assets.Add(showTip);
            StateTreeNodeAsset tooltip = MakeNode("tooltip", detail, showTip);
            tooltip.bindings.Add(new StateTreeFieldBinding
            {
                targetKind = StateTreeFieldBinding.TargetKind.Task,
                targetIndex = 0,
                fieldName = "itemId",
                sourceKind = StateTreeFieldBinding.SourceKind.BlackboardKey,
                blackboardKey = k_ClickedKey
            });

            // Wiring. closed --(trigger raised, interrupt)--> inventory.
            var openTrigger = ScriptableObject.CreateInstance<HasContextKeyCondition>();
            openTrigger.scope = StateTreeContextKind.Player;
            openTrigger.key.text = k_OpenKey;
            m_Assets.Add(openTrigger);
            AddTransition(closed, "inventory", openTrigger, interrupt: true);

            // inventory: click (result key present) -> itemFlow; otherwise (closed) -> closed.
            var clicked = ScriptableObject.CreateInstance<HasBlackboardKeyCondition>();
            clicked.key.text = k_ClickedKey;
            m_Assets.Add(clicked);
            AddTransition(inventory, "itemFlow", clicked, interrupt: false);
            AddTransition(inventory, "closed", null, interrupt: false);

            // itemFlow: weapon -> equip; anything else -> tooltip.
            var isWeapon = ScriptableObject.CreateInstance<ItemKindCondition>();
            isWeapon.kind = ItemKind.Weapon;
            m_Assets.Add(isWeapon);
            AddTransition(itemFlow, "equip", isWeapon, interrupt: false);
            AddTransition(itemFlow, "tooltip", null, interrupt: false);

            AddTransition(equipNode, "inventory", null, interrupt: false);
            AddTransition(tooltip, "inventory", null, interrupt: false);

            var rootNode = ScriptableObject.CreateInstance<StateTreeNodeAsset>();
            rootNode.nodeId = "uiRoot";
            rootNode.name = "Node uiRoot";
            m_Assets.Add(rootNode);
            rootNode.children.Add(closed);
            rootNode.children.Add(inventory);
            rootNode.children.Add(itemFlow);
            rootNode.children.Add(equipNode);
            rootNode.children.Add(tooltip);

            var tree = ScriptableObject.CreateInstance<StateTreeAsset>();
            tree.name = "InventoryUITree";
            tree.treeName = "InventoryUITree";
            tree.root = rootNode;
            // The M13 data connection: the tree lists its registry; the executor injects the
            // bind/detail/kind tasks' typed references from it at StartTree.
            tree.registries.Add(m_Registry);
            m_Assets.Add(tree);

            GameObject unit = MakeUnit("UIUnit", m_Player);
            var runnerGo = new GameObject("UIRunner");
            runnerGo.hideFlags = HideFlags.HideAndDontSave;
            runnerGo.transform.SetParent(unit.transform);
            m_Objects.Add(runnerGo);
            var runner = runnerGo.AddComponent<StateTreeRunner>();
            runner.autoStart = false;
            runner.data = tree;
            runner.ownerObject = unit;
            runner.context = new StateTreeContext(unit);
            return runner;
        }

        private static void Tick(StateTreeRunner runner, int times)
        {
            for (int i = 0; i < times; i++)
                runner.TickTree(0.1f);
        }

        private StateTreeContextHost MakeHost(string goName, StateTreeContextKind kind,
            StateTreeContextHost parent)
        {
            var go = new GameObject(goName);
            go.hideFlags = HideFlags.HideAndDontSave;
            if (parent != null)
                go.transform.SetParent(parent.transform);
            m_Objects.Add(go);
            var host = go.AddComponent<StateTreeContextHost>();
            host.kind = kind;
            host.autoStart = false;
            host.Register();
            m_Hosts.Add(host);
            return host;
        }

        private UIScreenBehaviour MakeScreen(string screenId, StateTreeContextHost parent)
        {
            var go = new GameObject("Screen " + screenId);
            go.hideFlags = HideFlags.HideAndDontSave;
            go.transform.SetParent(parent.transform);
            m_Objects.Add(go);
            var screen = go.AddComponent<UIScreenBehaviour>();
            screen.screenId = screenId;
            return screen;
        }

        private GameObject MakeUnit(string goName, StateTreeContextHost parent)
        {
            var go = new GameObject(goName);
            go.hideFlags = HideFlags.HideAndDontSave;
            if (parent != null)
                go.transform.SetParent(parent.transform);
            m_Objects.Add(go);
            return go;
        }

        /// <summary>An entry ROW (M13) — a plain serializable class, not an asset: the id
        /// is minted the way the dashboard would, the name is the runtime string.</summary>
        private static ItemDef MakeItem(string name, string displayName, ItemKind kind)
        {
            return new ItemDef
            {
                id = System.Guid.NewGuid().ToString("N"),
                name = name,
                displayName = displayName,
                kind = kind
            };
        }

        /// <summary>Aim a typed reference at an entry and bind it live — for the ATOM units,
        /// which tick tasks directly and so stand in for the executor's StartTree injection.</summary>
        private void SetItem(StateTreeEntryRef<ItemDef> reference, string name)
        {
            var def = (ItemDef)m_Registry.FindByName(name);
            reference.entryId = def.id;
            reference.entryName = def.name;
            ((IStateTreeEntryRef)reference).Bind(def);
        }

        private StateTreeNodeAsset MakeNode(string nodeId, params StateTreeTaskAsset[] tasks)
        {
            var node = ScriptableObject.CreateInstance<StateTreeNodeAsset>();
            node.nodeId = nodeId;
            node.name = "Node " + nodeId;
            for (int i = 0; i < tasks.Length; i++)
                node.tasks.Add(tasks[i]);
            m_Assets.Add(node);
            return node;
        }

        private static void AddTransition(StateTreeNodeAsset from, string to,
            StateTreeConditionAsset condition, bool interrupt)
        {
            from.transitions.Add(new StateTreeTransition
            {
                targetNodeId = to,
                condition = condition,
                checkWhileRunning = interrupt
            });
        }
    }
}
