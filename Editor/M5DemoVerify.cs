using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace PowerOfFire.DrawToPlay.Editor
{
    /// <summary>
    /// M5 exit-criterion smoke pass. Both halves of the criterion — "an animated character switches
    /// to ragdoll on keypress and back" and "a drawn prop fragments on impact into physical debris
    /// with matching render meshes" — live in ONE scene, because they are the same claim about the
    /// same pipeline: a drawing becomes physics, and physics writes back to the drawing.
    ///
    /// The scene is:
    ///   • DrawnGround — the M1 bowl, chain collision derived from a fitted curve.
    ///   • Character — the M4 build verbatim (fitted capsule limb, three-bone chain, skinned
    ///     deform, a looping two-column clip left PLAYING) plus a <see cref="RagdollDriver"/> and
    ///     the <see cref="RagdollDemoInput"/> toggler. Press Play: it animates. Press Space: the
    ///     animator pauses, bone capsules take over and it collapses onto the ground. Space again:
    ///     the bodies go away and the animation resumes from wherever the bones landed.
    ///   • DestructibleCrate — a drawn blob with a <see cref="DestructibleShape"/>, dropped from
    ///     height and tipped onto a corner so the landing is off-centre. Its impact speed clears
    ///     the component's threshold on its own, so the fragmenting half needs no keypress at all.
    ///
    /// Everything is built through the production path (DrawKit.FitCurve → DrawnShapeRenderer →
    /// TerrainBlob/DestructibleShape/DrawnShapeSkin). The rig and clip are ported INLINE, exactly as
    /// M3DemoVerify and M4DemoVerify do it: RigShapeTool and the Pose Sheet are click-driven
    /// gestures, and a verify command that needs a human with a mouse is not much of a verify. The
    /// inline chain math is the same port of _commit_rig_joints, so bone NAMES and REST poses — the
    /// only things binding, animation and the ragdoll care about — agree across all three scenes.
    ///
    /// The crate also carries a <see cref="HealthComponent"/> with <c>fragmentOnDeath</c> on. It is
    /// inert (nothing damages it in this scene) and is there to show the §6.3 seam wired up: the
    /// same prop breaks from an impact OR from running out of HP, through one Fragment call.
    /// </summary>
    internal static class M5DemoVerify
    {
        private const string k_DemoFolder = "Assets/DrawToPlay/Demo";
        private const string k_ScenePath = k_DemoFolder + "/M5CombatDemo.unity";
        private const string k_LimbAssetPath = k_DemoFolder + "/M5DemoLimb.asset";
        private const string k_RigAssetPath = k_DemoFolder + "/M5DemoRig.asset";
        private const string k_ClipAssetPath = k_DemoFolder + "/M5DemoClip.asset";
        private const string k_GroundAssetPath = k_DemoFolder + "/M5DemoGround.asset";
        private const string k_CrateAssetPath = k_DemoFolder + "/M5DemoCrate.asset";

        private const string k_SandboxScenePath = "Assets/Sandbox.unity";

        /// <summary>Entity root. Everything the animator addresses lives under it (channel paths
        /// resolve by descendant NAME, so one root has to see both the bones and the shape).</summary>
        private const string k_CharacterName = "Character";

        /// <summary>Name of the shape GameObject. Bones are named "{ShapeName}Bone{N}"
        /// (terrain_paint.gd line 325), so this is also the prefix of LimbBone1..3 and the target
        /// half of the "Limb:morph" channel path.</summary>
        private const string k_ShapeName = "Limb";
        private const string k_SkeletonName = "Skeleton";
        private const string k_GroundName = "DrawnGround";
        private const string k_CrateName = "DestructibleCrate";

        // --- character (M4 limb, unchanged so the three demo scenes stay comparable) -----

        private const float k_LimbHalfLength = 1.55f;
        private const float k_LimbRadius = 0.45f;
        private const int k_CapSegments = 12;

        private const int k_BoneCount = 3;
        private const float k_ChainSpan = 3f;
        private const float k_BendDegrees = 40f;
        private const float k_MorphScale = 1.25f;

        private const float k_ClipLength = 2f;
        private const float k_RestTime = 0f;
        private const float k_PoseTime = 1f;

        /// <summary>Character sits left of centre and about 3.5 units above the bowl surface under
        /// it — far enough that the ragdoll visibly FALLS rather than merely sagging, close enough
        /// that it lands inside the frame.</summary>
        private static readonly Vector3 k_CharacterPosition = new Vector3(-3f, 1.4f, 0f);

        // --- ground (M1 bowl) ------------------------------------------------------------

        private static readonly Vector3 k_GroundPosition = new Vector3(0f, -3f, 0f);
        private const float k_BowlHalfWidth = 7f;
        private const float k_BowlStep = 0.35f;
        private const float k_BowlLip = 0.5f;
        private const float k_BowlCurvature = 0.035f;
        private const float k_BowlDepth = -2.5f;

        // --- crate -----------------------------------------------------------------------

        /// <summary>Dropped from ~6 units above the bowl. Even at a quarter of Earth gravity that
        /// lands well past DestructibleShape's default 4 wu/s impact threshold, so the fragmenting
        /// half of the criterion does not depend on the world's gravity setting.</summary>
        private static readonly Vector3 k_CratePosition = new Vector3(2.4f, 4.2f, 0f);

        /// <summary>Tipped off-axis so it lands on a corner: an off-centre impact spreads the
        /// fragment points asymmetrically, which is what makes the break read as a break instead of
        /// a symmetric explosion.</summary>
        private const float k_CrateTiltDegrees = 18f;

        /// <summary>Half-width of the rounded square, world units.</summary>
        private const float k_CrateRadius = 0.6f;
        private const int k_CrateSegments = 28;

        /// <summary>Superellipse exponent: 2 is a circle, ∞ is a square. 4 gives a crate-ish
        /// rounded box whose flats are wide enough to produce slab-shaped fragments.</summary>
        private const float k_CrateSquareness = 4f;

        /// <summary>Low-frequency radial wobble, so the "crate" is hand-drawn rather than
        /// generated-looking — and so its fragments are not four identical quarters.</summary>
        private const float k_CrateWobble = 0.07f;

        private const float k_CrateMaxHP = 3f;

        [MenuItem("Tools/Draw To Play/Verify M5 Ragdoll + Destruction")]
        public static void Verify()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;

            EnsureDemoFolder();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            BuildCamera();
            BuildGround();

            var character = new GameObject(k_CharacterName);
            character.transform.position = k_CharacterPosition;

            var limb = BuildLimb(character.transform);
            AddFormTarget(limb.asset);

            var boneNames = new List<string>();
            var rig = BuildSkeleton(character.transform, boneNames);
            var skin = BindSkin(limb, rig, boneNames);

            var animator = BuildAnimator(character);
            BuildClip(animator, limb, rig, boneNames);
            BuildRagdoll(character, rig, animator);

            // The SAVED scene is the rest pose — play mode is what animates away from it. Going
            // through the clip rather than trusting the values left on the Transforms is the point:
            // if sampling were broken the saved scene would look wrong immediately.
            animator.Seek(k_RestTime);
            animator.Apply();
            skin.RegenerateSkin();

            BuildCrate();

            Selection.activeGameObject = character;

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, k_ScenePath);
            AssetDatabase.SaveAssets();
            EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<SceneAsset>(k_ScenePath));
        }

        /// <summary>Play the demo scene despite the project's StartupScene convention, which
        /// re-points playModeStartScene at Sandbox.unity on every Play press. Our handler is
        /// subscribed at click time — after StartupScene's load-time subscription — so within the
        /// same ExitingEditMode event it runs later and its assignment wins (C# multicast delegates
        /// invoke in subscription order). The Sandbox convention is restored when play mode ends.
        ///
        /// This matters more here than it did in M1: half the criterion is a KEYPRESS, and a scene
        /// you cannot actually reach with Play cannot be verified at all.</summary>
        [MenuItem("Tools/Draw To Play/Play M5 Demo Scene")]
        public static void PlayDemo()
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(k_ScenePath) == null)
            {
                Verify();
            }
            else if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().path != k_ScenePath)
            {
                if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                    return;
                EditorSceneManager.OpenScene(k_ScenePath, OpenSceneMode.Single);
            }

            EditorApplication.playModeStateChanged += OverrideStartScene;
            EditorApplication.EnterPlaymode();
        }

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
                    AssetDatabase.LoadAssetAtPath<SceneAsset>(k_SandboxScenePath);
            }
        }

        private static void EnsureDemoFolder()
        {
            if (!AssetDatabase.IsValidFolder(k_DemoFolder))
                AssetDatabase.CreateFolder("Assets/DrawToPlay", "Demo");
        }

        /// <summary>Frames the bowl (±7), the crate's drop (y 4.8 down) and the character's fall.
        /// At 16:9 the horizontal half-extent is ~8.9 units, so the bowl's lips stay in shot.</summary>
        private static void BuildCamera()
        {
            var cameraObject = new GameObject("Main Camera");
            cameraObject.tag = "MainCamera";
            var camera = cameraObject.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 5f;
            camera.transform.position = new Vector3(0f, 0.6f, -10f);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.13f, 0.14f, 0.17f);
        }

        // --- ground ----------------------------------------------------------------------

        /// <summary>The M1 demo's bowl, lowered so there is room to fall onto it. Chain collision
        /// (TerrainBlob's default) is the right mode: it is one-sided walkable ground, which is
        /// what both the ragdoll and the crate need to land on.</summary>
        private static TerrainBlob BuildGround()
        {
            var groundObject = new GameObject(k_GroundName);
            groundObject.transform.position = k_GroundPosition;
            var renderer = groundObject.AddComponent<DrawnShapeRenderer>();

            var asset = ScriptableObject.CreateInstance<DrawnShapeAsset>();
            asset.name = "M5DemoGround";
            asset.fillColor = new Color(0.24f, 0.36f, 0.25f);
            asset.outlineColor = new Color(0.05f, 0.08f, 0.05f);
            asset.rimColor = new Color(0.45f, 0.62f, 0.4f);
            asset.rimWidth = 0.12f;
            asset.curve = DrawKit.FitCurve(BuildBowlStroke(), closed: true, tolerance: 0.08f,
                smoothPasses: 2);
            AssetDatabase.CreateAsset(asset, k_GroundAssetPath);

            renderer.asset = asset;
            renderer.Regenerate();

            // Added last: TerrainBlob derives its collision from the renderer, and in play mode it
            // does so in OnEnable — the drawing has to be there first.
            return groundObject.AddComponent<TerrainBlob>();
        }

        /// <summary>Closed outline of a shallow bowl: curved top surface (so things roll toward the
        /// middle), straight sides and bottom. Local space, world units — the M1 stroke.</summary>
        private static List<Vector2> BuildBowlStroke()
        {
            var points = new List<Vector2>();
            for (float x = -k_BowlHalfWidth; x <= k_BowlHalfWidth; x += k_BowlStep)
                points.Add(new Vector2(x, k_BowlLip + k_BowlCurvature * x * x));
            points.Add(new Vector2(k_BowlHalfWidth, k_BowlDepth));
            points.Add(new Vector2(-k_BowlHalfWidth, k_BowlDepth));
            return points;
        }

        // --- character shape -------------------------------------------------------------

        private static DrawnShapeRenderer BuildLimb(Transform parent)
        {
            var limbObject = new GameObject(k_ShapeName);
            limbObject.transform.SetParent(parent, false);
            var renderer = limbObject.AddComponent<DrawnShapeRenderer>();

            var asset = ScriptableObject.CreateInstance<DrawnShapeAsset>();
            asset.name = "M5DemoLimb";
            asset.fillColor = new Color(0.72f, 0.45f, 0.32f);
            asset.outlineColor = new Color(0.12f, 0.07f, 0.05f);
            asset.outlineWidth = 0.06f;
            asset.rimColor = new Color(0.96f, 0.78f, 0.58f);
            asset.rimWidth = 0.05f;
            asset.fillShade = 0.35f;
            // Skin params stay at their defaults (skinDetail 0.1875, skinSoftness 0.125).
            asset.curve = DrawKit.FitCurve(BuildCapsuleStroke(), closed: true, tolerance: 0.08f,
                smoothPasses: 2);
            AssetDatabase.CreateAsset(asset, k_LimbAssetPath);

            renderer.asset = asset;
            renderer.Regenerate();
            return renderer;
        }

        /// <summary>Closed rounded-capsule outline in shape-local units, wound POSITIVE (CCW /
        /// outer ring per the M0 winding contract).</summary>
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
        /// does after the user reshapes the outline, minus the reshaping: clone the drawn curve and
        /// scale every control point (and its handles, so the bezier keeps its shape) about the
        /// outline's centroid.</summary>
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
        /// ends with (counting it twice would drag the centre toward that one point).</summary>
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

        // --- rig ---------------------------------------------------------------------------

        /// <summary>"Skeleton" child of the entity root carrying the ShapeRig and its RigAsset.
        /// Left at LOCAL identity under the same parent as the limb, so rig-root space and
        /// shape-local space coincide wherever the character is placed — which is what lets the
        /// joint coordinates below stay plain shape-local numbers even though this scene moves the
        /// character off the origin.</summary>
        private static ShapeRig BuildSkeleton(Transform parent, List<string> boneNames)
        {
            var skeletonObject = new GameObject(k_SkeletonName);
            skeletonObject.transform.SetParent(parent, false);

            var rig = skeletonObject.AddComponent<ShapeRig>();
            var rigAsset = ScriptableObject.CreateInstance<RigAsset>();
            rigAsset.name = "M5DemoRig";
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

        /// <summary>Port of terrain_paint.gd _commit_rig_joints' bone loop (lines 311-327), reduced
        /// to the data RigAsset stores — the same inline chain math M3/M4DemoVerify use, so all
        /// three demo scenes agree on bone NAMES and REST poses. The ragdoll adds a third consumer
        /// of those rests: capsule length comes from RigBone.length and hinge limits are measured
        /// relative to the rest relative angle.</summary>
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

        /// <summary>Bind: include the chain by NAME (replacing, like _bind_shape's replace_include
        /// path), then generate. Order matters — RegenerateSkin filters the rig through
        /// includeBones, so the list has to be on the asset first.</summary>
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

        // --- animation ---------------------------------------------------------------------

        /// <summary>PoseAnimator on the ENTITY ROOT with <c>root</c> pointed at that same transform:
        /// channel paths resolve by descendant name, and only the entity root sees BOTH the
        /// skeleton's bones (LimbBone1..3, grandchildren via Skeleton) and the shape (Limb).</summary>
        private static PoseAnimator BuildAnimator(GameObject character)
        {
            var animator = character.AddComponent<PoseAnimator>();
            animator.root = character.transform;
            animator.speed = 1f;
            // Serialized true: entering play mode plays the loop with no Play() call, so the
            // "animated character" half of the criterion is true before any key is pressed — which
            // is what makes the ragdoll toggle a visible STATE CHANGE rather than a start signal.
            animator.playing = true;
            return animator;
        }

        /// <summary>Author the two-column loop through the same CapturePose call the Pose Sheet's
        /// Key button makes: pose the rig, capture, pose back. Wiring order is load-bearing —
        /// CapturePose writes into the animator's CURRENT clip, so clips/current are assigned
        /// BEFORE the first capture.</summary>
        private static PoseClipAsset BuildClip(PoseAnimator animator, DrawnShapeRenderer limb,
            ShapeRig rig, List<string> boneNames)
        {
            var clip = ScriptableObject.CreateInstance<PoseClipAsset>();
            clip.name = "M5DemoClip";
            clip.kind = "default";
            clip.looping = true;
            clip.length = k_ClipLength;
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
            limb.Regenerate();

            Capture(animator, k_PoseTime);

            if (middle != null)
                middle.localRotation = restRotation;
            limb.morphWeight = 0f;
            limb.Regenerate();

            // InsertPose only ever GROWS length: pin the 2 s cycle after the columns exist.
            clip.length = k_ClipLength;
            EditorUtility.SetDirty(clip);
            return clip;
        }

        /// <summary>CapturePose returns -1 when it recorded nothing — a silent no-op here would
        /// save a scene that looks right and animates nothing.</summary>
        private static void Capture(PoseAnimator animator, float time)
        {
            if (animator.CapturePose(time) < 0)
                Debug.LogWarning($"[Verify M5] CapturePose({time:0.##}) recorded no column — the " +
                    "clip will not animate. Check the animator's root and current clip.");
        }

        // --- ragdoll -----------------------------------------------------------------------

        /// <summary>Driver and toggler both on the entity root, next to the animator: the driver
        /// needs to pause that animator, and the toggler resolves its driver from the same
        /// GameObject. Tuning is left at the component defaults on purpose — this scene exists to
        /// show that the DEFAULT auto-generated capsules and hinge limits produce a usable ragdoll,
        /// the same argument M3's scene makes for the default skin weights.</summary>
        private static RagdollDriver BuildRagdoll(GameObject character, ShapeRig rig,
            PoseAnimator animator)
        {
            var driver = character.AddComponent<RagdollDriver>();
            driver.rig = rig;
            driver.animator = animator;

            var input = character.AddComponent<RagdollDemoInput>();
            input.driver = driver;
            return driver;
        }

        // --- destructible prop ---------------------------------------------------------------

        /// <summary>A drawn blob with destruction wired up, hanging above the bowl. Component order
        /// matters the same way the ground's does: DestructibleShape derives its collision from the
        /// renderer's baked ring, so the drawing exists before the component is added.</summary>
        private static DestructibleShape BuildCrate()
        {
            var crateObject = new GameObject(k_CrateName);
            crateObject.transform.position = k_CratePosition;
            crateObject.transform.rotation = Quaternion.Euler(0f, 0f, k_CrateTiltDegrees);
            var renderer = crateObject.AddComponent<DrawnShapeRenderer>();

            var asset = ScriptableObject.CreateInstance<DrawnShapeAsset>();
            asset.name = "M5DemoCrate";
            asset.fillColor = new Color(0.55f, 0.38f, 0.2f);
            asset.outlineColor = new Color(0.13f, 0.08f, 0.04f);
            asset.outlineWidth = 0.05f;
            asset.rimColor = new Color(0.83f, 0.66f, 0.4f);
            asset.rimWidth = 0.04f;
            asset.fillShade = 0.3f;
            // Tighter fit tolerance than the limb: the crate is a tenth the size, and 0.08 would
            // simplify its flats and corners into a circle.
            asset.curve = DrawKit.FitCurve(BuildCrateStroke(), closed: true, tolerance: 0.05f,
                smoothPasses: 1);
            AssetDatabase.CreateAsset(asset, k_CrateAssetPath);

            renderer.asset = asset;
            renderer.Regenerate();

            var destructible = crateObject.AddComponent<DestructibleShape>();

            // The §6.3 seam, wired but unfired: nothing in this scene deals damage, so the crate
            // breaks from the impact. Give it HP anyway so the alternative path is one TakeDamage
            // call away for anyone poking at the scene.
            var health = crateObject.AddComponent<HealthComponent>();
            health.maxHP = k_CrateMaxHP;
            health.team = CombatTeam.Enemy;
            health.fragmentOnDeath = true;
            health.destructible = destructible;

            return destructible;
        }

        /// <summary>Closed outline of a wobbly rounded square, wound POSITIVE (CCW, θ increasing).
        /// A superellipse gives flats and corners a circle would not have, so the fragments come out
        /// as slabs and wedges — recognisably pieces OF something — and the low-frequency wobble
        /// keeps them from being four identical quarters.</summary>
        private static List<Vector2> BuildCrateStroke()
        {
            var points = new List<Vector2>(k_CrateSegments);
            for (int i = 0; i < k_CrateSegments; i++)
            {
                float angle = i / (float)k_CrateSegments * Mathf.PI * 2f;
                float cos = Mathf.Cos(angle);
                float sin = Mathf.Sin(angle);

                // Superellipse in polar form: r = 1 / (|cos|^n + |sin|^n)^(1/n).
                float denominator = Mathf.Pow(
                    Mathf.Pow(Mathf.Abs(cos), k_CrateSquareness)
                    + Mathf.Pow(Mathf.Abs(sin), k_CrateSquareness), 1f / k_CrateSquareness);
                float radius = k_CrateRadius / Mathf.Max(denominator, 1e-4f);
                radius *= 1f + k_CrateWobble * Mathf.Sin(angle * 3f + 0.7f);

                points.Add(new Vector2(cos * radius, sin * radius));
            }
            return points;
        }
    }
}
