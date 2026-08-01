using UnityEditor;
using UnityEngine;

namespace PowerOfFire.DrawToPlay.Editor
{
    /// <summary>
    /// Builds the three enemy archetype trees from hunter's <c>scripts/enemies/*.gd</c> —
    /// draw-tool-port-brief.md §7.2's closing promise that "the <c>enemies/*.gd</c> archetypes ship
    /// as the seed component library and preset trees". Nothing here is invented: every range,
    /// speed and duration below is a Godot export or prefab value divided by the project's 32 px =
    /// 1 world unit, and the node graphs are the <c>_brain()</c> match statements re-expressed as
    /// states.
    ///
    /// HOW A <c>_brain()</c> BECOMES A TREE. The Godot enemies are per-frame match statements over
    /// an enum with a countdown timer; the shape they all share is "decide, telegraph, strike,
    /// recover". That maps onto the runner without violence, with three translation rules:
    /// <list type="bullet">
    /// <item>ONE ENUM STATE = ONE NODE. State changes on a timer (<c>_t &lt;= 0</c>) become a
    /// <see cref="WaitTask"/> plus an unconditional completion transition, which is exactly what
    /// "when all tasks finish, take the first matching exit" means.</item>
    /// <item>A CHECK MADE EVERY FRAME = A checkWhileRunning TRANSITION. <c>see_player()</c> in
    /// IDLE and the reach test in ADVANCE are re-evaluated on every one of Godot's frames, so they
    /// port as interrupts, not as completion transitions — and running tasks are therefore torn
    /// down through OnExit(<see cref="StateTreeStatus.Cancelled"/>), which is the M6 exit
    /// criterion's Cancelled path arriving as a consequence of a faithful port rather than as a
    /// contrivance.</item>
    /// <item>TASKS IN A NODE RUN IN PARALLEL, NOT IN SEQUENCE. The runner ticks every task in the
    /// active node each frame and only takes a completion transition once ALL of them are done.
    /// So a windup is its own node (never "wait then attack" in one node), and a strike's active
    /// window is an <see cref="AttackTask"/> alongside a <see cref="WaitTask"/> holding the state
    /// open for the ported <c>slash_active</c> / <c>slam_active</c> duration.</item>
    /// </list>
    ///
    /// WHY EVERY CHASE NODE ALSO HAS AN UNCONDITIONAL LAST TRANSITION. The seed library has no
    /// negated condition (no "target lost", no NotCondition), which at first looks like a hole in
    /// these trees. It is not: <see cref="ChaseTargetTask"/> and <see cref="AttackTask"/> both
    /// report Failure the moment the target is gone or dead, a failed task is a finished task, and
    /// a finished task set is what makes the runner evaluate completion transitions. Ordering them
    /// "in range → attack, otherwise → idle" therefore expresses "…and if there is nothing left to
    /// chase, stand down" using only positive conditions. The Godot enemies do the same thing with
    /// <c>if target == null: state = IDLE</c>.
    ///
    /// STOP RANGE IS DELIBERATELY UNREACHABLE. Each chase task's <c>stopRange</c> is half the
    /// archetype's attack range, well inside the range at which the interrupt above it fires. The
    /// chase can therefore never succeed on its own — the ONLY exit from a chase node in practice
    /// is the interrupt, and the only exit from the interrupt is OnExit(Cancelled). That is what
    /// makes the criterion's Cancelled path reachable in the demo scene, every single cycle,
    /// without a scripted setup.
    ///
    /// SUB-ASSETS, and why the names look like that. Every node, task and condition is added to the
    /// tree file with <see cref="AssetDatabase.AddObjectToAsset"/>, so a preset is one file that
    /// can be dragged onto a <see cref="StateTreeRunner"/>. They are named
    /// "<c>Node 2 chase</c>" / "<c>Task chase Chase</c>" / "<c>Cond chase-&gt;windup InRange</c>"
    /// so that the Project window's alphabetical sub-asset list reads as the graph.
    ///
    /// RE-RUNNING REPLACES. <see cref="AssetDatabase.CreateAsset"/> deletes whatever is at the path
    /// first, so rebuilding a preset invalidates references to the OLD tree — a scene holding one
    /// must be rebuilt too, which is why <see cref="M6DemoVerify"/> rebuilds the zombie preset
    /// itself instead of loading it. Weapon defs are the exception: they are created only when
    /// absent, so hand-assigned fields (the archer's arrow prefab, most of all) survive.
    /// </summary>
    internal static class StateTreePresets
    {
        private const string k_PresetFolder = "Assets/DrawToPlay/Presets";

        private const string k_ZombiePath = k_PresetFolder + "/Zombie.asset";
        private const string k_BrutePath = k_PresetFolder + "/Brute.asset";
        private const string k_ArcherPath = k_PresetFolder + "/Archer.asset";

        private const string k_DaggersPath = k_PresetFolder + "/Daggers.asset";
        private const string k_LancePath = k_PresetFolder + "/Lance.asset";
        private const string k_BowPath = k_PresetFolder + "/Bow.asset";

        /// <summary>Godot pixels per world unit (m0 conventions). Every ported length below is
        /// written as its Godot number divided by this, so the source value stays legible.</summary>
        private const float k_PixelsPerUnit = 32f;

        // --- shared chassis (enemy_base.gd) ----------------------------------------------------

        /// <summary>enemy_base.gd <c>sight_range</c> 130 px. All three archetypes inherit it —
        /// brute.tscn and archer.tscn override neither it nor <c>sight_height</c>.</summary>
        private const float k_SightRange = 130f / k_PixelsPerUnit;

        /// <summary>Heartbeat for the idle nodes. NOT a Godot value: Godot's IDLE re-evaluates
        /// <c>see_player()</c> every frame, and a node with no tasks would take its completion
        /// transition instantly, so idle needs something to be busy with. Detection latency is
        /// unaffected — the interrupt is checked every tick regardless of how long the wait has
        /// left, so the pulse only decides how often idle re-enters itself.</summary>
        private const float k_IdlePulse = 0.2f;

        /// <summary>Chase stop range as a fraction of attack range. See the class comment: this is
        /// a floor the chase never reaches, not a stopping distance.</summary>
        private const float k_StopRangeFactor = 0.5f;

        // --- zombie.gd + prefabs/zombie.tscn ---------------------------------------------------

        /// <summary>zombie.gd <c>near_range</c> 22 px: slash instead of leaping.</summary>
        private const float k_ZombieNearRange = 22f / k_PixelsPerUnit;

        private const float k_ZombieSlashWindup = 0.4f;
        private const float k_ZombieSlashActive = 0.15f;
        private const float k_ZombieRecoverTime = 0.6f;

        /// <summary>enemy_base.gd <c>pursue(target, 40.0)</c> — the closing speed zombie.gd asks
        /// for. Its non-elite approach is a LEAP (<c>leap_speed_x</c> 130 px/s on a ballistic arc
        /// with the hitbox armed for the whole flight); a leap needs a mover with gravity and
        /// ground contact, which is M7+, so the ported approach is the pursuit walk the same
        /// script uses when it cannot jump.</summary>
        private const float k_ZombieChaseSpeed = 40f / k_PixelsPerUnit;

        // --- brute.gd + prefabs/brute.tscn -----------------------------------------------------

        /// <summary>brute.gd <c>reach</c> 26 px.</summary>
        private const float k_BruteReach = 26f / k_PixelsPerUnit;

        /// <summary>brute.gd <c>advance_speed</c> 18 px/s — a third of the zombie's, which is the
        /// whole of the archetype's feel.</summary>
        private const float k_BruteAdvanceSpeed = 18f / k_PixelsPerUnit;

        private const float k_BruteWindupTime = 0.8f;
        private const float k_BruteSlamActive = 0.2f;
        private const float k_BruteRecoverTime = 0.8f;

        // --- archer.gd + prefabs/archer.tscn ---------------------------------------------------

        /// <summary>archer.gd <c>band_near</c> 60 px: closer than this and it back-pedals.</summary>
        private const float k_ArcherBandNear = 60f / k_PixelsPerUnit;

        /// <summary>archer.gd <c>band_far</c> 120 px: further than this and it steps in.</summary>
        private const float k_ArcherBandFar = 120f / k_PixelsPerUnit;

        /// <summary>archer.gd ENGAGE step-in: <c>walk_speed</c> 30 px/s x 0.8.</summary>
        private const float k_ArcherStepInSpeed = 30f * 0.8f / k_PixelsPerUnit;

        private const float k_ArcherTelegraphTime = 0.8f;

        /// <summary>archer.gd <c>shot_cooldown</c>, charged AFTER the arrow leaves.</summary>
        private const float k_ArcherShotCooldown = 1.6f;

        /// <summary>Blackboard key for the archer's shot clock (the gate arms it, the shot does
        /// not — see <see cref="BuildArcher"/>).</summary>
        private const string k_ArcherShotCooldownKey = "shootCooldown";

        /// <summary>How long the archer holds position between band re-checks. Same role as
        /// <see cref="k_IdlePulse"/>: the cooldown interrupt is checked every tick regardless.</summary>
        private const float k_ArcherHoldPulse = 0.15f;

        /// <summary>archer.gd <c>_shoot</c> muzzle: <c>global_position + Vector2(6, -4)</c>,
        /// converted and Y-flipped for Unity's Y-up (WeaponDefAsset's documented rule).</summary>
        private static readonly Vector2 k_ArcherMuzzle =
            new Vector2(6f / k_PixelsPerUnit, 4f / k_PixelsPerUnit);

        [MenuItem("Tools/Draw To Play/Create Enemy Preset Trees")]
        public static void CreateEnemyPresetTrees()
        {
            StateTreeAsset zombie = BuildZombie();
            BuildBrute();
            BuildArcher();

            Selection.activeObject = zombie;
            EditorGUIUtility.PingObject(zombie);
        }

        // --- zombie ------------------------------------------------------------------------

        /// <summary>
        /// Port of <c>zombie.gd</c>'s six-state brain, collapsed to five nodes (LEAP folds into
        /// the chase — see <see cref="k_ZombieChaseSpeed"/>):
        /// <code>
        ///   idle ──(detect, interrupt)──▶ chase ──(in reach, interrupt)──▶ windup
        ///    ▲                             │                                 │
        ///    │                             └──(target lost)──▶ idle          ▼
        ///    └────────────────── recover ◀────────── slash ◀─────────────────┘
        /// </code>
        /// Both arrows marked "interrupt" are checkWhileRunning transitions cancelling a RUNNING
        /// task — the idle heartbeat and the chase respectively.
        ///
        /// The loop back through IDLE rather than straight from RECOVER to the strike is Godot's:
        /// <c>State.RECOVER</c> returns to <c>State.IDLE</c>, which re-decides between slashing and
        /// closing. Here that costs two ticks per cycle (idle re-detects, chase re-measures) and
        /// buys the same property: an enemy whose target vanished mid-recovery stands down instead
        /// of swinging at nothing.
        /// </summary>
        public static StateTreeAsset BuildZombie()
        {
            EnsurePresetFolder();
            WeaponDefAsset daggers = EnsureDaggers();

            var builder = new TreeBuilder(k_ZombiePath, "Zombie");

            StateTreeNodeAsset idle = builder.Node(1, "idle", "Idle");
            StateTreeNodeAsset chase = builder.Node(2, "chase", "Pursue");
            StateTreeNodeAsset windup = builder.Node(3, "windup", "Slash Windup");
            StateTreeNodeAsset slash = builder.Node(4, "slash", "Slash");
            StateTreeNodeAsset recover = builder.Node(5, "recover", "Recover");

            // IDLE — zombie.gd State.IDLE: stand at the spawn point until the player is seen.
            builder.AddTask<WaitTask>(idle, "Wait").seconds = k_IdlePulse;
            builder.AddInterrupt<TargetDetectedCondition>(idle, chase, "Detected")
                .detectRange = k_SightRange;
            builder.AddCompletion(idle, idle);

            // CHASE — enemy_base.pursue at 40 px/s. The interrupt below is the only way out.
            var chaseTask = builder.AddTask<ChaseTargetTask>(chase, "Chase");
            chaseTask.moveSpeed = k_ZombieChaseSpeed;
            chaseTask.stopRange = k_ZombieNearRange * k_StopRangeFactor;
            builder.AddInterrupt<TargetInRangeCondition>(chase, windup, "InReach")
                .range = k_ZombieNearRange;
            builder.AddCompletion<TargetInRangeCondition>(chase, windup, "InReachOnArrival")
                .range = k_ZombieNearRange;
            builder.AddCompletion(chase, idle);

            // WINDUP — State.SLASH_WINDUP: 0.4 s planted, facing the target, telegraphing. Godot
            // pulses the body tint here; the tint is a cue, so it ports as a cue emission.
            builder.AddTask<FaceTargetTask>(windup, "Face");
            builder.AddTask<FireCueTask>(windup, "Telegraph").cueName = "zombie_slash_telegraph";
            builder.AddTask<WaitTask>(windup, "Windup").seconds = k_ZombieSlashWindup;
            builder.AddCompletion(windup, slash);

            // SLASH — State.SLASH: strike(), then hold the state open for slash_active. The
            // AttackTask lands the hit on its first tick and the WaitTask keeps the node alive for
            // the ported active window, which is what running tasks in PARALLEL is for.
            var attack = builder.AddTask<AttackTask>(slash, "Attack");
            attack.weapon = daggers;
            attack.range = k_ZombieNearRange;
            builder.AddTask<WaitTask>(slash, "ActiveWindow").seconds = k_ZombieSlashActive;
            builder.AddCompletion(slash, recover);

            // RECOVER — State.RECOVER, then back to IDLE to re-decide.
            builder.AddTask<WaitTask>(recover, "Recover").seconds = k_ZombieRecoverTime;
            builder.AddCompletion(recover, idle);

            return builder.Finish();
        }

        // --- brute -------------------------------------------------------------------------

        /// <summary>
        /// Port of <c>brute.gd</c>. Same skeleton as the zombie with three differences that are the
        /// archetype: it advances at 18 px/s instead of 40, it winds up for twice as long (0.8 s),
        /// and it hits with the lance for 3 instead of the daggers' 1.
        ///
        /// The one structural difference: RECOVER returns to ADVANCE, not to IDLE. Godot's brute
        /// never stops advancing once it has seen you — <c>State.RECOVER</c> sets
        /// <c>state = State.ADVANCE</c> — and that relentlessness is the point of the archetype.
        /// Standing down still works, because ADVANCE's chase fails when the target is gone and the
        /// last transition catches it.
        ///
        /// Detect range is the full 130 px, not the shorter radius an archetype sketch might
        /// suggest: brute.tscn overrides neither <c>sight_range</c> nor <c>sight_height</c>, and
        /// the ground truth wins over the sketch (m0: port semantics faithfully, record deviations).
        /// The brute is differentiated by being SLOW, not by being short-sighted.
        /// </summary>
        public static StateTreeAsset BuildBrute()
        {
            EnsurePresetFolder();
            WeaponDefAsset lance = EnsureLance();

            var builder = new TreeBuilder(k_BrutePath, "Brute");

            StateTreeNodeAsset idle = builder.Node(1, "idle", "Idle");
            StateTreeNodeAsset advance = builder.Node(2, "advance", "Advance");
            StateTreeNodeAsset windup = builder.Node(3, "windup", "Slam Windup");
            StateTreeNodeAsset slam = builder.Node(4, "slam", "Slam");
            StateTreeNodeAsset recover = builder.Node(5, "recover", "Recover");

            builder.AddTask<WaitTask>(idle, "Wait").seconds = k_IdlePulse;
            builder.AddInterrupt<TargetDetectedCondition>(idle, advance, "Detected")
                .detectRange = k_SightRange;
            builder.AddCompletion(idle, idle);

            var advanceTask = builder.AddTask<ChaseTargetTask>(advance, "Advance");
            advanceTask.moveSpeed = k_BruteAdvanceSpeed;
            advanceTask.stopRange = k_BruteReach * k_StopRangeFactor;
            builder.AddInterrupt<TargetInRangeCondition>(advance, windup, "InReach")
                .range = k_BruteReach;
            builder.AddCompletion<TargetInRangeCondition>(advance, windup, "InReachOnArrival")
                .range = k_BruteReach;
            builder.AddCompletion(advance, idle);

            builder.AddTask<FaceTargetTask>(windup, "Face");
            builder.AddTask<FireCueTask>(windup, "Telegraph").cueName = "brute_slam_telegraph";
            builder.AddTask<WaitTask>(windup, "Windup").seconds = k_BruteWindupTime;
            builder.AddCompletion(windup, slam);

            var slamAttack = builder.AddTask<AttackTask>(slam, "Attack");
            slamAttack.weapon = lance;
            slamAttack.range = k_BruteReach;
            builder.AddTask<WaitTask>(slam, "ActiveWindow").seconds = k_BruteSlamActive;
            builder.AddCompletion(slam, recover);

            builder.AddTask<WaitTask>(recover, "Recover").seconds = k_BruteRecoverTime;
            builder.AddCompletion(recover, advance);

            return builder.Finish();
        }

        // --- archer ------------------------------------------------------------------------

        /// <summary>
        /// Port of <c>archer.gd</c>. Its ENGAGE state is a band controller rather than a chase, so
        /// this tree splits it in two: APPROACH closes the distance, HOLD stands in the band and
        /// waits out the shot clock.
        /// <code>
        ///   idle ──(detect)──▶ approach ──(in band, interrupt)──▶ hold ──(cooldown, interrupt)──▶ telegraph
        ///                         ▲                               │ ▲                              │
        ///                         └───────(band lost)─────────────┘ └──────── shoot ◀───────────────┘
        /// </code>
        ///
        /// THE BAND IS ONE CONDITION. <see cref="TargetInRangeCondition.minRange"/> makes
        /// "am I inside my firing band?" an annulus test — 60 px inner, 120 px outer, straight from
        /// the exports.
        ///
        /// WHAT DOES NOT PORT: the back-pedal. Godot's archer walks AWAY at <c>walk_speed</c> when
        /// the player closes inside <c>band_near</c>; the seed library has no retreat, and
        /// <see cref="ChaseTargetTask"/> explicitly treats a negative moveSpeed as "do not move", so
        /// there is no way to express it by parameterising what exists. Rather than leave the
        /// archer looping between idle and approach whenever something stands on top of it, the
        /// point-blank case takes the extra transition marked below and it simply shoots you in the
        /// face. A KeepDistanceTask (or a NotCondition, which would also give every tree a real
        /// "target lost" interrupt) is the M7 follow-up.
        ///
        /// THE SHOT CLOCK ARMS EARLY, ON PURPOSE. Godot charges <c>shot_cooldown</c> after the
        /// arrow leaves, giving 0.8 s telegraph + 1.6 s cooldown = 2.4 s between shots. Here the
        /// gate that ENTERS the telegraph arms it, so the authored number is that 2.4 s total and
        /// the shot-to-shot cadence comes out identical. Arming at the gate rather than at the shot
        /// is what keeps the rate honest when the shot fails (no arrow prefab assigned, target lost
        /// mid-draw): the archer still waits its full turn instead of retrying every frame.
        /// </summary>
        public static StateTreeAsset BuildArcher()
        {
            EnsurePresetFolder();
            WeaponDefAsset bow = EnsureBow();

            var builder = new TreeBuilder(k_ArcherPath, "Archer");

            StateTreeNodeAsset idle = builder.Node(1, "idle", "Idle");
            StateTreeNodeAsset approach = builder.Node(2, "approach", "Step In");
            StateTreeNodeAsset hold = builder.Node(3, "hold", "Hold Band");
            StateTreeNodeAsset telegraph = builder.Node(4, "telegraph", "Telegraph");
            StateTreeNodeAsset shoot = builder.Node(5, "shoot", "Loose");

            builder.AddTask<WaitTask>(idle, "Wait").seconds = k_IdlePulse;
            builder.AddInterrupt<TargetDetectedCondition>(idle, approach, "Detected")
                .detectRange = k_SightRange;
            builder.AddCompletion(idle, idle);

            // APPROACH — archer.gd ENGAGE's "dist > band_far" branch: step in at walk_speed * 0.8.
            var stepIn = builder.AddTask<ChaseTargetTask>(approach, "StepIn");
            stepIn.moveSpeed = k_ArcherStepInSpeed;
            stepIn.stopRange = k_ArcherBandNear;
            var enteredBand = builder.AddInterrupt<TargetInRangeCondition>(approach, hold, "InBand");
            enteredBand.minRange = k_ArcherBandNear;
            enteredBand.range = k_ArcherBandFar;
            var arrivedInBand =
                builder.AddCompletion<TargetInRangeCondition>(approach, hold, "InBandOnArrival");
            arrivedInBand.minRange = k_ArcherBandNear;
            arrivedInBand.range = k_ArcherBandFar;
            // The point-blank case (chase reached stopRange = band_near, so the annulus test above
            // is false): Godot would back away, this shoots. See the class comment.
            builder.AddCompletion<TargetInRangeCondition>(approach, hold, "PointBlank")
                .range = k_ArcherBandNear;
            builder.AddCompletion(approach, idle);

            // HOLD — ENGAGE's "in band" branch: velocity zeroed, facing the target, waiting on _cd.
            builder.AddTask<FaceTargetTask>(hold, "Face");
            builder.AddTask<WaitTask>(hold, "Hold").seconds = k_ArcherHoldPulse;
            var shotClock = builder.AddInterrupt<CooldownReadyCondition>(hold, telegraph, "ShotReady");
            shotClock.cooldownKey = k_ArcherShotCooldownKey;
            shotClock.cooldown = k_ArcherTelegraphTime + k_ArcherShotCooldown;
            shotClock.armOnReady = true;
            builder.AddCompletion<TargetInRangeCondition>(hold, hold, "StillInRange")
                .range = k_ArcherBandFar;
            builder.AddCompletion(hold, approach);

            // TELEGRAPH — 0.8 s with the bubble icon up. The icon is a cue.
            builder.AddTask<FaceTargetTask>(telegraph, "Face");
            builder.AddTask<FireCueTask>(telegraph, "Telegraph").cueName = "archer_aim_telegraph";
            builder.AddTask<WaitTask>(telegraph, "Draw").seconds = k_ArcherTelegraphTime;
            builder.AddCompletion(telegraph, shoot);

            // SHOOT — _shoot(). Fails (harmlessly, straight through to HOLD) until the bow def has
            // a projectile prefab; see EnsureBow.
            var loose = builder.AddTask<SpawnProjectileTask>(shoot, "Loose");
            loose.weapon = bow;
            loose.range = k_ArcherBandFar;
            loose.muzzleOffset = k_ArcherMuzzle;
            builder.AddCompletion(shoot, hold);

            return builder.Finish();
        }

        // --- weapons -----------------------------------------------------------------------

        /// <summary>
        /// <c>data/weapons/daggers.tres</c> — what zombie.tscn carries. Damage is the WeaponDef
        /// default of 1 (the .tres overrides everything except damage), which is also the number
        /// the M6 demo's arithmetic depends on.
        /// </summary>
        private static WeaponDefAsset EnsureDaggers()
        {
            return EnsureWeapon(k_DaggersPath, weapon =>
            {
                weapon.id = "daggers";
                weapon.displayName = "Twin Daggers";
                weapon.damage = 1f;
                weapon.cooldown = 0.18f;
                weapon.knockback = 40f / k_PixelsPerUnit;
                weapon.activeTime = 0.1f;
                weapon.hitTrack = new[] { new Vector2(9f / k_PixelsPerUnit, 2f / k_PixelsPerUnit) };
                weapon.hitSize = new Vector2(12f / k_PixelsPerUnit, 10f / k_PixelsPerUnit);
                weapon.comboSteps = 2;
            });
        }

        /// <summary><c>data/weapons/lance.tres</c> — brute.tscn's weapon. The two-point hit track
        /// is the near-to-far thrust.</summary>
        private static WeaponDefAsset EnsureLance()
        {
            return EnsureWeapon(k_LancePath, weapon =>
            {
                weapon.id = "lance";
                weapon.displayName = "Symmetrical Lance";
                weapon.damage = 3f;
                weapon.cooldown = 0.85f;
                weapon.knockback = 140f / k_PixelsPerUnit;
                weapon.activeTime = 0.25f;
                weapon.hitTrack = new[]
                {
                    new Vector2(8f / k_PixelsPerUnit, 2f / k_PixelsPerUnit),
                    new Vector2(26f / k_PixelsPerUnit, 2f / k_PixelsPerUnit)
                };
                weapon.hitSize = new Vector2(12f / k_PixelsPerUnit, 8f / k_PixelsPerUnit);
            });
        }

        /// <summary>
        /// <c>data/weapons/bow.tres</c> — archer.tscn's weapon, with the Godot BASE projectile
        /// numbers (220 px/s, 140 px). archer.gd scales them per shot (speed x 0.8, range x 1.2)
        /// because the same bow is also a player weapon;
        /// <see cref="SpawnProjectileTask"/> reads the def directly and has no per-shooter scale,
        /// so those two multipliers are the one archer detail this port drops. Keeping the def at
        /// the shared base values is the right side to err on — it stays correct for every other
        /// consumer.
        ///
        /// <c>projectile</c> is LEFT NULL: this port has no arrow prefab yet (Godot's
        /// <c>prefabs/arrow.tscn</c> is a Projectile script with a collision volume, which is M7
        /// gameplay work, not authoring-tool work). Assign one in the Inspector and the archer
        /// preset fires with no other change — that is why this asset is created once and never
        /// overwritten.
        /// </summary>
        private static WeaponDefAsset EnsureBow()
        {
            return EnsureWeapon(k_BowPath, weapon =>
            {
                weapon.id = "bow";
                weapon.displayName = "Bow";
                weapon.damage = 2f;
                weapon.cooldown = 0.6f;
                weapon.knockback = 60f / k_PixelsPerUnit;
                weapon.hitTrack = System.Array.Empty<Vector2>();
                weapon.projectileSpeed = 220f / k_PixelsPerUnit;
                weapon.projectileRange = 140f / k_PixelsPerUnit;
            });
        }

        /// <summary>Load the weapon def at <paramref name="path"/>, creating it from the Godot
        /// resource only when it is absent. Never overwrites: a designer's retuned damage — or the
        /// archer's hand-assigned arrow prefab — must survive a preset rebuild.
        ///
        /// The status effects the .tres files carry (bleed, shock, freeze) are NOT ported: there
        /// are no EffectDefAsset instances in this project yet, and inventing three would be
        /// authoring content rather than porting it.</summary>
        private static WeaponDefAsset EnsureWeapon(string path, System.Action<WeaponDefAsset> configure)
        {
            var existing = AssetDatabase.LoadAssetAtPath<WeaponDefAsset>(path);
            if (existing != null)
                return existing;

            var weapon = ScriptableObject.CreateInstance<WeaponDefAsset>();
            configure(weapon);
            AssetDatabase.CreateAsset(weapon, path);
            return weapon;
        }

        private static void EnsurePresetFolder()
        {
            if (!AssetDatabase.IsValidFolder(k_PresetFolder))
                AssetDatabase.CreateFolder("Assets/DrawToPlay", "Presets");
        }

        /// <summary>
        /// Assembles one StateTreeAsset and its sub-assets. Exists because the alternative —
        /// CreateInstance / name / AddObjectToAsset / add-to-list, four lines per component — buries
        /// the graph in bookkeeping, and the graph is the only part of a preset worth reading.
        ///
        /// The ORDER of transitions on a node is significant (the runner takes the first whose
        /// condition passes), and the order of these calls is that order.
        /// </summary>
        private sealed class TreeBuilder
        {
            private readonly StateTreeAsset m_Tree;
            private readonly StateTreeNodeAsset m_Root;

            public TreeBuilder(string assetPath, string treeName)
            {
                m_Tree = ScriptableObject.CreateInstance<StateTreeAsset>();
                m_Tree.name = treeName;
                m_Tree.treeName = treeName;
                m_Tree.treeKind = "enemy_ai";

                // Replaces whatever was there. The main asset must be on disk before anything can
                // be added to it, so this happens before the first sub-asset.
                AssetDatabase.CreateAsset(m_Tree, assetPath);

                // The root is organizational: no tasks, no transitions, so the runner's
                // ResolveEntryNode descends past it to children[0] — which every preset below
                // makes the idle node.
                m_Root = CreateSubAsset<StateTreeNodeAsset>("Node 0 root");
                m_Root.nodeId = "root";
                m_Root.displayName = treeName;
                m_Tree.root = m_Root;
            }

            /// <summary>A leaf state, registered under the root so the runner's id index finds it.
            /// <paramref name="order"/> only sorts the sub-asset list in the Project window;
            /// children[0] is the entry node and is decided by call order.</summary>
            public StateTreeNodeAsset Node(int order, string nodeId, string displayName)
            {
                var node = CreateSubAsset<StateTreeNodeAsset>($"Node {order} {nodeId}");
                node.nodeId = nodeId;
                node.displayName = displayName;
                m_Root.children.Add(node);
                return node;
            }

            public T AddTask<T>(StateTreeNodeAsset node, string label) where T : StateTreeTaskAsset
            {
                var task = CreateSubAsset<T>($"Task {node.nodeId} {label}");
                node.tasks.Add(task);
                return task;
            }

            /// <summary>checkWhileRunning transition: evaluated every tick BEFORE the node's tasks,
            /// so firing it cancels them (OnExit with Cancelled).</summary>
            public T AddInterrupt<T>(StateTreeNodeAsset from, StateTreeNodeAsset to, string label)
                where T : StateTreeConditionAsset
            {
                return AddTransition<T>(from, to, label, true);
            }

            /// <summary>Completion transition: evaluated only once every task in the node has
            /// finished.</summary>
            public T AddCompletion<T>(StateTreeNodeAsset from, StateTreeNodeAsset to, string label)
                where T : StateTreeConditionAsset
            {
                return AddTransition<T>(from, to, label, false);
            }

            /// <summary>Unconditional completion transition — the "otherwise" arm. Always added
            /// last on a node, because the runner stops at the first match.</summary>
            public void AddCompletion(StateTreeNodeAsset from, StateTreeNodeAsset to)
            {
                from.transitions.Add(new StateTreeTransition
                {
                    targetNodeId = to.nodeId,
                    condition = null,
                    checkWhileRunning = false
                });
            }

            public StateTreeAsset Finish()
            {
                EditorUtility.SetDirty(m_Tree);
                AssetDatabase.SaveAssets();
                return m_Tree;
            }

            private T AddTransition<T>(StateTreeNodeAsset from, StateTreeNodeAsset to, string label,
                bool checkWhileRunning) where T : StateTreeConditionAsset
            {
                var condition = CreateSubAsset<T>($"Cond {from.nodeId}->{to.nodeId} {label}");
                from.transitions.Add(new StateTreeTransition
                {
                    targetNodeId = to.nodeId,
                    condition = condition,
                    checkWhileRunning = checkWhileRunning
                });
                return condition;
            }

            private T CreateSubAsset<T>(string name) where T : ScriptableObject
            {
                var instance = ScriptableObject.CreateInstance<T>();
                instance.name = name;
                AssetDatabase.AddObjectToAsset(instance, m_Tree);
                return instance;
            }
        }
    }
}
