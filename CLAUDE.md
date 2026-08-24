# Draw To Play (com.powerofire.drawtoplay)

The shared runtime + editor toolset for PowerofFire games (M21 in the PhysicsExamples2D
sandbox, the arena in CyberBot). Unity 6000.5, PhysicsCore2D only (never the legacy
Physics2D components).

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
