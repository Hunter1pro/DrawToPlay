# The draw tool

The authoring surface the toolset is named for: you draw shapes in the scene and they become
real, simulated, tagged content — a port of the Godot `terrain_paint` tool and its creation
flows into Unity.

## What it is made of

Runtime lives under `Runtime/Paint`, `Runtime/Physics` and the drawn-shape types:

| type | what it does |
|---|---|
| `DrawnShapeAsset` | an authored shape, as data |
| `DrawnCurve` | the curve behind a stroke |
| `DrawnShapeRenderer` | draws it at runtime |
| `ShapeTessellator` | turns a curve into a mesh |
| `PolyBool` | boolean operations on polygons — union, subtract |
| `DrawKit` | the entry point the tools call |

Editor lives under `Editor/` — `DrawShapeTool`, `DrawToPlayOverlay`, `DrawToolSettings`,
`DrawToPlayMenu`, plus `CollisionDebugOverlay` for seeing what the physics world actually has.

## Drawing

`DrawShapeTool` is a Unity `EditorTool`, so it activates from the toolbar and the scene view
overlay (`DrawToPlayOverlay`) carries its options. You draw a stroke; it becomes a
`DrawnCurve`; `ShapeTessellator` gives it a mesh; `PolyBool` merges it with what is already
there or cuts it away.

Because shapes are **assets**, a drawn shape is content like any other: it can be referenced by
a placement, carried into a manifest, regenerated, and diffed.

## Physics

Everything drawn is **PhysicsCore2D** (`Unity.U2D.Physics`). This is not a preference — the
legacy `Rigidbody2D` / `Collider2D` component system is a different, unrelated engine, and the
two do not see each other. A drawn shape that ended up with a legacy collider would be invisible
to every query the toolset makes.

`Runtime/Physics` holds the bridge between drawn geometry and physics bodies/shapes.
`CollisionDebugOverlay` is how you check that what you drew is what the world got.

## Where output goes

Through `DrawToPlayFolders` (Editor), never a hardcoded path. A host project sets its roots
once:

```csharp
DrawToPlayFolders.project          = "Assets/YourProject";
DrawToPlayFolders.projectGenerated = "Assets/YourProject/GraphEditor/Generated";
```

and the folder properties — `Drawn`, `Trees`, `Tasks`, `Graphs`, `Flows`, `Stamps`,
`Subsystems`, `Tests` — derive from it. Output is predictable per project, and two projects
never fight over the same folder.

## Editor UI rules that apply here

The drawing tools are inspector-heavy, so the UI Toolkit rules bite hardest in this area:

- A custom `Editor` or `EditorWindow` hosting serialized properties is a **UI Toolkit host** —
  `CreateInspectorGUI`, properties as `PropertyField`s.
- A **UI Toolkit drawer inside an IMGUI host renders the literal text "No GUI Implemented"**.
  When a picker shows that string, the host is IMGUI; port the host, do not add an `OnGUI`
  fallback to the drawer.
- **Nothing per repaint that walks the project.** IMGUI repaints 2–3× per interaction; an
  `AssetDatabase.FindAssets` inside `OnGUI` is an inspector that takes a second.

The full statement of these is in `CLAUDE.md` at the repo root.
