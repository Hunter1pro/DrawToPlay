using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using PowerOfFire.DrawToPlay.Editor;
using UnityEditor;
using UnityEngine;

namespace PowerOfFire.DrawToPlay.Tests
{
    /// <summary>A service that announces on demand — the clock's shape, without a day.</summary>
    internal sealed class CrierService : StateTreeService
    {
        public const string Key = "crier.called";

        public CrierService(StateTreeContextHost scope, ServiceDef definition) : base(scope, definition)
        {
        }

        public void Cry(object payload)
        {
            Announce(Key, payload);
        }
    }

    /// <summary>
    /// M38.1 — the graph as the project's declared API.
    ///
    /// The graph assembly is firewalled from the tests (Graph Toolkit is experimental), so what
    /// is pinned here is the RUNTIME half the nodes bake to — the announcement serial and the
    /// condition that fires once per step of it — and the baked programs of the two graphs the
    /// waystation authors by picking, read back through the runtime types they bake into.
    /// </summary>
    [TestFixture]
    public sealed class DeclaredApiGraphTests
    {
        private readonly List<UnityEngine.Object> m_Junk = new List<UnityEngine.Object>();
        private StateTreeContextHost m_Root;

        [SetUp]
        public void SetUp()
        {
            var go = new GameObject("Root") { hideFlags = HideFlags.HideAndDontSave };
            m_Junk.Add(go);
            m_Root = go.AddComponent<StateTreeContextHost>();
            m_Root.kind = StateTreeContextKind.Root;
            m_Root.autoStart = false;
            m_Root.Register();
        }

        [TearDown]
        public void TearDown()
        {
            if (m_Root != null)
                m_Root.Unregister();
            for (int i = 0; i < m_Junk.Count; i++)
            {
                if (m_Junk[i] != null)
                    UnityEngine.Object.DestroyImmediate(m_Junk[i]);
            }
            m_Junk.Clear();
        }

        [Test]
        public void AnAnnouncementFiresItsListenerOnce_PerAnnouncement_AndLeavesThePayload()
        {
            var crier = new CrierService(m_Root, null);
            var condition = ScriptableObject.CreateInstance<AnnouncementCondition>();
            m_Junk.Add(condition);
            condition.key = CrierService.Key;
            condition.scope = StateTreeContextKind.Root;
            var context = new StateTreeContext(m_Root.gameObject);

            Assert.That(condition.Evaluate(context), Is.False, "nothing announced yet");

            crier.Cry(6.0f);
            Assert.That(m_Root.Context.blackboard[CrierService.Key], Is.EqualTo(6.0f),
                "the payload stays on the key for whoever reads it");
            Assert.That(m_Root.Context.blackboard[StateTreeService.AnnouncementSerialKey(CrierService.Key)],
                Is.EqualTo(1), "and the announcement has a number beside it");

            // A LISTENER ALIVE BEFORE THE FIRST ANNOUNCEMENT hears the first one.
            Assert.That(condition.Evaluate(context), Is.True, "the first dawn is heard");
            Assert.That(condition.Evaluate(context), Is.False, "and not again while it stands");
            crier.Cry(6.1f);
            Assert.That(condition.Evaluate(context), Is.True, "once, when the serial moves");
            Assert.That(condition.Evaluate(context), Is.False, "and not again while it stands");
            Assert.That(condition.Evaluate(context), Is.False);

            crier.Cry(6.1f);
            Assert.That(condition.Evaluate(context), Is.True,
                "the same payload twice is two announcements — the serial says so, the payload could not");

            // A LISTENER THAT STARTS AFTER THREE ANNOUNCEMENTS has not just heard one: its first
            // look adopts. And two listeners, neither consuming, both hear the next.
            var other = ScriptableObject.CreateInstance<AnnouncementCondition>();
            m_Junk.Add(other);
            other.key = CrierService.Key;
            Assert.That(other.Evaluate(context), Is.False, "adopted, not fired");
            crier.Cry(7f);
            Assert.That(condition.Evaluate(context), Is.True);
            Assert.That(other.Evaluate(context), Is.True);
        }

        [Test]
        public void TheKeepersGift_BakedFromAnAsk_IsTheBagsDeclaredRequest()
        {
            UnityEngine.Object[] parts = AssetDatabase.LoadAllAssetsAtPath(
                "Assets/DrawToPlayExamples/Demo/M21/Dialogs/M21Dialog_Keeper.taskgraph");
            RequestTask gift = null;
            foreach (UnityEngine.Object part in parts)
            {
                if (part is RequestTask request && request.key == "bag.add")
                    gift = request;
            }
            Assert.That(gift, Is.Not.Null, "the Ask node baked to the ordinary RequestTask");
            Assert.That(gift.value, Is.EqualTo("medkit"), "the value is a row of the catalog bag.add names");

            // AND THE BAG SERVES IT, typed: a row the catalog has lands; one it lacks is refused.
            var def = AssetDatabase.LoadAssetAtPath<ServiceDef>(
                "Assets/DrawToPlayExamples/Demo/M21/Registries/M21InventoryService.asset");
            ServiceRequest row = def.requests.Find(r => r.key == "bag.add");
            Assert.That(row, Is.Not.Null);
            Assert.That(row.action, Is.EqualTo(InventoryService.AddAction));
            Assert.That(row.namesRowOf, Is.Not.Null, "the value is typed by the item catalog");
        }

        [Test]
        public void TheDawnReaction_BakedByPicking_HoldsNoTypedString()
        {
            UnityEngine.Object[] parts = AssetDatabase.LoadAllAssetsAtPath(
                "Assets/DrawToPlayExamples/Demo/M21/Reactions/M21Reaction_Dawn.taskgraph");
            AnnouncementCondition when = null;
            UiCallTask say = null;
            GraphTaskAsset program = null;
            foreach (UnityEngine.Object part in parts)
            {
                when ??= part as AnnouncementCondition;
                say ??= part as UiCallTask;
                program ??= part as GraphTaskAsset;
            }
            Assert.That(when, Is.Not.Null, "When Announced baked to the once-per-announcement condition");
            Assert.That(when.key, Is.EqualTo("clock.dawn"), "the clock's declared announcement");
            Assert.That(say, Is.Not.Null, "Say To Screen baked to the ordinary UiCallTask");
            Assert.That(say.ui.entryName, Is.EqualTo("hud"));
            Assert.That(say.verb, Is.EqualTo("say"), "a verb the HUD's skin declares");
            Assert.That(program.inputBindings.Count, Is.EqualTo(1),
                "the hour reaches the verb's argument by a WIRE from Announced Payload, not a typed key");
        }
        // ------------------------------------------------------------------ M39.3: one slot per key

        /// <summary>Two asks of the same key in one chain are two requests, not one: the second
        /// waits (Running) until the service has consumed the first, instead of writing over it.
        /// Found when the warden paid one medkit for two asks. And since M40.1 an ask is a CALL:
        /// it stays Running until its own request is consumed, then succeeds.</summary>
        [Test]
        public void ASecondAskOfAFullKey_WaitsForTheFirstToBeServed()
        {
            var items = ScriptableObject.CreateInstance<ItemRegistry>();
            items.entries.Add(new ItemDef { name = "medkit" });
            m_Junk.Add(items);
            var def = ScriptableObject.CreateInstance<ServiceDef>();
            def.serviceName = "inventory";
            def.registry = items;
            def.requests.Add(new ServiceRequest
            {
                key = "bag.add", action = InventoryService.AddAction, namesRowOf = items
            });
            m_Junk.Add(def);
            var bag = new InventoryService(m_Root, def);
            m_Root.Provide(bag);

            var first = ScriptableObject.CreateInstance<RequestTask>();
            first.key = "bag.add";
            first.value = "medkit";
            m_Junk.Add(first);
            var second = ScriptableObject.CreateInstance<RequestTask>();
            second.key = "bag.add";
            second.value = "medkit";
            m_Junk.Add(second);

            first.OnEnter(m_Root.Context);
            second.OnEnter(m_Root.Context);
            Assert.AreEqual(StateTreeStatus.Running, first.OnTick(m_Root.Context, 0f),
                "posted, and waiting for the bag to serve it");
            Assert.AreEqual(StateTreeStatus.Running, second.OnTick(m_Root.Context, 0f),
                "the slot is full until the bag serves it");

            bag.Tick(0.02f);
            Assert.AreEqual(1, bag.Count("medkit"));
            Assert.AreEqual(StateTreeStatus.Success, first.OnTick(m_Root.Context, 0f),
                "served: the call returned");
            Assert.AreEqual(StateTreeStatus.Running, second.OnTick(m_Root.Context, 0f),
                "the slot was free, so the second ask posted and now waits");
            bag.Tick(0.02f);
            Assert.AreEqual(StateTreeStatus.Success, second.OnTick(m_Root.Context, 0f));
            Assert.AreEqual(2, bag.Count("medkit"), "two asks, two medkits");
        }

        // ------------------------------------------------------------------ M40.1: call and return

        /// <summary>
        /// THE CHAIN READS THIS ASK'S ANSWER. A program on an NPC's context asks the bench
        /// (DoTask: RequestTask) and then reads the answer (GetBlackboardString, scoped to the
        /// bench's Root) — with a STALE answer already on the root board from an earlier craft.
        /// The read must land after the bench served this ask, and on the bench's board, not the
        /// NPC's. Before M40.1 the chain ran through in one tick and copied "Raft".
        /// </summary>
        [Test]
        public void AskThenAskedResult_InOneChain_ReadsThisAsksAnswer_FromTheAnsweringScope()
        {
            var items = ScriptableObject.CreateInstance<ItemRegistry>();
            items.entries.Add(new ItemDef { name = "wood" });
            items.entries.Add(new ItemDef { name = "skiff" });
            m_Junk.Add(items);
            var bagDef = ScriptableObject.CreateInstance<ServiceDef>();
            bagDef.serviceName = "inventory";
            bagDef.registry = items;
            m_Junk.Add(bagDef);
            var bag = new InventoryService(m_Root, bagDef);
            m_Root.Provide(bag);
            m_Root.Provide(typeof(IBag), bag);
            bag.Add("wood", 3);

            var recipes = ScriptableObject.CreateInstance<CraftRecipeRegistry>();
            recipes.dependsOn.Add(items);
            var skiff = new CraftRecipeDef { name = "skiff", displayName = "Skiff", result = { entryName = "skiff" } };
            skiff.costs.Add(new CraftRecipeDef.Cost { item = { entryName = "wood" }, count = 3 });
            recipes.entries.Add(skiff);
            m_Junk.Add(recipes);
            var craftDef = ScriptableObject.CreateInstance<ServiceDef>();
            craftDef.serviceName = "craft";
            craftDef.scope = StateTreeContextKind.Root;
            craftDef.registry = recipes;
            craftDef.requests.Add(new ServiceRequest
            {
                key = CraftKeys.Begin, action = CraftService.CraftAction, namesRowOf = recipes
            });
            craftDef.settings.values.Add(new ServiceSettingValue
            {
                name = nameof(CraftService.stationTag), stringValue = "station"
            });
            m_Junk.Add(craftDef);
            var bench = new CraftService(m_Root, craftDef);
            m_Root.Provide(bench);
            bench.Tick(0f);

            // THE STALE ANSWER: an earlier craft left "Raft" on the bench's board.
            m_Root.Context.blackboard[ServiceContracts.FieldKey(CraftResult.Key, "line")] = "Raft";

            // The program, on an NPC of its own: ask, then copy the answer's line to 'said'.
            var ask = ScriptableObject.CreateInstance<RequestTask>();
            ask.key = CraftKeys.Begin;
            ask.value = "skiff";
            m_Junk.Add(ask);
            var program = ScriptableObject.CreateInstance<GraphTaskAsset>();
            program.nodes = new List<GraphTaskNode>
            {
                new GraphTaskNode { kind = GraphTaskNodeKind.DoTask, task = ask, exec = new[] { 1, 1 } },
                new GraphTaskNode
                {
                    kind = GraphTaskNodeKind.SetBlackboardString, stringValue = "said",
                    data = new[] { 3 }, exec = new[] { 2 }
                },
                new GraphTaskNode { kind = GraphTaskNodeKind.ReturnSuccess },
                new GraphTaskNode
                {
                    kind = GraphTaskNodeKind.GetBlackboardString,
                    stringValue = ServiceContracts.FieldKey(CraftResult.Key, "line"),
                    stringValue2 = nameof(StateTreeContextKind.Root)   // what Asked Result bakes
                }
            };
            program.tickEntry = 0;
            m_Junk.Add(program);

            var npcGo = new GameObject("Npc") { hideFlags = HideFlags.HideAndDontSave };
            npcGo.transform.SetParent(m_Root.transform);
            m_Junk.Add(npcGo);
            var npc = new StateTreeContext(npcGo);

            program.OnEnter(npc);
            Assert.AreEqual(StateTreeStatus.Running, program.OnTick(npc, 0.02f),
                "the ask posted and the chain is suspended on it");
            Assert.IsFalse(npc.blackboard.ContainsKey("said"), "nothing read yet — no stale 'Raft'");

            bench.Tick(0.02f);   // the bench serves: three wood become a skiff, "Skiff" announced
            Assert.AreEqual(StateTreeStatus.Success, program.OnTick(npc, 0.02f),
                "served, so the call returned and the chain ran on");
            Assert.AreEqual("Skiff", npc.blackboard["said"],
                "the answer to THIS ask, read from the bench's board while running on the NPC's");
            program.OnExit(npc, StateTreeStatus.Success);
        }

        // ------------------------------------------------------------------ M41.3: a subsystem with no class

        /// <summary>The shrine has no class: the API lists it beside the bench, an Ask ▾ can pick
        /// its one request, and the validator's note on the node says who serves it.</summary>
        [Test]
        public void TheShrine_IsASubsystemWithNoClass_ListedAndServedByItsGraph()
        {
            Assume.That(AssetDatabase.LoadAssetAtPath<ServiceDef>(
                "Assets/DrawToPlayExamples/Demo/M21/Registries/M21Kind_Shrine.asset"), Is.Not.Null);
            Assert.That(DeclaredApi.Subsystems(), Does.Contain("M21Kind_Shrine"));
            Assert.That(DeclaredApi.RequestKeys("M21Kind_Shrine"), Does.Contain("shrine.pray"));
            ServiceRequest pray = DeclaredApi.Request("M21Kind_Shrine", "shrine.pray");
            Assert.That(pray.action, Is.Empty, "no class verb");
            Assert.That(pray.reactionGraph, Is.Not.Null, "served by its graph");
            Assert.That(DeclaredApi.Subsystem("M21Kind_Shrine").serviceType, Is.Null);

            Type validator = GraphEditorType("PowerOfFire.DrawToPlay.GraphEditor.DeclaredApiValidator");
            Type authoring = GraphEditorType("PowerOfFire.DrawToPlay.GraphEditor.M21DialogGraphAuthoring");
            Type askNode = GraphEditorType("PowerOfFire.DrawToPlay.GraphEditor.AskSubsystemNode");
            if (validator == null || authoring == null || askNode == null)
                Assert.Inconclusive("graph assembly not loaded");
            const BindingFlags any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
            string path = AssetDatabase.GenerateUniqueAssetPath("Assets/DrawToPlay/Tests/Editor/Temp_Shrine.taskgraph");
            var problems = new List<string>();
            object graph = authoring.GetMethod("NewGraph", any).Invoke(null, new object[] { path, problems });
            try
            {
                object ask = authoring.GetMethod("Add", any).Invoke(null, new object[] { graph, askNode, 0f, 0f, problems });
                MethodInfo write = authoring.GetMethod("Write", any);
                write.Invoke(null, new object[] { ask, "subsystem", "M21Kind_Shrine", problems });
                write.Invoke(null, new object[] { ask, "key", "shrine.pray", problems });
                var found = new List<string>();
                foreach (object finding in (IEnumerable)validator.GetMethod("Findings", any).Invoke(null, new[] { Nodes(graph) }))
                    found.Add(finding.ToString());
                Assert.That(found, Has.Some.StartsWith("note:").And.Contains("served by the graph"),
                    string.Join("\n", found));
                Assert.That(found.Exists(f => f.StartsWith("error:")), Is.False);
            }
            finally
            {
                AssetDatabase.DeleteAsset(path);
            }
        }

        // ------------------------------------------------------------------ M38.4: findings on the node

        /// <summary>
        /// A pick no def declares says so ON THE NODE. The validator lives behind the graph
        /// firewall, so it is reached by reflection on a throwaway graph: a When Announced whose
        /// key the clock does not announce, and a Say To Screen whose verb no hud skin declares,
        /// both flagged; the graph the waystation authors, clean.
        /// </summary>
        [Test]
        public void APickNoDefDeclares_IsAFindingOnItsNode_AndTheAuthoredGraphIsClean()
        {
            Type validator = GraphEditorType("PowerOfFire.DrawToPlay.GraphEditor.DeclaredApiValidator");
            Type authoring = GraphEditorType("PowerOfFire.DrawToPlay.GraphEditor.M21DialogGraphAuthoring");
            Type baker = GraphEditorType("PowerOfFire.DrawToPlay.GraphEditor.TaskGraphBaker");
            Type whenNode = GraphEditorType("PowerOfFire.DrawToPlay.GraphEditor.WhenAnnouncedNode");
            Type sayNode = GraphEditorType("PowerOfFire.DrawToPlay.GraphEditor.SayToUiNode");
            if (validator == null || authoring == null || baker == null || whenNode == null || sayNode == null)
                Assert.Inconclusive("graph assembly not loaded");

            const BindingFlags any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
            MethodInfo findings = validator.GetMethod("Findings", any);
            MethodInfo newGraph = authoring.GetMethod("NewGraph", any);
            MethodInfo add = authoring.GetMethod("Add", any);
            MethodInfo write = authoring.GetMethod("Write", any);
            MethodInfo load = baker.GetMethod("LoadGraphAtPath", any);

            // The authored dawn reaction names only what the defs declare.
            object dawn = load.Invoke(null, new object[] { "Assets/DrawToPlayExamples/Demo/M21/Reactions/M21Reaction_Dawn.taskgraph" });
            Assume.That(dawn, Is.Not.Null, "the waystation's dawn graph exists");
            Assert.That(Count(findings.Invoke(null, new[] { Nodes(dawn) })), Is.EqualTo(0),
                "the graph authored by picking is clean");

            string path = AssetDatabase.GenerateUniqueAssetPath("Assets/DrawToPlay/Tests/Editor/Temp_Undeclared.taskgraph");
            var problems = new List<string>();
            object graph = newGraph.Invoke(null, new object[] { path, problems });
            try
            {
                Assume.That(graph, Is.Not.Null, string.Join("; ", problems));
                object when = add.Invoke(null, new object[] { graph, whenNode, 0f, 0f, problems });
                write.Invoke(null, new object[] { when, "subsystem", "ClockService", problems });
                write.Invoke(null, new object[] { when, "key", "clock.dusk", problems });
                object say = add.Invoke(null, new object[] { graph, sayNode, 0f, 100f, problems });
                write.Invoke(null, new object[] { say, "ui", "hud", problems });
                write.Invoke(null, new object[] { say, "verb", "shout", problems });
                Assert.That(problems, Is.Empty);

                var found = new List<string>();
                foreach (object finding in (IEnumerable)findings.Invoke(null, new[] { Nodes(graph) }))
                    found.Add(finding.GetType().GetField("node").GetValue(finding).GetType().Name + ": " + finding);

                Assert.That(found.Count, Is.EqualTo(2), string.Join("\n", found));
                Assert.That(found, Has.Some.StartsWith("WhenAnnouncedNode: error").And.Contains("clock.dusk"),
                    "the undeclared announcement is an error on the When node");
                Assert.That(found, Has.Some.StartsWith("SayToUiNode: error").And.Contains("shout"),
                    "the undeclared verb is an error on the Say node");
            }
            finally
            {
                AssetDatabase.DeleteAsset(path);
            }
        }

        private static Type GraphEditorType(string fullName)
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type type = assembly.GetType(fullName);
                if (type != null)
                    return type;
            }
            return null;
        }

        private static object Nodes(object graph)
        {
            // Graph.GetNodes() → IEnumerable<INode>; the validator takes IReadOnlyList<INode>.
            var enumerable = (IEnumerable)graph.GetType().GetMethod("GetNodes").Invoke(graph, null);
            Type nodeType = GraphEditorType("Unity.GraphToolkit.Editor.INode");
            var list = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(nodeType));
            foreach (object node in enumerable)
                list.Add(node);
            return list;
        }

        private static int Count(object findings)
        {
            int count = 0;
            foreach (object _ in (IEnumerable)findings)
                count++;
            return count;
        }
    }
}
