using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using PowerOfFire.DrawToPlay.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace PowerOfFire.DrawToPlay.Tests
{
    /// <summary>What a template's task saw when it was entered: a key, a row and a placement, each
    /// written from a String parameter the calling state handed over.</summary>
    internal sealed class SchemaStubTask : StateTreeTaskAsset
    {
        internal static string seenKey;
        internal static string seenRow;
        internal static string seenPlacement;

        public StateTreeKeyField key = new StateTreeKeyField();

        public StateTreeEntryRef<LevelObjectDef> row = new StateTreeEntryRef<LevelObjectDef>();

        [PlacementId] public string near = "";

        public override void OnEnter(StateTreeContext context)
        {
            seenKey = key;
            seenRow = row.entry != null ? row.entry.name : "<none>";
            seenPlacement = near;
        }

        public override StateTreeStatus OnTick(StateTreeContext context, float deltaTime)
            => StateTreeStatus.Running;
    }

    /// <summary>A group's context with a place: a zone's manifest, centre and radius.</summary>
    internal sealed class SchemaStubArea : StateTreeTaskAsset, IStateTreeArea
    {
        public LevelObjectRegistry manifest;

        [PlacementId] public string centre = "";

        public float radius = 2f;

        public string areaCentre => centre;

        public float areaRadius => radius;
    }

    /// <summary>
    /// The schema mode: its rules, its factory, its validation, the binding a template needs, the
    /// keep a build script uses, the window and inspector hosts, and the gizmo marks.
    /// </summary>
    [TestFixture]
    public sealed class StateTreeSchemaTests
    {
        private const string k_Folder = "Assets/__SchemaTests";
        private readonly List<Object> m_Junk = new List<Object>();
        private StateTreeSchema m_Schema;
        private StateTreeAsset m_Template;
        private LevelObjectRegistry m_Manifest;

        [SetUp]
        public void SetUp()
        {
            if (!AssetDatabase.IsValidFolder(k_Folder))
                AssetDatabase.CreateFolder("Assets", "__SchemaTests");

            m_Manifest = Junk(ScriptableObject.CreateInstance<LevelObjectRegistry>());
            m_Manifest.entries.Add(new LevelObjectDef { id = "p.stand", name = "hall.stand", position = new Vector2(3f, 4f) });
            m_Manifest.entries.Add(new LevelObjectDef { id = "p.door", name = "hall.door", position = new Vector2(-1f, 2f) });

            m_Template = TemplateTree(k_Folder + "/Walk.asset");

            m_Schema = Junk(ScriptableObject.CreateInstance<StateTreeSchema>());
            m_Schema.treeKind = "schema-test";
            m_Schema.topKinds.Add("level");
            m_Schema.kinds.Add(new StateTreeSchemaKind { kind = "level", children = { "zone", StateTreeSchema.StepToken } });
            m_Schema.kinds.Add(new StateTreeSchemaKind
            {
                kind = "zone", children = { StateTreeSchema.StepToken, "talk" },
                contextTask = typeof(SchemaStubArea).FullName
            });
            m_Schema.kinds.Add(new StateTreeSchemaKind { kind = "talk", children = { "branch" }, childLimit = 2 });
            m_Schema.kinds.Add(new StateTreeSchemaKind { kind = "branch", children = { StateTreeSchema.StepToken, "talk" } });
            m_Schema.kinds.Add(new StateTreeSchemaKind { kind = "walk", step = true, template = m_Template });
            StateTreeSchemas.Register(m_Schema);
        }

        [TearDown]
        public void TearDown()
        {
            StateTreeSchemas.Unregister(m_Schema);
            foreach (Object junk in m_Junk)
            {
                if (junk != null && !AssetDatabase.Contains(junk))
                    Object.DestroyImmediate(junk);
            }
            m_Junk.Clear();
            foreach (StateTreeEditorWindow window in Resources.FindObjectsOfTypeAll<StateTreeEditorWindow>())
                window.Close();
            AssetDatabase.DeleteAsset(k_Folder);
        }

        // ---- rules ------------------------------------------------------------------------

        [Test]
        public void TheRules_OfferOnlyAllowedChildren_StepMeansEveryStep()
        {
            Assert.IsTrue(m_Schema.Allows("", "level"));
            Assert.IsFalse(m_Schema.Allows("", "zone"), "a zone is not a top kind");
            Assert.IsTrue(m_Schema.Allows("zone", "walk"), "'step' admits every step kind");
            Assert.IsFalse(m_Schema.Allows("talk", "walk"), "a talk holds branches only");
            Assert.IsFalse(m_Schema.Allows("walk", "walk"), "a step is a leaf");
            CollectionAssert.AreEqual(new[] { "branch" }, m_Schema.AllowedUnder("talk").Select(k => k.kind));
            CollectionAssert.AreEqual(new[] { "zone", "walk" }, m_Schema.AllowedUnder("level").Select(k => k.kind));
        }

        [Test]
        public void AddNode_MakesTheKindsState_RefusesWhatTheRulesDoNot_AndHonoursTheLimit()
        {
            StateTreeAsset tree = HostTree(k_Folder + "/Host.asset");
            StateTreeNodeAsset level = StateTreeSchemaRules.AddNode(tree, m_Schema, tree.root, "level");
            StateTreeNodeAsset zone = StateTreeSchemaRules.AddNode(tree, m_Schema, level, "zone");
            StateTreeNodeAsset walk = StateTreeSchemaRules.AddNode(tree, m_Schema, zone, "walk");

            Assert.AreEqual("level", level.roleKind);
            Assert.IsEmpty(level.tasks, "a group with no context holds nothing");
            Assert.IsInstanceOf<SchemaStubArea>(zone.tasks.Single(), "a zone holds its context");
            var run = (RunSubTreeTask)walk.tasks.Single();
            Assert.AreSame(m_Template, run.subTree, "a step is ONE sub-tree task of its template");

            Assert.IsNull(StateTreeSchemaRules.AddNode(tree, m_Schema, walk, "walk"), "a step is a leaf");
            Assert.IsNull(StateTreeSchemaRules.AddNode(tree, m_Schema, tree.root, "zone"), "not at the top");

            StateTreeNodeAsset talk = StateTreeSchemaRules.AddNode(tree, m_Schema, zone, "talk");
            Assert.IsNotNull(StateTreeSchemaRules.AddNode(tree, m_Schema, talk, "branch"));
            Assert.IsNotNull(StateTreeSchemaRules.AddNode(tree, m_Schema, talk, "branch"));
            Assert.IsNull(StateTreeSchemaRules.AddNode(tree, m_Schema, talk, "branch"), "one per answer");
        }

        [Test]
        public void Validate_SaysWhatIsMisplaced_WhatRunsNoTemplate_AndWhatIsNotSet()
        {
            StateTreeAsset tree = HostTree(k_Folder + "/Host.asset");
            StateTreeNodeAsset level = StateTreeSchemaRules.AddNode(tree, m_Schema, tree.root, "level");
            StateTreeNodeAsset walk = StateTreeSchemaRules.AddNode(tree, m_Schema, level, "walk");

            List<StateTreeSchemaProblem> problems = StateTreeSchemaRules.Validate(tree, m_Schema);
            Assert.AreEqual(1, problems.Count, string.Join("\n", problems.Select(p => p.message)));
            StringAssert.Contains("not set", problems[0].message, "the walk's target is empty");

            StateTreeSchemaRules.SetParameter((RunSubTreeTask)walk.tasks[0], m_Template.parameters[0], "p.stand", 0f, "p.stand", "t");
            Assert.IsEmpty(StateTreeSchemaRules.Validate(tree, m_Schema).Where(p => p.node == walk));

            // A zone dropped straight under the root, and a step whose template task is gone.
            StateTreeNodeAsset stray = StateTreeEditorOps.CreateNode(tree, tree.root, "stray", "Stray", "t");
            stray.roleKind = "zone";
            walk.tasks.Clear();
            problems = StateTreeSchemaRules.Validate(tree, m_Schema);
            Assert.IsTrue(problems.Any(p => p.node == stray && p.message.Contains("cannot sit at the top")));
            Assert.IsTrue(problems.Any(p => p.node == walk && p.message.Contains("template")));
        }

        // ---- the binding a template needs ------------------------------------------------

        [Test]
        public void AStringParameter_WritesAKey_ARow_AndAPlacement_IntoTheTemplatesTask()
        {
            StateTreeAsset template = Junk(ScriptableObject.CreateInstance<StateTreeAsset>());
            template.registries.Add(m_Manifest);
            template.parameters.Add(new GraphTaskParameter { name = "t.key", id = "k", kind = GraphTaskParameterKind.String });
            template.parameters.Add(new GraphTaskParameter { name = "t.row", id = "r", kind = GraphTaskParameterKind.String });
            template.parameters.Add(new GraphTaskParameter { name = "t.near", id = "n", kind = GraphTaskParameterKind.String });
            var state = Junk(ScriptableObject.CreateInstance<StateTreeNodeAsset>());
            state.nodeId = "s";
            var stub = Junk(ScriptableObject.CreateInstance<SchemaStubTask>());
            stub.key = new StateTreeKeyField("authored") { keyId = "stale-wire" };
            stub.row.entryId = "p.stand";
            state.tasks.Add(stub);
            state.bindings.Add(new StateTreeFieldBinding { fieldName = "key", parameterId = "k" });
            state.bindings.Add(new StateTreeFieldBinding { fieldName = "row", parameterId = "r" });
            state.bindings.Add(new StateTreeFieldBinding { fieldName = "near", parameterId = "n" });
            template.root = state;

            var caller = Junk(ScriptableObject.CreateInstance<StateTreeAsset>());
            var call = Junk(ScriptableObject.CreateInstance<StateTreeNodeAsset>());
            call.nodeId = "call";
            var run = Junk(ScriptableObject.CreateInstance<RunSubTreeTask>());
            run.subTree = template;
            run.overrides.Add(new GraphTaskParameterOverride { id = "k", name = "t.key", enabled = true, stringValue = "helped" });
            run.overrides.Add(new GraphTaskParameterOverride { id = "r", name = "t.row", enabled = true, stringValue = "hall.door", entryId = "p.door" });
            run.overrides.Add(new GraphTaskParameterOverride { id = "n", name = "t.near", enabled = true, stringValue = "p.stand" });
            call.tasks.Add(run);
            caller.root = call;

            var owner = Junk(new GameObject("owner") { hideFlags = HideFlags.HideAndDontSave });
            var executor = new StateTreeExecutor { data = caller, owner = owner };
            executor.StartTree();
            executor.TickTree(0.1f);
            executor.StopTree();

            Assert.AreEqual("helped", SchemaStubTask.seenKey, "the key is the parameter, the stale wire dropped");
            Assert.AreEqual("hall.door", SchemaStubTask.seenRow, "the row resolves by name in the template's Data");
            Assert.AreEqual("p.stand", SchemaStubTask.seenPlacement);
        }

        // ---- the keep a build script uses ------------------------------------------------

        [Test]
        public void DomainKeep_KeepsTheDomainNodesAndWhatTheyOwn_NotTheBuildersStates()
        {
            StateTreeAsset tree = HostTree(k_Folder + "/Host.asset");
            StateTreeNodeAsset builder = StateTreeEditorOps.CreateNode(tree, tree.root, "chapter", "Chapter", "t");
            StateTreeNodeAsset level = StateTreeSchemaRules.AddNode(tree, m_Schema, tree.root, "level");
            StateTreeNodeAsset walk = StateTreeSchemaRules.AddNode(tree, m_Schema, level, "walk");
            var condition = ScriptableObject.CreateInstance<AllCondition>();
            AssetDatabase.AddObjectToAsset(condition, tree);
            var nested = ScriptableObject.CreateInstance<HasBlackboardKeyCondition>();
            AssetDatabase.AddObjectToAsset(nested, tree);
            condition.conditions.Add(nested);
            walk.transitions.Add(new StateTreeTransition { targetNodeId = "chapter", condition = condition });

            StateTreeDomainKeep keep = StateTreeDomainKeep.Detach(tree);

            CollectionAssert.AreEqual(new[] { level }, keep.roots);
            Assert.IsTrue(keep.Keeps(level) && keep.Keeps(walk) && keep.Keeps(walk.tasks[0]));
            Assert.IsTrue(keep.Keeps(condition) && keep.Keeps(nested), "a composed condition goes with its node");
            Assert.IsFalse(keep.Keeps(builder) || keep.Keeps(tree.root));

            var rebuilt = ScriptableObject.CreateInstance<StateTreeNodeAsset>();
            Junk(rebuilt);
            keep.Reattach(rebuilt);
            CollectionAssert.AreEqual(new[] { level }, rebuilt.children);
        }

        // ---- the window, the inspector, the gizmos --------------------------------------

        [Test]
        public void TheWindow_OpensATreeOfTheSchemasKind_InTheMode_ShowingOnlyDomainNodes()
        {
            StateTreeAsset tree = HostTree(k_Folder + "/Host.asset");
            StateTreeEditorOps.CreateNode(tree, tree.root, "chapter", "Chapter", "t");
            StateTreeNodeAsset level = StateTreeSchemaRules.AddNode(tree, m_Schema, tree.root, "level");
            StateTreeNodeAsset walk = StateTreeSchemaRules.AddNode(tree, m_Schema, level, "walk");

            StateTreeEditorWindow window = StateTreeEditorWindow.Open(tree);

            Assert.AreSame(m_Schema, window.schema);
            Assert.IsTrue(window.missionMode);
            CollectionAssert.AreEquivalent(new[] { level.nodeId, walk.nodeId }, window.visibleNodeIds,
                "the root and the builder's chapter are hidden");

            window.SetDevMode(true);
            Assert.IsFalse(window.missionMode);
            CollectionAssert.Contains(window.visibleNodeIds, "chapter", "Dev mode is today's window");
            window.SetDevMode(false);
        }

        [Test]
        public void TheMissionInspector_IsAToolkitHost_ShowingAStepsParameters_NotItsStates()
        {
            StateTreeAsset tree = HostTree(k_Folder + "/Host.asset");
            StateTreeNodeAsset level = StateTreeSchemaRules.AddNode(tree, m_Schema, tree.root, "level");
            StateTreeNodeAsset zone = StateTreeSchemaRules.AddNode(tree, m_Schema, level, "zone");
            ((SchemaStubArea)zone.tasks[0]).manifest = m_Manifest;
            StateTreeNodeAsset walk = StateTreeSchemaRules.AddNode(tree, m_Schema, zone, "walk");
            StateTreeSchemaRules.SetParameter((RunSubTreeTask)walk.tasks[0], m_Template.parameters[0], "p.stand", 0f, "p.stand", "t");

            var root = new ScrollView();
            var inspector = new StateTreeMissionInspector(root, null, null, null, null);
            inspector.Rebuild(tree, m_Schema, walk, StateTreeSchemaRules.Validate(tree, m_Schema));

            List<string> text = root.Query<Label>().ToList().Select(l => l.text).ToList();
            text.AddRange(root.Query<Button>().ToList().Select(b => b.text));
            Assert.IsTrue(text.Any(t => t.StartsWith("hall.stand")), "the placement shows by its row's name: " + string.Join(" | ", text));
            Assert.IsTrue(text.Contains("Near"), "the parameter is named for the designer, namespace dropped");
            Assert.IsFalse(text.Any(t => t.Contains("SchemaStubTask")), "the template's tasks stay hidden");
            Assert.AreEqual("hall.stand", StateTreeSchemaRules.Summary(tree, m_Schema, walk));
        }

        [Test]
        public void TheGizmos_DrawTheZonesCircle_AndALineToTheRowAStepTargets()
        {
            StateTreeAsset tree = HostTree(k_Folder + "/Host.asset");
            StateTreeNodeAsset level = StateTreeSchemaRules.AddNode(tree, m_Schema, tree.root, "level");
            StateTreeNodeAsset zone = StateTreeSchemaRules.AddNode(tree, m_Schema, level, "zone");
            var area = (SchemaStubArea)zone.tasks[0];
            area.manifest = m_Manifest;
            area.centre = "p.door";
            area.radius = 3f;
            StateTreeNodeAsset walk = StateTreeSchemaRules.AddNode(tree, m_Schema, zone, "walk");
            StateTreeSchemaRules.SetParameter((RunSubTreeTask)walk.tasks[0], m_Template.parameters[0], "p.stand", 0f, "p.stand", "t");

            List<StateTreeSchemaGizmos.Mark> marks = StateTreeSchemaGizmos.Collect(tree, m_Schema);

            StateTreeSchemaGizmos.Mark circle = marks.Single(m => m.kind == StateTreeSchemaGizmos.MarkKind.Area);
            Assert.AreSame(zone, circle.node);
            Assert.AreEqual(m_Manifest.ToWorld(new Vector2(-1f, 2f)), circle.position);
            Assert.AreEqual(3f, circle.radius);
            StateTreeSchemaGizmos.Mark target = marks.Single(m => m.kind == StateTreeSchemaGizmos.MarkKind.Target);
            Assert.AreSame(walk, target.node);
            Assert.AreSame(zone, target.area, "selecting the zone lights its steps' lines");
            Assert.IsTrue(target.hasFrom);
            Assert.AreEqual(circle.position, target.from);
            Assert.AreEqual(m_Manifest.ToWorld(new Vector2(3f, 4f)), target.position);
        }

        // ---- helpers ----------------------------------------------------------------------

        /// <summary>A reusable tree with one String parameter bound into a [PlacementId] field.</summary>
        private StateTreeAsset TemplateTree(string path)
        {
            var tree = ScriptableObject.CreateInstance<StateTreeAsset>();
            tree.treeKind = "task";
            tree.parameters.Add(new GraphTaskParameter { name = "walk.near", id = "near", kind = GraphTaskParameterKind.String });
            AssetDatabase.CreateAsset(tree, path);
            var state = ScriptableObject.CreateInstance<StateTreeNodeAsset>();
            state.nodeId = "walk";
            AssetDatabase.AddObjectToAsset(state, tree);
            var stub = ScriptableObject.CreateInstance<SchemaStubTask>();
            AssetDatabase.AddObjectToAsset(stub, tree);
            state.tasks.Add(stub);
            state.bindings.Add(new StateTreeFieldBinding { fieldName = "near", parameterId = "near" });
            tree.root = state;
            return tree;
        }

        private static StateTreeAsset HostTree(string path)
        {
            var tree = ScriptableObject.CreateInstance<StateTreeAsset>();
            tree.treeKind = "schema-test";
            AssetDatabase.CreateAsset(tree, path);
            var root = ScriptableObject.CreateInstance<StateTreeNodeAsset>();
            root.nodeId = "root";
            AssetDatabase.AddObjectToAsset(root, tree);
            tree.root = root;
            return tree;
        }

        private T Junk<T>(T thing) where T : Object
        {
            m_Junk.Add(thing);
            return thing;
        }
    }
}
