using System;
using PowerOfFire.DrawToPlay.Editor;
using Unity.GraphToolkit.Editor;

namespace PowerOfFire.DrawToPlay.GraphEditor
{
    /// <summary>
    /// SAY A VERB TO A SCREEN (M38.1) — row ▾, verb ▾ — the verbs being what the row's skins
    /// declare with <see cref="UiVerbContractAttribute"/>. Bakes to <see cref="UiCallTask"/>.
    /// </summary>
    [Serializable]
    [UseWithGraph(typeof(TaskGraph))]
    [Node("Subsystems", null, "Say To Screen")]
    public class SayToUiNode : TaskCallNode, IDeclaredApiNode
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
            TaskGraphPorts.AddExecIn(context);
            TaskGraphPorts.AddExecOut(context, TaskGraphPorts.SuccessExecPortName, "Success",
                "Runs once the verb is said (a hidden row is a quiet success).");
            TaskGraphPorts.AddExecOut(context, TaskGraphPorts.FailureExecPortName, "Failure",
                "Runs when the task fails.");
            TaskGraphPorts.AddChoiceData(context, "ui", "Screen", "Which UI row.", DeclaredApi.UiRows());
            TaskGraphPorts.AddChoiceData(context, "verb", "Verb",
                "What to say — the verbs the row's skins declare.", choices.Remembered("verb"));
            TaskGraphPorts.AddData<string>(context, "argument", "Argument", "The verb's argument, when it takes one.");
            TaskGraphPorts.AddData<string>(context, "argumentKey", "Argument Key",
                "Optional: a blackboard key holding the argument or a payload — wins when it resolves.");
            TaskOutputPorts.DefineOutputs(context, taskType);
        }

        public bool AdoptChoiceSources() => choices.AdoptChoiceSources();
        public bool IsStale() => choices.IsStale();
        public void DropUnoffered() => choices.DropUnoffered();
    }
}
