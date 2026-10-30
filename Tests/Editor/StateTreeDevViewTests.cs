using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using PowerOfFire.DrawToPlay.Editor;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace PowerOfFire.DrawToPlay.Tests
{
    [Serializable]
    internal sealed class DevViewStubRow : StateTreeRegistryEntry
    {
    }

    internal sealed class DevViewStubRegistry : StateTreeRegistry<DevViewStubRow>
    {
    }

    [Serializable]
    internal sealed class DevViewCreatorRow : StateTreeRegistryEntry
    {
    }

    /// <summary>A registry that makes its own rows (as a level does): "+ New" only reveals it.</summary>
    internal sealed class DevViewCreatorRegistry : StateTreeRegistry<DevViewCreatorRow>
    {
    }

    [RegistryCreator(typeof(DevViewCreatorRegistry))]
    internal sealed class DevViewCreatorPanel : VisualElement
    {
        public DevViewCreatorPanel(DevViewCreatorRegistry registry, Action changed)
        {
        }
    }

    /// <summary>Every kind of field the Dev view draws: a number, a bound number, a row by name, a key by
    /// name, a placement, a typed row reference and a key contract.</summary>
    [StateTreeCategory("Tasks/Test", "A task for the dev view")]
    internal sealed class DevViewStubTask : StateTreeTaskAsset
    {
        public float speed = 2f;

        public int count;

        [RowName(typeof(DevViewStubRow))] public string row = "";

        [KeyName] public string fact = "";

        [PlacementId] public string at = "";

        public StateTreeEntryRef<DevViewStubRow> entry = new StateTreeEntryRef<DevViewStubRow>();

        [StateTreeKey(StateTreeKeyKind.Bool)]
        public StateTreeKeyField flag = new StateTreeKeyField();

        public override StateTreeStatus OnTick(StateTreeContext context, float deltaTime) => StateTreeStatus.Running;
    }

    [StateTreeCategory("Conditions/Test", "A condition for the dev view")]
    internal sealed class DevViewStubCondition : StateTreeConditionAsset
    {
        [KeyName] public string fact = "";

        public bool invert;

        public override bool Evaluate(StateTreeContext context) => true;
    }

    /// <summary>
    /// Dev mode in Mission mode's look, nothing lost: a header, a card per task and condition with its
    /// readable name and line, every serialized field still reached (the drawer-carrying ones through a
    /// PropertyField), quiet fields under "More", transitions as a sentence with a picked target. And "+ New"
    /// in the pickers: offered only where the target is known, one row in that registry, picked, one undo.
    /// </summary>
    [TestFixture]
    public sealed class StateTreeDevViewTests
    {
        private const string k_Folder = "Assets/__DevViewTests";
        private readonly List<Object> m_Junk = new List<Object>();

        [SetUp]
        public void SetUp()
        {
            StateTreePickMenu.headless = true;
            StateTreePickMenu.lastShown = null;
            if (!AssetDatabase.IsValidFolder(k_Folder))
                AssetDatabase.CreateFolder("Assets", "__DevViewTests");
        }

        [TearDown]
        public void TearDown()
        {
            StateTreePickMenu.headless = false;
            foreach (Object junk in m_Junk)
            {
                if (junk != null && !AssetDatabase.Contains(junk))
                    Object.DestroyImmediate(junk);
            }
            m_Junk.Clear();
            AssetDatabase.DeleteAsset(k_Folder);
        }

        // ---- the Dev view ----------------------------------------------------------------------

        [Test]
        public void ADevRowsCounts_AreWords_NotGlyphsTheFontLacks()
        {
            var node = ScriptableObject.CreateInstance<StateTreeNodeAsset>();
            try
            {
                node.roleKind = "zone";
                node.transitions.Add(new StateTreeTransition { targetNodeId = "a", checkWhileRunning = true });
                string counts = StateTreeEditorWindow.Counts(node);
                Assert.AreEqual("0 tasks · 1 → ⚡1", counts);
                Assert.IsFalse(counts.Contains('⟨'), "the kind is its own label, not a bracketed badge");
            }
            finally
            {
                Object.DestroyImmediate(node);
            }
        }

        [Test]
        public void TheDevView_HasMissionModesHeader_AndACardPerTaskAndCondition()
        {
            Fixture fixture = Build();
            VisualElement root = Draw(fixture);

            Assert.Greater(root.childCount, 0, "the pane builds");
            Label header = root.Q(className: StateTreeInspectorLook.HeaderClass)?.Query<Label>().ToList()[1];
            Assert.IsNotNull(header, "Mission mode's header heads the state");
            Assert.AreEqual("Work", header.text);
            List<string> sections = root.Query<Label>(className: StateTreeInspectorLook.SectionClass).ToList()
                .Select(l => l.text).ToList();
            CollectionAssert.IsSubsetOf(new[] { "Completion", "Tasks (1)", "Transitions (1)", "This tree" }, sections);

            VisualElement task = root.Q("dtp-task-0");
            Assert.IsNotNull(task, "the task is a card");
            Assert.IsTrue(task.ClassListContains(StateTreeInspectorLook.CardClass));
            Assert.AreEqual("Dev View Stub", task.Q<Label>(className: StateTreeInspectorLook.CardTitleClass).text,
                "named by its readable name, not its class");
            Assert.IsTrue(task.Query<Label>().ToList().Any(l => l.text.Contains("A task for the dev view")),
                "with its one line");
            Assert.IsNotNull(task.Q<Button>(className: StateTreeInspectorLook.MenuClass), "and a ⋮ for move / remove");
            Assert.IsFalse(task.Query<Button>().ToList().Any(b => b.text == "✕" && b.parent == task.ElementAt(0)),
                "no raw ✕ on the card's title line");

            VisualElement condition = root.Q("dtp-condition-0");
            Assert.IsNotNull(condition, "the condition is a card inside its transition");
            Assert.AreEqual("Dev View Stub", condition.Q<Label>(className: StateTreeInspectorLook.CardTitleClass).text);

            VisualElement transition = root.Q("dtp-transition-0");
            string sentence = transition.Q<Label>(className: StateTreeInspectorLook.CardTitleClass).text;
            StringAssert.Contains("When done and Dev View Stub", sentence);
            StringAssert.Contains("→ Done", sentence);
            Assert.IsInstanceOf<StateTreePickField>(root.Q("dtp-target-0"), "the target is the searchable picker");
        }

        [Test]
        public void TheDevView_ReachesEverySerializedField_DrawersThroughAPropertyField()
        {
            Fixture fixture = Build();
            VisualElement root = Draw(fixture);

            AssertEveryField(root.Q("dtp-task-0"), fixture.task, "overrides", "returns");
            AssertEveryField(root.Q("dtp-condition-0"), fixture.condition);

            foreach (string drawn in new[] { "row", "fact", "at" })
            {
                VisualElement row = root.Q("dtp-task-0").Q(StateTreeInspectorPane.FieldRowName(drawn));
                Assert.IsTrue(row.Query<PropertyField>().ToList().Any(p => p.bindingPath == drawn),
                    drawn + " carries a drawer and reaches it through a PropertyField");
            }

            // The transition's own fields: target, interrupt, condition, routes.
            VisualElement transition = root.Q("dtp-transition-0");
            Assert.IsNotNull(transition.Q<StateTreePickField>(), "target");
            Assert.IsTrue(transition.Query<Toggle>().ToList().Any(t => t.label == "Interrupt"), "interrupt");
            Assert.IsTrue(transition.Query<Button>().ToList().Any(b => b.text == "Dev View Stub"), "the condition's swap");
            Assert.IsTrue(transition.Query<Foldout>().ToList().Any(f => f.text.StartsWith("Route outputs")), "routes");
        }

        [Test]
        public void TheDevView_FoldsFieldsAtTheirDefault_ButNotABoundOne()
        {
            Fixture fixture = Build();
            VisualElement root = Draw(fixture);
            VisualElement task = root.Q("dtp-task-0");
            Foldout more = task.Q<Foldout>(className: StateTreeInspectorLook.MoreClass);
            Assert.IsNotNull(more, "the quiet fields are under More");

            bool InMore(string path) => more.Q(StateTreeInspectorPane.FieldRowName(path)) != null;
            Assert.IsFalse(InMore("speed"), "a value the author set is on the card");
            Assert.IsFalse(InMore("count"), "a field bound to a parameter is on the card, though at its default");
            Assert.IsTrue(InMore("fact"), "an empty key is quiet");
            Assert.IsTrue(InMore("at"), "an empty placement is quiet");
        }

        [Test]
        public void MovingATask_CarriesItsBindingsAndRoutes()
        {
            Fixture fixture = Build();
            var second = ScriptableObject.CreateInstance<DevViewStubTask>();
            AssetDatabase.AddObjectToAsset(second, fixture.tree);
            fixture.work.tasks.Add(second);
            fixture.work.transitions[0].outputRoutes.Add(new TransitionOutputRoute { taskIndex = 0, outputName = "x" });

            Assert.IsTrue(StateTreeEditorOps.MoveTask(fixture.tree, fixture.work, 0, 1, "Move"));
            Assert.AreSame(fixture.task, fixture.work.tasks[1]);
            Assert.AreEqual(1, fixture.work.bindings.Single().targetIndex, "the binding follows its task");
            Assert.AreEqual(1, fixture.work.transitions[0].outputRoutes.Single().taskIndex, "and so does the route");
            Assert.IsFalse(StateTreeEditorOps.MoveTask(fixture.tree, fixture.work, 1, 1, "Move"), "nothing past the end");
        }

        // ---- "+ New" ----------------------------------------------------------------------------

        [Test]
        public void NewRow_FromTheDevViewsEntryPicker_MakesOneRowInTheTreesRegistry_PicksIt_AndUndoes()
        {
            Fixture fixture = Build();
            var pane = new StateTreeInspectorPane(new ScrollView(), null, null);
            pane.Rebuild(fixture.tree, fixture.work);

            pane.ShowEntryPickMenu(default, fixture.task, "entry", fixture.task.entry);
            StateTreePickMenu menu = StateTreePickMenu.lastShown;
            Assert.AreEqual(1, menu.creates.Count, "one target: the tree's one registry of that row");
            StringAssert.StartsWith("+ New Dev View Stub Row in Rows", menu.Layout("")[0], "on top");

            int before = fixture.rows.entries.Count;
            menu.RunCreate(0, "fresh");
            Assert.AreEqual(before + 1, fixture.rows.entries.Count, "exactly one row");
            DevViewStubRow made = fixture.rows.entries.Last();
            Assert.AreEqual("fresh", made.name);
            Assert.IsFalse(string.IsNullOrEmpty(made.id), "with a fresh id");
            Assert.AreEqual(made.id, fixture.task.entry.entryId, "and it is the field's value");

            Undo.PerformUndo();
            Assert.AreEqual(before, fixture.rows.entries.Count, "one undo takes the row away");
            Assert.IsTrue(string.IsNullOrEmpty(fixture.task.entry.entryId), "and the pick with it");

            menu.RunCreate(0, "one");
            Assert.AreEqual(before, fixture.rows.entries.Count, "a taken name picks the row, never a twin");
            Assert.AreEqual("row.one", fixture.task.entry.entryId);
        }

        [Test]
        public void NewRow_IsOffered_OnlyWhereTheFieldCanResolve()
        {
            Fixture fixture = Build();
            var serialized = new SerializedObject(fixture.task);

            var container = new VisualElement();
            RowNameDrawer.Build(container, serialized, "row", "Row", typeof(DevViewStubRow));
            container.Q<StateTreePickField>().Open();
            Assert.AreEqual(1, StateTreePickMenu.lastShown.creates.Count, "the tree declares the registry");
            StringAssert.Contains("Rows", StateTreePickMenu.lastShown.creates[0].label);

            fixture.tree.registries.Clear();
            container = new VisualElement();
            RowNameDrawer.Build(container, serialized, "row", "Row", typeof(DevViewStubRow));
            container.Q<StateTreePickField>().Open();
            Assert.AreEqual(0, StateTreePickMenu.lastShown.creates.Count,
                "the project-wide fallback is no target: the field could not resolve a row made there");
        }

        [Test]
        public void NewRow_InARegistryWithItsOwnCreator_OnlyRevealsIt()
        {
            var registry = ScriptableObject.CreateInstance<DevViewCreatorRegistry>();
            AssetDatabase.CreateAsset(registry, k_Folder + "/Creator.asset");
            var menu = new StateTreePickMenu();
            menu.OfferNew(new List<StateTreeRegistryAsset> { registry });
            Assert.AreEqual(1, menu.creates.Count);
            Assert.IsNull(menu.creates[0].make, "its panel makes the row");
            Assert.AreSame(registry, menu.creates[0].reveal);
            menu.RunCreate(0, "x");
            Assert.AreEqual(0, registry.entries.Count);
        }

        [Test]
        public void NewPlacement_GetsAFreshId_AndIsPickedById()
        {
            var manifest = ScriptableObject.CreateInstance<LevelObjectRegistry>();
            AssetDatabase.CreateAsset(manifest, k_Folder + "/Objects.asset");
            var menu = new StateTreePickMenu();
            menu.OfferNew(new List<StateTreeRegistryAsset> { manifest }, byId: true);
            StateTreePickItem picked = null;
            menu.Show(default, "", item => picked = item);

            menu.RunCreate(0, "lamp");
            LevelObjectDef row = manifest.entries.Single();
            Assert.AreEqual("lamp", row.name);
            Assert.IsFalse(string.IsNullOrEmpty(row.id));
            Assert.AreEqual(StateTreeRowCreate.SceneCentre(manifest), row.position, "at the Scene view's centre");
            Assert.AreEqual(row.id, picked.value, "a placement field keeps the id");
        }

        [Test]
        public void NewKey_IsDeclaredOnTheTree_AndPicked()
        {
            Fixture fixture = Build();
            StateTreeKeyOffer picked = default;
            StateTreeKeyPick.Menu(default, new List<StateTreeKeyOffer>(), fixture.tree, StateTreeKeyKind.Bool, "", true,
                offer => picked = offer);
            StateTreePickMenu menu = StateTreePickMenu.lastShown;
            StringAssert.StartsWith("+ New key on", menu.Layout("")[0]);

            menu.RunCreate(0, "dev.fresh");
            StateTreeKeyDeclaration key = fixture.tree.keys.Single(k => k.name == "dev.fresh");
            Assert.AreEqual(StateTreeKeyKind.Bool, key.kind);
            Assert.AreEqual("dev.fresh", picked.name);
            Assert.AreEqual(key.id, picked.id);

            Undo.PerformUndo();
            Assert.IsFalse(fixture.tree.keys.Any(k => k != null && k.name == "dev.fresh"), "undone");
        }

        // ---- helpers ----------------------------------------------------------------------------

        private sealed class Fixture
        {
            public StateTreeAsset tree;
            public StateTreeNodeAsset work;
            public DevViewStubTask task;
            public DevViewStubCondition condition;
            public DevViewStubRegistry rows;
        }

        private Fixture Build()
        {
            var fixture = new Fixture();
            fixture.rows = ScriptableObject.CreateInstance<DevViewStubRegistry>();
            fixture.rows.entries.Add(new DevViewStubRow { id = "row.one", name = "one" });
            AssetDatabase.CreateAsset(fixture.rows, k_Folder + "/Rows.asset");

            var tree = ScriptableObject.CreateInstance<StateTreeAsset>();
            tree.treeKind = "dev-view-test";
            tree.registries.Add(fixture.rows);
            tree.parameters.Add(new GraphTaskParameter { id = "n", name = "n", kind = GraphTaskParameterKind.Float });
            AssetDatabase.CreateAsset(tree, k_Folder + "/Tree.asset");

            StateTreeNodeAsset Node(string id, string name)
            {
                var node = ScriptableObject.CreateInstance<StateTreeNodeAsset>();
                node.nodeId = id;
                node.displayName = name;
                AssetDatabase.AddObjectToAsset(node, tree);
                return node;
            }

            StateTreeNodeAsset root = Node("root", "");
            fixture.work = Node("work", "Work");
            StateTreeNodeAsset done = Node("done", "Done");
            root.children.Add(fixture.work);
            root.children.Add(done);
            tree.root = root;

            fixture.task = ScriptableObject.CreateInstance<DevViewStubTask>();
            fixture.task.speed = 5f;
            AssetDatabase.AddObjectToAsset(fixture.task, tree);
            fixture.work.tasks.Add(fixture.task);
            fixture.work.bindings.Add(new StateTreeFieldBinding
            {
                targetKind = StateTreeFieldBinding.TargetKind.Task, targetIndex = 0, fieldName = "count", parameterId = "n"
            });

            fixture.condition = ScriptableObject.CreateInstance<DevViewStubCondition>();
            AssetDatabase.AddObjectToAsset(fixture.condition, tree);
            fixture.work.transitions.Add(new StateTreeTransition { targetNodeId = "done", condition = fixture.condition });

            fixture.tree = tree;
            EditorUtility.SetDirty(tree);
            return fixture;
        }

        private static VisualElement Draw(Fixture fixture)
        {
            var root = new ScrollView();
            var pane = new StateTreeInspectorPane(root, null, null);
            pane.Rebuild(fixture.tree, fixture.work);
            return root;
        }

        /// <summary>Every visible top-level serialized field of <paramref name="target"/> has its row in the card.</summary>
        private static void AssertEveryField(VisualElement card, Object target, params string[] drawnElsewhere)
        {
            Assert.IsNotNull(card);
            var serialized = new SerializedObject(target);
            SerializedProperty iterator = serialized.GetIterator();
            bool enter = true;
            int seen = 0;
            while (iterator.NextVisible(enter))
            {
                enter = false;
                if (iterator.propertyPath == "m_Script" || drawnElsewhere.Contains(iterator.propertyPath))
                    continue;
                VisualElement row = card.Q(StateTreeInspectorPane.FieldRowName(iterator.propertyPath));
                Assert.IsNotNull(row, iterator.propertyPath + " is reachable on the card");
                Assert.Greater(row.childCount, 0, iterator.propertyPath + " draws something");
                seen++;
            }
            Assert.Greater(seen, 1);
        }
    }
}
