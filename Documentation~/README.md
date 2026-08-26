# Draw-to-Play documentation — `project/room204`

> The drawing half is not on this branch, so there is no `draw-tool.md` here. `main` has it,
> along with the curves, shapes, painting, rigs and destructible bodies it describes.

How the toolset works and how you use it. One document per subsystem; each answers the same
three questions — **what it is**, **how you author it**, **what the flow is at runtime**.

| doc | covers |
|---|---|
| [meta-rules.md](meta-rules.md) | **the constitution for runtime code — read this first** |
| [state-trees.md](state-trees.md) | states, tasks, transitions, the executor, contexts and scopes |
| [graphs.md](graphs.md) | task graphs, the node picker, parameters, graphs as a declared API |
| [defs-and-services.md](defs-and-services.md) | `ServiceDef`, services as plain classes, settings, capabilities |
| [subsystems.md](subsystems.md) | the creation flow, requests and announcements, call and return |
| [tags.md](tags.md) | the declared vocabulary, `[WorldTag]`, who carries what |
| [registries.md](registries.md) | catalogs of rows, entry refs, how a def names a row |
| [inventory.md](inventory.md) | items, the bag, equipment slots, crafting |
| [levels.md](levels.md) | level defs, kinds, manifests, placement, portals, bootstrap |
| [objectives-and-cutscenes.md](objectives-and-cutscenes.md) | objectives, zones, cutscenes, autosave |
| [ui-wiring-brief.md](ui-wiring-brief.md) | how views are fed, and how a press becomes a request |

## The shape of the whole thing

```
Root scope ─────── services that outlive a level  (craft, cutscene, inventory, level, ui)
  └─ Level scope ─ services rebuilt per level      (ability, objective, world)
       └─ Player ─ services that belong to an actor (bag, ui)
```

A **scope** is a lifetime. A **service** is a plain C# class living on a scope, made from a
**def** that declares its whole surface. A **state tree** is behaviour: states holding tasks,
wired by transitions. A **task** is one step — C# type, sub-tree, or authored graph. Anything
that waits on another subsystem is a drawn flow, never a callback.

Everything else here is that paragraph, expanded.

> These docs live in the package so they travel with the code they describe. A consuming
> project should link to them rather than copy them — a forked doc is wrong the first time
> either side changes.
