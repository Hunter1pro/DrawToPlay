using UnityEngine;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// Tints a TextMesh by the owner's life state — the smallest possible "is it dead"
    /// display, and the reason a revive or a smite is VISIBLE at all on glyph entities
    /// (invisible-but-correct reads as broken; the recurring lesson, applied). Pure read-side:
    /// nothing else knows this exists.
    /// </summary>
    public sealed class HealthGlyphTintView : MonoBehaviour
    {
        public Color aliveColor = Color.white;

        public Color deadColor = new Color(0.35f, 0.35f, 0.38f);

        private HealthComponent m_Health;
        private TextMesh m_Mesh;
        private bool m_LastAlive = true;

        private void OnEnable()
        {
            m_Health = GetComponent<HealthComponent>();
            m_Mesh = GetComponent<TextMesh>();
            Apply(true);
        }

        private void Update()
        {
            if (m_Health == null || m_Mesh == null)
                return;
            bool alive = m_Health.isAlive;
            if (alive == m_LastAlive)
                return;
            Apply(alive);
        }

        private void Apply(bool alive)
        {
            m_LastAlive = alive;
            if (m_Mesh != null)
                m_Mesh.color = alive ? aliveColor : deadColor;
        }
    }
}
