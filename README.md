# Draw-to-Play — draw a world, rig it, animate it, give it a brain

Unity port of the Godot `terrain_paint` toolset plus the `statetree` addon, per
[`docs/draw-tool-port-brief.md`](../../../docs/draw-tool-port-brief.md).
**Milestones M0–M7b are complete**: you can draw a shape, derive PhysicsCore2D collision from
it, rig and skin it, animate it, ragdoll and shatter it, and author its AI **directly on the
asset the runner runs** — without leaving the Scene view or writing a line of gameplay code.

Assemblies: `PowerOfFire.DrawToPlay` (runtime) · `PowerOfFire.DrawToPlay.Editor` (tools,
including the State Tree Editor) · `PowerOfFire.DrawToPlay.GraphEditor` (the optional graph
visualisation, editor-only, isolated on purpose — see
[The boundary](#the-boundary-that-matters-73)) · `PowerOfFire.DrawToPlay.Tests.Editor`.

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
mesh that survives a redraw, pose clips, a ragdoll derived from the rig, and an ability tree.
*Create it:* **Tools ▸ Draw To Play ▸ Create Character Flow**. Existing flow assets gain the
newer tabs automatically — the built-in stage list is applied additively, so your edits and
your own stages survive.

The **Behavior** tab opens the entity's tree in the [State Tree Editor](#the-state-tree-editor-author--run):
the runner's own `data` when it has one, otherwise `Assets/DrawToPlay/Trees/<Entity>.asset`
— loaded if it is already there, created with a root state if it is not, and assigned to the
runner either way (creating the `.asset` is not undoable; the assignment is).

### Enemy / AI — the Character flow plus **AI**
§6.2 defines an enemy as "(inherits Character stages 1–5) → Combat → AI", so it is not a
separate asset: the **AI** tab is the seventh stage of the Character flow and stays neutral
for a player entity. It opens the same editor as Behavior on the same kind of asset; the only
difference is the `treeKind` a newly created tree is stamped with (`enemy_ai` vs
`player_flow`), which the runtime does not branch on.
*Combat* (health, hitbox windows, weapon/effect/loot defs) has no tab: its outputs are
components and assets with no tool of their own, and a tab that activates nothing and
validates nothing would be worse than the Inspector.

The AI badge asks for one thing more than Behavior's: not just "a tree is assigned" but "the
state the runner would enter has tasks or transitions in it". A tree that is nothing but a
root is what clicking the tab hands you, and a badge that called that *Complete* would be
congratulating you for clicking a tab.

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
| State Tree Editor | Edit a `StateTreeAsset` in place — states, tasks, wiring, parameters |
| Pose Sheet | Clip timeline: scrub, key, auto-key, capture form |
| Create Terrain Flow · Create Character Flow | Create (or upgrade) a built-in flow asset |
| Create Enemy Preset Trees | Zombie / Brute / Archer `StateTreeAsset`s + their weapon defs |
| Bake State Tree Graph | *(optional graph path)* Export the selected graph's tree as a standalone `<Graph>_Baked.asset` |
| Toggle Skin Debug Overlay · Regenerate Skins In Scene · Diagnose Skin Mesh | Skinning diagnostics |
| Build M1 Demo Scene · Verify M2…M7b · Play M1/M5/M6/M7 Demo Scene | See below |

Also: **GameObject ▸ 2D Object ▸ Drawn Shape**; **Assets ▸ Create ▸ Draw To Play ▸ …** for
pose clips, rigs, state trees, flow definitions, the AI condition/task assets and a **State
Tree Graph**. Double-clicking a `StateTreeAsset` opens the State Tree Editor. Scene-view
overlays: *Draw To Play* (tool + mode), *Stamps*, *Collision Debug*, *Skin Debug*,
*Active State*.

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
| Verify M7 Graph Frontend | `Demo/M7GraphDemo.unity` | *(optional path)* The M6 scene with one field changed — the zombie runs a tree that came out of the **graph editor**, via the bake. |
| Verify M7b Direct Editor | *(no scene — opens a window)* | The Zombie preset opens in the **State Tree Editor** and it renders the five real states; every transition resolves its target. |

`Play M1/M5/M6/M7 Demo Scene` opens the scene and enters play mode despite the project's
StartupScene convention, and restores it afterwards.

**M7b has no scene of its own** because its subject is a window, not a simulation. The
command checks what a machine can (the preset is there, five states, no dangling wire, the
window really renders them) and its class comment spells out the half only a human can: edit
a range in the editor, press **Play M6 Demo Scene**, watch the zombie behave differently. Use
*Play M6*, not *Verify M6* — verify rebuilds the preset from code and would throw your edit
away.

**M7 is a comparison, not a new scene.** It is a copy of the M6 scene with the runner
re-pointed at the baked tree, so "does a baked graph behave like the hand-built preset?" is
answered by watching the same fight twice. If a graph already exists at
`Presets/ZombieGraph.statetree`, the command bakes and runs **yours** untouched — which is the
milestone's real exit criterion, with the built-in build reduced to a fallback that lets the
pipeline be tested without a human.

---

## The state tree editor: author → run

There is no step in between. The editor is a live view of a `StateTreeAsset` — the *exact*
object a `StateTreeRunner` deep-copies on `StartTree` — so adding a state, wiring a
transition or nudging a range writes into that file and nothing else has to happen.

1. **Open.** Double-click any `StateTreeAsset`, use **Tools ▸ Draw To Play ▸ State Tree
   Editor**, or click the **Behavior** / **AI** tab in the Flow window with the entity
   selected. The tab opens the runner's own tree when it has one; otherwise it loads or
   creates `Assets/DrawToPlay/Trees/<Entity>.asset` (with a root state) and assigns it to the
   runner. Selecting a `StateTreeAsset` in the Project window also brings the editor to it —
   the window's **Auto-open** toggle turns that off for anyone who does not want a window that
   follows the selection.
2. **Author.** The state list *is* the tree — the rows are the real `StateTreeNodeAsset`
   sub-assets, and the toolbar's add / add-child / remove create and destroy them in place.
   - A **state** holds **tasks**, and they all run in **parallel**: the state is finished when
     every one of them is. That is why a windup is its own state rather than "wait, then
     attack" inside one.
   - **Children** are organizational nesting only. The runner enters the first leaf under the
     root (`ResolveEntryNode`), so the first state you add under the root is the entry state.
   - **Transitions** are a per-state list, and **their order is their evaluation order** — the
     runner takes the first whose condition passes, so the unconditional "otherwise" arm goes
     last. Each row picks its target from the tree's own states, and renaming a state rewires
     everything that pointed at it, so a wire cannot be typed wrong or silently stranded.
   - **Check While Running** is the whole personality of a transition: on = evaluated every
     tick *before* the tasks and cancelling them when it fires (the running tasks get
     `OnExit(Cancelled)`); off = evaluated only once every task has finished.
   - Task and condition **parameters** are drawn from the runtime type's own serialized
     fields, so a field and its control cannot drift apart. Adding a task type to
     `Runtime/StateTree/Library/` is all a new palette entry costs.
3. **Run.** Put a `StateTreeRunner` on the entity, set `ownerObject` to the entity root, press
   Play. The **active state is highlighted live in the editor** as the runner moves through the
   tree, with the previous → current transition in the status line; the *Active State*
   Scene-view overlay reports the same thing for when the window is closed. Every runner whose
   `data` is *this* asset is found, so a scene with six zombies lets you watch whichever one you
   pick. The highlight is matched by `nodeId`, not by object identity — the runner is executing
   a deep copy, which is what keeps two runners of one tree from sharing a task's timer state.

One honest caveat on "immediately": that deep copy is taken in `StartTree`, so an edit lands
on the **next** Play (or the next `StartTree`), not retroactively into a fight already
running. What M7b removed is the bake, not the frame boundary.

The seed component library (`Runtime/StateTree/Library/`) is the palette: conditions
(target detected / in range, line of sight, health threshold, cooldown, timer, blackboard
compare) and tasks (chase, face, wait, play pose clip, attack, spawn projectile, knockback,
set blackboard, fire cue). Read a preset before authoring your own — **Tools ▸ Draw To Play ▸
Create Enemy Preset Trees** builds Zombie / Brute / Archer, which are the ported
`enemies/*.gd` archetypes with their real ranges and timings, and they open in this editor
like anything else.

### The graph editor, demoted (M7b)

M7 built a Graph Toolkit frontend that authors a **separate** graph document and bakes it into
a `StateTreeAsset`. It still works, it still ships, and it is now **optional visualisation
only** — nothing routes you into it. Reach it through its own menus: **Assets ▸ Create ▸ Draw
To Play ▸ State Tree Graph** to author one, **Tools ▸ Draw To Play ▸ Bake State Tree Graph** to
export a standalone `<Graph>_Baked.asset` (a `ScriptedImporter` also bakes every `.statetree`
file on import, and the baked tree is that file's main asset).

**The caveat that demoted it:** a graph is not the tree. What you edit there is authoring data
that a bake converts, so the thing you changed and the thing the runner runs are two files
that agree only as of the last bake — and a tree that did **not** come from a graph (a preset,
an Inspector-built tree, anything a designer already has) cannot be opened there at all. If
you use both, treat the graph as the source and the baked asset as output: editing the baked
tree in the State Tree Editor works perfectly, and the next bake overwrites it.

### The boundary that matters (§7.3)

**The runtime never sees a graph type**, and after M7b it never sees an editor type either.
The model — `StateTreeAsset`, `StateTreeNodeAsset`, the task/condition sub-assets — is the
only thing both frontends and the runner have in common. Consequences worth knowing:

- A preset, an Inspector-built tree, an editor-built tree and a baked tree are
  indistinguishable to the runner. They are four ways to author one asset.
- The graph frontend lives in its own assembly that nothing else references, reached from the
  main tools only through `StateTreeGraphBridge`'s reflection. If it fails to compile against a
  newer experimental release, **every other tool keeps working** — including the State Tree
  Editor, which does not know it exists. This is not hypothetical: this project moved from the
  0.4.0-exp.2 package to the 0.5.0-exp.1 builtin module during M7, and the two do not agree on
  the case of a single member name.
- The M6 runtime is frozen. M7b adapted the editor to the model, never the reverse — the
  runner, the tick order and the deep copy are the same code the M6 tests pin.

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
Editor/                      the tools, overlays, flows, the State Tree Editor, and one verify
                             command per milestone
GraphEditor/                 the optional graph frontend: graph, nodes, baker, importer, highlight
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
- **`Verify M6 State Trees` rebuilds the Zombie preset from code**, and
  `AssetDatabase.CreateAsset` replaces the file — so it discards anything you authored into
  that preset by hand. Once the M6 scene exists, use `Play M6 Demo Scene`, which leaves the
  asset alone. The same is true of `Create Enemy Preset Trees` for all three archetypes; the
  weapon defs are the exception (created only when absent, so a hand-assigned arrow prefab
  survives). `Verify M7b Direct Editor` deliberately does **not** rebuild a preset that is
  already there.
- The Behavior / AI tabs create `Assets/DrawToPlay/Trees/<Entity>.asset` when the entity has
  no tree. Creating an asset is not undoable (the same caveat the drawn-shape tools carry), so
  undoing after clicking the tab unassigns the tree from the runner but leaves the file.
- A transition names its target by `nodeId` **string**, not by object reference — that is the
  M6 model and M7b did not change it. Renaming a state inside the editor rewrites every
  transition that pointed at it, and node ids are made unique on entry, so the two ways to
  strand a wire are editing `nodeId` in the raw Inspector and deleting a state from there.
  `Verify M7b Direct Editor` reports every transition whose target does not resolve, and the
  runner logs the unknown target at the moment it tries to take it.
