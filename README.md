# Draw-to-Play — `project/room204`

> **This branch has no drawing tool.** `Room204` consumes the toolset to build behaviour, not
> geometry, so the drawing half — curves, shapes, tessellation, painting, rigs, skins, pose
> animation, destructible bodies and terrain blobs, and every editor tool and flow over them —
> is not present. `main` still carries all of it. Nothing in the state-tree half depended on
> it, which is why the seam was clean.

What remains is the part a game is *wired* with: state trees and task graphs, services and
their defs, registries, levels and placement, inventory and crafting, objectives, cutscenes,
saving, and the UI wiring that feeds them.

## What this package gives you

| area | what it is |
|---|---|
| **State trees** | states holding tasks, wired by transitions; a headless executor and a live editor |
| **Task graphs** | the logic surface — a `.taskgraph` program run as a task, on Unity's Graph Toolkit |
| **Services + defs** | a service is a plain C# class; a `ServiceDef` declares its whole surface |
| **Registries** | catalogs of rows, with typed `StateTreeEntryRef<T>` pickers instead of strings |
| **Levels** | level defs, kinds, manifests, placement, portals, bootstrap — nothing placed by hand |
| **Inventory + craft** | items, `IBag`, equipment slots, recipes, a bench |
| **Objectives, zones, cutscenes** | the things that happen to a player over time |
| **UI** | `UiService`, defs, widgets — views are injected at spawn and never poll |

Full per-subsystem documentation is in **`Documentation~/`**.

## The two rules everything else follows

1. **Call and return.** A subsystem calls the services it needs, in order, in one method, and
   returns a result. There are no events between subsystems and nothing calls back.
2. **Waiting is a drawn flow.** Anything that has to wait is a state tree or a task graph —
   `Ask Subsystem` issues, `Asked Result` returns. Never a callback chain.

`Documentation~/meta-rules.md` is the full statement, and it is the constitution for runtime
code here.

## Layout

```
Runtime/                     PowerOfFire.DrawToPlay
  StateTree/                 the runner + model, and Library/ — the task palette
    Abilities/ Contexts/ Contracts/ Craft/ Cutscene/ Input/ Items/ Levels/
    Library/ Objectives/ Projectiles/ Services/ Ui/ World/
  Combat/                    HealthComponent, weapon/effect/spawn defs, HitboxWindow
  UI/                        the runtime UI pieces the UiService spawns
Editor/                      the State Tree Editor, drawers, level tooling, subsystem flow
  StateTreeEditor/ Graph/ Levels/ Subsystems/ Icons/
GraphEditor/                 the graph frontend: nodes, baker, importer, highlight
Tests/Editor/                EditMode tests for the runner's semantics
Documentation~/              the per-subsystem docs
```

## Host-project setup

A consumer tells the package where its output lives, once, from an
`[InitializeOnLoadMethod]`:

```csharp
DrawToPlayFolders.project          = "Assets/YourProject";
DrawToPlayFolders.projectGenerated = "Assets/YourProject/GraphEditor/Generated";
DrawToPlayFolders.RegisterTaskAssembly("Your.Assembly.Name");
```

Generated graph-node wrappers for your own tasks land in `projectGenerated`. The assembly whose
name contains `GraphEditor` is where they belong.

Two things a consumer supplies itself:

- **UniTask** — a git URL cannot be a package dependency, so add it to your own manifest:
  `"com.cysharp.unitask": "https://github.com/Cysharp/UniTask.git?path=src/UniTask/Assets/Plugins/UniTask#2.5.11"`
- **`com.unity.modules.physicscore2d`** — the runtime still uses `Unity.U2D.Physics` in
  `StateTree/Library/LineOfSightCondition.cs`. The package deliberately does not declare it, so
  a project that does not want 2D physics enabled is not forced into it; declare it in your
  manifest.

## Conventions

- **Persistence is ScriptableObject-only.** No JSON mirror, no export step for gameplay data.
- **Declared is what crosses.** A def's rows are the subsystem's public surface; everything
  inside is plain C#.
- **Capabilities are interfaces** (`IBag`, `IAutosave`, `IWornView`, …). Consumers name the
  capability, never the class.
- **Lifetime is the scope.** Root → Level → Player. The born thing asks at birth; nothing
  watches for a replacement.
- **Tests:** `Tests/Editor`, assembly `PowerOfFire.DrawToPlay.Tests.Editor`. Consumers list the
  package under `testables` to run them. Every change adds a test.
