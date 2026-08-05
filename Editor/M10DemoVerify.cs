using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace PowerOfFire.DrawToPlay.Editor
{
    /// <summary>
    /// Builds the M10 acceptance surface as REAL assets and a REAL scene — the notebook's
    /// Inventory flow (brief §3.5) you can open, read in the State Tree window, and play:
    ///
    ///   Verify M10 → writes <c>M10ItemRegistry.asset</c> (sword/potion/feather with the item
    ///   defs as sub-assets), <c>M10InventoryUITree.asset</c> (the whole flow as one preset —
    ///   boot seeds the inventory with the Inventory Add atoms, closed parks, inventory binds
    ///   and shows, itemFlow branches on Item Kind, equip publishes through a ⚑ binding,
    ///   tooltip shows the clicked item), and <c>M10UIDemo.unity</c> (Root+World → Level →
    ///   Player+UIService spine, two TextMesh screens, the runner, a hint).
    ///
    ///   Play M10 → press I to open, click rows: the weapon equips (watch "equipped" appear on
    ///   the Player host's blackboard), the potion opens the tooltip, close returns. Keep the
    ///   State Tree window open on the tree asset while playing — the active-state highlight
    ///   IS the UI state, which is the whole thesis.
    ///
    /// The preset pattern is <see cref="StateTreePresets"/>' (one file, sub-assets named so the
    /// Project list reads as the graph); re-running REPLACES both assets, so the scene is
    /// rebuilt in the same pass and always references the fresh tree.
    /// </summary>
    public static class M10DemoVerify
    {
        private const string k_DemoFolder = "Assets/DrawToPlay/Demo";
        private const string k_RegistryPath = k_DemoFolder + "/M10ItemRegistry.asset";
        private const string k_TreePath = k_DemoFolder + "/M10InventoryUITree.asset";
        private const string k_ScenePath = k_DemoFolder + "/M10UIDemo.unity";
        private const string k_SandboxScenePath = "Assets/Sandbox.unity";

        private const string k_OpenKey = "ui:openInventory";
        private const string k_ClickedKey = "clickedItem";

        [MenuItem("Tools/Draw To Play/Verify M10 UI Tree")]
        public static void Verify()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;

            if (!AssetDatabase.IsValidFolder(k_DemoFolder))
                AssetDatabase.CreateFolder("Assets/DrawToPlay", "Demo");

            ItemRegistryAsset registry = BuildRegistry();
            StateTreeAsset tree = BuildTree(registry);

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            BuildScene(tree, registry);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, k_ScenePath);
            AssetDatabase.SaveAssets();
            EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<StateTreeAsset>(k_TreePath));
            Debug.Log("M10: registry + UI tree + scene built. Play M10, press I, click rows.");
        }

        private const string k_PlayFlag = "DrawToPlay.M10.PlayRequested";

        [MenuItem("Tools/Draw To Play/Play M10 UI Demo")]
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

            SessionState.SetBool(k_PlayFlag, true);
            EditorApplication.EnterPlaymode();
        }

        /// <summary>
        /// The startup-scene bypass, made DOMAIN-RELOAD-PROOF — the M1/M6 shape subscribes at
        /// click time, and a click-time subscription does not survive the reload entering play
        /// mode, so its restore half never ran: <c>playModeStartScene</c> stayed pointed at the
        /// demo and every LATER manual Play, from any scene, silently loaded it. This shape
        /// survives everything: the handler is (re)registered on every load, the one-shot
        /// intent lives in <see cref="SessionState"/>, and the restore ALSO runs as a load-time
        /// heal — an editor that got stuck the old way unsticks itself on the next compile.
        /// </summary>
        [InitializeOnLoadMethod]
        private static void InstallPlayHook()
        {
            EditorApplication.playModeStateChanged += RouteStartScene;
            HealStartScene();
        }

        private static void RouteStartScene(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingEditMode)
            {
                // One shot: the flag is consumed the moment it is honored, so only the play the
                // menu itself requested is redirected — never a later manual Play.
                if (SessionState.GetBool(k_PlayFlag, false))
                {
                    SessionState.SetBool(k_PlayFlag, false);
                    EditorSceneManager.playModeStartScene =
                        AssetDatabase.LoadAssetAtPath<SceneAsset>(k_ScenePath);
                }
            }
            else if (state == PlayModeStateChange.EnteredEditMode)
            {
                HealStartScene();
            }
        }

        /// <summary>Put the project's startup-scene convention back whenever the start scene is
        /// still pointing at OUR demo — after our own play ended, and on load for editors the
        /// pre-fix leak left stuck.</summary>
        private static void HealStartScene()
        {
            SceneAsset current = EditorSceneManager.playModeStartScene;
            if (current == null || AssetDatabase.GetAssetPath(current) != k_ScenePath)
                return;
            EditorSceneManager.playModeStartScene =
                AssetDatabase.LoadAssetAtPath<SceneAsset>(k_SandboxScenePath);
        }

        // --- assets ----------------------------------------------------------------------

        private static ItemRegistryAsset BuildRegistry()
        {
            var registry = ScriptableObject.CreateInstance<ItemRegistryAsset>();
            registry.name = "M10ItemRegistry";
            AssetDatabase.CreateAsset(registry, k_RegistryPath);

            registry.items.Add(Def(registry, "sword", "Iron Sword", ItemKind.Weapon));
            registry.items.Add(Def(registry, "potion", "Health Potion", ItemKind.Consumable));
            registry.items.Add(Def(registry, "feather", "Lucky Feather", ItemKind.Trinket));

            EditorUtility.SetDirty(registry);
            return registry;
        }

        private static ItemDefAsset Def(ItemRegistryAsset registry, string id, string label,
            ItemKind kind)
        {
            var def = ScriptableObject.CreateInstance<ItemDefAsset>();
            def.name = "Item " + id;
            def.id = id;
            def.displayName = label;
            def.kind = kind;
            AssetDatabase.AddObjectToAsset(def, registry);
            return def;
        }

        /// <summary>The §3.5 flow as one preset file. Transition ORDER on each node is the
        /// runner's evaluation order; the calls below are that order.</summary>
        private static StateTreeAsset BuildTree(ItemRegistryAsset registry)
        {
            var tree = ScriptableObject.CreateInstance<StateTreeAsset>();
            tree.name = "M10InventoryUITree";
            tree.treeName = "Inventory UI";
            tree.treeKind = "ui";
            AssetDatabase.CreateAsset(tree, k_TreePath);

            var root = Sub<StateTreeNodeAsset>(tree, "Node 0 root");
            root.nodeId = "root";
            root.displayName = "Inventory UI";
            tree.root = root;

            StateTreeNodeAsset boot = Node(tree, root, 1, "boot", "Seed Inventory");
            StateTreeNodeAsset closed = Node(tree, root, 2, "closed", "Closed");
            StateTreeNodeAsset inventory = Node(tree, root, 3, "inventory", "Inventory Screen");
            StateTreeNodeAsset itemFlow = Node(tree, root, 4, "itemFlow", "Item Flow");
            StateTreeNodeAsset equip = Node(tree, root, 5, "equip", "Equip");
            StateTreeNodeAsset tooltip = Node(tree, root, 6, "tooltip", "Tooltip Screen");

            // boot: give the player something to look at, with the atoms themselves.
            var addSword = Sub<InventoryAddTask>(tree, "Task boot AddSword");
            addSword.itemId = "sword";
            addSword.count = 1;
            boot.tasks.Add(addSword);
            var addPotion = Sub<InventoryAddTask>(tree, "Task boot AddPotion");
            addPotion.itemId = "potion";
            addPotion.count = 2;
            boot.tasks.Add(addPotion);
            Wire(boot, closed, null, false);

            // closed: parked, no tasks; the trigger interrupts in.
            var opened = Sub<HasContextKeyCondition>(tree, "Cond closed->inventory Opened");
            opened.scope = StateTreeContextKind.Player;
            opened.key = k_OpenKey;
            Wire(closed, inventory, opened, true);

            // inventory: consume the trigger (clearing it in `closed` would lose the
            // interrupt-before-ticks race forever), bind, hold the screen open.
            var consume = Sub<SetContextValueTask>(tree, "Task inventory ConsumeTrigger");
            consume.scope = StateTreeContextKind.Player;
            consume.key = k_OpenKey;
            consume.kind = SetBlackboardTask.ValueKind.Clear;
            inventory.tasks.Add(consume);
            var bind = Sub<BindInventoryListTask>(tree, "Task inventory BindList");
            bind.screenId = "inv";
            bind.registry = registry;
            inventory.tasks.Add(bind);
            var showInv = Sub<ShowScreenTask>(tree, "Task inventory ShowScreen");
            showInv.screenId = "inv";
            inventory.tasks.Add(showInv);
            var clicked = Sub<HasBlackboardKeyCondition>(tree, "Cond inventory->itemFlow Clicked");
            clicked.key = k_ClickedKey;
            Wire(inventory, itemFlow, clicked, false);
            Wire(inventory, closed, null, false);

            // itemFlow: one beat whose only job is the branch.
            var mark = Sub<SetBlackboardTask>(tree, "Task itemFlow Mark");
            mark.key = "ui:flow";
            mark.kind = SetBlackboardTask.ValueKind.Float;
            mark.floatValue = 1f;
            itemFlow.tasks.Add(mark);
            var isWeapon = Sub<ItemKindCondition>(tree, "Cond itemFlow->equip IsWeapon");
            isWeapon.registry = registry;
            isWeapon.kind = ItemKind.Weapon;
            isWeapon.itemIdKey = k_ClickedKey;
            Wire(itemFlow, equip, isWeapon, false);
            Wire(itemFlow, tooltip, null, false);

            // equip: publish the clicked weapon on the Player scope — stringValue arrives
            // through the ⚑ entry binding, exactly as the inspector would wire it.
            var publish = Sub<SetContextValueTask>(tree, "Task equip PublishEquipped");
            publish.scope = StateTreeContextKind.Player;
            publish.key = "equipped";
            publish.kind = SetBlackboardTask.ValueKind.String;
            equip.tasks.Add(publish);
            equip.bindings.Add(new StateTreeFieldBinding
            {
                targetKind = StateTreeFieldBinding.TargetKind.Task,
                targetIndex = 0,
                fieldName = "stringValue",
                sourceKind = StateTreeFieldBinding.SourceKind.BlackboardKey,
                blackboardKey = k_ClickedKey
            });
            Wire(equip, inventory, null, false);

            // tooltip: content from the click (same ⚑ delivery), then hold the screen open.
            var detail = Sub<BindItemDetailTask>(tree, "Task tooltip BindDetail");
            detail.screenId = "tooltip";
            detail.registry = registry;
            tooltip.tasks.Add(detail);
            var showTip = Sub<ShowScreenTask>(tree, "Task tooltip ShowScreen");
            showTip.screenId = "tooltip";
            showTip.resultKey = "tooltipClicked";
            tooltip.tasks.Add(showTip);
            tooltip.bindings.Add(new StateTreeFieldBinding
            {
                targetKind = StateTreeFieldBinding.TargetKind.Task,
                targetIndex = 0,
                fieldName = "itemId",
                sourceKind = StateTreeFieldBinding.SourceKind.BlackboardKey,
                blackboardKey = k_ClickedKey
            });
            Wire(tooltip, inventory, null, false);

            EditorUtility.SetDirty(tree);
            return tree;
        }

        // --- scene -----------------------------------------------------------------------

        private static void BuildScene(StateTreeAsset tree, ItemRegistryAsset registry)
        {
            var cameraObject = new GameObject("Main Camera");
            cameraObject.tag = "MainCamera";
            var camera = cameraObject.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 3f;
            camera.transform.position = new Vector3(0f, 0f, -10f);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.13f, 0.14f, 0.17f);

            var rootObject = new GameObject("Root Context");
            var rootHost = rootObject.AddComponent<StateTreeContextHost>();
            rootHost.kind = StateTreeContextKind.Root;
            rootObject.AddComponent<WorldService>();

            var levelObject = new GameObject("Level");
            levelObject.transform.SetParent(rootObject.transform);
            var levelHost = levelObject.AddComponent<StateTreeContextHost>();
            levelHost.kind = StateTreeContextKind.Level;

            // THE MOUNT IS THE HOST'S OWN TREE SLOT — §3.7's "a tree mounted on its context",
            // literally: select Player and the tree asset is right there, and its states write
            // the Player scope directly (no child runner, no copy-down). The State Tree
            // window's play highlight follows host mounts since this same milestone.
            var playerObject = new GameObject("Player");
            playerObject.transform.SetParent(levelObject.transform);
            var playerHost = playerObject.AddComponent<StateTreeContextHost>();
            playerHost.kind = StateTreeContextKind.Player;
            playerHost.tree = tree;
            playerObject.AddComponent<UIService>();
            playerObject.AddComponent<ContextKeyHotkeyBehaviour>();

            BuildScreen("inv", "INVENTORY", new Vector3(-2.2f, 0.8f, 0f), playerObject);
            BuildScreen("tooltip", "ITEM", new Vector3(2.2f, 0.8f, 0f), playerObject);

            var hint = new GameObject("Hint");
            hint.transform.position = new Vector3(0f, -2.4f, 0f);
            var mesh = hint.AddComponent<TextMesh>();
            mesh.text = "Press I to open the inventory. Click a row: weapon equips, potion "
                + "shows a tooltip.\nWatch the tree in the State Tree window — the active "
                + "state IS the UI.";
            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            mesh.font = font;
            mesh.fontSize = 40;
            mesh.characterSize = 0.07f;
            mesh.anchor = TextAnchor.MiddleCenter;
            mesh.color = new Color(0.75f, 0.78f, 0.85f);
            var meshRenderer = hint.GetComponent<MeshRenderer>();
            if (font != null && meshRenderer != null)
                meshRenderer.sharedMaterial = font.material;

            Selection.activeObject = playerObject;
        }

        private static void BuildScreen(string screenId, string title, Vector3 position,
            GameObject parent)
        {
            var screenObject = new GameObject("Screen " + screenId);
            screenObject.transform.SetParent(parent.transform);
            screenObject.transform.position = position;

            var screen = screenObject.AddComponent<UIScreenBehaviour>();
            screen.screenId = screenId;
            var panel = new GameObject("Panel");
            panel.transform.SetParent(screenObject.transform, false);
            panel.SetActive(false);
            screen.visualRoot = panel;

            var view = screenObject.AddComponent<UIScreenTextView>();
            view.screen = screen;
            view.title = title;
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
