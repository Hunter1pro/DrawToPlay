# State trees

A state tree is the behaviour surface. It is where you say *what mode this thing is in* and
*what pre-empts what*. It is deliberately the first place you reach for a new feature: most
features are a few new tasks, or a rewiring of tasks that already exist.

## The parts

- **State** — a named node holding a **task list** and a set of **transitions**.
- **Task** — one step. Three interchangeable flavours, all offered by the same picker:
  1. a **C# type** deriving `StateTreeTaskAsset` — compile it and it appears,
  2. a **sub-tree** — a whole tree run as one task,
  3. a **task graph** — a `.taskgraph` program run as one task (see [graphs.md](graphs.md)).
- **Transition** — target state + optional condition + an *interrupt* flag. Interrupting
  transitions are evaluated before tasks tick — on the current state and on every ancestor
  of it, current state first, so one row high on the tree pre-empts any running
  descendant; non-interrupting ones only on completion.
- **Executor** — `StateTreeExecutor`, headless and testable. `StateTreeRunner` is the thin
  MonoBehaviour wrapper; a tree can also be run nested from inside another tree.

## Writing a task

```csharp
[StateTreeCategory("Tasks/Outpost", "Play an animation asset on the owner")]
public sealed class PlayAnimationTask : StateTreeTaskAsset
{
    public AnimationAsset animation;          // authored fields show in the inspector
    public bool waitForFinish = true;

    [InjectOwner]   private OutpostCharacter m_Character;   // the actor this tree runs on
    [InjectService] private OutpostAnimationService m_Animations;  // resolved from the scope

    public override void OnEnter(StateTreeContext context) { … }

    public override StateTreeStatus OnTick(StateTreeContext context, float deltaTime)
        => m_Animations.IsFinished(m_Character.animation)
            ? StateTreeStatus.Success
            : StateTreeStatus.Running;
}
```

The three lifecycle hooks are `OnEnter`, `OnTick`, `OnExit(context, status)`. `OnTick` returns
`Running`, `Success`, `Failure` or `Cancelled`.

**`Cancelled` is not optional.** Any task can be torn down mid-flight when an interrupting
transition fires. `OnExit` runs with the status that ended it, and a task that allocated,
spawned or subscribed must undo that on `Cancelled` exactly as it would on `Success`.

**Never inject by scanning.** `[InjectOwner]`, `[InjectService]` and `[InjectHost]` are filled
by the injector before `OnEnter`. A task that calls `FindObjectOfType` has broken the rule that
lifetime is the scope.

**`blocking`** (default `true`) — a blocking task must finish before the state's later tasks
are considered done. Set it false for something that runs alongside, like a hidden-child toggle.

## Contexts and scopes

`StateTreeContextHost` is one rung of the spine, and the spine is short on purpose:

```
StateTreeContextKind.Root  →  Level  →  Player
```

A scope is a **lifetime**: it is created, it is destroyed, and everything born under it asks
what it needs *at birth*. Nothing watches for a replacement — when a level ends, its scope and
every service on it are gone, and the next level builds new ones.

`StateTreeContext` itself is deliberately lean: the owner, a `blackboard` and a
`domainContext` dictionary, and signals. Domain state never grows typed fields on the base
class — if a subsystem needs to remember something, that lives on the subsystem.

`host.autoStart` controls whether the tree runs immediately. Anything that must be a registered
citizen of the world before its tree starts — a character whose tasks bind through the world
service — sets `autoStart = false` and is started explicitly after registration. Order there is
a requirement, not a hope.

## The flow, once, end to end

1. A scope is created (`StateTreeContextHost` on a GameObject, kind Root/Level/Player).
2. `StateTreeServiceInstaller` builds the services declared for that scope from their defs.
3. A tree starts on a host. The injector fills every `[Inject*]` field on its tasks.
4. Each tick: interrupting transitions are tested, current state first and then its ancestors, → active tasks tick → completion transitions
   are tested. A pre-emption tears the current tasks down with `Cancelled`.
5. The scope dies. Services and trees go with it.

## Sub-trees

`RunSubTreeTask` runs a tree as a task. The blackboard is shared, exits are named
(`success`/`exit`, `fail`/`failure`), `Cancelled` propagates inward, and cycle and depth guards
are enforced loudly rather than silently.

Use it when a piece of behaviour is genuinely reusable across trees. Prefer it to copying
states — a copied state stops being the same behaviour the first time one copy is edited.

## Editing

The **State Tree Editor** opens on the asset. It shows the real states, edits the inspector in
place, wires transitions (target dropdown, condition slot, interrupt toggle), and manages
sub-asset lifetime with full undo.

In play mode it highlights the **active state green and the previous amber**, matched by
`nodeId` through the executor's deep copy — so you are watching the exact machine you authored,
not a diagram of it.

## Boundaries worth knowing

- Conditions may have side effects, so boolean composition does **not** short-circuit.
- A graph task that falls off the end of its chain returns `Running`, not `Success`.
- Latent nodes resume where they parked, matching Unreal's semantics.

## Schema modes (mission mode)

Same runtime engine, different authoring experiences. A `StateTreeSchema` asset (Create → Draw To
Play → State Tree Schema) claims a `treeKind`; a tree of that kind opens the State Tree window in
the schema's mode, on the same asset. The toolbar's **Programmer** toggle is today's window.

- **Kinds** — a domain node is a state whose `roleKind` names a schema kind. A kind is either a
  **template** (a reusable tree: the node holds ONE `RunSubTreeTask` of it and sets its
  parameters) or a **group** (no tasks; optionally one inert *context task* holding its data — a
  level's manifest, a zone's place). `step: true` kinds are what a `step` child entry admits.
- **Rules** — `topKinds` and each kind's `children` (and `childLimit`) decide Add's filtered
  palette, drag-and-drop, and the problems `StateTreeSchemaRules.Validate` reports.
- **Hidden** — the mode lists only domain nodes; their tasks, conditions, transitions and keys,
  and every other state, are Programmer mode's.
- **Inspector** — a template node shows its template's parameters, each drawn after the template
  field it is bound to (a `[PlacementId]` field → the enclosing context's manifest; an entry ref →
  its registry's rows; a key field → a name), and read-only facts (resume, saves, where it leads).
- **Connection layer** — a context implementing `IStateTreeArea` is a circle in the Scene view; a
  step's placement parameter is a line from it to the row. Selection lights both ways; Play
  highlights the active domain node.
- **Templates' parameters** — a String parameter binds into a `string`, a `StateTreeKeyField` or a
  `StateTreeEntryRef<>` (resolved by name against the template's own registries). Parameter
  bindings are applied before keys and entry refs resolve.
- **Builders** — a script that re-authors a tree calls `StateTreeDomainKeep.Detach(tree)`, skips
  what it `Keeps` while clearing sub-assets, and `Reattach`es the domain nodes to the new root.
