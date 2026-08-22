using System;
using System.Collections.Generic;
using Unity.GraphToolkit.Editor;
using UnityEngine;

namespace PowerOfFire.DrawToPlay.GraphEditor
{
    /// <summary>
    /// SHOW A SCREEN (M38.1) — row ▾, and the row's own PARAMETERS as pins. A confirm popup's
    /// question, an HUD line's text: whatever the UI row declares a show-site may override
    /// (<see cref="UiDef.parameters"/>) is a port here, typed by the parameter's kind, and bakes
    /// into the <see cref="ShowUiTask"/>'s argument set — the same id-bound override rows the
    /// inspector writes. The pins grow and shrink with the row picked.
    /// </summary>
    [Serializable]
    [UseWithGraph(typeof(TaskGraph))]
    [Node("Subsystems", null, "Show Screen")]
    public class ShowUiNode : TaskCallNode, IDeclaredApiNode, IBakesExtras
    {
        public const string RowPortName = "ui";

        /// <summary>Parameter pins are named after the parameter, behind this, so they can never
        /// collide with the task's own fields.</summary>
        public const string ArgumentPrefix = "arg:";

        [NonSerialized] private string m_RememberedRow = "";

        public override Type taskType => typeof(ShowUiTask);

        protected override void OnDefinePorts(IPortDefinitionContext context)
        {
            TaskGraphPorts.AddExecIn(context);
            TaskGraphPorts.AddExecOut(context, TaskGraphPorts.SuccessExecPortName, "Success",
                "Runs once the row is shown — at once, or when it is hidden if Hold While Shown.");
            TaskGraphPorts.AddExecOut(context, TaskGraphPorts.FailureExecPortName, "Failure",
                "Runs when the row cannot be shown from here.");
            TaskGraphPorts.AddChoiceData(context, RowPortName, "Screen", "Which UI row.", DeclaredApi.UiRows());
            TaskGraphPorts.AddData<bool>(context, "holdWhileShown", "Hold While Shown",
                "Stay Running while the row is up — the state IS the open screen.");
            TaskGraphPorts.AddData<bool>(context, "hideOnExit", "Hide On Exit",
                "Take the row down when this task ends.");

            // THE ROW'S PARAMETERS, as pins: read from the remembered row, because definition
            // cannot read the Screen pin.
            IReadOnlyList<GraphTaskParameter> parameters = DeclaredApi.Parameters(m_RememberedRow);
            for (int i = 0; i < parameters.Count; i++)
            {
                GraphTaskParameter parameter = parameters[i];
                if (parameter == null || string.IsNullOrEmpty(parameter.name))
                    continue;
                string port = ArgumentPrefix + parameter.name;
                string label = UnityEditor.ObjectNames.NicifyVariableName(parameter.name);
                string tip = "The row's '" + parameter.name + "' parameter.";
                switch (parameter.kind)
                {
                    case GraphTaskParameterKind.Float:
                        context.AddInputPort<float>(port).WithDisplayName(label).WithTooltip(tip)
                            .WithDefaultValue(parameter.floatValue).Build();
                        break;
                    case GraphTaskParameterKind.Bool:
                        context.AddInputPort<bool>(port).WithDisplayName(label).WithTooltip(tip)
                            .WithDefaultValue(parameter.floatValue > 0.5f).Build();
                        break;
                    default:
                        context.AddInputPort<string>(port).WithDisplayName(label).WithTooltip(tip)
                            .WithDefaultValue(parameter.stringValue ?? "").Build();
                        break;
                }
            }
            TaskOutputPorts.DefineOutputs(context, taskType);
        }

        // ---- the dependent-pins seam -------------------------------------------------------

        public bool AdoptChoiceSources()
        {
            string live = LiveRow();
            if (live == m_RememberedRow)
                return false;
            m_RememberedRow = live;
            return true;
        }

        /// <summary>Stale when the parameter pins are not the picked row's parameters.</summary>
        public bool IsStale()
        {
            IReadOnlyList<GraphTaskParameter> wanted = DeclaredApi.Parameters(LiveRow());
            var names = new HashSet<string>();
            for (int i = 0; i < wanted.Count; i++)
            {
                if (wanted[i] != null && !string.IsNullOrEmpty(wanted[i].name))
                    names.Add(ArgumentPrefix + wanted[i].name);
            }
            var present = new HashSet<string>();
            try
            {
                foreach (IPort port in GetInputPorts())
                {
                    string name = port?.Name;
                    if (name != null && name.StartsWith(ArgumentPrefix, StringComparison.Ordinal))
                        present.Add(name);
                }
            }
            catch (Exception)
            {
                return false;
            }
            return !names.SetEquals(present);
        }

        public void DropUnoffered()
        {
        }

        private string LiveRow()
        {
            try
            {
                IPort port = GetInputPortByName(RowPortName);
                return port != null
                    && LibraryParameterPorts.TryReadValue(port, typeof(string), out object value)
                    && value is string text ? text : "";
            }
            catch (Exception)
            {
                return "";
            }
        }

        // ---- the bake -----------------------------------------------------------------------

        /// <summary>Every parameter pin becomes an id-bound override row on the task's argument
        /// set — exactly what the inspector's tick-to-override writes, so the service applies
        /// them through the same path.</summary>
        public void BakeInto(StateTreeTaskAsset task, List<string> problems)
        {
            if (!(task is ShowUiTask show))
                return;
            IReadOnlyList<GraphTaskParameter> parameters = DeclaredApi.Parameters(LiveRow());
            show.arguments.values.Clear();
            for (int i = 0; i < parameters.Count; i++)
            {
                GraphTaskParameter parameter = parameters[i];
                if (parameter == null || string.IsNullOrEmpty(parameter.name))
                    continue;
                IPort port = GetInputPortByName(ArgumentPrefix + parameter.name);
                if (port == null)
                    continue;
                if (port.IsConnected)
                {
                    problems.Add("Show Screen: '" + parameter.name + "' is wired — a row parameter "
                        + "takes a value, not a wire, until reaction graphs (38.2).");
                    continue;
                }
                var over = new GraphTaskParameterOverride
                {
                    id = parameter.id, name = parameter.name, enabled = true
                };
                switch (parameter.kind)
                {
                    case GraphTaskParameterKind.Float:
                        if (LibraryParameterPorts.TryReadValue(port, typeof(float), out object f))
                            over.floatValue = (float)f;
                        break;
                    case GraphTaskParameterKind.Bool:
                        if (LibraryParameterPorts.TryReadValue(port, typeof(bool), out object b))
                            over.floatValue = (bool)b ? 1f : 0f;
                        break;
                    default:
                        if (LibraryParameterPorts.TryReadValue(port, typeof(string), out object s))
                            over.stringValue = s as string ?? "";
                        break;
                }
                show.arguments.values.Add(over);
            }
        }
    }

    /// <summary>A node that writes MORE onto its baked task than its field-named ports — the
    /// one seam the baker offers a node that grows pins the task has no field for.</summary>
    public interface IBakesExtras
    {
        void BakeInto(StateTreeTaskAsset task, List<string> problems);
    }
}
