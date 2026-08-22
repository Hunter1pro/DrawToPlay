using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace PowerOfFire.DrawToPlay.Tests
{
    /// <summary>
    /// Task stub for the graph interpreter whose tick counter is NOT reset in OnEnter. That is the
    /// whole point of it: <see cref="StubRecordingTask"/> zeroes its counter on entry, which HIDES
    /// two runners sharing one task instance (the shared counter is reset by the second runner's
    /// entry and the logs come out identical either way). A counter that only ever accumulates makes
    /// the sharing visible in the very first tick of the second runner.
    ///
    /// It lives in this file rather than beside the other stubs because agent-vm owns exactly two
    /// files this round.
    /// </summary>
    internal sealed class GraphCountingTask : StateTreeTaskAsset
    {
        public string taskId = "count";

        /// <summary>Total ticks (across activations) after which this task Succeeds.</summary>
        public int finishAfter = 1;

        /// <summary>Private, therefore not serialized, therefore fresh in every Instantiate copy —
        /// and shared by everyone who ticks the SAME instance.</summary>
        private int m_TotalTicks;

        public override StateTreeStatus OnTick(StateTreeContext context, float deltaTime)
        {
            m_TotalTicks++;
            StateTreeTestLog.Record(context, taskId + ":" + m_TotalTicks);
            return m_TotalTicks >= finishAfter ? StateTreeStatus.Success : StateTreeStatus.Running;
        }
    }

    /// <summary>
    /// EditMode coverage of <see cref="GraphTaskAsset"/> — the Blueprint-style logic graph run as
    /// one state-tree task (M7e).
    ///
    /// Programs are built as RAW NODE LISTS here: no Graph Toolkit, no importer, no AssetDatabase.
    /// The baked program is the contract between the graph editor and the interpreter, so testing
    /// the interpreter against hand-written programs is what keeps a change on either side from
    /// silently redefining it — and it means these tests run with no editor window open.
    ///
    /// The task is driven directly (OnEnter / OnTick / OnExit) rather than through a
    /// <see cref="StateTreeRunner"/>: the status the graph RETURNS is the assertion in almost every
    /// case, and a surrounding tree would only hide it behind a transition. Cancelled is delivered
    /// the same way the runner delivers it — OnExit(Cancelled) while the task is mid-flight.
    /// </summary>
    [TestFixture]
    public sealed class GraphTaskTests
    {
        private readonly List<GameObject> m_Objects = new List<GameObject>();
        private readonly List<ScriptableObject> m_Assets = new List<ScriptableObject>();

        [SetUp]
        public void SetUp()
        {
            m_Objects.Clear();
            m_Assets.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            // Graphs first: a graph releases the private copies it made of its sub-assets when it is
            // destroyed, and destroying the originals first would leave that release nothing to do
            // but trip over already-dead objects.
            for (int i = 0; i < m_Assets.Count; i++)
            {
                if (m_Assets[i] is GraphTaskAsset && m_Assets[i] != null)
                    Object.DestroyImmediate(m_Assets[i]);
            }
            for (int i = 0; i < m_Assets.Count; i++)
            {
                if (m_Assets[i] != null)
                    Object.DestroyImmediate(m_Assets[i]);
            }
            for (int i = 0; i < m_Objects.Count; i++)
            {
                if (m_Objects[i] != null)
                    Object.DestroyImmediate(m_Objects[i]);
            }
            m_Assets.Clear();
            m_Objects.Clear();
        }

        // ------------------------------------------------------------------ required: branching

        /// <summary>Branch routes exec[0] on true and exec[1] on false, and the bool comes from a
        /// pulled data node (here a condition), not from a field.</summary>
        [Test]
        public void Branch_RoutesTrueAndFalseOnSeparateExecPins()
        {
            GraphTaskAsset graph = MakeGraph(
                new GraphTaskNode
                {
                    kind = GraphTaskNodeKind.Branch, data = new[] { 3 }, exec = new[] { 1, 2 }
                },
                SetFloat("route", 1f, -1),
                SetFloat("route", 2f, -1),
                new GraphTaskNode
                {
                    kind = GraphTaskNodeKind.EvaluateCondition, condition = MakeFlag("gate")
                });
            graph.tickEntry = 0;

            StateTreeContext context = MakeContext();
            context.blackboard["gate"] = true;

            graph.OnEnter(context);
            Assert.AreEqual(StateTreeStatus.Running, graph.OnTick(context, 0.1f),
                "the true branch ends without a Return node, so the task stays alive");
            Assert.AreEqual(1f, Float(context, "route"));
            graph.OnExit(context, StateTreeStatus.Cancelled);

            context.blackboard["gate"] = false;
            graph.OnEnter(context);
            graph.OnTick(context, 0.1f);
            Assert.AreEqual(2f, Float(context, "route"), "a false pin must take exec[1]");
            graph.OnExit(context, StateTreeStatus.Cancelled);
        }

        // ------------------------------------------------------------------ required: blackboard

        /// <summary>Write then read, for both value kinds, THROUGH the graph — a set node followed
        /// by a get node pulled by a second set node. Data nodes are evaluated fresh on every pull
        /// precisely so the read sees the write that happened earlier in the same chain.</summary>
        [Test]
        public void Blackboard_SetThenGetRoundTripsFloatsAndStrings()
        {
            GraphTaskAsset graph = MakeGraph(
                SetFloat("hp", 7f, 1),
                new GraphTaskNode
                {
                    kind = GraphTaskNodeKind.SetBlackboardFloat, stringValue = "hpCopy",
                    data = new[] { 5 }, exec = new[] { 2 }
                },
                new GraphTaskNode
                {
                    kind = GraphTaskNodeKind.SetBlackboardString, stringValue = "mood",
                    stringValue2 = "angry", exec = new[] { 3 }
                },
                new GraphTaskNode
                {
                    kind = GraphTaskNodeKind.SetBlackboardString, stringValue = "moodCopy",
                    data = new[] { 6 }, exec = new[] { 4 }
                },
                new GraphTaskNode { kind = GraphTaskNodeKind.ReturnSuccess },
                new GraphTaskNode
                {
                    kind = GraphTaskNodeKind.GetBlackboardFloat, stringValue = "hp"
                },
                new GraphTaskNode
                {
                    kind = GraphTaskNodeKind.GetBlackboardString, stringValue = "mood"
                });
            graph.tickEntry = 0;

            StateTreeContext context = MakeContext();
            graph.OnEnter(context);

            Assert.AreEqual(StateTreeStatus.Success, graph.OnTick(context, 0.1f));
            Assert.AreEqual(7f, Float(context, "hpCopy"));
            Assert.AreEqual("angry", context.blackboard["moodCopy"]);
            graph.OnExit(context, StateTreeStatus.Success);
        }

        // ------------------------------------------------------------------ required: fall off end

        /// <summary>THE Blueprint rule: a chain that runs out of nodes without hitting a Return
        /// leaves the task Running, so "do a bit of work every tick" needs no keep-alive node.</summary>
        [Test]
        public void ChainFallingOffTheEnd_LeavesTheTaskRunning()
        {
            GraphTaskAsset graph = MakeGraph(SetFloat("x", 5f, -1));
            graph.tickEntry = 0;

            StateTreeContext context = MakeContext();
            graph.OnEnter(context);

            Assert.AreEqual(StateTreeStatus.Running, graph.OnTick(context, 0.1f));
            Assert.AreEqual(5f, Float(context, "x"), "the node still ran; only the chain ended");
            Assert.AreEqual(StateTreeStatus.Running, graph.OnTick(context, 0.1f),
                "and it runs again next tick, from tickEntry");
            graph.OnExit(context, StateTreeStatus.Cancelled);
        }

        /// <summary>An unset tick entry is the empty program: Success at once, never Running.</summary>
        [Test]
        public void TickEntryUnset_SucceedsImmediately()
        {
            GraphTaskAsset graph = MakeGraph();
            StateTreeContext context = MakeContext();

            graph.OnEnter(context);
            Assert.AreEqual(StateTreeStatus.Success, graph.OnTick(context, 0.1f));
            graph.OnExit(context, StateTreeStatus.Success);
        }

        // ------------------------------------------------------------------ required: latent Wait

        /// <summary>A Wait suspends the chain and the next tick RESUMES AT THE WAIT — it does not
        /// re-walk from tickEntry. The DoTask upstream of the wait is what proves it: it must run
        /// exactly once across the three ticks, not once per tick.</summary>
        [Test]
        public void Wait_ResumesAtTheNodeInsteadOfRestartingTheChain()
        {
            GraphTaskAsset graph = MakeGraph(
                new GraphTaskNode
                {
                    kind = GraphTaskNodeKind.DoTask, task = MakeTask("pre", 1),
                    exec = new[] { 1, 1 }
                },
                new GraphTaskNode
                {
                    kind = GraphTaskNodeKind.Wait, floatValue = 0.25f, exec = new[] { 2 }
                },
                SetFloat("done", 1f, -1));
            graph.tickEntry = 0;

            StateTreeContext context = MakeContext();
            graph.OnEnter(context);

            string[] afterFirstTick = { "pre:enter", "pre:tick1", "pre:exit:Success" };

            Assert.AreEqual(StateTreeStatus.Running, graph.OnTick(context, 0.1f));
            CollectionAssert.AreEqual(afterFirstTick, Log(context));

            Assert.AreEqual(StateTreeStatus.Running, graph.OnTick(context, 0.1f));
            CollectionAssert.AreEqual(afterFirstTick, Log(context),
                "the second tick must resume inside the Wait, not re-run the DoTask above it");
            Assert.IsFalse(context.blackboard.ContainsKey("done"), "0.2s < 0.25s");

            Assert.AreEqual(StateTreeStatus.Running, graph.OnTick(context, 0.1f));
            Assert.AreEqual(1f, Float(context, "done"), "0.3s >= 0.25s, so the chain continues");
            CollectionAssert.AreEqual(afterFirstTick, Log(context));
            graph.OnExit(context, StateTreeStatus.Cancelled);
        }

        // ------------------------------------------------------------------ M38.4: the beat

        /// <summary>The program publishes the instruction it is AT, and a run is listed while it
        /// runs: what a canvas lights, the way a state tree's active state is lit. The beat is the
        /// suspended Wait while the chain waits, and the registry is empty again after OnExit.
        /// Every run is a copy that remembers the authored program, so a copy of a copy still
        /// points the canvas at the root.</summary>
        [Test]
        public void TheBeat_IsTheSuspendedNode_AndTheRunIsListedWhileItRuns()
        {
            GraphTaskAsset authored = MakeGraph(
                new GraphTaskNode
                {
                    kind = GraphTaskNodeKind.DoTask, task = MakeTask("pre", 1),
                    exec = new[] { 1, 1 }
                },
                new GraphTaskNode
                {
                    kind = GraphTaskNodeKind.Wait, floatValue = 0.25f, exec = new[] { 2 }
                },
                SetFloat("done", 1f, -1));
            authored.tickEntry = 0;

            GraphTaskAsset run = GraphTaskAsset.Copy(authored);
            m_Assets.Add(run);
            GraphTaskAsset again = GraphTaskAsset.Copy(run);
            m_Assets.Add(again);
            Assert.AreSame(authored, run.source, "a run remembers the program it was copied from");
            Assert.AreSame(authored, again.source, "a copy of a copy still names the authored one");
            Assert.IsNull(authored.source, "the authored program is nobody's copy");

            var running = new List<GraphTaskAsset>();
            GraphTaskAsset.CollectRunning(running);
            CollectionAssert.DoesNotContain(running, run, "not listed before OnEnter");
            Assert.AreEqual(-1, run.activeNode, "no beat before the first instruction");

            StateTreeContext context = MakeContext();
            run.OnEnter(context);
            GraphTaskAsset.CollectRunning(running);
            CollectionAssert.Contains(running, run, "listed between OnEnter and OnExit");

            run.OnTick(context, 0.1f);
            Assert.AreEqual(1, run.activeNode, "the beat is the Wait the chain is suspended in");
            run.OnTick(context, 0.1f);
            Assert.AreEqual(1, run.activeNode, "still the Wait");
            run.OnTick(context, 0.1f);
            Assert.AreEqual(2, run.activeNode, "the chain moved on to the Set after the wait");

            run.OnExit(context, StateTreeStatus.Cancelled);
            GraphTaskAsset.CollectRunning(running);
            CollectionAssert.DoesNotContain(running, run, "unlisted after OnExit");
        }

        // ------------------------------------------------------------------ required: latent DoTask

        /// <summary>A DoTask that returns Running suspends the graph; the next tick ticks the SAME
        /// task again without a second OnEnter, and its terminal status picks the exec pin.</summary>
        [TestCase(StateTreeStatus.Success, 0, 1f)]
        [TestCase(StateTreeStatus.Failure, 1, 2f)]
        public void DoTask_ResumesWithoutReEnteringThenRoutesOnItsStatus(
            StateTreeStatus finishStatus, int expectedPin, float expectedRoute)
        {
            GraphTaskAsset graph = MakeGraph(
                new GraphTaskNode
                {
                    kind = GraphTaskNodeKind.DoTask, task = MakeTask("t", 2, finishStatus),
                    exec = new[] { 1, 2 }
                },
                SetFloat("route", 1f, 3),
                SetFloat("route", 2f, 3),
                new GraphTaskNode { kind = GraphTaskNodeKind.ReturnSuccess });
            graph.tickEntry = 0;

            StateTreeContext context = MakeContext();
            graph.OnEnter(context);

            Assert.AreEqual(StateTreeStatus.Running, graph.OnTick(context, 0.1f));
            CollectionAssert.AreEqual(new[] { "t:enter", "t:tick1" }, Log(context));

            Assert.AreEqual(StateTreeStatus.Success, graph.OnTick(context, 0.1f),
                "the graph reaches its ReturnSuccess once the child task is done");
            CollectionAssert.AreEqual(
                new[] { "t:enter", "t:tick1", "t:tick2", "t:exit:" + finishStatus }, Log(context),
                "exactly one OnEnter: the resume must not re-enter the child task");
            Assert.AreEqual(expectedRoute, Float(context, "route"),
                "the child's status selects exec[" + expectedPin + "]");
            graph.OnExit(context, StateTreeStatus.Success);
        }

        // ------------------------------------------------------------------ required: Cancelled

        /// <summary>THE composition test: an interrupt in the owning tree arrives as
        /// OnExit(Cancelled) on the graph task, and must reach the library task that is mid-flight
        /// inside it — otherwise every pre-empted graph leaves nav goals, timers and spawned VFX
        /// behind. The exit chain runs FIRST (its cue is logged before the child's exit), which is
        /// what lets an exit chain react to a cancel.</summary>
        [Test]
        public void Cancelled_ReachesTheLatentDoTaskAfterTheExitChainRan()
        {
            GraphTaskAsset graph = MakeGraph(
                new GraphTaskNode
                {
                    kind = GraphTaskNodeKind.DoTask, task = MakeTask("t"), exec = new[] { 1, -1 }
                },
                new GraphTaskNode { kind = GraphTaskNodeKind.ReturnSuccess },
                new GraphTaskNode
                {
                    kind = GraphTaskNodeKind.SetBlackboardFloat, stringValue = "exitCode",
                    data = new[] { 4 }, exec = new[] { 3 }
                },
                new GraphTaskNode
                {
                    kind = GraphTaskNodeKind.FireCue, stringValue = "left", exec = new[] { -1 }
                },
                new GraphTaskNode { kind = GraphTaskNodeKind.ExitStatus });
            graph.tickEntry = 0;
            graph.exitEntry = 2;

            StateTreeContext context = MakeContext();
            RecordCues(context);
            graph.OnEnter(context);

            Assert.AreEqual(StateTreeStatus.Running, graph.OnTick(context, 0.1f));
            CollectionAssert.AreEqual(new[] { "t:enter", "t:tick1" }, Log(context));

            graph.OnExit(context, StateTreeStatus.Cancelled);

            CollectionAssert.AreEqual(
                new[] { "t:enter", "t:tick1", "cue:left", "t:exit:Cancelled" }, Log(context),
                "exit chain first, then the latent child is cancelled");
            Assert.AreEqual(2f, Float(context, "exitCode"));
        }

        // ------------------------------------------------------------------ required: ExitStatus

        /// <summary>ExitStatus is the exit chain's one piece of context: 0 Success, 1 Failure,
        /// 2 Cancelled, so one graph can tear down differently when it was pre-empted.</summary>
        [TestCase(StateTreeStatus.Success, 0f)]
        [TestCase(StateTreeStatus.Failure, 1f)]
        [TestCase(StateTreeStatus.Cancelled, 2f)]
        public void ExitStatus_IsVisibleInTheExitChain(StateTreeStatus status, float expected)
        {
            GraphTaskAsset graph = MakeGraph(
                new GraphTaskNode
                {
                    kind = GraphTaskNodeKind.SetBlackboardFloat, stringValue = "code",
                    data = new[] { 1 }, exec = new[] { -1 }
                },
                new GraphTaskNode { kind = GraphTaskNodeKind.ExitStatus });
            graph.exitEntry = 0;

            StateTreeContext context = MakeContext();
            graph.OnEnter(context);
            graph.OnExit(context, status);

            Assert.AreEqual(expected, Float(context, "code"));
        }

        // ------------------------------------------------------------------ required: guards

        /// <summary>An exec loop with no latent node in it would hang the editor. The step budget
        /// ends the tick with Failure, and says so ONCE however many ticks the broken graph
        /// gets.</summary>
        [Test]
        public void ExecCycle_TripsTheStepBudgetAndFailsWithASingleLog()
        {
            GraphTaskAsset graph = MakeGraph(SetFloat("spin", 1f, 0));
            graph.tickEntry = 0;

            StateTreeContext context = MakeContext();
            graph.OnEnter(context);

            LogAssert.Expect(LogType.Error, new Regex("exceeded " + GraphTaskAsset.stepBudget));
            Assert.AreEqual(StateTreeStatus.Failure, graph.OnTick(context, 0.1f));

            Assert.AreEqual(StateTreeStatus.Failure, graph.OnTick(context, 0.1f),
                "still failing on the next tick");
            LogAssert.NoUnexpectedReceived();

            graph.OnExit(context, StateTreeStatus.Failure);
        }

        /// <summary>A data cycle terminates at the depth guard with the unwired default instead of a
        /// stack overflow, and also logs once.</summary>
        [Test]
        public void DataCycle_TripsTheDepthGuardAndUsesTheUnwiredDefault()
        {
            GraphTaskAsset graph = MakeGraph(
                new GraphTaskNode
                {
                    kind = GraphTaskNodeKind.SetBlackboardFloat, stringValue = "r",
                    data = new[] { 1 }, exec = new[] { -1 }
                },
                new GraphTaskNode
                {
                    // Left-hand side pulls itself.
                    kind = GraphTaskNodeKind.CompareFloat, stringValue = ">", data = new[] { 1, 2 }
                },
                new GraphTaskNode { kind = GraphTaskNodeKind.ConstFloat, floatValue = -1f });
            graph.tickEntry = 0;

            StateTreeContext context = MakeContext();
            graph.OnEnter(context);

            LogAssert.Expect(LogType.Error, new Regex("data pull nested deeper"));
            Assert.AreEqual(StateTreeStatus.Running, graph.OnTick(context, 0.1f));
            Assert.AreEqual(1f, Float(context, "r"),
                "the guard returns the default and the comparison completes normally above it");

            graph.OnExit(context, StateTreeStatus.Cancelled);
        }

        /// <summary>The enter and exit chains have no tick to resume into, so a latent node there is
        /// an authoring error: one report, then it passes straight through on exec[0] rather than
        /// stalling the chain.</summary>
        [Test]
        public void LatentInTheEnterChain_ReportsOnceAndPassesThrough()
        {
            GraphTaskAsset graph = MakeGraph(
                new GraphTaskNode
                {
                    kind = GraphTaskNodeKind.Wait, floatValue = 99f, exec = new[] { 1 }
                },
                SetFloat("entered", 1f, -1));
            graph.enterEntry = 0;

            StateTreeContext context = MakeContext();

            LogAssert.Expect(LogType.Error, new Regex("cannot suspend"));
            graph.OnEnter(context);
            Assert.AreEqual(1f, Float(context, "entered"), "the chain continued past the Wait");
            Assert.AreEqual(StateTreeStatus.Success, graph.OnTick(context, 0.1f),
                "the Wait must not have armed anything: tickEntry is unset, so this is Success");
            graph.OnExit(context, StateTreeStatus.Success);

            context.blackboard.Remove("entered");
            graph.OnEnter(context);
            Assert.AreEqual(1f, Float(context, "entered"));
            LogAssert.NoUnexpectedReceived();
            graph.OnExit(context, StateTreeStatus.Success);
        }

        // ------------------------------------------------------------------ data nodes

        [TestCase("<", 1f, 2f, true)]
        [TestCase("<", 2f, 1f, false)]
        [TestCase("<=", 2f, 2f, true)]
        [TestCase(">", 2f, 1f, true)]
        [TestCase(">=", 1f, 2f, false)]
        [TestCase("==", 1f, 1f, true)]
        [TestCase("==", 1f, 1.00001f, true)]
        [TestCase("==", 1f, 1.5f, false)]
        [TestCase("!=", 1f, 2f, true)]
        [TestCase("!=", 1f, 1f, false)]
        [TestCase("~", 1f, 2f, false)]
        public void CompareFloat_ImplementsEveryOperator(string op, float lhs, float rhs,
            bool expected)
        {
            GraphTaskAsset graph = MakeGraph(
                new GraphTaskNode
                {
                    kind = GraphTaskNodeKind.SetBlackboardFloat, stringValue = "r",
                    data = new[] { 1 }, exec = new[] { -1 }
                },
                new GraphTaskNode
                {
                    kind = GraphTaskNodeKind.CompareFloat, stringValue = op, data = new[] { 2, 3 }
                },
                new GraphTaskNode { kind = GraphTaskNodeKind.ConstFloat, floatValue = lhs },
                new GraphTaskNode { kind = GraphTaskNodeKind.ConstFloat, floatValue = rhs });
            graph.tickEntry = 0;

            StateTreeContext context = MakeContext();
            graph.OnEnter(context);
            graph.OnTick(context, 0.1f);

            Assert.AreEqual(expected ? 1f : 0f, Float(context, "r"));
            graph.OnExit(context, StateTreeStatus.Cancelled);
        }

        [TestCase(GraphTaskNodeKind.BoolAnd, true, true, true)]
        [TestCase(GraphTaskNodeKind.BoolAnd, true, false, false)]
        [TestCase(GraphTaskNodeKind.BoolOr, false, true, true)]
        [TestCase(GraphTaskNodeKind.BoolOr, false, false, false)]
        [TestCase(GraphTaskNodeKind.BoolNot, false, false, true)]
        [TestCase(GraphTaskNodeKind.BoolNot, true, false, false)]
        public void BoolNodes_CombineTheirPulledOperands(GraphTaskNodeKind kind, bool a, bool b,
            bool expected)
        {
            GraphTaskAsset graph = MakeGraph(
                new GraphTaskNode
                {
                    kind = GraphTaskNodeKind.SetBlackboardFloat, stringValue = "r",
                    data = new[] { 1 }, exec = new[] { -1 }
                },
                new GraphTaskNode { kind = kind, data = new[] { 2, 3 } },
                new GraphTaskNode
                {
                    kind = GraphTaskNodeKind.ConstBool, floatValue = a ? 1f : 0f
                },
                new GraphTaskNode
                {
                    kind = GraphTaskNodeKind.ConstBool, floatValue = b ? 1f : 0f
                });
            graph.tickEntry = 0;

            StateTreeContext context = MakeContext();
            graph.OnEnter(context);
            graph.OnTick(context, 0.1f);

            Assert.AreEqual(expected ? 1f : 0f, Float(context, "r"));
            graph.OnExit(context, StateTreeStatus.Cancelled);
        }

        /// <summary>Every unwired data pin falls back to the value carried by the node that owns it,
        /// so a graph with nothing plugged in still behaves like the values shown on its face. Both
        /// spellings of "unwired" are covered: a null pin array and an explicit -1.</summary>
        [Test]
        public void UnwiredDataPins_FallBackToTheOwningNodeDefaults()
        {
            GraphTaskAsset graph = MakeGraph(
                new GraphTaskNode
                {
                    // No data array at all.
                    kind = GraphTaskNodeKind.SetBlackboardFloat, stringValue = "f",
                    floatValue = 3.5f, exec = new[] { 1 }
                },
                new GraphTaskNode
                {
                    kind = GraphTaskNodeKind.SetBlackboardString, stringValue = "s",
                    stringValue2 = "idle", exec = new[] { 2 }
                },
                new GraphTaskNode
                {
                    kind = GraphTaskNodeKind.SetBlackboardFloat, stringValue = "cmp",
                    data = new[] { 3 }, exec = new[] { 4 }
                },
                new GraphTaskNode
                {
                    // Explicitly unwired: lhs reads 0, rhs falls back to floatValue.
                    kind = GraphTaskNodeKind.CompareFloat, stringValue = "==",
                    floatValue = 0f, data = new[] { -1, -1 }
                },
                new GraphTaskNode
                {
                    // Unwired duration falls back to floatValue = 0 and completes at once.
                    kind = GraphTaskNodeKind.Wait, floatValue = 0f, exec = new[] { 5 }
                },
                SetFloat("past", 1f, -1));
            graph.tickEntry = 0;

            StateTreeContext context = MakeContext();
            graph.OnEnter(context);

            Assert.AreEqual(StateTreeStatus.Running, graph.OnTick(context, 0.1f));
            Assert.AreEqual(3.5f, Float(context, "f"));
            Assert.AreEqual("idle", context.blackboard["s"]);
            Assert.AreEqual(1f, Float(context, "cmp"), "0 == 0 with both sides unwired");
            Assert.AreEqual(1f, Float(context, "past"), "a zero-second Wait must not suspend");
            graph.OnExit(context, StateTreeStatus.Cancelled);
        }

        /// <summary>FireCue reaches the same listeners as <see cref="FireCueTask"/>, carrying the
        /// owner so a presentation layer knows who fired it.</summary>
        [Test]
        public void FireCue_EmitsOnTheSharedContextWithTheOwner()
        {
            GraphTaskAsset graph = MakeGraph(
                new GraphTaskNode
                {
                    kind = GraphTaskNodeKind.FireCue, stringValue = "roar", exec = new[] { -1 }
                });
            graph.tickEntry = 0;

            StateTreeContext context = MakeContext();
            string firedName = null;
            Dictionary<string, object> firedPayload = null;
            context.cueFired += (name, payload) =>
            {
                firedName = name;
                firedPayload = payload;
            };

            graph.OnEnter(context);
            graph.OnTick(context, 0.1f);

            Assert.AreEqual("roar", firedName);
            Assert.IsNotNull(firedPayload);
            Assert.AreSame(context.owner, firedPayload["owner"]);
            graph.OnExit(context, StateTreeStatus.Cancelled);
        }

        // ------------------------------------------------------------------ isolation + nesting

        /// <summary>Two runners sharing one authored graph must not share the TASKS inside it.
        /// StateTreeAsset.DeepCopy only duplicates one level — the tasks in a state's own list — so
        /// without a private copy per graph instance the second zombie's entry would reset the first
        /// zombie's attack timer. Interleaved on purpose: the second graph's first tick is where a
        /// shared instance shows up.</summary>
        [Test]
        public void TaskInstanceState_IsNotSharedBetweenCopiesOfOneGraph()
        {
            GraphTaskAsset authored = MakeGraph(
                new GraphTaskNode
                {
                    kind = GraphTaskNodeKind.DoTask, task = MakeCounter("count", 3),
                    exec = new[] { 1, 1 }
                },
                new GraphTaskNode { kind = GraphTaskNodeKind.ReturnSuccess });
            authored.tickEntry = 0;

            GraphTaskAsset first = Track(Object.Instantiate(authored));
            GraphTaskAsset second = Track(Object.Instantiate(authored));
            StateTreeContext firstContext = MakeContext("First");
            StateTreeContext secondContext = MakeContext("Second");

            first.OnEnter(firstContext);
            Assert.AreEqual(StateTreeStatus.Running, first.OnTick(firstContext, 0.1f));
            CollectionAssert.AreEqual(new[] { "count:1" }, Log(firstContext));

            second.OnEnter(secondContext);
            Assert.AreEqual(StateTreeStatus.Running, second.OnTick(secondContext, 0.1f));
            CollectionAssert.AreEqual(new[] { "count:1" }, Log(secondContext),
                "a shared task instance would already be on tick 2 here");

            Assert.AreEqual(StateTreeStatus.Running, first.OnTick(firstContext, 0.1f));
            CollectionAssert.AreEqual(new[] { "count:1", "count:2" }, Log(firstContext));

            first.OnExit(firstContext, StateTreeStatus.Cancelled);
            second.OnExit(secondContext, StateTreeStatus.Cancelled);
        }

        /// <summary>A DoTask may hold another graph task, and it is latent all the way down: the
        /// inner graph's Wait suspends the outer graph, and the outer resumes into it. The nesting
        /// counter it pushes on the shared context must be popped again on exit.</summary>
        [Test]
        public void NestedGraphTask_SuspendsAndResumesThroughBothLevels()
        {
            GraphTaskAsset inner = MakeGraph(
                new GraphTaskNode
                {
                    kind = GraphTaskNodeKind.Wait, floatValue = 0.15f, exec = new[] { 1 }
                },
                new GraphTaskNode { kind = GraphTaskNodeKind.ReturnSuccess });
            inner.tickEntry = 0;

            GraphTaskAsset outer = MakeGraph(
                new GraphTaskNode
                {
                    kind = GraphTaskNodeKind.DoTask, task = inner, exec = new[] { 1, 2 }
                },
                SetFloat("route", 1f, -1),
                SetFloat("route", 2f, -1));
            outer.tickEntry = 0;

            StateTreeContext context = MakeContext();
            outer.OnEnter(context);

            Assert.AreEqual(StateTreeStatus.Running, outer.OnTick(context, 0.1f));
            Assert.IsFalse(context.blackboard.ContainsKey("route"), "the inner Wait is still held");

            Assert.AreEqual(StateTreeStatus.Running, outer.OnTick(context, 0.1f));
            Assert.AreEqual(1f, Float(context, "route"),
                "the inner graph returned Success, so the outer took exec[0]");

            outer.OnExit(context, StateTreeStatus.Success);
            Assert.IsFalse(context.domainContext.ContainsKey(GraphTaskAsset.depthKey),
                "the nesting marker must be popped when the graph task exits");
        }

        /// <summary>A graph whose DoTask runs the graph itself is one click away in a graph editor.
        /// It must stop at the nesting guard with a single error rather than recursing until the
        /// stack gives out, and unwind cleanly when the outermost activation exits.</summary>
        [Test]
        public void SelfReferencingGraph_StopsAtTheNestingGuard()
        {
            GraphTaskAsset graph = MakeGraph(
                new GraphTaskNode { kind = GraphTaskNodeKind.DoTask, exec = new[] { -1, -1 } });
            graph.nodes[0].task = graph;
            graph.tickEntry = 0;

            StateTreeContext context = MakeContext();
            graph.OnEnter(context);

            LogAssert.Expect(LogType.Error, new Regex("nested deeper than " + GraphTaskAsset.maxDepth));
            Assert.AreEqual(StateTreeStatus.Running, graph.OnTick(context, 0.1f),
                "the innermost level fails, every level above it falls off the end");

            graph.OnExit(context, StateTreeStatus.Cancelled);
            Assert.IsFalse(context.domainContext.ContainsKey(GraphTaskAsset.depthKey),
                "every level that pushed a depth must have popped it");
            LogAssert.NoUnexpectedReceived();
        }

        // ------------------------------------------------------------------ parameters (M7f)

        /// <summary>THE parameter rule: the graph carries the default, the state that runs it may
        /// say otherwise, and an override row only counts while it is ENABLED. Unchecking a row must
        /// fall back to the graph default rather than freeze the value that was last typed into it
        /// — otherwise re-tuning a graph would silently miss every state that had ever overridden
        /// the parameter.</summary>
        [TestCase(true, 9f, TestName = "Parameters_EnabledOverrideWins")]
        [TestCase(false, 3f, TestName = "Parameters_DisabledOverrideKeepsTheGraphDefault")]
        public void Parameters_OverrideAppliesOnlyWhenItIsEnabled(bool enabled, float expected)
        {
            GraphTaskAsset graph = MakeGraph(
                new GraphTaskNode
                {
                    kind = GraphTaskNodeKind.SetBlackboardFloat, stringValue = "usedSpeed",
                    data = new[] { 1 }, exec = new[] { -1 }
                },
                new GraphTaskNode
                {
                    kind = GraphTaskNodeKind.GetParamFloat, stringValue = "speed"
                });
            graph.tickEntry = 0;
            graph.parameters = Params(Param("speed", GraphTaskParameterKind.Float, 3f));

            StateTreeContext context = MakeContext();
            graph.ApplyOverrides(Overrides(Override("speed", enabled, 9f)));

            graph.OnEnter(context);
            graph.OnTick(context, 0.1f);

            Assert.AreEqual(expected, Float(context, "usedSpeed"));
            graph.OnExit(context, StateTreeStatus.Cancelled);
        }

        /// <summary>The other two kinds, and each through its own pull path: a String parameter read
        /// as a string, a Bool parameter read as the bool a Branch routes on. Both are exercised
        /// twice on ONE instance — defaults first, then with overrides applied — which also pins
        /// down that a second ApplyOverrides re-derives everything from the defaults instead of
        /// layering onto whatever the last call left behind.</summary>
        [Test]
        public void Parameters_StringAndBoolResolveThroughTheirOwnKinds()
        {
            GraphTaskAsset graph = MakeGraph(
                new GraphTaskNode
                {
                    kind = GraphTaskNodeKind.SetBlackboardString, stringValue = "moodOut",
                    data = new[] { 1 }, exec = new[] { 2 }
                },
                new GraphTaskNode
                {
                    kind = GraphTaskNodeKind.GetParamString, stringValue = "mood"
                },
                new GraphTaskNode
                {
                    kind = GraphTaskNodeKind.Branch, data = new[] { 5 }, exec = new[] { 3, 4 }
                },
                SetFloat("route", 1f, -1),
                SetFloat("route", 2f, -1),
                new GraphTaskNode
                {
                    kind = GraphTaskNodeKind.GetParamBool, stringValue = "angry"
                });
            graph.tickEntry = 0;
            graph.parameters = Params(
                Param("mood", GraphTaskParameterKind.String, 0f, "calm"),
                Param("angry", GraphTaskParameterKind.Bool));

            StateTreeContext context = MakeContext();

            // No ApplyOverrides at all: the defaults must still resolve, because a graph reached as
            // a DoTask child is never handed overrides.
            graph.OnEnter(context);
            graph.OnTick(context, 0.1f);
            Assert.AreEqual("calm", context.blackboard["moodOut"]);
            Assert.AreEqual(2f, Float(context, "route"), "the Bool default is false");
            graph.OnExit(context, StateTreeStatus.Cancelled);

            graph.ApplyOverrides(Overrides(
                Override("mood", true, 0f, "furious"),
                Override("angry", true, 1f)));
            graph.OnEnter(context);
            graph.OnTick(context, 0.1f);

            Assert.AreEqual("furious", context.blackboard["moodOut"]);
            Assert.AreEqual(1f, Float(context, "route"), "a Bool override routes the Branch");
            graph.OnExit(context, StateTreeStatus.Cancelled);
        }

        /// <summary>A graph gets re-authored long after the states that use it were configured, so
        /// an override bound to a parameter that has since been DELETED is normal wear, not a
        /// crash: the row is ignored, the surviving overrides still apply, and it is a WARNING said
        /// once — a state re-entered every second must not turn a stale row into a console flood.
        /// The row still carries a display name, which is how the warning can name it.</summary>
        [Test]
        public void Parameters_UnmatchedIdWarnsOnceAndLeavesTheRestApplied()
        {
            GraphTaskAsset graph = MakeGraph(
                new GraphTaskNode
                {
                    kind = GraphTaskNodeKind.SetBlackboardFloat, stringValue = "usedSpeed",
                    data = new[] { 1 }, exec = new[] { -1 }
                },
                new GraphTaskNode
                {
                    kind = GraphTaskNodeKind.GetParamFloat, stringValue = "speed"
                });
            graph.tickEntry = 0;
            graph.parameters = Params(Param("speed", GraphTaskParameterKind.Float, 3f));

            List<GraphTaskParameterOverride> overrides = Overrides(
                Override("spede", true, 99f),
                Override("speed", true, 7f));

            LogAssert.Expect(LogType.Warning, new Regex("'spede'"));
            graph.ApplyOverrides(overrides);

            StateTreeContext context = MakeContext();
            graph.OnEnter(context);
            graph.OnTick(context, 0.1f);
            Assert.AreEqual(7f, Float(context, "usedSpeed"),
                "the stale row is dropped, the good one still applies");
            graph.OnExit(context, StateTreeStatus.Cancelled);

            graph.ApplyOverrides(overrides);
            LogAssert.NoUnexpectedReceived();
        }

        /// <summary>A GetParam node naming a parameter the program does not declare can only come
        /// from a baker that emitted the node and the parameter list out of step, so it is an ERROR
        /// (not the author's mistake to fix) — and it reads the type default rather than throwing,
        /// so the rest of the graph still runs and the console says what is wrong exactly once.</summary>
        [Test]
        public void Parameters_UndeclaredParameterErrorsOnceAndReadsTheTypeDefault()
        {
            GraphTaskAsset graph = MakeGraph(
                new GraphTaskNode
                {
                    kind = GraphTaskNodeKind.SetBlackboardFloat, stringValue = "usedSpeed",
                    data = new[] { 1 }, exec = new[] { 2 }
                },
                new GraphTaskNode
                {
                    kind = GraphTaskNodeKind.GetParamFloat, stringValue = "ghost"
                },
                SetFloat("past", 1f, -1));
            graph.tickEntry = 0;

            StateTreeContext context = MakeContext();
            graph.OnEnter(context);

            LogAssert.Expect(LogType.Error, new Regex("does not declare"));
            Assert.AreEqual(StateTreeStatus.Running, graph.OnTick(context, 0.1f));
            Assert.AreEqual(0f, Float(context, "usedSpeed"), "the float type default");
            Assert.AreEqual(1f, Float(context, "past"), "and the chain carried on");

            graph.OnTick(context, 0.1f);
            LogAssert.NoUnexpectedReceived();
            graph.OnExit(context, StateTreeStatus.Cancelled);
        }

        /// <summary>The point of putting the overrides on the WRAPPER and applying them to a fresh
        /// copy: two states (or two runners) using one authored graph at different settings must not
        /// see each other's values, and neither may write back into the asset on disk. Interleaved
        /// on purpose — a shared effective set would show up on the second instance's first
        /// tick.</summary>
        [Test]
        public void Parameters_AreIsolatedBetweenInstancesOfOneAuthoredGraph()
        {
            GraphTaskAsset authored = MakeGraph(
                new GraphTaskNode
                {
                    kind = GraphTaskNodeKind.SetBlackboardFloat, stringValue = "usedSpeed",
                    data = new[] { 1 }, exec = new[] { -1 }
                },
                new GraphTaskNode
                {
                    kind = GraphTaskNodeKind.GetParamFloat, stringValue = "speed"
                });
            authored.tickEntry = 0;
            authored.parameters = Params(Param("speed", GraphTaskParameterKind.Float, 3f));

            GraphTaskAsset fast = Track(Object.Instantiate(authored));
            GraphTaskAsset slow = Track(Object.Instantiate(authored));
            fast.ApplyOverrides(Overrides(Override("speed", true, 9f)));
            slow.ApplyOverrides(Overrides(Override("speed", true, 1f)));

            StateTreeContext fastContext = MakeContext("Fast");
            StateTreeContext slowContext = MakeContext("Slow");

            fast.OnEnter(fastContext);
            fast.OnTick(fastContext, 0.1f);
            Assert.AreEqual(9f, Float(fastContext, "usedSpeed"));

            slow.OnEnter(slowContext);
            slow.OnTick(slowContext, 0.1f);
            Assert.AreEqual(1f, Float(slowContext, "usedSpeed"),
                "the second instance must not inherit the first's override");

            fast.OnTick(fastContext, 0.1f);
            Assert.AreEqual(9f, Float(fastContext, "usedSpeed"),
                "nor the first the second's");

            Assert.AreEqual(3f, authored.parameters[0].floatValue,
                "an override must never reach the authored asset");

            fast.OnExit(fastContext, StateTreeStatus.Cancelled);
            slow.OnExit(slowContext, StateTreeStatus.Cancelled);
        }

        // ------------------------------------------------------------------ identity (M7h)
        //
        // An override binds to a declaration by ID. The name is a label the author retypes and the
        // key the running graph reads by; it is never a matching key. These four cases are the rule
        // from both sides — it applies when only the id agrees, it does NOT apply when only the
        // name does — plus the two ways a row can be bound to nothing.

        /// <summary>THE identity rule: the id decides, so a row whose display name has gone out of
        /// date (its declaration was renamed and nobody rewrote the row) still applies. Name-keyed
        /// matching would drop this state back to the graph default the moment someone retyped a
        /// variable — silently, which is the whole failure this replaces.</summary>
        [Test]
        public void Identity_OverrideAppliesWhenOnlyTheIdAgrees()
        {
            // The declaration has been renamed to "moveSpeed"; the row was created back when it was
            // called "speed" and nobody rewrote it. Only the id still agrees.
            GraphTaskAsset graph = ParamGraph("moveSpeed", 3f, "p-speed");

            graph.ApplyOverrides(Overrides(Override("speed", true, 9f, null, "p-speed")));

            Assert.AreEqual(9f, RunOnce(graph),
                "the id bound the row, so the stale display name cost nothing");
            LogAssert.NoUnexpectedReceived();
        }

        /// <summary>The converse, and the half that makes the rule a rule rather than a fallback: a
        /// row that agrees on the NAME and not on the id is stale. It has to be — that shape is a
        /// row left over from a deleted parameter whose name a later one reused, and quietly
        /// handing it the new parameter's value would apply a number the author last typed against
        /// something else entirely.</summary>
        [Test]
        public void Identity_OverrideIsStaleWhenOnlyTheNameAgrees()
        {
            GraphTaskAsset graph = ParamGraph("speed", 3f, "p-speed");

            LogAssert.Expect(LogType.Warning, new Regex("'speed'"));
            graph.ApplyOverrides(Overrides(Override("speed", true, 9f, null, "p-deleted")));

            Assert.AreEqual(3f, RunOnce(graph), "the graph default, not the row's 9");
            LogAssert.NoUnexpectedReceived();
        }

        /// <summary>The rename, executed rather than asserted about: one authored graph, one set of
        /// override rows, and the declaration renamed underneath them between two activations. The
        /// graph's own read moves with the declaration (the name IS the runtime key, and a rebake
        /// rewrites the GetParam node), the row does not move at all, and the value survives.</summary>
        [Test]
        public void Identity_IdBoundOverrideSurvivesADeclarationRename()
        {
            GraphTaskAsset graph = ParamGraph("speed", 3f, "p-speed");
            List<GraphTaskParameterOverride> rows =
                Overrides(Override("speed", true, 9f, null, "p-speed"));

            graph.ApplyOverrides(rows);
            Assert.AreEqual(9f, RunOnce(graph), "before the rename");

            // What the editor does on a rename: the declaration's name changes, its id does not,
            // and the in-graph reads are retargeted at the new name. The override row is untouched.
            graph.parameters[0].name = "moveSpeed";
            graph.nodes[1].stringValue = "moveSpeed";

            graph.ApplyOverrides(rows);
            Assert.AreEqual(9f, RunOnce(graph),
                "the same rows still tune the same parameter after the rename");
            Assert.AreEqual("speed", rows[0].name,
                "and the rename did not have to rewrite the caller's row to achieve it");
            LogAssert.NoUnexpectedReceived();
        }

        /// <summary>A row bound to nothing — no id at all — is stale by definition: there is no
        /// declaration it can claim to be an override OF. It is reported like any other stale row
        /// (once, by whatever label it carries) rather than silently ignored, because the only way
        /// to produce one is a bug in whatever wrote it.</summary>
        [Test]
        public void Identity_RowWithNoIdIsStale()
        {
            GraphTaskAsset graph = ParamGraph("speed", 3f, "p-speed");

            LogAssert.Expect(LogType.Warning, new Regex("'speed'"));
            graph.ApplyOverrides(Overrides(Override("speed", true, 9f, null, string.Empty)));

            Assert.AreEqual(3f, RunOnce(graph), "an unbound row cannot override anything");

            // Once per instance, however many activations re-apply it.
            graph.ApplyOverrides(Overrides(Override("speed", true, 9f, null, string.Empty)));
            LogAssert.NoUnexpectedReceived();
        }

        // ------------------------------------------------------------------ pass-through (M7i)

        /// <summary>
        /// A graph parameter fed by a parameter of the TREE the state lives in
        /// (<see cref="GraphTaskParameterOverride.sourceParameterId"/>) instead of a literal — the
        /// second half of M7i, and what lets one authored graph be driven by three trees rather than
        /// re-tuned by hand in each state that runs it.
        ///
        /// Driven through a real <see cref="StateTreeExecutor"/> because the scope the row reads is
        /// something only a running tree publishes: the assertion is that the value the executor
        /// established for its own parameter came out the other end of the graph, so both halves
        /// have to be real.
        /// </summary>
        [Test]
        public void PassThrough_TreeParameterFeedsAGraphParameter()
        {
            GraphTaskAsset graph = ParamGraph("speed", 3f, Id("speed"));
            RunGraphTask wrapper = MakeGraphTask(graph);
            wrapper.overrides = Overrides(
                Override("speed", true, -1f, null, Id("speed"), Id("treeSpeed")));

            StateTreeAsset tree = MakeTree(MakeNode("run", wrapper), "CallerTree");
            tree.parameters = Params(Param("treeSpeed", GraphTaskParameterKind.Float, 7f));

            StateTreeContext context = MakeContext();
            var executor = new StateTreeExecutor { data = tree, context = context };
            executor.StartTree();
            executor.TickTree(0.1f);

            Assert.AreEqual(7f, Float(context, k_ParamOut),
                "the graph ran at the TREE's value, not at its own 3 and not at the row's -1");
            executor.StopTree();
        }

        /// <summary>A source that is gone (or was retyped) drops the row, so the GRAPH's own default
        /// stands — the same fallback an unchecked row gives — and says so once per instance rather
        /// than once per activation.</summary>
        [Test]
        public void PassThrough_UnknownSourceWarnsOnceAndKeepsTheGraphDefault()
        {
            GraphTaskAsset graph = ParamGraph("speed", 3f, Id("speed"));
            RunGraphTask wrapper = MakeGraphTask(graph);
            wrapper.overrides = Overrides(
                Override("speed", true, -1f, null, Id("speed"), "pid-gone"));

            StateTreeContext context = MakeContext();

            LogAssert.Expect(LogType.Warning, new Regex("'speed'"));
            wrapper.OnEnter(context);
            wrapper.OnTick(context, 0.1f);
            Assert.AreEqual(3f, Float(context, k_ParamOut), "the graph default, never the row's -1");
            wrapper.OnExit(context, StateTreeStatus.Cancelled);

            wrapper.OnEnter(context);
            wrapper.OnExit(context, StateTreeStatus.Cancelled);
            LogAssert.NoUnexpectedReceived();
        }

        // ------------------------------------------------------------------ fixture helpers

        /// <summary>Blackboard key the identity fixture reports its parameter under. Deliberately
        /// NOT the parameter's own name: these cases rename the declaration mid-test, and an output
        /// key that moved with it would confuse "the override was lost" with "we looked in the
        /// wrong place".</summary>
        private const string k_ParamOut = "out";

        /// <summary>The smallest graph that makes one parameter observable: read it, write it to
        /// <see cref="k_ParamOut"/>, stop. Node 1 is the GetParam node, so a test can retarget the
        /// graph's own read the way a rebake would after a rename.</summary>
        private GraphTaskAsset ParamGraph(string parameterName, float declaredDefault, string id)
        {
            GraphTaskAsset graph = MakeGraph(
                new GraphTaskNode
                {
                    kind = GraphTaskNodeKind.SetBlackboardFloat, stringValue = k_ParamOut,
                    data = new[] { 1 }, exec = new[] { -1 }
                },
                new GraphTaskNode
                {
                    kind = GraphTaskNodeKind.GetParamFloat, stringValue = parameterName
                });
            graph.tickEntry = 0;
            graph.parameters = Params(
                Param(parameterName, GraphTaskParameterKind.Float, declaredDefault, null, id));
            return graph;
        }

        /// <summary>One enter/tick/exit cycle on a fresh context, returning what the graph wrote —
        /// the effective value of the parameter, as the running program saw it.</summary>
        private float RunOnce(GraphTaskAsset graph)
        {
            StateTreeContext context = MakeContext();
            graph.OnEnter(context);
            graph.OnTick(context, 0.1f);
            float value = Float(context, k_ParamOut);
            graph.OnExit(context, StateTreeStatus.Cancelled);
            return value;
        }

        private GraphTaskAsset MakeGraph(params GraphTaskNode[] program)
        {
            var graph = ScriptableObject.CreateInstance<GraphTaskAsset>();
            graph.name = "Graph";
            graph.nodes = new List<GraphTaskNode>(program);
            return Track(graph);
        }

        /// <summary>SetBlackboardFloat with a constant, wired to <paramref name="next"/>. The most
        /// repeated node in the fixture: it is how a chain leaves evidence of the route it took.</summary>
        private static GraphTaskNode SetFloat(string key, float value, int next)
        {
            return new GraphTaskNode
            {
                kind = GraphTaskNodeKind.SetBlackboardFloat,
                stringValue = key,
                floatValue = value,
                exec = new[] { next }
            };
        }

        /// <summary>Deterministic stand-in for the identity the editor generates
        /// (<c>Guid.NewGuid().ToString("N")</c>): readable in a failure message, and — because it
        /// is derived from the name — identical for a declaration and a row created from the same
        /// name. That is what lets every case that is NOT about identity keep saying only "speed"
        /// and still be id-bound, the way real authored data is.</summary>
        private static string Id(string name)
            => string.IsNullOrEmpty(name) ? null : "pid-" + name;

        private static GraphTaskParameter Param(string name, GraphTaskParameterKind kind,
            float floatValue = 0f, string stringValue = null, string id = null)
        {
            return new GraphTaskParameter
            {
                name = name, kind = kind, floatValue = floatValue, stringValue = stringValue,
                id = id ?? Id(name)
            };
        }

        private static List<GraphTaskParameter> Params(params GraphTaskParameter[] declared)
            => new List<GraphTaskParameter>(declared);

        /// <summary>An override row. <paramref name="id"/> is what it BINDS by;
        /// <paramref name="name"/> is only what the inspector would show — the identity cases pass
        /// the two deliberately out of step. <paramref name="sourceParameterId"/> makes it a
        /// PASS-THROUGH row: the value then comes from the calling tree's parameter of that id and
        /// <paramref name="floatValue"/> is the literal it must NOT use.</summary>
        private static GraphTaskParameterOverride Override(string name, bool enabled,
            float floatValue = 0f, string stringValue = null, string id = null,
            string sourceParameterId = null)
        {
            return new GraphTaskParameterOverride
            {
                name = name, enabled = enabled, floatValue = floatValue, stringValue = stringValue,
                id = id ?? Id(name), sourceParameterId = sourceParameterId
            };
        }

        /// <summary>The wrapper a state holds: the live graph reference plus this state's override
        /// rows. The pass-through cases need it because the rows live on the WRAPPER, not on the
        /// graph.</summary>
        private RunGraphTask MakeGraphTask(GraphTaskAsset graph)
        {
            var task = ScriptableObject.CreateInstance<RunGraphTask>();
            task.name = "RunGraph";
            task.graph = graph;
            return Track(task);
        }

        private StateTreeNodeAsset MakeNode(string nodeId, params StateTreeTaskAsset[] tasks)
        {
            var node = ScriptableObject.CreateInstance<StateTreeNodeAsset>();
            node.name = nodeId;
            node.nodeId = nodeId;
            node.displayName = nodeId;
            if (tasks != null)
                node.tasks.AddRange(tasks);
            return Track(node);
        }

        private StateTreeAsset MakeTree(StateTreeNodeAsset root, string treeName)
        {
            var tree = ScriptableObject.CreateInstance<StateTreeAsset>();
            tree.name = treeName;
            tree.treeName = treeName;
            tree.root = root;
            return Track(tree);
        }

        private static List<GraphTaskParameterOverride> Overrides(
            params GraphTaskParameterOverride[] rows)
            => new List<GraphTaskParameterOverride>(rows);

        private StubRecordingTask MakeTask(string id, int finishOnTick = 0,
            StateTreeStatus finishStatus = StateTreeStatus.Success)
        {
            var task = ScriptableObject.CreateInstance<StubRecordingTask>();
            task.name = id;
            task.taskId = id;
            task.finishOnTick = finishOnTick;
            task.finishStatus = finishStatus;
            return Track(task);
        }

        private GraphCountingTask MakeCounter(string id, int finishAfter)
        {
            var task = ScriptableObject.CreateInstance<GraphCountingTask>();
            task.name = id;
            task.taskId = id;
            task.finishAfter = finishAfter;
            return Track(task);
        }

        private StubFlagCondition MakeFlag(string key)
        {
            var condition = ScriptableObject.CreateInstance<StubFlagCondition>();
            condition.name = key;
            condition.flagKey = key;
            return Track(condition);
        }

        private StateTreeContext MakeContext(string ownerName = "Owner")
        {
            var owner = new GameObject(ownerName);
            owner.SetActive(false);
            m_Objects.Add(owner);
            return new StateTreeContext(owner);
        }

        /// <summary>Fold cue emissions into the same log the task stubs write to, which is the only
        /// way to assert on the ORDER of an exit chain against a child task's teardown.</summary>
        private static void RecordCues(StateTreeContext context)
        {
            context.cueFired += (name, payload) => StateTreeTestLog.Record(context, "cue:" + name);
        }

        private T Track<T>(T asset) where T : ScriptableObject
        {
            m_Assets.Add(asset);
            return asset;
        }

        private static List<string> Log(StateTreeContext context) => StateTreeTestLog.Get(context);

        private static float Float(StateTreeContext context, string key)
        {
            object value;
            if (context.blackboard.TryGetValue(key, out value) && value is float number)
                return number;
            return float.NaN;
        }
    }
}
