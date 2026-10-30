using System;
using UnityEngine;

namespace PowerOfFire.DrawToPlay.Editor
{
    /// <summary>
    /// A guide being played: which chapter and step are open, what is done, and the rules — a task's Next unlocks
    /// once its check has passed, Skip always moves on, and both are saved at once. No UI: the window draws it,
    /// the tests drive it.
    /// </summary>
    public sealed class GuideRun
    {
        public readonly EditorGuide guide;
        public readonly string path;
        public GuideProgress progress { get; private set; }

        /// <summary>The open step's live warning, as of the last <see cref="Refresh"/>.</summary>
        public string status { get; private set; }

        /// <summary>Set when the last <see cref="Refresh"/> saw the open step pass for the first time.</summary>
        public bool justDone { get; private set; }

        public GuideRun(EditorGuide guide, string path)
        {
            this.guide = guide;
            this.path = path;
            progress = GuideProgress.Load(path);
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

        /// <summary>Asks the open step's check and warning. True when what the window shows changed.</summary>
        public bool Refresh()
        {
            justDone = false;
            GuideStep open = step;
            string was = status;
            status = open != null ? Ask(open.status) : null;
            if (open != null && open.isTask && !IsDone(chapter, open) && Passes(open))
            {
                progress.skipped.Remove(Key(chapter, open));
                progress.done.Add(Key(chapter, open));
                justDone = true;
                Save();
            }
            return justDone || was != status;
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

        public void StartOver()
        {
            progress = new GuideProgress();
            Changed();
        }

        public string Note(string key) => progress.Note(key);

        public void SetNote(string key, string value)
        {
            progress.SetNote(key, value);
            Save();
        }

        public void Save() => progress.Save(path);

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
