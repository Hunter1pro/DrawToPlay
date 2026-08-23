using System.Collections.Generic;
using NUnit.Framework;
using PowerOfFire.DrawToPlay.Editor;
using UnityEditor;
using UnityEngine;

namespace PowerOfFire.DrawToPlay.Tests
{
    /// <summary>
    /// M41's exit, made literal: every def in the waystation reads as a sentence a designer
    /// would write, derived from the same model the inspector, the API window and the node
    /// dropdowns read — nothing typed that the class already said, and one subsystem that
    /// exists with no class at all.
    /// </summary>
    [TestFixture]
    public sealed class ServiceDefSentenceTests
    {
        private const string k_Registries = "Assets/DrawToPlayExamples/Demo/M21/Registries/";

        private static ServiceDef Def(string name)
        {
            var def = AssetDatabase.LoadAssetAtPath<ServiceDef>(k_Registries + name + ".asset");
            Assume.That(def, Is.Not.Null, name + " — run Draw To Play Examples › M21 Waystation › Verify first");
            return def;
        }

        [Test]
        public void TheBench_ReadsAsOneSentence()
        {
            string sentence = DeclaredApi.Sentence(Def("M21CraftService"));
            Assert.That(sentence, Does.StartWith("the craft (CraftService · Root): "));
            Assert.That(sentence, Does.Contain("ask it to craft.begin → answers CraftResult"));
            Assert.That(sentence, Does.Contain("reacts with M21Reaction_Crafted"));
            Assert.That(sentence, Does.Contain("value names a row of M21Recipes"));
            Assert.That(sentence, Does.Contain("empty means "));
            Assert.That(sentence, Does.Contain("announces craft.last (CraftResult)"));
            Assert.That(sentence, Does.Contain("shows craft"));
        }

        [Test]
        public void TheUiService_IsInfrastructure_InOneSentence()
        {
            Assert.That(DeclaredApi.Sentence(Def("M21UiService")),
                Is.EqualTo("the ui (UiService · Root): offers nothing to a flow — it is infrastructure."));
        }

        [Test]
        public void TheShrine_HasNoClass_AndSaysWhoServesIt()
        {
            string sentence = DeclaredApi.Sentence(Def("M21Kind_Shrine"));
            Assert.That(sentence, Does.StartWith("the shrine (no class · Root): "));
            Assert.That(sentence, Does.Contain("is a body — M21Shrine, wears shrine"));
            Assert.That(sentence, Does.Contain("ask it to shrine.pray → served by the graph M21Reaction_Prayed"));
        }

        [Test]
        public void TheBag_AsksAreVerbs_ThatAnswerByBeingDone()
        {
            string sentence = DeclaredApi.Sentence(Def("M21InventoryService"));
            Assert.That(sentence, Does.Contain("ask it to bag.add → value names a row of M21Items"));
            Assert.That(sentence, Does.Not.Contain("answers"), "bag.add is done when it is done");
        }

        [Test]
        public void TheQuestLine_BuildsItsMarker()
        {
            string sentence = DeclaredApi.Sentence(Def("M21ObjectiveService"));
            Assert.That(sentence, Does.Contain("builds M21ObjectiveMarker"), "M42.3: the marker is the objective's");
            Assert.That(sentence, Does.Not.Contain("is a body"), "a subsystem with a class builds, a kind is");
        }

        [Test]
        public void AKind_IsABodyThatHas()
        {
            string sentence = DeclaredApi.Sentence(Def("M21Kind_Player"));
            Assert.That(sentence, Does.Contain("is a body — M21Player, wears player"));
            Assert.That(sentence, Does.Contain("has health"));
        }

        /// <summary>Every waystation def has a sentence with no hole in it, and exactly one
        /// subsystem with an Ask has no class — the shrine.</summary>
        [Test]
        public void EveryWaystationDef_ReadsPlainly_AndOneSubsystemHasNoClass()
        {
            var classless = new List<string>();
            int defs = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:ServiceDef", new[] { k_Registries.TrimEnd('/') }))
            {
                var def = AssetDatabase.LoadAssetAtPath<ServiceDef>(AssetDatabase.GUIDToAssetPath(guid));
                if (def == null)
                    continue;
                defs++;
                string sentence = DeclaredApi.Sentence(def);
                Assert.That(sentence, Does.EndWith("."), def.name);
                Assert.That(sentence, Does.Not.Contain("not declared by the class"), def.name + ": an action the class does not declare");
                Assert.That(sentence, Does.Not.Contain("served by nothing"), def.name + ": an ask nobody serves");
                Assert.That(sentence, Does.Not.Contain("?"), def.name + ": a class that does not resolve");
                if (sentence.Contains("(no class") && sentence.Contains("ask it to"))
                    classless.Add(def.name);
            }
            Assume.That(defs, Is.GreaterThan(10), "the waystation is generated");
            Assert.That(classless, Is.EqualTo(new[] { "M21Kind_Shrine" }));
        }
    }
}
