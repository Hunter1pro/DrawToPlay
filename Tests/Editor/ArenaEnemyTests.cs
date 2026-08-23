using System.Collections.Generic;
using NUnit.Framework;
using PowerOfFire.DrawToPlay.Examples.Arena;
using UnityEditor;
using UnityEngine;

namespace PowerOfFire.DrawToPlay.Tests
{
    /// <summary>
    /// M43.4 — enemies are trees over split tasks. Teams are tags; each strategy tree arms
    /// its own weapon in its first state and fights in the shape its weapon wants: the rusher
    /// swaps seek/attack on distance, the sniper backs off when crowded, the zoner holds its
    /// post and lobs without asking for a clear line.
    /// </summary>
    [TestFixture]
    public sealed class ArenaEnemyTests
    {
        private const string k_Rusher = "Assets/DrawToPlayExamples/Demo/Arena/Gameplay/ArenaAI_Rusher.asset";
        private const string k_Sniper = "Assets/DrawToPlayExamples/Demo/Arena/Gameplay/ArenaAI_Sniper.asset";
        private const string k_Zoner = "Assets/DrawToPlayExamples/Demo/Arena/Gameplay/ArenaAI_Zoner.asset";

        [Test]
        public void TeamsAreTags_AndTheTeamlessFightEveryone()
        {
            var a = new GameObject("A") { hideFlags = HideFlags.HideAndDontSave };
            var b = new GameObject("B") { hideFlags = HideFlags.HideAndDontSave };
            var c = new GameObject("C") { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                a.SetActive(false); b.SetActive(false); c.SetActive(false);
                ArenaFighter red = a.AddComponent<ArenaFighter>();
                red.tags.Add("team.red");
                ArenaFighter red2 = b.AddComponent<ArenaFighter>();
                red2.tags.Add("team.red");
                ArenaFighter blue = c.AddComponent<ArenaFighter>();
                blue.tags.Add("team.blue");
                Assert.That(red.IsFoeOf(red2), Is.False, "same team");
                Assert.That(red.IsFoeOf(blue), Is.True, "other team");
                Assert.That(red.IsFoeOf(red), Is.False, "never its own foe");
                blue.dead = true;
                Assert.That(red.IsFoeOf(blue), Is.False, "the dead are nobody's foe");
                red.tags.Clear();
                blue.dead = false;
                Assert.That(red.IsFoeOf(red2), Is.True, "the teamless fight everyone");
            }
            finally
            {
                Object.DestroyImmediate(a); Object.DestroyImmediate(b); Object.DestroyImmediate(c);
            }
        }

        [Test]
        public void EachStrategyTree_ArmsItsOwnWeapon_AndFightsItsOwnShape()
        {
            (string path, string weapon)[] trees =
            {
                (k_Rusher, "shotgun"),
                (k_Sniper, "rifle"),
                (k_Zoner, "launcher")
            };
            foreach ((string path, string weapon) in trees)
            {
                var tree = AssetDatabase.LoadAssetAtPath<StateTreeAsset>(path);
                Assume.That(tree, Is.Not.Null, path + " — run Draw To Play Examples › Arena › Verify");
                List<Object> subs = new List<Object>(AssetDatabase.LoadAllAssetsAtPath(path));
                ArenaGiveWeaponTask give = subs.Find(s => s is ArenaGiveWeaponTask) as ArenaGiveWeaponTask;
                Assert.That(give, Is.Not.Null, tree.name + ": arms itself");
                Assert.That(give.weapon.entryName, Is.EqualTo(weapon), tree.name + ": its own weapon");
                Assert.That(subs.Exists(s => s is ArenaSenseFoeTask), Is.True, tree.name + ": senses");
                Assert.That(subs.Exists(s => s is ArenaAimAtFoeTask), Is.True, tree.name + ": fires");
            }

            // The shapes that make them THREE strategies, not one.
            var rusher = new List<Object>(AssetDatabase.LoadAllAssetsAtPath(k_Rusher));
            Assert.That(rusher.Exists(s => s is StateTreeNodeAsset node && node.nodeId == "attack"), Is.True,
                "the rusher has a close-range state");
            var sniper = new List<Object>(AssetDatabase.LoadAllAssetsAtPath(k_Sniper));
            Assert.That(sniper.Exists(s => s is ArenaChaseFoeTask chase && chase.mode == ArenaChaseFoeTask.Mode.Away),
                Is.True, "the sniper backs off");
            var zoner = new List<Object>(AssetDatabase.LoadAllAssetsAtPath(k_Zoner));
            ArenaAimAtFoeTask lob = zoner.Find(s => s is ArenaAimAtFoeTask) as ArenaAimAtFoeTask;
            Assert.That(lob.requiresLineOfSight, Is.False, "the zoner lobs over cover");
            Assert.That(lob.lobPerMetre, Is.GreaterThan(0f), "on an arc");
            Assert.That(zoner.Exists(s => s is ArenaChaseFoeTask chase && chase.mode == ArenaChaseFoeTask.Mode.HoldPost),
                Is.True, "and holds its post");
        }
    }
}
