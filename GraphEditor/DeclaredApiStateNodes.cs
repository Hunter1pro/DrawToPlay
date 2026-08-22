using System;
using PowerOfFire.DrawToPlay.Editor;
using Unity.GraphToolkit.Editor;

namespace PowerOfFire.DrawToPlay.GraphEditor
{
    // THE SAME FIVE, ON THE STATE-MACHINE CANVAS (M38.1b). A state block or a transition
    // condition whose dropdowns read the defs, baking to the same library types the task-graph
    // nodes do — the ports are named after the fields, and the state baker copies by name.
    // One class per node because the palette wants a concrete attributed type per entry.

    /// <summary>Ask a subsystem, as a state block.</summary>
    [Serializable]
    [UseWithGraph(typeof(StateTreeGraph))]
    [UseWithContext(typeof(StateNode))]
    [Node("Subsystems", null, "Ask")]
    public class AskSubsystemBlockNode : StateTaskBlockNode, IDeclaredApiNode
    {
        [NonSerialized] private DeclaredApiChoices m_Choices;

        public override Type taskType => typeof(RequestTask);

        private DeclaredApiChoices choices
        {
            get
            {
                if (m_Choices == null)
                {
                    m_Choices = new DeclaredApiChoices(this);
                    m_Choices.DependsOn("key", s => DeclaredApi.RequestKeys(s[0]), AskSubsystemNode.SubsystemPortName);
                    m_Choices.DependsOn("value", s => DeclaredApi.ValueChoices(s[0], s[1]),
                        AskSubsystemNode.SubsystemPortName, "key");
                }
                return m_Choices;
            }
        }

        protected override void OnDefinePorts(IPortDefinitionContext context)
        {
            TaskGraphPorts.AddChoiceData(context, AskSubsystemNode.SubsystemPortName, "Subsystem",
                "Which subsystem — every def that names a class.", DeclaredApi.Subsystems());
            TaskGraphPorts.AddChoiceData(context, "key", "Request",
                "What to ask for — the chosen subsystem's declared requests.", choices.Remembered("key"));
            TaskGraphPorts.AddChoiceData(context, "value", "Value",
                "The request's value. A row of the catalog the request names rows of, or free text.",
                choices.Remembered("value"));
            TaskGraphPorts.AddData<string>(context, "valueKey", "Value Key",
                "Optional: a blackboard key holding the value — wins over Value when it resolves.");
        }

        public bool AdoptChoiceSources() => choices.AdoptChoiceSources();
        public bool IsStale() => choices.IsStale();
        public void DropUnoffered() => choices.DropUnoffered();
    }

    /// <summary>Say a declared verb to a screen, as a state block.</summary>
    [Serializable]
    [UseWithGraph(typeof(StateTreeGraph))]
    [UseWithContext(typeof(StateNode))]
    [Node("Subsystems", null, "Say To Screen")]
    public class SayToUiBlockNode : StateTaskBlockNode, IDeclaredApiNode
    {
        [NonSerialized] private DeclaredApiChoices m_Choices;

        public override Type taskType => typeof(UiCallTask);

        private DeclaredApiChoices choices
        {
            get
            {
                if (m_Choices == null)
                {
                    m_Choices = new DeclaredApiChoices(this);
                    m_Choices.DependsOn("verb", s => DeclaredApi.Verbs(s[0]), "ui");
                }
                return m_Choices;
            }
        }

        protected override void OnDefinePorts(IPortDefinitionContext context)
        {
            TaskGraphPorts.AddChoiceData(context, "ui", "Screen", "Which UI row.", DeclaredApi.UiRows());
            TaskGraphPorts.AddChoiceData(context, "verb", "Verb",
                "What to say — the verbs the row's skins declare.", choices.Remembered("verb"));
            TaskGraphPorts.AddData<string>(context, "argument", "Argument", "The verb's argument, when it takes one.");
            TaskGraphPorts.AddData<string>(context, "argumentKey", "Argument Key",
                "Optional: a blackboard key holding the argument or a payload — wins when it resolves.");
        }

        public bool AdoptChoiceSources() => choices.AdoptChoiceSources();
        public bool IsStale() => choices.IsStale();
        public void DropUnoffered() => choices.DropUnoffered();
    }

    /// <summary>Show a UI row, as a state block. Parameters are the row's inspector job here —
    /// a state block has no pins to grow; the task graph's Show Screen is the one that does.</summary>
    [Serializable]
    [UseWithGraph(typeof(StateTreeGraph))]
    [UseWithContext(typeof(StateNode))]
    [Node("Subsystems", null, "Show Screen")]
    public class ShowUiBlockNode : StateTaskBlockNode
    {
        public override Type taskType => typeof(ShowUiTask);

        protected override void OnDefinePorts(IPortDefinitionContext context)
        {
            TaskGraphPorts.AddChoiceData(context, "ui", "Screen", "Which UI row.", DeclaredApi.UiRows());
            TaskGraphPorts.AddData<bool>(context, "holdWhileShown", "Hold While Shown",
                "Stay Running while the row is up — the state IS the open screen.");
            TaskGraphPorts.AddData<bool>(context, "hideOnExit", "Hide On Exit",
                "Take the row down when this state ends.");
        }
    }

    /// <summary>When a subsystem announces — as a transition condition.</summary>
    [Serializable]
    [UseWithGraph(typeof(StateTreeGraph))]
    [Node("Conditions/Subsystems", null, "When Announced")]
    public class WhenAnnouncedConditionNode : StateTreeConditionNode, IDeclaredApiNode
    {
        [NonSerialized] private DeclaredApiChoices m_Choices;

        public override Type conditionType => typeof(AnnouncementCondition);

        private DeclaredApiChoices choices
        {
            get
            {
                if (m_Choices == null)
                {
                    m_Choices = new DeclaredApiChoices(this);
                    m_Choices.DependsOn("key", s => DeclaredApi.AnnouncementKeys(s[0]), WhenAnnouncedNode.SubsystemPortName);
                }
                return m_Choices;
            }
        }

        protected override void OnDefinePorts(IPortDefinitionContext context)
        {
            context.AddOutputPort(ConditionPortName)
                .WithDataType(conditionType)
                .WithDisplayName(string.Empty)
                .WithTooltip("Wire into a Transition's condition slot.")
                .Build();
            TaskGraphPorts.AddChoiceData(context, WhenAnnouncedNode.SubsystemPortName, "Subsystem",
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
