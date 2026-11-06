using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using PowerOfFire.DrawToPlay.Editor;
using UnityEditor;
using UnityEngine;

namespace PowerOfFire.DrawToPlay.Tests
{
    /// <summary>
    /// A level visited twice, on throwaway assets: the episode joins the level's first visit not finished and keeps
    /// where it stood per visit, a save from before visits is the first visit's; a visit stands as the level's rows less
    /// the hidden, then its own — in the spawner, the step pickers and the Level Manifest, where a row is hidden and shown
    /// in one undo step, the palette adds to the visit, and "+ New visit" makes its manifest and its Level node.
    /// </summary>
    [TestFixture]
    public sealed class LevelVisitTests
    {
        private const string k_Folder = "Assets/__LevelVisitTests";
        private readonly List<Object> m_Junk = new List<Object>();

        [SetUp]
        public void SetUp()
        {
            EpisodeStubStep.entered.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            StateTreePickMenu.headless = false;
            foreach (Object junk in m_Junk)
            {
                if (junk is GameObject go && go != null)
                {
                    foreach (StateTreeContextHost host in go.GetComponents<StateTreeContextHost>())
                        host.Unregister();
                }
            }
            for (int i = m_Junk.Count - 1; i >= 0; i--)
            {
                if (m_Junk[i] is LevelContent level && level != null)
                    LevelVisitsEditor.Pick(level, "");
                if (m_Junk[i] != null && !AssetDatabase.Contains(m_Junk[i]))
                    Object.DestroyImmediate(m_Junk[i]);
            }
            m_Junk.Clear();
            Undo.ClearAll();
            if (AssetDatabase.IsValidFolder(k_Folder))
                AssetDatabase.DeleteAsset(k_Folder);
        }

        // ---- the episode ---------------------------------------------------------------------

        /// <summary>root → hall (Hall: s1) · lobby (Lobby: l1) · hall2 (Hall: t1) — the hall visited twice.</summary>
        private StateTreeAsset Story()
        {
            var tree = Junk(ScriptableObject.CreateInstance<StateTreeAsset>());
            tree.root = Node("root");
            Step("s1", Level("hall", "Hall", tree.root));
            Step("l1", Level("lobby", "Lobby", tree.root));
            Step("t1", Level("hall2", "Hall", tree.root));
            return tree;
        }

        [Test]
        public void TheSecondVisit_JoinsOnceTheFirstIsDone_FromWhereTheStorySentIt()
        {
            StateTreeContextHost episode = Host("Episode", StateTreeContextKind.Episode, Story());
            StateTreeContextHost hall = Host("Hall", StateTreeContextKind.Level);
            Assert.IsTrue(episode.JoinEpisode("Hall", hall));
            Assert.AreEqual("hall", episode.episodeVisit, "the first visit first");
            Finish(episode, hall, "s1");
            Assert.AreEqual("hall2", episode.VisitToJoin("Hall"), "its part done: the next visit is the one to join");
            Unload(hall);

            StateTreeContextHost lobby = Host("Lobby", StateTreeContextKind.Level);
            Assert.IsTrue(episode.JoinEpisode("Lobby", lobby));
            Finish(episode, lobby, "l1");
            Unload(lobby);

            StateTreeContextHost again = Host("Hall again", StateTreeContextKind.Level);
            Assert.IsTrue(episode.JoinEpisode("Hall", again));
            Assert.AreEqual("hall2", episode.episodeVisit);
            Assert.AreEqual("t1", episode.activeNodeId, "the second visit's step runs");
            Assert.AreEqual(1, Entered("s1"), "the first visit is not played again");
            Assert.AreEqual("t1", episode.CursorOf("Hall").node, "the level's cursor is its latest visit's");
        }

        [Test]
        public void AVisitThatRestsAtItsEnd_IsOver_OneStillGoingIsNot()
        {
            StateTreeAsset tree = Story();
            StateTreeNodeAsset hall = tree.root.children[0];
            hall.completionFlow = StateTreeCompletionFlow.Hold;
            Step("s2", hall);
            StateTreeContextHost episode = Host("Episode", StateTreeContextKind.Episode, tree);

            Assert.IsFalse(LevelVisits.IsOver(tree, "hall", new StateTreeEpisodeCursor { node = "s1", complete = true }),
                "done with s1, s2 is next");
            Assert.IsFalse(LevelVisits.IsOver(tree, "hall", new StateTreeEpisodeCursor { node = "s2" }), "s2 running");
            Assert.IsTrue(LevelVisits.IsOver(tree, "hall", new StateTreeEpisodeCursor { node = "s2", complete = true }),
                "the last step done: the Level rests there, its part over");
            tree.root.children[0].children[1].disabled = true;
            Assert.IsTrue(LevelVisits.IsOver(tree, "hall", new StateTreeEpisodeCursor { node = "s1", complete = true }),
                "a switched-off step after it is passed through");

            episode.RestoreCursor("Hall", new StateTreeEpisodeCursor { node = "s1", complete = true });
            Assert.AreEqual("hall2", episode.VisitToJoin("Hall"), "a visit resting at its end: the next is joined");
        }

        [Test]
        public void AVisitWithEverythingSwitchedOff_IsPassed_UntilItsTheLast()
        {
            StateTreeAsset tree = Story();
            StateTreeContextHost episode = Host("Episode", StateTreeContextKind.Episode, tree);
            Assert.AreEqual("hall", episode.VisitToJoin("Hall"));
            tree.root.children[0].children[0].disabled = true;
            Assert.AreEqual("hall2", episode.VisitToJoin("Hall"), "nothing on in the first visit: the story is at the second");
            tree.root.children[2].children[0].disabled = true;
            Assert.AreEqual("hall2", episode.VisitToJoin("Hall"), "nothing on anywhere: the last");
        }

        [Test]
        public void ASaveFromBeforeVisits_LoadsAsTheFirstVisit_AndAFinishedFirstVisitGoesOnToTheSecond()
        {
            StateTreeContextHost episode = Host("Episode", StateTreeContextKind.Episode, Story());
            episode.RestoreCursor("Hall", new StateTreeEpisodeCursor { node = "s1" });
            StateTreeContextHost hall = Host("Hall", StateTreeContextKind.Level);
            Assert.IsTrue(episode.JoinEpisode("Hall", hall));
            Assert.AreEqual("hall", episode.episodeVisit);
            Assert.AreEqual("s1", episode.activeNodeId);
            Unload(hall);
            Unload(episode);

            StateTreeContextHost later = Host("Episode", StateTreeContextKind.Episode, Story());
            later.RestoreCursor("Hall", new StateTreeEpisodeCursor { node = "hall", complete = true });
            StateTreeContextHost back = Host("Hall", StateTreeContextKind.Level);
            Assert.IsTrue(later.JoinEpisode("Hall", back));
            Assert.AreEqual("hall2", later.episodeVisit, "the hall's part was over: its next visit");
            Assert.AreEqual("t1", later.activeNodeId);
        }

        [Test]
        public void TheSecondVisitsPlace_TravelsThroughASave_AndIsKnownBeforeTheRestore()
        {
            StateTreeContextHost episode = Host("Episode", StateTreeContextKind.Episode, Story());
            episode.RestoreCursor("Hall", new StateTreeEpisodeCursor { node = "hall2" });
            StateTreeContextHost hall = Host("Hall", StateTreeContextKind.Level);
            episode.JoinEpisode("Hall", hall);
            string saved = JsonUtility.ToJson(episode.CursorOf("Hall"));
            Unload(hall);
            Unload(episode);

            StateTreeContextHost relaunched = Host("Episode", StateTreeContextKind.Episode, Story());
            var cursor = JsonUtility.FromJson<StateTreeEpisodeCursor>(saved);
            Assert.AreEqual("hall2", relaunched.VisitToJoin("Hall", cursor), "a level's birth knows its visit before its restore");
            Assert.AreEqual("hall", relaunched.VisitToJoin("Hall"), "and without the save, the first");
            relaunched.RestoreCursor("Hall", cursor);
            StateTreeContextHost again = Host("Hall", StateTreeContextKind.Level);
            relaunched.JoinEpisode("Hall", again);
            Assert.AreEqual("hall2", relaunched.episodeVisit);
            Assert.AreEqual("t1", relaunched.activeNodeId);
            Assert.AreEqual(0, Entered("s1"));
        }

        // ---- what stands on a visit -------------------------------------------------------------

        [Test]
        public void AVisitsRows_AreTheLevelLessTheHidden_ThenItsOwn()
        {
            LevelObjectRegistry level = Manifest("bed", "find.18", "find.19", "lamp");
            LevelObjectRegistry visit = Visit(level, "bed.tidy", "find.new");
            visit.hidden.AddRange(new[] { "bed", "find.18", "find.19" });

            CollectionAssert.AreEqual(new[] { "lamp", "bed.tidy", "find.new" }, Ids(level, visit),
                "what the spawner builds on the visit");
            CollectionAssert.AreEqual(new[] { "bed", "find.18", "find.19", "lamp" }, Ids(level, null),
                "no visit: the level as it is");
            var offered = new List<LevelObjectDef>();
            StateTreeOffers.PlacementsFor(visit, offered);
            CollectionAssert.AreEqual(new[] { "lamp", "bed.tidy", "find.new" }, offered.Select(r => r.id).ToArray(),
                "and what a visit offers");

            Assert.AreEqual("lamp", visit.Row("lamp").name, "a level's row stands on the visit");
            Assert.IsNull(visit.Row("bed"), "a hidden one does not");
            Assert.AreEqual("bed.tidy", visit.Row("bed.tidy").name, "and the visit's own does");
            Assert.AreSame(level, visit.OwnerOf(visit.Row("lamp")));
        }

        [Test]
        public void ThePlacementPicker_UnderAVisit_ListsTheLevelLessTheHidden_ThenItsOwn_AndAHiddenTargetIsAProblem()
        {
            LevelObjectRegistry level = Manifest("hall.phone.1", "hall.bed", "hall.lamp");
            LevelObjectRegistry visit = Visit(level, "hall.bed.tidy");
            visit.hidden.Add("hall.bed");
            Mission(out StateTreeAsset tree, out StateTreeSchema schema, out StateTreeNodeAsset levelNode,
                out StateTreeNodeAsset step, out StateTreeAsset use);
            try
            {
                ((LevelVisitStubLevel)levelNode.tasks[0]).manifest = visit;
                StateTreePickMenu.headless = true;
                StateTreePickMenu picker = OpenPlacementPicker(tree, schema, step, use);
                CollectionAssert.AreEquivalent(new[] { "hall.phone.1", "hall.lamp", "hall.bed.tidy" },
                    picker.items.Select(i => i.value).Distinct().ToArray());
                Assert.IsTrue(picker.items.All(i => i.fits), "every one is this visit's, none under other levels");

                SetTarget(step, "hall.bed");
                Assert.IsTrue(StateTreeSchemaRules.Validate(tree, schema).Any(p => p.message.Contains("this visit of the level hides")),
                    "a step about a row the visit hides is on the problem list");
                SetTarget(step, "hall.lamp");
                Assert.IsFalse(StateTreeSchemaRules.Validate(tree, schema).Any(p => p.message.Contains("hides")));

                ((LevelVisitStubLevel)levelNode.tasks[0]).manifest = null;
                var stub = new VisitStubSetup { levels = Make<LevelRegistry>() };
                var content = Make<LevelContent>();
                content.objects = level;
                stub.levels.entries.Add(new LevelDef { id = "level.hall", name = "hall", content = content });
                LevelSetups.Register(stub);
                try
                {
                    CollectionAssert.AreEquivalent(new[] { "hall.phone.1", "hall.bed", "hall.lamp" },
                        OpenPlacementPicker(tree, schema, step, use).items.Select(i => i.value).Distinct().ToArray(),
                        "a visit with no manifest of its own is the plain level");
                }
                finally
                {
                    LevelSetups.Unregister(stub);
                }
            }
            finally
            {
                StateTreeSchemas.Unregister(schema);
            }
        }

        // ---- the Level Manifest -----------------------------------------------------------------

        [Test]
        public void TheManifest_OnAVisit_ShowsTheLevelsRowsDimmed_HidesAndShowsThemWithUndo_AndAddsToTheVisit()
        {
            VisitStubSetup setup = Setup(out LevelContent level, out LevelObjectRegistry visit, out StateTreeNodeAsset second);
            try
            {
                var window = Junk(ScriptableObject.CreateInstance<LevelManifestWindow>());
                window.Edit(level, Kinds(), "hall");
                Assert.AreSame(level.objects, window.edited, "the first visit: the level's own manifest");

                LevelVisitsEditor.Pick(level, second.nodeId);
                Assert.AreSame(visit, window.edited, "the visit picked: its manifest");
                CollectionAssert.AreEqual(new[] { false, false }, window.ShownBaseRows().Select(r => r.hidden).ToArray());
                StringAssert.Contains("from Hall", window.ShownBaseRows()[0].text);

                Assert.IsTrue(window.SetHiddenInVisit("hall.bed", true));
                Assert.IsTrue(visit.Hides("hall.bed"));
                Assert.IsTrue(window.ShownBaseRows()[0].hidden, "crossed out");
                StringAssert.Contains("<s>", window.ShownBaseRows()[0].text);
                Assert.IsTrue(level.objects.entries.Exists(r => r.id == "hall.bed"), "the level's row stays the level's");

                Undo.PerformUndo();
                Assert.IsFalse(visit.Hides("hall.bed"), "hiding is one undo step");
                Assert.IsTrue(LevelVisitsEditor.SetHidden(visit, "hall.bed", true));
                Assert.IsTrue(LevelVisitsEditor.SetHidden(visit, "hall.bed", false));
                Undo.PerformUndo();
                Assert.IsTrue(visit.Hides("hall.bed"), "showing too");

                LevelObjectDef added = window.PlaceAtCentre("prop");
                Assert.IsNotNull(added);
                CollectionAssert.Contains(visit.entries, added, "the palette adds to the visit");
                Assert.AreEqual(2, level.objects.entries.Count, "never to the level");
                Assert.IsNull(level.objects.FindById(added.id), "and does not take a level row's name");
            }
            finally
            {
                LevelSetups.Unregister(setup);
            }
        }

        [Test]
        public void NewVisit_MakesItsManifestBesideTheLevels_AndItsLevelNode_AndPicksIt()
        {
            VisitStubSetup setup = Setup(out LevelContent level, out _, out _);
            try
            {
                LevelVisitsEditor.Visit made = LevelVisitsEditor.NewVisit(level, out string report);
                Assert.IsNotNull(made, report);
                Assert.AreEqual("Hall_3", made.label);
                Assert.AreEqual(k_Folder + "/Hall_3Objects.asset", AssetDatabase.GetAssetPath(made.manifest));
                Assert.AreSame(level.objects, made.manifest.visitOf);
                Assert.AreSame(level.objects.presets, made.manifest.presets);
                Assert.AreEqual(3, LevelVisitsEditor.Of(level).Count, "its Level node is in the episode");
                Assert.AreEqual(made.node.nodeId, LevelVisitsEditor.Picked(level), "and it is the visit picked");
            }
            finally
            {
                LevelSetups.Unregister(setup);
            }
        }

        [Test]
        public void AVisitWithNoManifest_GetsOneOnTheDesignersClick_AndUntilThenTheWindowAddsNothing()
        {
            VisitStubSetup setup = Setup(out LevelContent level, out _, out StateTreeNodeAsset second);
            ((LevelVisitStubLevel)second.tasks[0]).manifest = null;
            try
            {
                var window = Junk(ScriptableObject.CreateInstance<LevelManifestWindow>());
                window.Edit(level, Kinds(), "hall");
                LevelVisitsEditor.Pick(level, second.nodeId);
                Assert.IsTrue(window.pickedVisit.lacksManifest);
                Assert.IsNull(window.PlaceAtCentre("prop"), "nothing lands in the level while its visit is picked");

                LevelObjectRegistry made = LevelVisitsEditor.MakeOwnManifest(level, window.pickedVisit);
                Assert.IsNotNull(made);
                Assert.AreSame(made, ((LevelVisitStubLevel)second.tasks[0]).manifest);
                Assert.AreSame(made, window.edited);
            }
            finally
            {
                LevelSetups.Unregister(setup);
            }
        }

        // ---- helpers ----------------------------------------------------------------------------

        private sealed class VisitStubSetup : ILevelSetup
        {
            public LevelRegistry levels { get; set; }
            public StateTreeAsset episode;
            public System.Func<LevelObjectRegistry, string, StateTreeNodeAsset> addVisit;

            public string placeLabel => "Episode";

            public IReadOnlyList<string> Places() => new[] { "E01" };

            public LevelDef Create(string name, string place, out string report)
            {
                report = "a stub makes nothing";
                return null;
            }

            public StateTreeAsset EpisodeOf(LevelContent level) => episode;

            public StateTreeAsset SessionOf(LevelContent level) => null;

            public List<string> Connect(LevelContent level) => new List<string>();

            public StateTreeNodeAsset AddVisit(LevelContent level, LevelObjectRegistry visit, string label) =>
                addVisit?.Invoke(visit, label);
        }

        /// <summary>A hall (its manifest an asset, two rows) in an episode with two visits: Hall, and Hall_2 with its own
        /// manifest.</summary>
        private VisitStubSetup Setup(out LevelContent level, out LevelObjectRegistry visit, out StateTreeNodeAsset second)
        {
            if (!AssetDatabase.IsValidFolder(k_Folder))
                AssetDatabase.CreateFolder("Assets", "__LevelVisitTests");
            LevelObjectRegistry objects = Manifest("hall.bed", "hall.phone.1");
            objects.presets = Make<LevelObjectPresetRegistry>();
            AssetDatabase.CreateAsset(objects, k_Folder + "/HallObjects.asset");
            level = Junk(ScriptableObject.CreateInstance<LevelContent>());
            level.objects = objects;
            AssetDatabase.CreateAsset(level, k_Folder + "/HallContent.asset");
            visit = Visit(objects);

            var tree = Junk(ScriptableObject.CreateInstance<StateTreeAsset>());
            tree.root = Node("root");
            Level("mission.hall", "hall", tree.root, objects).displayName = "Hall";
            second = Level("mission.hall.2", "hall", tree.root, visit);
            second.displayName = "Hall_2";

            var setup = new VisitStubSetup { levels = Make<LevelRegistry>(), episode = tree };
            setup.levels.entries.Add(new LevelDef { id = "level.hall", name = "hall", content = level });
            setup.addVisit = (manifest, label) =>
            {
                StateTreeNodeAsset node = Level("mission.hall." + label, "hall", tree.root, manifest);
                node.displayName = label;
                return node;
            };
            LevelSetups.Register(setup);
            return setup;
        }

        private LevelObjectKindRegistry Kinds()
        {
            var kinds = Make<LevelObjectKindRegistry>();
            kinds.entries.Add(new LevelObjectKindDef { id = "kind.prop", name = "prop" });
            return kinds;
        }

        private static string[] Ids(LevelObjectRegistry level, LevelObjectRegistry visit)
        {
            var rows = new List<LevelObjectDef>();
            LevelVisits.Rows(level, visit, rows);
            return rows.Select(r => r.id).ToArray();
        }

        private LevelObjectRegistry Manifest(params string[] ids)
        {
            var manifest = Make<LevelObjectRegistry>();
            manifest.plane = LevelGroundPlane.XZ;
            foreach (string id in ids)
            {
                var row = new LevelObjectDef { id = id, name = id };
                row.kind.entryId = "kind.prop";
                row.kind.entryName = "prop";
                manifest.entries.Add(row);
            }
            return manifest;
        }

        private LevelObjectRegistry Visit(LevelObjectRegistry level, params string[] own)
        {
            LevelObjectRegistry visit = Manifest(own);
            visit.visitOf = level;
            return visit;
        }

        /// <summary>A mission tree: a Level (its data a <see cref="LevelVisitStubLevel"/>) holding one Use step whose
        /// target is a placement.</summary>
        private void Mission(out StateTreeAsset tree, out StateTreeSchema schema, out StateTreeNodeAsset level,
            out StateTreeNodeAsset step, out StateTreeAsset use)
        {
            if (!AssetDatabase.IsValidFolder(k_Folder))
                AssetDatabase.CreateFolder("Assets", "__LevelVisitTests");
            use = ScriptableObject.CreateInstance<StateTreeAsset>();
            use.treeKind = "task";
            use.parameters.Add(new GraphTaskParameter { name = "use.target", id = "target", kind = GraphTaskParameterKind.String });
            AssetDatabase.CreateAsset(use, k_Folder + "/Use.asset");
            var act = ScriptableObject.CreateInstance<StateTreeNodeAsset>();
            act.nodeId = "act";
            AssetDatabase.AddObjectToAsset(act, use);
            var task = ScriptableObject.CreateInstance<SchemaStubTask>();
            AssetDatabase.AddObjectToAsset(task, use);
            act.tasks.Add(task);
            act.bindings.Add(new StateTreeFieldBinding { fieldName = "near", parameterId = "target" });
            use.root = act;

            schema = Make<StateTreeSchema>();
            schema.treeKind = "level-visit-test";
            schema.topKinds.Add("level");
            schema.kinds.Add(new StateTreeSchemaKind
            {
                kind = "level", children = { StateTreeSchema.StepToken }, contextTask = typeof(LevelVisitStubLevel).FullName
            });
            schema.kinds.Add(new StateTreeSchemaKind { kind = "use", label = "Use", step = true, template = use });
            StateTreeSchemas.Register(schema);

            tree = ScriptableObject.CreateInstance<StateTreeAsset>();
            tree.treeKind = schema.treeKind;
            AssetDatabase.CreateAsset(tree, k_Folder + "/Mission.asset");
            var root = ScriptableObject.CreateInstance<StateTreeNodeAsset>();
            root.nodeId = "root";
            AssetDatabase.AddObjectToAsset(root, tree);
            tree.root = root;
            level = StateTreeSchemaRules.AddNode(tree, schema, root, "level");
            ((LevelVisitStubLevel)level.tasks[0]).level = "hall";
            step = StateTreeSchemaRules.AddNode(tree, schema, level, "use");
        }

        private static StateTreePickMenu OpenPlacementPicker(StateTreeAsset tree, StateTreeSchema schema,
            StateTreeNodeAsset step, StateTreeAsset use)
        {
            var run = (RunSubTreeTask)step.tasks[0];
            var inputs = new StateTreeStepParameterInputs(tree, schema, step, use, (_, _, _, _, _) => { });
            StateTreePickMenu.lastShown = null;
            var field = (StateTreePickField)inputs.Picker(
                StateTreeSchemaRules.Parameters(run).Single(p => p.declaration.id == "target"), "Target");
            field.Open();
            Assert.IsNotNull(StateTreePickMenu.lastShown);
            return StateTreePickMenu.lastShown;
        }

        private static void SetTarget(StateTreeNodeAsset step, string value)
        {
            var run = (RunSubTreeTask)step.tasks[0];
            StateTreeSchemaRules.SetParameter(run, run.subTree.parameters.Single(p => p.id == "target"), value, 0f, "", "t");
        }

        private StateTreeNodeAsset Node(string id, StateTreeNodeAsset parent = null)
        {
            var node = Junk(ScriptableObject.CreateInstance<StateTreeNodeAsset>());
            node.nodeId = id;
            node.name = id;
            parent?.children.Add(node);
            return node;
        }

        private StateTreeNodeAsset Level(string id, string level, StateTreeNodeAsset parent, LevelObjectRegistry manifest = null)
        {
            StateTreeNodeAsset node = Node(id, parent);
            var context = Junk(ScriptableObject.CreateInstance<LevelVisitStubLevel>());
            context.level = level;
            context.manifest = manifest;
            context.blocking = false;
            node.tasks.Add(context);
            node.completionFlow = StateTreeCompletionFlow.NextSibling;
            return node;
        }

        private void Step(string id, StateTreeNodeAsset parent)
        {
            StateTreeNodeAsset node = Node(id, parent);
            node.roleKind = "walk";
            var step = Junk(ScriptableObject.CreateInstance<EpisodeStubStep>());
            step.step = id;
            node.tasks.Add(step);
        }

        private StateTreeContextHost Host(string name, StateTreeContextKind kind, StateTreeAsset tree = null)
        {
            var go = Junk(new GameObject(name));
            go.SetActive(false);
            var host = go.AddComponent<StateTreeContextHost>();
            host.kind = kind;
            host.tree = tree;
            host.autoStart = false;
            host.Register();
            return host;
        }

        /// <summary>The level's step done, ticked until the level's part has left it.</summary>
        private static void Finish(StateTreeContextHost episode, StateTreeContextHost level, string step)
        {
            level.Context.blackboard["done." + step] = 1f;
            for (int i = 0; i < 8 && episode.isRunning; i++)
                episode.TickTree(0.1f);
            Assert.IsFalse(episode.isRunning, "the level's part is over");
        }

        private static void Unload(StateTreeContextHost host)
        {
            host.Unregister();
            Object.DestroyImmediate(host.gameObject);
        }

        private static int Entered(string step) => EpisodeStubStep.entered.TryGetValue(step, out int n) ? n : 0;

        private T Make<T>() where T : ScriptableObject => Junk(ScriptableObject.CreateInstance<T>());

        private T Junk<T>(T thing) where T : Object
        {
            m_Junk.Add(thing);
            return thing;
        }
    }
}
