using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace PowerOfFire.DrawToPlay.Editor
{
    /// <summary>
    /// The MINIMAL GAME CIRCLE on the contexts spine — the answer to "the Root and Level Tree
    /// slots are empty; show me why they exist." Every rung runs a tree now:
    ///
    ///   ROOT tree (Game Session):   boot → watch —(evt:cleared raised)→ score(+100) → watch.
    ///     Root state OUTLIVES the circle: the score only ever grows.
    ///   LEVEL tree (Wave Circle):   prep(revive dummies, wave+1) → fight —(none left
    ///     alive)→ cleared(publish evt:cleared TO ROOT) → prep. The whole game loop is four
    ///     states; the win test is one registry condition; the "event" is one context key the
    ///     handler consumes — no event bus, no game manager class.
    ///   ZOMBIE: the unmodified M6 preset AI (StateTreePresets.BuildZombie) on a runner —
    ///     it hunts whatever the registry says is alive, knowing nothing about waves.
    ///
    /// WHY THIS SHAPE SELLS THE SYSTEM: extending it is wiring, not architecture. Another
    /// zombie = duplicate a GameObject. Kill-streak bonus = one AddContextNumber on a new
    /// transition. Game over after wave 5 = one BlackboardCompare interrupt on the Root tree.
    /// The M10 inventory in this scene = mount its tree under a Player host, done. Each of
    /// those is a few states or one component — never a new subsystem.
    ///
    /// Entities are TextMesh glyphs (Z hunts D D D): the circle is the demo, not the art —
    /// the drawn-body pipeline stays M6's showcase.
    /// </summary>
    public static class M11DemoVerify
    {
        private const string k_DemoFolder = "Assets/DrawToPlay/Demo";
        private const string k_RootTreePath = k_DemoFolder + "/M11RootTree.asset";
        private const string k_LevelTreePath = k_DemoFolder + "/M11LevelTree.asset";
        private const string k_ScenePath = k_DemoFolder + "/M11GameLoop.unity";

        private const string k_ClearedEvent = "evt:cleared";
        private const string k_DummyTag = "dummy";
        private const string k_ZombieTag = "zombie";
        private const string k_AttackCommand = "cmd:attack";
        private const string k_PlayerTreePath = k_DemoFolder + "/M11PlayerTree.asset";

        [MenuItem("Tools/Draw To Play/Verify M11 Game Loop")]
        public static void Verify()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;

            if (!AssetDatabase.IsValidFolder(k_DemoFolder))
                AssetDatabase.CreateFolder("Assets/DrawToPlay", "Demo");

            StateTreeAsset zombieTree = StateTreePresets.BuildZombie();
            StateTreeAsset rootTree = BuildRootTree();
            StateTreeAsset levelTree = BuildLevelTree();
            StateTreeAsset playerTree = BuildPlayerTree();

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            BuildScene(rootTree, levelTree, playerTree, zombieTree);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, k_ScenePath);
            AssetDatabase.SaveAssets();
            EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<StateTreeAsset>(k_LevelTreePath));
            Debug.Log("M11: game-loop trees + scene built. Play M11 and watch the circle run.");
        }

        [MenuItem("Tools/Draw To Play/Play M11 Game Loop")]
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

        // --- trees -----------------------------------------------------------------------

        /// <summary>Session orchestration: score lives on ROOT, so it survives anything the
        /// level does — including the circle resetting itself forever. The watch state is the
        /// no-event-bus pattern verbatim: the interrupt is the subscription, the key is the
        /// event, the handling state consumes it.</summary>
        private static StateTreeAsset BuildRootTree()
        {
            var tree = ScriptableObject.CreateInstance<StateTreeAsset>();
            tree.name = "M11RootTree";
            tree.treeName = "Game Session";
            tree.treeKind = "session";
            AssetDatabase.CreateAsset(tree, k_RootTreePath);

            var root = Sub<StateTreeNodeAsset>(tree, "Node 0 root");
            root.nodeId = "root";
            root.displayName = "Game Session";
            tree.root = root;

            StateTreeNodeAsset boot = Node(tree, root, 1, "boot", "Boot Session");
            StateTreeNodeAsset watch = Node(tree, root, 2, "watch", "Watch");
            StateTreeNodeAsset score = Node(tree, root, 3, "score", "Score Wave");

            var seedScore = Sub<SetContextValueTask>(tree, "Task boot SeedScore");
            seedScore.scope = StateTreeContextKind.Root;
            seedScore.key = "score";
            seedScore.kind = SetBlackboardTask.ValueKind.Float;
            seedScore.floatValue = 0f;
            boot.tasks.Add(seedScore);
            Wire(boot, watch, null, false);

            StateTreeNodeAsset gameOver = Node(tree, root, 4, "gameOver", "Game Over");

            // Interrupt ORDER is priority: a downed hero outranks a cleared wave.
            var heroDown = Sub<HasContextKeyCondition>(tree, "Cond watch->gameOver HeroDown");
            heroDown.scope = StateTreeContextKind.Root;
            heroDown.key = "evt:heroDown";
            Wire(watch, gameOver, heroDown, true);
            var cleared = Sub<HasContextKeyCondition>(tree, "Cond watch->score Cleared");
            cleared.scope = StateTreeContextKind.Root;
            cleared.key = k_ClearedEvent;
            Wire(watch, score, cleared, true);

            // gameOver: say it, hold the beat, put the hero back, say nothing again.
            var consumeDown = Sub<SetContextValueTask>(tree, "Task gameOver ConsumeEvent");
            consumeDown.scope = StateTreeContextKind.Root;
            consumeDown.key = "evt:heroDown";
            consumeDown.kind = SetBlackboardTask.ValueKind.Clear;
            gameOver.tasks.Add(consumeDown);
            var wasted = Sub<SetContextValueTask>(tree, "Task gameOver ShowStatus");
            wasted.scope = StateTreeContextKind.Root;
            wasted.key = "status";
            wasted.kind = SetBlackboardTask.ValueKind.String;
            wasted.stringValue = "WASTED — respawning...";
            gameOver.tasks.Add(wasted);
            var mourn = Sub<WaitTask>(tree, "Task gameOver Mourn");
            mourn.seconds = 2f;
            gameOver.tasks.Add(mourn);
            var respawn = Sub<ReviveByTagTask>(tree, "Task gameOver ReviveHero");
            respawn.tag = "hero";
            gameOver.tasks.Add(respawn);
            var clearStatus = Sub<SetContextValueTask>(tree, "Task gameOver ClearStatus");
            clearStatus.scope = StateTreeContextKind.Root;
            clearStatus.key = "status";
            clearStatus.kind = SetBlackboardTask.ValueKind.Clear;
            gameOver.tasks.Add(clearStatus);
            Wire(gameOver, watch, null, false);

            var consume = Sub<SetContextValueTask>(tree, "Task score ConsumeEvent");
            consume.scope = StateTreeContextKind.Root;
            consume.key = k_ClearedEvent;
            consume.kind = SetBlackboardTask.ValueKind.Clear;
            score.tasks.Add(consume);
            var addScore = Sub<AddContextNumberTask>(tree, "Task score Add100");
            addScore.scope = StateTreeContextKind.Root;
            addScore.key = "score";
            addScore.delta = 100f;
            score.tasks.Add(addScore);
            Wire(score, watch, null, false);

            EditorUtility.SetDirty(tree);
            return tree;
        }

        /// <summary>The circle itself: four states, one registry condition, one published
        /// key. The zombie is not mentioned anywhere — it acts because the world says there
        /// is something alive to hunt.</summary>
        private static StateTreeAsset BuildLevelTree()
        {
            var tree = ScriptableObject.CreateInstance<StateTreeAsset>();
            tree.name = "M11LevelTree";
            tree.treeName = "Wave Circle";
            tree.treeKind = "level";
            AssetDatabase.CreateAsset(tree, k_LevelTreePath);

            var root = Sub<StateTreeNodeAsset>(tree, "Node 0 root");
            root.nodeId = "root";
            root.displayName = "Wave Circle";
            tree.root = root;

            StateTreeNodeAsset prep = Node(tree, root, 1, "prep", "Prepare Wave");
            StateTreeNodeAsset fight = Node(tree, root, 2, "fight", "Fight");
            StateTreeNodeAsset clearedNode = Node(tree, root, 3, "cleared", "Wave Cleared");

            var revive = Sub<ReviveByTagTask>(tree, "Task prep ReviveHorde");
            revive.tag = k_ZombieTag;
            prep.tasks.Add(revive);
            var bumpWave = Sub<AddContextNumberTask>(tree, "Task prep WavePlusOne");
            bumpWave.scope = StateTreeContextKind.Level;
            bumpWave.key = "wave";
            bumpWave.delta = 1f;
            prep.tasks.Add(bumpWave);
            var breathe = Sub<WaitTask>(tree, "Task prep Breathe");
            breathe.seconds = 0.6f;
            prep.tasks.Add(breathe);
            Wire(prep, fight, null, false);

            var hordeDown = Sub<AnyAliveWithTagCondition>(tree, "Cond fight->cleared HordeDown");
            hordeDown.tag = k_ZombieTag;
            hordeDown.invert = true;
            Wire(fight, clearedNode, hordeDown, true);

            var publish = Sub<SetContextValueTask>(tree, "Task cleared PublishToRoot");
            publish.scope = StateTreeContextKind.Root;
            publish.key = k_ClearedEvent;
            publish.kind = SetBlackboardTask.ValueKind.Float;
            publish.floatValue = 1f;
            clearedNode.tasks.Add(publish);
            var escalate = Sub<SpawnClonesByTagTask>(tree, "Task cleared SpawnOneMore");
            escalate.templateTag = k_ZombieTag;
            escalate.count = 1;
            escalate.scatterRadius = 1.2f;
            clearedNode.tasks.Add(escalate);
            var savor = Sub<WaitTask>(tree, "Task cleared Savor");
            savor.seconds = 0.8f;
            clearedNode.tasks.Add(savor);
            Wire(clearedNode, prep, null, false);

            EditorUtility.SetDirty(tree);
            return tree;
        }

        /// <summary>The PLAYER'S controls as a tree — the input component only writes a
        /// context key; this tree is the handler: consume the command, smite the nearest
        /// zombie, breathe (the Wait IS the cooldown), listen again. Adding a second ability
        /// is another interrupt + state — the "controls" grow the same way everything else
        /// does.</summary>
        private static StateTreeAsset BuildPlayerTree()
        {
            var tree = ScriptableObject.CreateInstance<StateTreeAsset>();
            tree.name = "M11PlayerTree";
            tree.treeName = "Hero";
            tree.treeKind = "player";
            AssetDatabase.CreateAsset(tree, k_PlayerTreePath);

            var root = Sub<StateTreeNodeAsset>(tree, "Node 0 root");
            root.nodeId = "root";
            root.displayName = "Hero";
            tree.root = root;

            StateTreeNodeAsset alive = Node(tree, root, 1, "alive", "Alive");
            StateTreeNodeAsset strike = Node(tree, root, 2, "strike", "Strike");
            StateTreeNodeAsset down = Node(tree, root, 3, "down", "Down");

            // alive: the legs run exactly while this state does — a stun or a death takes the
            // controls away by LEAVING, which is the whole point of movement-as-a-task.
            var move = Sub<MoveOwnerByAxisTask>(tree, "Task alive Move");
            move.scope = StateTreeContextKind.Player;
            move.speed = 2.4f;
            move.clampExtents = new Vector2(4.4f, 2.6f);
            alive.tasks.Add(move);

            // Death outranks the attack command.
            var died = Sub<HealthThresholdCondition>(tree, "Cond alive->down Died");
            died.source = HealthThresholdCondition.Source.Owner;
            died.comparison = HealthThresholdCondition.Comparison.AtOrBelow;
            died.fraction = 0f;
            Wire(alive, down, died, true);
            var commanded = Sub<HasContextKeyCondition>(tree, "Cond alive->strike Commanded");
            commanded.scope = StateTreeContextKind.Player;
            commanded.key = k_AttackCommand;
            Wire(alive, strike, commanded, true);

            // strike: a melee SWING, not a smite — reach-limited, three hits fell a zombie.
            var consume = Sub<SetContextValueTask>(tree, "Task strike ConsumeCommand");
            consume.scope = StateTreeContextKind.Player;
            consume.key = k_AttackCommand;
            consume.kind = SetBlackboardTask.ValueKind.Clear;
            strike.tasks.Add(consume);
            var swing = Sub<DamageByTagTask>(tree, "Task strike Swing");
            swing.tag = k_ZombieTag;
            swing.amount = 1f;
            swing.maxRange = 1.3f;
            strike.tasks.Add(swing);
            var recover = Sub<WaitTask>(tree, "Task strike Recover");
            recover.seconds = 0.25f;
            strike.tasks.Add(recover);
            Wire(strike, alive, null, false);

            // down: report up, then lie there until the session puts the hero back.
            var report = Sub<SetContextValueTask>(tree, "Task down ReportUp");
            report.scope = StateTreeContextKind.Root;
            report.key = "evt:heroDown";
            report.kind = SetBlackboardTask.ValueKind.Float;
            report.floatValue = 1f;
            down.tasks.Add(report);
            var revived = Sub<HealthThresholdCondition>(tree, "Cond down->alive Revived");
            revived.source = HealthThresholdCondition.Source.Owner;
            revived.comparison = HealthThresholdCondition.Comparison.Above;
            revived.fraction = 0f;
            Wire(down, alive, revived, true);

            EditorUtility.SetDirty(tree);
            return tree;
        }

        // --- scene -----------------------------------------------------------------------

        private static void BuildScene(StateTreeAsset rootTree, StateTreeAsset levelTree,
            StateTreeAsset playerTree, StateTreeAsset zombieTree)
        {
            var cameraObject = new GameObject("Main Camera");
            cameraObject.tag = "MainCamera";
            var camera = cameraObject.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 3f;
            camera.transform.position = new Vector3(0f, 0f, -10f);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.12f, 0.13f, 0.16f);

            var rootObject = new GameObject("Root Context");
            var rootHost = rootObject.AddComponent<StateTreeContextHost>();
            rootHost.kind = StateTreeContextKind.Root;
            rootHost.tree = rootTree;
            rootObject.AddComponent<WorldService>();

            var levelObject = new GameObject("Level");
            levelObject.transform.SetParent(rootObject.transform);
            var levelHost = levelObject.AddComponent<StateTreeContextHost>();
            levelHost.kind = StateTreeContextKind.Level;
            levelHost.tree = levelTree;

            // The HERO IS the Player context: host, tree, body, health and input on one
            // object — moving the owner moves you, and every zombie targets you the ordinary
            // perception way (nearest living Player-team combatant).
            var heroObject = new GameObject("Hero P");
            heroObject.transform.SetParent(levelObject.transform);
            heroObject.transform.position = new Vector3(-2.5f, 0.5f, 0f);
            AddText(heroObject, "P", new Color(1f, 0.95f, 0.5f), 64, 0.12f);
            var playerHost = heroObject.AddComponent<StateTreeContextHost>();
            playerHost.kind = StateTreeContextKind.Player;
            playerHost.tree = playerTree;
            var heroHealth = heroObject.AddComponent<HealthComponent>();
            heroHealth.team = CombatTeam.Player;
            heroHealth.maxHP = 5f;
            heroHealth.fragmentOnDeath = false;
            var heroCitizen = heroObject.AddComponent<WorldObjectBehaviour>();
            heroCitizen.tags.Add("hero");
            var heroTint = heroObject.AddComponent<HealthGlyphTintView>();
            heroTint.aliveColor = new Color(1f, 0.95f, 0.5f);
            heroObject.AddComponent<AxisInputBehaviour>();
            var attackKey = heroObject.AddComponent<ContextKeyHotkeyBehaviour>();
#if ENABLE_INPUT_SYSTEM
            attackKey.hotkey = UnityEngine.InputSystem.Key.Space;
#else
            attackKey.hotkey = KeyCode.Space;
#endif
            attackKey.scope = StateTreeContextKind.Player;
            attackKey.blackboardKey = k_AttackCommand;

            BuildHud("Score HUD", new Vector3(-2.6f, 2.4f, 0f), StateTreeContextKind.Root,
                "score", "SCORE ", new Color(1f, 0.9f, 0.5f));
            BuildHud("Wave HUD", new Vector3(2.6f, 2.4f, 0f), StateTreeContextKind.Level,
                "wave", "WAVE ", new Color(0.6f, 0.9f, 1f));
            var statusHud = new GameObject("Status HUD");
            statusHud.transform.position = new Vector3(0f, 1.9f, 0f);
            AddText(statusHud, "", new Color(1f, 0.45f, 0.4f), 56, 0.1f);
            var statusView = statusHud.AddComponent<ContextKeyTextView>();
            statusView.scope = StateTreeContextKind.Root;
            statusView.key = "status";
            statusView.missingText = "";

            BuildZombie(zombieTree, new Vector3(2.6f, -0.8f, 0f));

            var hint = new GameObject("Hint");
            hint.transform.position = new Vector3(0f, -2.5f, 0f);
            var mesh = AddText(hint, "WASD/arrows: move P. SPACE: strike (short reach, 3 hits "
                + "fell a Z). Clear the horde: +100 and one MORE spawns.\nThey hunt YOU. Die "
                + "and the session mourns you back in. Three rungs, three trees, zero managers.",
                new Color(0.72f, 0.75f, 0.82f), 36, 0.06f);
            mesh.anchor = TextAnchor.MiddleCenter;

            Selection.activeObject = levelObject;
        }

        private static void BuildHud(string goName, Vector3 position, StateTreeContextKind scope,
            string key, string prefix, Color color)
        {
            var go = new GameObject(goName);
            go.transform.position = position;
            AddText(go, prefix + "0", color, 52, 0.09f);
            var view = go.AddComponent<ContextKeyTextView>();
            view.scope = scope;
            view.key = key;
            view.prefix = prefix;
        }

        /// <summary>The unmodified M6 archetype brain on a glyph body: the AI needs health (a
        /// team to be hostile against) and a runner — nothing about it knows this scene is a
        /// game loop.</summary>
        private static void BuildZombie(StateTreeAsset zombieTree, Vector3 position)
        {
            var go = new GameObject("Zombie Z");
            go.transform.position = position;
            AddText(go, "Z", new Color(0.95f, 0.4f, 0.35f), 64, 0.12f);

            var health = go.AddComponent<HealthComponent>();
            health.team = CombatTeam.Enemy;
            health.maxHP = 3f;
            health.fragmentOnDeath = false;

            var citizen = go.AddComponent<WorldObjectBehaviour>();
            citizen.tags.Add(k_ZombieTag);

            var runner = go.AddComponent<StateTreeRunner>();
            runner.data = zombieTree;
            runner.ownerObject = go;
            go.AddComponent<DisableRunnerOnDeath>();

            var tint = go.AddComponent<HealthGlyphTintView>();
            tint.aliveColor = new Color(0.95f, 0.4f, 0.35f);
        }

        private static TextMesh AddText(GameObject go, string text, Color color, int fontSize,
            float characterSize)
        {
            var mesh = go.AddComponent<TextMesh>();
            mesh.text = text;
            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            mesh.font = font;
            mesh.fontSize = fontSize;
            mesh.characterSize = characterSize;
            mesh.anchor = TextAnchor.MiddleCenter;
            mesh.color = color;
            var renderer = go.GetComponent<MeshRenderer>();
            if (font != null && renderer != null)
                renderer.sharedMaterial = font.material;
            return mesh;
        }

        // --- small builders ---------------------------------------------------------------

        private static StateTreeNodeAsset Node(StateTreeAsset tree, StateTreeNodeAsset root,
            int order, string nodeId, string displayName)
        {
            var node = Sub<StateTreeNodeAsset>(tree, $"Node {order} {nodeId}");
            node.nodeId = nodeId;
            node.displayName = displayName;
            root.children.Add(node);
            return node;
        }

        private static void Wire(StateTreeNodeAsset from, StateTreeNodeAsset to,
            StateTreeConditionAsset condition, bool interrupt)
        {
            from.transitions.Add(new StateTreeTransition
            {
                targetNodeId = to.nodeId,
                condition = condition,
                checkWhileRunning = interrupt
            });
        }

        private static T Sub<T>(StateTreeAsset tree, string name) where T : ScriptableObject
        {
            var instance = ScriptableObject.CreateInstance<T>();
            instance.name = name;
            AssetDatabase.AddObjectToAsset(instance, tree);
            return instance;
        }
    }
}
