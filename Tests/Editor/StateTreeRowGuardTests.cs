using NUnit.Framework;
using PowerOfFire.DrawToPlay.Editor;
using UnityEditor;
using UnityEngine;

namespace PowerOfFire.DrawToPlay.Tests
{
    public sealed class StateTreeRowGuardTests
    {
        private const string k_Folder = "Assets/__RowGuardTests";

        [SetUp]
        public void SetUp()
        {
            if (!AssetDatabase.IsValidFolder(k_Folder))
                AssetDatabase.CreateFolder("Assets", "__RowGuardTests");
        }

        [TearDown]
        public void TearDown()
        {
            AssetDatabase.DeleteAsset(k_Folder);
            AssetWireScan.Invalidate();
        }

        /// <summary>The use given up in the editor and not saved yet: no file changed, and the remove is still allowed.</summary>
        [Test]
        public void AUseTakenAwayBeforeASave_NoLongerHoldsTheRow()
        {
            var cues = ScriptableObject.CreateInstance<CueRegistry>();
            var impact = new CueDef { id = "cue.row-guard-test", name = "row-guard-test" };
            cues.entries.Add(impact);
            AssetDatabase.CreateAsset(cues, k_Folder + "/Cues.asset");

            var effects = ScriptableObject.CreateInstance<EffectRegistry>();
            var hit = new EffectDef { id = "effect.row-guard-test", name = "row-guard-test-hit" };
            hit.cue.entryId = impact.id;
            hit.cue.entryName = impact.name;
            effects.entries.Add(hit);
            AssetDatabase.CreateAsset(effects, k_Folder + "/Effects.asset");

            Assert.IsNotNull(StateTreeRowGuard.WhyNotRemove(impact), "the effect names it");

            hit.cue.entryId = "";
            hit.cue.entryName = "";
            EditorUtility.SetDirty(effects);

            Assert.IsNull(StateTreeRowGuard.WhyNotRemove(impact), "nothing names it now, saved or not");
        }
    }
}
