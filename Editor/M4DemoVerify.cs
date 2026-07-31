using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace PowerOfFire.DrawToPlay.Editor
{
    /// <summary>
    /// M4 exit-criterion smoke pass: builds a fresh scene holding the M3 demo limb + three-bone
    /// rig + skin, gives the limb a second FORM (a 1.25x inflated copy of its outline), and
    /// authors a looping two-column pose clip on a <see cref="PoseAnimator"/> — rest at t=0,
    /// middle bone bent 40° with the form fully blended in at t=1. The animator is left
    /// <c>playing</c>, so the saved scene IS the proof of "play it at runtime through
    /// PoseAnimator": press Play and the limb bends and inflates on a loop, with no other script
    /// in the scene.
    ///
    /// Structurally this is M3DemoVerify plus animation — same capsule limb through the
    /// production path (DrawKit.FitCurve → DrawnShapeRenderer), same inline port of
    /// _commit_rig_joints for the chain — with one hierarchy change: the limb and the skeleton
    /// are now children of a "Character" entity root, because pose channel paths resolve by
    /// descendant NAME and one root has to see both the bones and the shape.
    ///
    /// The clip is authored through PoseAnimator.CapturePose, i.e. the exact call the Pose
    /// Sheet's Key button and auto-key poll make. The Sheet's own gestures (scrubbing the
    /// diamond timeline, auto-key detecting a drag) still need a human with a mouse — this
    /// verifies the model underneath them.
    /// </summary>
    internal static class M4DemoVerify
    {
        private const string k_DemoFolder = "Assets/DrawToPlay/Demo";
        private const string k_ScenePath = k_DemoFolder + "/M4AnimDemo.unity";
        private const string k_ShapeAssetPath = k_DemoFolder + "/M4DemoLimb.asset";
        private const string k_RigAssetPath = k_DemoFolder + "/M4DemoRig.asset";
        private const string k_ClipAssetPath = k_DemoFolder + "/M4DemoClip.asset";

        /// <summary>Entity root. Everything the animator addresses lives under it.</summary>
        private const string k_CharacterName = "Character";

        /// <summary>Name of the shape GameObject. Bones are named "{ShapeName}Bone{N}"
        /// (terrain_paint.gd line 325), so this is also the prefix of LimbBone1..3, and it is
        /// the target half of the "Limb:morph" channel path.</summary>
        private const string k_ShapeName = "Limb";
        private const string k_SkeletonName = "Skeleton";

        // Capsule: 2 * 1.55 + 2 * 0.45 = 4.0 world units long, 0.9 thick — same limb as the M3
        // scene, so the two captures are directly comparable.
        private const float k_LimbHalfLength = 1.55f;
        private const float k_LimbRadius = 0.45f;
        private const int k_CapSegments = 12;

        private const int k_BoneCount = 3;
        private const float k_ChainSpan = 3f;

        /// <summary>Bend applied to the middle bone in the t=1 column. Larger than M3's 35°
        /// because a moving pose reads as less than a static one.</summary>
        private const float k_BendDegrees = 40f;

        /// <summary>Form target = the drawn outline scaled uniformly about its centroid. A pure
        /// inflate is the cheapest morph that is unmistakable on camera AND stays a valid ring,
        /// so a failed blend (self-intersection, mis-anchored correspondence) is obvious rather
        /// than plausible.</summary>
        private const float k_MorphScale = 1.25f;

        private const float k_ClipLength = 2f;
        private const float k_RestTime = 0f;
        private const float k_PoseTime = 1f;

        [MenuItem("Tools/Draw To Play/Verify M4 Pose Animation")]
        public static void Verify()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;

            EnsureDemoFolder();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            BuildCamera();

            var character = new GameObject(k_CharacterName);

            var limb = BuildLimb(character.transform);
            AddFormTarget(limb.asset);

            var boneNames = new List<string>();
            var rig = BuildSkeleton(character.transform, boneNames);
            var skin = BindSkin(limb, rig, boneNames);

            var animator = BuildAnimator(character);
            BuildClip(animator, limb, rig, boneNames);

            // The SAVED scene is the rest pose — play mode is what animates away from it. Going
            // through the clip (rather than trusting the values still on the Transforms) is the
            // point: if sampling were broken the saved scene would look wrong immediately. Seek
            // applies internally; the second call is the explicit statement that what got saved
            // came out of Apply, and is a pure re-write of the same values.
            animator.Seek(k_RestTime);
            animator.Apply();
            skin.RegenerateSkin();

            Selection.activeGameObject = character;

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
            // Frames the inflated limb (5 units long) plus the swing of the bent half, which
            // reaches roughly y = 2.4 at the tip.
            camera.orthographicSize = 2.9f;
            camera.transform.position = new Vector3(0f, 0.35f, -10f);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.13f, 0.14f, 0.17f);
        }

        // --- shape ----------------------------------------------------------------------

        private static DrawnShapeRenderer BuildLimb(Transform parent)
        {
            var limbObject = new GameObject(k_ShapeName);
            limbObject.transform.SetParent(parent, false);
            var renderer = limbObject.AddComponent<DrawnShapeRenderer>();

            var asset = ScriptableObject.CreateInstance<DrawnShapeAsset>();
            asset.name = "M4DemoLimb";
            asset.fillColor = new Color(0.72f, 0.45f, 0.32f);
            asset.outlineColor = new Color(0.12f, 0.07f, 0.05f);
            asset.outlineWidth = 0.06f;
            asset.rimColor = new Color(0.96f, 0.78f, 0.58f);
            asset.rimWidth = 0.05f;
            asset.fillShade = 0.35f;
            // Skin params stay at their defaults (skinDetail 0.1875, skinSoftness 0.125).
            asset.curve = DrawKit.FitCurve(BuildCapsuleStroke(), closed: true, tolerance: 0.08f,
                smoothPasses: 2);

            AssetDatabase.CreateAsset(asset, k_ShapeAssetPath);
            renderer.asset = asset;
            renderer.Regenerate();
            return renderer;
        }

        /// <summary>Closed rounded-capsule outline in shape-local units, wound POSITIVE (CCW /
        /// outer ring per the M0 winding contract) — identical to the M3 demo limb.</summary>
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

        /// <summary>Author a second FORM programmatically — what terrain_paint.gd's _capture_form
        /// does after the user reshapes the outline, minus the reshaping: clone the drawn curve
        /// and scale every control point (and its handles, so the bezier keeps its shape) by
        /// <see cref="k_MorphScale"/> about the outline's centroid. The renderer blends
        /// [base, target] positionally by morphWeight, so the two rings only have to be
        /// *comparable*, not to share a point count.</summary>
        private static void AddFormTarget(DrawnShapeAsset asset)
        {
            if (asset == null || asset.curve == null || asset.curve.pointCount < 3)
                return;

            var target = asset.curve.Clone();
            Vector2 center = Centroid(target);
            for (int i = 0; i < target.pointCount; i++)
            {
                var point = target[i];
                point.position = center + (point.position - center) * k_MorphScale;
                point.inHandle *= k_MorphScale;
                point.outHandle *= k_MorphScale;
                target[i] = point;
            }

            if (asset.morphTargets == null)
                asset.morphTargets = new List<DrawnCurve>();
            // Re-running the verify must not stack forms: morphWeight 1 has to mean "target 1".
            asset.morphTargets.Clear();
            asset.morphTargets.Add(target);
            EditorUtility.SetDirty(asset);
        }

        /// <summary>Mean control-point position, ignoring the closing duplicate a fit_curve ring
        /// ends with (counting it twice would drag the centre toward that one point and turn the
        /// uniform scale into a slight slide).</summary>
        private static Vector2 Centroid(DrawnCurve curve)
        {
            int count = curve.pointCount;
            if (count > 1
                && (curve.GetPosition(count - 1) - curve.GetPosition(0)).sqrMagnitude < 1e-8f)
                count--;
            if (count <= 0)
                return Vector2.zero;

            var sum = Vector2.zero;
            for (int i = 0; i < count; i++)
                sum += curve.GetPosition(i);
            return sum / count;
        }

        // --- rig ------------------------------------------------------------------------

        /// <summary>"Skeleton" child of the entity root carrying the ShapeRig and its RigAsset.
        /// Left at identity under the same parent as the limb, so rig-root space and shape-local
        /// space coincide and the joints below are plain shape-local coordinates.</summary>
        private static ShapeRig BuildSkeleton(Transform parent, List<string> boneNames)
        {
            var skeletonObject = new GameObject(k_SkeletonName);
            skeletonObject.transform.SetParent(parent, false);

            var rig = skeletonObject.AddComponent<ShapeRig>();
            var rigAsset = ScriptableObject.CreateInstance<RigAsset>();
            rigAsset.name = "M4DemoRig";
            rigAsset.bones = BuildChainBones(k_ShapeName, BuildChainJoints());
            AssetDatabase.CreateAsset(rigAsset, k_RigAssetPath);

            rig.rig = rigAsset;
            rig.SyncBones();

            for (int i = 0; i < rigAsset.bones.Count; i++)
                boneNames.Add(rigAsset.bones[i].name);
            return rig;
        }

        /// <summary>Four joints along the limb axis → three bones. These stand in for the clicks
        /// RigShapeTool collects.</summary>
        private static List<Vector2> BuildChainJoints()
        {
            var joints = new List<Vector2>(k_BoneCount + 1);
            float step = k_ChainSpan / k_BoneCount;
            for (int i = 0; i <= k_BoneCount; i++)
                joints.Add(new Vector2(-k_ChainSpan * 0.5f + step * i, 0f));
            return joints;
        }

        /// <summary>Port of terrain_paint.gd _commit_rig_joints' bone loop (lines 311-327),
        /// reduced to the data RigAsset stores — the same inline chain math M3DemoVerify uses, so
        /// the two demo scenes agree on bone NAMES and REST poses (the only things downstream
        /// binding and pose paths care about).</summary>
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
        /// replace_include path), then generate.</summary>
        private static DrawnShapeSkin BindSkin(DrawnShapeRenderer limb, ShapeRig rig,
            List<string> boneNames)
        {
            var skin = limb.gameObject.AddComponent<DrawnShapeSkin>();
            skin.rig = rig;

            var asset = limb.asset;
            asset.includeBones = new List<string>(boneNames);
            EditorUtility.SetDirty(asset);

            skin.RegenerateSkin();
            return skin;
        }

        // --- animation ------------------------------------------------------------------

        /// <summary>PoseAnimator goes on the ENTITY ROOT with <c>root</c> pointed at that same
        /// transform. The stub's "null = my parent" default would work for an animator parented
        /// under the character, but naming the root explicitly is the invariant that matters:
        /// channel paths resolve by descendant name, and only the entity root sees BOTH the
        /// skeleton's bones (LimbBone1..3, grandchildren via Skeleton) and the shape (Limb).
        /// Hanging it off the Skeleton instead would resolve the bones and miss "Limb:morph".</summary>
        private static PoseAnimator BuildAnimator(GameObject character)
        {
            var animator = character.AddComponent<PoseAnimator>();
            animator.root = character.transform;
            animator.speed = 1f;
            // Serialized true: entering play mode plays the loop with no Play() call and no
            // driver script — that IS the M4 exit criterion, so it must survive the scene save.
            animator.playing = true;
            return animator;
        }

        /// <summary>Author the two-column loop through the same CapturePose call the Pose Sheet's
        /// Key button makes: pose the rig, capture, pose back.
        ///
        /// Wiring order is load-bearing — CapturePose writes into the animator's CURRENT clip, so
        /// clips/current are assigned BEFORE the first capture rather than after the clip is
        /// filled. (Which is also how the Sheet works: you pick a clip, then key into it.)
        ///
        /// Loop shape: length 2 s with columns at 0 s and 1 s, so the first second blends rest →
        /// bent+inflated and the second holds it before wrapping — pose_clip.Sample holds the
        /// last column past its time, it does not interpolate back to the first. That hold is
        /// deliberate here (it makes the extreme readable on a capture); adding a third column at
        /// t=2 that re-captures the rest pose is all a ping-pong loop would take.</summary>
        private static PoseClipAsset BuildClip(PoseAnimator animator, DrawnShapeRenderer limb,
            ShapeRig rig, List<string> boneNames)
        {
            var clip = ScriptableObject.CreateInstance<PoseClipAsset>();
            clip.name = "M4DemoClip";
            clip.kind = "default";
            clip.looping = true;
            clip.length = k_ClipLength;
            // Empty filter = record every channel. A partial-body layer clip (aim, shot) is the
            // case that fills this in.
            clip.captureFilter.Clear();
            AssetDatabase.CreateAsset(clip, k_ClipAssetPath);

            animator.clips = new List<PoseClipAsset> { clip };
            animator.current = clip.name;

            Capture(animator, k_RestTime);

            int middleIndex = k_BoneCount / 2;
            Transform middle = boneNames.Count > middleIndex
                ? rig.FindBone(boneNames[middleIndex])
                : null;
            Quaternion restRotation = middle != null ? middle.localRotation : Quaternion.identity;
            if (middle != null)
                middle.localRotation = restRotation * Quaternion.Euler(0f, 0f, k_BendDegrees);
            limb.morphWeight = 1f;
            // Not needed by the capture (it reads the field) — it keeps the scene coherent while
            // the extreme is posed, which is what makes this step debuggable by pausing here.
            limb.Regenerate();

            Capture(animator, k_PoseTime);

            if (middle != null)
                middle.localRotation = restRotation;
            limb.morphWeight = 0f;
            limb.Regenerate();

            // InsertPose only ever GROWS length (never shrinks it), and a capture at 1 s must not
            // be allowed to leave the clip 1 s long: pin the 2 s cycle after the columns exist.
            clip.length = k_ClipLength;
            EditorUtility.SetDirty(clip);
            return clip;
        }

        /// <summary>CapturePose returns -1 when it recorded nothing — a silent no-op here would
        /// save a scene that looks right and animates nothing, so the one failure this command
        /// cannot express in the scene is reported instead.</summary>
        private static void Capture(PoseAnimator animator, float time)
        {
            if (animator.CapturePose(time) < 0)
                Debug.LogWarning($"[Verify M4] CapturePose({time:0.##}) recorded no column — " +
                    "the clip will not animate. Check the animator's root and current clip.");
        }
    }
}
