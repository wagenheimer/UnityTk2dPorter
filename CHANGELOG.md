# Changelog

All notable changes to this project will be documented in this file.

## [0.5.1] - 2026-07-20

### Fixed
- Inherit `TextMeshProToUGUIConverter` from `TMP_EditorPanel` instead of `Editor` to preserve TextMeshPro's native rich inspector UI while keeping the conversion button.

## [0.5.0] - 2026-07-17

### Added
- simplify conversion menus into root of Tools/Wagenheimer/Tk2d Porter/ and add CONTEXT menu support for converting individual components to SpriteRenderer

## [0.4.4] - 2026-07-17

### Fixed
- resolve NRE when converting tk2d to SpriteRenderer by destroying legacy Mesh components before adding SpriteRenderer

## [0.4.3] - 2026-07-12

### Fixed
- stop touching already-correct sprites; stop reparenting/destroying GameObjects during scrollbar conversion (was corrupting hierarchy and zeroing Transform scale via nested prefab unpack interaction)

## [0.4.2] - 2026-07-12

### Fixed
- use TextureImporterSettings for spriteAlignment (not exposed directly on TextureImporter)

## [0.4.1] - 2026-07-12

### Fixed
- only use Multiple sprite import mode for shared atlas regions, Single otherwise

## [0.4.0] - 2026-07-12

### Added
- convert tk2dUIScrollbar roots without their own sprite (background/handle/fill wiring, SendMessage relay, dead wrapper cleanup)

## [0.3.0] - 2026-07-12

### Added
- Toggle uses SpriteSwap for hover feedback, fully cleans up nested checkbox wrapper hierarchy

## [0.2.3] - 2026-07-12

### Fixed
- remove leftover MeshRenderer/MeshFilter after TextMeshPro to TextMeshProUGUI conversion

## [0.2.2] - 2026-07-12

### Fixed
- consolidate all menu items under Tools/Wagenheimer/Tk2d Porter

## [0.2.1] - 2026-07-12

### Fixed
- add missing .meta files (Unity ignores immutable package folders without them)

## [0.2.0] - 2026-07-12

### Added
- initial release of Tk2d Porter

## [0.1.0] - 2026-07-11

### Added
- Initial release: `Tk2dImageConverter` (tk2d sprites/buttons/toggles/scrollbars → uGUI Image/Button/Toggle/Slider), `Tk2dSpriteRendererConverter` (tk2d sprites → native `SpriteRenderer` for world-space/gameplay use), `Tk2dConversionRouter` (auto-detect which of the two to use based on whether the object sits under a `Canvas`), `TextMeshProToUGUIConverter` (per-component Inspector button), `Tk2dScriptFieldMigrator` (mechanical C# field-declaration migration with a dry-run report), `UIEventRelay` (preserves tk2d's `SendMessage`-based click/toggle wiring on the new UGUI components), and an auto-updater (`UpdateChecker`/`UpdateAvailableWindow`).
