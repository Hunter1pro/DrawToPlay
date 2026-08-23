using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using PowerOfFire.DrawToPlay.Examples.Arena;
using UnityEditor;
using UnityEngine;

namespace PowerOfFire.DrawToPlay.Tests
{
    /// <summary>
    /// M43.7 — a level per mode, designed for its rules: every mode row plays its own level
    /// and that level is stamped with the mode's name (typed once, read from the content
    /// parameter); the energy level carries its three nodes and one blue ally; the free-for-all
    /// tower is teamless so everyone is everyone's foe; and the session opens on a menu that
    /// waits for the pick — the button writes <c>mode.start</c>, the travel key is the answer.
    /// </summary>
    [TestFixture]
    public sealed class ArenaLevelsTests
    {
        private const string k_Root = "Assets/DrawToPlayExamples/Demo/Arena";
        private const string k_ModesPath = k_Root + "/Registries/ArenaModes.asset";
        private const string k_LevelsPath = k_Root + "/Registries/ArenaLevels.asset";
        private const string k_UiPath = k_Root + "/Registries/ArenaUi.asset";
        private const string k_SessionTreePath = k_Root + "/Gameplay/ArenaSessionTree.asset";

        private ArenaModeRegistry m_Modes;
        private LevelRegistry m_Levels;

        [SetUp]
        public void SetUp()
        {
            m_Modes = AssetDatabase.LoadAssetAtPath<ArenaModeRegistry>(k_ModesPath);
            m_Levels = AssetDatabase.LoadAssetAtPath<LevelRegistry>(k_LevelsPath);
            Assert.That(m_Modes, Is.Not.Null, "run Draw To Play Examples › Arena › Verify first");
            Assert.That(m_Levels, Is.Not.Null);
        }

        private LevelDef LevelOf(string mode)
        {
            ArenaModeDef row = m_Modes.entries.FirstOrDefault(r => r != null && r.name == mode);
            Assert.That(row, Is.Not.Null, "no mode row '" + mode + "'");
            var level = m_Levels.FindByName(row.level) as LevelDef;
            Assert.That(level, Is.Not.Null, "mode '" + mode + "' plays level '" + row.level + "' which is not in the catalog");
            return level;
        }

        [Test]
        public void EveryMode_PlaysItsOwnLevel_AndTheLevelKnowsIt()
        {
            var seen = new HashSet<string>();
            foreach (ArenaModeDef row in m_Modes.entries)
            {
                LevelDef level = LevelOf(row.name);
                Assert.That(seen.Add(level.name), Is.True,
                    "two modes share level '" + level.name + "' — each mode has its own place");
                GraphTaskParameter stamp = level.parameters?.FirstOrDefault(p => p.name == "mode");
                Assert.That(stamp, Is.Not.Null, level.name + " has no 'mode' parameter");
                Assert.That(stamp.stringValue, Is.EqualTo(row.name),
                    level.name + " is stamped '" + stamp.stringValue + "', not its mode");
                Assert.That(AssetDatabase.LoadAssetAtPath<SceneAsset>(level.scenePath), Is.Not.Null,
                    level.name + "'s scene is missing at " + level.scenePath);
            }
        }

        [Test]
        public void TheConduit_HasThreeNodes_AndOneBlueAlly()
        {
            LevelDef level = LevelOf("energy");
            List<LevelObjectDef> rows = level.objects.entries;
            Assert.That(rows.Count(r => r.kind.entryName == "node"), Is.EqualTo(3),
                "energy is fought over three nodes");
            bool Team(LevelObjectDef r, string team) => r.tags.Any(t => t != null && t.tag == team);
            Assert.That(rows.Any(r => r.kind.entryName == "enemy" && Team(r, ArenaTeams.Blue)), Is.True,
                "the player holds the left with one ally");
            Assert.That(rows.Count(r => r.kind.entryName == "enemy" && Team(r, ArenaTeams.Red)),
                Is.GreaterThanOrEqualTo(2), "the other side is a side");
        }

        [Test]
        public void TheTower_IsTeamless_SoEveryoneIsEveryonesFoe()
        {
            LevelDef level = LevelOf("free-for-all");
            foreach (LevelObjectDef row in level.objects.entries)
            {
                Assert.That(row.tags.Any(t => t != null && t.tag.StartsWith("team.")), Is.False,
                    "free-for-all placement '" + row.name + "' carries a team");
            }
        }

        [Test]
        public void TheSession_OpensOnTheMenu_AndTheMenuWaitsForThePick()
        {
            var ui = AssetDatabase.LoadAssetAtPath<UiRegistry>(k_UiPath);
            UiDef menuRow = ui.entries.FirstOrDefault(r => r != null && r.name == "menu");
            Assert.That(menuRow, Is.Not.Null, "no ui.menu row");
            Assert.That(menuRow.kind, Is.EqualTo(UiKind.Screen), "the menu is a screen, not a widget");
            var view = menuRow.prefab != null ? menuRow.prefab.GetComponent<ArenaMenuView>() : null;
            Assert.That(view, Is.Not.Null, "the menu prefab has no ArenaMenuView");
            Assert.That(view.catalog, Is.EqualTo(m_Modes), "the menu offers the mode catalog");

            Object[] parts = AssetDatabase.LoadAllAssetsAtPath(k_SessionTreePath);
            var menu = parts.OfType<StateTreeNodeAsset>().FirstOrDefault(n => n.nodeId == "menu");
            Assert.That(menu, Is.Not.Null, "the session tree has no menu state");

            var show = menu.tasks.OfType<ShowUiTask>().FirstOrDefault();
            Assert.That(show, Is.Not.Null, "the menu state shows nothing");
            Assert.That(show.holdWhileShown, Is.True,
                "a fire-and-forget show completes at once and its own exit hides the menu "
                + "the same frame — the holding-panel pattern keeps the task Running");
            Assert.That(show.hideOnExit, Is.True, "picking a mode leaves the menu behind");
            Assert.That(show.ui.entryName, Is.EqualTo("menu"));
            StateTreeTransition pick = menu.transitions.FirstOrDefault(t => t.targetNodeId == "travel");
            Assert.That(pick, Is.Not.Null, "the menu leads nowhere");
            var picked = pick.condition as HasBlackboardKeyCondition;
            Assert.That(picked, Is.Not.Null, "the menu waits on a key, not a clock");
            Assert.That(picked.key.Value, Is.EqualTo(LevelService.GotoKey),
                "the pick is the travel key the mode subsystem writes");
        }
    }
}
