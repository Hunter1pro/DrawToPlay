using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace PowerOfFire.DrawToPlay.Editor
{
    /// <summary>Where one designer stands in a guide: the steps done or skipped, where each chapter stands, and the
    /// small notes a guide keeps.</summary>
    [Serializable]
    public class GuideState
    {
        public int chapter;
        public List<string> done = new List<string>();
        public List<string> skipped = new List<string>();
        public List<GuideNote> at = new List<GuideNote>();
        public List<GuideNote> notes = new List<GuideNote>();

        public string Note(string key) => notes.Find(n => n.key == key)?.value;

        public void SetNote(string key, string value)
        {
            notes.RemoveAll(n => n.key == key);
            if (value != null)
                notes.Add(new GuideNote { key = key, value = value });
        }

        public int StepOf(string chapterId)
        {
            GuideNote note = at.Find(n => n.key == chapterId);
            return note != null && int.TryParse(note.value, out int step) ? step : 0;
        }

        public void SetStep(string chapterId, int step)
        {
            at.RemoveAll(n => n.key == chapterId);
            at.Add(new GuideNote { key = chapterId, value = step.ToString() });
        }

        public void Clear()
        {
            chapter = 0;
            done.Clear();
            skipped.Clear();
            at.Clear();
            notes.Clear();
        }

        /// <summary>A file older than a field reads it as null.</summary>
        internal void Mend()
        {
            done ??= new List<string>();
            skipped ??= new List<string>();
            at ??= new List<GuideNote>();
            notes ??= new List<GuideNote>();
        }
    }

    /// <summary>
    /// How far one designer got in one guide: its own state, and one per scope for a guide that keeps its progress
    /// per thing worked on (<see cref="EditorGuide.scope"/> — a level). A JSON file per user under
    /// <c>UserSettings/</c> — never a project asset, so it is never committed. A file from before scopes is the
    /// unscoped state, as it was.
    /// </summary>
    [Serializable]
    public sealed class GuideProgress : GuideState
    {
        public List<GuideScopeState> scopes = new List<GuideScopeState>();

        public static GuideProgress Load(string path)
        {
            GuideProgress loaded = null;
            try
            {
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                    loaded = JsonUtility.FromJson<GuideProgress>(File.ReadAllText(path));
            }
            catch (Exception e)
            {
                Debug.LogWarning("Guide: " + path + " could not be read, starting over — " + e.Message);
            }
            loaded ??= new GuideProgress();
            loaded.Mend();
            loaded.scopes ??= new List<GuideScopeState>();
            loaded.scopes.RemoveAll(s => s == null || string.IsNullOrEmpty(s.key));
            foreach (GuideScopeState scope in loaded.scopes)
                scope.Mend();
            return loaded;
        }

        public void Save(string path)
        {
            if (string.IsNullOrEmpty(path))
                return;
            string folder = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(folder))
                Directory.CreateDirectory(folder);
            File.WriteAllText(path, JsonUtility.ToJson(this, true));
        }

        /// <summary>The state kept for <paramref name="key"/>, or null on a first visit.</summary>
        public GuideScopeState Scope(string key) => scopes.Find(s => s.key == key);

        public GuideScopeState AddScope(string key)
        {
            GuideScopeState scope = Scope(key);
            if (scope != null)
                return scope;
            scope = new GuideScopeState { key = key };
            scopes.Add(scope);
            return scope;
        }
    }

    [Serializable]
    public sealed class GuideScopeState : GuideState
    {
        public string key;
    }

    [Serializable]
    public sealed class GuideNote
    {
        public string key;
        public string value;
    }
}
