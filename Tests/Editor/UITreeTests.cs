using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace PowerOfFire.DrawToPlay.Tests
{
    /// <summary>
    /// THE VIEW, AS A TEST STILL WANTS IT (M35.7) — two fields and two presses.
    ///
    /// IT LIVES IN THIS FILE ON PURPOSE: Unity refuses to AddComponent a MonoBehaviour that is
    /// an Editor-assembly script's PRIMARY class ("it is an editor script"), and a test
    /// component is exactly that unless it rides along as a second class in a test's own file —
    /// which is why every other test component in this suite does too.
    ///
    /// The UI tree tests used to have no view at all: they called the seam's Report* methods
    /// directly, which was honest while a seam existed. With screens spawned from rows there IS
    /// something on screen, so the tests wear the same base class a real skin does — it records
    /// what it was handed and turns a press into the same request a button would.
    /// </summary>
    internal sealed class ListSkin : UiViewBehaviour
    {
        public readonly List<UiListEntry> rows = new List<UiListEntry>();

        public string detail = "";

        public string clickKey = "clickedItem";

        public string closeKey = "";

        public override bool Call(string verb, string argument, object payload)
        {
            switch (verb)
            {
                case "list":
                    rows.Clear();
                    if (payload is IReadOnlyList<UiListEntry> given)
                        rows.AddRange(given);
                    return true;

                case "detail":
                    detail = argument ?? "";
                    return true;
            }
            return false;
        }

        public void Click(string itemId)
        {
            Request(clickKey, itemId);
        }

        public void Close()
        {
            Request(closeKey);
        }
    }

    /// <summary>
    /// The notebook's Inventory flow (brief §3.5) driven end to end in EditMode — the proof
    /// that "the state tree controls the state of UI": closed → (trigger) → inventory →
    /// (click a weapon) → equip → back; (click a consumable) → tooltip → close → back;
    /// (close) → closed — with the click carried by the skin's own REQUEST, the tooltip
    /// content by an entry-time ⚑ binding, and every screen hidden by state exit, Cancelled
    /// included.
    ///
    /// M35.7: one screen service. The flow used to run on an address book of scene-authored
    /// screens; it now runs on the same UI rows every other panel in the project uses — a row
    /// says what exists, the service spawns it, a task hands it content by verb, and a press
    /// leaves as a key. The tree above the seam did not change, which is the point worth
    /// keeping: the demo's thesis survived its plumbing being replaced.
    ///
    /// The view here is a two-field stub (<see cref="ListSkin"/>) — tests still ARE the view,
    /// they just wear the same base class a real skin does.
    /// </summary>
    [TestFixture]
    public sealed class UITreeTests
    {
        private const string k_OpenKey = "ui:openInventory";
        private const string k_ClickedKey = "clickedItem";
        // ONE DISMISS KEY PER SCREEN. A shared one is stale the moment it is used: the press
        // that closes the card would still be sitting there when the list came back, and the
        // list's own close-interrupt — checked before any task can clear it — would fire on it.
        private const string k_CloseInvKey = "ui:close.inv";
        private const string k_CloseTipKey = "ui:close.tip";

        private readonly List<GameObject> m_Objects = new List<GameObject>();
        private readonly List<ScriptableObject> m_Assets = new List<ScriptableObject>();
        private readonly List<StateTreeContextHost> m_Hosts = new List<StateTreeContextHost>();

        private StateTreeContextHost m_Player;
        private UiService m_Ui;
        private UiRegistry m_Screens;
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
            Assert.IsFalse(m_Ui.IsShown("inv"));

            // Trigger — a button, a level, any tree: one context key.
            m_Player.Context.blackboard[k_OpenKey] = 1f;
            Tick(runner, 1);
            Assert.AreEqual("inventory", runner.activeNodeId, "the open trigger interrupts in");
            // The state is entered on the tick the interrupt fires and its tasks run on the
            // next one — so the panel is up one tick later, filled by the same tick that
            // showed it (show precedes bind in the list, and a state's tasks all tick).
            Tick(runner, 1);
            Assert.IsTrue(m_Ui.IsShown("inv"), "the inventory screen is open");
            ListSkin list = Skin("inv");
            Assert.AreEqual(2, list.rows.Count,
                "both held items were bound on the first task tick");
            Assert.AreEqual("sword", list.rows[0].itemId);
            Assert.AreEqual(2, list.rows[1].count, "two potions, one row");
            Assert.IsFalse(m_Player.Context.blackboard.ContainsKey(k_OpenKey),
                "opening CONSUMED the trigger — the key is the event, the handler eats it");

            // Weapon click → equip → back, with the id delivered by the ⚑ entry binding.
            Skin("inv").Click("sword");
            Tick(runner, 4);
            Assert.AreEqual("inventory", runner.activeNodeId, "equip flowed back to inventory");
            Assert.AreEqual("sword", m_Player.Context.blackboard["equipped"],
                "the clicked weapon landed on the Player scope through the bound field");
            Assert.IsTrue(m_Ui.IsShown("inv"), "and the inventory reopened");

            // Consumable click → tooltip (content from the routed click), close → back.
            Skin("inv").Click("potion");
            Tick(runner, 3);
            Assert.AreEqual("tooltip", runner.activeNodeId, "a consumable opens the tooltip");
            Assert.IsTrue(m_Ui.IsShown("tooltip"), "tooltip screen shown");
            Assert.IsFalse(m_Ui.IsShown("inv"),
                "inventory hid when its state exited — and a screen is exclusive with a screen");
            Assert.AreEqual("Health Potion — Consumable", Skin("tooltip").detail,
                "the tooltip bound the CLICKED item's detail — even a tooltip is a state");

            Skin("tooltip").Close();
            Tick(runner, 2);
            Assert.AreEqual("inventory", runner.activeNodeId, "close returns to the previous screen");
            Assert.IsTrue(m_Ui.IsShown("inv"));
            Assert.IsFalse(m_Ui.IsShown("tooltip"));

            // Close → everything shut, trigger cleared, ready for the next open.
            Skin("inv").Close();
            Tick(runner, 2);
            Assert.AreEqual("closed", runner.activeNodeId);
            Assert.IsFalse(m_Ui.IsShown("inv"), "closing the inventory hid it");

            // And it opens again — the flow is a loop, not a one-shot.
            m_Player.Context.blackboard[k_OpenKey] = 1f;
            Tick(runner, 2);
            Assert.AreEqual("inventory", runner.activeNodeId);
            Assert.IsTrue(m_Ui.IsShown("inv"));
        }

        [Test]
        public void StoppingTheTree_HidesTheOpenScreen_Cancelled()
        {
            StateTreeRunner runner = BuildExample();
            runner.StartTree();
            m_Player.Context.blackboard[k_OpenKey] = 1f;
            Tick(runner, 2);
            Assert.IsTrue(m_Ui.IsShown("inv"));

            runner.StopTree();
            Assert.IsFalse(m_Ui.IsShown("inv"),
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

        /// <summary>Root → Level → Player hosts; the UI service on the Player object
        /// (per-player UI, which is what its placement means); two screen ROWS; the item
        /// registry; a seeded inventory.</summary>
        private void BuildSpine()
        {
            StateTreeContextHost root = MakeHost("Root", StateTreeContextKind.Root, null);
            StateTreeContextHost level = MakeHost("Level", StateTreeContextKind.Level, root);
            m_Player = MakeHost("P1", StateTreeContextKind.Player, level);

            m_Screens = ScriptableObject.CreateInstance<UiRegistry>();
            m_Assets.Add(m_Screens);
            m_Screens.entries.Add(Row("inv", 0f, k_CloseInvKey));
            m_Screens.entries.Add(Row("tooltip", 1f, k_CloseTipKey));

            var uiDef = ScriptableObject.CreateInstance<ServiceDef>();
            uiDef.name = "ui";
            uiDef.serviceName = "ui";
            uiDef.registry = m_Screens;
            m_Assets.Add(uiDef);
            m_Ui = new UiService(m_Player, uiDef);
            m_Player.Provide(m_Ui);

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
            var spendClose = ScriptableObject.CreateInstance<SetContextValueTask>();
            spendClose.scope = StateTreeContextKind.Player;
            spendClose.key.text = k_CloseInvKey;
            spendClose.kind = SetBlackboardTask.ValueKind.Clear;
            m_Assets.Add(spendClose);
            StateTreeNodeAsset closed = MakeNode("closed", spendClose, park);

            // inventory: CONSUME the trigger (clearing it in `closed` would lose a race
            // forever — interrupts run before task ticks, so the still-set trigger re-fires
            // into inventory before a clearing task there could run), bind, then hold the
            // screen open.
            var consumeTrigger = ScriptableObject.CreateInstance<SetContextValueTask>();
            consumeTrigger.scope = StateTreeContextKind.Player;
            consumeTrigger.key.text = k_OpenKey;
            consumeTrigger.kind = SetBlackboardTask.ValueKind.Clear;
            m_Assets.Add(consumeTrigger);
            // SHOW, THEN FILL: a skin exists only once its row is on screen.
            var showInv = ScriptableObject.CreateInstance<ShowUiTask>();
            SetUi(showInv.ui, "inv");
            showInv.holdWhileShown = true;
            m_Assets.Add(showInv);
            var bind = ScriptableObject.CreateInstance<BindInventoryListTask>();
            SetUi(bind.ui, "inv");
            m_Assets.Add(bind);
            StateTreeNodeAsset inventory = MakeNode("inventory", consumeTrigger, showInv, bind);

            // itemFlow: a beat that exists to BRANCH on the clicked item's kind.
            var flowMark = ScriptableObject.CreateInstance<SetBlackboardTask>();
            flowMark.key.text = "ui:flow";
            flowMark.kind = SetBlackboardTask.ValueKind.Float;
            flowMark.floatValue = 1f;
            m_Assets.Add(flowMark);
            var freshCard = ScriptableObject.CreateInstance<SetContextValueTask>();
            freshCard.scope = StateTreeContextKind.Player;
            freshCard.key.text = k_CloseTipKey;
            freshCard.kind = SetBlackboardTask.ValueKind.Clear;
            m_Assets.Add(freshCard);
            // The beat before the card opens forgets the last dismissal, so a card cannot be
            // born already closed.
            StateTreeNodeAsset itemFlow = MakeNode("itemFlow", flowMark, freshCard);

            // equip: publish the clicked weapon on the Player scope — stringValue is ⚑-bound
            // to the click, the M7k entry-time binding doing the delivery.
            var equip = ScriptableObject.CreateInstance<SetContextValueTask>();
            equip.scope = StateTreeContextKind.Player;
            equip.key.text = "equipped";
            equip.kind = SetBlackboardTask.ValueKind.String;
            m_Assets.Add(equip);
            // The state that HANDLES the click eats it — last, after the entry bindings have
            // read it, and never back in `inventory`, where the clear would beat the
            // transition that routes it.
            StateTreeNodeAsset equipNode = MakeNode("equip", equip, SpendClick());
            equipNode.bindings.Add(new StateTreeFieldBinding
            {
                targetKind = StateTreeFieldBinding.TargetKind.Task,
                targetIndex = 0,
                fieldName = "stringValue",
                sourceKind = StateTreeFieldBinding.SourceKind.BlackboardKey,
                blackboardKey = k_ClickedKey
            });

            // tooltip: hold the card open, then write on it from the click.
            var showTip = ScriptableObject.CreateInstance<ShowUiTask>();
            SetUi(showTip.ui, "tooltip");
            showTip.holdWhileShown = true;
            m_Assets.Add(showTip);
            var detail = ScriptableObject.CreateInstance<BindItemDetailTask>();
            SetUi(detail.ui, "tooltip");
            m_Assets.Add(detail);
            StateTreeNodeAsset tooltip = MakeNode("tooltip", showTip, detail, SpendClick());
            tooltip.bindings.Add(new StateTreeFieldBinding
            {
                targetKind = StateTreeFieldBinding.TargetKind.Task,
                targetIndex = 1,   // the bind, after the show
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
            // Interrupts: the panel holds the state open, so a press has to be able to route
            // out of it mid-run — waiting for "the tasks finished" would wait for the screen
            // to close, which is the thing the press is asking for.
            AddTransition(inventory, "itemFlow", clicked, interrupt: true);
            var closePressed = ScriptableObject.CreateInstance<HasBlackboardKeyCondition>();
            closePressed.key.text = k_CloseInvKey;
            m_Assets.Add(closePressed);
            AddTransition(inventory, "closed", closePressed, interrupt: true);

            // itemFlow: weapon -> equip; anything else -> tooltip.
            var isWeapon = ScriptableObject.CreateInstance<ItemKindCondition>();
            isWeapon.kind = ItemKind.Weapon;
            m_Assets.Add(isWeapon);
            AddTransition(itemFlow, "equip", isWeapon, interrupt: false);
            AddTransition(itemFlow, "tooltip", null, interrupt: false);

            AddTransition(equipNode, "inventory", null, interrupt: false);
            var tipClosed = ScriptableObject.CreateInstance<HasBlackboardKeyCondition>();
            tipClosed.key.text = k_CloseTipKey;
            m_Assets.Add(tipClosed);
            AddTransition(tooltip, "inventory", tipClosed, interrupt: true);

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
            tree.registries.Add(m_Screens);
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
            // THE PLAYER'S OWN BOARD, as the demo's mounted tree has: the skin's press lands on
            // the scope that owns the screen, so the tree that reads it must be reading the
            // same board. A private context here would make the click land in one dictionary
            // and be looked for in another — which is the fixture lying about the wiring.
            runner.context = m_Player.Context;
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

        /// <summary>One screen ROW over a stub skin. Both are Screens, so showing one hides
        /// the other — the exclusivity rule doing the work the old seam's Hide calls did.</summary>
        private UiDef Row(string rowName, float order, string closeKey)
        {
            var template = new GameObject("Screen " + rowName);
            template.hideFlags = HideFlags.HideAndDontSave;
            template.SetActive(false);
            ListSkin skin = template.AddComponent<ListSkin>();
            skin.closeKey = closeKey;
            m_Objects.Add(template);

            return new UiDef
            {
                id = "ui." + rowName,
                name = rowName,
                kind = UiKind.Screen,
                prefab = template,
                sortingOrder = order
            };
        }

        /// <summary>The shown row's skin — the same walk a task takes (see UiSkin).</summary>
        private ListSkin Skin(string rowName)
        {
            GameObject view = m_Ui.ShownView(rowName);
            Assert.IsNotNull(view, "row '" + rowName + "' is not on screen");
            var skin = view.GetComponentInChildren<ListSkin>(true);
            if (skin == null)
            {
                var names = new List<string>();
                foreach (Component part in view.GetComponents<Component>())
                    names.Add(part == null ? "<missing>" : part.GetType().Name);
                Assert.Fail("view '" + view.name + "' carries no skin — components: "
                    + string.Join(", ", names));
            }
            return skin;
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

        private SetContextValueTask SpendClick()
        {
            var spend = ScriptableObject.CreateInstance<SetContextValueTask>();
            spend.scope = StateTreeContextKind.Player;
            spend.key.text = k_ClickedKey;
            spend.kind = SetBlackboardTask.ValueKind.Clear;
            m_Assets.Add(spend);
            return spend;
        }

        private void SetUi(StateTreeEntryRef<UiDef> reference, string rowName)
        {
            var row = (UiDef)m_Screens.FindByName(rowName);
            reference.entryId = row.id;
            reference.entryName = row.name;
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
