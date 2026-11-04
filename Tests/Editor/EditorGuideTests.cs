using System.IO;
using System.Linq;
using NUnit.Framework;
using PowerOfFire.DrawToPlay.Editor;
using UnityEngine;
using UnityEngine.UIElements;

namespace PowerOfFire.DrawToPlay.Tests
{
    /// <summary>
    /// The Guide: a task's Next opens only once its check has passed and stays open; Skip moves on and remembers;
    /// a note's Next is always open; the progress file round-trips, in a throwaway folder, one state per scope; a scope
    /// met for the first time catches up; a checklist chapter reads how things stand. The window is a UI Toolkit
    /// host that draws the chapters and the open card, and the Level Manifest shows its "?" while a guide is
    /// registered.
    /// </summary>
    [TestFixture]
    public sealed class EditorGuideTests
    {
        private string m_Folder;
        private bool m_Door;
        private EditorGuide m_Guide;
        private EditorGuideWindow m_Window;
        private LevelManifestWindow m_Manifest;

        [SetUp]
        public void SetUp()
        {
            m_Folder = Path.Combine(Path.GetTempPath(), "DrawToPlayGuideTests_" + System.Guid.NewGuid().ToString("N"));
            m_Door = false;
            m_Guide = new EditorGuide("test.guide", "Test Guide");
            GuideChapter first = m_Guide.Chapter("one", "One", "Two steps.");
            first.Step("door", "Place a door", "Drag it in.").Checks(_ => m_Door, "Waiting for a door.", "Nice — a door.");
            first.Step("note", "Read this", "Just a note.").Term("Door", "A thing that opens.");
            m_Guide.Chapter("two", "Two").Step("end", "The end", "Bye.");
        }

        [TearDown]
        public void TearDown()
        {
            EditorGuides.Unregister(m_Guide);
            if (m_Window != null)
                Object.DestroyImmediate(m_Window);
            if (m_Manifest != null)
                Object.DestroyImmediate(m_Manifest);
            if (Directory.Exists(m_Folder))
                Directory.Delete(m_Folder, true);
        }

        private string ProgressPath => Path.Combine(m_Folder, "Guides", "test.guide.json");

        [Test]
        public void ATask_OpensNext_OnlyOnceItsCheckPasses_AndStaysDone()
        {
            var run = new GuideRun(m_Guide, ProgressPath);
            Assert.AreEqual("door", run.step.id);
            Assert.IsFalse(run.canNext, "locked until the check passes");
            run.Next();
            Assert.AreEqual("door", run.step.id, "Next does nothing while locked");

            Assert.IsFalse(run.Refresh());
            m_Door = true;
            Assert.IsTrue(run.Refresh(), "the check flipped");
            Assert.IsTrue(run.justDone);
            Assert.IsTrue(run.canNext);
            m_Door = false;
            run.Refresh();
            Assert.IsTrue(run.canNext, "once done, a step stays done");

            run.Next();
            Assert.AreEqual("note", run.step.id);
            Assert.IsTrue(run.canNext, "a note is read and passed");
            run.Next();
            Assert.IsNull(run.step, "the chapter's end page");
            Assert.IsTrue(run.IsComplete(run.chapter));
            Assert.AreEqual(2, run.Passed(run.chapter));
        }

        [Test]
        public void Skip_MovesOn_RemembersTheStep_AndBackReturnsToIt()
        {
            var run = new GuideRun(m_Guide, ProgressPath);
            run.Skip();
            GuideChapter one = m_Guide.chapters[0];
            Assert.AreEqual("note", run.step.id);
            Assert.IsTrue(run.IsSkipped(one, one.steps[0]));
            Assert.IsFalse(run.IsDone(one, one.steps[0]));
            Assert.AreEqual(1, run.Passed(one));

            run.Back();
            Assert.AreEqual("door", run.step.id);
            m_Door = true;
            run.Refresh();
            Assert.IsTrue(run.IsDone(one, one.steps[0]), "done after all");
            Assert.IsFalse(run.IsSkipped(one, one.steps[0]));
        }

        [Test]
        public void Progress_RoundTrips_ThroughItsFile()
        {
            var run = new GuideRun(m_Guide, ProgressPath);
            m_Door = true;
            run.Refresh();
            run.Next();
            run.SetNote("made", "preset.x");
            run.OpenChapter(1);
            Assert.IsTrue(File.Exists(ProgressPath));

            var again = new GuideRun(m_Guide, ProgressPath);
            Assert.AreEqual(1, again.chapterIndex);
            GuideChapter one = m_Guide.chapters[0];
            Assert.IsTrue(again.IsDone(one, one.steps[0]));
            Assert.AreEqual(1, again.StepIndex(one), "chapter one stands at its note");
            Assert.AreEqual("preset.x", again.Note("made"));

            again.StartOver();
            var fresh = new GuideRun(m_Guide, ProgressPath);
            Assert.AreEqual(0, fresh.chapterIndex);
            Assert.IsFalse(fresh.IsDone(one, one.steps[0]));
            Assert.IsTrue(EditorGuides.ProgressPath("g").StartsWith("UserSettings/", System.StringComparison.Ordinal),
                "per user, never under Assets");
        }

        [Test]
        public void AScopedGuide_KeepsAStatePerScope_InOneFile_AndAFileFromBeforeScopesIsTheUnscopedState()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ProgressPath));
            File.WriteAllText(ProgressPath, "{\"chapter\":1,\"done\":[\"one/door\"],\"notes\":[{\"key\":\"practice.level\",\"value\":\"practice\"}]}");
            string level = "";
            m_Guide.scope = () => level;

            var run = new GuideRun(m_Guide, ProgressPath);
            GuideChapter one = m_Guide.chapters[0];
            Assert.AreEqual("", run.scope);
            Assert.AreEqual(1, run.chapterIndex, "the old file reads as the unscoped state");
            Assert.IsTrue(run.IsDone(one, one.steps[0]));
            Assert.AreEqual("practice", run.Note("practice.level"));

            level = "cove";
            Assert.IsTrue(run.Refresh(), "the scope moved");
            Assert.AreEqual("cove", run.scope);
            Assert.IsFalse(run.IsDone(one, one.steps[0]), "a new scope starts fresh");
            Assert.IsNull(run.Note("practice.level"));
            m_Door = true;
            run.Refresh();
            run.Next();
            run.SetNote("made", "cove.x");

            m_Door = false;
            level = "reef";
            run.Refresh();
            Assert.AreEqual(0, run.chapterIndex);
            Assert.AreEqual("door", run.step.id, "the second scope starts at its first unmet step");
            Assert.IsNull(run.Note("made"));

            level = "cove";
            var again = new GuideRun(m_Guide, ProgressPath);
            Assert.AreEqual("cove", again.scope);
            Assert.IsTrue(again.IsDone(one, one.steps[0]));
            Assert.AreEqual("note", again.step.id, "cove stands where it was left");
            Assert.AreEqual("cove.x", again.Note("made"));
            level = "";
            again.Refresh();
            Assert.AreEqual("practice", again.Note("practice.level"), "the unscoped state is kept as it was");
            CollectionAssert.AreEquivalent(new[] { "cove", "reef" }, again.file.scopes.Select(s => s.key));

            level = "reef";
            again.Refresh();
            again.SetNote("made", "reef.y");
            m_Door = true;
            again.StartOver();
            Assert.IsNull(again.Note("made"), "Start over clears the scope it is in");
            Assert.IsTrue(again.IsDone(one, one.steps[0]), "and catches it up at once");
            Assert.AreEqual("cove.x", again.file.Scope("cove").Note("made"), "other scopes are kept");
        }

        [Test]
        public void AScopeMetForTheFirstTime_CatchesUp_ToItsFirstUnmetTask()
        {
            bool named = false;
            GuideChapter one = m_Guide.chapters[0];
            one.Step("name", "Name it", "Type a name.").Checks(_ => named, "Waiting for a name.", "Named.");
            GuideChapter notes = m_Guide.chapters[1];
            GuideChapter three = m_Guide.Chapter("three", "Three");
            three.Step("read", "Read first", "A note.");
            three.Step("paint", "Paint it", "Paint.").Checks(_ => true, "Waiting for paint.", "Painted.");
            m_Door = true;
            m_Guide.scope = () => "cove";

            var run = new GuideRun(m_Guide, ProgressPath);
            Assert.IsTrue(run.IsDone(one, one.steps[0]), "the door was there before the guide opened");
            Assert.IsTrue(run.IsDone(one, one.steps[1]), "the note ahead of the first unmet task is passed");
            Assert.IsFalse(run.IsDone(one, one.steps[2]));
            Assert.AreEqual(0, run.chapterIndex);
            Assert.AreEqual("name", run.step.id, "it opens at the first unmet task");
            Assert.IsFalse(run.IsComplete(notes), "a chapter of notes only is left as it is");
            Assert.IsTrue(run.IsComplete(three), "a later chapter whose tasks all pass reads ✓, its notes too");

            named = true;
            run.Refresh();
            Assert.IsTrue(run.IsComplete(one));
        }

        [Test]
        public void AChecklistChapter_ReadsHowThingsStandNow()
        {
            bool baked = false;
            GuideChapter list = m_Guide.Chapter("ship", "Ship it");
            list.checklist = true;
            list.Step("bake", "Baked", "Bake it.").Checks(_ => baked, "Not baked.", "Baked.");
            list.Step("door", "A door", "Place one.").Checks(_ => m_Door, "No door.", "A door.");
            var run = new GuideRun(m_Guide, ProgressPath);
            run.OpenChapter(2);

            m_Door = true;
            Assert.IsTrue(run.Refresh());
            Assert.IsTrue(run.IsDone(list, list.steps[1]), "every item is asked, not only the open one");
            Assert.IsFalse(run.IsDone(list, list.steps[0]));
            Assert.IsFalse(run.IsDone(m_Guide.chapters[0], m_Guide.chapters[0].steps[0]), "only the open chapter is a list");
            m_Door = false;
            Assert.IsTrue(run.Refresh());
            Assert.IsFalse(run.IsDone(list, list.steps[1]), "a ✓ goes back to ○ when it stops passing");
            baked = true;
            run.Refresh();
            Assert.IsTrue(run.IsDone(list, list.steps[0]));
        }

        [Test]
        public void TheWindow_IsAUiToolkitHost_DrawingTheChaptersAndTheOpenCard()
        {
            m_Window = ScriptableObject.CreateInstance<EditorGuideWindow>();
            m_Window.Play(m_Guide, ProgressPath);
            VisualElement root = m_Window.rootVisualElement;

            Assert.AreEqual(2, root.Query(className: EditorGuideWindow.ChapterClass).ToList().Count);
            VisualElement card = root.Q(className: EditorGuideWindow.StepClass);
            Assert.IsNotNull(card, "the open step's card");
            Assert.AreEqual("door", ((GuideStep)card.userData).id);
            Assert.IsFalse(Next(root).enabledSelf, "Next is locked");
            Assert.IsNotNull(root.Q<Button>(className: EditorGuideWindow.SkipClass));
            Assert.IsTrue(root.Q<Label>(className: EditorGuideWindow.CheckClass).text.Contains("Waiting for a door."));

            m_Door = true;
            m_Window.Refresh(false);
            Assert.IsTrue(Next(root).enabledSelf, "the check passed: Next opens");
            Assert.IsTrue(root.Q<Label>(className: EditorGuideWindow.CheckClass).text.Contains("Nice — a door."));
            Assert.IsNull(root.Q<Button>(className: EditorGuideWindow.SkipClass), "nothing to skip once done");

            m_Window.run.Next();
            m_Window.Refresh(true);
            Assert.AreEqual("note", ((GuideStep)root.Q(className: EditorGuideWindow.StepClass).userData).id);
            Assert.AreEqual(1, root.Query(className: EditorGuideWindow.TermClass).ToList().Count, "its word card");
            Assert.AreEqual(1, root.Query(className: EditorGuideWindow.LineClass).ToList().Count, "the step behind it");
        }

        [Test]
        public void EveryTextTheWindowShows_TakesTheRunsOwnNames()
        {
            m_Guide.chapters[0].title = "{door}s";
            m_Guide.chapters[0].steps[0].text = "Drag the {door} in.";
            m_Guide.chapters[0].steps[0].waitingFor = "Waiting for the {door}.";
            m_Guide.words = (run, text) => text.Replace("{door}", "Gate");
            m_Window = ScriptableObject.CreateInstance<EditorGuideWindow>();
            m_Window.Play(m_Guide, ProgressPath);
            VisualElement root = m_Window.rootVisualElement;

            string[] shown = root.Query<Label>().ToList().Select(l => l.text).ToArray();
            Assert.IsFalse(shown.Any(t => t.Contains("{door}")), string.Join(" | ", shown));
            Assert.IsTrue(shown.Contains("Drag the Gate in."));
            Assert.IsTrue(shown.Any(t => t.Contains("Waiting for the Gate.")));
            Assert.IsTrue(shown.Any(t => t.Contains("Gates")), "the chapter's title too");
            Assert.AreEqual("Drag the {door} in.", m_Guide.chapters[0].steps[0].text, "the guide's own text is not rewritten");
        }

        [Test]
        public void TheLevelManifest_ShowsTheGuidesQuestionMark_WhileOneIsRegistered()
        {
            EditorGuides.Register(m_Guide);
            m_Manifest = ScriptableObject.CreateInstance<LevelManifestWindow>();
            m_Manifest.Edit(null, null, null);
            Button help = m_Manifest.rootVisualElement.Q<Button>(className: EditorGuides.HelpClass);
            Assert.IsNotNull(help);
            Assert.AreEqual(DisplayStyle.Flex, help.style.display.value);

            EditorGuides.Unregister(m_Guide);
            m_Manifest.Edit(null, null, null);
            if (EditorGuides.all.Count == 0)
                Assert.AreEqual(DisplayStyle.None,
                    m_Manifest.rootVisualElement.Q<Button>(className: EditorGuides.HelpClass).style.display.value);

            int before = EditorGuides.all.Count;
            EditorGuides.Register(m_Guide);
            EditorGuides.Register(new EditorGuide("test.guide", "Again"));
            Assert.AreEqual(before + 1, EditorGuides.all.Count, "one guide per id");
            Assert.AreEqual("Again", EditorGuides.Find("test.guide").title);
            EditorGuides.Unregister(EditorGuides.Find("test.guide"));
        }

        private static Button Next(VisualElement root) => root.Q<Button>(className: EditorGuideWindow.NextClass);
    }
}
