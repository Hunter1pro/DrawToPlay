using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace PowerOfFire.DrawToPlay.Editor
{
    /// <summary>
    /// The water volume's inspector: the fields, with the typed extent hidden while the volume
    /// fits itself to what is drawn, and the FITTED extent read back from the same place
    /// Contains reads it — so the line can never disagree with the behaviour.
    ///
    /// A UI Toolkit host (the project rule): the water tag is a UI Toolkit drawer, and it drew
    /// "No GUI Implemented" inside the IMGUI version of this editor.
    /// </summary>
    [CustomEditor(typeof(WaterVolumeBehaviour))]
    public sealed class WaterVolumeEditor : UnityEditor.Editor
    {
        public override VisualElement CreateInspectorGUI()
        {
            var water = (WaterVolumeBehaviour)target;
            var root = new VisualElement();

            PropertyField size = null;
            SerializedProperty property = serializedObject.GetIterator();
            bool enterChildren = true;
            while (property.NextVisible(enterChildren))
            {
                enterChildren = false;
                if (property.name == "m_Script")
                    continue;
                var field = new PropertyField(property.Copy());
                if (property.name == "size")
                    size = field;
                root.Add(field);
            }

            var missing = new HelpBox("Fit To Visual is on, but this object draws nothing — so the "
                + "volume falls back to the Size above. Add a mesh (a plane is the usual one) or "
                + "turn fitting off.", HelpBoxMessageType.Warning);
            root.Add(missing);

            var fitted = new VisualElement();
            fitted.style.marginTop = 4f;
            var extent = new Label();
            extent.SetEnabled(false);
            var surface = new Label();
            surface.SetEnabled(false);
            fitted.Add(extent);
            fitted.Add(surface);
            fitted.Add(new HelpBox("The plane IS the water: move or scale it and the volume "
                + "follows. Depth is how far the volume reaches above and below the surface — "
                + "generous, so a hull on top and a walker on the bed are both in it.",
                HelpBoxMessageType.None));
            root.Add(fitted);

            void Refresh()
            {
                if (water == null)
                    return;
                var renderer = water.GetComponentInChildren<Renderer>();
                bool fits = water.fitToVisual && renderer != null;
                // The typed extent is the FALLBACK, and only exists while nothing is drawn to
                // measure — see WaterVolumeBehaviour.fitToVisual.
                if (size != null)
                    size.style.display = fits ? DisplayStyle.None : DisplayStyle.Flex;
                missing.style.display = water.fitToVisual && renderer == null
                    ? DisplayStyle.Flex : DisplayStyle.None;
                fitted.style.display = fits ? DisplayStyle.Flex : DisplayStyle.None;
                if (!fits)
                    return;
                Bounds bounds = renderer.bounds;
                extent.text = "Fitted extent   " + bounds.size.x.ToString("0.##") + " × "
                    + Mathf.Max(0.01f, water.depth).ToString("0.##") + " × "
                    + bounds.size.z.ToString("0.##") + "  (metres)";
                surface.text = "Surface height   " + water.SurfaceY.ToString("0.###");
            }

            root.RegisterCallback<SerializedPropertyChangeEvent>(_ => Refresh());
            // The plane can be moved or scaled without this inspector hearing about it.
            root.schedule.Execute(Refresh).Every(250);
            root.Bind(serializedObject);
            Refresh();
            return root;
        }
    }
}
