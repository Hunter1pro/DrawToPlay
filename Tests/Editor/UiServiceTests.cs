using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace PowerOfFire.DrawToPlay.Tests
{
    /// <summary>
    /// The UI pass: rows say WHAT exists on screen (prefab, kind, PANEL ORDER AS DATA,
    /// declared parameters), the service is the open-state ledger (screens exclusive,
    /// re-show re-binds instead of duplicating, duplicate orders are a reported finding),
    /// and ShowUiTask makes a popup A STATE — shown on enter, hidden on exit, taken down
    /// by whatever pre-empts it.
    /// </summary>
    [TestFixture]
    public sealed class UiServiceTests
    {
        private readonly List<GameObject> m_Objects = new List<GameObject>();
        private readonly List<ScriptableObject> m_Assets = new List<ScriptableObject>();
        private readonly List<StateTreeContextHost> m_Hosts = new List<StateTreeContextHost>();

        private StateTreeContextHost m_Root;
        private UiService m_Service;
        private UiRegistry m_Registry;

        private sealed class RecordingView : UiViewBehaviour
        {
            public readonly List<string> bound = new List<string>();

            public override void Bind(IReadOnlyList<GraphTaskParameter> arguments)
            {
                bound.Clear();
                for (int i = 0; i < arguments.Count; i++)
                    bound.Add(arguments[i].name + "=" + arguments[i].stringValue);
            }
        }

        [SetUp]
        public void SetUp()
        {
            m_Registry = ScriptableObject.CreateInstance<UiRegistry>();
            m_Assets.Add(m_Registry);

            var def = ScriptableObject.CreateInstance<ServiceDef>();
            def.serviceName = "ui";
            def.scope = StateTreeContextKind.Root;
            def.registry = m_Registry;
            m_Assets.Add(def);

            var rootGo = new GameObject("Root");
            rootGo.hideFlags = HideFlags.HideAndDontSave;
            m_Objects.Add(rootGo);
            m_Root = rootGo.AddComponent<StateTreeContextHost>();
            m_Root.kind = StateTreeContextKind.Root;
            m_Root.autoStart = false;
            m_Root.Register();
            m_Hosts.Add(m_Root);

            m_Service = new UiService(m_Root, def);
            m_Root.Provide(m_Service);
        }

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < m_Hosts.Count; i++)
            {
                if (!ReferenceEquals(m_Hosts[i], null))
                    m_Hosts[i].Unregister();
            }
            for (int i = 0; i < m_Objects.Count; i++)
            {
                if (m_Objects[i] != null)
                    Object.DestroyImmediate(m_Objects[i]);
            }
            for (int i = 0; i < m_Assets.Count; i++)
            {
                if (m_Assets[i] != null)
                    Object.DestroyImmediate(m_Assets[i]);
            }
            m_Objects.Clear();
            m_Assets.Clear();
            m_Hosts.Clear();
        }

        private UiDef MakeRow(string rowName, UiKind kind, float order,
            bool withRecorder = false)
        {
            var template = new GameObject("Ui_" + rowName);
            template.hideFlags = HideFlags.HideAndDontSave;
            template.SetActive(false);
            if (withRecorder)
                template.AddComponent<RecordingView>();
            m_Objects.Add(template);

            var row = new UiDef
            {
                id = "ui." + rowName, name = rowName,
                kind = kind, prefab = template, sortingOrder = order
            };
            m_Registry.entries.Add(row);
            return row;
        }

        [Test]
        public void Show_InstantiatesOnce_ReShowReuses_HideDestroys()
        {
            UiDef row = MakeRow("panel", UiKind.Widget, 5f);

            GameObject first = m_Service.Show(row);
            Assert.IsNotNull(first);
            Assert.IsTrue(m_Service.IsShown("panel"));

            GameObject second = m_Service.Show(row);
            Assert.AreSame(first, second, "a re-show re-asserts, it does not duplicate");

            m_Service.Hide(row);
            Assert.IsFalse(m_Service.IsShown("panel"));
            Assert.IsTrue(first == null, "the view went with the ledger entry");
        }

        [Test]
        public void Screens_AreExclusive_PopupsAndWidgetsAreNot()
        {
            UiDef title = MakeRow("title", UiKind.Screen, 1f);
            UiDef hud = MakeRow("hud", UiKind.Screen, 2f);
            UiDef popup = MakeRow("confirm", UiKind.Popup, 3f);

            m_Service.Show(title);
            m_Service.Show(popup);
            m_Service.Show(hud);

            Assert.IsFalse(m_Service.IsShown("title"),
                "showing a screen hides its sibling screen — the kind's rule, not a chore");
            Assert.IsTrue(m_Service.IsShown("hud"));
            Assert.IsTrue(m_Service.IsShown("confirm"), "popups ride on top, untouched");
        }

        [Test]
        public void SortingOrder_IsAssertedFromTheRow()
        {
            UiDef row = MakeRow("layered", UiKind.Popup, 7f);
            row.prefab.AddComponent<UnityEngine.UIElements.UIDocument>();

            GameObject view = m_Service.Show(row);
            var document = view.GetComponentInChildren<UnityEngine.UIElements.UIDocument>(true);
            Assert.AreEqual(7f, document.sortingOrder,
                "who draws on top (and takes the press) comes from the ROW");
        }

        [Test]
        public void DuplicateOrders_AreAReportedFinding()
        {
            MakeRow("one", UiKind.Screen, 2f);
            MakeRow("two", UiKind.Popup, 2f);

            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex(
                "share sorting order"));
            m_Service.ValidateRows();
        }

        [Test]
        public void Arguments_OverrideTheRowsDeclaredDefaults_ById()
        {
            UiDef row = MakeRow("ask", UiKind.Popup, 4f, withRecorder: true);
            row.parameters.Add(new GraphTaskParameter
            {
                name = "title", kind = GraphTaskParameterKind.String,
                stringValue = "Are you sure?", id = "ui.ask.title"
            });
            row.parameters.Add(new GraphTaskParameter
            {
                name = "confirm", kind = GraphTaskParameterKind.String,
                stringValue = "Yes", id = "ui.ask.confirm"
            });

            GameObject view = m_Service.Show(row, new List<GraphTaskParameterOverride>
            {
                new GraphTaskParameterOverride
                {
                    name = "title", enabled = true,
                    stringValue = "Travel to the ridge?", id = "ui.ask.title"
                }
            });

            var recorder = view.GetComponent<RecordingView>();
            CollectionAssert.AreEqual(
                new[] { "title=Travel to the ridge?", "confirm=Yes" }, recorder.bound,
                "the show-site's answer replaced the default; the untouched knob kept its");
        }

        [Test]
        public void ShowUiTask_ShowsWhileTheStateRuns_AndExitTakesItDown()
        {
            UiDef row = MakeRow("paused", UiKind.Popup, 6f);

            var owner = new GameObject("mind");
            owner.hideFlags = HideFlags.HideAndDontSave;
            owner.transform.SetParent(m_Root.transform);
            owner.SetActive(false);
            m_Objects.Add(owner);

            var task = ScriptableObject.CreateInstance<ShowUiTask>();
            task.ui.entryName = "paused";
            m_Assets.Add(task);
            var context = new StateTreeContext(owner);

            Assert.AreEqual(StateTreeStatus.Running, task.OnTick(context, 0.1f),
                "the state owns the popup's lifetime — the task holds");
            Assert.IsTrue(m_Service.IsShown("paused"));
            Assert.AreEqual(StateTreeStatus.Running, task.OnTick(context, 0.1f));

            task.OnExit(context, StateTreeStatus.Cancelled);
            Assert.IsFalse(m_Service.IsShown("paused"),
                "pre-emption takes the popup with it — nothing can forget to close");
        }

        private sealed class InjectedView : UiViewBehaviour
        {
            [InjectService] public InventoryService inventory;
        }

        [Test]
        public void Show_InjectsSpawnedViews()
        {
            // Spawn-time is bind-time: the spawner fills the view's [InjectService]
            // fields — a view never polls for its services.
            var bagDef = ScriptableObject.CreateInstance<ServiceDef>();
            bagDef.serviceName = "inventory";
            bagDef.registry = ScriptableObject.CreateInstance<ItemRegistry>();
            m_Assets.Add(bagDef);
            m_Assets.Add(bagDef.registry);
            var inventory = new InventoryService(m_Root, bagDef);
            m_Root.Provide(inventory);

            UiDef row = MakeRow("bag", UiKind.Widget, 9f);
            row.prefab.AddComponent<InjectedView>();

            GameObject view = m_Service.Show(row);
            Assert.AreSame(inventory, view.GetComponent<InjectedView>().inventory,
                "the spawned view was handed the service, not left to find it");
        }
    }
}
