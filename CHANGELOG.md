# Changelog

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
