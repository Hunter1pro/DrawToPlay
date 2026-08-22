using System;
using PowerOfFire.DrawToPlay.Editor;
using Unity.GraphToolkit.Editor;

namespace PowerOfFire.DrawToPlay.GraphEditor
{
    /// <summary>
    /// WHAT A REQUEST ANSWERED (M38.2) — subsystem ▾, request ▾, field ▾ — the request's TARGET:
    /// the contract its action declares it answers with, read from the key that contract lands
    /// on, one field at a time. "Ask craft, then read craft's answer" without knowing the
    /// contract's key or class: the def and the class declared both.
    ///
    /// The answer arrives on the tick the request is served, which is the tick after it was
    /// asked — so this node lives in a REACTION graph (run by the subsystem after serving) or
    /// after a When Announced, never on the same chain as the Ask that caused it.
    /// </summary>
    [Serializable]
    [UseWithGraph(typeof(TaskGraph))]
    [Node("Subsystems", null, "Asked Result")]
    public class AskedResultNode : Node, ITaskGraphNode, IDeclaredApiNode, IBakesKey
    {
        public const string SubsystemPortName = "subsystem";
        public const string RequestPortName = "request";
        public const string FieldPortName = "field";

        [NonSerialized] private DeclaredApiChoices m_Choices;

        /// <summary>Whether the picked field reads as a number — REMEMBERED IN THE FILE, because a
        /// reloaded node defines its pins before anyone can read them, and a result pin that
        /// came back as text would drop the float wire the author saved.</summary>
        [UnityEngine.SerializeField] private bool m_Numeric;

        public GraphTaskNodeKind nodeKind => m_Numeric
            ? GraphTaskNodeKind.GetBlackboardFloat
            : GraphTaskNodeKind.GetBlackboardString;

        private DeclaredApiChoices choices
        {
            get
            {
                if (m_Choices == null)
                {
                    m_Choices = new DeclaredApiChoices(this);
                    m_Choices.DependsOn(RequestPortName, s => AnsweringRequests(s[0]), SubsystemPortName);
                    m_Choices.DependsOn(FieldPortName,
                        s => DeclaredApi.FieldChoices(DeclaredApi.AnswerOf(s[0], s[1])),
                        SubsystemPortName, RequestPortName);
                }
                return m_Choices;
            }
        }

        /// <summary>Only the requests that answer with something.</summary>
        private static System.Collections.Generic.List<string> AnsweringRequests(string defName)
        {
            var all = DeclaredApi.RequestKeys(defName);
            var answering = new System.Collections.Generic.List<string> { DeclaredApi.None };
            for (int i = 0; i < all.Count; i++)
            {
                if (!string.IsNullOrEmpty(all[i]) && DeclaredApi.AnswerOf(defName, all[i]) != null)
                    answering.Add(all[i]);
            }
            return answering;
        }

        protected override void OnDefinePorts(IPortDefinitionContext context)
        {
            TaskGraphPorts.AddChoiceData(context, SubsystemPortName, "Subsystem",
                "Which subsystem was asked.", DeclaredApi.Subsystems());
            TaskGraphPorts.AddChoiceData(context, RequestPortName, "Request",
                "Which request — only those that answer with a contract are listed.",
                choices.Remembered(RequestPortName));
            TaskGraphPorts.AddChoiceData(context, FieldPortName, "Field",
                "One field of the answer, or blank for the whole contract as text.",
                choices.Remembered(FieldPortName));
            if (m_Numeric)
                TaskGraphPorts.AddResult<float>(context, "The field as a number — a bool reads 1 or 0.");
            else
                TaskGraphPorts.AddResult<string>(context, "The answer, or its field, as text.");
        }

        public string BakedKey()
        {
            string[] live = Live();
            Type answer = DeclaredApi.AnswerOf(live[0], live[1]);
            return ServiceContracts.FieldKey(ServiceContracts.KeyOf(answer), live[2]);
        }

        public string BakedScope()
        {
            ServiceDef def = DeclaredApi.Subsystem(Live()[0]);
            return def != null ? def.scope.ToString() : "";
        }

        private static bool Numeric(string[] sources)
        {
            if (string.IsNullOrEmpty(sources[2]))
                return false;
            return DeclaredApi.IsNumeric(DeclaredApi.AnswerOf(sources[0], sources[1]), sources[2]);
        }

        private string[] Live()
        {
            return new[] { ReadPin(SubsystemPortName), ReadPin(RequestPortName), ReadPin(FieldPortName) };
        }

        private string ReadPin(string pin)
        {
            try
            {
                IPort port = GetInputPortByName(pin);
                return port != null
                    && LibraryParameterPorts.TryReadValue(port, typeof(string), out object value)
                    && value is string text ? text : "";
            }
            catch (Exception)
            {
                return "";
            }
        }

        public bool AdoptChoiceSources()
        {
            bool moved = choices.AdoptChoiceSources();
            bool numeric = Numeric(Live());
            if (numeric != m_Numeric)
            {
                m_Numeric = numeric;
                moved = true;
            }
            return moved;
        }
        public bool IsStale() => choices.IsStale();
        public void DropUnoffered() => choices.DropUnoffered();
    }
}
