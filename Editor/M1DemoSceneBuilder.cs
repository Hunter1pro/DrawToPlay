using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace PowerOfFire.DrawToPlay.Editor
{
    /// <summary>
    /// One-click M1 exit-criterion scene: a drawn bowl-shaped terrain blob (chain
    /// collision) and a RollingBall dropped above it. Press Play — the ball falls onto
    /// the drawn surface and rolls toward the bowl's centre. The ground curve is built
    /// through the exact production path (DrawKit.FitCurve over a point stroke), so the
    /// demo also exercises fit → bake → compose → chain derivation end to end.
    /// </summary>
    internal static class M1DemoSceneBuilder
    {
        private const string k_ScenePath = "Assets/DrawToPlay/Demo/M1PhysicsDemo.unity";

        [MenuItem("Tools/Draw To Play/Build M1 Demo Scene")]
        public static void Build()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var cameraObject = new GameObject("Main Camera");
            cameraObject.tag = "MainCamera";
            var camera = cameraObject.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 5.5f;
            camera.transform.position = new Vector3(0f, 1f, -10f);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.13f, 0.14f, 0.17f);

            var groundObject = new GameObject("DrawnGround");
            var renderer = groundObject.AddComponent<DrawnShapeRenderer>();

            var asset = ScriptableObject.CreateInstance<DrawnShapeAsset>();
            asset.name = "M1DemoGround";
            asset.fillColor = new Color(0.24f, 0.36f, 0.25f);
            asset.outlineColor = new Color(0.05f, 0.08f, 0.05f);
            asset.rimColor = new Color(0.45f, 0.62f, 0.4f);
            asset.rimWidth = 0.12f;
            asset.curve = DrawKit.FitCurve(BuildBowlStroke(), closed: true, tolerance: 0.08f, smoothPasses: 2);

            const string demoFolder = "Assets/DrawToPlay/Demo";
            if (!AssetDatabase.IsValidFolder(demoFolder))
                AssetDatabase.CreateFolder("Assets/DrawToPlay", "Demo");
            AssetDatabase.CreateAsset(asset, demoFolder + "/M1DemoGround.asset");

            renderer.asset = asset;
            renderer.Regenerate();
            groundObject.AddComponent<TerrainBlob>();

            var ballObject = new GameObject("RollingBall");
            ballObject.transform.position = new Vector3(-4f, 4f, 0f);
            ballObject.AddComponent<RollingBall>();

            EditorSceneManager.SaveScene(scene, k_ScenePath);
            EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<SceneAsset>(k_ScenePath));
        }

        /// <summary>Play the demo scene despite the project's StartupScene convention,
        /// which re-points playModeStartScene at Sandbox.unity on every Play press.
        /// Our handler is subscribed at click time — after StartupScene's load-time
        /// subscription — so within the same ExitingEditMode event it runs later and its
        /// assignment wins (C# multicast delegates invoke in subscription order). The
        /// Sandbox convention is restored when play mode ends.</summary>
        [MenuItem("Tools/Draw To Play/Play M1 Demo Scene")]
        public static void PlayDemo()
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(k_ScenePath) == null)
                Build();
            else if (SceneManager_GetActivePath() != k_ScenePath)
            {
                if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                    return;
                EditorSceneManager.OpenScene(k_ScenePath, OpenSceneMode.Single);
            }

            EditorApplication.playModeStateChanged += OverrideStartScene;
            EditorApplication.EnterPlaymode();
        }

        private static string SceneManager_GetActivePath()
            => UnityEngine.SceneManagement.SceneManager.GetActiveScene().path;

        private static void OverrideStartScene(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingEditMode)
            {
                EditorSceneManager.playModeStartScene =
                    AssetDatabase.LoadAssetAtPath<SceneAsset>(k_ScenePath);
            }
            else if (state == PlayModeStateChange.EnteredEditMode)
            {
                EditorApplication.playModeStateChanged -= OverrideStartScene;
                EditorSceneManager.playModeStartScene =
                    AssetDatabase.LoadAssetAtPath<SceneAsset>("Assets/Sandbox.unity");
            }
        }

        /// <summary>Closed outline of a shallow bowl: curved top surface (so the ball
        /// visibly rolls), straight sides and bottom. Local space, world units.</summary>
        private static List<Vector2> BuildBowlStroke()
        {
            var points = new List<Vector2>();
            for (float x = -7f; x <= 7f; x += 0.35f)
                points.Add(new Vector2(x, 0.5f + 0.035f * x * x));
            points.Add(new Vector2(7f, -2.5f));
            points.Add(new Vector2(-7f, -2.5f));
            return points;
        }
    }
}
