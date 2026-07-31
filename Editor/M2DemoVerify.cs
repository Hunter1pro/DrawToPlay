using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace PowerOfFire.DrawToPlay.Editor
{
    /// <summary>
    /// M2 exit-criterion smoke pass, runnable headlessly: rebuilds the M1 demo scene,
    /// assigns two generated paint textures to the ground asset, paints two overlapping
    /// brush swaths (slots 2 and 3) through the real ShapePaint pipeline, and scatters
    /// sprite stamps along the surface. Gesture-level verification (undo-per-stroke,
    /// brush feel) still belongs to a human with a mouse — this proves the mask, shader
    /// blend, and layer clipping render correctly.
    /// </summary>
    internal static class M2DemoVerify
    {
        [MenuItem("Tools/Draw To Play/Verify M2 Paint + Stamps")]
        public static void Verify()
        {
            M1DemoSceneBuilder.Build();

            var ground = GameObject.Find("DrawnGround");
            if (ground == null)
                return;
            var renderer = ground.GetComponent<DrawnShapeRenderer>();
            var asset = renderer.asset;

            asset.paintTexture2 = EnsureTexture("M2PaintMoss", new Color(0.30f, 0.55f, 0.22f), new Color(0.20f, 0.42f, 0.16f));
            asset.paintTexture3 = EnsureTexture("M2PaintRock", new Color(0.55f, 0.52f, 0.50f), new Color(0.38f, 0.36f, 0.35f));
            EditorUtility.SetDirty(asset);

            var paint = ground.GetComponent<ShapePaint>();
            if (paint == null)
                paint = ground.AddComponent<ShapePaint>();

            // Two overlapping swaths along the bowl's top surface: moss (slot 2) on the
            // left half, rock (slot 3) on the right, blending where they cross at centre.
            for (float x = -5.5f; x <= 1.5f; x += 0.25f)
                paint.Paint(new Vector2(x, SurfaceY(x)), false, 0.8f, 1);
            for (float x = -1.5f; x <= 5.5f; x += 0.25f)
                paint.Paint(new Vector2(x, SurfaceY(x)), false, 0.8f, 2);
            paint.Commit();
            paint.SyncLayer();

            ScatterStamps(ground.transform);

            EditorSceneManager.MarkSceneDirty(ground.scene);
            EditorSceneManager.SaveScene(ground.scene);
        }

        private static float SurfaceY(float x) => 0.5f + 0.035f * x * x;

        private static void ScatterStamps(Transform parent)
        {
            var stampTex = EnsureTexture("M2StampShrub", new Color(0.16f, 0.35f, 0.18f), new Color(0.10f, 0.24f, 0.12f), 32);
            var sprite = Sprite.Create(stampTex, new Rect(0, 0, stampTex.width, stampTex.height),
                new Vector2(0.5f, 0.1f), 32f);
            for (int i = 0; i < 6; i++)
            {
                float x = -5f + i * 2f;
                var go = new GameObject("Shrub" + i);
                go.transform.SetParent(parent, false);
                go.transform.position = new Vector3(x, SurfaceY(x) + 0.02f, 0f);
                go.transform.localScale = Vector3.one * Random.Range(0.8f, 1.3f);
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = sprite;
                sr.flipX = Random.value < 0.5f;
                sr.sortingOrder = 10;
                go.AddComponent<StampMarker>();
            }
        }

        /// <summary>Small two-tone checker texture asset (Repeat wrap) under Demo/.</summary>
        private static Texture2D EnsureTexture(string name, Color a, Color b, int size = 64)
        {
            const string folder = "Assets/DrawToPlay/Demo";
            string path = folder + "/" + name + ".asset";
            var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (existing != null)
                return existing;

            if (!AssetDatabase.IsValidFolder(folder))
                AssetDatabase.CreateFolder("Assets/DrawToPlay", "Demo");
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Repeat;
            var px = new Color[size * size];
            int cell = size / 8;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                    px[y * size + x] = ((x / cell + y / cell) & 1) == 0 ? a : b;
            tex.SetPixels(px);
            tex.Apply();
            tex.name = name;
            AssetDatabase.CreateAsset(tex, path);
            return tex;
        }
    }
}
