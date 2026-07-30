# Draw-to-Play — M0 (DrawKit + shape rendering)

Port of the Godot `terrain_paint` drawing core per `docs/draw-tool-port-brief.md` (§8 M0).
Runtime: `PowerOfFire.DrawToPlay` · Editor: `PowerOfFire.DrawToPlay.Editor`.

## Try it (M0 exit criterion)

1. **GameObject → 2D Object → Drawn Shape** (or just start drawing — the first stroke
   spawns a shape). Activate the tool via **Tools → Draw To Play → Draw Shape Tool**
   or the *Draw To Play* overlay in the Scene view.
2. **LMB-drag** — freehand stroke. Overlapping the shape **extends** it (union);
   a stroke fully inside spawns a layered detail shape; a miss spawns a new shape.
3. **Ctrl/Cmd + LMB-drag** — carve (subtract). Carving fully inside punches a **hole**.
4. Overlay: **Free / Circle / Rect** modes (anchor-drag; **Shift** in Circle/Rect =
   carve → hole gesture), **Force New** = every stroke becomes its own object.
5. Select a shape + the **Transform Shape** tool: drag inside to move, drag the 46 px
   ring to rotate (**Shift** = 15° snap).
6. Style (fill/outline/rim colors, widths, edge wobble, shadow) lives on the shape's
   `DrawnShapeAsset` — created on demand under `Assets/DrawToPlay/Drawn/`.

Every gesture is exactly **one undo step** (Godot parity).

## Layout

- `Runtime/DrawKit.cs` — line-faithful port of `curve_kit.gd` (fit/resample/smooth/
  simplify, `MergeStroke` sculpt booleans, keyhole, displace…). Unit-agnostic.
- `Runtime/PolyBool.cs` — Godot `Geometry2D` subset over vendored **Clipper2**
  (`ThirdParty/Clipper2/`, Boost license — Godot wraps the same algorithm family).
  Winding contract: outers positive area, holes negative.
- `Runtime/DrawnCurve.cs` — serialized Bézier path (Godot `Curve2D` stand-in;
  closed ring = last point duplicates first).
- `Runtime/DrawnShapeAsset.cs` — the drawing as source of truth + style (SO-only).
- `Runtime/DrawnShapeRenderer.cs` + `ShapeTessellator.cs` — shadow/fill/rim/outline
  mesh, ear-clip fill over keyholed rings, `fillShade` vertex gradient, edge wobble.
- `Editor/` — Draw + Transform Shape `EditorTool`s, Scene-view overlay, menu items.

## Conventions

- **1 world unit ≈ 32 Godot px**; style defaults are the Godot values ÷32. Stroke fit
  tolerance = 3 *screen* px converted to world units (Godot zoom parity); absolute
  clamp bounds use the fixed 1/32 factor (`DrawToolSettings`).
- No PhysicsCore2D in M0 — physics derivation starts in M1.

## Known M0 caveats

- Undoing a spawned shape leaves its `.asset` in `Assets/DrawToPlay/Drawn/`
  (AssetDatabase creation isn't undoable) — cleanup utility planned with M1+.
- `fillTexture` needs wrap mode **Repeat**; UVs tile every `textureScale` units.
- Materials are generated (`Sprites/Default` fallback chain); for player builds add
  the shader to *Always Included Shaders* — irrelevant while authoring-only.
