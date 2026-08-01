# Draw-to-Play — draw a world, rig it, animate it, give it a brain

Unity port of the Godot `terrain_paint` toolset plus the `statetree` addon, per
[`docs/draw-tool-port-brief.md`](../../../docs/draw-tool-port-brief.md).
**Milestones M0–M7 are complete**: you can draw a shape, derive PhysicsCore2D collision from
it, rig and skin it, animate it, ragdoll and shatter it, and author its AI in a node graph —
without leaving the Scene view or writing a line of gameplay code.

Assemblies: `PowerOfFire.DrawToPlay` (runtime) · `PowerOfFire.DrawToPlay.Editor` (tools) ·
`PowerOfFire.DrawToPlay.GraphEditor` (M7 graph frontend, editor-only, isolated on purpose —
see [The boundary](#the-boundary-that-matters-73)) · `PowerOfFire.DrawToPlay.Tests.Editor`.

PhysicsCore2D only (`Unity.U2D.Physics`). No `Rigidbody2D`, no `Collider2D`, anywhere.

---

## The four flows

A **flow** is a set of stage tabs in the Flow window (**Tools ▸ Draw To Play ▸ Flow Window**)
— a checklist and a shortcut into the right tool for each step of making one kind of thing.
Flows are data (`FlowDefinition` assets under `Assets/DrawToPlay/Flows/`), so a new content
type is a new asset, not new window code. Each tab shows a badge: *Not started* / *In
progress* / *Complete*, or a neutral dot when no validator can honestly judge it.

Nothing is modal. No tab is ever disabled, working out of order is legal, and the badges only
report what is actually in the scene.

### Terrain — `Sculpt → Paint → Decorate → Collision → Gameplay`
The level as a set of drawn blobs. Sculpt the ground freehand, paint up to three blended
textures into it, scatter prefab stamps over it, and derive chain or solid collision that
regenerates on every edit. *Create it:* **Tools ▸ Draw To Play ▸ Create Terrain Flow**.
*Gameplay* (spawn markers, room/zone metadata) is a placeholder tab with no validator — the
`room_def` / `zone_rules` ports are not in this port yet.

### Character — `Draw → Rig → Skin → Animate → Physics → Behavior`
One entity: body parts as drawn shapes under a single root, a bone chain per part, a skinned
mesh that survives a redraw, pose clips, a ragdoll derived from the rig, and an ability graph.
*Create it:* **Tools ▸ Draw To Play ▸ Create Character Flow**. Existing flow assets gain the
newer tabs automatically — the built-in stage list is applied additively, so your edits and
your own stages survive.

### Enemy / AI — the Character flow plus **AI**
§6.2 defines an enemy as "(inherits Character stages 1–5) → Combat → AI", so it is not a
separate asset: the **AI** tab is the seventh stage of the Character flow and stays neutral
for a player entity. It opens the same graph editor as Behavior with the perception palette.
*Combat* (health, hitbox windows, weapon/effect/loot defs) has no tab: its outputs are
components and assets with no tool of their own, and a tab that activates nothing and
validates nothing would be worse than the Inspector.

### Prop / destructible — `Draw → Surface → Body → Destruction → Reward`
Every tool this flow needs ships (draw, physics material fields on the shape definition,
`EntityBody` body types, `DestructibleShape` slice/fragment, `SpawnEntryAsset` loot entries),
but **there is no Prop `FlowDefinition` asset yet** — author a prop through the Inspector, or
add a flow asset of your own listing these stages. Saying so here rather than shipping a tab
strip whose badges cannot judge anything is the same rule the flow window follows.

---

## Tools and menus

| Tool (Scene view) | What it does |
| --- | --- |
| **Draw Shape** | LMB-drag draws; overlapping a shape unions into it, a stroke fully inside spawns a layered detail shape, `Ctrl/Cmd` carves, carving fully inside punches a hole. *Free / Circle / Rect* modes and *Force New* live in the Draw To Play overlay. |
| **Transform Shape** | Drag inside to move, drag the ring to rotate (`Shift` = 15° snap). |
| **Paint Shape** | Paints one of three texture slots into the shape's mask; `Shift` erases; `[` / `]` resize the brush. On release the stroke also sculpts the outline. |
| **Rig Shape** | Click a chain of joints along a part, `Enter` (or double-click) commits it into a sibling `Skeleton` and binds the part to it. *Setup Mode* writes moved bones back into the rest pose. |
| **Stamp** | Drag-scatters the armed prefab or texture: one placement per spacing unit, with jitter, optional flip and random scale. |

Every gesture — stroke, carve, paint, drag, scatter batch — is exactly **one undo step**
(Godot parity).

| Menu (**Tools ▸ Draw To Play ▸ …**) | |
| --- | --- |
| Draw Shape Tool | Activate the Draw tool |
| Flow Window | The staged creation flows |
| Pose Sheet | Clip timeline: scrub, key, auto-key, capture form |
| Create Terrain Flow · Create Character Flow | Create (or upgrade) a built-in flow asset |
| Create Enemy Preset Trees | Zombie / Brute / Archer `StateTreeAsset`s + their weapon defs |
| Bake State Tree Graph | Export the selected graph's tree as a standalone `<Graph>_Baked.asset` (the importer already bakes on save — this is for when a separate file is wanted) |
| Toggle Skin Debug Overlay · Regenerate Skins In Scene · Diagnose Skin Mesh | Skinning diagnostics |
| Build M1 Demo Scene · Verify M2…M7 · Play M1/M5/M6/M7 Demo Scene | See below |

Also: **GameObject ▸ 2D Object ▸ Drawn Shape**; **Assets ▸ Create ▸ Draw To Play ▸ …** for
pose clips, rigs, state trees, flow definitions, the AI condition/task assets and a **State
Tree Graph**. Scene-view overlays: *Draw To Play* (tool + mode), *Stamps*, *Collision Debug*,
*Skin Debug*, *Active State*.

---

## Demo and verify scenes

Each milestone ships a one-click scene that IS its exit criterion. They are built by code, so
they can be rebuilt at any time and they never rot silently.

| Menu | Scene | What it proves |
| --- | --- | --- |
| Build M1 Demo Scene | `Demo/M1PhysicsDemo.unity` | A drawn bowl gets chain collision; a ball rolls on it. |
| Verify M2 Paint + Stamps | *(rebuilds M1)* | Two blended textures painted through the real mask pipeline; stamps scattered along the surface. |
| Verify M3 Rig + Skin | `Demo/M3RigDemo.unity` | A three-bone chain on a drawn limb, bent 35° — the shape deforms; the binding survives a redraw. |
| Verify M4 Pose Animation | `Demo/M4AnimDemo.unity` | A looping two-column clip with a form morph, playing at runtime. |
| Verify M5 Ragdoll + Destruction | `Demo/M5CombatDemo.unity` | `Space` toggles ragdoll and back; a dropped crate fragments into physical debris with matching meshes. |
| Verify M6 State Trees | `Demo/M6AIDemo.unity` | The zombie preset chases, stops, strikes; the dummy bursts, which can only mean damage was dealt. |
| Verify M7 Graph Frontend | `Demo/M7GraphDemo.unity` | The M6 scene with one field changed — the zombie runs a tree that came out of the **graph editor**, via the bake. |

`Play M1/M5/M6/M7 Demo Scene` opens the scene and enters play mode despite the project's
StartupScene convention, and restores it afterwards.

**M7 is a comparison, not a new scene.** It is a copy of the M6 scene with the runner
re-pointed at the baked tree, so "does a baked graph behave like the hand-built preset?" is
answered by watching the same fight twice. If a graph already exists at
`Presets/ZombieGraph.statetree`, the command bakes and runs **yours** untouched — which is the
milestone's real exit criterion, with the built-in build reduced to a fallback that lets the
pipeline be tested without a human.

---

## The graph editor: author → bake → run

1. **Author.** Select an entity and click the **Behavior** (player) or **AI** (enemy) tab in
   the Flow window; it opens that entity's graph, or offers to create one. Graphs live in
   `Assets/DrawToPlay/Graphs/<Entity>.statetree` (the M7 demo's own graph sits with the preset
   trees instead, because it *is* the zombie preset authored the M7 way).
   - A **State** is a context node; the **task blocks** inside it are what runs while it is
     active, all of them in parallel.
   - A **Transition** is its own node between two states, carrying the condition, the
     interrupt flag and an authoring-only evaluation order.
   - **Check While Running** is the whole personality of a transition: on = evaluated every
     tick *before* the tasks and cancelling them when it fires; off = evaluated only once every
     task has finished.
   - Port types make bad wiring impossible rather than reporting it afterwards: only a
     condition node fits a condition slot, only a task block fits a state.
2. **Bake.** Automatic — a `ScriptedImporter` bakes every `.statetree` file on import, so the
   runtime tree is never out of step with the graph. The baked `StateTreeAsset` is the graph
   file's **main asset**: drag the graph file straight onto a `StateTreeRunner`'s `data` field,
   and double-clicking it still opens the graph. A standalone `<Graph>_Baked.asset` export
   exists for when a separate file is wanted.
3. **Run.** Put a `StateTreeRunner` on the entity, set `ownerObject` to the entity root, press
   Play. In play mode the **active state is tinted live** in the graph window, and the *Active
   State* Scene-view overlay reports which runner, which state and which state it came from —
   for when the graph window is closed, or when the runner's tree was hand-built and has no
   node to tint. The tint is borrowed and restored on every transition, on stop, and on exiting
   play mode; it is never saved.

The seed component library (`Runtime/StateTree/Library/`) is the palette: conditions
(target detected / in range, line of sight, health threshold, cooldown, timer, blackboard
compare) and tasks (chase, face, wait, play pose clip, attack, spawn projectile, knockback,
set blackboard, fire cue). Adding one to the library plus a wrapper class naming its runtime
type is all a new node costs — parameter ports are generated from the runtime type's public
fields, so a port and its field cannot drift apart.

### The boundary that matters (§7.3)

**The runtime never sees a graph type.** Graph Toolkit is experimental and editor-only, so the
graph is authoring data and nothing else; a bake step extracts the flat tree the runner
executes — node-id index, tasks, conditions, transitions — in exactly the shape
`StateTreePresets` builds by hand. Consequences worth knowing:

- A hand-built tree and a baked tree are indistinguishable to the runner. Presets, the
  Inspector and the graph are three ways to author the same asset.
- The graph frontend lives in its own assembly that nothing else references. If it fails to
  compile against a newer experimental release, the graph tab says so and **every other tool
  keeps working**. This is not hypothetical: this project moved from the 0.4.0-exp.2 package
  to the 0.5.0-exp.1 builtin module during M7, and the two do not agree on the case of a
  single member name.
- If the toolkit ever becomes unusable, the model and the runner do not move — the fallback
  frontend is a tree outliner, and the Godot dock proved that shape works.

---

## Layout

```
Runtime/                     PowerOfFire.DrawToPlay
  DrawKit.cs PolyBool.cs     curve math + Godot Geometry2D subset over vendored Clipper2
  DrawnCurve/ShapeAsset      the drawing as source of truth + style (SO-only)
  DrawnShapeRenderer.cs      shadow / fill / rim / outline mesh, edge wobble
  ShapeTessellator.cs        ear-clip fill over keyholed rings
  Paint/                     mask lifecycle, CPU stamps, the blend shader
  Physics/                   EntityBody, TerrainBlob, DestructibleShape, FragmentBody
  Rig/                       RigAsset, ShapeRig, DrawnShapeSkin, SkinMeshBuilder, RagdollDriver
  Anim/                      PoseClipAsset, PoseAnimator
  Combat/                    HealthComponent, Weapon/Effect/SpawnEntry defs, HitboxWindow
  StateTree/                 the runner + model, and Library/ — the component palette
Editor/                      the tools, overlays, flows, and one verify command per milestone
GraphEditor/                 M7 graph frontend: graph, nodes, baker, importer, highlight
Tests/Editor/                EditMode tests for the runner's semantics
```

---

## Conventions

- **1 world unit ≈ 32 Godot px.** Every ported constant is written as its Godot value ÷ 32 so
  the source number stays legible. Stroke tolerances are screen-pixel based (Godot zoom
  parity).
- **Winding:** outer rings positive area, holes negative.
- **Persistence is ScriptableObject-only.** No JSON mirror, no export step for gameplay data.
- **Collision comes from the RAW outline**, before render-time wobble — so visual/collision
  drift is real, deliberate, and visible in the collision debug overlay.
- Port semantics faithfully first. Where Unity forced a deviation, the file that made it says
  so in its class comment.

## Known caveats

Carried forward and still true:

- Undoing a spawned shape leaves its `.asset` in `Assets/DrawToPlay/Drawn/` — AssetDatabase
  creation is not undoable.
- `fillTexture` needs wrap mode **Repeat**; UVs tile every `textureScale` units.
- Materials are generated (`Sprites/Default` fallback chain); for player builds add the shader
  to *Always Included Shaders* — irrelevant while authoring-only.
- The blend shader is hand-written HLSL, not Shader Graph: `.shadergraph` files are not
  hand-authorable. Same contract.
- Non-uniform scale combined with rotation is unsupported when deriving collision (physics has
  no scale; vertices are baked through `lossyScale`).
- A skinned mesh layer is `HideFlags.DontSave`. A freshly reopened scene shows the flat mesh
  until something regenerates it (the next bind, *Regenerate Skins In Scene*, or entering play
  mode) — which is why the Skin badge reads *In progress* until then.
- Shapes with holes are ignored by the skinning path; a part that needs a hole stays unskinned.
- Ragdoll bones can collide with non-adjacent bones (only hinge pairs are exempted).
- Pose clips are not `AnimationClip`s: no Timeline or Animator integration. A baked exporter is
  a cheap bridge if it is ever needed.
- The M6/M7 zombie moves its **Transform**, not a body — it floats level over the ground
  instead of falling onto it. A mover component is the follow-up.
- The archer preset's `Bow` has no projectile prefab (`arrow.tscn` is gameplay work, not
  authoring work); assign one and it fires with no other change. The archer also cannot
  back-pedal — the library has no retreat task, so at point-blank range it shoots instead.
- The seed library has no negated condition, so "target lost" is expressed by a failing chase
  plus an unconditional last transition rather than by a `Not`.
- §6 checklist items with no honest edit-time test are not validated, and say so in the
  checklist: every part bound or explicitly static, required clip names, no orphan weights,
  collision categories, min &lt; max ranges, a death clip existing.
- The M7 verify builds its zombie graph through Graph Toolkit's public authoring API. If a
  future experimental release moves it, the command reports the exact missing member and tells
  you to author the graph by hand — which is the exit criterion anyway. Baking and running an
  existing graph does not depend on that path.
