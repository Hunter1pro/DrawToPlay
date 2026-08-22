using System;
using PowerOfFire.DrawToPlay.Editor;
using Unity.GraphToolkit.Editor;

namespace PowerOfFire.DrawToPlay.GraphEditor
{
    /// <summary>
    /// WHAT WAS ANNOUNCED (M38.1, fields in M38.2) — subsystem ▾, announcement ▾, field ▾ → the
    /// payload, or one field of it. The twin of <see cref="WhenAnnouncedNode"/>: that one says
    /// "now", this one says "what".
    ///
    /// A bare payload (the clock's hour) reads as text. A CONTRACT (a CraftResult) offers its
    /// fields, which the service wrote beside the key when it announced — so "line" reads
    /// <c>craft.last.line</c> as text and "made" reads <c>craft.last.made</c> as a number (a
    /// bool is 1/0 to a Compare). Bakes to the ordinary Get String or Get Float, with the key
    /// composed from the dropdowns.
    /// </summary>
    [Serializable]
    [UseWithGraph(typeof(TaskGraph))]
    [Node("Subsystems", null, "Announced Payload")]
    public class AnnouncedPayloadNode : Node, ITaskGraphNode, IDeclaredApiNode, IBakesKey
    {
        public const string SubsystemPortName = "subsystem";
        public const string FieldPortName = "field";

        [NonSerialized] private DeclaredApiChoices m_Choices;

        /// <summary>Whether the picked field reads as a number — REMEMBERED IN THE FILE, because a
        /// reloaded node defines its pins before anyone can read them, and a result pin that
        /// came back as text would drop the float wire the author saved.</summary>
        [UnityEngine.SerializeField] private bool m_Numeric;

        /// <summary>Float when the remembered field is a number or a bool; text otherwise.</summary>
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
                    m_Choices.DependsOn(GetBlackboardStringNode.KeyPortName,
                        s => DeclaredApi.AnnouncementKeys(s[0]), SubsystemPortName);
                    m_Choices.DependsOn(FieldPortName,
                        s => DeclaredApi.FieldChoices(DeclaredApi.PayloadOf(s[0], s[1])),
                        SubsystemPortName, GetBlackboardStringNode.KeyPortName);
                }
                return m_Choices;
            }
        }

        protected override void OnDefinePorts(IPortDefinitionContext context)
        {
            TaskGraphPorts.AddChoiceData(context, SubsystemPortName, "Subsystem",
                "Which subsystem announces it.", DeclaredApi.Subsystems());
            TaskGraphPorts.AddChoiceData(context, GetBlackboardStringNode.KeyPortName, "Announcement",
                "Which announcement — the payload is read from its key.",
                choices.Remembered(GetBlackboardStringNode.KeyPortName));
            TaskGraphPorts.AddChoiceData(context, FieldPortName, "Field",
                "One field of the announced contract, or blank for the whole payload as text.",
                choices.Remembered(FieldPortName));
            if (m_Numeric)
                TaskGraphPorts.AddResult<float>(context, "The field as a number — a bool reads 1 or 0.");
            else
                TaskGraphPorts.AddResult<string>(context,
                    "The payload or field as text — a number's digits, a contract's ToString, \"\" when unset.");
        }

        /// <summary>The key the baked Get reads: the announcement's, or a field beside it.</summary>
        public string BakedKey()
        {
            string[] live = Sources(remembered: false);
            return ServiceContracts.FieldKey(live[1], live[2]);
        }

        public string BakedScope()
        {
            ServiceDef def = DeclaredApi.Subsystem(Sources(remembered: false)[0]);
            return def != null ? def.scope.ToString() : "";
        }

        private bool Numeric(string[] sources)
        {
            if (string.IsNullOrEmpty(sources[2]))
                return false;
            return DeclaredApi.IsNumeric(DeclaredApi.PayloadOf(sources[0], sources[1]), sources[2]);
        }

        /// <summary>subsystem, announcement, field — remembered (safe in definition) or live.</summary>
        private string[] Sources(bool remembered = true)
        {
            if (remembered)
                return choices.RememberedSources(FieldPortName, 3);
            return new[]
            {
                ReadPin(SubsystemPortName), ReadPin(GetBlackboardStringNode.KeyPortName), ReadPin(FieldPortName)
            };
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
            bool numeric = Numeric(Sources(remembered: false));
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
