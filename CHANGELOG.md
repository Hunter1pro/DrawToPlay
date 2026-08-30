# Changelog

## 0.2.0 — 2026-08-31
- Interrupts are heard along the whole active chain: a `checkWhileRunning` transition on
  any ancestor of the current state is evaluated every tick — current state first, then up
  the parents — so "a film pre-empts everything" is one row on the root instead of one per
  leaf. A pre-empted task still exits `Cancelled`, never completed.
- Entry resolution descends through a node whose only transitions are interrupts:
  listening from the chain does not make an organizational node a resident state.

## 0.1.2 — 2026-08-27
- Constructor injection for subsystems: after `(scope, def)`, further constructor parameters
  are resolved from the scope at install. A missing required one fails the install, naming
  the parameter. `[InjectService]` on a service stays for late and cross-scope collaborators.

## 0.1.1 — 2026-08-27
- `ManifestSpawner` holds the level's OWN host the way it holds spawned bodies: a level host
  with `autoStart` off on the spawner's object starts on frame two, after its rows are
  citizens. Consumers no longer need a spawner subclass for a level tree.
- `[PlacementId]`: a manifest row id, picked from the manifests the asset declares — the
  `[WorldTag]` rule for placements. `StateTreeOffers.PlacementsFor` is the offer.

## 0.1.0 — 2026-08-24
First cut as a package, extracted from the PhysicsExamples2D sandbox with its history
(M0–M43). Promoted from the examples: `InputService`, `LevelBootstrap` (press play on a
level and the session comes in behind you), `ManifestSpawner` (rows → bodies, frame-two
tree start; derive for progress/gained), `LevelAuthoring` (placement + subsystems
authoring helpers), `GraphAuthoring` (task-graph authoring by picking). All output
folders now route through `DrawToPlayFolders` (a host project sets `project`,
`projectGenerated`, and registers its task assemblies).
