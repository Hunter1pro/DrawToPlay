using System;
using Unity.GraphToolkit.Editor;

namespace PowerOfFire.DrawToPlay.GraphEditor
{
    /// <summary>
    /// Base for every condition node: one graph node = one <see cref="StateTreeConditionAsset"/>
    /// instance hanging off the transition it is wired to.
    ///
    /// THE OUTPUT PORT IS TYPED WITH THE CONCRETE CONDITION CLASS, and that single choice is what
    /// makes bad wiring impossible on this side of the model: a port pair connects only when the
    /// input's data type is assignable from the output's (PortModel.CanConnectPort,
    /// UnityEditor.GraphToolkitModule IL 75069-75110). The transition's condition input is typed
    /// <see cref="StateTreeConditionAsset"/>, so every condition fits it, a condition fits nothing
    /// else, and no other node can be dropped into a condition slot. It also reads better on the
    /// canvas: the wire carries the name of the condition type.
    ///
    /// Like the task blocks, parameters are generated from the runtime type's public fields, so
    /// port name == field name by construction.
    /// </summary>
    [Serializable]
    public abstract class StateTreeConditionNode : Node
    {
        /// <summary>Name of the output port that plugs into a transition's condition slot.</summary>
        public const string ConditionPortName = "condition";

        /// <summary>
        /// The <see cref="StateTreeConditionAsset"/> subclass this node bakes into. The bake creates
        /// one instance of it per node and copies every parameter port into the field of the same
        /// name.
        /// </summary>
        public abstract Type conditionType { get; }

        /// <inheritdoc />
        protected override void OnDefinePorts(IPortDefinitionContext context)
        {
            context.AddOutputPort(ConditionPortName)
                .WithDataType(conditionType)
                .WithDisplayName(string.Empty)
                .WithTooltip("Wire into a Transition's condition slot.")
                .Build();

            LibraryParameterPorts.DefineParameterPorts(context, conditionType, this);
        }
    }
}
