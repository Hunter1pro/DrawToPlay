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
5. Trees start. Anything with `autoStart = false` is started explicitly, after registration.

Step 4 before step 5 is not incidental. A character whose tasks bind through the world must be a
citizen before its tree runs, or its first tick asks about a world that has never heard of it.

## Building a level

A level is authored as rows and built in one action; `LevelManifestWindow` is where the manifest
is edited, and the generated ghost meshes (`LevelGhostMeshes`) show placements in the scene
without those placements being real scene objects.

The test that a level is well-formed is that you can delete the scene and rebuild it from the
manifest with no difference. If that is not true, something was placed by hand.

## Rules

- **A placement names a kind; it does not embed one.** Two placements of the same kind share one
  template, so fixing the template fixes both.
- **Level-scoped services die with the level.** Do not cache one across travel.
- **Nothing self-injects.** A component that finds its service by scanning will find the
  previous level's service after the first switch.
