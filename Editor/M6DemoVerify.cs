using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace PowerOfFire.DrawToPlay.Editor
{
    /// <summary>
    /// M6 exit-criterion smoke pass, half one: "the zombie preset tree chases and attacks a target
    /// in a test scene". (Half two — an interrupt firing OnExit(Cancelled) — is the EditMode test
    /// suite's job, because a cancellation is a fact about a callback, not about pixels. This scene
    /// still EXERCISES that path every cycle: see "What you are looking at" below.)
    ///
    /// The scene is three objects:
    ///   • DrawnGround — a flat drawn slab with chain collision (the M1 TerrainBlob path). Scenery
    ///     for the zombie, which moves its Transform and has no body, but real ground for the
    ///     debris at the end.
    ///   • TrainingDummy — the "Player": a drawn post with a <see cref="HealthComponent"/> on team
    ///     <see cref="CombatTeam.Player"/>. STATIC in the gameplay sense (no input, no brain); it is
    ///     a dynamic body only so that its remains can fall.
    ///   • Zombie — a drawn capsule with a <see cref="HealthComponent"/> on team
    ///     <see cref="CombatTeam.Enemy"/> and a <see cref="StateTreeRunner"/> pointed at
    ///     Presets/Zombie.asset, which this command (re)builds through
    ///     <see cref="StateTreePresets.BuildZombie"/>.
    ///
    /// WHAT YOU ARE LOOKING AT (how to observe the criterion — there is no HUD and no logging, by
    /// design: <c>StateTreeRunner.context</c> only exists after StartTree, so a scene-time listener
    /// would have nothing to subscribe to, and no runtime component in this milestone may be
    /// created just for a demo):
    ///
    ///   1. CHASE — the zombie walks right from the first frame. The dummy starts 3.5 wu away,
    ///      inside the ported 4.0625 wu sight range, so TargetDetectedCondition fires the idle
    ///      node's checkWhileRunning transition on tick one — before the idle WaitTask has ticked
    ///      even once, and that WaitTask therefore ends the only way it can: OnExit(Cancelled).
    ///      Interrupt number one, in the first frame, every run.
    ///   2. STOP — after ~2.2 s it halts a third of a unit short of the dummy. That is interrupt
    ///      number two, chase→windup at attack range, cancelling a RUNNING ChaseTargetTask. The
    ///      chase task's own stopRange is deliberately half the attack range, so the task can
    ///      never finish on its own and the Cancelled path is the ONLY way out of the chase node.
    ///      (This is why "it stopped" is meaningful and not just "it arrived".)
    ///   3. ATTACK — a 0.4 s pause (the ported slash windup), then the hit lands. Damage is not
    ///      drawn anywhere, so watch for the consequence instead:
    ///   4. PROOF — three hits at 1 damage each, about 1.15 s apart, empty the dummy's 3 HP and it
    ///      BURSTS into physical debris that falls onto the drawn ground. That break is the M5
    ///      <see cref="HealthComponent"/> → <see cref="DestructibleShape"/> death seam, and nothing
    ///      else in this scene can trigger it: the prop's impact-fragment threshold is switched
    ///      off. Debris on the ground therefore means, and can only mean, that AttackTask dealt
    ///      damage. It happens about five seconds in.
    ///   5. SURVIVAL — with the dummy gone the tree does not fault: the target goes invalid, chase
    ///      reports Failure and the terminal transition returns the zombie to idle, where it waits
    ///      forever. Leave it running; nothing should ever log.
    ///
    /// EVERYTHING IS AT GODOT SCALE. The ported constants (sight 130 px, near_range 22 px, elite
    /// pursuit 40 px/s) are only meaningful against bodies the size hunter's are, so the drawn
    /// entities are the collision shapes from <c>prefabs/zombie.tscn</c> converted at the project's
    /// 32 px = 1 wu — a zombie 0.28 x 0.56 wu. The camera is pulled in to match rather than the
    /// world being scaled up, so every number in the preset asset still means what it meant in
    /// Godot.
    /// </summary>
    internal static class M6DemoVerify
    {
        private const string k_DemoFolder = "Assets/DrawToPlay/Demo";
        private const string k_ScenePath = k_DemoFolder + "/M6AIDemo.unity";
        private const string k_GroundAssetPath = k_DemoFolder + "/M6DemoGround.asset";
        private const string k_DummyAssetPath = k_DemoFolder + "/M6DemoDummy.asset";
        private const string k_ZombieAssetPath = k_DemoFolder + "/M6DemoZombie.asset";

        private const string k_GroundName = "DrawnGround";
        private const string k_DummyName = "TrainingDummy";
        private const string k_ZombieName = "Zombie";

        // --- units ---------------------------------------------------------------------------

        /// <summary>Godot pixels per world unit (m0 conventions). Every dimension below is a
        /// hunter measurement divided by this, so the scene and the preset asset agree.</summary>
        private const float k_PixelsPerUnit = 32f;

        // --- ground --------------------------------------------------------------------------

        /// <summary>Flat, and wide enough that the whole chase plus the debris scatter happens over
        /// it — a bowl would roll the remains out of frame and add a slope the transform-driven
        /// zombie cannot feel anyway.</summary>
        private const float k_GroundHalfWidth = 5f;

        private const float k_GroundThickness = 0.6f;
        private const float k_GroundStep = 0.5f;

        // --- entities ------------------------------------------------------------------------

        /// <summary>zombie.tscn CapsuleShape2D radius 4.5 px. The drawn capsule uses it for both
        /// the cap radius and the half-length, which reproduces the 18 px total height of the
        /// Godot shape (4 x radius) and the 9 px width.</summary>
        private const float k_ZombieRadius = 4.5f / k_PixelsPerUnit;

        /// <summary>Half-width of the dummy post: 10 px wide, the player's rough footprint.</summary>
        private const float k_DummyHalfWidth = 5f / k_PixelsPerUnit;

        /// <summary>Half-height of the dummy post: 22 px tall.</summary>
        private const float k_DummyHalfHeight = 11f / k_PixelsPerUnit;

        /// <summary>Corner rounding on the dummy's top. The BOTTOM stays square: the post is a
        /// dynamic body resting on flat ground, and a flat base is what keeps it standing instead
        /// of rocking for the first second of the demo.</summary>
        private const float k_DummyCornerRadius = 3f / k_PixelsPerUnit;

        private const int k_CapSegments = 10;

        /// <summary>Curve-fit tolerance for the entities. An eighth of the zombie's radius: 0.08
        /// (the tolerance every earlier demo uses on metre-scale props) would collapse a 0.14 wu
        /// capsule into a triangle.</summary>
        private const float k_EntityFitTolerance = 0.012f;

        /// <summary>Bake interval for the entities. The 0.125 default is most of a zombie —
        /// baking at that spacing would leave the outline with about six points.</summary>
        private const float k_EntityBakeInterval = 0.02f;

        private const float k_EntityOutlineWidth = 0.02f;
        private const float k_EntityRimWidth = 0.015f;

        // --- placement -----------------------------------------------------------------------

        /// <summary>Ground surface height. Entities are placed with their feet on it.</summary>
        private const float k_GroundTop = 0f;

        /// <summary>Start distance between the two, world units: inside the zombie's ported
        /// 130 px = 4.0625 wu sight range and well outside its 22 px = 0.6875 wu reach, so both
        /// interrupts have to fire in order for anything at all to happen.</summary>
        private const float k_StartSeparation = 3.5f;

        private const float k_DummyX = 0.6f;

        /// <summary>Player HP is 3 here — one zombie cycle (windup, slash, recover) is about
        /// 1.15 s and the daggers deal 1, so the dummy breaks roughly 4 s after the zombie arrives.
        /// Long enough to read as a fight, short enough that nobody has to wait for the payoff.</summary>
        private const float k_DummyMaxHP = 3f;

        /// <summary>zombie.tscn Health.max_hp. Nothing damages the zombie in this scene; it is set
        /// because <see cref="TargetDetectedCondition"/> selects by TEAM, which lives on the health
        /// pool — an enemy with no HealthComponent could not be told apart from the scenery, and
        /// (worse) would happily target itself.</summary>
        private const float k_ZombieMaxHP = 3f;

        // --- destruction tuning ----------------------------------------------------------------

        /// <summary>Fragment-point scatter radius. The 0.4 default is more than twice the dummy's
        /// width: every fragment point would land outside the shape and the "break" would produce
        /// one single piece. Sized to the post instead.</summary>
        private const float k_DummyShardScatter = 0.12f;

        private const int k_DummyShardCount = 5;

        /// <summary>Outward impulse per shard. A fragment of this prop masses about 0.04 (area x
        /// unit density), so the 1.5 default would launch the debris off-screen at ~40 wu/s. This
        /// gives it about 0.8 wu/s: a pop, not an explosion.</summary>
        private const float k_DummyShardImpulse = 0.03f;

        [MenuItem("Tools/Draw To Play/Verify M6 State Trees")]
        public static void Verify()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;

            EnsureDemoFolder();

            // Build the preset first: the scene stores a REFERENCE to the tree asset, and
            // AssetDatabase.CreateAsset replaces whatever was at the path, so a preset rebuilt
            // after the scene was saved would leave the runner pointing at a deleted object.
            StateTreeAsset zombieTree = StateTreePresets.BuildZombie();

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            BuildWorldRoot();
            BuildCamera();
            BuildGround();
            BuildDummy();
            GameObject zombie = BuildZombie(zombieTree);

            Selection.activeGameObject = zombie;

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, k_ScenePath);
            AssetDatabase.SaveAssets();
            EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<SceneAsset>(k_ScenePath));
        }

        /// <summary>Open the demo scene and press Play. No start-scene redirection: the
        /// StartupScene hijack (and the counter-bypass every demo menu carried) is removed
        /// (user decision, 2026-08-05) — Play plays the open scene, always.</summary>
        [MenuItem("Tools/Draw To Play/Play M6 Demo Scene")]
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

            EditorApplication.EnterPlaymode();
        }


        private static void EnsureDemoFolder()
        {
            if (!AssetDatabase.IsValidFolder(k_DemoFolder))
                AssetDatabase.CreateFolder("Assets/DrawToPlay", "Demo");
        }

        /// <summary>Framed on the walk, not on the world: at Godot scale the whole fight happens
        /// inside four units. Orthographic size 1.8 gives a 6.4 x 3.6 unit view at 16:9, which holds
        /// the zombie's start, the dummy, and enough headroom above the ground for the debris.</summary>
        /// <summary>The M9 spine's minimum: a Root context host carrying the WorldService.
        /// Perception reads the world registry now — every HealthComponent enrolls itself as a
        /// combatant on enable, and TargetDetectedCondition asks THIS service for candidates,
        /// so a scene without it has blind zombies (and one warning saying exactly that).</summary>
        private static void BuildWorldRoot()
        {
            var rootObject = new GameObject("Root Context");
            var host = rootObject.AddComponent<StateTreeContextHost>();
            host.kind = StateTreeContextKind.Root;
            rootObject.AddComponent<WorldService>();
        }

        private static void BuildCamera()
        {
            var cameraObject = new GameObject("Main Camera");
            cameraObject.tag = "MainCamera";
            var camera = cameraObject.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 1.8f;
            camera.transform.position = new Vector3(k_DummyX - k_StartSeparation * 0.5f, 0.5f, -10f);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.13f, 0.14f, 0.17f);
        }

        // --- ground ------------------------------------------------------------------------

        /// <summary>The M1 terrain path with a flat profile: fitted curve → DrawnShapeRenderer →
        /// TerrainBlob chain collision. The zombie never touches it (transform mover, no body), so
        /// its only physical job is catching the debris — which is exactly the job that makes the
        /// debris legible as debris rather than as a puff of things drifting away.</summary>
        private static TerrainBlob BuildGround()
        {
            var groundObject = new GameObject(k_GroundName);
            var renderer = groundObject.AddComponent<DrawnShapeRenderer>();

            var asset = ScriptableObject.CreateInstance<DrawnShapeAsset>();
            asset.name = "M6DemoGround";
            asset.fillColor = new Color(0.24f, 0.36f, 0.25f);
            asset.outlineColor = new Color(0.05f, 0.08f, 0.05f);
            asset.rimColor = new Color(0.45f, 0.62f, 0.4f);
            asset.rimWidth = 0.06f;
            // Default bake interval: the slab is ten units wide, and baking it at the entities'
            // 0.02 would spend a thousand vertices on a straight line.
            asset.curve = DrawKit.FitCurve(BuildGroundStroke(), closed: true, tolerance: 0.08f,
                smoothPasses: 1);
            AssetDatabase.CreateAsset(asset, k_GroundAssetPath);

            renderer.asset = asset;
            renderer.Regenerate();

            // Added last: TerrainBlob derives its collision from the renderer, and in play mode it
            // does so in OnEnable — the drawing has to be there first.
            return groundObject.AddComponent<TerrainBlob>();
        }

        /// <summary>Closed outline of a flat slab, local space, world units. Traversed top-left →
        /// top-right → bottom-right → bottom-left, the same winding order the M1 bowl uses.</summary>
        private static List<Vector2> BuildGroundStroke()
        {
            var points = new List<Vector2>();
            for (float x = -k_GroundHalfWidth; x <= k_GroundHalfWidth; x += k_GroundStep)
                points.Add(new Vector2(x, k_GroundTop));
            points.Add(new Vector2(k_GroundHalfWidth, k_GroundTop - k_GroundThickness));
            points.Add(new Vector2(-k_GroundHalfWidth, k_GroundTop - k_GroundThickness));
            return points;
        }

        // --- dummy -------------------------------------------------------------------------

        /// <summary>The target. Team Player, so <see cref="TargetDetectedCondition"/> (nearest
        /// living health pool of a DIFFERENT team) picks it and nothing else in the scene.
        ///
        /// It carries a <see cref="DestructibleShape"/> purely as an output device: this scene has
        /// no HUD, and <c>HealthComponent.hp</c> is a runtime property rather than a serialized
        /// field, so it does not even show in the Inspector. Death, however, is loud —
        /// <c>fragmentOnDeath</c> routes the M5 seam straight into Fragment(). The impact trigger
        /// is switched OFF (<c>impactSpeedThreshold = 0</c>) so that landing on the ground can
        /// never break it: with impacts disabled, debris means damage, full stop.</summary>
        private static GameObject BuildDummy()
        {
            var dummyObject = new GameObject(k_DummyName);
            dummyObject.transform.position = new Vector3(k_DummyX,
                k_GroundTop + k_DummyHalfHeight, 0f);
            var renderer = dummyObject.AddComponent<DrawnShapeRenderer>();

            var asset = ScriptableObject.CreateInstance<DrawnShapeAsset>();
            asset.name = "M6DemoDummy";
            asset.fillColor = new Color(0.78f, 0.72f, 0.52f);
            asset.outlineColor = new Color(0.16f, 0.13f, 0.08f);
            asset.outlineWidth = k_EntityOutlineWidth;
            asset.rimColor = new Color(0.95f, 0.9f, 0.72f);
            asset.rimWidth = k_EntityRimWidth;
            asset.fillShade = 0.25f;
            asset.bakeInterval = k_EntityBakeInterval;
            asset.curve = DrawKit.FitCurve(BuildDummyStroke(), closed: true,
                tolerance: k_EntityFitTolerance, smoothPasses: 1);
            AssetDatabase.CreateAsset(asset, k_DummyAssetPath);

            renderer.asset = asset;
            renderer.Regenerate();

            var destructible = dummyObject.AddComponent<DestructibleShape>();
            destructible.impactSpeedThreshold = 0f;
            destructible.shardCount = k_DummyShardCount;
            destructible.shardScatterRadius = k_DummyShardScatter;
            destructible.shardImpulse = k_DummyShardImpulse;

            var health = dummyObject.AddComponent<HealthComponent>();
            health.maxHP = k_DummyMaxHP;
            health.team = CombatTeam.Player;
            // health.gd hit_grace, left at the component default (0.25 s). The zombie's cycle is
            // over a second long, so i-frames never swallow a hit here — but leaving the guard on
            // is what proves the attack is going through the real damage path.
            health.fragmentOnDeath = true;
            health.destructible = destructible;

            return dummyObject;
        }

        /// <summary>Closed outline of a post with a rounded top and a SQUARE base, wound positive
        /// (CCW), local space. Order: bottom-left → bottom-right → up the right side → over the top
        /// → down the left side.</summary>
        private static List<Vector2> BuildDummyStroke()
        {
            var points = new List<Vector2>();
            float shoulder = k_DummyHalfHeight - k_DummyCornerRadius;
            float side = k_DummyHalfWidth - k_DummyCornerRadius;

            points.Add(new Vector2(-k_DummyHalfWidth, -k_DummyHalfHeight));
            points.Add(new Vector2(k_DummyHalfWidth, -k_DummyHalfHeight));
            points.Add(new Vector2(k_DummyHalfWidth, shoulder));

            // Top cap: right shoulder over to the left shoulder, 0° → 180°.
            for (int i = 0; i <= k_CapSegments; i++)
            {
                float angle = Mathf.PI * (i / (float)k_CapSegments);
                points.Add(new Vector2(side + Mathf.Cos(angle) * k_DummyCornerRadius,
                    shoulder + Mathf.Sin(angle) * k_DummyCornerRadius));
            }

            points.Add(new Vector2(-k_DummyHalfWidth, -k_DummyHalfHeight + k_DummyCornerRadius));
            return points;
        }

        // --- zombie ------------------------------------------------------------------------

        /// <summary>The brain-carrying entity: drawn capsule, health pool (team Enemy) and the
        /// runner. NO body — <see cref="ChaseTargetTask"/> moves the Transform, and a dynamic body
        /// syncing the Transform back every step would fight it. That deviation is the documented
        /// M6 shape ("a mover component is M7+ polish"), and it is why the zombie floats level
        /// instead of falling onto the slab.</summary>
        private static GameObject BuildZombie(StateTreeAsset tree)
        {
            var zombieObject = new GameObject(k_ZombieName);
            zombieObject.transform.position = new Vector3(k_DummyX - k_StartSeparation,
                k_GroundTop + k_ZombieRadius * 2f, 0f);
            var renderer = zombieObject.AddComponent<DrawnShapeRenderer>();

            var asset = ScriptableObject.CreateInstance<DrawnShapeAsset>();
            asset.name = "M6DemoZombie";
            // zombie.gd COL_BODY.
            asset.fillColor = new Color(0.55f, 0.72f, 0.5f);
            asset.outlineColor = new Color(0.08f, 0.14f, 0.07f);
            asset.outlineWidth = k_EntityOutlineWidth;
            asset.rimColor = new Color(0.76f, 0.92f, 0.7f);
            asset.rimWidth = k_EntityRimWidth;
            asset.fillShade = 0.3f;
            asset.bakeInterval = k_EntityBakeInterval;
            asset.curve = DrawKit.FitCurve(BuildZombieStroke(), closed: true,
                tolerance: k_EntityFitTolerance, smoothPasses: 1);
            AssetDatabase.CreateAsset(asset, k_ZombieAssetPath);

            renderer.asset = asset;
            renderer.Regenerate();

            var health = zombieObject.AddComponent<HealthComponent>();
            health.maxHP = k_ZombieMaxHP;
            health.team = CombatTeam.Enemy;
            // No DestructibleShape on this one, so the seam would no-op anyway; off for clarity.
            health.fragmentOnDeath = false;

            var runner = zombieObject.AddComponent<StateTreeRunner>();
            runner.data = tree;
            // Explicit rather than relying on the owner_path default (parent, or self at the
            // hierarchy root): this object HAS no parent today, but a future scene that groups the
            // demo under an empty would silently retarget every task at the group.
            runner.ownerObject = zombieObject;
            runner.autoStart = true;

            return zombieObject;
        }

        /// <summary>Closed outline of the vertical capsule from zombie.tscn (radius 4.5 px, total
        /// height 18 px), wound positive (CCW), local space: right side up, top cap, left side
        /// down, bottom cap.</summary>
        private static List<Vector2> BuildZombieStroke()
        {
            var points = new List<Vector2>((k_CapSegments + 1) * 2);

            // Top cap, 0° → 180° about (0, +radius).
            for (int i = 0; i <= k_CapSegments; i++)
            {
                float angle = Mathf.PI * (i / (float)k_CapSegments);
                points.Add(new Vector2(Mathf.Cos(angle) * k_ZombieRadius,
                    k_ZombieRadius + Mathf.Sin(angle) * k_ZombieRadius));
            }

            // Bottom cap, 180° → 360° about (0, -radius).
            for (int i = 0; i <= k_CapSegments; i++)
            {
                float angle = Mathf.PI * (1f + i / (float)k_CapSegments);
                points.Add(new Vector2(Mathf.Cos(angle) * k_ZombieRadius,
                    -k_ZombieRadius + Mathf.Sin(angle) * k_ZombieRadius));
            }

            return points;
        }
    }
}
