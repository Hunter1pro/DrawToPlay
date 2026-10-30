using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using PowerOfFire.DrawToPlay.Editor;
using UnityEditor;
using UnityEngine;

namespace PowerOfFire.DrawToPlay.Tests
{
    /// <summary>
    /// Steps about a level object, from what its preset declares, on throwaway assets: a fact parameter knows the
    /// placement it is a fact of; the fact picker offers only what the target's preset writes, named by meaning,
    /// and validation flags a fact it does not write (no preset: as before); "Use in mission" lists the places
    /// under the object's level and the steps that fit, and adds one filled in, as one undo step; a row dropped
    /// on the State Tree window lands in or after what it is dropped on.
    /// </summary>
    [TestFixture]
    public sealed class StateTreeObjectStepsTests
    {
        private const string k_Folder = "Assets/__ObjectStepsTests";
        private readonly List<Object> m_Junk = new List<Object>();
        private StateTreeSchema m_Schema;
        private StateTreeAsset m_Use;
        private StateTreeAsset m_Walk;
        private LevelObjectRegistry m_Manifest;
        private LevelObjectPresetRegistry m_Presets;
        private StateTreeAsset m_Tree;
        private StateTreeNodeAsset m_Level;
        private StateTreeNodeAsset m_Zone;
        private StateTreeNodeAsset m_Step;

        [SetUp]
        public void SetUp()
        {
            if (!AssetDatabase.IsValidFolder(k_Folder))
                AssetDatabase.CreateFolder("Assets", "__ObjectStepsTests");

            m_Presets = Junk(ScriptableObject.CreateInstance<LevelObjectPresetRegistry>());
            var door = new LevelObjectPresetDef { id = "preset.door", name = "Door" };
            door.kind.entryName = "door";
            door.facts.Add(new LevelObjectPresetFact { key = new StateTreeKeyField("door.opened"), meaning = "Opened" });
            door.facts.Add(new LevelObjectPresetFact { key = new StateTreeKeyField("door.shut"), meaning = "Shut" });
            door.steps.AddRange(new[] { "use", "walk" });
            m_Presets.entries.Add(door);
            var marker = new LevelObjectPresetDef { id = "preset.marker", name = "Marker" };
            marker.kind.entryName = "marker";
            marker.steps.Add("walk");
            m_Presets.entries.Add(marker);

            m_Manifest = Junk(ScriptableObject.CreateInstance<LevelObjectRegistry>());
            m_Manifest.presets = m_Presets;
            Row("p.door", "hall.door.front", door);
            Row("p.mark", "hall.marker.1", marker);
            Row("p.plain", "hall.door.old", null);

            m_Use = Template(k_Folder + "/Use.asset", true);
            m_Walk = Template(k_Folder + "/Walk.asset", false);

            m_Schema = Junk(ScriptableObject.CreateInstance<StateTreeSchema>());
            m_Schema.treeKind = "object-steps-test";
            m_Schema.topKinds.Add("level");
            m_Schema.kinds.Add(new StateTreeSchemaKind
            {
                kind = "level", children = { "zone", StateTreeSchema.StepToken }, contextTask = typeof(SchemaStubArea).FullName
            });
            m_Schema.kinds.Add(new StateTreeSchemaKind
            {
                kind = "zone", children = { StateTreeSchema.StepToken, "talk" }, contextTask = typeof(SchemaStubArea).FullName
            });
            m_Schema.kinds.Add(new StateTreeSchemaKind { kind = "talk", children = { "branch" }, childLimit = 2 });
            m_Schema.kinds.Add(new StateTreeSchemaKind { kind = "branch", children = { StateTreeSchema.StepToken } });
            m_Schema.kinds.Add(new StateTreeSchemaKind { kind = "use", label = "Use", step = true, template = m_Use });
            m_Schema.kinds.Add(new StateTreeSchemaKind { kind = "walk", label = "Walk", step = true, template = m_Walk });
            StateTreeSchemas.Register(m_Schema);

            m_Tree = ScriptableObject.CreateInstance<StateTreeAsset>();
            m_Tree.treeKind = m_Schema.treeKind;
            AssetDatabase.CreateAsset(m_Tree, k_Folder + "/Host.asset");
            var root = ScriptableObject.CreateInstance<StateTreeNodeAsset>();
            root.nodeId = "root";
            AssetDatabase.AddObjectToAsset(root, m_Tree);
            m_Tree.root = root;
            m_Level = StateTreeSchemaRules.AddNode(m_Tree, m_Schema, root, "level");
            ((SchemaStubArea)m_Level.tasks[0]).manifest = m_Manifest;
            m_Zone = StateTreeSchemaRules.AddNode(m_Tree, m_Schema, m_Level, "zone");
            ((SchemaStubArea)m_Zone.tasks[0]).manifest = m_Manifest;
            m_Step = StateTreeSchemaRules.AddNode(m_Tree, m_Schema, m_Zone, "use");
            Undo.ClearAll();
        }

        [TearDown]
        public void TearDown()
        {
            StateTreePickMenu.headless = false;
            StateTreeSchemas.Unregister(m_Schema);
            foreach (StateTreeEditorWindow window in Resources.FindObjectsOfTypeAll<StateTreeEditorWindow>())
                window.Close();
            foreach (Object junk in m_Junk)
            {
                if (junk != null && !AssetDatabase.Contains(junk))
                    Object.DestroyImmediate(junk);
            }
            m_Junk.Clear();
            Undo.ClearAll();
            AssetDatabase.DeleteAsset(k_Folder);
        }

        // ---- the facts an object writes --------------------------------------------------

        [Test]
        public void AFactParameter_KnowsThePlacementItIsAFactOf()
        {
            Assert.AreEqual("target", StateTreeSchemaRules.FactTarget(m_Use, "fact"));
            Assert.IsNull(StateTreeSchemaRules.FactTarget(m_Use, "target"), "a placement is about itself");
            Assert.IsNull(StateTreeSchemaRules.FactTarget(m_Walk, "near"));
        }

        [Test]
        public void TheFactPicker_OffersOnlyWhatTheTargetsPresetWrites_ByMeaning_NoPresetOffersEveryKey()
        {
            StateTreePickMenu.headless = true;
            Set(m_Step, "target", "p.door");

            StateTreePickMenu menu = OpenFactPicker(m_Step);

            CollectionAssert.AreEqual(new[] { "door.opened", "door.shut" }, menu.items.Select(i => i.value).ToArray());
            CollectionAssert.AreEqual(new[] { "Opened", "Shut" }, menu.items.Select(i => i.label).ToArray());
            Assert.IsNull(menu.none, "a fact the object writes is picked, never left empty");

            Set(m_Step, "target", "p.plain");
            StateTreePickMenu plain = OpenFactPicker(m_Step);
            Assert.AreNotSame(menu, plain);
            Assert.IsFalse(plain.title.StartsWith("What ", System.StringComparison.Ordinal),
                "no preset: the key picker every step had");

            Set(m_Step, "target", "p.mark");
            StateTreePickMenu marker = OpenFactPicker(m_Step);
            Assert.IsFalse(marker.title.StartsWith("What ", System.StringComparison.Ordinal),
                "a preset that declares no facts narrows nothing");
        }

        [Test]
        public void Validation_FlagsAFactTheTargetsPresetDoesNotWrite_AndNothingWithoutAPreset()
        {
            Set(m_Step, "target", "p.door");
            Set(m_Step, "fact", "door.opened");
            Assert.IsFalse(FactProblems().Any(), "a fact it writes: " + string.Join(" | ", FactProblems()));

            Set(m_Step, "fact", "laptop.read");
            string problem = FactProblems().SingleOrDefault();
            Assert.IsNotNull(problem, "a fact it does not write is flagged");
            Assert.IsTrue(problem.Contains("Opened (door.opened)"), problem);

            Set(m_Step, "target", "p.plain");
            Assert.IsFalse(FactProblems().Any(), "no preset: any key, as before");
        }

        // ---- use in mission -------------------------------------------------------------

        [Test]
        public void UseInMission_ListsThePlacesUnderTheObjectsLevel_AndTheStepsThatFitThere()
        {
            StateTreeNodeAsset talk = StateTreeSchemaRules.AddNode(m_Tree, m_Schema, m_Zone, "talk");
            var trees = new List<StateTreeAsset> { m_Tree };

            List<StateTreeObjectSteps.Use> door = StateTreeObjectSteps.Uses(trees, m_Manifest, Row("p.door"));

            Assert.IsTrue(door.All(use => use.place != talk), "a talk holds branches, not steps");
            CollectionAssert.AreEquivalent(new[] { m_Level, m_Zone }, door.Select(use => use.place).Distinct().ToArray());
            CollectionAssert.AreEqual(new[] { "use", "walk" },
                door.Where(use => use.place == m_Zone).Select(use => use.kind.kind).ToArray(), "in the preset's order");
            Assert.IsTrue(door.Any(use => use.path == m_Level.displayName + " › " + m_Zone.displayName + "/Use"),
                string.Join(" | ", door.Select(use => use.path)));

            List<StateTreeObjectSteps.Use> marker = StateTreeObjectSteps.Uses(trees, m_Manifest, Row("p.mark"));
            Assert.IsTrue(marker.All(use => use.kind.kind == "walk"));
            Assert.IsTrue(marker.Any(use => use.path == m_Level.displayName + " › " + m_Zone.displayName),
                "one fitting step: no submenu");

            Assert.AreEqual(0, StateTreeObjectSteps.Uses(trees, m_Manifest, Row("p.plain")).Count, "no preset, no steps");
            var elsewhere = ScriptableObject.CreateInstance<LevelObjectRegistry>();
            Junk(elsewhere).presets = m_Presets;
            elsewhere.entries.Add(new LevelObjectDef { id = "p.door", preset = Row("p.door").preset });
            Assert.AreEqual(0, StateTreeObjectSteps.Uses(trees, elsewhere, elsewhere.entries[0]).Count,
                "another level's object has no place here");
        }

        [Test]
        public void UseInMission_AddsTheStepFilledIn_TheObjectAndItsPrimaryFact_OneUndo()
        {
            int before = m_Zone.children.Count;

            StateTreeNodeAsset step = StateTreeObjectSteps.Add(m_Tree, m_Schema, m_Zone, -1, "use", m_Manifest, Row("p.door"));

            Assert.IsNotNull(step);
            Assert.AreEqual("use", step.roleKind);
            Assert.AreSame(step, m_Zone.children.Last(), "appended to the zone");
            StateTreeStepParameter target = Parameter(step, "target");
            Assert.AreEqual("p.door", target.stringValue);
            Assert.AreEqual("p.door", target.overrideRow.entryId);
            Assert.AreEqual("door.opened", Parameter(step, "fact").stringValue, "the preset's primary fact");
            Assert.IsFalse(FactProblems().Any());

            Undo.PerformUndo();
            Assert.AreEqual(before, m_Zone.children.Count, "one undo step");

            StateTreeNodeAsset first = StateTreeObjectSteps.Add(m_Tree, m_Schema, m_Zone, 0, "walk", m_Manifest, Row("p.mark"));
            Assert.AreSame(first, m_Zone.children[0], "inserted where asked");
            Assert.AreEqual("p.mark", Parameter(first, "near").stringValue);

            StateTreeNodeAsset talk = StateTreeSchemaRules.AddNode(m_Tree, m_Schema, m_Zone, "talk");
            Assert.IsNull(StateTreeObjectSteps.Add(m_Tree, m_Schema, talk, -1, "use", m_Manifest, Row("p.door")),
                "only kinds the place allows");
        }

        [Test]
        public void ARowDroppedOnTheTree_LandsInAZone_OrAfterAStep_AndIsSelected()
        {
            StateTreeEditorWindow window = StateTreeEditorWindow.Open(m_Tree);
            Assert.IsTrue(window.missionMode);
            var drag = new LevelObjectDrag { manifest = m_Manifest, id = "p.door" };

            Assert.IsTrue(window.DropPlace(m_Step, drag, out StateTreeNodeAsset parent, out int index,
                out List<StateTreeSchemaKind> fits));
            Assert.AreSame(m_Zone, parent, "a step: after it, in its zone");
            Assert.AreEqual(m_Zone.children.IndexOf(m_Step) + 1, index);
            CollectionAssert.AreEqual(new[] { "use", "walk" }, fits.Select(kind => kind.kind).ToArray());

            Assert.IsTrue(window.DropPlace(m_Zone, drag, out parent, out index, out _));
            Assert.AreSame(m_Zone, parent, "a zone: into it");
            Assert.AreEqual(-1, index);

            Assert.IsFalse(window.DropPlace(m_Zone, new LevelObjectDrag { manifest = m_Manifest, id = "p.plain" },
                out _, out _, out _), "no preset: nothing fits");

            StateTreeNodeAsset added = window.DropObject(m_Zone, 0, "walk", m_Manifest, Row("p.door"));
            Assert.AreSame(added, m_Zone.children[0]);
            Assert.AreEqual(added.nodeId, window.selectedNodeId);
        }

        // ---- helpers ----------------------------------------------------------------------

        private StateTreePickMenu OpenFactPicker(StateTreeNodeAsset step)
        {
            var run = (RunSubTreeTask)step.tasks[0];
            var inputs = new StateTreeStepParameterInputs(m_Tree, m_Schema, step, m_Use, (_, _, _, _, _) => { });
            StateTreePickMenu.lastShown = null;
            var field = (StateTreePickField)inputs.Picker(StateTreeSchemaRules.Parameters(run).Single(p => p.declaration.id == "fact"), "Fact");
            field.Open();
            Assert.IsNotNull(StateTreePickMenu.lastShown);
            return StateTreePickMenu.lastShown;
        }

        private IEnumerable<string> FactProblems()
        {
            return StateTreeSchemaRules.Validate(m_Tree, m_Schema)
                .Where(problem => problem.message.Contains("does not write"))
                .Select(problem => problem.message);
        }

        private void Set(StateTreeNodeAsset step, string id, string value)
        {
            var run = (RunSubTreeTask)step.tasks[0];
            GraphTaskParameter declaration = run.subTree.parameters.Single(p => p.id == id);
            StateTreeSchemaRules.SetParameter(run, declaration, value, 0f, "", "t");
        }

        private static StateTreeStepParameter Parameter(StateTreeNodeAsset step, string id)
        {
            return StateTreeSchemaRules.Parameters((RunSubTreeTask)step.tasks[0]).Single(p => p.declaration.id == id);
        }

        private LevelObjectDef Row(string id) => (LevelObjectDef)m_Manifest.FindById(id);

        private void Row(string id, string name, LevelObjectPresetDef preset)
        {
            var row = new LevelObjectDef { id = id, name = name };
            if (preset != null)
            {
                row.kind.entryName = preset.kind.entryName;
                row.preset.entryId = preset.id;
                row.preset.entryName = preset.name;
            }
            m_Manifest.entries.Add(row);
        }

        /// <summary>A reusable tree whose placement parameter (and, for Use, a fact of it) bind into one task.</summary>
        private static StateTreeAsset Template(string path, bool withFact)
        {
            var tree = ScriptableObject.CreateInstance<StateTreeAsset>();
            tree.treeKind = "task";
            string near = withFact ? "target" : "near";
            tree.parameters.Add(new GraphTaskParameter { name = (withFact ? "use." : "walk.") + near, id = near, kind = GraphTaskParameterKind.String });
            if (withFact)
                tree.parameters.Add(new GraphTaskParameter { name = "use.fact", id = "fact", kind = GraphTaskParameterKind.String });
            AssetDatabase.CreateAsset(tree, path);
            var state = ScriptableObject.CreateInstance<StateTreeNodeAsset>();
            state.nodeId = "act";
            AssetDatabase.AddObjectToAsset(state, tree);
            var stub = ScriptableObject.CreateInstance<SchemaStubTask>();
            AssetDatabase.AddObjectToAsset(stub, tree);
            state.tasks.Add(stub);
            state.bindings.Add(new StateTreeFieldBinding { fieldName = "near", parameterId = near });
            if (withFact)
                state.bindings.Add(new StateTreeFieldBinding { fieldName = "key", parameterId = "fact" });
            tree.root = state;
            return tree;
        }

        private T Junk<T>(T thing) where T : Object
        {
            m_Junk.Add(thing);
            return thing;
        }
    }
}
