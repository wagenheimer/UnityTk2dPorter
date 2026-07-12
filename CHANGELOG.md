# Changelog

All notable changes to this project will be documented in this file.

## [0.1.0] - 2026-07-11

### Added
- Initial release: `Tk2dImageConverter` (tk2d sprites/buttons/toggles/scrollbars → uGUI Image/Button/Toggle/Slider), `Tk2dSpriteRendererConverter` (tk2d sprites → native `SpriteRenderer` for world-space/gameplay use), `Tk2dConversionRouter` (auto-detect which of the two to use based on whether the object sits under a `Canvas`), `TextMeshProToUGUIConverter` (per-component Inspector button), `Tk2dScriptFieldMigrator` (mechanical C# field-declaration migration with a dry-run report), `UIEventRelay` (preserves tk2d's `SendMessage`-based click/toggle wiring on the new UGUI components), and an auto-updater (`UpdateChecker`/`UpdateAvailableWindow`).
