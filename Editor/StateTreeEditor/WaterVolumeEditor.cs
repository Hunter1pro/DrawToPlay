using UnityEditor;
using UnityEngine;

namespace PowerOfFire.DrawToPlay.Editor
{
    /// <summary>
    /// THE WATER'S INSPECTOR SHOWS ONLY WHAT IS TRUE OF IT.
    ///
    /// The component can take its extent two ways — from the shape it draws, or from a typed
    /// size — and showing both at once was worth a question the moment it shipped: a Size of
    /// (20, 6, 20) sat under a fitted volume that measured five metres, and there was no way
    /// to tell from the inspector which number the rules were using. A field that does not
    /// apply should not be on screen; the number that IS being used should be, even though
    /// nobody can type it.
    /// </summary>
    [CustomEditor(typeof(WaterVolumeBehaviour))]
    public sealed class WaterVolumeEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            SerializedProperty property = serializedObject.GetIterator();
            bool enterChildren = true;
            var water = (WaterVolumeBehaviour)target;
            while (property.NextVisible(enterChildren))
            {
                enterChildren = false;
                if (property.name == "m_Script")
                    continue;
                // The typed extent is the FALLBACK, and only exists while nothing is drawn to
                // measure — see WaterVolumeBehaviour.fitToVisual.
                if (property.name == "size" && water.fitToVisual)
                    continue;
                EditorGUILayout.PropertyField(property, true);
            }

            serializedObject.ApplyModifiedProperties();

            if (!water.fitToVisual)
                return;

            var renderer = water.GetComponentInChildren<Renderer>();
            if (renderer == null)
            {
                EditorGUILayout.HelpBox("Fit To Visual is on, but this object draws nothing — "
                    + "so the volume falls back to the Size below. Add a mesh (a plane is the "
                    + "usual one) or turn fitting off.", MessageType.Warning);
                EditorGUILayout.PropertyField(serializedObject.FindProperty("size"), true);
                serializedObject.ApplyModifiedProperties();
                return;
            }

            // WHAT THE RULES ACTUALLY TEST, in metres, read from the same place Contains reads
            // it — so this line can never disagree with the behaviour.
            Bounds bounds = renderer.bounds;
            EditorGUILayout.Space(4f);
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.LabelField("Fitted extent",
                    bounds.size.x.ToString("0.##") + " × " + Mathf.Max(0.01f, water.depth)
                        .ToString("0.##") + " × " + bounds.size.z.ToString("0.##") + "  (metres)");
                EditorGUILayout.LabelField("Surface height",
                    water.SurfaceY.ToString("0.###"));
            }
            EditorGUILayout.HelpBox("The plane IS the water: move or scale it and the volume "
                + "follows. Depth is how far the volume reaches above and below the surface — "
                + "generous, so a hull on top and a walker on the bed are both in it.",
                MessageType.None);
        }
    }
}
