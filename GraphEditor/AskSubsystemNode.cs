using System;
using Unity.GraphToolkit.Editor;

namespace PowerOfFire.DrawToPlay.GraphEditor
{
    /// <summary>
    /// ASK A SUBSYSTEM (M38.1) — subsystem ▾, request ▾, value ▾ — with nothing typed.
    ///
    /// The generic Request node offered every key in the project and a free-text value. This
    /// one reads the def: the requests are the chosen subsystem's, and when the request names
    /// rows of a catalog (<see cref="ServiceRequest.namesRowOf"/>) the value is a dropdown of
    /// that catalog's rows. It bakes to the same <see cref="RequestTask"/> the old node did —
    /// the ports are named after the task's fields, so the baker copies them unchanged and a
    /// graph that asked by typing and one that asked by picking run the same program.
    /// </summary>
    [Serializable]
    [UseWithGraph(typeof(TaskGraph))]
    [Node("Subsystems", null, "Ask")]
    public class AskSubsystemNode : TaskCallNode, IDeclaredApiNode
    {
        public const string SubsystemPortName = "subsystem";

        [NonSerialized] private DeclaredApiChoices m_Choices;

        public override Type taskType => typeof(RequestTask);

        private DeclaredApiChoices choices
        {
            get
            {
                if (m_Choices == null)
                {
                    m_Choices = new DeclaredApiChoices(this);
                    m_Choices.DependsOn("key", s => DeclaredApi.RequestKeys(s[0]), SubsystemPortName);
                    m_Choices.DependsOn("value", s => DeclaredApi.ValueChoices(s[0], s[1]),
                        SubsystemPortName, "key");
                }
                return m_Choices;
            }
        }

        protected override void OnDefinePorts(IPortDefinitionContext context)
        {
            TaskGraphPorts.AddExecIn(context);
            TaskGraphPorts.AddExecOut(context, TaskGraphPorts.SuccessExecPortName, "Success",
                "Runs once the request is written.");
            TaskGraphPorts.AddExecOut(context, TaskGraphPorts.FailureExecPortName, "Failure",
                "Runs when no subsystem serves the request from here.");

            TaskGraphPorts.AddChoiceData(context, SubsystemPortName, "Subsystem",
                "Which subsystem — every def that names a class.", DeclaredApi.Subsystems());
            TaskGraphPorts.AddChoiceData(context, "key", "Request",
                "What to ask for — the chosen subsystem's declared requests.", choices.Remembered("key"));
            TaskGraphPorts.AddChoiceData(context, "value", "Value",
                "The request's value. A row of the catalog the request names rows of, or free text.",
                choices.Remembered("value"));
            TaskGraphPorts.AddData<string>(context, "valueKey", "Value Key",
                "Optional: a blackboard key holding the value — wins over Value when it resolves.");
            TaskOutputPorts.DefineOutputs(context, taskType);
        }

        public bool AdoptChoiceSources() => choices.AdoptChoiceSources();
        public bool IsStale() => choices.IsStale();
        public void DropUnoffered() => choices.DropUnoffered();
    }
}
