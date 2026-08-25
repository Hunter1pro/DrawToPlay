using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace PowerOfFire.DrawToPlay.Tests
{
    /// <summary>
    /// A SCOPE'S TREE NEVER RUNS BEFORE THE PARAMETERS THAT SCOPE DECLARES.
    ///
    /// A level's parameters used to be written straight onto the level host's blackboard after
    /// the scene loaded, and the claim that they landed "before anything ticks" held only by
    /// luck: <c>OnEnter</c> is not a tick, and three ordinary paths ran a tree first — a scene
    /// already open that gets ADOPTED (its hosts Started long ago), a host RE-ENABLED (which
    /// starts its tree again with nobody re-seeding), and a second travel to a level already
    /// up. A state that reads a key its scope declares and finds nothing takes the wrong
    /// branch, and downstream nothing can tell that from a level that declares nothing at all.
    ///
    /// <see cref="StateTreeContextHost.Seed"/> is the guarantee. These tests drive it at that
    /// seam rather than through a scene load, because the seam is where the ordering lives:
    /// every one of them fails if a state ever ticks before its seed is on the board.
    /// </summary>
    [TestFixture]
    public sealed class LevelSeedOrderingTests
    {
        private readonly List<Object> m_Junk = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < m_Junk.Count; i++)
            {
                if (m_Junk[i] is GameObject go && go.TryGetComponent(out StateTreeContextHost host))
                {
                    host.StopTree();
                    host.Unregister();
                }
                if (m_Junk[i] != null)
                    Object.DestroyImmediate(m_Junk[i]);
            }
            m_Junk.Clear();
        }

        [Test]
        public void ASeededScope_HasItsValuesOnTheBoard_BeforeTheTreesFirstEnter()
        {
            StateTreeContextHost host = Scope();
            host.Seed(new Dictionary<string, object> { { "mode", "deathmatch" } });
            host.StartTree();

            Assert.That(First(host), Is.EqualTo("enter:deathmatch"),
                "the state's very first breath found the value its scope declares");
        }

        [Test]
        public void AScopeSeededAFTERItsTreeRan_RunsItAgain_WithTheValue()
        {
            // THE ADOPT PATH, and the double travel: the scene was already open, so the host
            // Started — and ran its first OnEnter — long before anything seeded it.
            StateTreeContextHost host = Scope();
            host.StartTree();
            host.TickTree(0.02f);
            Assert.That(First(host), Is.EqualTo("enter:(nothing)"),
                "the defect, reproduced: the state began against an empty board");

            host.Seed(new Dictionary<string, object> { { "mode", "energy" } });

            List<string> log = StateTreeTestLog.Get(host.Context);
            Assert.That(log[log.Count - 1], Is.EqualTo("enter:energy"),
                "and the seed started it again, with the value on the board");
            host.TickTree(0.02f);
            Assert.That(log[log.Count - 1], Is.EqualTo("tick:energy"),
                "no state ever ticks before the parameters it declares");
        }

        [Test]
        public void ASeedAlreadyOnTheBoard_RestartsNothing()
        {
            StateTreeContextHost host = Scope();
            host.Seed(new Dictionary<string, object> { { "mode", "energy" } });
            host.StartTree();
            host.TickTree(0.02f);
            int before = StateTreeTestLog.Get(host.Context).Count;

            host.Seed(new Dictionary<string, object> { { "mode", "energy" } });

            Assert.That(StateTreeTestLog.Get(host.Context).Count, Is.EqualTo(before),
                "seeding is idempotent — the same values twice are not a second entry");
        }

        [Test]
        public void AReEnabledScope_FindsItsSeedAgain_EvenWithABoardWipedUnderIt()
        {
            // A host disabled and enabled starts its tree again with nobody re-seeding it.
            // The seed is REMEMBERED, so the second start finds it exactly as the first did.
            StateTreeContextHost host = Scope();
            host.Seed(new Dictionary<string, object> { { "mode", "free-for-all" } });
            host.StartTree();
            host.StopTree();
            host.Context.blackboard.Remove("mode");

            host.StartTree();

            List<string> log = StateTreeTestLog.Get(host.Context);
            Assert.That(log[log.Count - 1], Is.EqualTo("enter:free-for-all"),
                "a re-enable is a start, and every start writes the seed first");
            Assert.That(host.seeded["mode"], Is.EqualTo("free-for-all"),
                "the scope still says what it was seeded with");
        }

        // ---- the bench --------------------------------------------------------------------

        /// <summary>One level scope, its tree a single state whose one task says what it saw.
        /// <c>autoStart</c> is off so the test owns the start, which is the thing under test.</summary>
        private StateTreeContextHost Scope()
        {
            var witness = ScriptableObject.CreateInstance<StubSeedWitnessTask>();
            witness.watchKey = "mode";
            m_Junk.Add(witness);
            var node = ScriptableObject.CreateInstance<StateTreeNodeAsset>();
            node.nodeId = "playing";
            node.completeWhen = StateTreeCompleteWhen.Never;
            node.tasks.Add(witness);
            m_Junk.Add(node);
            var tree = ScriptableObject.CreateInstance<StateTreeAsset>();
            tree.treeName = "SeedWitness";
            tree.root = node;
            m_Junk.Add(tree);

            var go = new GameObject("Level") { hideFlags = HideFlags.HideAndDontSave };
            m_Junk.Add(go);
            var host = go.AddComponent<StateTreeContextHost>();
            host.kind = StateTreeContextKind.Level;
            host.autoStart = false;
            host.tree = tree;
            host.Register();
            return host;
        }

        private static string First(StateTreeContextHost host)
        {
            List<string> log = StateTreeTestLog.Get(host.Context);
            Assert.That(log.Count, Is.GreaterThan(0), "the tree ran at all");
            return log[0];
        }
    }
}
