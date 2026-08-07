using UnityEngine;
using UnityEngine.UIElements;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// The transition veil (M16) — a ROOT overlay, which is the whole reason no loading
    /// scene exists: Root never unloads, so its overlay can cover the unload→load gap.
    /// Pure read-side view: it watches <see cref="LevelService.LoadingKey"/> on the Root
    /// scope and shows a full-screen veil with the incoming level's label while the key is
    /// present. Classes (<c>dtp-loading</c>, <c>dtp-loading__label</c>) are the theming
    /// hook, as everywhere.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class UIToolkitLoadingOverlay : MonoBehaviour
    {
        private VisualElement m_Veil;
        private Label m_Label;
        private bool m_Shown;

        private void OnEnable()
        {
            var root = GetComponent<UIDocument>().rootVisualElement;
            root.Clear();

            m_Veil = new VisualElement();
            m_Veil.AddToClassList("dtp-loading");
            m_Veil.style.position = Position.Absolute;
            m_Veil.style.top = 0f;
            m_Veil.style.bottom = 0f;
            m_Veil.style.left = 0f;
            m_Veil.style.right = 0f;
            m_Veil.style.backgroundColor = new Color(0.04f, 0.05f, 0.07f, 0.94f);
            m_Veil.style.alignItems = Align.Center;
            m_Veil.style.justifyContent = Justify.Center;
            m_Veil.style.display = DisplayStyle.None;

            m_Label = new Label(string.Empty);
            m_Label.AddToClassList("dtp-loading__label");
            m_Label.style.fontSize = 22f;
            m_Label.style.unityFontStyleAndWeight = FontStyle.Bold;
            m_Label.style.color = new Color(0.90f, 0.92f, 0.96f);
            m_Veil.Add(m_Label);

            root.Add(m_Veil);
            m_Shown = false;
        }

        private void Update()
        {
            if (m_Veil == null)
                return;

            StateTreeContextHost root = StateTreeContextHost.Resolve(gameObject,
                StateTreeContextKind.Root);
            string loading = null;
            if (root != null
                && root.Context.blackboard.TryGetValue(LevelService.LoadingKey, out object held))
                loading = held as string;

            bool show = !string.IsNullOrEmpty(loading);
            if (show)
                m_Label.text = "LOADING — " + loading;
            if (show == m_Shown)
                return;
            m_Shown = show;
            m_Veil.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
        }
    }
}
