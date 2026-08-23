using NUnit.Framework;
using PowerOfFire.DrawToPlay.Editor;
using UnityEditor;

namespace PowerOfFire.DrawToPlay.Tests
{
    /// <summary>
    /// Editor rule 4 — nothing per repaint walks the project. The placement panel asks for
    /// its kind in OnGUI and GetPropertyHeight, per row, per repaint; the answer has to come
    /// from a cache that is listed once and forgotten on a project change.
    /// </summary>
    [TestFixture]
    public sealed class LevelObjectKindsTests
    {
        [Test]
        public void TheKindRegistries_AreListedOnce_UntilTheProjectChanges()
        {
            LevelObjectKinds.Forget();
            var first = LevelObjectKinds.Registries();
            var second = LevelObjectKinds.Registries();
            Assert.That(second, Is.SameAs(first), "the second ask is the same list — no project walk");

            var kinds = AssetDatabase.LoadAssetAtPath<LevelObjectKindRegistry>(
                "Assets/DrawToPlayExamples/Demo/M21/Levels/M21ObjectKinds.asset");
            Assume.That(kinds, Is.Not.Null, "run Draw To Play Examples › M21 Waystation › Verify first");
            Assert.That(first, Does.Contain(kinds));
            LevelObjectKindDef shrine = LevelObjectKinds.Find("", "shrine");
            Assert.That(shrine, Is.Not.Null);
            Assert.That(LevelObjectKinds.Find(shrine.id, ""), Is.SameAs(shrine), "by id, from the same cache");
            Assert.That(LevelObjectKinds.Find("", ""), Is.Null);

            LevelObjectKinds.Forget();
            Assert.That(LevelObjectKinds.Registries(), Is.Not.SameAs(first), "a project change lists again");
        }
    }
}
