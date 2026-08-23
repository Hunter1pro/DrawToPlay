using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using PowerOfFire.DrawToPlay.Examples;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace PowerOfFire.DrawToPlay.Tests
{
    /// <summary>
    /// M42.2 — the joystick is a HUD row. The session shows it with the other HUDs, the UI
    /// service fills its input at spawn, and the input it writes is the session's: a stick
    /// holds nothing of a level's, so nothing re-finds it when the level changes.
    /// </summary>
    [TestFixture]
    public sealed class OutpostJoystickTests
    {
        private readonly List<Object> m_Junk = new List<Object>();
        private StateTreeContextHost m_Root;

        [SetUp]
        public void SetUp()
        {
            var go = new GameObject("Root") { hideFlags = HideFlags.HideAndDontSave };
            m_Junk.Add(go);
            m_Root = go.AddComponent<StateTreeContextHost>();
            m_Root.kind = StateTreeContextKind.Root;
            m_Root.autoStart = false;
            m_Root.Register();
        }

        [TearDown]
        public void TearDown()
        {
            if (m_Root != null)
                m_Root.Unregister();
            for (int i = 0; i < m_Junk.Count; i++)
            {
                if (m_Junk[i] != null)
                    Object.DestroyImmediate(m_Junk[i]);
            }
            m_Junk.Clear();
        }

        [Test]
        public void AShownJoystick_IsGivenTheSessionsInput_AtSpawn()
        {
            var input = new OutpostInputService();
            m_Root.Provide(input);

            var registry = ScriptableObject.CreateInstance<UiRegistry>();
            m_Junk.Add(registry);
            var def = ScriptableObject.CreateInstance<ServiceDef>();
            def.serviceName = "ui";
            def.scope = StateTreeContextKind.Root;
            def.registry = registry;
            m_Junk.Add(def);
            var ui = new UiService(m_Root, def);
            m_Root.Provide(ui);

            var template = new GameObject("Ui_joystick") { hideFlags = HideFlags.HideAndDontSave };
            template.SetActive(false);
            template.AddComponent<UIDocument>();
            template.AddComponent<OutpostJoystickView>();
            m_Junk.Add(template);
            var row = new UiDef { id = "ui.joystick", name = "joystick", kind = UiKind.Widget, prefab = template };
            registry.entries.Add(row);

            GameObject shown = ui.Show(row);
            m_Junk.Add(shown);
            var view = shown.GetComponent<OutpostJoystickView>();
            Assert.That(view, Is.Not.Null);
            Assert.That(view.shownBy, Is.SameAs(m_Root), "a session's screen");
            object held = typeof(OutpostJoystickView)
                .GetField("m_Input", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(view);
            Assert.That(held, Is.SameAs(input), "the session's input, filled at spawn — no Start, no retry");
        }

        [Test]
        public void TheWaystation_ShowsTheJoystickAsARow_AndNoLevelSceneHoldsOne()
        {
            var rows = AssetDatabase.LoadAssetAtPath<UiRegistry>(
                "Assets/DrawToPlayExamples/Demo/M21/Registries/M21Ui.asset");
            Assume.That(rows, Is.Not.Null, "run Verify M21 Waystation first");
            UiDef joystick = rows.FindByName("joystick") as UiDef;
            Assert.That(joystick, Is.Not.Null, "the joystick is a row");
            Assert.That(joystick.prefab.GetComponent<OutpostJoystickView>(), Is.Not.Null);
            Assert.That(joystick.kind, Is.EqualTo(UiKind.Widget),
                "a control that stays under every screen — a Screen would be hidden by the HUD");
            UiDef hud = rows.FindByName("hud") as UiDef;
            Assert.That(joystick.sortingOrder, Is.LessThan(hud.sortingOrder),
                "the bottom of the stack — every other panel sits above its touch zone");

            var session = AssetDatabase.LoadAssetAtPath<StateTreeAsset>(
                "Assets/DrawToPlayExamples/Demo/M21/Levels/M21SessionTree.asset");
            bool shown = false;
            foreach (Object sub in AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GetAssetPath(session)))
            {
                if (sub is ShowUiTask show && show.ui.entryId == "ui.joystick")
                    shown = true;
            }
            Assert.That(shown, Is.True, "the session's Setup shows it with the other HUDs");
        }
    }
}
