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

        [Test]
        public void TreeNesting_FollowsTheServiceRules()
        {
            // The HT ability editor's law, on states — reviewed twice into this exact chain:
            // from the ROOT you create an ABILITY, on the ability an effect, on the effect a
            // cue, and a cue is a leaf.
            m_Def.nestingRules.Add(new ServiceNestingRule
            {
                parentKind = ServiceDef.TreeRootKind,
                childKinds = new List<string> { "ability" }
            });
            m_Def.nestingRules.Add(new ServiceNestingRule
            {
                parentKind = "ability", childKinds = new List<string> { "effect" }
            });
            m_Def.nestingRules.Add(new ServiceNestingRule
            {
                parentKind = "effect", childKinds = new List<string> { "cue" }
            });

            StateTreeAsset tree = MakeResidentTree();
            StateTreeNodeAsset root = tree.root;

            var abilityState = MakeNode("strike");
            abilityState.roleKind = "ability";
            root.children.Add(abilityState);
            var effect = MakeNode("land");
            effect.roleKind = "effect";
            abilityState.children.Add(effect);
            var cue = MakeNode("flash");
            cue.roleKind = "cue";
            effect.children.Add(cue);

            var problems = new List<string>();
            AbilityRules.ValidateTree(m_Def, tree, problems);
            CollectionAssert.IsEmpty(problems,
                "root → ability → effect → cue as STATES is the lawful shape");

            // An effect directly under the ROOT: the root creates abilities, nothing else.
            var strayEffect = MakeNode("strayLand");
            strayEffect.roleKind = "effect";
            root.children.Add(strayEffect);
            AbilityRules.ValidateTree(m_Def, tree, problems);
            Assert.AreEqual(1, problems.Count, "an 'effect' cannot sit under 'root'");
            StringAssert.Contains("cannot sit under", problems[0]);

            // Anything under a cue state: a leaf is a leaf, plain grouping included.
            problems.Clear();
            root.children.Remove(strayEffect);
            cue.children.Add(MakeNode("underLeaf"));
            AbilityRules.ValidateTree(m_Def, tree, problems);
            Assert.AreEqual(1, problems.Count, "a leaf kind admits nothing beneath it");
            StringAssert.Contains("leaf", problems[0]);

            // Two ability states in one tree: legal by the nesting rules alone, but this
            // project's cut is ONE tree has ONE ability — a declared finding.
            problems.Clear();
            cue.children.Clear();
            var second = MakeNode("second");
            second.roleKind = "ability";
            root.children.Add(second);
            AbilityRules.ValidateTree(m_Def, tree, problems);
            Assert.AreEqual(1, problems.Count, "one tree has one ability");
            StringAssert.Contains("ONE ability", problems[0]);
        }

        private StateTreeNodeAsset MakeNode(string nodeId)
        {
            var node = ScriptableObject.CreateInstance<StateTreeNodeAsset>();
            node.nodeId = nodeId;
            node.completeWhen = StateTreeCompleteWhen.Never;
            m_Assets.Add(node);
            return node;
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

        [Test]
        public void ActivationPayload_LandsUnderTheDeclaredKey_ByWire()
        {
            // The mind searched; the activation hands the result over; the ability tree
            // finds it under ITS declared key — and the row's field being wired by id means
            // the declaration's CURRENT name wins over the field's stale text.
            StateTreeAsset tree = MakeResidentTree();
            tree.keys.Add(new StateTreeKeyDeclaration
            {
                id = "key.victim", name = "victim", kind = StateTreeKeyKind.String
            });
            AbilityDef ambush = MakeAbility("ambush", tree: tree);
            ambush.targetKey = new StateTreeKeyField("renamed-away") { keyId = "key.victim" };

            var prey = new GameObject("prey");
            prey.hideFlags = HideFlags.HideAndDontSave;
            prey.SetActive(false);
            m_Objects.Add(prey);

            AbilityHost hunter = MakeActor("hunter");
            Assert.IsTrue(hunter.Activate(ambush, prey));
            Assert.AreSame(prey, hunter.activeContext.blackboard["victim"],
                "the payload landed under the DECLARATION'S name, not the field's stale text");
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
        public void CueAspect_Source_ShowsAtTheCaster_NotTheVictim()
        {
            // The review's question: from a tree the task's variable aims a cue — from the
            // registry, the effect row's ASPECT does. Source shows at whoever applied it.
            var template = new GameObject("FlashTemplate");
            template.hideFlags = HideFlags.HideAndDontSave;
            template.SetActive(false);
            m_Objects.Add(template);

            var flash = new CueDef
            {
                id = "cue.flash", name = "flash", prefab = template,
                secondsAlive = 0.2f, attachToTarget = true
            };
            m_Cues.entries.Add(flash);

            EffectDef drain = MakeEffect("drain", magnitude: -1f);
            drain.cue.entryName = "flash";
            drain.cueAspect = AbilityCueAspect.Source;

            AbilityHost caster = MakeActor("caster");
            AbilityHost victim = MakeActor("drained", withHealth: true);

            victim.ApplyEffect(drain, caster.gameObject);

            Assert.AreEqual(1, caster.transform.childCount,
                "a Source-aspect cue attaches at the CASTER — the drain's glow");
            Assert.AreEqual(0, victim.transform.childCount,
                "and not at the victim, who only takes the magnitude");
        }

        [Test]
        public void BlockedByTags_GateApplication_AndExpiryReopensIt()
        {
            // The old i-frame window as DATA: a hit row refused while the target is Guarded.
            EffectDef hit = MakeEffect("gated-hit", magnitude: -3f);
            hit.attribute.entryId = "attribute.stamina";
            hit.attribute.entryName = "stamina";
            hit.blockedByTags.Add("Guarded");

            EffectDef guard = MakeEffect("guard", magnitude: 0f);
            guard.duration = AbilityEffectDuration.Duration;
            guard.seconds = 0.35f;
            guard.grantedTags.Add("Guarded");

            AbilityHost victim = MakeActor("gated");
            var vitals = victim.gameObject.AddComponent<AttributeComponent>();
            vitals.Ensure("stamina", 10f);

            Assert.IsTrue(victim.ApplyEffect(hit), "an unguarded target takes the hit");
            Assert.AreEqual(7f, vitals.Value("stamina"), 0.001f);

            victim.ApplyEffect(guard);
            Assert.IsFalse(victim.ApplyEffect(hit), "refused at the door while Guarded");
            Assert.AreEqual(7f, vitals.Value("stamina"), 0.001f,
                "no magnitude landed through the gate");

            victim.Tick(0.4f);
            Assert.IsTrue(victim.ApplyEffect(hit), "the guard expired; the door is open");
            Assert.AreEqual(4f, vitals.Value("stamina"), 0.001f);
        }

        [Test]
        public void ABlockedApplication_ShowsNoCue()
        {
            var flash = new CueDef { id = "cue.flash2", name = "flash2", secondsAlive = 0.2f };
            m_Cues.entries.Add(flash);

            EffectDef hit = MakeEffect("cued-hit", magnitude: 0f);
            hit.cue.entryName = "flash2";
            hit.blockedByTags.Add("Guarded");

            EffectDef guard = MakeEffect("guard2", magnitude: 0f);
            guard.duration = AbilityEffectDuration.Duration;
            guard.seconds = 1f;
            guard.grantedTags.Add("Guarded");

            AbilityHost victim = MakeActor("uncued");
            victim.ApplyEffect(guard);
            var cues = 0;
            victim.cueFired += (_, _2) => cues++;
            victim.ApplyEffect(hit);
            Assert.AreEqual(0, cues, "a hit that did not land shows nothing");
        }

        [Test]
        public void ARunningStatus_TicksThroughTheGate()
        {
            // The gate is at the DOOR only — a poison already inside keeps draining while
            // the target guards (the TickDamage rule, kept).
            EffectDef poison = MakeEffect("gate-poison", magnitude: -1f);
            poison.attribute.entryId = "attribute.stamina";
            poison.attribute.entryName = "stamina";
            poison.duration = AbilityEffectDuration.Duration;
            poison.seconds = 10f;
            poison.tickInterval = 1f;
            poison.blockedByTags.Add("Guarded");

            EffectDef guard = MakeEffect("guard3", magnitude: 0f);
            guard.duration = AbilityEffectDuration.Duration;
            guard.seconds = 10f;
            guard.grantedTags.Add("Guarded");

            AbilityHost victim = MakeActor("poisoned3");
            var vitals = victim.gameObject.AddComponent<AttributeComponent>();
            vitals.Ensure("stamina", 10f);

            victim.ApplyEffect(poison);   // lands -1 on application
            victim.ApplyEffect(guard);
            victim.Tick(1f);
            Assert.AreEqual(8f, vitals.Value("stamina"), 0.001f,
                "application -1, then one tick -1 THROUGH the guard");
            Assert.IsFalse(victim.ApplyEffect(poison),
                "but a fresh application is still refused at the door");
        }

        [Test]
        public void HasTagCondition_ReadsTheOwnersStatusTags()
        {
            EffectDef venom = MakeEffect("cond-venom", magnitude: 0f);
            venom.duration = AbilityEffectDuration.Duration;
            venom.seconds = 1f;
            venom.grantedTags.Add("Poisoned");

            AbilityHost owner = MakeActor("conditioned");
            var condition = ScriptableObject.CreateInstance<HasTagCondition>();
            condition.tag = "Poisoned";
            m_Assets.Add(condition);
            typeof(HasTagCondition)
                .GetField("m_Owner", System.Reflection.BindingFlags.NonPublic
                    | System.Reflection.BindingFlags.Instance)
                .SetValue(condition, owner);

            Assert.IsFalse(condition.Evaluate(null), "no status, no tag");
            owner.ApplyEffect(venom);
            Assert.IsTrue(condition.Evaluate(null), "the status holds the tag");
            condition.absent = true;
            Assert.IsFalse(condition.Evaluate(null));
            owner.Tick(1.1f);
            condition.absent = false;
            Assert.IsFalse(condition.Evaluate(null), "expiry took the tag with it");
        }

        [Test]
        public void ScaledEffect_HitsAtTheSourcesLevel()
        {
            // The ScalableFloat half of progression: the row's magnitude times its picked
            // curve at the SOURCE's level — resolved through the same dependsOn closure as
            // every other reference.
            var progression = ScriptableObject.CreateInstance<ProgressionTable>();
            m_Assets.Add(progression);
            var power = new ProgressionRow
            {
                id = "progress.power", name = "power",
                valueByLevel = new AnimationCurve(new Keyframe(1f, 1f), new Keyframe(5f, 2f)),
                wholeNumbers = true
            };
            power.attribute.entryName = "power";
            progression.entries.Add(power);
            m_Effects.dependsOn.Add(progression);

            EffectDef bite = MakeEffect("bite", magnitude: -1f);
            bite.scaleByLevel.entryName = "power";

            AbilityHost caster = MakeActor("veteran");
            var casterAttributes = caster.gameObject.AddComponent<AttributeComponent>();
            casterAttributes.level = 5;

            AbilityHost victim = MakeActor("bitten", withHealth: true);
            victim.ApplyEffect(bite, caster.gameObject);
            Assert.AreEqual(3f, victim.GetComponent<HealthComponent>().hp, 0.001f,
                "-1 at power 2: the same row hits harder because the SOURCE is level 5");
        }

        [Test]
        public void ScaledEffect_PrefersTheSourcesOwnSheet()
        {
            // The world's shared line says power is 1 at level 1 — but the boss carries
            // its own sheet where the same line reads 3. The effect row picks WHICH line;
            // the striker's sheet says what it means where they stand.
            var shared = ScriptableObject.CreateInstance<ProgressionTable>();
            m_Assets.Add(shared);
            var worldPower = new ProgressionRow
            {
                id = "progress.power", name = "power",
                valueByLevel = new AnimationCurve(new Keyframe(1f, 1f), new Keyframe(5f, 2f))
            };
            worldPower.attribute.entryName = "power";
            shared.entries.Add(worldPower);
            m_Effects.dependsOn.Add(shared);

            var bossSheet = ScriptableObject.CreateInstance<ProgressionTable>();
            m_Assets.Add(bossSheet);
            var bossPower = new ProgressionRow
            {
                id = "boss.power", name = "power",
                valueByLevel = new AnimationCurve(new Keyframe(1f, 3f))
            };
            bossPower.attribute.entryName = "power";
            bossSheet.entries.Add(bossPower);

            EffectDef bite = MakeEffect("bite", magnitude: -1f);
            bite.scaleByLevel.entryName = "power";

            AbilityHost boss = MakeActor("boss");
            var bossAttributes = boss.gameObject.AddComponent<AttributeComponent>();
            bossAttributes.table = bossSheet;
            bossAttributes.level = 1;

            AbilityHost victim = MakeActor("mauled", withHealth: true);
            victim.ApplyEffect(bite, boss.gameObject);
            Assert.AreEqual(2f, victim.GetComponent<HealthComponent>().hp, 0.001f,
                "-3, the boss's own line — not -1, the shared world scale's");
        }

        [Test]
        public void ScaledStatus_SnapshotsItsStrength_WhenItLands()
        {
            var progression = ScriptableObject.CreateInstance<ProgressionTable>();
            m_Assets.Add(progression);
            var power = new ProgressionRow
            {
                id = "progress.power", name = "power",
                valueByLevel = new AnimationCurve(new Keyframe(1f, 1f), new Keyframe(5f, 2f)),
                wholeNumbers = true
            };
            power.attribute.entryName = "power";
            progression.entries.Add(power);
            m_Effects.dependsOn.Add(progression);

            EffectDef venom = MakeEffect("venom", magnitude: -1f);
            venom.scaleByLevel.entryName = "power";
            venom.duration = AbilityEffectDuration.Duration;
            venom.seconds = 10f;
            venom.tickInterval = 1f;

            AbilityHost caster = MakeActor("snake");
            var casterAttributes = caster.gameObject.AddComponent<AttributeComponent>();
            casterAttributes.level = 5;

            AbilityHost victim = MakeActor("poisoned2", withHealth: true);
            victim.ApplyEffect(venom, caster.gameObject);   // lands -2 (power 2 at level 5)
            Assert.AreEqual(3f, victim.GetComponent<HealthComponent>().hp, 0.001f);

            casterAttributes.level = 1;   // the snake got weaker AFTER the bite
            victim.Tick(1f);
            Assert.AreEqual(1f, victim.GetComponent<HealthComponent>().hp, 0.001f,
                "the tick still drains -2 — a landed status keeps the strength it landed "
                + "with (the snapshot rule)");
        }

        [Test]
        public void ModifierEffect_GrantsWhileAlive_RevertsOnExpiry_RecomputesOnStack()
        {
            // The GAS step: a Duration effect can carry a revertible MODIFIER instead of a
            // delta — applied on application, resized when stacks change, gone on expiry.
            EffectDef haste = MakeEffect("haste", magnitude: 2f);
            haste.attribute.entryId = "attribute.speed";
            haste.attribute.entryName = "speed";
            haste.operation = EffectOperation.Modifier;
            haste.duration = AbilityEffectDuration.Duration;
            haste.seconds = 1f;
            haste.maxStacks = 2;
            haste.stacking = AbilityStacking.AddStacksRefreshDuration;

            AbilityHost runner = MakeActor("runner");
            var attributes = runner.gameObject.AddComponent<AttributeComponent>();
            attributes.Ensure("speed", 10f);

            runner.ApplyEffect(haste);
            Assert.AreEqual(12f, attributes.Effective("speed"), 0.001f,
                "one stack grants one additive step");

            runner.ApplyEffect(haste);
            Assert.AreEqual(14f, attributes.Effective("speed"), 0.001f,
                "a second stack RECOMPUTES the grant — no drift from regranting on top");

            runner.Tick(1.1f);
            Assert.AreEqual(10f, attributes.Effective("speed"), 0.001f,
                "expiry reverts the whole grant — the modifier contract");
        }

        [Test]
        public void InstantModifier_IsRefused_NothingGranted()
        {
            // An instant modifier would be granted with no expiry to revert it — the host
            // refuses loudly instead of leaking a permanent buff.
            EffectDef trap = MakeEffect("trap", magnitude: 5f);
            trap.attribute.entryId = "attribute.speed";
            trap.attribute.entryName = "speed";
            trap.operation = EffectOperation.Modifier;
            trap.duration = AbilityEffectDuration.Instant;

            AbilityHost actor = MakeActor("trapped");
            var attributes = actor.gameObject.AddComponent<AttributeComponent>();
            attributes.Ensure("speed", 10f);

            UnityEngine.TestTools.LogAssert.Expect(LogType.Warning,
                new System.Text.RegularExpressions.Regex("Instant Modifier"));
            actor.ApplyEffect(trap);
            Assert.AreEqual(10f, attributes.Effective("speed"), 0.001f);
        }

        [Test]
        public void DeltaEffect_OnANonHealthAttribute_MovesThePool()
        {
            // A delta on any attribute the target carries lands directly on the component —
            // health is only special for its rulekeeper.
            EffectDef exhaust = MakeEffect("exhaust", magnitude: -3f);
            exhaust.attribute.entryId = "attribute.stamina";
            exhaust.attribute.entryName = "stamina";

            AbilityHost actor = MakeActor("tired");
            var attributes = actor.gameObject.AddComponent<AttributeComponent>();
            attributes.Ensure("stamina", 10f);

            actor.ApplyEffect(exhaust);
            Assert.AreEqual(7f, attributes.Value("stamina"), 0.001f,
                "negative consumed; the sign convention health uses holds everywhere");
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
            // The attribute is a PICKED row (M23 attributes) — the tests' effects land on
            // health unless a test rewires them.
            def.attribute.entryId = "attribute.health";
            def.attribute.entryName = HealthComponent.AttributeName;
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
