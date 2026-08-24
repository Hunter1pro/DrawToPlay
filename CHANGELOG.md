# Changelog

## 0.1.0 — 2026-08-24
First cut as a package, extracted from the PhysicsExamples2D sandbox with its history
(M0–M43). Promoted from the examples: `InputService`, `LevelBootstrap` (press play on a
level and the session comes in behind you), `ManifestSpawner` (rows → bodies, frame-two
tree start; derive for progress/gained), `LevelAuthoring` (placement + subsystems
authoring helpers), `GraphAuthoring` (task-graph authoring by picking). All output
folders now route through `DrawToPlayFolders` (a host project sets `project`,
`projectGenerated`, and registers its task assemblies).
