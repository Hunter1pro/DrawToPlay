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

        // ------------------------------------------------------------------ fixture helpers

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
