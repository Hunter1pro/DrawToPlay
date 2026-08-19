using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace PowerOfFire.DrawToPlay.Tests
{
    /// <summary>
    /// EditMode coverage of the M9 world registry (brief §3.3): order-free adoption, stable
    /// ids, tag queries with distance, teardown, the two atoms, and the deep log. Same ground
    /// rules as the other suites — everything in memory, lifecycle mirrored through the public
    /// methods (Register/AdoptStrays/RegisterToWorld/EnsureStableId), because plain
    /// MonoBehaviour callbacks do not run in EditMode. World-object GameObjects carry no
    /// HideAndDontSave: the adoption sweep goes through FindObjectsByType, which skips
    /// DontSave objects, and hiding them would test nothing.
    /// </summary>
    [TestFixture]
    public sealed class WorldServiceTests
    {
        private readonly List<GameObject> m_Objects = new List<GameObject>();
        private readonly List<ScriptableObject> m_Assets = new List<ScriptableObject>();
        private readonly List<StateTreeContextHost> m_Hosts = new List<StateTreeContextHost>();

        [SetUp]
        public void SetUp()
        {
            m_Objects.Clear();
            m_Assets.Clear();
            m_Hosts.Clear();
        }

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

        // ------------------------------------------------------------------ 1. adoption

        [Test]
        public void Adoption_IsOrderFree_AndIdempotent()
        {
            WorldObjectBehaviour early = MakeWorldObject("Early", "barrel");
            WorldService world = MakeWorld(out _);

            world.AdoptStrays();
            Assert.AreEqual(1, world.registeredCount, "the sweep adopted the early object");
            Assert.AreSame(world, early.registeredWith, "and the object knows its registry");

            WorldObjectBehaviour late = MakeWorldObject("Late", "barrel");
            late.RegisterToWorld();
            Assert.AreEqual(2, world.registeredCount, "a later object self-registers");

            world.AdoptStrays();
            late.RegisterToWorld();
            Assert.AreEqual(2, world.registeredCount, "both paths are idempotent");
        }

        // ----------------------------------------------------------------- 2. identity

        [Test]
        public void StableIds_MintOnce_AndResolveById()
        {
            WorldService world = MakeWorld(out _);
            WorldObjectBehaviour a = MakeWorldObject("A", "chest");
            WorldObjectBehaviour b = MakeWorldObject("B", "chest");
            a.EnsureStableId();
            b.EnsureStableId();

            Assert.IsNotEmpty(a.stableId);
            Assert.AreNotEqual(a.stableId, b.stableId, "two objects, two identities");

            string minted = a.stableId;
            a.EnsureStableId();
            Assert.AreEqual(minted, a.stableId, "minting is once — the id is stable");

            a.RegisterToWorld();
            Assert.AreSame(a, world.FindById(minted), "the registry answers by id");
            Assert.IsNull(world.FindById("no-such-id"));
        }

        // -------------------------------------------------------------- 3. tag queries

        [Test]
        public void TagQueries_NearestDistanceAndCollection()
        {
            WorldService world = MakeWorld(out _);
            WorldObjectBehaviour near = MakeWorldObject("Near", "lever");
            WorldObjectBehaviour far = MakeWorldObject("Far", "lever");
            near.transform.position = new Vector3(1f, 0f, 0f);
            far.transform.position = new Vector3(10f, 0f, 0f);
            near.RegisterToWorld();
            far.RegisterToWorld();

            Assert.AreSame(near, world.FindNearest("lever", Vector3.zero),
                "nearest by distance");
            Assert.AreSame(far, world.FindNearest("lever", new Vector3(8f, 0f, 0f)),
                "measured from the caller, not the origin");
            Assert.IsNull(world.FindNearest("lever", Vector3.zero, 0.5f),
                "maxDistance is a cutoff, not a preference");
            Assert.IsNull(world.FindNearest("door", Vector3.zero), "an unknown tag finds nothing");

            var bucket = new List<WorldObjectBehaviour>();
            Assert.AreEqual(2, world.CollectByTag("lever", bucket));
            Assert.IsTrue(world.HasTag("lever"));
            Assert.IsFalse(world.HasTag("door"));
        }

        [Test]
        public void Unregister_RemovesFromEveryIndex()
        {
            WorldService world = MakeWorld(out _);
            WorldObjectBehaviour obj = MakeWorldObject("Barrel", "explosive");
            obj.EnsureStableId();
            obj.RegisterToWorld();

            obj.UnregisterFromWorld();
            Assert.AreEqual(0, world.registeredCount);
            Assert.IsFalse(world.HasTag("explosive"));
            Assert.IsNull(world.FindById(obj.stableId));
            Assert.IsNull(obj.registeredWith);
        }

        // ------------------------------------------------------------------- 4. atoms

        [Test]
        public void FindByTagTask_WritesTarget_ClearsOnMiss_PicksNearest()
        {
            MakeWorld(out StateTreeContextHost root);
            WorldObjectBehaviour near = MakeWorldObject("NearEnemy", "enemy");
            WorldObjectBehaviour far = MakeWorldObject("FarEnemy", "enemy");
            near.transform.position = new Vector3(2f, 0f, 0f);
            far.transform.position = new Vector3(9f, 0f, 0f);
            near.RegisterToWorld();
            far.RegisterToWorld();

            GameObject unit = MakeUnit("Unit", root);
            var find = ScriptableObject.CreateInstance<FindByTagTask>();
            find.tag.text = "enemy";
            m_Assets.Add(find);

            var context = new StateTreeContext(unit);
            Assert.AreEqual(StateTreeStatus.Success, find.OnTick(context, 0.1f));
            Assert.AreSame(near.gameObject, context.blackboard["target"],
                "the nearest enemy landed under the perception convention key");

            find.tag.text = "dragon";
            Assert.AreEqual(StateTreeStatus.Failure, find.OnTick(context, 0.1f),
                "no dragon = Failure, the branchable answer");
            Assert.IsFalse(context.blackboard.ContainsKey("target"),
                "and the stale target was CLEARED, not kept");
        }

        [Test]
        public void HasWorldTagCondition_ExistenceAndInvert()
        {
            MakeWorld(out StateTreeContextHost root);
            WorldObjectBehaviour beacon = MakeWorldObject("Beacon", "beacon");
            beacon.RegisterToWorld();
            GameObject unit = MakeUnit("Unit", root);
            var context = new StateTreeContext(unit);

            var has = ScriptableObject.CreateInstance<HasWorldTagCondition>();
            has.tag.text = "beacon";
            m_Assets.Add(has);
            Assert.IsTrue(has.Evaluate(context));
            has.invert = true;
            Assert.IsFalse(has.Evaluate(context));

            beacon.UnregisterFromWorld();
            Assert.IsTrue(has.Evaluate(context), "inverted goes true when the last one is gone");
        }

        // ------------------------------------------------------- 4b. composed citizens

        /// <summary>
        /// An object that is honestly TWO citizens — M21's NPC is a body that animates and a
        /// person who talks — answers as either of them.
        ///
        /// The by-GameObject index keeps one citizen per object, last registration winning, so
        /// before this the answer depended on enable order: the NPC was not an
        /// <c>OutpostCharacter</c> the world knew, [InjectOwner] refused, and the tree that needed
        /// a body silently would not run.
        /// </summary>
        [Test]
        public void FacetOf_AnswersForEitherCitizenOnOneObject()
        {
            MakeWorld(out _);

            var go = new GameObject("Npc");
            m_Objects.Add(go);
            var body = go.AddComponent<StubBodyCitizen>();
            var mind = go.AddComponent<StubMindCitizen>();
            body.RegisterToWorld();
            mind.RegisterToWorld();   // last in, so it is what the index holds

            Assert.AreSame(mind, WorldOf(go).FacetOf<StubMindCitizen>(go),
                "the citizen the index holds still answers directly");
            Assert.AreSame(body, WorldOf(go).FacetOf<StubBodyCitizen>(go),
                "and so does the one it displaced");
        }

        /// <summary>The reflection twin the [InjectOwner] injector actually calls — same
        /// guarantee, because that is the caller the bug was found through.</summary>
        [Test]
        public void FacetOfByType_AnswersForTheDisplacedCitizen()
        {
            MakeWorld(out _);

            var go = new GameObject("Npc");
            m_Objects.Add(go);
            var body = go.AddComponent<StubBodyCitizen>();
            go.AddComponent<StubMindCitizen>().RegisterToWorld();
            body.RegisterToWorld();

            Assert.AreSame(body, WorldOf(go).FacetOf(typeof(StubBodyCitizen), go));
            Assert.IsNotNull(WorldOf(go).FacetOf(typeof(StubMindCitizen), go));
        }

        /// <summary>A type nobody on the object is stays null — the fallback must not turn
        /// "this is not that" into a match.</summary>
        [Test]
        public void FacetOf_StillMissesWhenNoCitizenIsThatType()
        {
            MakeWorld(out _);

            var go = new GameObject("Npc");
            m_Objects.Add(go);
            go.AddComponent<StubMindCitizen>().RegisterToWorld();

            Assert.IsNull(WorldOf(go).FacetOf<StubBodyCitizen>(go));
        }

        /// <summary>An object the world has never registered is unknown, siblings or not.</summary>
        [Test]
        public void FacetOf_UnregisteredObjectIsUnknown()
        {
            WorldService world = MakeWorld(out _);

            var go = new GameObject("Stranger");
            m_Objects.Add(go);
            go.AddComponent<StubBodyCitizen>();

            Assert.IsNull(world.FacetOf<StubBodyCitizen>(go));
        }

        /// <summary>The world a citizen registered into — the same lookup the atoms use.</summary>
        private static WorldService WorldOf(GameObject go)
        {
            return StateTreeContextHost.FindService<WorldService>(go);
        }

        // ---------------------------------------------------------------- 5. deep log

        [Test]
        public void DeepLog_RecordsAndCaps()
        {
            WorldService world = MakeWorld(out _);
            WorldObjectBehaviour obj = MakeWorldObject("Barrel", "explosive");
            obj.RegisterToWorld();
            world.FindNearest("explosive", Vector3.zero);
            world.FindNearest("missing", Vector3.zero);

            Assert.IsTrue(world.recentLog.Count >= 3, "register + both queries were recorded");
            StringAssert.Contains("register 'Barrel'", world.recentLog[world.recentLog.Count - 3]);
            StringAssert.Contains("-> 'Barrel'", world.recentLog[world.recentLog.Count - 2]);
            StringAssert.Contains("-> none", world.recentLog[world.recentLog.Count - 1]);

            world.logCapacity = 2;
            world.FindNearest("explosive", Vector3.zero);
            Assert.AreEqual(2, world.recentLog.Count, "the ring honors its cap");
        }

        // ---------------------------------------------------------------------- fixtures

        /// <summary>Root host + WorldService on the same GameObject, connected the way
        /// placement would in play mode.</summary>
        private WorldService MakeWorld(out StateTreeContextHost root)
        {
            var go = new GameObject("Root");
            m_Objects.Add(go);
            root = go.AddComponent<StateTreeContextHost>();
            root.kind = StateTreeContextKind.Root;
            root.autoStart = false;
            root.Register();
            m_Hosts.Add(root);

            var world = new WorldService(root, null);
            root.Provide(world);
            return world;
        }

        private WorldObjectBehaviour MakeWorldObject(string goName, string tag)
        {
            var go = new GameObject(goName);
            m_Objects.Add(go);
            var obj = go.AddComponent<WorldObjectBehaviour>();
            obj.tags.Add(tag);
            return obj;
        }

        private GameObject MakeUnit(string goName, StateTreeContextHost parent)
        {
            var go = new GameObject(goName);
            go.hideFlags = HideFlags.HideAndDontSave;
            if (parent != null)
                go.transform.SetParent(parent.transform);
            m_Objects.Add(go);
            return go;
        }
    }

    /// <summary>One half of a composed object: the BODY, the thing that moves and animates.
    /// Stands in for M21's OutpostCharacter, which lives in the examples assembly.</summary>
    internal sealed class StubBodyCitizen : WorldObjectBehaviour
    {
    }

    /// <summary>The other half: the MIND, the thing with something to say. A second citizen on
    /// the same transform, exactly as an NPC is.</summary>
    internal sealed class StubMindCitizen : WorldObjectBehaviour
    {
    }
}
