# Task graphs

A graph is the **logic** surface — the inside of a task, authored on a canvas instead of in
C#. If a state tree says *what mode am I in*, a graph says *what happens in this step*.

Graphs are built on Unity's Graph Toolkit (`Unity.GraphToolkit.Editor`, a built-in editor
module in Unity 6 — there is no package to add). A graph is a `.taskgraph` asset backed by
`GraphTaskAsset`.

## When to draw instead of write

Draw a graph when the logic is **wiring**: branch on a condition, ask a subsystem, wait for the
answer, set a blackboard value, return. Write C# when the step is **computation** — a solver, a
query, anything with a loop or real math. The boundary is not aesthetic: a graph node is a unit
of reuse offered to a designer, and a node that only ever appears inside one graph should have
been a method.

## Running one

`RunGraphTask` runs a graph as a task **by live reference** — edit the canvas and every user of
that graph runs the new program on the next entry. There is no bake step to forget.

Entry chains are `OnEnter` / `OnTick` / `OnExit`. Inside: `Branch`, blackboard get/set/has,
compare and boolean operations, cues, and `Return Success` / `Failure` / `Running`.

Three semantics that will bite if you assume otherwise:

- **Falling off the end of a chain returns `Running`**, not `Success`. Say what you mean.
- **Boolean operators do not short-circuit.** Conditions in this system are allowed to have
  side effects, so both sides evaluate.
- **Latent nodes resume where they parked.** A graph that awaited something continues from the
  await, not from the top — the Unreal model.

Cycles and runaway step counts fail loudly rather than hanging the editor.

## Parameters

**The graph's Blackboard panel is the task's parameter list.** Variables declared there bake
into parameter definitions; variable nodes on the canvas are live reads; and each *state* that
uses the graph overrides values per use. This is the Blueprint-instance model — one program,
many configurations — carried by `GraphTaskParameterSet` and `GraphTaskParameterOverride`.

So a "chase the target" graph is authored once with a `speed` variable, and the three states
that use it each set their own speed without copying the graph.

## Graphs talk to subsystems

The nodes that make a graph more than local logic are the subsystem pair:

- **`Ask Subsystem`** — issue a request to a service on the scope, by key.
- **`Asked Result`** — the return, on the far side of the wait.

This is the drawn form of *call and return* (see [subsystems.md](subsystems.md)). A graph that
needs another subsystem asks and awaits; it never subscribes to an event, and no service calls
back into a graph.

`Announced Payload` reads what a service announced. `Await Screen` waits on a UI verb.

## Generated node wrappers

Every task type a project registers gets a node wrapper generated for it, so a C# task is
immediately available on the canvas without hand-writing a node class. That registration is one
line in the host project:

```csharp
[InitializeOnLoadMethod]
private static void Tell()
{
    DrawToPlayFolders.project          = "Assets/YourProject";
    DrawToPlayFolders.projectGenerated = "Assets/YourProject/GraphEditor/Generated";
    DrawToPlayFolders.RegisterTaskAssembly("Your.Assembly.Name");
}
```

Wrappers land in `projectGenerated`. They are generated output — do not hand-edit them, and do
not review them line by line; regenerate instead. Each task gets both a plain node and a
`…CallNode` variant where call-and-return applies.

**Drift is the thing to watch.** The generator and the classes it mirrors will disagree the
first time someone renames a field. The wrapper set is regenerated from the assembly, so the
fix is always "regenerate", never "patch the wrapper".

## The picker

One search-first, categorised picker offers all three task flavours together — C# types,
authored sub-trees, authored graphs — plus pinned rows to create a new task graph or sub-tree
on the spot. `[StateTreeCategory("Path/Here", "what it does")]` on a C# task is what puts it in
the right place with the right description.
