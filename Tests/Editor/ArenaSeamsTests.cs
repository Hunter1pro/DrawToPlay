using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using PowerOfFire.DrawToPlay.Editor;
using PowerOfFire.DrawToPlay.Examples.Arena;
using UnityEditor;
using UnityEngine;

namespace PowerOfFire.DrawToPlay.Tests
{
    /// <summary>
    /// M43.8 — the designer's seams: every arena def reads aloud with no broken clause; the
    /// modes subsystem shows its own scoreboard and its pick reacts through a DRAWN graph
    /// (baked from picked dropdowns, the value read from the request's own `.asked` key);
    /// travel is a declared ask; and the scoreboard's skin declares the verb the canvas says.
    /// </summary>
    [TestFixture]
    public sealed class ArenaSeamsTests
    {
        private const string k_Root = "Assets/DrawToPlayExamples/Demo/Arena";
        private const string k_ReactionPath = k_Root + "/Reactions/ArenaReaction_ModePicked.taskgraph";

        private static IEnumerable<ServiceDef> AllDefs()
        {
            foreach (string guid in AssetDatabase.FindAssets("t:ServiceDef", new[] { k_Root }))
            {
                var def = AssetDatabase.LoadAssetAtPath<ServiceDef>(AssetDatabase.GUIDToAssetPath(guid));
                if (def != null)
                    yield return def;
            }
        }

        [Test]
        public void EveryArenaDef_ReadsAloud_WithNoBrokenClause()
        {
            int count = 0;
            foreach (ServiceDef def in AllDefs())
            {
                count++;
                string sentence = DeclaredApi.Sentence(def);
                Assert.That(sentence, Is.Not.Empty, def.name);
                Assert.That(sentence, Does.Not.Contain("not declared by the class"),
                    def.name + " promises a verb its class does not have: " + sentence);
                Assert.That(sentence, Does.Not.Contain("served by nothing yet"),
                    def.name + " declares an ask nobody serves: " + sentence);
                Assert.That(sentence, Does.Not.Contain("?"),
                    def.name + " names a class that does not resolve: " + sentence);
            }
            Assert.That(count, Is.GreaterThanOrEqualTo(14), "the arena's defs are all readable");
        }

        [Test]
        public void TheModesDef_ShowsItsBoard_AndTravelIsADeclaredAsk()
        {
            ServiceDef modes = AllDefs().First(d => d.serviceName == "modes");
            string sentence = DeclaredApi.Sentence(modes);
            Assert.That(sentence, Does.Contain("shows score"),
                "the subsystem that owns the rules owns the screen that states them");
            Assert.That(sentence, Does.Contain("reacts with ArenaReaction_ModePicked"),
                "the pick's call-out is a drawn reaction, not a line of C#");
            Assert.That(sentence, Does.Contain("value names a row of ArenaModes"));

            ServiceDef level = AllDefs().First(d => d.serviceName == "level");
            Assert.That(DeclaredApi.Sentence(level), Does.Contain("ask it to level.goto"),
                "travel reads aloud — a graph can send the session somewhere");
        }

        [Test]
        public void TheModePickedReaction_BakedByPicking_SaysBannerFromTheAskedKey()
        {
            var says = new List<UiCallTask>();
            GraphTaskAsset program = null;
            foreach (Object part in AssetDatabase.LoadAllAssetsAtPath(k_ReactionPath))
            {
                if (part is UiCallTask call)
                    says.Add(call);
                program ??= part as GraphTaskAsset;
            }
            Assert.That(program, Is.Not.Null, "run Draw To Play Examples › Arena › Verify first");
            Assert.That(says, Has.Count.EqualTo(2), "the splash and its own take-down");
            Assert.That(says.All(c => c.ui.entryName == "score" && c.verb == "banner"), Is.True,
                "both say a verb the scoreboard's skin declares");
            UiCallTask splash = says.FirstOrDefault(c => c.argumentKey.Value == "mode.start.asked");
            Assert.That(splash, Is.Not.Null,
                "the splash reads the request's value from the key the base publishes");
            Assert.That(says.Any(c => string.IsNullOrEmpty(c.argumentKey.Value)
                && string.IsNullOrEmpty(c.argument)), Is.True,
                "the other call clears the banner after the graph's own wait");
        }

        [Test]
        public void TheScoreSkin_DeclaresTheBanner_AndAnswersIt()
        {
            DeclaredApi.Forget();
            Assert.That(DeclaredApi.Verbs("score"), Does.Contain("banner"),
                "the canvas dropdown offers what the skin declares");

            var go = new GameObject("Score") { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                var view = go.AddComponent<ArenaScoreView>();
                Assert.That(view.Call("banner", "deathmatch"), Is.True, "the declared verb is answered");
                Assert.That(view.Call("juggle", "x"), Is.False, "an undeclared verb is refused");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void TheDash_IsARow_WhoseCooldownTheChipDrains_AndItCutsAFire()
        {
            // M43.12 — the ability system made visible: the dash's cooldown lives on ITS ROW
            // (the chip only draws the fraction), and the tag vocabulary says an escape
            // outranks a volley — cancelTags, not code.
            var abilities = AssetDatabase.LoadAssetAtPath<AbilityRegistry>(
                k_Root + "/Registries/ArenaAbilities.asset");
            var dash = abilities.entries.FirstOrDefault(r => r != null && r.name == "dash");
            Assert.That(dash, Is.Not.Null, "the dash is a row");
            Assert.That(dash.cooldownSeconds, Is.GreaterThan(0f), "the row owns the cooldown");
            Assert.That(dash.tree, Is.Not.Null, "and its body is a drawn tree");
            Assert.That(dash.cancelTags, Does.Contain("fire"), "an escape outranks a volley");
            foreach (AbilityDef row in abilities.entries)
            {
                if (row != null && row.name.StartsWith("fire-"))
                    Assert.That(row.abilityTags, Does.Contain("fire"),
                        row.name + " must wear what the dash cancels");
            }

            ServiceDef overlay = AllDefs().First(d => d.serviceName == "overlay");
            string sentence = DeclaredApi.Sentence(overlay);
            Assert.That(sentence, Does.Contain("shows bars"));
            Assert.That(sentence, Does.Contain("shows dash"), "the chip is the overlay's screen too");
        }
    }
}
