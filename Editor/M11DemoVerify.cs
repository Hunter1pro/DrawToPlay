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
        private const string k_SmiteCommand = "cmd:smite";
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

            var cleared = Sub<HasContextKeyCondition>(tree, "Cond watch->score Cleared");
            cleared.scope = StateTreeContextKind.Root;
            cleared.key = k_ClearedEvent;
            Wire(watch, score, cleared, true);

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

            var revive = Sub<ReviveByTagTask>(tree, "Task prep ReviveDummies");
            revive.tag = k_DummyTag;
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

            var allDown = Sub<AnyAliveWithTagCondition>(tree, "Cond fight->cleared AllDown");
            allDown.tag = k_DummyTag;
            allDown.invert = true;
            Wire(fight, clearedNode, allDown, true);

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
            tree.treeName = "Defender";
            tree.treeKind = "player";
            AssetDatabase.CreateAsset(tree, k_PlayerTreePath);

            var root = Sub<StateTreeNodeAsset>(tree, "Node 0 root");
            root.nodeId = "root";
            root.displayName = "Defender";
            tree.root = root;

            StateTreeNodeAsset idle = Node(tree, root, 1, "idle", "Listen");
            StateTreeNodeAsset smite = Node(tree, root, 2, "smite", "Smite");

            var commanded = Sub<HasContextKeyCondition>(tree, "Cond idle->smite Commanded");
            commanded.scope = StateTreeContextKind.Player;
            commanded.key = k_SmiteCommand;
            Wire(idle, smite, commanded, true);

            var consume = Sub<SetContextValueTask>(tree, "Task smite ConsumeCommand");
            consume.scope = StateTreeContextKind.Player;
            consume.key = k_SmiteCommand;
            consume.kind = SetBlackboardTask.ValueKind.Clear;
            smite.tasks.Add(consume);
            var strike = Sub<DamageByTagTask>(tree, "Task smite StrikeNearest");
            strike.tag = k_ZombieTag;
            strike.amount = 9999f;
            smite.tasks.Add(strike);
            var cooldown = Sub<WaitTask>(tree, "Task smite Cooldown");
            cooldown.seconds = 0.4f;
            smite.tasks.Add(cooldown);
            Wire(smite, idle, null, false);

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

            var playerObject = new GameObject("Player");
            playerObject.transform.SetParent(levelObject.transform);
            var playerHost = playerObject.AddComponent<StateTreeContextHost>();
            playerHost.kind = StateTreeContextKind.Player;
            playerHost.tree = playerTree;
            var smiteKey = playerObject.AddComponent<ContextKeyHotkeyBehaviour>();
#if ENABLE_INPUT_SYSTEM
            smiteKey.hotkey = UnityEngine.InputSystem.Key.Space;
#else
            smiteKey.hotkey = KeyCode.Space;
#endif
            smiteKey.scope = StateTreeContextKind.Player;
            smiteKey.blackboardKey = k_SmiteCommand;

            BuildHud("Score HUD", new Vector3(-2.6f, 2.4f, 0f), StateTreeContextKind.Root,
                "score", "SCORE ", new Color(1f, 0.9f, 0.5f));
            BuildHud("Wave HUD", new Vector3(2.6f, 2.4f, 0f), StateTreeContextKind.Level,
                "wave", "WAVE ", new Color(0.6f, 0.9f, 1f));

            BuildZombie(zombieTree, new Vector3(-2.5f, -0.5f, 0f));
            BuildDummy("Dummy A", new Vector3(0.8f, -0.5f, 0f));
            BuildDummy("Dummy B", new Vector3(1.8f, 0.4f, 0f));
            BuildDummy("Dummy C", new Vector3(2.6f, -1.1f, 0f));

            var hint = new GameObject("Hint");
            hint.transform.position = new Vector3(0f, -2.5f, 0f);
            var mesh = AddText(hint, "Z hunts the dummies; every cleared wave spawns ANOTHER "
                + "Z. SPACE smites the nearest zombie (your Player tree). Gray glyph = dead.\n"
                + "Root keeps the score, Level runs the circle, Player answers your input — "
                + "three rungs, three trees.", new Color(0.72f, 0.75f, 0.82f), 36, 0.06f);
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
            health.maxHP = 999f;
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

        /// <summary>A one-hit victim that DOES NOT fragment: death leaves the object in place,
        /// dead in the registry's eyes, which is exactly what lets ReviveByTagTask flip the
        /// wave back on without any spawning machinery.</summary>
        private static void BuildDummy(string goName, Vector3 position)
        {
            var go = new GameObject(goName);
            go.transform.position = position;
            AddText(go, "D", new Color(0.5f, 0.9f, 0.55f), 64, 0.12f);

            var health = go.AddComponent<HealthComponent>();
            health.team = CombatTeam.Player;
            health.maxHP = 1f;
            health.fragmentOnDeath = false;

            var citizen = go.AddComponent<WorldObjectBehaviour>();
            citizen.tags.Add(k_DummyTag);

            var tint = go.AddComponent<HealthGlyphTintView>();
            tint.aliveColor = new Color(0.5f, 0.9f, 0.55f);
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
