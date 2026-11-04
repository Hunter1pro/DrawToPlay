using System;
using UnityEngine;

namespace PowerOfFire.DrawToPlay.Editor
{
    /// <summary>
    /// A guide being played: which chapter and step are open, what is done, and the rules — a task's Next unlocks
    /// once its check has passed, Skip always moves on, and both are saved at once. A guide with a
    /// <see cref="EditorGuide.scope"/> keeps one state per scope in the same file and follows the scope as it changes;
    /// a scope met for the first time catches up (<see cref="CatchUp"/>). No UI: the window draws it, the tests drive it.
    /// </summary>
    public sealed class GuideRun
    {
        public readonly EditorGuide guide;
        public readonly string path;

        /// <summary>The whole file: the unscoped state and every scope's.</summary>
        public GuideProgress file { get; private set; }

        /// <summary>The state of the scope the run is in.</summary>
        public GuideState progress { get; private set; }

        /// <summary>The scope the run is in; empty: the guide's unscoped state.</summary>
        public string scope { get; private set; } = "";

        /// <summary>The open step's live warning, as of the last <see cref="Refresh"/>.</summary>
        public string status { get; private set; }

        /// <summary>Set when the last <see cref="Refresh"/> saw the open step pass for the first time.</summary>
        public bool justDone { get; private set; }

        public GuideRun(EditorGuide guide, string path)
        {
            this.guide = guide;
            this.path = path;
            file = GuideProgress.Load(path);
            progress = file;
            Follow();
        }

        public int chapterIndex => guide.chapters.Count == 0 ? -1 : Mathf.Clamp(progress.chapter, 0, guide.chapters.Count - 1);

        public GuideChapter chapter => chapterIndex >= 0 ? guide.chapters[chapterIndex] : null;

        /// <summary>The open step's index; the chapter's step count when its end page is open.</summary>
        public int stepIndex => StepIndex(chapter);

        public GuideStep step
        {
            get
            {
                GuideChapter open = chapter;
                int index = StepIndex(open);
                return open != null && index < open.steps.Count ? open.steps[index] : null;
            }
        }

        public int StepIndex(GuideChapter of) => of == null ? 0 : Mathf.Clamp(progress.StepOf(of.id), 0, of.steps.Count);

        public static string Key(GuideChapter of, GuideStep that) => of.id + "/" + that.id;

        public bool IsDone(GuideChapter of, GuideStep that) => progress.done.Contains(Key(of, that));

        public bool IsSkipped(GuideChapter of, GuideStep that) => progress.skipped.Contains(Key(of, that));

        /// <summary>Done or skipped.</summary>
        public int Passed(GuideChapter of)
        {
            int passed = 0;
            foreach (GuideStep each in of.steps)
            {
                if (IsDone(of, each) || IsSkipped(of, each))
                    passed++;
            }
            return passed;
        }

        public bool IsComplete(GuideChapter of) => of.steps.Count > 0 && Passed(of) == of.steps.Count;

        public bool canNext
        {
            get
            {
                GuideStep open = step;
                return open != null && (!open.isTask || IsDone(chapter, open));
            }
        }

        public bool canBack => stepIndex > 0;

        /// <summary>
        /// Moves the run into the guide's scope as it is now; true when it moved. A scope seen before comes back where
        /// it was left; a new one starts fresh and catches up.
        /// </summary>
        public bool Follow()
        {
            string now = AskScope();
            if (now == scope)
                return false;
            scope = now;
            status = null;
            justDone = false;
            if (now.Length == 0)
            {
                progress = file;
                return true;
            }
            GuideScopeState kept = file.Scope(now);
            progress = kept ?? file.AddScope(now);
            if (kept == null)
            {
                CatchUp();
                Save();
            }
            return true;
        }

        /// <summary>
        /// Every task whose check already passes is done, and the run opens at the first that does not — work done
        /// before the guide was opened shows as ✓. In each chapter the notes ahead of its first unmet task are passed
        /// too, so a chapter whose tasks all pass reads ✓. Chapters of notes only are left as they are.
        /// </summary>
        public void CatchUp()
        {
            int open = -1;
            int lastWithTasks = -1;
            for (int c = 0; c < guide.chapters.Count; c++)
            {
                GuideChapter each = guide.chapters[c];
                int unmet = -1;
                bool tasks = false;
                for (int i = 0; i < each.steps.Count; i++)
                {
                    GuideStep that = each.steps[i];
                    if (!that.isTask)
                        continue;
                    tasks = true;
                    if (Passes(that))
                        MarkDone(each, that);
                    else if (unmet < 0)
                        unmet = i;
                }
                if (!tasks)
                    continue;
                lastWithTasks = c;
                int upTo = unmet < 0 ? each.steps.Count : unmet;
                for (int i = 0; i < upTo; i++)
                {
                    if (!each.steps[i].isTask && !IsSkipped(each, each.steps[i]))
                        MarkDone(each, each.steps[i]);
                }
                progress.SetStep(each.id, upTo);
                if (open < 0 && unmet >= 0)
                    open = c;
            }
            progress.chapter = Mathf.Max(0, open >= 0 ? open : lastWithTasks);
        }

        /// <summary>Asks the open step's check and warning — and, in a checklist chapter, every task's, ✓ or ○ as
        /// things stand now. True when what the window shows changed.</summary>
        public bool Refresh()
        {
            bool moved = Follow();
            justDone = false;
            GuideStep open = step;
            string was = status;
            status = open != null ? Ask(open.status) : null;
            bool listChanged = chapter != null && chapter.checklist && Recheck(chapter);
            if (open != null && open.isTask && !IsDone(chapter, open) && Passes(open))
            {
                MarkDone(chapter, open);
                justDone = true;
            }
            if (justDone || listChanged)
                Save();
            return moved || justDone || listChanged || was != status;
        }

        public void Next()
        {
            GuideStep open = step;
            if (open == null || !canNext)
                return;
            if (!IsDone(chapter, open))
                progress.done.Add(Key(chapter, open));
            Move(1);
        }

        public void Skip()
        {
            GuideStep open = step;
            if (open == null)
                return;
            if (!IsDone(chapter, open) && !IsSkipped(chapter, open))
                progress.skipped.Add(Key(chapter, open));
            Move(1);
        }

        public void Back()
        {
            if (canBack)
                Move(-1);
        }

        /// <summary>Opens <paramref name="index"/> of the open chapter — a step already passed, to read it again.</summary>
        public void Go(int index)
        {
            GuideChapter open = chapter;
            if (open == null)
                return;
            progress.SetStep(open.id, Mathf.Clamp(index, 0, open.steps.Count));
            Changed();
        }

        public void OpenChapter(int index)
        {
            if (guide.chapters.Count == 0)
                return;
            progress.chapter = Mathf.Clamp(index, 0, guide.chapters.Count - 1);
            Changed();
        }

        /// <summary>The scope the run is in starts again; a scope catches up at once. Other scopes are kept.</summary>
        public void StartOver()
        {
            progress.Clear();
            if (scope.Length > 0)
                CatchUp();
            Changed();
        }

        public string Note(string key) => progress.Note(key);

        public void SetNote(string key, string value)
        {
            progress.SetNote(key, value);
            Save();
        }

        public void Save() => file.Save(path);

        private void Move(int by)
        {
            GuideChapter open = chapter;
            progress.SetStep(open.id, Mathf.Clamp(StepIndex(open) + by, 0, open.steps.Count));
            Changed();
        }

        private void Changed()
        {
            status = null;
            justDone = false;
            Save();
        }

        private void MarkDone(GuideChapter of, GuideStep that)
        {
            string key = Key(of, that);
            progress.skipped.Remove(key);
            if (!progress.done.Contains(key))
                progress.done.Add(key);
        }

        /// <summary>Every task of a checklist chapter as things stand: ✓ while it passes, ○ once it does not.</summary>
        private bool Recheck(GuideChapter list)
        {
            bool changed = false;
            foreach (GuideStep that in list.steps)
            {
                if (!that.isTask)
                    continue;
                bool passes = Passes(that);
                if (passes == IsDone(list, that))
                    continue;
                if (passes)
                    MarkDone(list, that);
                else
                    progress.done.Remove(Key(list, that));
                changed = true;
            }
            return changed;
        }

        private string AskScope()
        {
            if (guide.scope == null)
                return "";
            try
            {
                return guide.scope() ?? "";
            }
            catch (Exception e)
            {
                Debug.LogWarning("Guide: its scope failed — " + e.Message);
                return scope;
            }
        }

        private bool Passes(GuideStep that)
        {
            try
            {
                return that.check(this);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Guide: the check of '{that.title}' failed — {e.Message}");
                return false;
            }
        }

        private string Ask(Func<GuideRun, string> warning)
        {
            try
            {
                return warning?.Invoke(this);
            }
            catch (Exception e)
            {
                Debug.LogWarning("Guide: a warning failed — " + e.Message);
                return null;
            }
        }
    }
}
