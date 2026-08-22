using System;
using PowerOfFire.DrawToPlay.Editor;
using Unity.GraphToolkit.Editor;

namespace PowerOfFire.DrawToPlay.GraphEditor
{
    /// <summary>
    /// WHAT WAS ANNOUNCED (M38.1) — subsystem ▾, announcement ▾ → the payload as text, read
    /// from the key the announcement leaves it on. The twin of <see cref="WhenAnnouncedNode"/>:
    /// that one says "now", this one says "what". Bakes to the ordinary Get String.
    /// </summary>
    [Serializable]
    [UseWithGraph(typeof(TaskGraph))]
    [Node("Subsystems", null, "Announced Payload")]
    public class AnnouncedPayloadNode : Node, ITaskGraphNode, IDeclaredApiNode
    {
        public const string SubsystemPortName = "subsystem";

        [NonSerialized] private DeclaredApiChoices m_Choices;

        public GraphTaskNodeKind nodeKind => GraphTaskNodeKind.GetBlackboardString;

        private DeclaredApiChoices choices
        {
            get
            {
                if (m_Choices == null)
                {
                    m_Choices = new DeclaredApiChoices(this);
                    m_Choices.DependsOn(GetBlackboardStringNode.KeyPortName,
                        s => DeclaredApi.AnnouncementKeys(s[0]), SubsystemPortName);
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
            TaskGraphPorts.AddResult<string>(context,
                "The payload as text — a number's digits, a contract's ToString, \"\" when unset.");
        }

        public bool AdoptChoiceSources() => choices.AdoptChoiceSources();
        public bool IsStale() => choices.IsStale();
        public void DropUnoffered() => choices.DropUnoffered();
    }
}
