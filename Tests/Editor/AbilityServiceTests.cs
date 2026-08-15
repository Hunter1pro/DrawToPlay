using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace PowerOfFire.DrawToPlay.Tests
{
    /// <summary>
    /// M23 (brief §10.3, reworked on review): the ability ServiceDef — effects and cues as
    /// PICKED registry rows chained by dependsOn (abilities → effects → cues), application as
    /// a TASK on the ability's tree with a declared target (self, or whoever the swing
    /// published), the four-tag activation gates, the idle floor, the nextOnFinish
    /// continuation riding M22's treeFinished, and the one-ability-one-tree validation.
    ///
    /// EditMode ground rules: actor GameObjects stay INACTIVE so no Unity message runs behind
    /// the tests' back — the service connects explicitly, the host ticks explicitly.
    /// </summary>
    [TestFixture]
    public sealed class AbilityServiceTests
    {
        private readonly List<GameObject> m_Objects = new List<GameObject>();
        private readonly List<ScriptableObject> m_Assets = new List<ScriptableObject>();
        private readonly List<StateTreeContextHost> m_Hosts = new List<StateTreeContextHost>();

        private StateTreeContextHost m_Level;
        private AbilityService m_Service;
        private AbilityRegistry m_Registry;
        private EffectRegistry m_Effects;
        private CueRegistry m_Cues;
        private ServiceDef m_Def;

        [SetUp]
        public void SetUp()
        {
            m_Cues = ScriptableObject.CreateInstance<CueRegistry>();
            m_Assets.Add(m_Cues);

            m_Effects = ScriptableObject.CreateInstance<EffectRegistry>();
            m_Effects.dependsOn.Add(m_Cues);
            m_Assets.Add(m_Effects);

            m_Registry = ScriptableObject.CreateInstance<AbilityRegistry>();
            m_Registry.dependsOn.Add(m_Effects);
            m_Assets.Add(m_Registry);

            m_Def = ScriptableObject.CreateInstance<ServiceDef>();
            m_Def.serviceName = "abilities";
            m_Def.scope = StateTreeContextKind.Level;
            m_Def.registry = m_Registry;
            m_Def.treeKind = "ability";
            m_Assets.Add(m_Def);

            var levelGo = new GameObject("Level");
            levelGo.hideFlags = HideFlags.HideAndDontSave;
            m_Objects.Add(levelGo);
            m_Level = levelGo.AddComponent<StateTreeContextHost>();
            m_Level.kind = StateTreeContextKind.Level;
            m_Level.autoStart = false;
            m_Level.Register();
            m_Hosts.Add(m_Level);

            m_Service = levelGo.AddComponent<AbilityService>();
            m_Service.definition = m_Def;
            m_Service.Connect();
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

        // ------------------------------------------------------------------ 1. validation

        [Test]
        public void Validate_TreeOfAnotherKind_IsReported()
        {
            AbilityDef stray = MakeAbility("stray", tree: MakeResidentTree());
            stray.tree.treeKind = "npc";   // an ability row pointing at somebody's mind

            var problems = new List<string>();
            AbilityRules.Validate(m_Def, stray, problems);
            Assert.AreEqual(1, problems.Count,
                "one ability is one ability tree — a row naming another domain's tree is a "
                + "finding");
            StringAssert.Contains("not 'ability'", problems[0]);

            problems.Clear();
            AbilityDef lawful = MakeAbility("lawful", tree: MakeResidentTree());
            AbilityRules.Validate(m_Def, lawful, problems);
            CollectionAssert.IsEmpty(problems);
        }

        // -------------------------------------------------------------------- 2. tag gates

        [Test]
        public void BlockTags_SuppressWhileActive_CancelTags_Replace()
        {
            AbilityDef guard = MakeAbility("guard", tree: MakeResidentTree());
            guard.abilityTags.Add("Stance");
            guard.blockTags.Add("Attack");

            AbilityDef swing = MakeAbility("swing");
            swing.abilityTags.Add("Attack");

            AbilityDef stagger = MakeAbility("stagger");
            stagger.cancelTags.Add("Stance");

            AbilityHost host = MakeActor("gates");
            Assert.IsTrue(host.Activate(guard));
            Assert.IsFalse(host.Activate(swing),
                "the active stance's block tags name 'Attack' — refused");
            Assert.AreSame(guard, host.active);

            var log = new List<string>();
            host.abilityFinished += (def, status) => log.Add(def.name + ":" + status);
            Assert.IsTrue(host.Activate(stagger),
                "cancel tags naming the active's ability tags replace it");
            CollectionAssert.Contains(log, "guard:Cancelled");
        }

        [Test]
        public void Cooldown_RefusesReactivation()
        {
            AbilityDef burst = MakeAbility("burst");
            burst.cooldownSeconds = 60f;

            AbilityHost host = MakeActor("cooldown");
            Assert.IsTrue(host.Activate(burst), "first activation is free");
            Assert.IsFalse(host.Activate(burst), "sixty seconds have not passed");
        }

        // ----------------------------------------------------------------------- 3. the floor

        [Test]
        public void Floor_DefaultAbilityActivates_WhenNothingIs()
        {
            AbilityDef idle = MakeAbility("idle", tree: MakeResidentTree());
            AbilityHost host = MakeActor("floor");
            host.defaultAbility.entryName = "idle";

            Assert.IsNull(host.active);
            host.Tick(0.1f);
            Assert.AreSame(idle, host.active,
                "nothing active means the default — the idle floor, not nothing");
        }

        // --------------------------------------------------------------- 4. the continuation

        [Test]
        public void NextOnFinish_ChainsWithZeroCode()
        {
            AbilityDef recover = MakeAbility("recover", tree: MakeResidentTree());
            AbilityDef swing = MakeAbility("swing", tree: MakeOneShotTree());
            swing.nextOnFinish.entryName = "recover";

            AbilityHost host = MakeActor("chain");
            var log = new List<string>();
            host.abilityFinished += (def, status) => log.Add(def.name + ":" + status);

            Assert.IsTrue(host.Activate(swing));
            host.Tick(0.1f);   // the one-shot tree finishes (M22 runs it off its end)
            host.Tick(0.1f);   // the continuation's one-hop-per-frame lands
            Assert.AreSame(recover, host.active,
                "a combo is a wire between two rows — no code anywhere");
            CollectionAssert.Contains(log, "swing:Success");
        }

        // ------------------------------------------------- 5. effects as rows, with targets

        [Test]
        public void InstantEffect_Row_LandsOnTheTargetsHealth()
        {
            EffectDef bite = MakeEffect("bite", magnitude: -1f);
            AbilityHost victim = MakeActor("victim", withHealth: true);
            var health = victim.GetComponent<HealthComponent>();
            float before = health.hp;

            victim.ApplyEffect(bite);
            Assert.AreEqual(before - 1f, health.hp, 0.001f,
                "negative magnitude is damage — the HT sign convention, on the TARGET");
        }

        [Test]
        public void DurationEffect_Row_Stacks_GrantsTags_Expires()
        {
            EffectDef venom = MakeEffect("venom", magnitude: 0f);
            venom.duration = AbilityEffectDuration.Duration;
            venom.seconds = 1f;
            venom.maxStacks = 3;
            venom.stacking = AbilityStacking.AddStacksRefreshDuration;
            venom.grantedTags.Add("Poisoned");

            AbilityHost victim = MakeActor("poisoned");
            victim.ApplyEffect(venom);
            victim.ApplyEffect(venom);
            Assert.AreEqual(2, victim.StacksOf("venom"), "re-application stacked");
            Assert.IsTrue(victim.HasTag("Poisoned"), "the status holds its granted tag");

            victim.Tick(1.1f);
            Assert.AreEqual(0, victim.StacksOf("venom"), "the duration ran out");
            Assert.IsFalse(victim.HasTag("Poisoned"));
        }

        [Test]
        public void Cue_IsAPickedRow_AndFiresOnApplication()
        {
            var flash = new CueDef { id = "cue.flash", name = "flash", secondsAlive = 0.2f };
            m_Cues.entries.Add(flash);

            EffectDef hit = MakeEffect("hit", magnitude: 0f);
            hit.cue.entryId = "cue.flash";
            hit.cue.entryName = "flash";

            AbilityHost shown = MakeActor("shown");
            var cues = new List<string>();
            shown.cueFired += (cue, effect) => cues.Add(effect.name + ":" + cue.name);

            shown.ApplyEffect(hit);
            CollectionAssert.AreEqual(new[] { "hit:flash" }, cues,
                "the cue arrived as the ROW the effect picked, not a string that hoped");
        }

        [Test]
        public void ApplyEffectTask_FromBlackboard_LandsOnTheStruckActor()
        {
            EffectDef row = MakeEffect("strike-hit", magnitude: -1f);
            AbilityHost attacker = MakeActor("attacker");
            AbilityHost victim = MakeActor("victim2", withHealth: true);
            float before = victim.GetComponent<HealthComponent>().hp;

            var world = m_Level.gameObject.AddComponent<WorldService>();
            world.Connect();
            attacker.RegisterToWorld();

            var apply = ScriptableObject.CreateInstance<ApplyEffectTask>();
            apply.effect.entryName = "strike-hit";
            apply.target = ApplyEffectTask.Target.FromBlackboard;
            m_Assets.Add(apply);

            var node = ScriptableObject.CreateInstance<StateTreeNodeAsset>();
            node.nodeId = "land";
            node.tasks.Add(apply);
            m_Assets.Add(node);
            var tree = ScriptableObject.CreateInstance<StateTreeAsset>();
            tree.treeName = "LandTree";
            tree.root = node;
            tree.registries.Add(m_Effects);
            m_Assets.Add(tree);

            var mind = new StateTreeExecutor
            {
                data = tree,
                owner = attacker.gameObject,
                context = new StateTreeContext(attacker.gameObject)
            };
            mind.context.blackboard["struck"] = victim.gameObject;
            mind.StartTree();
            mind.TickTree(0.1f);
            mind.StopTree();

            Assert.AreEqual(before - 1f, victim.GetComponent<HealthComponent>().hp, 0.001f,
                "the swing published WHO; the task landed the picked row on exactly them");
        }

        // ------------------------------------------------------------------------ 6. the task

        [Test]
        public void ActivateAbilityTask_WaitsForFinish_ThenTheStateMovesOn()
        {
            MakeAbility("shove", tree: MakeOneShotTree());

            AbilityHost host = MakeActor("actor");

            // [InjectOwner] is a WORLD contract — the owner must be a citizen the world knows,
            // with the host found as its sibling facet (the M21 composed-object rule).
            var world = m_Level.gameObject.AddComponent<WorldService>();
            world.Connect();
            host.RegisterToWorld();

            var task = ScriptableObject.CreateInstance<ActivateAbilityTask>();
            task.ability.entryName = "shove";
            task.waitForFinish = true;
            m_Assets.Add(task);

            var call = ScriptableObject.CreateInstance<StateTreeNodeAsset>();
            call.nodeId = "call";
            call.tasks.Add(task);
            m_Assets.Add(call);
            var after = ScriptableObject.CreateInstance<StateTreeNodeAsset>();
            after.nodeId = "after";
            after.completeWhen = StateTreeCompleteWhen.Never;
            m_Assets.Add(after);
            var root = ScriptableObject.CreateInstance<StateTreeNodeAsset>();
            root.nodeId = "root";
            root.children.Add(call);
            root.children.Add(after);
            m_Assets.Add(root);
            var tree = ScriptableObject.CreateInstance<StateTreeAsset>();
            tree.treeName = "MindTree";
            tree.root = root;
            // The DATA this tree speaks: entry refs on its tasks resolve against listed
            // registries — the same rule as every typed reference (M13).
            tree.registries.Add(m_Registry);
            m_Assets.Add(tree);

            var mind = new StateTreeExecutor
            {
                data = tree,
                owner = host.gameObject,
                context = new StateTreeContext(host.gameObject)
            };
            mind.StartTree();
            Assert.AreEqual("call", mind.activeNodeId);

            for (int i = 0; i < 4 && mind.activeNodeId == "call"; i++)
            {
                mind.TickTree(0.1f);
                host.Tick(0.1f);
            }

            Assert.AreEqual("after", mind.activeNodeId,
                "the ability ran, finished, and the mind-state moved on — M22's sequence flow "
                + "carrying M23's bridge");
            mind.StopTree();
        }

        // ------------------------------------------------------------------------- helpers

        private AbilityDef MakeAbility(string abilityName, StateTreeAsset tree = null)
        {
            var def = new AbilityDef
            {
                id = "ability." + abilityName,
                name = abilityName,
                tree = tree
            };
            m_Registry.entries.Add(def);
            return def;
        }

        private EffectDef MakeEffect(string effectName, float magnitude)
        {
            var def = new EffectDef
            {
                id = "effect." + effectName,
                name = effectName,
                magnitude = magnitude
            };
            m_Effects.entries.Add(def);
            return def;
        }

        /// <summary>A tree that finishes on its first tick — one state, one instant task,
        /// zero edges: M22's implicit flow runs it off its end.</summary>
        private StateTreeAsset MakeOneShotTree()
        {
            var task = ScriptableObject.CreateInstance<StubRecordingTask>();
            task.taskId = "act";
            task.finishOnTick = 1;
            m_Assets.Add(task);
            return MakeTreeWith(task, StateTreeCompleteWhen.AllTasks);
        }

        /// <summary>A tree that never finishes — a resident state, the shape of idle.</summary>
        private StateTreeAsset MakeResidentTree()
        {
            var task = ScriptableObject.CreateInstance<StubRecordingTask>();
            task.taskId = "idle";
            m_Assets.Add(task);
            return MakeTreeWith(task, StateTreeCompleteWhen.Never);
        }

        private StateTreeAsset MakeTreeWith(StateTreeTaskAsset task, StateTreeCompleteWhen when)
        {
            var node = ScriptableObject.CreateInstance<StateTreeNodeAsset>();
            node.nodeId = "only";
            node.completeWhen = when;
            node.tasks.Add(task);
            m_Assets.Add(node);
            var tree = ScriptableObject.CreateInstance<StateTreeAsset>();
            tree.treeName = "AbilityTree";
            tree.treeKind = "ability";
            tree.root = node;
            m_Assets.Add(tree);
            return tree;
        }

        private AbilityHost MakeActor(string actorName, bool withHealth = false)
        {
            var go = new GameObject(actorName);
            go.hideFlags = HideFlags.HideAndDontSave;
            go.transform.SetParent(m_Level.transform);
            go.SetActive(false);
            m_Objects.Add(go);
            if (withHealth)
            {
                var health = go.AddComponent<HealthComponent>();
                health.maxHP = 5f;
                health.ResetHealth();
            }
            return go.AddComponent<AbilityHost>();
        }
    }
}
