using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace PowerOfFire.DrawToPlay.Tests
{
    /// <summary>
    /// M27 — the director. A cutscene is a row (cast + beats), the cast is bound by world tag
    /// ONCE at the start, the script is an ordinary tree on a host the service makes and
    /// destroys, and skipping is the same shutdown as finishing.
    ///
    /// The first test here is a regression lock with a story: the cast used to be published
    /// to the director's board AFTER StartTree, and since starting a tree opens its first
    /// state in the same call, every beat entered against an empty board and finished
    /// instantly — a scene that played perfectly in zero seconds with nobody in it. Only
    /// OnEnter can see that, which is what the witness beat is for.
    /// </summary>
    [TestFixture]
    public sealed class CutsceneServiceTests
    {
        private readonly List<GameObject> m_Objects = new List<GameObject>();
        private readonly List<ScriptableObject> m_Assets = new List<ScriptableObject>();
        private readonly List<StateTreeContextHost> m_Hosts = new List<StateTreeContextHost>();

        private StateTreeContextHost m_Root;
        private StateTreeContextHost m_Level;
        private StateTreeContextHost m_Player;
        private WorldService m_World;
        private CutsceneService m_Director;
        private CutsceneRegistry m_Cutscenes;

        [SetUp]
        public void SetUp()
        {
            m_Cutscenes = ScriptableObject.CreateInstance<CutsceneRegistry>();
            m_Assets.Add(m_Cutscenes);

            var def = ScriptableObject.CreateInstance<ServiceDef>();
            def.serviceName = "cutscenes";
            def.scope = StateTreeContextKind.Root;
            def.registry = m_Cutscenes;
            m_Assets.Add(def);

            // THE SPINE, three deep: the director lives on the root, the stage is the level,
            // and the player hangs inside the level — which is what makes "walk up from
            // something that is IN it" answer at all.
            m_Root = MakeHost("Root", StateTreeContextKind.Root, null);
            m_Level = MakeHost("Level", StateTreeContextKind.Level, m_Root.transform);
            m_Player = MakeHost("Player", StateTreeContextKind.Player, m_Level.transform);

            m_World = new WorldService(m_Level, null);


            m_Level.Provide(m_World);

            m_Director = new CutsceneService(m_Root, def);
            m_Root.Provide(m_Director);
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

        /// <summary>THE LOCK. The beat must find its actor the instant it opens.</summary>
        [Test]
        public void TheCastStandsOnTheBoard_BeforeTheFirstBeatOpens()
        {
            MakeCitizen("Keeper", new Vector3(3f, 0f, 0f), "keeper");
            CutsceneDef scene = MakeScene("greeting", MakeWitness("keeper"),
                Role("keeper", "keeper", true));

            CutsceneResult result = m_Director.Play(scene.name, "place.scene.greeting",
                m_Player.gameObject);

            Assert.That(result.refusal, Is.Empty, "a fully cast scene must not refuse");
            Assert.That(ScriptLog(), Is.EqualTo(new[] { "keeper:enter:Keeper" }),
                "the cast must be on the board when the beat opens, not one moment later");
        }

        /// <summary>A scene runs on a host of its own, under the stage — so the beats are an
        /// ordinary tree with an ordinary blackboard, and the stage is the LEVEL the hint
        /// stands in rather than whatever the spine answers from the top.</summary>
        [Test]
        public void TheScriptRunsOnItsOwnHost_UnderTheStageTheHintStandsIn()
        {
            MakeCitizen("Keeper", new Vector3(3f, 0f, 0f), "keeper");
            CutsceneDef scene = MakeScene("greeting", MakeWitness("keeper"),
                Role("keeper", "keeper", true));

            m_Director.Play(scene.name, "place.scene.greeting", m_Player.gameObject);

            Transform stage = FindDirectorHost();
            Assert.That(stage, Is.Not.Null, "the director must raise a host for the script");
            Assert.That(stage.parent, Is.SameAs(m_Level.transform),
                "the script belongs to the level the asker stands in");

            m_Director.Finish(skipped: false);
            Assert.That(FindDirectorHost(), Is.Null, "and the stage comes down after it");
        }

        /// <summary>A part nobody can play is a refusal in a sentence, not a null actor
        /// halfway through the scene.</summary>
        [Test]
        public void AMissingRequiredActor_RefusesInASentence_AndNothingOpens()
        {
            CutsceneDef scene = MakeScene("greeting", MakeWitness("keeper"),
                Role("keeper", "keeper", true));

            CutsceneResult result = m_Director.Play(scene.name, "place.scene.greeting",
                m_Player.gameObject);

            Assert.That(result.refusal, Does.Contain("keeper"), "the refusal must name the part");
            Assert.That(m_Director.playing, Is.Null);
            Assert.That(FindDirectorHost(), Is.Null,
                "no stage, so no beat opened without its cast");
        }

        /// <summary>An OPTIONAL part missing is not a refusal — the scene plays without it,
        /// and the beat addressed to nobody is the author's problem, not the director's.</summary>
        [Test]
        public void AnOptionalPartMissing_PlaysAnyway()
        {
            MakeCitizen("Keeper", new Vector3(3f, 0f, 0f), "keeper");
            CutsceneDef scene = MakeScene("greeting", MakeWitness("keeper"),
                Role("keeper", "keeper", true), Role("crowd", "villager", false));

            CutsceneResult result = m_Director.Play(scene.name, "", m_Player.gameObject);

            Assert.That(result.refusal, Is.Empty);
            Assert.That(m_Director.playing, Is.SameAs(scene));
        }

        /// <summary>A scene is a MODE, and a mode has exactly one holder.</summary>
        [Test]
        public void ASecondScene_WhileOnePlays_Refuses()
        {
            MakeCitizen("Keeper", new Vector3(3f, 0f, 0f), "keeper");
            CutsceneDef first = MakeScene("greeting", MakeWitness("keeper"),
                Role("keeper", "keeper", true));
            CutsceneDef second = MakeScene("parting", MakeWitness("keeper"),
                Role("keeper", "keeper", true));

            m_Director.Play(first.name, "", m_Player.gameObject);
            CutsceneResult result = m_Director.Play(second.name, "", m_Player.gameObject);

            Assert.That(result.refusal, Does.Contain("already playing"));
            Assert.That(m_Director.playing, Is.SameAs(first), "the one that holds it keeps it");
        }

        /// <summary>Taking the controls is a standing tag on everyone the scene uses, plus a
        /// key on the root board the player's own tree reads — and both come off at the end,
        /// by the same road whether the script ran out or the player skipped.</summary>
        [Test]
        public void Skipping_TakesTheTagsBack_WritesThePlacementOff_AndSaysItWasSkipped()
        {
            WorldObjectBehaviour keeper = MakeCitizen("Keeper", new Vector3(3f, 0f, 0f), "keeper");
            AbilityHost keeperHost = keeper.gameObject.AddComponent<AbilityHost>();
            AbilityHost playerHost = m_Player.gameObject.AddComponent<AbilityHost>();
            CutsceneDef scene = MakeScene("greeting", MakeWitness("keeper"),
                Role("keeper", "keeper", true));

            var spent = new List<string>();
            m_Director.spent += spent.Add;

            m_Director.Play(scene.name, "place.scene.greeting", m_Player.gameObject);
            Assert.That(keeperHost.HasTag(CutsceneKeys.WatchingTag), Is.True, "the cast is held");
            Assert.That(playerHost.HasTag(CutsceneKeys.WatchingTag), Is.True, "and so is the player");
            Assert.That(m_Root.Context.blackboard.ContainsKey(CutsceneKeys.Playing), Is.True,
                "the mode key is what parks the player's tree in its watching state");

            m_Director.Finish(skipped: true);

            Assert.That(keeperHost.HasTag(CutsceneKeys.WatchingTag), Is.False);
            Assert.That(playerHost.HasTag(CutsceneKeys.WatchingTag), Is.False);
            Assert.That(m_Root.Context.blackboard.ContainsKey(CutsceneKeys.Playing), Is.False);
            Assert.That(spent, Is.EqualTo(new[] { "place.scene.greeting" }),
                "one-shot-ness belongs to the PLACE, so the placement id is what comes back");

            var announced = m_Root.Context.blackboard[CutsceneResult.Key] as CutsceneResult;
            Assert.That(announced, Is.Not.Null);
            Assert.That(announced.skipped, Is.True);
            Assert.That(announced.played, Is.False);
        }

        /// <summary>A scene that is NOT once-only leaves no placement to write off, however
        /// it was placed — the row says whether the moment is spendable.</summary>
        [Test]
        public void AReplayableScene_IsNeverWrittenOff()
        {
            MakeCitizen("Keeper", new Vector3(3f, 0f, 0f), "keeper");
            CutsceneDef scene = MakeScene("greeting", MakeWitness("keeper"),
                Role("keeper", "keeper", true));
            scene.playsOnce = false;

            var spent = new List<string>();
            m_Director.spent += spent.Add;

            m_Director.Play(scene.name, "place.scene.greeting", m_Player.gameObject);
            m_Director.Finish(skipped: false);

            Assert.That(spent, Is.Empty);
        }

        /// <summary>An unknown row refuses by name, which is the only legible answer when a
        /// placement's entry has gone stale.</summary>
        [Test]
        public void AnUnknownRow_RefusesByName()
        {
            CutsceneResult result = m_Director.Play("no-such-scene", "", m_Player.gameObject);

            Assert.That(result.refusal, Does.Contain("no-such-scene"));
            Assert.That(m_Director.playing, Is.Null);
        }

        private StateTreeContextHost MakeHost(string hostName, StateTreeContextKind kind,
            Transform parent)
        {
            var go = new GameObject(hostName);
            go.hideFlags = HideFlags.HideAndDontSave;
            if (parent != null)
                go.transform.SetParent(parent);
            m_Objects.Add(go);

            var host = go.AddComponent<StateTreeContextHost>();
            host.kind = kind;
            host.autoStart = false;
            host.Register();
            m_Hosts.Add(host);
            return host;
        }

        private WorldObjectBehaviour MakeCitizen(string citizenName, Vector3 position,
            params string[] tags)
        {
            var go = new GameObject(citizenName);
            go.hideFlags = HideFlags.HideAndDontSave;
            go.transform.SetParent(m_Level.transform);
            go.transform.position = position;
            go.SetActive(false);
            m_Objects.Add(go);

            var citizen = go.AddComponent<WorldObjectBehaviour>();
            citizen.tags.AddRange(tags);
            citizen.RegisterToWorld();
            return citizen;
        }

        private StubCastWitnessTask MakeWitness(string roleKey)
        {
            var task = ScriptableObject.CreateInstance<StubCastWitnessTask>();
            task.name = "Witness_" + roleKey;
            task.roleKey = roleKey;
            task.finishOnTick = 0;
            m_Assets.Add(task);
            return task;
        }

        private CutsceneDef MakeScene(string sceneName, StateTreeTaskAsset beat,
            params CutsceneRole[] cast)
        {
            var node = ScriptableObject.CreateInstance<StateTreeNodeAsset>();
            node.name = sceneName;
            node.nodeId = sceneName;
            node.displayName = sceneName;
            node.tasks.Add(beat);
            m_Assets.Add(node);

            var tree = ScriptableObject.CreateInstance<StateTreeAsset>();
            tree.name = "Scene_" + sceneName;
            tree.treeName = sceneName;
            tree.root = node;
            m_Assets.Add(tree);

            var row = new CutsceneDef
            {
                id = "cutscene." + sceneName,
                name = sceneName,
                displayName = sceneName,
                beats = tree
            };
            for (int i = 0; i < cast.Length; i++)
                row.cast.Add(cast[i]);
            m_Cutscenes.entries.Add(row);
            return row;
        }

        private static CutsceneRole Role(string role, string tag, bool required)
        {
            return new CutsceneRole { role = role, tag = tag, required = required };
        }

        /// <summary>What the running script wrote down. Read from the DIRECTOR HOST's own
        /// context, because the executor deep-copies the tree and the authored tasks are not
        /// the ones that ran.</summary>
        private string[] ScriptLog()
        {
            Transform stage = FindDirectorHost();
            var host = stage != null ? stage.GetComponent<StateTreeContextHost>() : null;
            return host != null
                ? StateTreeTestLog.Get(host.Context).ToArray()
                : new string[0];
        }

        private Transform FindDirectorHost()
        {
            for (int i = 0; i < m_Level.transform.childCount; i++)
            {
                Transform child = m_Level.transform.GetChild(i);
                if (child.name.StartsWith("Cutscene · "))
                    return child;
            }
            return null;
        }
    }
}
