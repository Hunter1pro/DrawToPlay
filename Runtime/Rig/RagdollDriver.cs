using System.Collections.Generic;
using Unity.U2D.Physics;
using UnityEngine;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// Ragdoll from the rig, for free (brief §5): each RigAsset bone (position, rotation,
    /// length) maps to a capsule — CapsuleGeometry along the bone's local X — on a dynamic
    /// body per bone; parent-child bones connect with PhysicsHingeJoints anchored at the
    /// child bone origin, angle limits authored here relative to the REST relative angle.
    /// Animate mode = no physics bodies at all (PoseAnimator owns the bones); ragdoll mode
    /// = dynamic bodies with body.transformObject = bone Transform, so bones — and through
    /// them the skinned mesh — follow the simulation automatically. StartRagdoll pauses the
    /// PoseAnimator; StopRagdoll destroys the physics and hands the bones back.
    ///
    /// WHY THE BONE TRANSFORM IS THE WHOLE OUTPUT PATH: DrawnShapeSkin binds a
    /// SkinnedMeshRenderer to the very Transforms ShapeRig created, with REST-derived
    /// bindposes. Nothing here touches the skin — moving the bones is moving the art.
    ///
    /// POSE AT HANDOVER: bodies are created at each bone's CURRENT world pose, so switching
    /// mid-animation is seamless. The hinge limit windows are still measured from REST (see
    /// <see cref="hingeLowerDegrees"/>), which is the whole point of a rig-derived ragdoll:
    /// an elbow may not straighten past its rest angle no matter what pose it died in. A pose
    /// already outside its window is pulled back into it over the first few solver steps.
    ///
    /// PLAY MODE ONLY, no [ExecuteAlways] — the same rule the rest of this package follows.
    /// Bodies exist only between StartRagdoll and StopRagdoll/OnDisable.
    /// </summary>
    public sealed class RagdollDriver : MonoBehaviour
    {
        /// <summary>Below this a shape stops being valid geometry; also the minimum gap kept
        /// between a capsule's two centers so it never degenerates into a point.</summary>
        private const float k_MinRadius = 0.005f;

        private const float k_CapsuleMargin = 0.005f;

        /// <summary>Cycle guard for the parentIndex walks — same value ShapeRig and
        /// RigAsset.RestRootMatrix use, for the same reason (a hand-edited asset can contain
        /// a parent cycle and must not hang the editor).</summary>
        private const int k_ChainGuard = 256;

        /// <summary>A hinge angle wraps to (-180, 180], so a limit outside that is simply
        /// unreachable. Limits are clamped here rather than silently producing a window the
        /// solver can never enforce.</summary>
        private const float k_AngleLimit = 180f;

        public ShapeRig rig;
        /// <summary>Paused while ragdolling; optional.</summary>
        public PoseAnimator animator;

        /// <summary>Capsule radius = bone length * this, clamped to [minBoneRadius, ∞).</summary>
        [Range(0.05f, 1f)] public float boneRadiusScale = 0.35f;
        public float minBoneRadius = 0.04f;

        public PhysicsBodyDefinition bodyDefinition = PhysicsBodyDefinition.defaultDefinition;
        public PhysicsShapeDefinition shapeDefinition = PhysicsShapeDefinition.defaultDefinition;

        /// <summary>Hinge limits in DEGREES around each joint's rest relative angle.
        ///
        /// These go to PhysicsHingeJointDefinition.lowerAngleLimit/upperAngleLimit, which are
        /// DEGREES in 6000.5 (verified: UnityEngine.PhysicsCore2DModule.xml, "The lower angle
        /// limit, in degrees") — no conversion happens anywhere in this file. See the report:
        /// the joints *pattern* skill still says radians and is stale.</summary>
        public float hingeLowerDegrees = -60f;
        public float hingeUpperDegrees = 60f;

        public bool isRagdolling { get; private set; }

        /// <summary>One entry per bone body, in creation order. The three lists below are
        /// index-aligned with each other; <see cref="m_BodyIndexByAsset"/> maps the other way,
        /// from a RigAsset bone index into them (-1 = that bone got no body).</summary>
        private readonly List<int> m_BoneOrder = new List<int>();

        private readonly List<Transform> m_BoneTransforms = new List<Transform>();
        private readonly List<PhysicsBody> m_Bodies = new List<PhysicsBody>();
        private readonly List<PhysicsHingeJoint> m_Joints = new List<PhysicsHingeJoint>();
        private readonly HashSet<string> m_UsedNames = new HashSet<string>();
        private int[] m_BodyIndexByAsset = System.Array.Empty<int>();

        /// <summary>The animator instance that was paused, remembered separately from the
        /// serialized field: reassigning <see cref="animator"/> mid-ragdoll must not leave the
        /// original one stopped forever.</summary>
        private PoseAnimator m_PausedAnimator;

        private bool m_AnimatorWasPlaying;

        /// <summary>Create dynamic bone bodies + hinges at the CURRENT pose and let physics
        /// take over. No-op while already ragdolling. Play mode only.</summary>
        public void StartRagdoll()
        {
            if (isRagdolling || !Application.isPlaying)
                return;
            if (rig == null || rig.rig == null || rig.rig.bones == null)
                return;

            // Always the default world (brief §3); invalid only very early in a domain reload.
            PhysicsWorld world = PhysicsWorld.defaultWorld;
            if (!world.isValid)
                return;

            if (!CollectBones())
                return;

            PauseAnimator();
            CreateBodies(world);
            CreateJoints(world);
            isRagdolling = true;
        }

        /// <summary>Destroy joints + bodies; bones keep their last simulated pose and the
        /// animator (if any) resumes ownership. No-op while not ragdolling.</summary>
        public void StopRagdoll()
        {
            if (!isRagdolling)
                return;
            Teardown();
            ResumeAnimator();
            isRagdolling = false;
        }

        public void Toggle()
        {
            if (isRagdolling) StopRagdoll();
            else StartRagdoll();
        }

        // --- lifecycle ---------------------------------------------------------------------

        /// <summary>Unconditional teardown, deliberately not guarded by
        /// <see cref="isRagdolling"/>: a StartRagdoll that failed half way (invalid world,
        /// bone destroyed under us) still has handles to release. Destroying bodies here is
        /// legal — OnDisable is a main-thread MonoBehaviour callback, never inside a solver
        /// callback, so the WORM rule is satisfied (m1 conventions).</summary>
        private void OnDisable()
        {
            Teardown();
            ResumeAnimator();
            isRagdolling = false;
        }

        // --- bone selection ----------------------------------------------------------------

        /// <summary>Usable bones in RigAsset order, refined to a PARENT-FIRST order.
        ///
        /// Usable = named, not a duplicate name, and backed by a scene Transform (a RigAsset
        /// entry can exist before ShapeRig.SyncBones ran, and DrawnShapeSkin skips those for
        /// the same reason). Duplicate names are dropped rather than doubled up because
        /// ShapeRig.FindBone resolves by name and would hand two bodies the same Transform to
        /// write — first entry wins, matching ShapeRig's own rule.
        ///
        /// PARENT-FIRST matters because a body writes its GLOBAL pose to its Transform
        /// (PhysicsWorld.SetTransform takes "the global position/rotation to set"). A child
        /// bone written before its parent is then dragged by the parent's write and only
        /// re-corrected on the next step — a one-frame wobble, not accumulating drift, since
        /// every write is absolute. Creating parent-first is the cheapest hedge available: it
        /// costs nothing and makes the writes exact for any writer that preserves creation
        /// order. Bones are bucketed by chain depth with a stable inner pass, so within a
        /// depth the RigAsset order is preserved exactly.</summary>
        private bool CollectBones()
        {
            m_BoneOrder.Clear();
            m_BoneTransforms.Clear();
            m_UsedNames.Clear();

            List<RigAsset.RigBone> bones = rig.rig.bones;
            if (m_BodyIndexByAsset.Length != bones.Count)
                m_BodyIndexByAsset = new int[bones.Count];
            for (int i = 0; i < m_BodyIndexByAsset.Length; i++)
                m_BodyIndexByAsset[i] = -1;

            int maxDepth = 0;
            for (int i = 0; i < bones.Count; i++)
                maxDepth = Mathf.Max(maxDepth, Depth(bones, i));

            for (int depth = 0; depth <= maxDepth; depth++)
            {
                for (int i = 0; i < bones.Count; i++)
                {
                    if (Depth(bones, i) != depth)
                        continue;
                    string boneName = bones[i].name;
                    if (string.IsNullOrEmpty(boneName) || !m_UsedNames.Add(boneName))
                        continue;
                    Transform bone = rig.FindBone(boneName);
                    if (bone == null)
                        continue;
                    m_BoneOrder.Add(i);
                    m_BoneTransforms.Add(bone);
                }
            }
            return m_BoneOrder.Count > 0;
        }

        /// <summary>Number of parent links above a bone. Guarded against the out-of-range and
        /// cyclic parentIndex values a hand-edited RigAsset can hold.</summary>
        private static int Depth(List<RigAsset.RigBone> bones, int index)
        {
            int depth = 0;
            int walk = bones[index].parentIndex;
            int guard = 0;
            while (walk >= 0 && walk < bones.Count && guard++ < k_ChainGuard)
            {
                depth++;
                walk = bones[walk].parentIndex;
            }
            return depth;
        }

        // --- bodies ------------------------------------------------------------------------

        /// <summary>One dynamic body per usable bone, created AT the bone's current world pose
        /// (creating at the origin and moving afterwards nearly doubles the cost —
        /// PhysicsBodyDefinition.position) and wired to write back through transformObject.
        ///
        /// Every entry is appended even if creation somehow failed, so the list stays index-
        /// aligned with <see cref="m_BoneOrder"/>; the joint pass re-checks isValid.</summary>
        private void CreateBodies(PhysicsWorld world)
        {
            List<RigAsset.RigBone> bones = rig.rig.bones;
            for (int i = 0; i < m_BoneOrder.Count; i++)
            {
                int assetIndex = m_BoneOrder[i];
                Transform bone = m_BoneTransforms[i];

                // Work on a COPY: the serialized definition stays exactly as authored, so a
                // second StartRagdoll reproduces the same bodies (EntityBody's rule).
                PhysicsBodyDefinition definition = bodyDefinition;

                // Forced, not merely defaulted — a ragdoll bone that is Static or Kinematic
                // would silently pin the whole chain.
                definition.type = PhysicsBody.BodyType.Dynamic;
                definition.position = (Vector2)bone.position;

                // 2D rotation from the Transform's world Z Euler angle, exactly as EntityBody
                // does it: correct for the XY transform plane and Z-only rotations, which is
                // the whole of the 2D authoring case.
                definition.rotation = PhysicsRotate.FromDegrees(bone.eulerAngles.z);

                // Local first: PhysicsBody is a handle struct and C# forbids property setters
                // on a by-value struct returned from a property (so m_Bodies[i].transformObject
                // would not compile).
                PhysicsBody body = world.CreateBody(definition);
                if (body.isValid)
                {
                    // The entire output path: physics writes the bone, the bone drives the
                    // skinned mesh. Nothing in this file touches DrawnShapeSkin.
                    body.transformObject = bone;
                    CreateBoneShape(body, bone, bones[assetIndex].length);
                }

                m_BodyIndexByAsset[assetIndex] = m_Bodies.Count;
                m_Bodies.Add(body);
            }
        }

        /// <summary>The bone's collision volume: a capsule laid along the body's local +X, from
        /// (r, 0) to (length - r, 0), because a bone's local +X IS its length axis (RigAsset
        /// rests place the tip at (length, 0) — the same convention DrawnShapeSkin binds
        /// weights against).
        ///
        /// DEGENERATE BONES: when the requested radius does not leave a segment between the two
        /// centers — a very short bone, a zero-length leaf, or boneRadiusScale above 0.5 on a
        /// bone already at minBoneRadius — the capsule collapses (and a reversed one, with
        /// center2 behind center1, would extend backwards past the joint). Those fall back to a
        /// CircleGeometry at the bone midpoint: always valid, never reversed, and it keeps the
        /// bone in the chain so its children stay hinged instead of dropping free.</summary>
        private void CreateBoneShape(PhysicsBody body, Transform bone, float restLength)
        {
            float length = WorldBoneLength(bone, restLength);
            float half = length * 0.5f;
            float minRadius = Mathf.Max(minBoneRadius, k_MinRadius);

            // Mathf.Clamp's LOWER bound wins when the range inverts, which is exactly the
            // degenerate case: the resulting radius is then larger than half the bone.
            float radius = Mathf.Clamp(length * Mathf.Max(boneRadiusScale, 0f),
                minRadius, half - k_CapsuleMargin);

            if (radius <= half - k_CapsuleMargin)
            {
                body.CreateShape(new CapsuleGeometry
                {
                    center1 = new Vector2(radius, 0f),
                    center2 = new Vector2(length - radius, 0f),
                    radius = radius
                }, shapeDefinition);
                return;
            }

            body.CreateShape(new CircleGeometry
            {
                center = new Vector2(half, 0f),
                radius = radius
            }, shapeDefinition);
        }

        /// <summary>Bone length in WORLD units. PhysicsCore2D has no scale, so the rig-local
        /// length has to be pushed through the bone's transform first — the same problem
        /// TerrainBlob solves by baking lossyScale into its vertices and RollingBall by scaling
        /// its radius. Measuring the transformed tip covers the whole ancestor chain's scale in
        /// one step; the XY magnitude is used because the physics plane is XY.
        ///
        /// Non-uniform scale is unsupported (the capsule would no longer follow the body's
        /// local X), as is negative scale (it mirrors the axis while the body's Z-Euler
        /// rotation does not) — the same limits EntityBody and RollingBall already document.
        /// </summary>
        private static float WorldBoneLength(Transform bone, float restLength)
        {
            if (restLength <= 0f)
                return 0f;
            Vector3 tip = bone.TransformPoint(new Vector3(restLength, 0f, 0f));
            return ((Vector2)(tip - bone.position)).magnitude;
        }

        // --- joints ------------------------------------------------------------------------

        /// <summary>A hinge per bone that has an ancestor body, anchored at the CHILD bone's
        /// origin. Bones without one (chain roots) stay free-floating, which is what makes the
        /// ragdoll fall.
        ///
        /// ANCHORS: both are derived from the shared world pivot via GetLocalPoint, so the two
        /// anchor frames coincide exactly at creation and the solver has nothing to correct —
        /// "pre-position bodies so the anchors coincide" (joints skill), and the pattern the
        /// verified RagdollFactory example uses. localAnchorB comes out as (0,0) because the
        /// child body was created AT that pivot.
        ///
        /// LIMITS ARE REST-RELATIVE: with identity anchor frames the joint angle is the current
        /// relative rotation of the two bodies, which at rest equals the child's
        /// restRotationDegrees (a rest pose is stored local to the parent bone). So the window
        /// is restRotation + [lower, upper] — an elbow authored bent stays bent-relative, not
        /// straight-relative. When intermediate bones were skipped (no scene Transform) their
        /// rest rotations accumulate into that offset, keeping the chain intact and the window
        /// honest.
        ///
        /// collideConnected stays false so a bone never fights its own parent. Non-adjacent
        /// bones CAN still collide with each other — accepted M5 noise; authoring a negative
        /// contactFilter.groupIndex on <see cref="shapeDefinition"/> switches all self-
        /// collision off the way RagdollFactory does, without any code change here.</summary>
        private void CreateJoints(PhysicsWorld world)
        {
            List<RigAsset.RigBone> bones = rig.rig.bones;
            for (int i = 0; i < m_BoneOrder.Count; i++)
            {
                PhysicsBody childBody = m_Bodies[i];
                if (!childBody.isValid)
                    continue;

                int assetIndex = m_BoneOrder[i];
                float restRelativeDegrees = bones[assetIndex].restRotationDegrees;
                int parentBodyIndex = -1;
                int walk = bones[assetIndex].parentIndex;
                int guard = 0;
                while (walk >= 0 && walk < bones.Count && guard++ < k_ChainGuard)
                {
                    if (m_BodyIndexByAsset[walk] >= 0)
                    {
                        parentBodyIndex = m_BodyIndexByAsset[walk];
                        break;
                    }
                    // a skipped bone contributes its own rest rotation to the offset
                    restRelativeDegrees += bones[walk].restRotationDegrees;
                    walk = bones[walk].parentIndex;
                }
                if (parentBodyIndex < 0)
                    continue;

                PhysicsBody parentBody = m_Bodies[parentBodyIndex];
                if (!parentBody.isValid)
                    continue;

                Vector2 pivot = (Vector2)m_BoneTransforms[i].position;

                // Always from defaultDefinition, never a zero-init struct (joints skill).
                PhysicsHingeJointDefinition definition = PhysicsHingeJointDefinition.defaultDefinition;
                definition.bodyA = parentBody;
                definition.bodyB = childBody;
                definition.localAnchorA = new PhysicsTransform(parentBody.GetLocalPoint(pivot));
                definition.localAnchorB = new PhysicsTransform(childBody.GetLocalPoint(pivot));
                definition.collideConnected = false;
                definition.enableLimit = true;
                ComputeLimits(restRelativeDegrees, hingeLowerDegrees, hingeUpperDegrees,
                    out float lower, out float upper);
                definition.lowerAngleLimit = lower;
                definition.upperAngleLimit = upper;

                m_Joints.Add(PhysicsHingeJoint.Create(world, definition));
            }
        }

        /// <summary>Slide the authored window onto the rest angle. The rest angle is wrapped
        /// into (-180, 180] first because the joint angle is, and the result is clamped to the
        /// same range: a window wider than the joint can express is not representable, and
        /// clamping makes that side effectively free rather than permanently violated. An
        /// inverted authored pair (lower above upper) is swapped rather than fed to the solver
        /// as an empty window.</summary>
        private static void ComputeLimits(float restRelativeDegrees, float lowerDegrees,
            float upperDegrees, out float lower, out float upper)
        {
            if (lowerDegrees > upperDegrees)
            {
                float swap = lowerDegrees;
                lowerDegrees = upperDegrees;
                upperDegrees = swap;
            }
            // Mathf.DeltaAngle(0, x) is the shortest signed angle to x, i.e. x wrapped.
            float rest = Mathf.DeltaAngle(0f, restRelativeDegrees);
            lower = Mathf.Clamp(rest + lowerDegrees, -k_AngleLimit, k_AngleLimit);
            upper = Mathf.Clamp(rest + upperDegrees, -k_AngleLimit, k_AngleLimit);
        }

        // --- teardown ----------------------------------------------------------------------

        /// <summary>Joints first, then bodies. Destroying a body cascades to its joints, so the
        /// order is not strictly required — but it is the documented practice and it keeps
        /// every handle in the lists valid right up to the moment it is destroyed. Bones simply
        /// stop being written and keep their last simulated pose.</summary>
        private void Teardown()
        {
            for (int i = 0; i < m_Joints.Count; i++)
            {
                if (m_Joints[i].isValid)
                    m_Joints[i].Destroy();
            }
            m_Joints.Clear();

            for (int i = 0; i < m_Bodies.Count; i++)
            {
                if (m_Bodies[i].isValid)
                    m_Bodies[i].Destroy();
            }
            m_Bodies.Clear();

            m_BoneOrder.Clear();
            m_BoneTransforms.Clear();
            m_UsedNames.Clear();
            for (int i = 0; i < m_BodyIndexByAsset.Length; i++)
                m_BodyIndexByAsset[i] = -1;
        }

        // --- animator ----------------------------------------------------------------------

        /// <summary>Stop the animator writing bone Transforms it no longer owns, remembering
        /// whether it was playing so StopRagdoll can hand ownership back exactly as found.
        ///
        /// PoseAnimator.Advance does nothing at all when playing is false and no layer slots
        /// are live, so the flag is a complete pause for the ordinary single-clip case. A clip
        /// running in a LAYER slot (LayerPlay) would keep advancing and re-Applying over the
        /// simulated pose — see the report; stopping those would need LayerStop, which itself
        /// calls Apply and would stamp the rig mid-ragdoll.</summary>
        private void PauseAnimator()
        {
            if (animator == null)
                return;
            m_PausedAnimator = animator;
            m_AnimatorWasPlaying = animator.playing;
            animator.playing = false;
        }

        private void ResumeAnimator()
        {
            // Unity's null check covers an animator destroyed while ragdolling.
            if (m_PausedAnimator != null)
                m_PausedAnimator.playing = m_AnimatorWasPlaying;
            m_PausedAnimator = null;
            m_AnimatorWasPlaying = false;
        }
    }
}
