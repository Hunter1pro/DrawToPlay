using System;
using Unity.GraphToolkit.Editor;

namespace PowerOfFire.DrawToPlay.GraphEditor
{
    /// <summary>Runs ANOTHER logic graph as one instruction — the node that lets a task graph call a
    /// task graph, so a chain that shows up in five behaviours becomes one <c>.taskgraph</c> file
    /// everybody points at.
    ///
    /// Calls <see cref="RunGraphTask"/> latently: Running while the called program runs, then the
    /// Success or Failure pin. Cancelling this graph cancels the called one, and its latent
    /// instructions, all the way down.
    ///
    /// BY LIVE REFERENCE, WHICH IS THE DIFFERENCE THAT MATTERS. Every other call node bakes a
    /// CONFIGURED COPY of its library task into the importing file — retune the node, re-import, done.
    /// This one bakes a thin <see cref="RunGraphTask"/> holding a REFERENCE to the callee's baked
    /// asset, so editing the called graph reaches every caller with nothing to re-sync. It is the same
    /// by-reference model <see cref="RunSubTreeTaskCallNode"/> uses for trees, and it is what makes a
    /// shared sub-program worth extracting at all. The runtime still creates a fresh instance of the
    /// called program per activation, so two callers never share timers or latent positions.
    ///
    /// A GRAPH MAY REACH ITSELF, and nothing here stops it: the callee is picked from the project, and
    /// the honest place to catch a cycle is at run time, where the depth is actually known.
    /// <see cref="GraphTaskAsset.maxDepth"/> (32, counted on the shared context under
    /// <see cref="GraphTaskAsset.depthKey"/>) aborts the chain with an error naming exactly that case.
    /// Recursion that terminates is therefore legal and works.
    ///
    /// Its single parameter port mirrors <see cref="RunGraphTask.graph"/> 1:1, like every other
    /// wrapper — a <see cref="GraphTaskAsset"/> is a <see cref="UnityEngine.Object"/>, which
    /// <see cref="LibraryParameterPorts.IsSupportedParameterType"/> carries as an object-reference
    /// port, so the callee is picked with the ordinary object field on the node.</summary>
    [Serializable]
    [UseWithGraph(typeof(TaskGraph))]
    [Node("Tasks/Composite", null, "Run Task Graph")]
    public class RunGraphTaskCallNode : TaskCallNode
    {
        /// <inheritdoc />
        public override Type taskType => typeof(RunGraphTask);
    }
}
