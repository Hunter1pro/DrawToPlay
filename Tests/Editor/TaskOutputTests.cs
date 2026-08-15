using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace PowerOfFire.DrawToPlay.Tests
{
    /// <summary>
    /// Task stub that RETURNS values — one <c>[TaskOutput]</c> field per supported kind, plus an
    /// undecorated one that must never be captured.
    ///
    /// The outputs are written on the tick the task FINISHES, not in OnEnter, because that is where
    /// a real task computes them (a damage task's last-dealt figure is set on the tick the
    /// hit lands) and because it is the only way to tell "the executor read the fields at completion"
    /// apart from "the executor read the authored values".
    ///
    /// <see cref="OnExit"/> deliberately CORRUPTS <see cref="amount"/>. The capture happens one
    /// statement before OnExit is called, so a routed -999 is the single unambiguous symptom of the
    /// ordering having been reversed — which is the failure that would otherwise show up only as
    /// every graph task silently returning nothing.
    /// </summary>
    internal sealed class StubOutputTask : StateTreeTaskAsset
    {
        /// <summary>Value OnExit writes over <see cref="amount"/>: impossible as a real result, so a
        /// test that sees it can only be seeing a capture that ran too late.</summary>
        public const float exitPoison = -999f;

        public string taskId = "out";

        /// <summary>1-based tick index at which OnTick returns <see cref="finishStatus"/>. Zero or
        /// less = never finishes (the interruptible case).</summary>
        public int finishOnTick = 1;

        public StateTreeStatus finishStatus = StateTreeStatus.Success;

        /// <summary>Write the outputs on EVERY tick rather than only on the finishing one — how a
        /// cancelled task ends up holding a perfectly plausible value it never returned.</summary>
        public bool emitEveryTick;

        public float emitAmount = 1f;
        public int emitCount = 1;
        public bool emitHit = true;
        public string emitLabel = "hit";

        [TaskOutput("How much this stub dealt")]
        public float amount;

        [TaskOutput]
        public int count;

        [TaskOutput]
        public bool hit;

        [TaskOutput]
        public string label = "";

        /// <summary>Not decorated: a public field of a bindable type that is NOT part of the task's
        /// return contract. Nothing may capture it.</summary>
        public float bookkeeping;

        private int m_Ticks;

        public override void OnEnter(StateTreeContext context)
        {
            m_Ticks = 0;
            StateTreeTestLog.Record(context, taskId + ":enter");
        }

        public override StateTreeStatus OnTick(StateTreeContext context, float deltaTime)
        {
            m_Ticks++;
            StateTreeTestLog.Record(context, taskId + ":tick" + m_Ticks);
            bookkeeping = emitAmount;

            bool finishing = finishOnTick > 0 && m_Ticks >= finishOnTick;
            if (finishing || emitEveryTick)
            {
                amount = emitAmount;
                count = emitCount;
                hit = emitHit;
                label = emitLabel;
            }
            return finishing ? finishStatus : StateTreeStatus.Running;
        }

        public override void OnExit(StateTreeContext context, StateTreeStatus status)
        {
            amount = exitPoison;
            StateTreeTestLog.Record(context, taskId + ":exit:" + status);
        }
    }

    /// <summary>
    /// EditMode coverage of TASK OUTPUTS and TRANSITION ROUTING (M7j) — the return flow that mirrors
    /// the parameter flow of M7g/M7h/M7i. A task hands back named values when it finishes; the
    /// transition that ends the state decides where each one is written.
    ///
    /// Every case drives a <see cref="StateTreeExecutor"/> directly rather than through a runner: the
    /// capture record is internal to the machine and the ONLY way to observe it is a route writing
    /// the blackboard, so the tests are written the way the feature is actually used — set up a
    /// route, fire the transition, read the key. That also keeps the "when" honest, since a route
    /// fires exactly once and only for the transition that was taken.
    ///
    /// Trees are built in memory (no AssetDatabase) and the stubs record into the shared context log,
    /// the same rules as <see cref="StateTreeRunnerTests"/> and <see cref="SubTreeTaskTests"/>.
    /// </summary>
    [TestFixture]
    public sealed class TaskOutputTests
    {
        private const string k_InterruptKey = "interrupt";

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
        }

        // ------------------------------------------------------------------ required case 1

        /// <summary>
        /// A <c>[TaskOutput]</c> field is read when the task returns Success, and the value that
        /// reaches the blackboard is the one the field held AT THAT MOMENT — not the authored zero it
        /// started at, and not the poison its OnExit writes a statement later.
        /// </summary>
        [Test]
        public void Capture_TaskOutputFieldsAreReadWhenTheTaskSucceeds()
        {
            StubOutputTask attack = MakeOutputTask("attack", emitAmount: 7f);
            var fight = MakeNode("fight", attack);
            Route(AddTransition(fight, "done", null, false), 0, "amount", "damageDealt");

            StateTreeContext context = MakeContext("Zombie");
            StateTreeExecutor executor = MakeExecutor(TwoStateTree(fight, MakeNode("done")), context);
            executor.StartTree();
            executor.TickTree(0.1f);

            Assert.AreEqual(7f, RoutedFloat(context, "damageDealt"),
                "the value the field held when the task returned Success");
            Assert.AreNotEqual(StubOutputTask.exitPoison, RoutedFloat(context, "damageDealt"),
                "the capture must run BEFORE the task's OnExit — a wrapper's OnExit destroys the "
                + "instance the values live on (RunGraphTask), so this ordering is the feature");
            Assert.AreEqual(0f, attack.amount, "the AUTHORED task is never touched — only the copy");
            LogAssert.NoUnexpectedReceived();
            executor.StopTree();
        }

        /// <summary>A task that FAILS still returned: Success and Failure are both completions, and
        /// only Cancelled is not. The failure edge is exactly where an author wants the reason on the
        /// blackboard.</summary>
        [Test]
        public void Capture_AFailedTaskStillReturnsItsOutputs()
        {
            var fight = MakeNode("fight",
                MakeOutputTask("attack", emitAmount: 2f, finishStatus: StateTreeStatus.Failure));
            Route(AddTransition(fight, "done", null, false), 0, "amount");

            StateTreeContext context = MakeContext("Zombie");
            StateTreeExecutor executor = MakeExecutor(TwoStateTree(fight, MakeNode("done")), context);
            executor.StartTree();
            executor.TickTree(0.1f);

            Assert.AreEqual(2f, RoutedFloat(context, "amount"));
            LogAssert.NoUnexpectedReceived();
            executor.StopTree();
        }

        /// <summary>Only DECORATED fields are outputs. <c>bookkeeping</c> is a public float of a
        /// bindable type holding a real value, and it is still not part of the contract — the
        /// attribute is the contract, which is what keeps every existing task from acquiring an
        /// accidental return surface the day this feature shipped.</summary>
        [Test]
        public void Capture_OnlyDecoratedFieldsAreOutputs()
        {
            var fight = MakeNode("fight", MakeOutputTask("attack", emitAmount: 7f));
            Route(AddTransition(fight, "done", null, false), 0, "bookkeeping");

            StateTreeContext context = MakeContext("Zombie");
            StateTreeExecutor executor = MakeExecutor(TwoStateTree(fight, MakeNode("done")), context);
            LogAssert.Expect(LogType.Warning, new Regex("bookkeeping"));
            executor.StartTree();
            executor.TickTree(0.1f);

            Assert.IsFalse(context.blackboard.ContainsKey("bookkeeping"),
                "an undecorated field is not a return value, however routable its type is");
            LogAssert.NoUnexpectedReceived();
            executor.StopTree();
        }

        // ------------------------------------------------------------------ required case 2

        /// <summary>
        /// A CANCELLED task returns nothing, even though its output fields hold a perfectly plausible
        /// value: it was abandoned mid-flight, and an abandoned call has no result. The route asking
        /// for it is told so and writes nothing, which is the difference between "no answer" and "an
        /// answer from a run that never finished".
        /// </summary>
        [Test]
        public void Capture_ACancelledTaskReturnsNothing()
        {
            StubOutputTask slow = MakeOutputTask("slow", emitAmount: 5f, finishOnTick: 0);
            slow.emitEveryTick = true;                 // the fields DO hold a value all along
            var fight = MakeNode("fight", slow);
            var interrupt = AddTransition(fight, "done", MakeFlag(k_InterruptKey), true);
            Route(interrupt, 0, "amount");

            StateTreeContext context = MakeContext("Zombie");
            StateTreeExecutor executor = MakeExecutor(TwoStateTree(fight, MakeNode("done")), context);
            executor.StartTree();
            executor.TickTree(0.1f);
            context.blackboard[k_InterruptKey] = true;

            LogAssert.Expect(LogType.Warning, new Regex("task 0"));
            executor.TickTree(0.1f);

            Assert.IsFalse(context.blackboard.ContainsKey("amount"),
                "a cancelled task's field value must not reach the blackboard as if it had been "
                + "returned");
            CollectionAssert.Contains(Log(context), "slow:exit:Cancelled",
                "and it really was cancelled rather than merely skipped");
            LogAssert.NoUnexpectedReceived();
            executor.StopTree();
        }

        // ------------------------------------------------------------------ required case 3

        /// <summary>A graph's <c>Set Output</c> instruction is captured the same way a field is: the
        /// program buffers it while it runs, the executor collects it when the task returns.</summary>
        [Test]
        public void Capture_GraphSetOutputIsCaptured()
        {
            GraphTaskAsset graph = MakeOutputGraph("result", 12f);
            var fight = MakeNode("fight", graph);
            Route(AddTransition(fight, "done", null, false), 0, "result");

            StateTreeContext context = MakeContext("Zombie");
            StateTreeExecutor executor = MakeExecutor(TwoStateTree(fight, MakeNode("done")), context);
            executor.StartTree();
            executor.TickTree(0.1f);

            Assert.AreEqual(12f, RoutedFloat(context, "result"));
            Assert.AreEqual("done", executor.activeNodeId);
            LogAssert.NoUnexpectedReceived();
            executor.StopTree();
        }

        /// <summary>Writing one name twice REPLACES: an output is a variable being assigned, not a
        /// stream. The two writes sit on one straight chain, so both run and only the second
        /// counts.</summary>
        [Test]
        public void Capture_GraphLastWriteOfANameWins()
        {
            GraphTaskAsset graph = MakeGraph(
                SetOutput(GraphTaskNodeKind.SetOutputFloat, "result", 1f, 1),
                SetOutput(GraphTaskNodeKind.SetOutputFloat, "result", 4f, 2),
                new GraphTaskNode { kind = GraphTaskNodeKind.ReturnSuccess });
            graph.tickEntry = 0;

            var fight = MakeNode("fight", graph);
            Route(AddTransition(fight, "done", null, false), 0, "result");

            StateTreeContext context = MakeContext("Zombie");
            StateTreeExecutor executor = MakeExecutor(TwoStateTree(fight, MakeNode("done")), context);
            executor.StartTree();
            executor.TickTree(0.1f);

            Assert.AreEqual(4f, RoutedFloat(context, "result"),
                "the second assignment is the one that was in effect when the task returned");
            LogAssert.NoUnexpectedReceived();
            executor.StopTree();
        }

        // ------------------------------------------------------------------ required case 4

        /// <summary>
        /// The routed value is on the blackboard BEFORE the target state's first OnEnter — which is
        /// the only ordering that makes routing useful, since the state the transition leads to is
        /// precisely who wanted the value. Asserted through a reader task inside that state rather
        /// than from the outside, because "the key exists afterwards" would pass either way.
        /// </summary>
        [Test]
        public void Routing_CompletionTransitionWritesTheBlackboardBeforeTheNextStateEnters()
        {
            var fight = MakeNode("fight", MakeOutputTask("attack", emitAmount: 3f));
            Route(AddTransition(fight, "done", null, false), 0, "amount", "damage");
            var done = MakeNode("done", MakeReader("next", "damage"));

            StateTreeContext context = MakeContext("Zombie");
            StateTreeExecutor executor = MakeExecutor(TwoStateTree(fight, done), context);
            executor.StartTree();
            executor.TickTree(0.1f);

            CollectionAssert.AreEqual(
                new[] { "attack:enter", "attack:tick1", "attack:exit:Success", "next:enter:Single(3)" },
                Log(context),
                "the next state's very first lifecycle call already sees the routed value");
            LogAssert.NoUnexpectedReceived();
            executor.StopTree();
        }

        // ------------------------------------------------------------------ required case 5

        /// <summary>
        /// An interrupt routes ONLY the tasks that had already finished. The fast task returned on
        /// the previous tick and its value is routed; the slow one is still running when the
        /// interrupt fires, so it has returned nothing and its row is told so. This falls out of the
        /// exit record holding exactly the completions — there is no separate rule for interrupts,
        /// which is what makes it hard to get wrong later.
        /// </summary>
        [Test]
        public void Routing_AnInterruptRoutesOnlyTheTasksThatAlreadyFinished()
        {
            var fight = MakeNode("fight",
                MakeOutputTask("fast", emitAmount: 5f),
                MakeOutputTask("slow", emitAmount: 9f, finishOnTick: 0));
            var interrupt = AddTransition(fight, "done", MakeFlag(k_InterruptKey), true);
            Route(interrupt, 0, "amount", "fromFast");
            Route(interrupt, 1, "amount", "fromSlow");

            StateTreeContext context = MakeContext("Zombie");
            StateTreeExecutor executor = MakeExecutor(TwoStateTree(fight, MakeNode("done")), context);
            executor.StartTree();
            executor.TickTree(0.1f);
            Assert.AreEqual("fight", executor.activeNodeId,
                "one task is still running, so nothing has completed the state yet");

            context.blackboard[k_InterruptKey] = true;
            LogAssert.Expect(LogType.Warning, new Regex("task 1"));
            executor.TickTree(0.1f);

            Assert.AreEqual(5f, RoutedFloat(context, "fromFast"),
                "the task that finished a tick ago still has its result on the record");
            Assert.IsFalse(context.blackboard.ContainsKey("fromSlow"),
                "the one that was cancelled by this very transition returned nothing");
            LogAssert.NoUnexpectedReceived();
            executor.StopTree();
        }

        // ------------------------------------------------------------------ required case 6

        /// <summary>
        /// A route naming an output the task does not return is normal wear — the output was renamed,
        /// or the row was copied from another task — so it is a WARNING, the row is skipped, the
        /// transition still fires, and it is said ONCE for that row however many times the transition
        /// is taken. The tree here ping-pongs so the same route fires twice.
        /// </summary>
        [Test]
        public void Routing_AMissingOutputWarnsOnceAndSkipsTheRow()
        {
            var fight = MakeNode("fight", MakeOutputTask("attack", emitAmount: 7f));
            Route(AddTransition(fight, "done", null, false), 0, "damage");   // no such output
            var done = MakeNode("done");
            AddTransition(done, "fight", null, false);                       // straight back

            StateTreeContext context = MakeContext("Zombie");
            StateTreeExecutor executor = MakeExecutor(TwoStateTree(fight, done), context);
            LogAssert.Expect(LogType.Warning, new Regex("'damage'"));
            executor.StartTree();

            executor.TickTree(0.1f);                                         // fight -> done (warn)
            Assert.AreEqual("done", executor.activeNodeId, "the transition still fires");
            Assert.IsFalse(context.blackboard.ContainsKey("damage"));

            executor.TickTree(0.1f);                                         // done -> fight
            executor.TickTree(0.1f);                                         // fight -> done again
            Assert.AreEqual("done", executor.activeNodeId);
            LogAssert.NoUnexpectedReceived();
            executor.StopTree();
        }

        /// <summary>The other half of the same rule: a row pointing at a task position that has no
        /// task at all. One warning naming the index, the row skipped, the tree unaffected — an
        /// authoring row that lost its target cannot be allowed to strand the state it was authored
        /// on.</summary>
        [Test]
        public void Routing_AnOutOfRangeTaskIndexWarnsAndTheTransitionStillFires()
        {
            var fight = MakeNode("fight", MakeOutputTask("attack", emitAmount: 7f));
            Route(AddTransition(fight, "done", null, false), 4, "amount");

            StateTreeContext context = MakeContext("Zombie");
            StateTreeExecutor executor = MakeExecutor(TwoStateTree(fight, MakeNode("done")), context);
            LogAssert.Expect(LogType.Warning, new Regex("task 4"));
            executor.StartTree();
            executor.TickTree(0.1f);

            Assert.AreEqual("done", executor.activeNodeId);
            Assert.IsFalse(context.blackboard.ContainsKey("amount"));
            LogAssert.NoUnexpectedReceived();
            executor.StopTree();
        }

        // ------------------------------------------------------------------ required case 7

        /// <summary>An empty key means the output's own name, so routing "amount" to "amount" — the
        /// overwhelmingly common case — needs nothing typed.</summary>
        [Test]
        public void Routing_AnEmptyKeyDefaultsToTheOutputName()
        {
            var fight = MakeNode("fight", MakeOutputTask("attack", emitAmount: 6f));
            var transition = AddTransition(fight, "done", null, false);
            Route(transition, 0, "amount");                 // no key given
            Route(transition, 0, "count", "");              // and an explicitly empty one

            StateTreeContext context = MakeContext("Zombie");
            StateTreeExecutor executor = MakeExecutor(TwoStateTree(fight, MakeNode("done")), context);
            executor.StartTree();
            executor.TickTree(0.1f);

            Assert.AreEqual(6f, RoutedFloat(context, "amount"));
            Assert.AreEqual(1f, RoutedFloat(context, "count"),
                "an int output routes as a Float, the same pairing a Float parameter binds to an "
                + "int field with");
            LogAssert.NoUnexpectedReceived();
            executor.StopTree();
        }

        // ------------------------------------------------------------------ required case 8

        /// <summary>
        /// A state with several tasks: each row picks ITS task by index, so two tasks returning the
        /// same output name land in two different keys with two different values. Without the index
        /// the whole feature would collapse to "the state returned something".
        /// </summary>
        [Test]
        public void Routing_EachRowPicksItsOwnTaskByIndex()
        {
            var fight = MakeNode("fight",
                MakeOutputTask("left", emitAmount: 2f),
                MakeOutputTask("right", emitAmount: 8f));
            var transition = AddTransition(fight, "done", null, false);
            Route(transition, 0, "amount", "leftAmount");
            Route(transition, 1, "amount", "rightAmount");

            StateTreeContext context = MakeContext("Zombie");
            StateTreeExecutor executor = MakeExecutor(TwoStateTree(fight, MakeNode("done")), context);
            executor.StartTree();
            executor.TickTree(0.1f);

            Assert.AreEqual(2f, RoutedFloat(context, "leftAmount"));
            Assert.AreEqual(8f, RoutedFloat(context, "rightAmount"));
            LogAssert.NoUnexpectedReceived();
            executor.StopTree();
        }

        /// <summary>
        /// The index a row carries is the AUTHORED position, which is not the position among the
        /// RUNNING tasks: a hole in the list (a cleared slot, a task whose script went missing) is
        /// skipped when the state is entered but still counts in the inspector, in the Ops remapping
        /// and therefore here. Off-by-one between those two numberings would route the wrong task's
        /// result under the right name, which is the worst failure this feature has.
        /// </summary>
        [Test]
        public void Routing_TaskIndexIsTheAuthoredPositionEvenWithHolesInTheList()
        {
            var fight = MakeNode("fight", null, MakeOutputTask("attack", emitAmount: 5f));
            Route(AddTransition(fight, "done", null, false), 1, "amount");

            StateTreeContext context = MakeContext("Zombie");
            StateTreeExecutor executor = MakeExecutor(TwoStateTree(fight, MakeNode("done")), context);
            executor.StartTree();
            executor.TickTree(0.1f);

            Assert.AreEqual(5f, RoutedFloat(context, "amount"),
                "the only running task is authored at index 1, and that is what the row names");
            LogAssert.NoUnexpectedReceived();
            executor.StopTree();
        }

        // ------------------------------------------------------------------ required case 9

        /// <summary>
        /// <see cref="RunGraphTask"/> forwards the outputs of the instance it owns. This is the case
        /// the capture ORDERING exists for: the wrapper's OnExit destroys that instance, so a capture
        /// that ran after it would find nothing — and would find it silently, for every graph task in
        /// the project.
        /// </summary>
        [Test]
        public void Routing_RunGraphTaskForwardsTheInnerGraphsOutputs()
        {
            RunGraphTask wrapper = MakeGraphTask(MakeOutputGraph("result", 11f));
            var fight = MakeNode("fight", wrapper);
            Route(AddTransition(fight, "done", null, false), 0, "result", "graphResult");

            StateTreeContext context = MakeContext("Zombie");
            StateTreeExecutor executor = MakeExecutor(TwoStateTree(fight, MakeNode("done")), context);
            executor.StartTree();
            executor.TickTree(0.1f);

            Assert.AreEqual(11f, RoutedFloat(context, "graphResult"),
                "the wrapper is what the route names, the program is what produced the value");
            Assert.AreEqual("done", executor.activeNodeId);
            LogAssert.NoUnexpectedReceived();
            executor.StopTree();
        }

        // ------------------------------------------------------------------ boxed types

        /// <summary>
        /// The boxed type each kind lands as, which is a real contract on a
        /// <c>Dictionary&lt;string, object&gt;</c> and not a formality. A routed output shares the
        /// M7g rule with a seeded parameter — String as <c>string</c>, Float as <c>float</c>, and
        /// Bool as a <c>float</c> 1/0 rather than a boxed <c>bool</c>, because
        /// <c>StateTreeLibraryUtil.TryGetFloat</c> accepts float/int/double and nothing else
        /// (StateTreeLibraryUtil.cs:164-177) and that is the path
        /// <see cref="BlackboardCompareCondition"/> reads through. A boxed bool would make every
        /// transition gated on a routed Bool read false forever, with no diagnostic.
        /// </summary>
        [Test]
        public void Routing_EachKindLandsAsTheBoxedTypeTheLibraryReads()
        {
            StubOutputTask attack = MakeOutputTask("attack", emitAmount: 4f);
            attack.emitHit = true;
            attack.emitLabel = "critical";
            var fight = MakeNode("fight", attack);
            var transition = AddTransition(fight, "done", null, false);
            Route(transition, 0, "amount");
            Route(transition, 0, "hit");
            Route(transition, 0, "label");

            StateTreeContext context = MakeContext("Zombie");
            StateTreeExecutor executor = MakeExecutor(TwoStateTree(fight, MakeNode("done")), context);
            executor.StartTree();
            executor.TickTree(0.1f);

            Assert.AreEqual(4f, RoutedFloat(context, "amount"));
            Assert.AreEqual(1f, RoutedFloat(context, "hit"),
                "a Bool routes as float 1/0 — see the summary for what a boxed bool would break");
            object label = Routed(context, "label");
            Assert.IsInstanceOf<string>(label, "a String routes as a string");
            Assert.AreEqual("critical", label);
            LogAssert.NoUnexpectedReceived();
            executor.StopTree();
        }

        /// <summary>The graph's three Set Output kinds, end to end through the same routing. The bool
        /// and string instructions carry their unwired literal in different slots
        /// (<c>floatValue</c> and <c>stringValue2</c>), which is the part a rebake could quietly get
        /// wrong.</summary>
        [Test]
        public void Routing_GraphSetOutputCoversAllThreeKinds()
        {
            GraphTaskAsset graph = MakeGraph(
                SetOutput(GraphTaskNodeKind.SetOutputFloat, "num", 2.5f, 1),
                SetOutput(GraphTaskNodeKind.SetOutputBool, "flag", 1f, 2),
                SetOutputText("text", "ready", 3),
                new GraphTaskNode { kind = GraphTaskNodeKind.ReturnSuccess });
            graph.tickEntry = 0;

            var fight = MakeNode("fight", graph);
            var transition = AddTransition(fight, "done", null, false);
            Route(transition, 0, "num");
            Route(transition, 0, "flag");
            Route(transition, 0, "text");

            StateTreeContext context = MakeContext("Zombie");
            StateTreeExecutor executor = MakeExecutor(TwoStateTree(fight, MakeNode("done")), context);
            executor.StartTree();
            executor.TickTree(0.1f);

            Assert.AreEqual(2.5f, RoutedFloat(context, "num"));
            Assert.AreEqual(1f, RoutedFloat(context, "flag"));
            Assert.AreEqual("ready", Routed(context, "text"));
            LogAssert.NoUnexpectedReceived();
            executor.StopTree();
        }

        // ------------------------------------------------------------------ re-entry

        /// <summary>The exit record belongs to ONE activation. A state re-entered after returning 7
        /// and then cancelled returns nothing at all — not 7 again — because a stale result routed as
        /// a fresh one is indistinguishable from a correct one.</summary>
        [Test]
        public void Routing_ARepeatedActivationDoesNotRouteTheLastOnesResult()
        {
            StubOutputTask attack = MakeOutputTask("attack", emitAmount: 7f, finishOnTick: 2);
            var fight = MakeNode("fight", attack);
            AddTransition(fight, "done", null, false);
            var interrupt = AddTransition(fight, "done", MakeFlag(k_InterruptKey), true);
            Route(interrupt, 0, "amount");
            var done = MakeNode("done");
            AddTransition(done, "fight", null, false);

            StateTreeContext context = MakeContext("Zombie");
            StateTreeExecutor executor = MakeExecutor(TwoStateTree(fight, done), context);
            executor.StartTree();
            executor.TickTree(0.1f);        // tick1: running
            executor.TickTree(0.1f);        // tick2: finishes, completion transition -> done
            Assert.AreEqual("done", executor.activeNodeId);
            Assert.IsFalse(context.blackboard.ContainsKey("amount"),
                "the completion transition carries no routes, so nothing was written");

            executor.TickTree(0.1f);        // done -> fight, record cleared
            context.blackboard[k_InterruptKey] = true;
            LogAssert.Expect(LogType.Warning, new Regex("task 0"));
            executor.TickTree(0.1f);        // interrupt over a task that has not finished again

            Assert.IsFalse(context.blackboard.ContainsKey("amount"),
                "the previous activation's result must not survive into this one");
            LogAssert.NoUnexpectedReceived();
            executor.StopTree();
        }

        // ------------------------------------------------------------------ fixture helpers

        /// <summary>Root over a working state and a terminal one — the shape every case here needs
        /// and none of them is about.</summary>
        private StateTreeAsset TwoStateTree(StateTreeNodeAsset first, StateTreeNodeAsset second)
        {
            var root = MakeNode("root");
            root.children.Add(first);
            root.children.Add(second);
            return MakeTree(root, "OutputTree");
        }

        private StubOutputTask MakeOutputTask(string id, float emitAmount = 1f, int finishOnTick = 1,
            StateTreeStatus finishStatus = StateTreeStatus.Success)
        {
            var task = ScriptableObject.CreateInstance<StubOutputTask>();
            task.name = id + "Output";
            task.taskId = id;
            task.emitAmount = emitAmount;
            task.finishOnTick = finishOnTick;
            task.finishStatus = finishStatus;
            m_Assets.Add(task);
            return task;
        }

        /// <summary>Reuses the sub-tree fixture's reader — it reports the boxed type as well as the
        /// value, which is what makes "the next state saw it" an assertion about the real
        /// contract.</summary>
        private StubBlackboardReadTask MakeReader(string id, string key)
        {
            var task = ScriptableObject.CreateInstance<StubBlackboardReadTask>();
            task.name = id + "Reader";
            task.taskId = id;
            task.key = key;
            m_Assets.Add(task);
            return task;
        }

        private StubFlagCondition MakeFlag(string flagKey)
        {
            var condition = ScriptableObject.CreateInstance<StubFlagCondition>();
            condition.name = flagKey + "Condition";
            condition.flagKey = flagKey;
            m_Assets.Add(condition);
            return condition;
        }

        /// <summary>Adds one route to a transition, the way the inspector's "Route outputs" foldout
        /// would.</summary>
        private static TransitionOutputRoute Route(StateTreeTransition transition, int taskIndex,
            string outputName, string blackboardKey = null)
        {
            var route = new TransitionOutputRoute
            {
                taskIndex = taskIndex,
                outputName = outputName,
                blackboardKey = blackboardKey
            };
            transition.outputRoutes.Add(route);
            return route;
        }

        /// <summary>Smallest program that returns one number: write it, return Success.</summary>
        private GraphTaskAsset MakeOutputGraph(string outputName, float value)
        {
            GraphTaskAsset graph = MakeGraph(
                SetOutput(GraphTaskNodeKind.SetOutputFloat, outputName, value, 1),
                new GraphTaskNode { kind = GraphTaskNodeKind.ReturnSuccess });
            graph.tickEntry = 0;
            return graph;
        }

        private GraphTaskAsset MakeGraph(params GraphTaskNode[] program)
        {
            var graph = ScriptableObject.CreateInstance<GraphTaskAsset>();
            graph.name = "Graph";
            graph.nodes = new List<GraphTaskNode>(program);
            m_Assets.Add(graph);
            return graph;
        }

        /// <summary>A Set Output instruction with its value pin UNWIRED, so the literal slot is what
        /// is returned — <c>floatValue</c> for the float and bool kinds, which is the encoding the
        /// baker emits.</summary>
        private static GraphTaskNode SetOutput(GraphTaskNodeKind kind, string outputName, float value,
            int next)
        {
            return new GraphTaskNode
            {
                kind = kind,
                stringValue = outputName,
                floatValue = value,
                data = new[] { -1 },
                exec = new[] { next }
            };
        }

        /// <summary>The string kind, whose unwired literal lives in <c>stringValue2</c> because
        /// <c>stringValue</c> is already spent on the output's name.</summary>
        private static GraphTaskNode SetOutputText(string outputName, string value, int next)
        {
            return new GraphTaskNode
            {
                kind = GraphTaskNodeKind.SetOutputString,
                stringValue = outputName,
                stringValue2 = value,
                data = new[] { -1 },
                exec = new[] { next }
            };
        }

        private RunGraphTask MakeGraphTask(GraphTaskAsset graph)
        {
            var task = ScriptableObject.CreateInstance<RunGraphTask>();
            task.name = "RunGraph";
            task.graph = graph;
            m_Assets.Add(task);
            return task;
        }

        private StateTreeNodeAsset MakeNode(string nodeId, params StateTreeTaskAsset[] tasks)
        {
            var node = ScriptableObject.CreateInstance<StateTreeNodeAsset>();
            node.name = nodeId;
            node.nodeId = nodeId;
            node.displayName = nodeId;
            if (tasks != null)
                node.tasks.AddRange(tasks);
            m_Assets.Add(node);
            return node;
        }

        private static StateTreeTransition AddTransition(StateTreeNodeAsset source,
            string targetNodeId, StateTreeConditionAsset condition, bool checkWhileRunning)
        {
            var transition = new StateTreeTransition
            {
                targetNodeId = targetNodeId,
                condition = condition,
                checkWhileRunning = checkWhileRunning
            };
            source.transitions.Add(transition);
            return transition;
        }

        private StateTreeAsset MakeTree(StateTreeNodeAsset root, string treeName)
        {
            var tree = ScriptableObject.CreateInstance<StateTreeAsset>();
            tree.name = treeName;
            tree.treeName = treeName;
            tree.root = root;
            m_Assets.Add(tree);
            return tree;
        }

        private static StateTreeExecutor MakeExecutor(StateTreeAsset tree, StateTreeContext context)
        {
            return new StateTreeExecutor
            {
                data = tree,
                context = context,
                owner = context.owner
            };
        }

        private StateTreeContext MakeContext(string ownerName)
        {
            var go = new GameObject(ownerName);
            go.hideFlags = HideFlags.HideAndDontSave;
            go.SetActive(false);
            m_Objects.Add(go);
            return new StateTreeContext(go);
        }

        /// <summary>The routed value AND its boxed type — see
        /// <see cref="Routing_EachKindLandsAsTheBoxedTypeTheLibraryReads"/> for why the type is
        /// asserted rather than just the number.</summary>
        private static float RoutedFloat(StateTreeContext context, string key)
        {
            object value = Routed(context, key);
            Assert.IsInstanceOf<float>(value,
                "'" + key + "' must be boxed as a float — StateTreeLibraryUtil.TryGetFloat "
                + "(the BlackboardCompareCondition path) reads float/int/double and nothing else");
            return (float)value;
        }

        private static object Routed(StateTreeContext context, string key)
        {
            object value;
            Assert.IsTrue(context.blackboard.TryGetValue(key, out value),
                "the blackboard carries no key '" + key + "' — the route did not fire");
            return value;
        }

        private static List<string> Log(StateTreeContext context) => StateTreeTestLog.Get(context);
    }
}
