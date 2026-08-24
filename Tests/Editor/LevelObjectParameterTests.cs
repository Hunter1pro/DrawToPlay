using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace PowerOfFire.DrawToPlay.Tests
{
    /// <summary>
    /// The MANIFEST → TREE argument channel: a placement row carries id-bound override rows for
    /// the parameters its tree declares (<see cref="LevelObjectDef.parameters"/>), the
    /// spawner copies them onto the spawned host (<see cref="ServiceBodyFactory.Mind"/>),
    /// and the executor seeds the effective values into the blackboard under the parameters'
    /// names — so a task reads a plain key and one authored tree serves every placement.
    ///
    /// This is the surface that keeps "where does this exit lead" out of components: no task
    /// reaches into the object it runs on to find out what the author meant.
    /// </summary>
    [TestFixture]
    public sealed class LevelObjectParameterTests
    {
        private const string k_ParamId = "test.destination.id";

        private readonly List<GameObject> m_Objects = new List<GameObject>();
        private readonly List<ScriptableObject> m_Assets = new List<ScriptableObject>();
        private readonly List<StateTreeContextHost> m_Hosts = new List<StateTreeContextHost>();

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < m_Hosts.Count; i++)
            {
                if (!ReferenceEquals(m_Hosts[i], null))
                    m_Hosts[i].Unregister();
            }
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
            m_Hosts.Clear();
        }

        [Test]
        public void RowArgument_ReachesTheBlackboard_UnderTheParameterName()
        {
            var row = new LevelObjectDef { tree = MakeDeclaringTree() };
            row.parameters.values.Add(new GraphTaskParameterOverride
            {
                name = "destination", enabled = true, stringValue = "ridge", id = k_ParamId
            });

            StateTreeContextHost host = MakeHost();
            ServiceBodyFactory.Mind(host, row);
            host.StartTree();

            Assert.AreEqual("ridge", host.Context.blackboard["destination"],
                "the row's argument, not the declared default, is what a task reads");
        }

        [Test]
        public void NoArgument_SeedsTheDeclaredDefault()
        {
            var row = new LevelObjectDef { tree = MakeDeclaringTree() };

            StateTreeContextHost host = MakeHost();
            ServiceBodyFactory.Mind(host, row);
            host.StartTree();

            Assert.AreEqual("", host.Context.blackboard["destination"],
                "an unargued parameter still seeds — with the tree's own default");
        }

        [Test]
        public void HostRows_AreCopies_NeverTheRegistrysOwn()
        {
            var row = new LevelObjectDef { tree = MakeDeclaringTree() };
            var argument = new GraphTaskParameterOverride
            {
                name = "destination", enabled = true, stringValue = "ridge", id = k_ParamId,
                entryId = "level.ridge"
            };
            row.parameters.values.Add(argument);

            StateTreeContextHost host = MakeHost();
            ServiceBodyFactory.Mind(host, row);

            Assert.AreEqual(1, host.parameterOverrides.Count);
            Assert.AreNotSame(argument, host.parameterOverrides[0],
                "a live host must never hold the registry's rows — an inspector edit on the "
                + "spawned object would write the authored asset");
            Assert.AreEqual("ridge", host.parameterOverrides[0].stringValue);
            Assert.AreEqual(k_ParamId, host.parameterOverrides[0].id,
                "the id is the binding (M7h) and must survive the copy");
            Assert.AreEqual("level.ridge", host.parameterOverrides[0].entryId,
                "the entry wire survives the copy — it is authored data like the rest");
        }

        // ------------------------------------------------------------------------- helpers

        /// <summary>A tree that declares one String parameter, 'destination', defaulting to
        /// empty — the exit trees' shape, reduced to what these tests are about.</summary>
        private StateTreeAsset MakeDeclaringTree()
        {
            var task = ScriptableObject.CreateInstance<WaitTask>();
            task.seconds = 1f;
            m_Assets.Add(task);

            var node = ScriptableObject.CreateInstance<StateTreeNodeAsset>();
            node.nodeId = "resident";
            node.tasks.Add(task);
            m_Assets.Add(node);

            var tree = ScriptableObject.CreateInstance<StateTreeAsset>();
            tree.treeName = "ParameterTestTree";
            tree.root = node;
            tree.parameters.Add(new GraphTaskParameter
            {
                name = "destination",
                kind = GraphTaskParameterKind.String,
                stringValue = "",
                id = k_ParamId
            });
            m_Assets.Add(tree);
            return tree;
        }

        private StateTreeContextHost MakeHost()
        {
            var go = new GameObject("ExitHost");
            go.hideFlags = HideFlags.HideAndDontSave;
            m_Objects.Add(go);

            var host = go.AddComponent<StateTreeContextHost>();
            host.kind = StateTreeContextKind.Level;
            host.autoStart = false;
            host.Register();
            m_Hosts.Add(host);
            return host;
        }
    }
}
