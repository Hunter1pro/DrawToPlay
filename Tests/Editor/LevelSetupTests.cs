using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using PowerOfFire.DrawToPlay.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace PowerOfFire.DrawToPlay.Tests
{
    /// <summary>
    /// A level's connections through the game's <see cref="ILevelSetup"/>, on throwaway objects: the Level Manifest
    /// shows the episode entry and the session, or that they are missing; the first object placed makes what is
    /// missing in the same undo step, and the next object makes nothing; a level in no episode is never connected;
    /// the catalogue the setup adds to shows its New level box; and the Scene view draws only the open levels' marks.
    /// </summary>
    [TestFixture]
    public sealed class LevelSetupTests
    {
        private readonly List<Object> m_Junk = new List<Object>();
        private StubSetup m_Setup;
        private LevelObjectKindRegistry m_Kinds;
        private LevelContent m_Level;
        private LevelManifestWindow m_Window;

        private sealed class StubSetup : ILevelSetup
        {
            public LevelRegistry levels { get; set; }
            public StateTreeAsset episode;
            public StateTreeAsset session;
            public StateTreeAsset sessionToMake;
            public int connects;

            public string placeLabel => "Episode";

            public IReadOnlyList<string> Places() => new[] { "E01", "E02" };

            public LevelDef Create(string name, string place, out string report)
            {
                report = "a stub makes nothing";
                return null;
            }

            public StateTreeAsset EpisodeOf(LevelContent level) => episode;

            public StateTreeAsset SessionOf(LevelContent level) => session;

            public List<string> Connect(LevelContent level)
            {
                connects++;
                var made = new List<string>();
                if (LevelLinks.LevelNode(episode, level, "cove") == null)
                {
                    var node = ScriptableObject.CreateInstance<StateTreeNodeAsset>();
                    node.nodeId = "mission.cove";
                    node.displayName = "Cove";
                    var context = ScriptableObject.CreateInstance<PickMenuStubLevel>();
                    context.level = "cove";
                    context.manifest = level.objects;
                    node.tasks.Add(context);
                    Undo.RecordObject(episode.root, "Connect");
                    episode.root.children.Add(node);
                    made.Add("its Level node");
                }
                if (session == null)
                {
                    session = sessionToMake;
                    made.Add("its tree");
                }
                return made;
            }
        }

        [SetUp]
        public void SetUp()
        {
            m_Kinds = Make<LevelObjectKindRegistry>();
            m_Kinds.entries.Add(new LevelObjectKindDef { id = "kind.door", name = "door" });

            var manifest = Make<LevelObjectRegistry>();
            manifest.plane = LevelGroundPlane.XZ;
            m_Level = Make<LevelContent>();
            m_Level.name = "CoveContent";
            m_Level.displayName = "Cove";
            m_Level.objects = manifest;

            var levels = Make<LevelRegistry>();
            levels.entries.Add(new LevelDef { id = "level.cove", name = "cove", content = m_Level });

            m_Setup = new StubSetup
            {
                levels = levels,
                episode = Tree("EpisodeTree"),
                sessionToMake = Tree("CoveTree")
            };
            LevelSetups.Register(m_Setup);
        }

        [TearDown]
        public void TearDown()
        {
            LevelSetups.Unregister(m_Setup);
            if (m_Window != null)
                Object.DestroyImmediate(m_Window);
            Undo.ClearUndo(m_Level.objects);
            if (m_Setup.episode != null)
                Undo.ClearUndo(m_Setup.episode.root);
            foreach (Object junk in m_Junk)
            {
                if (junk != null)
                    Object.DestroyImmediate(junk);
            }
            m_Junk.Clear();
        }

        [Test]
        public void TheManifest_ShowsTheEpisodeEntryAndTheSession_OrThatTheyAreMissing()
        {
            m_Window = Open();
            CollectionAssert.AreEqual(new[] { "missing", "missing" }, Links().Select(l => l.Split(' ')[0]).ToArray(),
                string.Join(" | ", Links()));

            m_Setup.Connect(m_Level);
            m_Window.Edit(m_Level, m_Kinds, "cove");

            List<string> links = Links();
            StringAssert.StartsWith("Cove", links[0], "the Level node, by its name");
            StringAssert.Contains("EpisodeTree", links[0], "in its tree");
            Assert.AreEqual("CoveTree", links[1]);
            Assert.AreEqual(3, m_Window.rootVisualElement.Query<Button>().ToList()
                .Count(b => b.text == "◎" || b.text == "▶"), "◎ on the node, ◎ and ▶ on the session");
        }

        [Test]
        public void TheFirstObjectPlaced_MakesWhatIsMissing_InItsUndoStep_TheNextMakesNothing()
        {
            m_Window = Open();
            LevelObjectDef first = m_Window.PlaceAtCentre("door");

            Assert.IsNotNull(first);
            Assert.AreEqual(1, m_Setup.connects);
            Assert.IsTrue(m_Window.links.complete, "the window shows what was made");
            Assert.IsNotNull(LevelLinks.LevelNode(m_Setup.episode, m_Level, "cove"));

            m_Window.PlaceAtCentre("door");
            Assert.AreEqual(1, m_Setup.connects, "a level connected is not connected again");
            Assert.AreEqual(1, m_Setup.episode.root.children.Count);

            Undo.PerformUndo();
            Undo.PerformUndo();
            Assert.AreEqual(0, m_Level.objects.entries.Count);
            Assert.AreEqual(0, m_Setup.episode.root.children.Count, "the Level node went with the first object's undo");
        }

        [Test]
        public void TheMissingLinks_HaveButtonsThatConnectNow_InOneUndoStep_ThenGoAway()
        {
            m_Window = Open();
            Assert.AreEqual(new[] { "+ Add to episode", "+ Create session tree" }, ConnectButtons(),
                "one beside each missing link");

            List<string> made = m_Window.Connect();
            Assert.AreEqual(2, made.Count, string.Join("; ", made));
            Assert.AreEqual(1, m_Setup.connects, "the first object's connect");
            Assert.IsTrue(m_Window.links.complete);
            Assert.IsEmpty(ConnectButtons(), "nothing left to make");
            Assert.AreEqual(0, m_Level.objects.entries.Count, "nothing was placed");

            Assert.IsEmpty(m_Window.Connect(), "made once");
            Assert.AreEqual(1, m_Setup.episode.root.children.Count);

            Undo.PerformUndo();
            Assert.AreEqual(0, m_Setup.episode.root.children.Count, "one undo takes the Level node back");
        }

        [Test]
        public void OnlyTheMissingLink_HasAButton_AndALevelInNoEpisodeHasNone()
        {
            m_Setup.session = m_Setup.sessionToMake;
            m_Window = Open();
            Assert.AreEqual(new[] { "+ Add to episode" }, ConnectButtons());

            m_Setup.episode = null;
            m_Setup.session = null;
            m_Window.Edit(m_Level, m_Kinds, "cove");
            Assert.IsEmpty(ConnectButtons(), "a level's episode is the setup's to say, not a button's");
            Assert.IsEmpty(m_Window.Connect());
            Assert.AreEqual(0, m_Setup.connects);
        }

        [Test]
        public void RemovingALevelNode_TakesItsZonesAndSteps_AndNothingElse()
        {
            StateTreeAsset episode = m_Setup.episode;
            var other = Make<StateTreeNodeAsset>();
            other.nodeId = "mission.hall";
            episode.root.children.Add(other);
            LevelSetups.ConnectIfMissing(m_Level);
            StateTreeNodeAsset node = LevelLinks.LevelNode(episode, m_Level, "cove");
            var zone = Make<StateTreeNodeAsset>();
            zone.nodeId = "mission.cove.zone";
            node.children.Add(zone);

            Assert.IsTrue(LevelLinks.RemoveLevelNode(episode, m_Level, "cove"));
            Assert.IsNull(LevelLinks.LevelNode(episode, m_Level, "cove"));
            CollectionAssert.AreEqual(new[] { other }, episode.root.children, "the other level stays");
            Assert.IsTrue(node == null && zone == null, "its node and its zone are gone");
            Assert.IsFalse(LevelLinks.RemoveLevelNode(episode, m_Level, "cove"), "nothing left to remove");
        }

        [Test]
        public void ALevelInNoEpisode_IsNeverConnected()
        {
            m_Setup.episode = null;
            m_Window = Open();
            m_Window.PlaceAtCentre("door");

            Assert.AreEqual(0, m_Setup.connects);
            Assert.IsTrue(Links()[0].StartsWith("—"), Links()[0]);
            Assert.IsTrue(LevelSetups.ConnectIfMissing(m_Level).Count == 0);
        }

        [Test]
        public void TheCatalogueTheSetupAddsTo_ShowsTheSetupsBox_AnotherKeepsTheFactorysFields()
        {
            var own = new NewLevelPanel(m_Setup.levels, null);
            Assert.AreEqual(1, own.Query<TextField>(className: NewLevelBox.NameClass).ToList().Count);
            DropdownField place = own.Query<DropdownField>(className: NewLevelBox.PlaceClass).First();
            Assert.AreEqual("Episode", place.label);
            CollectionAssert.AreEqual(new[] { "E01", "E02" }, place.choices);
            Assert.AreEqual("E01", place.value, "the first place is the default");

            var other = new NewLevelPanel(Make<LevelRegistry>(), null);
            Assert.AreEqual(0, other.Query<TextField>(className: NewLevelBox.NameClass).ToList().Count);
            Assert.IsTrue(other.Query<TextField>().ToList().Any(f => f.label == "In folder"));
        }

        [Test]
        public void WithNoLevelOpen_TheManifestOffersANewLevel()
        {
            m_Window = ScriptableObject.CreateInstance<LevelManifestWindow>();
            m_Window.Edit(null, m_Kinds, null);
            Assert.AreEqual(1, m_Window.rootVisualElement.Query<Button>(className: LevelManifestWindow.NewLevelClass)
                .ToList().Count);

            // A game's own setup stays registered in its project; where there is none, there is no button.
            LevelSetups.Unregister(m_Setup);
            m_Window.Edit(null, m_Kinds, null);
            if (LevelSetups.current == null)
                Assert.AreEqual(0, m_Window.rootVisualElement.Query<Button>(className: LevelManifestWindow.NewLevelClass)
                    .ToList().Count, "no setup, no button");
        }

        [Test]
        public void TheSceneView_DrawsOnlyTheOpenLevelsZones_AndThoseOfNoLevel()
        {
            var schema = Make<StateTreeSchema>();
            schema.treeKind = "setup-test";
            schema.topKinds.Add("zone");
            schema.kinds.Add(new StateTreeSchemaKind { kind = "zone", contextTask = typeof(SchemaStubArea).FullName });
            StateTreeAsset tree = Tree("Zones");
            var here = Manifest(new Vector2(1f, 1f));
            var there = Manifest(new Vector2(5f, 5f));
            var loose = Manifest(new Vector2(9f, 9f));
            StateTreeNodeAsset zoneHere = Zone(tree, here);
            StateTreeNodeAsset zoneThere = Zone(tree, there);
            StateTreeNodeAsset zoneLoose = Zone(tree, loose);
            var levelHere = Make<LevelContent>();
            levelHere.scenePath = "Assets/Here.unity";
            levelHere.objects = here;
            var levelThere = Make<LevelContent>();
            levelThere.scenePath = "Assets/There.unity";
            levelThere.objects = there;

            List<StateTreeSchemaGizmos.Mark> all = StateTreeSchemaGizmos.Collect(tree, schema);
            Assert.AreEqual(3, all.Count);

            List<StateTreeSchemaGizmos.Mark> shown = StateTreeSchemaGizmos.Visible(all,
                new[] { levelHere, levelThere }, new HashSet<string> { "Assets/Here.unity" });
            CollectionAssert.AreEquivalent(new[] { zoneHere, zoneLoose }, shown.Select(m => m.node).ToArray(),
                "the other level's zone stays out of this scene");

            shown = StateTreeSchemaGizmos.Visible(all, new[] { levelHere, levelThere }, new HashSet<string>());
            CollectionAssert.AreEquivalent(new[] { zoneLoose }, shown.Select(m => m.node).ToArray());
            Assert.IsFalse(shown.Any(m => m.node == zoneThere));
        }

        private StateTreeNodeAsset Zone(StateTreeAsset tree, LevelObjectRegistry manifest)
        {
            var node = Make<StateTreeNodeAsset>();
            node.nodeId = "zone." + tree.root.children.Count;
            node.roleKind = "zone";
            var area = Make<SchemaStubArea>();
            area.manifest = manifest;
            area.centre = "centre";
            node.tasks.Add(area);
            tree.root.children.Add(node);
            return node;
        }

        private LevelObjectRegistry Manifest(Vector2 centre)
        {
            var manifest = Make<LevelObjectRegistry>();
            manifest.plane = LevelGroundPlane.XZ;
            manifest.entries.Add(new LevelObjectDef { id = "centre", name = "centre", position = centre });
            return manifest;
        }

        private string[] ConnectButtons()
        {
            return m_Window.rootVisualElement.Query<Button>(className: LevelManifestWindow.ConnectClass).ToList()
                .Select(b => b.text).ToArray();
        }

        private List<string> Links()
        {
            return m_Window.rootVisualElement.Query(className: LevelManifestWindow.LinkClass).ToList()
                .Select(row => row.Q<Label>("value").text).ToList();
        }

        private LevelManifestWindow Open()
        {
            var window = ScriptableObject.CreateInstance<LevelManifestWindow>();
            window.Edit(m_Level, m_Kinds, "cove");
            return window;
        }

        private StateTreeAsset Tree(string name)
        {
            var tree = Make<StateTreeAsset>();
            tree.name = name;
            tree.root = Make<StateTreeNodeAsset>();
            tree.root.nodeId = "root";
            return tree;
        }

        private T Make<T>() where T : ScriptableObject
        {
            var made = ScriptableObject.CreateInstance<T>();
            m_Junk.Add(made);
            return made;
        }
    }
}
