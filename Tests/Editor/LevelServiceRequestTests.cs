using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace PowerOfFire.DrawToPlay.Tests
{
    /// <summary>
    /// M34 — travel is DECLARED: the session's level machinery has a def like every other
    /// subsystem, so its verbs can be asked for by key instead of only by holding the class.
    ///
    /// What that buys is the typed door. The def names the level catalog it manages, so a
    /// request for a level nobody has is refused with the name in the message — at the moment
    /// somebody asks, rather than three frames later in a scene load that finds nothing.
    /// </summary>
    [TestFixture]
    public sealed class LevelServiceRequestTests
    {
        private readonly List<Object> m_Junk = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < m_Junk.Count; i++)
            {
                if (m_Junk[i] != null)
                    Object.DestroyImmediate(m_Junk[i]);
            }
            m_Junk.Clear();
        }

        [Test]
        public void TravelIsARequest_AndALevelNobodyHasIsRefusedAtTheDoor()
        {
            var levels = ScriptableObject.CreateInstance<LevelRegistry>();
            m_Junk.Add(levels);
            levels.entries.Add(new LevelDef { id = "level.ridge", name = "ridge" });

            var def = ScriptableObject.CreateInstance<ServiceDef>();
            def.serviceName = "level";
            def.serviceTypeName = typeof(LevelService).FullName;
            def.scope = StateTreeContextKind.Root;
            def.registry = levels;
            def.requests.Add(new ServiceRequest
            {
                key = "level.goto",
                action = LevelService.GotoAction,
                description = "travel",
                namesRowOf = levels
            });
            m_Junk.Add(def);

            var rootObject = new GameObject("Session") { hideFlags = HideFlags.HideAndDontSave };
            m_Junk.Add(rootObject);
            StateTreeContextHost host = rootObject.AddComponent<StateTreeContextHost>();
            host.kind = StateTreeContextKind.Root;
            host.autoStart = false;
            host.Register();

            var travel = new LevelService(host, def);
            host.Provide(travel);
            travel.Tick(0.02f);

            travel.Request("level.goto", "ridge");
            travel.Tick(0.02f);

            Assert.That(host.Context.blackboard.TryGetValue(LevelService.GotoKey, out object asked),
                Is.True, "the declared request reaches the verb, and the verb writes the inbox");
            Assert.That(asked, Is.EqualTo("ridge"));

            // AND THE ONE NOBODY HAS is refused where it was asked, not where it would land.
            host.Context.blackboard.Remove(LevelService.GotoKey);
            LogAssert.Expect(LogType.Error, new Regex("names none of them"));
            travel.Request("level.goto", "atlantis");
            travel.Tick(0.02f);
            Assert.That(host.Context.blackboard.ContainsKey(LevelService.GotoKey), Is.False,
                "a refused request writes nothing — the session never hears about it");
        }
    }
}
