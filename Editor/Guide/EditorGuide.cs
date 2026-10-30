using System;
using System.Collections.Generic;

namespace PowerOfFire.DrawToPlay.Editor
{
    /// <summary>
    /// An interactive tutorial for designers, played by the Guide window (<see cref="EditorGuideWindow"/>):
    /// chapters of short step cards, each turning ✓ by itself when its check sees the designer did it. A game
    /// writes the content — texts, checks, Show me and Do it for me — and registers it with
    /// <see cref="EditorGuides.Register"/>; the package knows nothing of the game.
    /// </summary>
    public sealed class EditorGuide
    {
        public string id;
        public string title;
        public string intro = "";
        public readonly List<GuideChapter> chapters = new List<GuideChapter>();

        /// <summary>A line over every page while it returns text: a warning that outlives any one step.</summary>
        public Func<GuideRun, string> banner;

        /// <summary>Puts the run's own names into a text before it shows — the name the designer gave what the guide
        /// only suggested. Null: every text shows as written.</summary>
        public Func<GuideRun, string, string> words;

        /// <summary>A text (a title, a card, a tip) as <paramref name="run"/> shows it.</summary>
        public string Say(GuideRun run, string text)
        {
            if (words == null || string.IsNullOrEmpty(text))
                return text;
            try
            {
                return words(run, text) ?? text;
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogWarning("Guide: its words failed — " + e.Message);
                return text;
            }
        }

        public EditorGuide(string id, string title)
        {
            this.id = id;
            this.title = title;
        }

        public GuideChapter Chapter(string chapterId, string chapterTitle, string chapterIntro = "")
        {
            var chapter = new GuideChapter { id = chapterId, title = chapterTitle, intro = chapterIntro ?? "" };
            chapters.Add(chapter);
            return chapter;
        }
    }

    public sealed class GuideChapter
    {
        public string id;
        public string title;
        public string intro = "";
        public readonly List<GuideStep> steps = new List<GuideStep>();

        public GuideStep Step(string stepId, string stepTitle, string text)
        {
            var step = new GuideStep { id = stepId, title = stepTitle, text = text ?? "" };
            steps.Add(step);
            return step;
        }
    }

    /// <summary>
    /// One card. With a <see cref="check"/> it is a task: Next unlocks when the check first passes, and stays
    /// unlocked. Without one it is a note to read: Next is always open.
    /// </summary>
    public sealed class GuideStep
    {
        public string id;
        public string title;
        public string text = "";

        /// <summary>The deeper note, folded away under "Why".</summary>
        public string why = "";

        /// <summary>What the check looks for, shown while it has not passed.</summary>
        public string waitingFor = "";

        /// <summary>The ✓ line once it passes: "Nice — now…".</summary>
        public string praise = "";

        /// <summary>Asked when something in the Editor changed, never per repaint: keep it to lookups.</summary>
        public Func<GuideRun, bool> check;

        /// <summary>A live warning on the card, asked with the check.</summary>
        public Func<GuideRun, string> status;

        public Action<GuideRun> show;
        public string showTip = "";

        /// <summary>For chores only — opening a window, tidying up — never the thing the step teaches.</summary>
        public Action<GuideRun> doIt;
        public string doItLabel = "Do it for me";
        public string doItTip = "";

        /// <summary>Small word cards under the text: a glossary.</summary>
        public readonly List<GuideTerm> terms = new List<GuideTerm>();

        public bool isTask => check != null;

        public GuideStep Checks(Func<GuideRun, bool> passes, string waiting, string nice)
        {
            check = passes;
            waitingFor = waiting ?? "";
            praise = nice ?? "";
            return this;
        }

        public GuideStep Shows(Action<GuideRun> action, string tip = "")
        {
            show = action;
            showTip = tip ?? "";
            return this;
        }

        public GuideStep Does(Action<GuideRun> action, string label = "Do it for me", string tip = "")
        {
            doIt = action;
            doItLabel = label;
            doItTip = tip ?? "";
            return this;
        }

        public GuideStep Why(string text)
        {
            why = text ?? "";
            return this;
        }

        public GuideStep Term(string word, string meaning)
        {
            terms.Add(new GuideTerm { word = word, meaning = meaning });
            return this;
        }
    }

    public struct GuideTerm
    {
        public string word;
        public string meaning;
    }
}
