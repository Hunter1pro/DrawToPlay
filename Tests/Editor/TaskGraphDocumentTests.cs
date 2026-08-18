using System.Collections.Generic;
using NUnit.Framework;
using PowerOfFire.DrawToPlay.Editor;
using UnityEditor;
using UnityEngine;

namespace PowerOfFire.DrawToPlay.Tests
{
    /// <summary>
    /// M30.6 — our own authoring surface, judged by the only test that matters: does it bake to
    /// the SAME program?
    ///
    /// The runtime is frozen for this milestone, so a document is only worth having if the
    /// interpreter cannot tell which surface a program came from. The first test re-authors the
    /// demo's push ability as a document and compares the bake, instruction for instruction,
    /// against the one Graph Toolkit produced and shipped — same kinds, same order, same wires,
    /// same calls, same entry. The rest pin what a bake refuses to guess.
    /// </summary>
    [TestFixture]
    public sealed class TaskGraphDocumentTests
    {
        private const string k_PushPath =
            "Assets/DrawToPlayExamples/Demo/M21/Abilities/M21Ability_Push.taskgraph";

        private readonly List<Object> m_Junk = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < m_Junk.Count; i++)
            {
                if (m_Junk[i] != null)
                    Object.DestroyImmediate(m_Junk[i]);
            }
            m_Junk.Clear();
        }

        [Test]
        public void TheDemosAbility_ReAuthoredOnOurSurface_BakesToTheSameProgram()
        {
            GraphTaskAsset shipped = ProgramAt(k_PushPath);
            if (shipped == null)
            {
                Assert.Ignore("The demo's push program is not in this project, so there is "
                    + "nothing to compare against.");
                return;
            }

            TaskGraphDocument document = Doc("Push");

            // THE SAME PROGRAM, SAID IN OUR WORDS: a tick marker, the animation-cue wait, the
            // strike, and one return that both outcomes reach. The calls are the shipped ones —
            // what is under test is the surface, not anybody's ability to retype seven floats.
            TaskGraphDocNode tick = Marker(document, TaskGraphEntry.Tick);
            TaskGraphDocNode wait = Call(document, "wait", shipped.nodes[0].task);
            TaskGraphDocNode strike = Call(document, "strike", shipped.nodes[1].task);
            TaskGraphDocNode done = Node(document, "done", GraphTaskNodeKind.ReturnSuccess);

            Exec(document, tick, 0, wait);
            Exec(document, wait, 0, strike);   // the cue landed
            Exec(document, wait, 1, done);     // a clip with no such frame still ends the ability
            Exec(document, strike, 0, done);
            Exec(document, strike, 1, done);   // a miss is a normal way for a push to go

            var problems = new List<string>();
            GraphTaskAsset ours = TaskGraphDocBaker.Bake(document, problems);
            m_Junk.Add(ours);

            Assert.That(problems, Is.Empty, "a clean document bakes with nothing to say");
            AssertSameProgram(shipped, ours);
        }

        [Test]
        public void ParametersAndOutputs_CrossOver_AndAreNotAliased()
        {
            TaskGraphDocument document = Doc("Returns");
            document.parameters.Add(new GraphTaskParameter
            {
                name = "speed", id = "p1", kind = GraphTaskParameterKind.Float, floatValue = 3f
            });

            TaskGraphDocNode tick = Marker(document, TaskGraphEntry.Tick);
            TaskGraphDocNode read = Node(document, "read", GraphTaskNodeKind.GetParamFloat);
            read.stringValue = "speed";
            TaskGraphDocNode publish = Node(document, "publish", GraphTaskNodeKind.SetOutputFloat);
            publish.stringValue = "distance";
            TaskGraphDocNode done = Node(document, "done", GraphTaskNodeKind.ReturnSuccess);

            Exec(document, tick, 0, publish);
            Exec(document, publish, 0, done);
            Data(document, read, publish, 0);

            var problems = new List<string>();
            GraphTaskAsset baked = TaskGraphDocBaker.Bake(document, problems);
            m_Junk.Add(baked);

            Assert.That(problems, Is.Empty);
            Assert.That(baked.parameters.Count, Is.EqualTo(1));
            Assert.That(baked.parameters[0], Is.Not.SameAs(document.parameters[0]),
                "a bake that aliased its source could edit the document it came from");
            Assert.That(baked.declaredOutputs.Count, Is.EqualTo(1));
            Assert.That(baked.declaredOutputs[0].name, Is.EqualTo("distance"),
                "what a graph returns is read off what it writes — the same answer the other "
                + "baker gives, so a transition's dropdown does not care which surface it was");

            int publishAt = IndexOfKind(baked, GraphTaskNodeKind.SetOutputFloat);
            int readAt = IndexOfKind(baked, GraphTaskNodeKind.GetParamFloat);
            Assert.That(baked.nodes[publishAt].data[0], Is.EqualTo(readAt),
                "a value wire is an index in the program and a NAME in the document — which is "
                + "what lets a node be reordered without breaking a connection");
            Assert.That(baked.tickEntry, Is.EqualTo(publishAt));
        }

        [Test]
        public void ABakeRefusesToGuess_AndNamesWhatItDropped()
        {
            TaskGraphDocument document = Doc("Broken");
            TaskGraphDocNode tick = Marker(document, TaskGraphEntry.Tick);
            TaskGraphDocNode done = Node(document, "done", GraphTaskNodeKind.ReturnSuccess);
            TaskGraphDocNode wait = Node(document, "wait", GraphTaskNodeKind.Wait);

            Exec(document, tick, 0, wait);
            Exec(document, wait, 0, done);
            Exec(document, wait, 3, done);              // a pin it has not got
            Data(document, done, wait, 0);              // a return produces no value
            document.wires.Add(new TaskGraphDocWire { from = wait.id, to = "ghost" });

            var problems = new List<string>();
            GraphTaskAsset baked = TaskGraphDocBaker.Bake(document, problems);
            m_Junk.Add(baked);

            Assert.That(problems.Count, Is.EqualTo(3));
            Assert.That(string.Join(" | ", problems), Does.Contain("exec pin 3")
                .And.Contain("produces no value").And.Contain("ghost"));

            int waitAt = IndexOfKind(baked, GraphTaskNodeKind.Wait);
            Assert.That(baked.nodes[waitAt].exec[0], Is.EqualTo(IndexOfKind(baked,
                GraphTaskNodeKind.ReturnSuccess)), "the wires that made sense still landed");
            Assert.That(baked.nodes[waitAt].data[0], Is.EqualTo(-1));
        }

        // ---- helpers -------------------------------------------------------------------

        private static void AssertSameProgram(GraphTaskAsset expected, GraphTaskAsset actual)
        {
            Assert.That(actual.nodes.Count, Is.EqualTo(expected.nodes.Count), "instruction count");
            Assert.That(actual.tickEntry, Is.EqualTo(expected.tickEntry), "tick entry");
            Assert.That(actual.enterEntry, Is.EqualTo(expected.enterEntry), "enter entry");
            Assert.That(actual.exitEntry, Is.EqualTo(expected.exitEntry), "exit entry");
            Assert.That(actual.parameters.Count, Is.EqualTo(expected.parameters.Count));
            Assert.That(actual.declaredOutputs.Count, Is.EqualTo(expected.declaredOutputs.Count));

            for (int i = 0; i < expected.nodes.Count; i++)
            {
                GraphTaskNode want = expected.nodes[i];
                GraphTaskNode got = actual.nodes[i];
                string at = "instruction " + i + " (" + want.kind + ")";
                Assert.That(got.kind, Is.EqualTo(want.kind), at);
                Assert.That(got.floatValue, Is.EqualTo(want.floatValue).Within(0.0001f), at);
                Assert.That(got.stringValue ?? "", Is.EqualTo(want.stringValue ?? ""), at);
                Assert.That(got.stringValue2 ?? "", Is.EqualTo(want.stringValue2 ?? ""), at);
                Assert.That(got.exec, Is.EqualTo(want.exec), at + " exec");
                Assert.That(got.data, Is.EqualTo(want.data), at + " data");

                if (want.task == null)
                {
                    Assert.That(got.task, Is.Null, at + " call");
                }
                else
                {
                    Assert.That(got.task, Is.Not.Null, at + " call");
                    Assert.That(got.task.GetType(), Is.EqualTo(want.task.GetType()), at + " call");
                    Assert.That(JsonUtility.ToJson(got.task),
                        Is.EqualTo(JsonUtility.ToJson(want.task)),
                        at + " — the call's own settings must survive the bake");
                }
                Assert.That(got.condition == null, Is.EqualTo(want.condition == null), at);
            }
        }

        private static int IndexOfKind(GraphTaskAsset program, GraphTaskNodeKind kind)
        {
            for (int i = 0; i < program.nodes.Count; i++)
            {
                if (program.nodes[i].kind == kind)
                    return i;
            }
            return -1;
        }

        private static GraphTaskAsset ProgramAt(string path)
        {
            Object[] all = AssetDatabase.LoadAllAssetsAtPath(path);
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] is GraphTaskAsset program)
                    return program;
            }
            return null;
        }

        private TaskGraphDocument Doc(string name)
        {
            var document = ScriptableObject.CreateInstance<TaskGraphDocument>();
            document.name = name;
            m_Junk.Add(document);
            return document;
        }

        private static TaskGraphDocNode Marker(TaskGraphDocument document, TaskGraphEntry which)
        {
            var node = new TaskGraphDocNode { id = which + "-marker", entry = which };
            document.nodes.Add(node);
            return node;
        }

        private static TaskGraphDocNode Node(TaskGraphDocument document, string id,
            GraphTaskNodeKind kind)
        {
            var node = new TaskGraphDocNode { id = id, kind = kind };
            document.nodes.Add(node);
            return node;
        }

        private TaskGraphDocNode Call(TaskGraphDocument document, string id,
            StateTreeTaskAsset configured)
        {
            TaskGraphDocNode node = Node(document, id, GraphTaskNodeKind.DoTask);
            node.task = Object.Instantiate(configured);
            node.task.name = configured.name;
            m_Junk.Add(node.task);
            return node;
        }

        private static void Exec(TaskGraphDocument document, TaskGraphDocNode from, int pin,
            TaskGraphDocNode to)
        {
            document.wires.Add(new TaskGraphDocWire
            {
                from = from.id, fromPin = pin, to = to.id
            });
        }

        private static void Data(TaskGraphDocument document, TaskGraphDocNode from,
            TaskGraphDocNode to, int pin)
        {
            document.wires.Add(new TaskGraphDocWire
            {
                from = from.id, to = to.id, toPin = pin, data = true
            });
        }
    }
}
