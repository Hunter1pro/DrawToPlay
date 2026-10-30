# Levels, placement and travel

A level is data. Nothing in a level is placed by hand — a level is a **manifest** of placements,
and the scene is what the manifest builds.

## The pieces

| type | what it is |
|---|---|
| `LevelDef` / `LevelRegistry` | the catalog of levels |
| `LevelObjectDef` / `LevelObjectRegistry` | one **placement** — what to put down, where |
| `LevelObjectKindDef` / `LevelObjectKindRegistry` | the **kinds** a placement can be |
| `LevelContent` | the manifest: the placements that make up one level |
| `LevelContent.data` / `Data<T>()` | what else a level carries — per-level assets the game reads by type; the runtime never does |
| `ManifestSpawner` | what turns a manifest into objects |
| `LevelBootstrap` | what starts a level |
| `LevelService` | travel, and the current level |
| `LevelPortal` / `PortalKind` | the ways out |

## Kinds versus placements

A **kind** is a `ServiceDef` that names no class and builds a `ServiceBody` — enemy, npc,
pickup, resource, zone, exit, shrine, cutscene. It is the *template*.

A **placement** (`LevelObjectDef`) is one instance of a kind in one level: which kind, where,
facing which way, carrying which tags, with which attributes overridden.

```
Placement  "yard.exit.ridge"   kind "exit"   at (1.0, -0.6)   facing 180°
```

`LevelObjectTagRef` carries the tags; `PlacementAttribute` / `PlacementAttributeSet` carry the
per-placement overrides; `[PlacementAttributes]` declares which attributes a kind exposes to a
placement. `LevelGroundPlane` (`XY` by default) says which plane the level lies in.

This is why *nothing is placed by hand*: a level is a list of rows, so it can be generated,
diffed, validated and regenerated. A hand-dragged object in a scene is invisible to all of that
and is overwritten the next time the level is built.

## Travel

`LevelPortal` is the way out, and `PortalKind` distinguishes the two shapes:

- **`GoTo`** — travel to `levelName`. One way; the old level is gone.
- **`Expedition`** — travel to `levelName` **remembering the way back**, so a return lands where
  you left.

`LevelService` owns the current level and performs the switch. Because *lifetime is the scope*,
a level switch destroys the Level scope and every service on it, then builds the next one. A
subsystem that must survive travel belongs on **Root**, not Level — that choice is the `scope`
field on its def, and it is the whole mechanism.

`ILevelProgressStore` is the capability for persisting what a level remembers between visits.

## Bootstrap, in order

1. `LevelBootstrap` starts, on a fresh Level scope.
2. The scope's services are installed from their defs.
3. `ManifestSpawner` walks the `LevelContent` manifest and builds each placement from its kind.
4. Each spawned body registers with `WorldService` — that is when its tags become askable.
5. Trees start. Anything with `autoStart = false` is started explicitly, after registration —
   the spawned bodies' hosts, and the level's own host when it sits on the spawner's object.
   A level tree whose first state names citizens (a beat that lights up a door) needs exactly
   the same wait a body's tree does, so it gets the same one.

A field that names a placement by id carries `[PlacementId]`: it is picked from the manifests
the asset declares (a tree's `registries`, a registry's `dependsOn`), the way `[WorldTag]` picks
a tag from declared vocabularies.

Step 4 before step 5 is not incidental. A character whose tasks bind through the world must be a
citizen before its tree runs, or its first tick asks about a world that has never heard of it.

## Building a level

A level is authored as rows and built in one action; `LevelManifestWindow` is where the manifest
is edited, and the generated ghost meshes (`LevelGhostMeshes`) show placements in the scene
without those placements being real scene objects.

The window's left column is a palette of the kinds the level's catalog offers (`LevelRegistry.kinds`).
Drag a kind into the Scene view and a row of it stands where it lands: on whatever the scene shows
under the pointer, else on a collider, else on the ground plane. Drop it on the list, or double-click it (a single click places nothing),
and it stands at the view's centre. A new row is named and id'd `level.kind.N` (`hall.door.3`, the
first free number), filed in the group the level already uses for that kind, selected with its name
focused; one undo takes it back. The list groups the rows by kind — a fold per kind with its icon and
count, rows by name — and each fold remembers whether it was open (per user). Selecting a row opens
its group; the search hides rows without rebuilding the panel.

A new row's name is shown as its fixed `level.kind.` and the last part, which is all that is typed
(focused on create; emptied, it keeps its number). The full name and the group are under **More**.
The id never follows a rename: it is what steps name.

### Presets

A **preset** (`LevelObjectPresetDef`, in the game's one `LevelObjectPresetRegistry`) is a placement's
*core* — kind, tree, the tree's arguments, tags — plus the **facts** an object of it writes (a key and
what it means: the first is its primary fact) and the **mission steps** that fit it (schema kind names,
picked). A few of the tree's parameters are its **settings**: each object keeps its own value for those,
and one it never set follows the preset's.

- Every level's manifest lists the catalogue (`LevelObjectRegistry.presets`); the palette shows each
  preset under its kind, placed the same way (drag, or double-click).
- A row names its preset (`LevelObjectDef.preset`). What runs is `LevelObjectRegistry.Resolve(row)`: a
  copy wearing the preset's core, then the row's settings. An edit to the core reaches every object made
  from it. A row with no preset is its own core, exactly as before.
- The window shows a linked row's core read-only, its settings editable (↺ follows the preset again),
  and **New preset from this…**: the preset copied under a new name with this object's settings as its
  defaults, the object switched to it — what runs does not change. One undo step.
- **Use in mission…** lists the zones of the mission trees under this level, and the preset's steps that
  fit each; the step is added last, its object and the preset's primary fact filled in, and selected in
  the State Tree window. A row dragged from the list onto a zone or a step does the same there.

The test that a level is well-formed is that you can delete the scene and rebuild it from the
manifest with no difference. If that is not true, something was placed by hand.

## Rules

- **A placement names a kind; it does not embed one.** Two placements of the same kind share one
  template, so fixing the template fixes both. A preset is the same rule one level up.
- **Read a row through `Resolve`.** A reader of `entries` that wants what runs (its tree, tags,
  arguments) asks the manifest, or it misses a preset's core.
- **Level-scoped services die with the level.** Do not cache one across travel.
- **Nothing self-injects.** A component that finds its service by scanning will find the
  previous level's service after the first switch.
