using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace PowerOfFire.DrawToPlay.Editor
{
    /// <summary>
    /// How far one designer got in one guide: the steps done or skipped, where each chapter stands, and the small
    /// notes a guide keeps (what its practice made). A JSON file per user under <c>UserSettings/</c> — never a
    /// project asset, so it is never committed.
    /// </summary>
    [Serializable]
    public sealed class GuideProgress
    {
        public int chapter;
        public List<string> done = new List<string>();
        public List<string> skipped = new List<string>();
        public List<GuideNote> at = new List<GuideNote>();
        public List<GuideNote> notes = new List<GuideNote>();

        public static GuideProgress Load(string path)
        {
            try
            {
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                    return JsonUtility.FromJson<GuideProgress>(File.ReadAllText(path)) ?? new GuideProgress();
            }
            catch (Exception e)
            {
                Debug.LogWarning("Guide: " + path + " could not be read, starting over — " + e.Message);
            }
            return new GuideProgress();
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
    }

    [Serializable]
    public sealed class GuideNote
    {
        public string key;
        public string value;
    }
}
