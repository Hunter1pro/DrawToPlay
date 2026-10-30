using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace PowerOfFire.DrawToPlay.Editor
{
    /// <summary>The one place a new name is typed — declaring a key, naming a new row — as a small window
    /// (not modal: nothing waits on it) with Declare / Create.</summary>
    internal sealed class StateTreeNamePrompt : EditorWindow
    {
        private Action<string> m_Done;

        /// <summary><paramref name="anchor"/> is unused beyond placement near the window that asked.</summary>
        public static void Show(Rect anchor, string title, string start, Action<string> done, string verb = "Declare")
        {
            EditorWindow asking = focusedWindow;
            var prompt = CreateInstance<StateTreeNamePrompt>();
            prompt.titleContent = new GUIContent(title);
            prompt.m_Done = done;
            prompt.Build(start, verb);
            Rect near = asking != null ? asking.position : new Rect(200f, 200f, 400f, 300f);
            prompt.position = new Rect(near.center.x - 170f, near.y + 80f, 340f, 60f);
            prompt.ShowAuxWindow();
        }

        private void Build(string start, string verb)
        {
            VisualElement root = rootVisualElement;
            root.style.paddingLeft = root.style.paddingRight = root.style.paddingTop = 6f;
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            var text = new TextField { value = start ?? "" };
            text.style.flexGrow = 1f;
            row.Add(text);
            void Commit()
            {
                string value = text.value != null ? text.value.Trim() : "";
                Action<string> done = m_Done;
                m_Done = null;
                Close();
                if (value.Length > 0)
                    done?.Invoke(value);
            }
            text.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter)
                    Commit();
            }, TrickleDown.TrickleDown);
            var ok = new Button(Commit) { text = verb };
            row.Add(ok);
            root.Add(row);
            text.schedule.Execute(() => text.Focus());
        }
    }
}
