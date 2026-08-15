using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace PowerOfFire.DrawToPlay.Editor
{
    /// <summary>
    /// TYPE ICONS for the toolset's assets — a Project window full of identical
    /// ScriptableObject glyphs is a filing cabinet with the labels torn off. Each family
    /// gets a drawn icon (drawn, not shipped: the no-external-art rule the demo's item
    /// icons follow), and the mapping is INHERITANCE-AWARE: any
    /// <see cref="StateTreeRegistryAsset"/> subclass — today's and every future one, the
    /// examples' included — wears the generic registry glyph unless a specialized rule
    /// (abilities, effects, cues) claims it first.
    ///
    /// Applied by stamping the icon onto each type's SCRIPT importer, which is what the
    /// Project window, the pickers and the inspector headers all read. Idempotent: a script
    /// already wearing its icon is left alone, so domain reloads cost nothing.
    /// </summary>
    [InitializeOnLoad]
    internal static class DrawToPlayIcons
    {
        private const string k_Folder = "Assets/DrawToPlay/Editor/Icons";

        static DrawToPlayIcons()
        {
            // Delayed: the AssetDatabase is not writable during the reload itself.
            EditorApplication.delayCall += () => Apply(false);
        }

        [MenuItem("Tools/Draw To Play/Rebuild Type Icons")]
        private static void RebuildMenu()
        {
            Apply(true);
        }

        private static void Apply(bool force)
        {
            EnsureFolder();
            Texture2D registry = Icon("registry", force, DrawRegistry);
            Texture2D tree = Icon("statetree", force, DrawStateTree);
            Texture2D graph = Icon("taskgraph", force, DrawTaskGraph);
            Texture2D service = Icon("service", force, DrawService);
            Texture2D abilities = Icon("abilities", force, DrawAbilities);
            Texture2D effects = Icon("effects", force, DrawEffects);
            Texture2D cues = Icon("cues", force, DrawCues);

            // Specialized rules first — the order IS the precedence.
            var rules = new List<(Func<Type, bool> claims, Texture2D icon)>
            {
                (type => type == typeof(AbilityRegistry), abilities),
                (type => type == typeof(EffectRegistry), effects),
                (type => type == typeof(CueRegistry), cues),
                (type => type == typeof(ServiceDef), service),
                (type => type == typeof(StateTreeAsset), tree),
                (type => type == typeof(GraphTaskAsset), graph),
                (type => typeof(StateTreeRegistryAsset).IsAssignableFrom(type), registry)
            };

            foreach (string guid in AssetDatabase.FindAssets("t:MonoScript",
                new[] { "Assets/DrawToPlay", "Assets/DrawToPlayExamples" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var script = AssetDatabase.LoadAssetAtPath<MonoScript>(path);
                Type type = script != null ? script.GetClass() : null;
                if (type == null)
                    continue;

                foreach ((Func<Type, bool> claims, Texture2D icon) in rules)
                {
                    if (!claims(type) || icon == null)
                        continue;
                    Stamp(script, icon);
                    break;
                }
            }
        }

        /// <summary>Set the script's icon when it differs — what the Project window, the
        /// pickers and inspector headers read. SESSION-scoped on purpose: the API that once
        /// persisted an icon into the .meta (CopyMonoScriptIconToImporters) is gone from
        /// Unity 6, and the [InitializeOnLoad] constructor above re-applies after every
        /// domain reload anyway — self-healing beats serialized, and the repo carries no
        /// meta churn.</summary>
        private static void Stamp(MonoScript script, Texture2D icon)
        {
            if (EditorGUIUtility.GetIconForObject(script) == icon)
                return;
            EditorGUIUtility.SetIconForObject(script, icon);
        }

        // ---- the drawings -----------------------------------------------------------------

        private static void EnsureFolder()
        {
            if (!AssetDatabase.IsValidFolder(k_Folder))
            {
                Directory.CreateDirectory(Path.GetFullPath(k_Folder));
                AssetDatabase.Refresh();
            }
        }

        private static Texture2D Icon(string name, bool force, Action<Texture2D> draw)
        {
            string path = k_Folder + "/" + name + ".png";
            if (!force)
            {
                var held = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                if (held != null)
                    return held;
            }

            const int size = 64;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var clear = new Color(0f, 0f, 0f, 0f);
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                    texture.SetPixel(x, y, clear);
            draw(texture);
            texture.Apply();
            File.WriteAllBytes(Path.GetFullPath(path), texture.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);

            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.GUI;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.filterMode = FilterMode.Bilinear;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        private static void Rect(Texture2D t, int x0, int y0, int x1, int y1, Color c)
        {
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                    t.SetPixel(x, y, c);
        }

        private static void Disc(Texture2D t, float cx, float cy, float r, Color c)
        {
            for (int y = 0; y < t.height; y++)
                for (int x = 0; x < t.width; x++)
                    if ((x - cx) * (x - cx) + (y - cy) * (y - cy) <= r * r)
                        t.SetPixel(x, y, c);
        }

        private static void Line(Texture2D t, Vector2 a, Vector2 b, float width, Color c)
        {
            Vector2 dir = b - a;
            float length = dir.magnitude;
            if (length < 0.001f)
                return;
            dir /= length;
            for (int y = 0; y < t.height; y++)
            {
                for (int x = 0; x < t.width; x++)
                {
                    Vector2 p = new Vector2(x, y) - a;
                    float along = Vector2.Dot(p, dir);
                    if (along < 0f || along > length)
                        continue;
                    float aside = Mathf.Abs(p.x * dir.y - p.y * dir.x);
                    if (aside <= width)
                        t.SetPixel(x, y, c);
                }
            }
        }

        /// <summary>Rows behind a spine — a catalog.</summary>
        private static void DrawRegistry(Texture2D t)
        {
            var blue = new Color(0.43f, 0.56f, 0.71f);
            var pale = new Color(0.62f, 0.73f, 0.85f);
            Rect(t, 10, 10, 16, 54, blue);                 // the spine
            Rect(t, 22, 44, 54, 52, pale);                 // three rows
            Rect(t, 22, 28, 54, 36, pale);
            Rect(t, 22, 12, 54, 20, pale);
        }

        /// <summary>A root with two children — the tree.</summary>
        private static void DrawStateTree(Texture2D t)
        {
            var green = new Color(0.35f, 0.70f, 0.41f);
            var dark = new Color(0.24f, 0.50f, 0.30f);
            Line(t, new Vector2(32, 46), new Vector2(16, 20), 2.5f, dark);
            Line(t, new Vector2(32, 46), new Vector2(48, 20), 2.5f, dark);
            Disc(t, 32, 48, 9f, green);
            Disc(t, 15, 16, 7f, green);
            Disc(t, 49, 16, 7f, green);
        }

        /// <summary>Two nodes and the wire between them — the canvas.</summary>
        private static void DrawTaskGraph(Texture2D t)
        {
            var orange = new Color(0.91f, 0.58f, 0.29f);
            var pale = new Color(0.96f, 0.76f, 0.52f);
            Line(t, new Vector2(22, 42), new Vector2(42, 22), 2.5f, pale);
            Rect(t, 8, 34, 26, 52, orange);
            Rect(t, 38, 12, 56, 30, orange);
            Disc(t, 26, 43, 3.4f, pale);                    // the out pin
            Disc(t, 38, 21, 3.4f, pale);                    // the in pin
        }

        /// <summary>A hexagon with a core — the mounted service.</summary>
        private static void DrawService(Texture2D t)
        {
            var purple = new Color(0.61f, 0.44f, 0.82f);
            var pale = new Color(0.80f, 0.68f, 0.94f);
            for (int y = 0; y < 64; y++)
            {
                for (int x = 0; x < 64; x++)
                {
                    float dx = Mathf.Abs(x - 32f);
                    float dy = Mathf.Abs(y - 32f);
                    // A flat-topped hexagon as an intersection of half-planes.
                    bool inside = dx <= 22f && dy <= 24f && dx * 0.5773f + dy * 1f <= 26f;
                    bool rim = inside && !(dx <= 17f && dy <= 19f && dx * 0.5773f + dy <= 20.5f);
                    if (rim)
                        t.SetPixel(x, y, purple);
                }
            }
            Disc(t, 32, 32, 7f, pale);
        }

        /// <summary>A four-point burst — the act.</summary>
        private static void DrawAbilities(Texture2D t)
        {
            var red = new Color(0.90f, 0.38f, 0.29f);
            var pale = new Color(0.98f, 0.63f, 0.45f);
            for (int y = 0; y < 64; y++)
            {
                for (int x = 0; x < 64; x++)
                {
                    float dx = Mathf.Abs(x - 32f);
                    float dy = Mathf.Abs(y - 32f);
                    // |x|·|y| small near the axes = a slim four-point star.
                    if (dx * dy <= 60f && dx + dy <= 28f)
                        t.SetPixel(x, y, red);
                }
            }
            Disc(t, 32, 32, 6f, pale);
        }

        /// <summary>A falling drop — the delta that lands.</summary>
        private static void DrawEffects(Texture2D t)
        {
            var magenta = new Color(0.82f, 0.33f, 0.58f);
            Disc(t, 32, 24, 14f, magenta);
            for (int y = 24; y < 56; y++)
            {
                float half = 14f * (56 - y) / 32f;
                for (int x = 0; x < 64; x++)
                    if (Mathf.Abs(x - 32f) <= half)
                        t.SetPixel(x, y, magenta);
            }
        }

        /// <summary>A spark — seen, never felt.</summary>
        private static void DrawCues(Texture2D t)
        {
            var yellow = new Color(0.93f, 0.79f, 0.30f);
            var pale = new Color(0.99f, 0.92f, 0.62f);
            Line(t, new Vector2(32, 8), new Vector2(32, 56), 2.6f, yellow);
            Line(t, new Vector2(8, 32), new Vector2(56, 32), 2.6f, yellow);
            Line(t, new Vector2(15, 15), new Vector2(49, 49), 1.8f, yellow);
            Line(t, new Vector2(15, 49), new Vector2(49, 15), 1.8f, yellow);
            Disc(t, 32, 32, 5.5f, pale);
        }
    }
}
