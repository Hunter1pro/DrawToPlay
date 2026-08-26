# Draw To Play (com.powerofire.drawtoplay)

The shared runtime + editor toolset for PowerofFire games. Unity 6000.5, PhysicsCore2D only
(never the legacy Physics2D components).

**Branch `project/room204`: the drawing half is not here.** Curves, shapes, tessellation,
painting, rigs, skins, pose animation, destructible bodies, terrain blobs and every editor tool
and flow over them were removed — this branch is the wiring toolset only. `main` carries the
full package. Nothing under `Runtime/StateTree` or the state-tree editors depended on the
drawing half, which is why the seam was clean; do not reintroduce a dependency on it here.

**No 2D physics on this branch.** `com.unity.modules.physicscore2d` is not declared and not
needed: `LineOfSightCondition` was the last file touching `Unity.U2D.Physics` and it is gone,
so a consuming project can disable the module outright. Do not reintroduce a `Unity.U2D.Physics`
reference here — that is what `main` is for.

- `Documentation~/meta-rules.md` is the constitution for runtime code — read before adding a
  service, screen, flow or wire. Five rules: call and return (no events on services); waiting
  is a drawn flow; lifetime is the scope; declared is what crosses; capabilities are
  interfaces (`IBag`, `IAutosave`, `IScreenJuice`, …) — consumers name the capability, never
  the class, never scan the scene.
- `Documentation~/ui-wiring-brief.md`: views never poll — UiService injects at spawn; a press is
  a Request on the showing scope. A def that `spawns` a screen shows it ON BEHALF OF its
  own scope.
- Editor UI: hosts are UI Toolkit; drawers may be either; nothing per repaint walks the project.
- Output folders go through `DrawToPlayFolders` (Editor). A host project sets
  `DrawToPlayFolders.project`, `projectGenerated`, and calls `RegisterTaskAssembly` from an
  `[InitializeOnLoadMethod]`. Generated node wrappers for a host's tasks live in an assembly
  whose name contains `GraphEditor`.
- Tests: `Tests/Editor` (assembly `PowerOfFire.DrawToPlay.Tests.Editor`); consumers list the
  package under `testables` to run them. Every change adds a test.
- Consumers must add UniTask themselves (a git URL cannot be a package dependency):
  `"com.cysharp.unitask": "https://github.com/Cysharp/UniTask.git?path=src/UniTask/Assets/Plugins/UniTask#2.5.11"`.
