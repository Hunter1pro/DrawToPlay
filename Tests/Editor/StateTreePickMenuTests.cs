using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using PowerOfFire.DrawToPlay.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace PowerOfFire.DrawToPlay.Tests
{
    /// <summary>A flag check and a number, for the key narrowing.</summary>
    internal sealed class PickMenuStubCondition : StateTreeConditionAsset
    {
        [StateTreeKey(StateTreeKeyKind.Event, any: true)]
        public StateTreeKeyField flag = new StateTreeKeyField();

        [StateTreeKey(StateTreeKeyKind.Float)]
        public StateTreeKeyField amount = new StateTreeKeyField();
    }

    /// <summary>A level group's data: its manifest.</summary>
    internal sealed class PickMenuStubLevel : StateTreeTaskAsset, IStateTreeLevel
    {
        public string level = "";

        public LevelObjectRegistry manifest;

        public string levelName => level;
    }

    /// <summary>
    /// The one picker: a searchable dropdown every reference field opens — what this tree uses first, the
    /// catalogue grouped by meaning (keys by prefix), what the field narrows out under "Other", a meaning
    /// per item, the commands last.
    /// </summary>
    [TestFixture]
    public sealed class StateTreePickMenuTests
    {
        private const string k_Folder = "Assets/__PickMenuTests";
        private readonly List<Object> m_Junk = new List<Object>();

        [SetUp]
        public void SetUp()
        {
            StateTreePickMenu.headless = true;
            StateTreePickMenu.lastShown = null;
            if (!AssetDatabase.IsValidFolder(k_Folder))
                AssetDatabase.CreateFolder("Assets", "__PickMenuTests");
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

        private T Junk<T>(T thing) where T : Object
        {
            m_Junk.Add(thing);
            return thing;
        }

        [Test]
        public void TheLayout_InThisTreeFirst_ThenTheCatalogueByGroup_ThenOther_ThenTheCommands()
        {
            var menu = new StateTreePickMenu();
            menu.items.Add(new StateTreePickItem("room204.door.opened", null, "her door gave", "room204/door") { inTree = true });
            menu.items.Add(new StateTreePickItem("room204.door.closed", null, "her door shut", "room204/door"));
            menu.items.Add(new StateTreePickItem("level:current", null, "the level up", "level") { fits = false });
            menu.Command("Declare a new key…", () => { });

            List<string> layout = menu.Layout("room204.door.closed");

            CollectionAssert.AreEqual(new[]
            {
                StateTreePickMenu.NoneLabel,
                "# " + StateTreePickMenu.InTreeHeader,
                "room204.door.opened  —  her door gave",
                "---",
                "room204/door/● room204.door.closed  —  her door shut",
                "Other/level/level:current  —  the level up",
                "---",
                "> Declare a new key…"
            }, layout);
        }

        [Test]
        public void ChoosingALine_OfTheDropdown_PicksItsItem_OrRunsItsCommand()
        {
            var menu = new StateTreePickMenu();
            var used = new StateTreePickItem("hall.help", null, "Meeting Nadine", "Episode 1") { inTree = true };
            var grouped = new StateTreePickItem("hall.knock", null, "", "Episode 1");
            var other = new StateTreePickItem("riverside.end", null, "", "Riverside") { fits = false };
            menu.items.Add(used);
            menu.items.Add(grouped);
            menu.items.Add(other);
            bool commanded = false;
            menu.Command("Declare…", () => commanded = true);
            menu.Create("+ New Video…", "Name", name => new StateTreePickItem(name));

            var picked = new List<StateTreePickItem>();
            var dropdown = new StateTreePickDropdown(menu, "", picked.Add);
            List<UnityEditor.IMGUI.Controls.AdvancedDropdownItem> lines = Lines(dropdown.Root()).ToList();
            List<StateTreePickDropdown.Leaf> leaves = lines.OfType<StateTreePickDropdown.Leaf>().ToList();

            // Unity re-numbers an item's id as it is added to its parent: chosen by id, a line picked nothing.
            foreach (StateTreePickItem item in new[] { used, grouped, other })
            {
                dropdown.Choose(leaves.First(l => l.item == item));
                Assert.AreSame(item, picked.LastOrDefault(), item.value + " is what its line picks");
            }
            dropdown.Choose(leaves.Single(l => l.item != null && l.item.value == ""));
            Assert.AreEqual("", picked.Last().value, "(none) picks empty");
            Assert.AreEqual(4, picked.Count);

            dropdown.Choose(leaves.Single(l => l.command != null));
            Assert.IsTrue(commanded, "a command line runs it");
            Assert.AreEqual(0, leaves.Single(l => l.create >= 0).create, "+ New is its create");

            foreach (UnityEditor.IMGUI.Controls.AdvancedDropdownItem line in lines.Where(l => !(l is StateTreePickDropdown.Leaf)))
                dropdown.Choose(line);
            Assert.AreEqual(4, picked.Count, "a header, a group or a separator picks nothing");
        }

        private static IEnumerable<UnityEditor.IMGUI.Controls.AdvancedDropdownItem> Lines(UnityEditor.IMGUI.Controls.AdvancedDropdownItem at)
        {
            foreach (UnityEditor.IMGUI.Controls.AdvancedDropdownItem child in at.children)
            {
                yield return child;
                foreach (UnityEditor.IMGUI.Controls.AdvancedDropdownItem below in Lines(child))
                    yield return below;
            }
        }

        [Test]
        public void AKeysGroup_IsItsPrefix()
        {
            Assert.AreEqual("room204/door", StateTreePickMenu.PrefixGroup("room204.door.opened"));
            Assert.AreEqual("level", StateTreePickMenu.PrefixGroup("level:current"));
            Assert.AreEqual("", StateTreePickMenu.PrefixGroup("plain"));
        }

        [Test]
        public void AFlagCheck_IsOfferedFlagsFirst_ATypedField_ItsOwnKind()
        {
            var offers = new List<StateTreeKeyOffer>
            {
                new StateTreeKeyOffer("door.open", "k1", "declared", "", StateTreeKeyKind.Bool),
                new StateTreeKeyOffer("door.knocked", "k2", "declared", "", StateTreeKeyKind.Event),
                new StateTreeKeyOffer("door.count", "k3", "declared", "", StateTreeKeyKind.Float),
                new StateTreeKeyOffer("door.who", "k4", "declared", "", StateTreeKeyKind.String),
            };
            StateTreePickMenu flags = StateTreePickItems.Keys(offers, null, StateTreeKeyKind.Event, presence: true);
            CollectionAssert.AreEquivalent(new[] { "door.open", "door.knocked" }, flags.items.Where(i => i.fits).Select(i => i.value));
            StateTreePickMenu numbers = StateTreePickItems.Keys(offers, null, StateTreeKeyKind.Float, presence: false);
            CollectionAssert.AreEqual(new[] { "door.count" }, numbers.items.Where(i => i.fits).Select(i => i.value));
            Assert.AreEqual(4, numbers.items.Count, "narrowed, never hidden: the rest wait under Other");
            StringAssert.StartsWith("Float", numbers.items.Single(i => i.value == "door.count").meaning, "a meaning per item");
        }

        [Test]
        public void ACatalogKey_CarriesTheFirstSentenceOfItsSummary()
        {
            Dictionary<string, string> meanings = StateTreeKeyOffers.ParseSummaries(
                "/// <summary>Her door gave. It stays given.</summary>\n"
                + "public const string Opened = \"door.opened\";\n"
                + "public const string Bare = \"door.bare\";\n"
                + "/// <summary>A request.</summary>\n[ServiceRequestKey]\npublic const string Asked = \"door.asked\";\n");
            Assert.AreEqual("Her door gave.", meanings["Opened"]);
            Assert.AreEqual("", meanings["Bare"], "no summary, no meaning");
            Assert.AreEqual("A request.", meanings["Asked"], "an attribute between the summary and the constant");
        }

        [Test]
        public void WhatThisTreeUses_IsFound_InTasksConditionsAndASteps_Values()
        {
            var tree = Junk(ScriptableObject.CreateInstance<StateTreeAsset>());
            tree.root = Junk(ScriptableObject.CreateInstance<StateTreeNodeAsset>());
            tree.root.nodeId = "root";
            var task = Junk(ScriptableObject.CreateInstance<PickStubTask>());
            task.named = "used.by.task";
            tree.root.tasks.Add(task);
            var condition = Junk(ScriptableObject.CreateInstance<PickMenuStubCondition>());
            condition.flag = new StateTreeKeyField("used.by.condition");
            var all = Junk(ScriptableObject.CreateInstance<AllCondition>());
            all.conditions.Add(condition);
            tree.root.transitions.Add(new StateTreeTransition { targetNodeId = "root", condition = all });
            var run = Junk(ScriptableObject.CreateInstance<RunSubTreeTask>());
            run.overrides.Add(new GraphTaskParameterOverride { id = "p", name = "walk.near", enabled = true, stringValue = "used.by.step" });
            tree.root.tasks.Add(run);

            HashSet<string> used = StateTreePickMenu.UsedIn(tree);

            CollectionAssert.IsSubsetOf(new[] { "used.by.task", "used.by.condition", "used.by.step" }, used);
        }

        [Test]
        public void APlacement_InALevelsNode_IsNarrowedToThatLevelsManifest()
        {
            var hall = Junk(ScriptableObject.CreateInstance<LevelObjectRegistry>());
            hall.name = "Hall";
            hall.entries.Add(new LevelObjectDef { id = "hall.door", name = "door" });
            var lobby = Junk(ScriptableObject.CreateInstance<LevelObjectRegistry>());
            lobby.name = "Lobby";
            lobby.entries.Add(new LevelObjectDef { id = "lobby.desk", name = "desk" });

            var schema = Junk(ScriptableObject.CreateInstance<StateTreeSchema>());
            schema.treeKind = "pick-menu-test";
            schema.topKinds.Add("level");
            schema.kinds.Add(new StateTreeSchemaKind { kind = "level", children = { StateTreeSchema.StepToken }, contextTask = typeof(PickMenuStubLevel).FullName, rests = true });
            schema.kinds.Add(new StateTreeSchemaKind { kind = "step", step = true });
            StateTreeSchemas.Register(schema);
            try
            {
                var tree = ScriptableObject.CreateInstance<StateTreeAsset>();
                tree.treeKind = schema.treeKind;
                AssetDatabase.CreateAsset(tree, k_Folder + "/Episode.asset");
                var root = ScriptableObject.CreateInstance<StateTreeNodeAsset>();
                root.nodeId = "root";
                AssetDatabase.AddObjectToAsset(root, tree);
                tree.root = root;
                StateTreeNodeAsset level = StateTreeSchemaRules.AddNode(tree, schema, root, "level");
                Assert.AreEqual(StateTreeCompletionFlow.Hold, level.completionFlow, "a level rests where its run ends");
                ((PickMenuStubLevel)level.tasks[0]).manifest = hall;
                var task = ScriptableObject.CreateInstance<PickStubTask>();
                AssetDatabase.AddObjectToAsset(task, tree);
                StateTreeNodeAsset step = StateTreeEditorOps.CreateNode(tree, level, "step", "Step", "test");
                step.tasks.Add(task);

                Assert.AreSame(hall, StateTreePickItems.Enclosing(task, typeof(LevelObjectDef)));
                StateTreePickMenu menu = StateTreePickItems.Placements(
                    hall.entries.Concat(lobby.entries).ToList(), tree, hall);
                CollectionAssert.AreEqual(new[] { "hall.door" }, menu.items.Where(i => i.fits).Select(i => i.value));
                Assert.AreEqual("door", menu.items.Single(i => i.value == "hall.door").label, "shown by name, written by id");
            }
            finally
            {
                StateTreeSchemas.Unregister(schema);
            }
        }

        [TestCase("key", typeof(StateTreeKeyField))]
        [TestCase("near", typeof(PlacementIdAttribute))]
        [TestCase("tag", typeof(WorldTagAttribute))]
        [TestCase("level", typeof(RowNameAttribute))]
        [TestCase("named", typeof(KeyNameAttribute))]
        [TestCase("scopeId", typeof(ScopeIdAttribute))]
        [TestCase("state", typeof(SubTreeStateAttribute))]
        public void EveryPicker_OpensTheOneSearchableDropdown(string fieldName, Type drawnFor)
        {
            var task = Junk(ScriptableObject.CreateInstance<PickStubTask>());
            var serialized = new SerializedObject(task);
            VisualElement drawn = Draw(drawnFor, typeof(PickStubTask).GetField(fieldName), serialized.FindProperty(fieldName));
            StateTreePickField field = drawn.Q<StateTreePickField>();
            Assert.IsNotNull(field);
            Assert.IsTrue(field.button.text.EndsWith("▾"), "the drop-down look, unchanged");

            field.Open();

            Assert.IsNotNull(StateTreePickMenu.lastShown, fieldName + " opens the shared dropdown");
        }

        [Test]
        public void TheNotFoundDisplay_IsUnchanged()
        {
            var field = new StateTreePickField("Near", "p.gone", true, "", _ => { });
            Assert.AreEqual("p.gone   ✕ not found  ▾", field.button.text);
            Assert.IsTrue(field.missing);
        }

        /// <summary>The drawer Unity would pick for the field, built the way a PropertyField builds it.</summary>
        private static VisualElement Draw(Type drawnFor, FieldInfo field, SerializedProperty property)
        {
            Type drawerType = TypeCache.GetTypesWithAttribute<CustomPropertyDrawer>().First(t =>
                t.GetCustomAttributes<CustomPropertyDrawer>(false).Any(a =>
                    (Type)typeof(CustomPropertyDrawer).GetField("m_Type", BindingFlags.NonPublic | BindingFlags.Instance)
                        .GetValue(a) == drawnFor));
            var drawer = (PropertyDrawer)Activator.CreateInstance(drawerType, true);
            typeof(PropertyDrawer).GetField("m_FieldInfo", BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(drawer, field);
            object attribute = field.GetCustomAttributes(true).FirstOrDefault(a => a.GetType() == drawnFor);
            if (attribute != null)
                typeof(PropertyDrawer).GetField("m_Attribute", BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(drawer, attribute);
            return drawer.CreatePropertyGUI(property);
        }
    }
}
