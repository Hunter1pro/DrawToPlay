using System.Collections.Generic;
using NUnit.Framework;
using PowerOfFire.DrawToPlay.Editor;

namespace PowerOfFire.DrawToPlay.Tests
{
    /// <summary>
    /// M38.3 — the library against the palette. A library task or condition without its two
    /// wrappers is invisible on the canvas, and this is the test that says so instead of the
    /// silence that let 57 of 77 go missing.
    /// </summary>
    [TestFixture]
    public sealed class NodeWrapperDriftTests
    {
        [Test]
        public void EveryLibraryTaskAndCondition_HasItsTwoWrappers()
        {
            List<NodeWrapperDrift.Finding> findings = NodeWrapperDrift.Check();
            var lines = new List<string>();
            for (int i = 0; i < findings.Count; i++)
                lines.Add(findings[i].ToString());
            Assert.That(findings, Is.Empty,
                "run Tools/Draw To Play/Graph/Generate Node Wrappers and commit the files:\n"
                + string.Join("\n", lines));
            Assert.That(NodeWrapperDrift.LibraryTasks().Count, Is.GreaterThan(30), "the library is large");
        }

        [Test]
        public void AGeneratedWrapper_NamesTheTypeItsCategoryAndItsCanvas()
        {
            string block = NodeWrapperGenerator.Source(typeof(HideUiTask), NodeWrapperDrift.Wrapper.Block);
            Assert.That(block, Does.Contain("[UseWithGraph(typeof(StateTreeGraph))]"));
            Assert.That(block, Does.Contain("[UseWithContext(typeof(StateNode))]"), "a block lives in a state");
            Assert.That(block, Does.Contain("[Node(\"Tasks/Ui\", null, \"Hide Ui\")]"), "the library's own category");
            Assert.That(block, Does.Contain("public class HideUiTaskNode : StateTaskBlockNode"));
            Assert.That(block, Does.Contain("typeof(PowerOfFire.DrawToPlay.HideUiTask)"));

            string call = NodeWrapperGenerator.Source(typeof(HideUiTask), NodeWrapperDrift.Wrapper.Call);
            Assert.That(call, Does.Contain("[UseWithGraph(typeof(TaskGraph))]"));
            Assert.That(call, Does.Contain("public class HideUiTaskCallNode : TaskCallNode"));

            string value = NodeWrapperGenerator.Source(typeof(HasTagCondition), NodeWrapperDrift.Wrapper.ConditionValue);
            Assert.That(value, Does.Contain("public class HasTagConditionValueNode : ConditionValueNode"));
            Assert.That(value, Does.Contain("conditionType => typeof(PowerOfFire.DrawToPlay.HasTagCondition)"));
            Assert.That(NodeWrapperGenerator.DisplayName(typeof(HasTagCondition)), Is.EqualTo("Has Tag"));
        }

        [Test]
        public void TheCensus_DoesNotCountADeclaredApiNodeAsAWrapper()
        {
            // Ask wraps RequestTask and Say To Screen wraps UiCallTask — as a SUBSYSTEM's node,
            // not as the library's. The generic pair is still expected beside each.
            Dictionary<NodeWrapperDrift.Wrapper, Dictionary<System.Type, List<string>>> have =
                NodeWrapperDrift.ExistingWrappers();
            Assert.That(have[NodeWrapperDrift.Wrapper.Call][typeof(RequestTask)],
                Is.EqualTo(new[] { "RequestTaskCallNode" }), "Ask is not RequestTask's wrapper");
            Assert.That(have[NodeWrapperDrift.Wrapper.Call][typeof(UiCallTask)].Count, Is.EqualTo(1));
        }
    }
}
