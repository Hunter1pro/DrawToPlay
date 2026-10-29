using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace PowerOfFire.DrawToPlay.Tests
{
    /// <summary>A step: counts its entries, writes its name on the board it runs on, and finishes when
    /// the board holds its done key.</summary>
    internal sealed class EpisodeStubStep : StateTreeTaskAsset
    {
        internal static readonly Dictionary<string, int> entered = new Dictionary<string, int>();

        public string step = "";

        public override void OnEnter(StateTreeContext context)
        {
            entered.TryGetValue(step, out int count);
            entered[step] = count + 1;
            context.blackboard["wrote." + step] = 1f;
        }

        public override StateTreeStatus OnTick(StateTreeContext context, float deltaTime)
            => context.blackboard.ContainsKey("done." + step) ? StateTreeStatus.Success : StateTreeStatus.Running;
    }

    /// <summary>A level group's data. Entering it would be a defect: it counts if it ever is.</summary>
    internal sealed class EpisodeStubLevel : StateTreeTaskAsset, IStateTreeLevel
    {
        internal static int ran;

        public string level = "";

        public string levelName => level;

        public override void OnEnter(StateTreeContext context) => ran++;
    }

    /// <summary>
    /// The episode scope: a tree above the levels whose level nodes run only inside the level that is
    /// up, on that level's board, and keep where they stood across travel and through a save. Under it
    /// the executor's three new rules — a group's data never runs, a resumed finished state is not run
    /// again, and a run does not leave its boundary.
    /// </summary>
    [TestFixture]
    public sealed class StateTreeEpisodeTests
    {
        private readonly List<Object> m_Junk = new List<Object>();

        [SetUp]
        public void SetUp()
        {
            EpisodeStubStep.entered.Clear();
            EpisodeStubLevel.ran = 0;
        }

        [TearDown]
        public void TearDown()
        {
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
                if (m_Junk[i] != null)
                    Object.DestroyImmediate(m_Junk[i]);
            }
            m_Junk.Clear();
        }

        // ---- the tree -------------------------------------------------------------------------

        private T Junk<T>(T thing) where T : Object
        {
            m_Junk.Add(thing);
            return thing;
        }

        private StateTreeNodeAsset Node(string id, StateTreeNodeAsset parent = null)
        {
            var node = Junk(ScriptableObject.CreateInstance<StateTreeNodeAsset>());
            node.nodeId = id;
            node.name = id;
            parent?.children.Add(node);
            return node;
        }

        private StateTreeNodeAsset Level(string id, string level, StateTreeNodeAsset parent)
        {
            StateTreeNodeAsset node = Node(id, parent);
            var context = Junk(ScriptableObject.CreateInstance<EpisodeStubLevel>());
            context.level = level;
            context.blocking = false;
            node.tasks.Add(context);
            node.completionFlow = StateTreeCompletionFlow.Hold;
            return node;
        }

        private StateTreeNodeAsset Step(string id, StateTreeNodeAsset parent)
        {
            StateTreeNodeAsset node = Node(id, parent);
            node.roleKind = "walk";
            var step = Junk(ScriptableObject.CreateInstance<EpisodeStubStep>());
            step.step = id;
            node.tasks.Add(step);
            return node;
        }

        /// <summary>root → hall (s1, s2) · lobby (l1).</summary>
        private StateTreeAsset Episode()
        {
            var tree = Junk(ScriptableObject.CreateInstance<StateTreeAsset>());
            tree.root = Node("root");
            StateTreeNodeAsset hall = Level("hall", "Hall", tree.root);
            Step("s1", hall);
            Step("s2", hall);
            StateTreeNodeAsset lobby = Level("lobby", "Lobby", tree.root);
            Step("l1", lobby);
            return tree;
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

        /// <summary>A level's scene going: its host unregisters, which is what tells the episode.</summary>
        private static void Unload(StateTreeContextHost level)
        {
            level.Unregister();
            Object.DestroyImmediate(level.gameObject);
        }

        private static int Entered(string step) => EpisodeStubStep.entered.TryGetValue(step, out int n) ? n : 0;

        // ---- the executor -----------------------------------------------------------------------

        [Test]
        public void AGroupsData_NeverRuns_AndTheGroupRunsItsChildrenInOrder()
        {
            StateTreeAsset tree = Episode();
            var executor = new StateTreeExecutor { data = tree, context = new StateTreeContext() };
            executor.StartTree("hall", false);
            Assert.AreEqual("s1", executor.activeNodeId, "a group holding only its data is entered through its first child");
            executor.context.blackboard["done.s1"] = 1f;
            executor.TickTree(0.1f);
            executor.TickTree(0.1f);
            Assert.AreEqual("s2", executor.activeNodeId, "then the next");
            Assert.AreEqual(0, EpisodeStubLevel.ran, "the level's data never ran");
            executor.StopTree();
        }

        [Test]
        public void AResumedFinishedState_IsNotRunAgain_AndGoesOnFromThere()
        {
            StateTreeAsset tree = Episode();
            var executor = new StateTreeExecutor { data = tree, context = new StateTreeContext() };
            executor.StartTree("s1", true);
            Assert.AreEqual("s1", executor.activeNodeId);
            Assert.IsTrue(executor.activeNodeComplete);
            Assert.AreEqual(0, Entered("s1"), "finished when it was left: not entered again");
            executor.TickTree(0.1f);
            Assert.AreEqual("s2", executor.activeNodeId);
            Assert.AreEqual(1, Entered("s2"));
            executor.StopTree();
        }

        [Test]
        public void ARun_DoesNotLeaveItsBoundary_ItStopsAndSaysWhere()
        {
            StateTreeAsset tree = Episode();
            tree.root.children[0].children[1].transitions.Add(new StateTreeTransition { targetNodeId = "l1" });
            var executor = new StateTreeExecutor { data = tree, context = new StateTreeContext(), boundaryNodeId = "hall" };
            executor.StartTree("s2", false);
            executor.context.blackboard["done.s2"] = 1f;
            executor.TickTree(0.1f);
            Assert.IsFalse(executor.isRunning, "the lobby's step is not run in the hall");
            Assert.AreEqual("l1", executor.leftFor);
            Assert.AreEqual(0, Entered("l1"));
        }

        /// <summary>hall → zone (ask → yes (y1) · no) · next (n1): a question whose branches end their parent.</summary>
        private StateTreeAsset Question(out StateTreeNodeAsset ask)
        {
            var tree = Junk(ScriptableObject.CreateInstance<StateTreeAsset>());
            tree.root = Node("root");
            StateTreeNodeAsset hall = Level("hall", "Hall", tree.root);
            StateTreeNodeAsset zone = Node("zone", hall);
            ask = Step("ask", zone);
            StateTreeNodeAsset yes = Node("yes", ask);
            yes.completionFlow = StateTreeCompletionFlow.EndsParent;
            Step("y1", yes);
            Node("no", ask).completionFlow = StateTreeCompletionFlow.EndsParent;
            var chose = Junk(ScriptableObject.CreateInstance<BlackboardCompareCondition>());
            chose.key = new StateTreeKeyField("answer");
            chose.op = BlackboardCompareCondition.Operator.Equal;
            chose.value = 0f;
            ask.transitions.Add(new StateTreeTransition { targetNodeId = "yes", condition = chose });
            ask.transitions.Add(new StateTreeTransition { targetNodeId = "no" });
            Step("n1", Node("next", hall));
            return tree;
        }

        [Test]
        public void ABranchThatEndsItsParent_GoesOnAfterTheQuestion_NotIntoTheNextBranch()
        {
            StateTreeAsset tree = Question(out _);
            var executor = new StateTreeExecutor { data = tree, context = new StateTreeContext(), boundaryNodeId = "hall" };
            executor.context.blackboard["answer"] = 0f;
            executor.StartTree("ask", false);
            executor.context.blackboard["done.ask"] = 1f;
            executor.TickTree(0.1f);
            Assert.AreEqual("y1", executor.activeNodeId, "the answer's branch");
            executor.context.blackboard["done.y1"] = 1f;
            for (int i = 0; i < 4 && executor.activeNodeId != "n1"; i++)
                executor.TickTree(0.1f);
            Assert.AreEqual("n1", executor.activeNodeId, "after the branch: the step after the question's zone");
            Assert.AreEqual(1, Entered("y1"), "the question's ways out are not asked again");
            executor.StopTree();
        }

        [Test]
        public void AnEmptyBranchThatEndsItsParent_GoesOnAtOnce()
        {
            StateTreeAsset tree = Question(out _);
            var executor = new StateTreeExecutor { data = tree, context = new StateTreeContext(), boundaryNodeId = "hall" };
            executor.context.blackboard["answer"] = 1f;
            executor.StartTree("ask", false);
            executor.context.blackboard["done.ask"] = 1f;
            for (int i = 0; i < 6 && executor.activeNodeId != "n1"; i++)
                executor.TickTree(0.1f);
            Assert.AreEqual("n1", executor.activeNodeId, "nothing on this answer: the story goes on");
            Assert.AreEqual(0, Entered("y1"));
            executor.StopTree();
        }

        [Test]
        public void ABranchThatEndsItsParent_StopsUnderAParentThatRests()
        {
            StateTreeAsset tree = Question(out StateTreeNodeAsset ask);
            ask.completionFlow = StateTreeCompletionFlow.Hold;
            var executor = new StateTreeExecutor { data = tree, context = new StateTreeContext(), boundaryNodeId = "hall" };
            executor.context.blackboard["answer"] = 1f;
            executor.StartTree("ask", false);
            executor.context.blackboard["done.ask"] = 1f;
            for (int i = 0; i < 6; i++)
                executor.TickTree(0.1f);
            Assert.AreEqual("no", executor.activeNodeId, "the question rests: so does its branch");
            executor.StopTree();
        }

        [Test]
        public void ASchemaKind_SaysWhereItsNodeGoesWhenItsRunEnds()
        {
            Assert.AreEqual(StateTreeCompletionFlow.NextSibling, PowerOfFire.DrawToPlay.Editor.StateTreeSchemaRules.FlowOf(new PowerOfFire.DrawToPlay.Editor.StateTreeSchemaKind()));
            Assert.AreEqual(StateTreeCompletionFlow.Hold, PowerOfFire.DrawToPlay.Editor.StateTreeSchemaRules.FlowOf(new PowerOfFire.DrawToPlay.Editor.StateTreeSchemaKind { rests = true, endsParent = true }),
                "resting wins");
            Assert.AreEqual(StateTreeCompletionFlow.EndsParent, PowerOfFire.DrawToPlay.Editor.StateTreeSchemaRules.FlowOf(new PowerOfFire.DrawToPlay.Editor.StateTreeSchemaKind { endsParent = true }));
        }

        // ---- the episode scope ------------------------------------------------------------------

        [Test]
        public void TheEpisode_RunsALevelsNode_OnThatLevelsBoard_AndSaysItsStep()
        {
            StateTreeContextHost episode = Host("Episode", StateTreeContextKind.Episode, Episode());
            StateTreeContextHost hall = Host("Hall", StateTreeContextKind.Level);
            Assert.IsTrue(episode.JoinEpisode("Hall", hall));
            Assert.AreEqual("Hall", episode.episodeLevel);
            Assert.AreEqual("s1", episode.activeNodeId);
            Assert.IsTrue(hall.Context.blackboard.ContainsKey("wrote.s1"), "a step writes the level's board");
            Assert.IsFalse(episode.Context.blackboard.ContainsKey("wrote.s1"), "never the episode's own");
            Assert.AreEqual("walk", hall.Context.blackboard[StateTreeContextHost.EpisodeStepKey], "the step is said, by its kind");

            hall.Context.blackboard["done.s1"] = 1f;
            hall.Context.blackboard["done.s2"] = 1f;
            for (int tick = 0; tick < 4; tick++)
                episode.TickTree(0.1f);
            Assert.AreEqual("s2", episode.activeNodeId, "the level rests at its last step");
            Assert.IsTrue(episode.CursorOf("Hall").complete);
            Assert.IsFalse(hall.Context.blackboard.ContainsKey(StateTreeContextHost.EpisodeStepKey), "nothing runs: nothing said");
            Assert.AreEqual(1, Entered("s2"));
        }

        [Test]
        public void ALevelTheEpisodeHasNoNodeFor_JoinsNothing()
        {
            StateTreeContextHost episode = Host("Episode", StateTreeContextKind.Episode, Episode());
            StateTreeContextHost outside = Host("Outside", StateTreeContextKind.Level);
            Assert.IsFalse(episode.JoinEpisode("Outside", outside));
            Assert.IsFalse(episode.isRunning);
            Assert.AreEqual("", episode.episodeLevel);
        }

        [Test]
        public void HallLobbyHall_EachLevelResumesWhereItStood_NothingRunTwice()
        {
            StateTreeContextHost episode = Host("Episode", StateTreeContextKind.Episode, Episode());

            StateTreeContextHost hall = Host("Hall", StateTreeContextKind.Level);
            episode.JoinEpisode("Hall", hall);
            hall.Context.blackboard["done.s1"] = 1f;
            episode.TickTree(0.1f);
            episode.TickTree(0.1f);
            Assert.AreEqual("s2", episode.activeNodeId);

            Unload(hall);
            Assert.AreEqual("", episode.episodeLevel, "the hall's going is told; nothing runs between levels");
            Assert.IsFalse(episode.isRunning);
            Assert.AreEqual("s2", episode.CursorOf("Hall").node);
            Assert.IsFalse(episode.CursorOf("Hall").complete, "left while it ran");

            StateTreeContextHost lobby = Host("Lobby", StateTreeContextKind.Level);
            episode.JoinEpisode("Lobby", lobby);
            Assert.AreEqual("l1", episode.activeNodeId, "the lobby's own node, from its start");
            Assert.AreEqual(1, Entered("s2"), "the hall's step is not run in the lobby");
            Unload(lobby);

            StateTreeContextHost again = Host("Hall again", StateTreeContextKind.Level);
            episode.JoinEpisode("Hall", again);
            Assert.AreEqual("s2", episode.activeNodeId, "back in the hall: where it stood");
            Assert.AreEqual(1, Entered("s1"), "the step done before the travel is not run again");
            Assert.AreEqual(2, Entered("s2"), "the step it was in is entered again — a quit mid-step plays it again");
            Assert.AreEqual("l1", episode.CursorOf("Lobby").node, "and the lobby's place is kept for its next visit");
        }

        [Test]
        public void TheCursors_TravelThroughASave_IntoANewSession()
        {
            StateTreeContextHost episode = Host("Episode", StateTreeContextKind.Episode, Episode());
            StateTreeContextHost hall = Host("Hall", StateTreeContextKind.Level);
            episode.JoinEpisode("Hall", hall);
            hall.Context.blackboard["done.s1"] = 1f;
            episode.TickTree(0.1f);
            episode.TickTree(0.1f);
            string saved = JsonUtility.ToJson(episode.CursorOf("Hall"));
            Unload(hall);
            Unload(episode);

            StateTreeContextHost relaunched = Host("Episode", StateTreeContextKind.Episode, Episode());
            relaunched.RestoreCursor("Hall", JsonUtility.FromJson<StateTreeEpisodeCursor>(saved));
            StateTreeContextHost hallAgain = Host("Hall", StateTreeContextKind.Level);
            relaunched.JoinEpisode("Hall", hallAgain);
            Assert.AreEqual("s2", relaunched.activeNodeId);
            Assert.AreEqual(1, Entered("s1"));
        }

        [Test]
        public void ACursorFromAnotherLevel_IsNotTrusted_TheLevelStartsAtItsOwnNode()
        {
            StateTreeContextHost episode = Host("Episode", StateTreeContextKind.Episode, Episode());
            episode.RestoreCursor("Lobby", new StateTreeEpisodeCursor { node = "s2" });
            StateTreeContextHost lobby = Host("Lobby", StateTreeContextKind.Level);
            episode.JoinEpisode("Lobby", lobby);
            Assert.AreEqual("l1", episode.activeNodeId);
        }

        [Test]
        public void ARunThatHeadsForAnotherLevel_WaitsForThatLevel()
        {
            StateTreeAsset tree = Episode();
            tree.root.children[0].children[1].transitions.Add(new StateTreeTransition { targetNodeId = "l1" });
            StateTreeContextHost episode = Host("Episode", StateTreeContextKind.Episode, tree);
            StateTreeContextHost hall = Host("Hall", StateTreeContextKind.Level);
            episode.RestoreCursor("Hall", new StateTreeEpisodeCursor { node = "s2" });
            episode.JoinEpisode("Hall", hall);
            hall.Context.blackboard["done.s2"] = 1f;
            episode.TickTree(0.1f);
            Assert.IsFalse(episode.isRunning);
            Assert.AreEqual("hall", episode.CursorOf("Hall").node, "the hall's part is over");
            Assert.IsTrue(episode.CursorOf("Hall").complete);
            Assert.AreEqual("l1", episode.CursorOf("Lobby").node, "the lobby's part starts where the hall sent it");
            Assert.AreEqual(0, Entered("l1"), "not in the hall");
        }

        [Test]
        public void AnEpisodeHost_NeverRunsItsTreeOnItsOwnBoard()
        {
            StateTreeContextHost episode = Host("Episode", StateTreeContextKind.Episode, Episode());
            episode.StartTree();
            Assert.IsFalse(episode.isRunning);
        }

        [Test]
        public void JoinEpisodeTask_JoinsTheLevelItNames()
        {
            StateTreeContextHost episode = Host("Episode", StateTreeContextKind.Episode, Episode());
            StateTreeContextHost hall = Host("Hall", StateTreeContextKind.Level);
            hall.gameObject.SetActive(true);
            episode.gameObject.SetActive(true);
            var join = Junk(ScriptableObject.CreateInstance<JoinEpisodeTask>());
            join.level = "Hall";
            StateTreeServiceInjector.Inject(join, hall.gameObject);
            Assert.AreEqual(StateTreeStatus.Success, join.OnTick(hall.Context, 0f));
            Assert.AreEqual("Hall", episode.episodeLevel);
            Assert.AreEqual("s1", episode.activeNodeId);
        }
    }
}
