using System;
using Unity.GraphToolkit.Editor;

namespace PowerOfFire.DrawToPlay.GraphEditor
{
    /// <summary>
    /// WHEN A SUBSYSTEM ANNOUNCES (M38.1) — subsystem ▾, announcement ▾ — true ONCE per
    /// announcement, for any number of listeners.
    ///
    /// What used to be Has Blackboard Key with a typed key — which was true forever after the
    /// first dawn. The key is picked from the def's declared announcements and the node bakes to
    /// an <see cref="AnnouncementCondition"/>, which fires when the announcement's serial moves.
    /// The payload stays on the key; <see cref="AnnouncedPayloadNode"/> reads it beside this.
    /// </summary>
    [Serializable]
    [UseWithGraph(typeof(TaskGraph))]
    [Node("Subsystems", null, "When Announced")]
    public class WhenAnnouncedNode : ConditionValueNode, IDeclaredApiNode
    {
        public const string SubsystemPortName = "subsystem";

        [NonSerialized] private DeclaredApiChoices m_Choices;

        public override Type conditionType => typeof(AnnouncementCondition);

        private DeclaredApiChoices choices
        {
            get
            {
                if (m_Choices == null)
                {
                    m_Choices = new DeclaredApiChoices(this);
                    m_Choices.DependsOn("key", s => DeclaredApi.AnnouncementKeys(s[0]), SubsystemPortName);
                }
                return m_Choices;
            }
        }

        protected override void OnDefinePorts(IPortDefinitionContext context)
        {
            TaskGraphPorts.AddResult<bool>(context, "True once, each time it is announced.");
            TaskGraphPorts.AddChoiceData(context, SubsystemPortName, "Subsystem",
                "Which subsystem announces it.", DeclaredApi.Subsystems());
            TaskGraphPorts.AddChoiceData(context, "key", "Announcement",
                "Which announcement — the chosen subsystem's declared ones.", choices.Remembered("key"));
            TaskGraphPorts.AddData<StateTreeContextKind>(context, "scope", "Scope",
                "Whose board the announcement is on — Root for a root subsystem.");
        }

        public bool AdoptChoiceSources() => choices.AdoptChoiceSources();
        public bool IsStale() => choices.IsStale();
        public void DropUnoffered() => choices.DropUnoffered();
    }
}
