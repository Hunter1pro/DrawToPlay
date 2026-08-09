using UnityEngine;
using UnityEngine.UIElements;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// The OVERLAY half of the pattern: one label mirroring one context-scope value — the
    /// UI Toolkit sibling of the TextMesh HUD view, polling the spine the same way. Trees
    /// never know it exists; add a readout by adding a component. Classes
    /// (<c>dtp-hud</c>, <c>dtp-hud__label</c>) are the theming hook.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class UIToolkitValueLabel : MonoBehaviour
    {
        public StateTreeContextKind scope = StateTreeContextKind.Root;

        public string contextId = "";

        [StateTreeKey(StateTreeKeyKind.Float, any: true)]
        public StateTreeKeyField key = new StateTreeKeyField();

        public string prefix = "";

        /// <summary>Shown while the key is absent — often "0", sometimes "—".</summary>
        public string missingText = "";

        /// <summary>0..1 across the screen, left to right.</summary>
        public float anchorX = 0.5f;

        public float top = 8f;

        private Label m_Label;
        private string m_LastShown;

        private void OnEnable()
        {
            var root = GetComponent<UIDocument>().rootVisualElement;
            var holder = new VisualElement();
            holder.AddToClassList("dtp-hud");
            holder.style.position = Position.Absolute;
            holder.style.top = top;
            holder.style.left = Length.Percent(anchorX * 100f);
            m_Label = new Label(string.Empty);
            m_Label.AddToClassList("dtp-hud__label");
            m_Label.style.unityFontStyleAndWeight = FontStyle.Bold;
            m_Label.style.fontSize = 14f;
            m_Label.style.color = new Color(0.95f, 0.90f, 0.55f);
            holder.Add(m_Label);
            root.Add(holder);
            m_LastShown = null;
        }

        private void Update()
        {
            if (m_Label == null || string.IsNullOrEmpty((string)key))
                return;

            StateTreeContextHost host =
                StateTreeContextHost.Resolve(gameObject, scope, contextId);
            string shown = missingText;
            if (host != null && host.Context.blackboard.TryGetValue(key, out object held))
            {
                if (held is float f)
                    shown = ((int)f).ToString();
                else if (held is string s)
                    shown = s;
            }

            if (shown == m_LastShown)
                return;
            m_LastShown = shown;
            m_Label.text = prefix + shown;
        }
    }
}
