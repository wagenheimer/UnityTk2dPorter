# Tk2d Porter

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
[![Unity](https://img.shields.io/badge/Unity-2021.3%2B-blue.svg)](https://unity.com)
[![UPM](https://img.shields.io/badge/UPM-com.wagenheimer.tk2dporter-green.svg)](https://github.com/wagenheimer/UnityTk2dPorter)

Editor tools to migrate [2D Toolkit](https://unikronsoftware.com/2dtoolkit/) (tk2d) content to
native Unity — sprites, buttons, toggles, scrollbars, and TextMeshPro (3D) — without hand-converting
every GameObject or rewriting every referencing script from scratch.

---

## Features

- **Two conversion modes, auto-detected**
  - `Tk2dImageConverter` — tk2d sprites/buttons/toggles/scrollbars → uGUI `Image`/`Button`/`Toggle`/
    `Slider`, for UI content.
  - `Tk2dSpriteRendererConverter` — tk2d sprites → native `SpriteRenderer`, for gameplay/world-space
    content (this is the one most `tk2dSprite` fields in an older game actually need — not
    everything tk2d touched was UI).
  - `Tk2dConversionRouter` — one "Auto-detect" menu command that picks the right converter per
    selected GameObject (already under a `Canvas`? → Image. Otherwise → SpriteRenderer). Force either
    mode explicitly via the two dedicated menu items when the heuristic guesses wrong.
- **Click/toggle wiring is preserved automatically** — tk2d's `tk2dUIItem`/`tk2dUIToggleButton`
  reflection-based `SendMessage(target, methodName)` dispatch is reproduced on the new `Button.onClick`/
  `Toggle.onValueChanged` via a tiny runtime relay (`UIEventRelay`), added and wired automatically
  during conversion. No manual re-wiring of existing click handlers required.
- **Button state merging** — tk2d's common "Off/On/Disabled state child GameObjects" button pattern
  (`tk2dUIHoverDisabledItem`) is merged into a single root `Image` + `Button` with an `Animation`
  transition, auto-assigning any project `AnimatorController` whose name contains "Button".
- **TextMeshPro (3D) → TextMeshProUGUI**, recursively, preserving font/size/color/alignment/spacing.
- **`Tk2dScriptFieldMigrator`** — scans your C# scripts for field DECLARATIONS typed as tk2d
  components and mechanically rewrites just the type token to the uGUI/native equivalent (e.g.
  `public tk2dUIItem btOk;` → `public Button btOk;`). It never touches method bodies, event wiring, or
  property access (`.IsOn`, `.Value`, `.SelectedIndex`, `.Enabled`, `.SetSprite(...)`,
  `GetComponent<tk2dXxx>()`) — those get reported instead, with a Markdown dry-run report and a
  confirmation-gated Apply step.
- **Gameplay components are never silently deleted** — the converters purge orphaned
  `tk2dUI*`-prefixed components after conversion (e.g. leftover `tk2dUIToggleButtonGroup`
  coordinators), but respect an explicit exclusion list for custom `tk2dUI*`-named classes that are
  actually your own gameplay logic (drag-and-drop, etc.), not tk2d visuals.

---

## Requirements

| Dependency | Version |
|---|---|
| Unity | 2021.3 LTS or newer |
| [2D Toolkit](https://unikronsoftware.com/2dtoolkit/) | Any version — imported manually into your project (`Assets/TK2DROOT` or similar), not a UPM package |
| TextMeshPro | Required for the TextMeshPro → TextMeshProUGUI converter |

---

## Installation

Add the package via the Unity Package Manager **Add package from git URL**:

```
https://github.com/wagenheimer/UnityTk2dPorter.git
```

Or add it manually to `Packages/manifest.json`:

```json
{
  "dependencies": {
    "com.wagenheimer.tk2dporter": "https://github.com/wagenheimer/UnityTk2dPorter.git"
  }
}
```

---

## Usage

### 1. Convert scene/prefab content

Select one or more GameObjects containing tk2d sprites, then use one of:

```
Tools/Seven Sails/Convert tk2d → uGUI/Auto-detect (Selection)
Tools/Seven Sails/Convert tk2d → uGUI/Force UI Image (Selection)
Tools/Seven Sails/Convert tk2d → uGUI/Force Sprite Renderer (Selection)
```

Right-click a `tk2dSprite`/`tk2dSlicedSprite`/`tk2dClippedSprite`/`tk2dTiledSprite` component in the
Inspector for a per-component "Convert to Image" context menu entry, too.

For buttons using tk2d's `Animation`-transition pattern, create an `AnimatorController` somewhere in
your project with "Button" in its name (e.g. `Button.controller`) beforehand — the converter finds
and assigns it automatically.

### 2. Migrate referencing script fields

```
Tools/Seven Sails/Migrate tk2d Script Fields → uGUI (Dry Run Report)
Tools/Seven Sails/Migrate tk2d Script Fields → uGUI (Apply)
```

Run the Dry Run first, review the generated Markdown report (field rewrites + manual-review items),
then run Apply once you're comfortable with what it's about to change.

**Recommended order:** convert scene/prefab content first, THEN run the script field migrator, THEN
reopen each affected scene/prefab and re-drag any broken Inspector references (see Known Limitations
below) before committing.

---

## Known Limitations

- **Changing a field's C# type does not remap the serialized Inspector reference.** After the
  migrator rewrites `public tk2dUIItem btOk;` to `public Button btOk;`, any scene/prefab that already
  had that field assigned will show it as `Missing`/`None` after recompilation — the underlying
  `tk2dUIItem` component instance the reference pointed to was already destroyed by the scene
  converter. You must manually re-drag the correct component back into the field, per affected
  scene/prefab. There is no way around this — Unity does not remap serialized references across a
  field's declared-type change.
- The script field migrator uses line-based regex matching, not a real C# parser — multi-line field
  declarations and generic/nested tk2d field types (`List<tk2dSprite>`) are not detected.
- `Image` vs `SpriteRenderer` resolution for ambiguous `tk2dSprite*`-typed fields only searches
  **prefabs** (not scenes, to avoid disruptively opening/closing scenes during a batch scan) —
  unresolved fields default to `Image` and should be double-checked against the dry-run report.
- No 1:1 mapping is attempted for `tk2dUIToggleButtonGroup`, `tk2dSpriteAnimator`, or custom
  drag-and-drop control classes (`tk2dUIDragItemGrid`/`tk2dUIDragItemClamp`-style components) — these
  require a bespoke rewrite per project and are only flagged, never auto-converted.

---

## License

MIT — see [LICENSE](LICENSE).
