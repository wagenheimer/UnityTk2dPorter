# tk2d → native migration runbook (for agents)

This is the operational checklist for migrating a 2D Toolkit project to native Unity
sprites/UI. It is written to be executed by another agent (or a human) with the
Tk2d Porter package installed. Every step produces a file you can inspect, so
nothing depends on "it looked fine".

> The tool converts **content**. It cannot rewrite your C# API usage. Plan for a
> code pass (step 4); it is usually 10–30 lines per project.

---

## 0. Before touching anything

1. Commit the project. Create a tag: `pre-tk2d-migration`.
2. Run **Tools → Wagenheimer → Tk2d Porter → Audit Project (Report)**.
   Read `Logs/tk2d-audit-*.md`:
   - `blockers` column: components with no automatic equivalent.
   - totals per component type.
3. Decide the order: **prefabs first, then scenes** (scenes reference prefabs).
4. Note the Unity compile time. Large projects (tk2d + analyzers) can take
   several minutes per domain reload — batch code changes; do not iterate one
   line at a time.

## 1. Snapshot the references (per scene/prefab)

Open the scene (or the prefab in Prefab Mode) and run:

```
Tools → Wagenheimer → Tk2d Porter → References → Snapshot tk2d References
```

It writes `Logs/tk2d-refs-<scene>-<timestamp>.json` with every serialized field
whose declared type is a tk2d component, keyed by `GlobalObjectId` (stable
across the reparenting/renaming that conversion performs).

Do this **before** converting. Without it, the fields cannot be rebuilt
automatically and you will have to re-drag every reference by hand.

## 2. Generate the runtime libraries (once, before deleting tk2d)

```
Tools → Wagenheimer → Tk2d Porter → Settings (Create/Select)        # optional: paths/policies
Tools → Wagenheimer → Tk2d Porter → Libraries → Generate Sprite + Animation Libraries
```

This bakes every tk2d sprite name and animation clip into
`Resources/SpriteLibrary.asset` and `Resources/AnimationLibrary.asset`, so migrated code keeps
working with names (`SetSprite("club_A")`, `Play("rockform")`) instead of hard references.

## 3. Convert the content

- Prefab asset: open it in Prefab Mode, select the root, then
  **Force Convert to Sprite Renderer** (world) or **Force Convert to uGUI Image** (UI).
- Scene: select the **smallest roots that contain all the UI** (not the camera),
  and use **Auto-detect Conversion** or the forced mode.
  - UI content gets a `Screen Space - Overlay` Canvas with
    `CanvasScaler.ScaleWithScreenSize` (match = height) using the tk2d camera's
    visible world height as reference, so 1 canvas unit = 1 world unit at any
    resolution.
  - Select several HUD roots in the same invocation: they share one canvas, and
    the selection order becomes the draw order (back → front).
- Save the scene/prefab.

Checklist after conversion:
- 0 `tk2dBaseSprite` left in the converted hierarchy.
- `SpriteRenderer`s at PPU 1, scale matching the old tk2d `_scale`.
- Colliders preserved for world content.
- `tk2dCameraAnchor` removed (it fights RectTransforms).
- `tk2dSpriteAnimator` disabled and reported (see blockers).

## 4. Migrate the script field types

```
Tools → Wagenheimer → Tk2d Porter → Migrate tk2d Script Fields → uGUI (Dry Run Report)
```

Review the report, then run `(Apply)`. It rewrites only **field declarations**
(`tk2dUIItem btOk` → `Button btOk`, `tk2dSprite s` → `Image`/`SpriteRenderer`,
`TextMeshPro` → `TextMeshProUGUI`).

Then compile and fix the reported call sites. Typical ones:

| tk2d | native |
|---|---|
| `sprite.SetSprite(name)` | **works unchanged** via the package's `SetSprite` extension (SpriteLibrary) — or `sprite.sprite = SpriteLibrary.Find(name)` |
| `sprite.CurrentSprite.name` | `sprite.sprite.name` |
| `item.Enabled` | `button.interactable` |
| `toggle.IsOn` | `toggle.isOn` / `SetIsOnWithoutNotify` |
| `scrollbar.Value` | `slider.value` |
| `clipped.ClipRect` | `image.fillAmount` (Filled, axis-aware) |
| `radial.VisibleAmount` | `image.fillAmount` (Filled, Radial360) |
| `sprite.scale` | `transform.localScale` |
| `tk2dSpriteAnimator.Play(clip)` | a native frame animator (bake the clips into an asset) |
| `tk2dUIDragItemGrid` | a pointer-drag component (Rewired/mouse raycast) |

## 5. Remap the references

Open the scene/prefab again and run:

```
Tools → Wagenheimer → Tk2d Porter → References → Remap from Latest Snapshot
```

It assigns the new component on the same GameObject to each migrated field and
writes `Logs/tk2d-refs-remap-*.md`. Anything it cannot resolve is listed with
the reason (`field type still tk2d`, `target not found`, `no equivalent`).

## 6. Verify

1. Build a baseline **before** converting (Play Mode capture + a probe of
   positions/sizes). Compare after conversion:
   - layout: positions/sizes within a few units;
   - visuals: additive-material sprites (glows) need an additive material in
     uGUI or must be removed; sibling order replaces tk2d's Z order.
2. Play each converted scene end-to-end: menus, dialogs, gameplay, level editor.
3. Watch the Console for `NullReferenceException` — the usual cause is a field
   that was skipped by the remapper.

## 7. Remove tk2d

Only after every scene/prefab is converted and tested:
- delete `Assets/TK2DROOT` (or the tk2d folder) and the tk2d sprite collections;
- remove the package/legacy scripts that only existed for tk2d;
- re-run the audit: it must report 0 files.

---

## Known blockers and their replacements

| Blocker | Native replacement |
|---|---|
| `tk2dSpriteAnimator` | A small `SpriteClipAnimator` (+ a generated clip asset: name, fps, `Sprite[]`). Bake the clips from the tk2d animation asset **before** deleting tk2d. |
| `tk2dUIDragItemGrid` / `Clamp` | A pointer component using the project's input (raycast → drag with offset, snap in editor mode). |
| `tk2dRadialSprite` | `Image.Type.Filled` + `FillMethod.Radial360` + `fillAmount`. |
| `tk2dClippedSprite` | `Image.Type.Filled` (Horizontal/Vertical) or a world-space Canvas + Filled image. |
| `tk2dCameraAnchor` | Canvas anchors (the world→canvas mapping already reproduces the camera). |
| `tk2dCamera` | A plain orthographic `Camera` with the same `orthographicSize`; keep any aspect/letterbox script. |
| Additive tk2d materials | uGUI material with additive blending, or drop the glow. |
| Same-Z tk2d draw order | Sibling order in the Canvas (selection order = back→front). |

## Pitfalls (learned the hard way)

- **PPU**: tk2d is 1 texel = 1 world unit. Converted textures must be PPU 1 and
  the sprite's own scale baked into the transform; never compensate with a 100x
  localScale (gameplay code that sets `transform.localScale` absolutely breaks).
- **Canvas math**: `worldPositionStays` is wrong for overlay canvases (their
  world origin is the screen corner). Use `world - camera.position`.
- **Prefab instances**: unpacking must target the outermost prefab root; passing
  a child throws and silently converts nothing.
- **Script references**: destroying a tk2d component nulls every field of that
  type. Snapshot before, remap after.
- **Editor Game view size** differs from the play-mode screen; verify layout in
  Play Mode, not in the edit-mode Game view.
- **Compile time**: after editing the package or project scripts, wait for
  `EditorApplication.isCompiling == false` before running menu commands.
