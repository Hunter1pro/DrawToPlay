using System.Collections.Generic;
using NUnit.Framework;
using PowerOfFire.DrawToPlay.Editor;
using UnityEditor;
using UnityEngine;

namespace PowerOfFire.DrawToPlay.Tests
{
    /// <summary>A service whose one action answers with a contract, and a request that reacts by graph.</summary>
    internal sealed class OracleAnswer
    {
        public const string Key = "oracle.last";
        public string line = "";
        public bool truthful;
        public int weight;
        public OracleAnswer nested;   // not exposed: objects stay on the whole payload
    }

    [ServiceActionContract(AskAction, "value = a question", typeof(OracleAnswer))]
    internal sealed class OracleService : StateTreeService
    {
        public const string AskAction = "ask";

        public OracleService(StateTreeContextHost scope, ServiceDef definition) : base(scope, definition)
        {
        }

        protected override void OnRequest(ServiceRequest request, string value)
        {
            if (request.action == AskAction)
                Announce(OracleAnswer.Key, new OracleAnswer { line = "about " + value, truthful = value == "sky", weight = 7 });
        }
    }

    /// <summary>
    /// M38.2 — a request exposes its TARGET: the contract its action answers with lands on the
    /// contract's key with its fields beside it, and a request may react with a GRAPH run on the
    /// subsystem's scope after serving.
    /// </summary>
    [TestFixture]
    public sealed class ReactionGraphTests
    {
        private readonly List<Object> m_Junk = new List<Object>();
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
                    Object.DestroyImmediate(m_Junk[i]);
            }
            m_Junk.Clear();
        }

        [Test]
        public void AnAnsweredRequest_LeavesItsContractsFieldsAsKeys()
        {
            ServiceDef def = Def();
            var oracle = new OracleService(m_Root, def);
            oracle.Tick(0.02f);
            oracle.Request("oracle.ask", "sky");
            oracle.Tick(0.02f);

            var board = m_Root.Context.blackboard;
            Assert.That(board[OracleAnswer.Key], Is.InstanceOf<OracleAnswer>(), "the whole contract, for a skin");
            Assert.That(board["oracle.last.line"], Is.EqualTo("about sky"), "and each exposed field beside it");
            Assert.That(board["oracle.last.truthful"], Is.EqualTo(true));
            Assert.That(board["oracle.last.weight"], Is.EqualTo(7));
            Assert.That(board.ContainsKey("oracle.last.nested"), Is.False, "an object field is not a key");
            Assert.That(board.ContainsKey("oracle.ask.asked"), Is.False,
                "the request's value is kept beside the answer only for a reaction GRAPH to read");

            // WHAT THE CLASS DECLARES is what the picker reads: the target and its fields.
            Assert.That(ServiceContracts.KeyOf(typeof(OracleAnswer)), Is.EqualTo("oracle.last"));
            var names = new List<string>();
            foreach (System.Reflection.FieldInfo field in ServiceContracts.ExposedFields(typeof(OracleAnswer)))
                names.Add(field.Name);
            Assert.That(names, Is.EquivalentTo(new[] { "line", "truthful", "weight" }));
        }

        [Test]
        public void TheCraftRequest_DeclaresItsTarget_AndThePickerOffersItsFields()
        {
            Assert.That(DeclaredApi.AnswerOf("M21CraftService", "craft.begin"), Is.EqualTo(typeof(CraftResult)),
                "the bench answers a craft with a CraftResult — said on the class, read from the def");
            List<string> fields = DeclaredApi.FieldChoices(typeof(CraftResult));
            Assert.That(fields, Does.Contain("line"));
            Assert.That(fields, Does.Contain("made"));
            Assert.That(DeclaredApi.IsNumeric(typeof(CraftResult), "made"), Is.True, "a bool reads 1/0");
            Assert.That(DeclaredApi.IsNumeric(typeof(CraftResult), "line"), Is.False);
            Assert.That(DeclaredApi.PayloadOf("M21CraftService", CraftResult.Key), Is.EqualTo(typeof(CraftResult)),
                "and the announcement on the contract's own key is the same contract");
        }

        [Test]
        public void TheCraftedReaction_IsAGraphOnTheRequest_ReadingComposedKeys()
        {
            var craft = AssetDatabase.LoadAssetAtPath<ServiceDef>(
                "Assets/DrawToPlayExamples/Demo/M21/Registries/M21CraftService.asset");
            ServiceRequest begin = craft.requests.Find(r => r.key == "craft.begin");
            Assert.That(begin.reactionGraph, Is.Not.Null, "the 'say the result' row is a graph now");
            Assert.That(begin.reactions.Exists(r => r.verb == "say"), Is.False, "and the row is gone");

            var keys = new List<string>();
            foreach (GraphTaskNode node in begin.reactionGraph.nodes)
            {
                if (node.kind == GraphTaskNodeKind.GetBlackboardString || node.kind == GraphTaskNodeKind.GetBlackboardFloat)
                    keys.Add(node.kind + ":" + node.stringValue);
            }
            Assert.That(keys, Does.Contain("GetBlackboardString:craft.last.line"),
                "the line, composed from the request's target — nobody typed 'craft.last'");
            Assert.That(keys, Does.Contain("GetBlackboardFloat:craft.last.made"),
                "a bool field reads as a number, for the Compare that branches on it");
        }

        private ServiceDef Def()
        {
            var def = ScriptableObject.CreateInstance<ServiceDef>();
            def.name = "oracle";
            def.serviceName = "oracle";
            def.serviceTypeName = typeof(OracleService).FullName;
            def.requests.Add(new ServiceRequest { key = "oracle.ask", action = OracleService.AskAction });
            m_Junk.Add(def);
            return def;
        }
    }
}
