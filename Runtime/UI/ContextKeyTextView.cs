using UnityEngine;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// A TextMesh that mirrors one context-scope number — the smallest HUD element: "SCORE
    /// 300" is this component pointed at Root's "score", "WAVE 3" at Level's "wave". Pure
    /// read-side view over the spine's state, polling like the other demo views; the trees
    /// never know it exists, which is the point — add a display by adding a component, remove
    /// it and nothing changes.
    /// </summary>
    public sealed class ContextKeyTextView : MonoBehaviour
    {
        public StateTreeContextKind scope = StateTreeContextKind.Root;

        public string contextId = "";

        public string key = "";

        public string prefix = "";

        /// <summary>Shown while the key is absent — usually "0", sometimes "-".</summary>
        public string missingText = "0";

        private TextMesh m_Mesh;
        private string m_LastShown;

        private void OnEnable()
        {
            m_Mesh = GetComponent<TextMesh>();
        }

        private void Update()
        {
            if (m_Mesh == null || string.IsNullOrEmpty(key))
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
            m_Mesh.text = prefix + shown;
        }
    }
}
