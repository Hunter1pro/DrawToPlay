using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace PowerOfFire.DrawToPlay.Editor
{
    /// <summary>
    /// M3 exit-criterion smoke pass: builds a fresh scene holding one drawn limb, rigs a
    /// three-bone chain along it, binds a skin, and bends the middle bone 35° — so the saved
    /// scene IS the proof that "rotate a bone, the shape deforms". The skin debug overlay is
    /// switched on at the end, which makes the same capture show which bone owns which part
    /// of the silhouette.
    ///
    /// The limb goes through the production path (DrawKit.FitCurve over a capsule stroke →
    /// DrawnShapeRenderer), but the rig is built INLINE here rather than through RigShapeTool:
    /// the tool is a click-driven SceneView gesture, and a verify command that cannot be run
    /// headlessly is not much of a verify. The inline chain math is a straight port of the
    /// same Godot function the tool ports (_commit_rig_joints), so the two paths agree on the
    /// only things that matter downstream — bone NAMES and REST poses.
    ///
    /// Gesture-level verification (clicking a chain, Setup mode dragging bones, redraw-keeps-
    /// binding) still belongs to a human with a mouse.
    /// </summary>
    internal static class M3DemoVerify
    {
        private const string k_DemoFolder = "Assets/DrawToPlay/Demo";
        private const string k_ScenePath = k_DemoFolder + "/M3RigDemo.unity";
        private const string k_ShapeAssetPath = k_DemoFolder + "/M3DemoLimb.asset";
        private const string k_RigAssetPath = k_DemoFolder + "/M3DemoRig.asset";

        /// <summary>Name of the shape GameObject. Bones are named "{ShapeName}Bone{N}"
        /// (terrain_paint.gd line 325), so this is also the prefix of LimbBone1..3.</summary>
        private const string k_ShapeName = "Limb";
        private const string k_SkeletonName = "Skeleton";

        // Capsule: 2 * 1.55 + 2 * 0.45 = 4.0 world units long, 0.9 thick — elongated enough
        // that a bend at the middle joint is unmistakable, thick enough that the interior
        // lattice (asset.skinDetail = 0.1875) is several rows deep and the deform reads smooth.
        private const float k_LimbHalfLength = 1.55f;
        private const float k_LimbRadius = 0.45f;
        private const int k_CapSegments = 12;

        private const int k_BoneCount = 3;
        /// <summary>Chain spans the straight part of the capsule, so every bone owns a slab of
        /// body instead of a rounded cap.</summary>
        private const float k_ChainSpan = 3f;
        private const float k_BendDegrees = 35f;

        [MenuItem("Tools/Draw To Play/Verify M3 Rig + Skin")]
        public static void Verify()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;

            EnsureDemoFolder();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            BuildCamera();

            var limb = BuildLimb();
            var boneNames = new List<string>();
            var rig = BuildSkeleton(limb.transform, boneNames);
            BindSkin(limb, rig, boneNames);
            BendMiddleBone(rig, boneNames);

            // The point of the capture: colour-coded ownership over a visibly bent limb.
            SkinDebugOverlay.enabled = true;
            Selection.activeGameObject = limb.gameObject;

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, k_ScenePath);
            AssetDatabase.SaveAssets();
            EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<SceneAsset>(k_ScenePath));
        }

        private static void EnsureDemoFolder()
        {
            if (!AssetDatabase.IsValidFolder(k_DemoFolder))
                AssetDatabase.CreateFolder("Assets/DrawToPlay", "Demo");
        }

        private static void BuildCamera()
        {
            var cameraObject = new GameObject("Main Camera");
            cameraObject.tag = "MainCamera";
            var camera = cameraObject.AddComponent<Camera>();
            camera.orthographic = true;
            // Frames the 4-unit limb plus the swing of the bent half.
            camera.orthographicSize = 2.6f;
            camera.transform.position = new Vector3(0f, 0.5f, -10f);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.13f, 0.14f, 0.17f);
        }

        private static DrawnShapeRenderer BuildLimb()
        {
            var limbObject = new GameObject(k_ShapeName);
            var renderer = limbObject.AddComponent<DrawnShapeRenderer>();

            var asset = ScriptableObject.CreateInstance<DrawnShapeAsset>();
            asset.name = "M3DemoLimb";
            asset.fillColor = new Color(0.72f, 0.45f, 0.32f);
            asset.outlineColor = new Color(0.12f, 0.07f, 0.05f);
            asset.outlineWidth = 0.06f;
            asset.rimColor = new Color(0.96f, 0.78f, 0.58f);
            asset.rimWidth = 0.05f;
            // A vertical gradient makes the deform readable on a flat-coloured limb: the shade
            // travels with the vertices, so a bent segment is visibly lit differently.
            asset.fillShade = 0.35f;
            // Skin params stay at their defaults (skinDetail 0.1875, skinSoftness 0.125) —
            // this scene exists to verify the DEFAULTS produce a usable deform.
            asset.curve = DrawKit.FitCurve(BuildCapsuleStroke(), closed: true, tolerance: 0.08f,
                smoothPasses: 2);

            AssetDatabase.CreateAsset(asset, k_ShapeAssetPath);
            renderer.asset = asset;
            renderer.Regenerate();
            return renderer;
        }

        /// <summary>Closed rounded-capsule outline in shape-local units, wound POSITIVE (CCW /
        /// outer ring per the M0 winding contract). Only the two cap arcs are sampled — the
        /// straight sides fall out of FitCurve's resample, which walks the chord between the
        /// cap ends at the fit tolerance.</summary>
        private static List<Vector2> BuildCapsuleStroke()
        {
            var points = new List<Vector2>((k_CapSegments + 1) * 2);
            // right cap, -90° → +90° about (+halfLength, 0)
            for (int i = 0; i <= k_CapSegments; i++)
            {
                float angle = Mathf.PI * (-0.5f + i / (float)k_CapSegments);
                points.Add(new Vector2(k_LimbHalfLength + Mathf.Cos(angle) * k_LimbRadius,
                    Mathf.Sin(angle) * k_LimbRadius));
            }
            // left cap, +90° → +270° about (-halfLength, 0)
            for (int i = 0; i <= k_CapSegments; i++)
            {
                float angle = Mathf.PI * (0.5f + i / (float)k_CapSegments);
                points.Add(new Vector2(-k_LimbHalfLength + Mathf.Cos(angle) * k_LimbRadius,
                    Mathf.Sin(angle) * k_LimbRadius));
            }
            return points;
        }

        /// <summary>Sibling "Skeleton" GameObject carrying the ShapeRig and its RigAsset, with
        /// the chain already written into the asset and materialised as Transforms. Placed on
        /// the shape's origin so rig-root space and shape-local space coincide — the joints
        /// below are then plain shape-local coordinates.</summary>
        private static ShapeRig BuildSkeleton(Transform limb, List<string> boneNames)
        {
            var skeletonObject = new GameObject(k_SkeletonName);
            skeletonObject.transform.SetParent(limb.parent, false);
            skeletonObject.transform.position = limb.position;
            skeletonObject.transform.rotation = limb.rotation;

            var rig = skeletonObject.AddComponent<ShapeRig>();
            var rigAsset = ScriptableObject.CreateInstance<RigAsset>();
            rigAsset.name = "M3DemoRig";
            rigAsset.bones = BuildChainBones(k_ShapeName, BuildChainJoints());
            AssetDatabase.CreateAsset(rigAsset, k_RigAssetPath);

            rig.rig = rigAsset;
            rig.SyncBones();

            for (int i = 0; i < rigAsset.bones.Count; i++)
                boneNames.Add(rigAsset.bones[i].name);
            return rig;
        }

        /// <summary>Four joints along the limb axis → three bones. These stand in for the
        /// clicks RigShapeTool collects.</summary>
        private static List<Vector2> BuildChainJoints()
        {
            var joints = new List<Vector2>(k_BoneCount + 1);
            float step = k_ChainSpan / k_BoneCount;
            for (int i = 0; i <= k_BoneCount; i++)
                joints.Add(new Vector2(-k_ChainSpan * 0.5f + step * i, 0f));
            return joints;
        }

        /// <summary>Port of terrain_paint.gd _commit_rig_joints' bone loop (lines 311-327),
        /// reduced to the data RigAsset stores. Each joint pair becomes one bone whose rest is
        /// expressed in its PARENT bone's frame: position = the joint mapped through the
        /// accumulated inverse, rotation = the direction angle in that frame, length = the
        /// distance to the next joint. The accumulation matches RigAsset.RestRootMatrix
        /// (TRS(bone0) * … * TRS(boneN)), which is what makes these rests round-trip.
        ///
        /// Names follow the "{ShapeName}Bone{N}", N from 1, convention (line 325) — the name IS
        /// the binding, so this is the one detail the inline path must not improvise on.</summary>
        private static List<RigAsset.RigBone> BuildChainBones(string shapeName,
            IReadOnlyList<Vector2> joints)
        {
            var bones = new List<RigAsset.RigBone>();
            var accumulated = Matrix4x4.identity;
            for (int i = 0; i < joints.Count - 1; i++)
            {
                var inverse = accumulated.inverse;
                Vector2 localStart = inverse.MultiplyPoint3x4(joints[i]);
                Vector2 localDirection = inverse.MultiplyVector(joints[i + 1] - joints[i]);
                float degrees = Mathf.Atan2(localDirection.y, localDirection.x) * Mathf.Rad2Deg;

                bones.Add(new RigAsset.RigBone
                {
                    name = shapeName + "Bone" + (i + 1),
                    parentIndex = i - 1,          // -1 on the first bone = child of the rig root
                    restPosition = localStart,
                    restRotationDegrees = degrees,
                    length = localDirection.magnitude
                });

                accumulated *= Matrix4x4.TRS(localStart, Quaternion.Euler(0f, 0f, degrees),
                    Vector3.one);
            }
            return bones;
        }

        /// <summary>Bind: include the chain by NAME (replacing, like _bind_shape's
        /// replace_include path), then generate. Order matters — RegenerateSkin filters the
        /// rig through includeBones, so the list has to be on the asset first.</summary>
        private static void BindSkin(DrawnShapeRenderer limb, ShapeRig rig, List<string> boneNames)
        {
            var skin = limb.gameObject.AddComponent<DrawnShapeSkin>();
            skin.rig = rig;

            var asset = limb.asset;
            asset.includeBones = new List<string>(boneNames);
            EditorUtility.SetDirty(asset);

            skin.RegenerateSkin();
        }

        /// <summary>Rotate the middle bone — LimbBone2 — and leave it posed. No RegenerateSkin
        /// afterwards on purpose: bindposes are built from REST, so the SkinnedMeshRenderer
        /// follows the bone Transforms on its own, and regenerating here would hide a
        /// pose-contaminated bindpose bug instead of exposing it.</summary>
        private static void BendMiddleBone(ShapeRig rig, List<string> boneNames)
        {
            if (boneNames.Count < k_BoneCount)
                return;
            var bone = rig.FindBone(boneNames[k_BoneCount / 2]);
            if (bone == null)
                return;
            bone.localRotation *= Quaternion.Euler(0f, 0f, k_BendDegrees);
        }
    }
}
