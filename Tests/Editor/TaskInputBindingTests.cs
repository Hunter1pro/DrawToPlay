using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace PowerOfFire.DrawToPlay.Tests
{
    /// <summary>Consumer stub for input bindings: whatever <see cref="damage"/> holds when the
    /// task RUNS is copied to the blackboard — the only window a test has into the private
    /// per-activation copy the interpreter actually wrote.</summary>
    internal sealed class InputEchoTask : StateTreeTaskAsset
    {
        /// <summary>Baked default is deliberately impossible as a pulled value, so the echo
        /// tells "the binding landed" apart from "the default survived".</summary>
        public float damage = -1f;

        public string echoKey = "seen";

        public override StateTreeStatus OnTick(StateTreeContext context, float deltaTime)
        {
            context.blackboard[echoKey] = damage;
            return StateTreeStatus.Success;
        }
    }

    /// <summary>
    /// EditMode coverage of TASK INPUT BINDINGS — the input mirror of the M7j output pins: a
    /// value pin wired into an embedded call's plain field is pulled and written at every
    /// ENTER of the call. Programs are raw node lists, the same contract-level testing as
    /// <see cref="GraphTaskTests"/>: the baked program IS the interface between baker and
    /// interpreter.
    /// </summary>
    [TestFixture]
    public sealed class TaskInputBindingTests
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

        /// <summary>The core contract: a producer's [TaskOutput] flows through a GetTaskOutput
        /// pull into the consumer's field before the consumer's OnEnter — `var result =
        /// task(); other(result)` with no blackboard in between.</summary>
        [Test]
        public void InputBinding_ProducerOutputLandsOnConsumerFieldAtEnter()
        {
            var producer = ScriptableObject.CreateInstance<StubOutputTask>();
            producer.name = "Producer";
            producer.taskId = "producer";
            producer.finishOnTick = 1;
            producer.emitAmount = 42f;
            Track(producer);

            var consumer = ScriptableObject.CreateInstance<InputEchoTask>();
            consumer.name = "Consumer";
            Track(consumer);

            GraphTaskAsset graph = MakeGraph(
                // 0: the producer — finishes on its first tick, amount = 42.
                new GraphTaskNode
                {
                    kind = GraphTaskNodeKind.DoTask, task = producer, exec = new[] { 2, -1 }
                },
                // 1: the pull off the producer's 'amount' pin.
                new GraphTaskNode
                {
                    kind = GraphTaskNodeKind.GetTaskOutputFloat, stringValue = "amount",
                    data = new[] { 0 }
                },
                // 2: the consumer — its 'damage' field fed by the pull via the binding.
                new GraphTaskNode
                {
                    kind = GraphTaskNodeKind.DoTask, task = consumer, exec = new[] { 3, -1 },
                    data = new[] { 1 }
                },
                // 3: done.
                new GraphTaskNode { kind = GraphTaskNodeKind.ReturnSuccess });
            graph.tickEntry = 0;
            graph.inputBindings.Add(new GraphTaskInputBinding
            {
                node = 2,
                field = nameof(InputEchoTask.damage),
                pin = 0
            });

            StateTreeContext context = MakeContext();
            graph.OnEnter(context);
            StateTreeStatus status = graph.OnTick(context, 0.1f);
            graph.OnExit(context, status);

            Assert.That(status, Is.EqualTo(StateTreeStatus.Success));
            Assert.That(context.blackboard.TryGetValue("seen", out object seen), Is.True,
                "the consumer never ran");
            Assert.That(seen, Is.EqualTo(42f),
                "the producer's output should have landed on the consumer's field before "
                + "OnEnter — the echo saw the baked default instead");
        }

        /// <summary>An unwired program (no bindings) leaves the baked default untouched — the
        /// extension is additive, old programs read exactly as before.</summary>
        [Test]
        public void InputBinding_AbsentBindingKeepsBakedDefault()
        {
            var consumer = ScriptableObject.CreateInstance<InputEchoTask>();
            consumer.name = "Consumer";
            consumer.damage = 7f;
            Track(consumer);

            GraphTaskAsset graph = MakeGraph(
                new GraphTaskNode
                {
                    kind = GraphTaskNodeKind.DoTask, task = consumer, exec = new[] { 1, -1 }
                },
                new GraphTaskNode { kind = GraphTaskNodeKind.ReturnSuccess });
            graph.tickEntry = 0;

            StateTreeContext context = MakeContext();
            graph.OnEnter(context);
            StateTreeStatus status = graph.OnTick(context, 0.1f);
            graph.OnExit(context, status);

            Assert.That(status, Is.EqualTo(StateTreeStatus.Success));
            Assert.That(context.blackboard.TryGetValue("seen", out object seen), Is.True);
            Assert.That(seen, Is.EqualTo(7f));
        }

        // ------------------------------------------------------------------ helpers

        private GraphTaskAsset MakeGraph(params GraphTaskNode[] program)
        {
            var graph = ScriptableObject.CreateInstance<GraphTaskAsset>();
            graph.name = "Graph";
            graph.nodes = new List<GraphTaskNode>(program);
            m_Assets.Add(graph);
            return graph;
        }

        private T Track<T>(T asset) where T : ScriptableObject
        {
            m_Assets.Add(asset);
            return asset;
        }

        private StateTreeContext MakeContext(string ownerName = "Owner")
        {
            var owner = new GameObject(ownerName);
            owner.SetActive(false);
            m_Objects.Add(owner);
            return new StateTreeContext(owner);
        }
    }
}
