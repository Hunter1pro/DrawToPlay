using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace PowerOfFire.DrawToPlay.Editor
{
    /// <summary>
    /// The Guide window: a registered <see cref="EditorGuide"/> played chapter by chapter, one open step card at a
    /// time. A step's check is asked when the Editor tells us something changed — the project, the hierarchy, an
    /// object, the selection, play mode, a scene, focus — a moment later and once, never per repaint.
    /// </summary>
    public sealed class EditorGuideWindow : EditorWindow
    {
        public const string ChapterClass = "dtp-guide-chapter";
        public const string StepClass = "dtp-guide-step";
        public const string LineClass = "dtp-guide-line";
        public const string CheckClass = "dtp-guide-check";
        public const string StatusClass = "dtp-guide-status";
        public const string BannerClass = "dtp-guide-banner";
        public const string TermClass = "dtp-guide-term";
        public const string ShowClass = "dtp-guide-show";
        public const string DoClass = "dtp-guide-do";
        public const string BackClass = "dtp-guide-back";
        public const string SkipClass = "dtp-guide-skip";
        public const string NextClass = "dtp-guide-next";
        public const string EndClass = "dtp-guide-end";

        private static readonly Color Accent = new Color(0.45f, 0.8f, 1f);
        private static readonly Color Good = new Color(0.45f, 0.85f, 0.5f);
        private static readonly Color Warn = new Color(1f, 0.7f, 0.35f);
        private static readonly Color Dim = new Color(0.6f, 0.6f, 0.6f);

        private const long SettleMs = 250;

        [SerializeField] private string m_GuideId;

        private GuideRun m_Run;
        private VisualElement m_Banner;
        private VisualElement m_Chapters;
        private VisualElement m_Page;
        private ScrollView m_PageScroll;
        private IVisualElementScheduledItem m_Pending;
        private string m_BannerText;

        public static EditorGuideWindow Open(string guideId = null)
        {
            var window = GetWindow<EditorGuideWindow>();
            EditorGuide guide = EditorGuides.Find(guideId);
            if (guide != null && (window.m_Run == null || window.m_Run.guide != guide))
                window.Play(guide, null);
            window.Show();
            window.Focus();
            return window;
        }

        public GuideRun run => m_Run;

        /// <summary>Plays <paramref name="guide"/>, its progress in <paramref name="progressPath"/> (null: the
        /// designer's own file under UserSettings) — a test's throwaway file.</summary>
        public void Play(EditorGuide guide, string progressPath)
        {
            m_GuideId = guide != null ? guide.id : null;
            m_Run = guide != null ? new GuideRun(guide, progressPath ?? EditorGuides.ProgressPath(guide.id)) : null;
            titleContent = new GUIContent(guide != null ? guide.title : "Guide");
            CreateGUI();
        }

        private void OnEnable()
        {
            EditorApplication.projectChanged += Changed;
            EditorApplication.hierarchyChanged += Changed;
            EditorApplication.playModeStateChanged += OnPlayMode;
            Selection.selectionChanged += Changed;
            Undo.undoRedoPerformed += Changed;
            ObjectChangeEvents.changesPublished += OnObjectsChanged;
            EditorSceneManager.sceneOpened += OnSceneOpened;
            windowFocusChanged += Changed;
        }

        private void OnDisable()
        {
            EditorApplication.projectChanged -= Changed;
            EditorApplication.hierarchyChanged -= Changed;
            EditorApplication.playModeStateChanged -= OnPlayMode;
            Selection.selectionChanged -= Changed;
            Undo.undoRedoPerformed -= Changed;
            ObjectChangeEvents.changesPublished -= OnObjectsChanged;
            EditorSceneManager.sceneOpened -= OnSceneOpened;
            windowFocusChanged -= Changed;
        }

        private void OnFocus() => Changed();

        private void OnPlayMode(PlayModeStateChange change) => Changed();

        private void OnObjectsChanged(ref ObjectChangeEventStream stream) => Changed();

        private void OnSceneOpened(Scene scene, OpenSceneMode mode) => Changed();

        /// <summary>Something changed: ask the checks once things settle.</summary>
        private void Changed()
        {
            if (m_Page == null)
                return;
            m_Pending ??= rootVisualElement.schedule.Execute(() => Refresh(false));
            m_Pending.ExecuteLater(SettleMs);
        }

        private void CreateGUI()
        {
            VisualElement root = rootVisualElement;
            root.Clear();
            root.style.paddingLeft = 6f;
            root.style.paddingRight = 6f;
            root.style.paddingTop = 6f;

            if (m_Run == null)
            {
                EditorGuide guide = EditorGuides.Find(m_GuideId);
                if (guide != null)
                {
                    m_GuideId = guide.id;
                    m_Run = new GuideRun(guide, EditorGuides.ProgressPath(guide.id));
                    titleContent = new GUIContent(guide.title);
                }
            }

            VisualElement header = StateTreeInspectorLook.Header("?", Accent, m_Run != null ? m_Run.guide.title : "Guide", out _);
            if (m_Run != null)
                header.Add(StateTreeInspectorLook.MenuButton("Guide", ShowMenu));
            root.Add(header);

            m_Banner = new VisualElement();
            m_Banner.AddToClassList(BannerClass);
            root.Add(m_Banner);

            var split = new TwoPaneSplitView(0, 190f, TwoPaneSplitViewOrientation.Horizontal)
            {
                viewDataKey = "PowerOfFire.DrawToPlay.Guide.Split"
            };
            split.style.flexGrow = 1f;
            root.Add(split);

            var chapters = new ScrollView(ScrollViewMode.Vertical) { style = { minWidth = 120f } };
            m_Chapters = new VisualElement { style = { paddingRight = 4f } };
            chapters.Add(m_Chapters);
            split.Add(chapters);

            m_PageScroll = new ScrollView(ScrollViewMode.Vertical) { style = { minWidth = 220f, flexGrow = 1f } };
            m_Page = new VisualElement { style = { paddingLeft = 8f, paddingRight = 6f, paddingBottom = 12f } };
            m_PageScroll.Add(m_Page);
            split.Add(m_PageScroll);

            m_Pending = null;
            Refresh(true);
        }

        private void ShowMenu(Rect at)
        {
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("Start over"), false, () =>
            {
                m_Run.StartOver();
                Refresh(true);
            });
            menu.DropDown(at);
        }

        /// <summary>Asks the open step's check and the banner; draws again when what shows changed.</summary>
        internal void Refresh(bool draw)
        {
            if (m_Run == null)
            {
                // Opened before the game registered its guide (a domain reload): attach once it is there.
                if (EditorGuides.Find(m_GuideId) != null && m_Page != null)
                    CreateGUI();
                else
                    Draw();
                return;
            }
            bool changed = m_Run.Refresh();
            string banner = Banner();
            if (draw || changed || banner != m_BannerText)
            {
                m_BannerText = banner;
                Draw();
            }
        }

        private string Banner()
        {
            try
            {
                return m_Run.guide.banner?.Invoke(m_Run);
            }
            catch (Exception e)
            {
                Debug.LogWarning("Guide: the banner failed — " + e.Message);
                return null;
            }
        }

        private void Draw()
        {
            if (m_Page == null)
                return;
            m_Banner.Clear();
            m_Chapters.Clear();
            m_Page.Clear();
            if (m_Run == null)
            {
                m_Page.Add(StateTreeInspectorLook.Note("No guide is registered in this project."));
                return;
            }
            if (!string.IsNullOrEmpty(m_BannerText))
                m_Banner.Add(Warning(m_BannerText));
            DrawChapters();
            DrawPage();
        }

        private void DrawChapters()
        {
            for (int i = 0; i < m_Run.guide.chapters.Count; i++)
            {
                GuideChapter chapter = m_Run.guide.chapters[i];
                int index = i;
                bool open = i == m_Run.chapterIndex;
                bool complete = m_Run.IsComplete(chapter);

                var item = new VisualElement();
                item.AddToClassList(ChapterClass);
                item.userData = chapter;
                item.style.paddingLeft = 6f;
                item.style.paddingRight = 4f;
                item.style.paddingTop = 3f;
                item.style.paddingBottom = 4f;
                item.style.marginBottom = 2f;
                item.style.borderTopLeftRadius = 3f;
                item.style.borderTopRightRadius = 3f;
                item.style.borderBottomLeftRadius = 3f;
                item.style.borderBottomRightRadius = 3f;
                item.style.borderLeftWidth = 3f;
                item.style.borderLeftColor = open ? Accent : complete ? Good : new Color(0f, 0f, 0f, 0f);
                if (open)
                    item.style.backgroundColor = StateTreeInspectorLook.CardBackground;
                item.RegisterCallback<ClickEvent>(_ =>
                {
                    m_Run.OpenChapter(index);
                    Refresh(true);
                    m_PageScroll.scrollOffset = Vector2.zero;
                });

                var title = new Label((complete ? "✓  " : "") + Say(chapter.title))
                {
                    style = { whiteSpace = WhiteSpace.Normal, unityFontStyleAndWeight = open ? FontStyle.Bold : FontStyle.Normal }
                };
                if (complete)
                    title.style.color = Good;
                item.Add(title);
                item.Add(Bar(m_Run.Passed(chapter), chapter.steps.Count, 4f));
                m_Chapters.Add(item);
            }
        }

        private void DrawPage()
        {
            GuideChapter chapter = m_Run.chapter;
            if (chapter == null)
                return;
            var title = new Label(Say(chapter.title))
            {
                style = { fontSize = 15, unityFontStyleAndWeight = FontStyle.Bold, whiteSpace = WhiteSpace.Normal, marginBottom = 2f }
            };
            m_Page.Add(title);
            if (!string.IsNullOrEmpty(chapter.intro))
                m_Page.Add(StateTreeInspectorLook.Note(Say(chapter.intro)));
            int passed = m_Run.Passed(chapter);
            VisualElement bar = Bar(passed, chapter.steps.Count, 6f);
            bar.style.marginTop = 4f;
            bar.style.marginBottom = 8f;
            bar.tooltip = passed + " of " + chapter.steps.Count + " steps";
            m_Page.Add(bar);

            for (int i = 0; i < chapter.steps.Count; i++)
                m_Page.Add(i == m_Run.stepIndex ? StepCard(chapter, chapter.steps[i]) : StepLine(chapter, chapter.steps[i], i));
            if (m_Run.stepIndex >= chapter.steps.Count)
                m_Page.Add(EndCard(chapter));
        }

        /// <summary>A step not open: ✓ done, ↷ skipped (both open again on a click), ○ still ahead.</summary>
        private VisualElement StepLine(GuideChapter chapter, GuideStep step, int index)
        {
            bool done = m_Run.IsDone(chapter, step);
            bool skipped = m_Run.IsSkipped(chapter, step);
            bool behind = index < m_Run.stepIndex;
            string glyph = done ? "✓" : skipped ? "↷" : "○";
            var line = new Label(glyph + "  " + Say(step.title) + (skipped ? "  (skipped)" : ""));
            line.AddToClassList(LineClass);
            line.userData = step;
            line.style.paddingLeft = 6f;
            line.style.paddingTop = 2f;
            line.style.paddingBottom = 2f;
            line.style.color = done ? Good : Dim;
            if (behind || done || skipped)
            {
                line.tooltip = "Open this step again.";
                line.RegisterCallback<ClickEvent>(_ =>
                {
                    m_Run.Go(index);
                    Refresh(true);
                });
            }
            else
                line.style.opacity = 0.7f;
            return line;
        }

        private VisualElement StepCard(GuideChapter chapter, GuideStep step)
        {
            bool done = m_Run.IsDone(chapter, step);
            VisualElement card = StateTreeInspectorLook.Card(StateTreeInspectorLook.CardBackground, done ? Good : Accent);
            card.AddToClassList(StepClass);
            card.userData = step;
            card.style.paddingTop = 6f;
            card.style.paddingBottom = 8f;
            card.style.marginTop = 2f;
            card.style.marginBottom = 6f;
            card.Add(StateTreeInspectorLook.CardHeader(done ? "✓" : "●", done ? Good : Accent, Say(step.title), out Label title));
            title.style.fontSize = 13;
            title.style.whiteSpace = WhiteSpace.Normal;

            if (!string.IsNullOrEmpty(step.text))
                card.Add(Text(Say(step.text)));
            foreach (GuideTerm term in step.terms)
                card.Add(Term(term));
            if (!string.IsNullOrEmpty(m_Run.status))
            {
                Label status = Warning(m_Run.status);
                status.AddToClassList(StatusClass);
                card.Add(status);
            }
            if (!string.IsNullOrEmpty(step.why))
            {
                var why = new Foldout { text = "Why", value = false, style = { marginTop = 4f } };
                why.Add(StateTreeInspectorLook.Note(Say(step.why)));
                card.Add(why);
            }

            if (step.show != null || step.doIt != null)
            {
                var actions = new VisualElement { style = { flexDirection = FlexDirection.Row, flexWrap = Wrap.Wrap, marginTop = 6f } };
                if (step.show != null)
                    actions.Add(ActionButton("Show me", Say(step.showTip), ShowClass, () => step.show(m_Run)));
                if (step.doIt != null)
                    actions.Add(ActionButton(Say(step.doItLabel), Say(step.doItTip), DoClass, () => step.doIt(m_Run)));
                card.Add(actions);
            }

            if (step.isTask)
            {
                var check = new Label(done
                    ? "✓  " + (string.IsNullOrEmpty(step.praise) ? "Done." : Say(step.praise))
                    : "○  " + (string.IsNullOrEmpty(step.waitingFor) ? "Waiting for you to do it." : Say(step.waitingFor)));
                check.AddToClassList(CheckClass);
                check.style.whiteSpace = WhiteSpace.Normal;
                check.style.marginTop = 6f;
                check.style.color = done ? Good : Dim;
                if (done)
                    check.style.unityFontStyleAndWeight = FontStyle.Bold;
                card.Add(check);
            }

            var nav = new VisualElement { style = { flexDirection = FlexDirection.Row, marginTop = 8f } };
            Button back = Nav("◀ Back", BackClass, () => m_Run.Back());
            back.SetEnabled(m_Run.canBack);
            nav.Add(back);
            nav.Add(new VisualElement { style = { flexGrow = 1f } });
            if (step.isTask && !done)
            {
                Button skip = Nav("Skip", SkipClass, () => m_Run.Skip());
                skip.tooltip = "Move on without doing it; the step stays open to come back to.";
                nav.Add(skip);
            }
            Button next = Nav("Next ▶", NextClass, () => m_Run.Next());
            next.SetEnabled(m_Run.canNext);
            if (!m_Run.canNext)
                next.tooltip = "Opens once the step is ✓ — or Skip it.";
            nav.Add(next);
            card.Add(nav);
            return card;
        }

        private VisualElement EndCard(GuideChapter chapter)
        {
            VisualElement card = StateTreeInspectorLook.Card(StateTreeInspectorLook.CardBackground, Good);
            card.AddToClassList(EndClass);
            card.style.paddingTop = 6f;
            card.style.paddingBottom = 8f;
            card.Add(StateTreeInspectorLook.CardHeader("✓", Good, "Chapter complete", out _));
            int skipped = 0;
            foreach (GuideStep step in chapter.steps)
            {
                if (m_Run.IsSkipped(chapter, step))
                    skipped++;
            }
            if (skipped > 0)
                card.Add(Text(skipped + (skipped == 1 ? " step" : " steps") + " skipped — click one above to go back to it."));
            int at = m_Run.chapterIndex;
            var nav = new VisualElement { style = { flexDirection = FlexDirection.Row, marginTop = 6f } };
            Button back = Nav("◀ Back", BackClass, () => m_Run.Back());
            back.SetEnabled(m_Run.canBack);
            nav.Add(back);
            nav.Add(new VisualElement { style = { flexGrow = 1f } });
            if (at + 1 < m_Run.guide.chapters.Count)
            {
                GuideChapter next = m_Run.guide.chapters[at + 1];
                nav.Add(Nav("Next: " + Say(next.title) + " ▶", NextClass, () => m_Run.OpenChapter(at + 1)));
            }
            else
                card.Add(Text("That is the whole guide. Nice work."));
            card.Add(nav);
            return card;
        }

        private Button ActionButton(string label, string tip, string className, Action run)
        {
            var button = new Button(() =>
            {
                try
                {
                    run();
                }
                catch (Exception e)
                {
                    Debug.LogWarning("Guide: " + label + " failed — " + e.Message);
                }
                Changed();
            }) { text = label, tooltip = tip ?? "" };
            button.AddToClassList(className);
            return button;
        }

        private Button Nav(string label, string className, Action move)
        {
            var button = new Button(() =>
            {
                move();
                Refresh(true);
                VisualElement open = m_Page.Q(className: StepClass) ?? m_Page.Q(className: EndClass);
                if (open != null)
                    m_PageScroll.schedule.Execute(() => m_PageScroll.ScrollTo(open));
            }) { text = label };
            button.AddToClassList(className);
            return button;
        }

        /// <summary>A text with the run's own names in it.</summary>
        private string Say(string text) => m_Run != null ? m_Run.guide.Say(m_Run, text) : text;

        private static Label Text(string text)
        {
            var label = new Label(text) { style = { whiteSpace = WhiteSpace.Normal, marginTop = 4f } };
            return label;
        }

        private static Label Warning(string text)
        {
            var label = new Label("⚠  " + text)
            {
                style = { whiteSpace = WhiteSpace.Normal, color = Warn, marginTop = 4f, marginBottom = 4f, unityFontStyleAndWeight = FontStyle.Bold }
            };
            return label;
        }

        private VisualElement Term(GuideTerm term)
        {
            var row = new VisualElement();
            row.AddToClassList(TermClass);
            row.style.marginTop = 4f;
            row.style.paddingLeft = 6f;
            row.style.paddingTop = 2f;
            row.style.paddingBottom = 3f;
            row.style.borderLeftWidth = 2f;
            row.style.borderLeftColor = Accent;
            row.Add(new Label(Say(term.word)) { style = { unityFontStyleAndWeight = FontStyle.Bold } });
            row.Add(new Label(Say(term.meaning)) { style = { whiteSpace = WhiteSpace.Normal, opacity = 0.85f } });
            return row;
        }

        private static VisualElement Bar(int passed, int count, float height)
        {
            var track = new VisualElement
            {
                style =
                {
                    height = height, marginTop = 3f, backgroundColor = new Color(0f, 0f, 0f, 0.25f),
                    borderTopLeftRadius = height / 2f, borderTopRightRadius = height / 2f,
                    borderBottomLeftRadius = height / 2f, borderBottomRightRadius = height / 2f
                }
            };
            var fill = new VisualElement
            {
                style =
                {
                    height = height, width = Length.Percent(count > 0 ? 100f * passed / count : 0f),
                    backgroundColor = passed == count && count > 0 ? Good : Accent,
                    borderTopLeftRadius = height / 2f, borderTopRightRadius = height / 2f,
                    borderBottomLeftRadius = height / 2f, borderBottomRightRadius = height / 2f
                }
            };
            track.Add(fill);
            return track;
        }
    }
}
