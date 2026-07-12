# Tk2d Porter — Agent Notes

UPM package. Repo root = package root, installed via git URL, no wrapper Unity project.

## Structure

- `Runtime/UIEventRelay.cs` — the only runtime `MonoBehaviour`. Bridges `Button.onClick`/
  `Toggle.onValueChanged` (UnityEvent, wired via `UnityEditor.Events.UnityEventTools.AddPersistentListener`
  at conversion time) to a `GameObject.SendMessage(methodName, ...)` call, preserving tk2d's
  reflection-based click/toggle dispatch (`tk2dUIItem.sendMessageTarget` +
  `SendMessageOnReleaseMethodName`, `tk2dUIToggleButton.SendMessageTarget` +
  `SendMessageOnToggleMethodName`) without requiring every consumer script to be rewritten by hand.
- `Editor/` (root, **no asmdef** — see "Why Editor/ has no asmdef" below) —
  `Tk2dSpriteAssetUtility.cs` (shared sprite-asset resolution/creation, used by both converters),
  `Tk2dToUGUIConverter.cs` (tk2d → Image/Button/Toggle/Slider), `Tk2dToSpriteRendererConverter.cs`
  (tk2d → native `SpriteRenderer`), `Tk2dConversionRouter.cs` (auto-detect menu, delegates to one of
  the two above based on `GetComponentInParent<Canvas>()`), `TextMeshProToUGUIConverter.cs`
  (per-component custom Inspector button), `Tk2dScriptFieldMigrator.cs` (regex-based C# field-type
  migrator, dry-run + apply, writes a Markdown report to `Logs/`).
- `Editor/UpdateChecker/` (**has its own isolated asmdef**) — `UpdateChecker.cs` +
  `UpdateAvailableWindow.cs`, copy-pasted-and-renamed from the sibling packages
  (UnityRateControl/UnityCloudSave/UnityNativeSocial/UnityRewiredHelper), not a shared library. If
  you fix a bug here, port the fix to the other `wagenheimer/Unity*` repos by hand.

## Conventions & Gotchas

- **Why `Editor/` (root) has NO `.asmdef`, unlike every sibling `wagenheimer/Unity*` package.**
  2D Toolkit (`Assets/TK2DROOT`) ships as raw, loose `.cs` source with no `.asmdef` of its own — it
  compiles directly into the consuming project's default assembly (`Assembly-CSharp-Editor` for
  Editor scripts). Unlike Rewired (see `UnityRewiredHelper/AGENTS.md`), which auto-references into
  *every* assembly because it ships as a precompiled DLL with `isExplicitlyReferenced: 0`, loose
  `.cs` files with no asmdef do **not** auto-reference into an isolated package asmdef — an isolated
  asmdef simply cannot see `tk2dSprite`, `tk2dUIItem`, etc. Since every file directly under `Editor/`
  needs compile-time access to tk2d types, that folder is intentionally left WITHOUT an asmdef so it
  falls into the consumer's default assembly alongside TK2DROOT itself. Do not add one "for
  consistency" with the other packages — it will break the build for any consumer whose TK2DROOT
  isn't separately wrapped in its own named asmdef (which essentially none are, since tk2d predates
  asmdefs).
- **Why `Editor/UpdateChecker/` DOES have its own asmdef, nested one level down.** `UpdateChecker.cs`
  uses `UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(UpdateChecker).Assembly)` to
  discover the installed package version — this only resolves correctly if that type's assembly is
  recognized by the Package Manager as belonging to this package, which requires an isolated asmdef.
  Unity's asmdef folder-scoping cascades to subfolders unless a subfolder declares its own asmdef —
  so nesting `UpdateChecker/` with its own `.asmdef` carves it out of the (asmdef-less) `Editor/`
  root, giving it isolation without forcing the tk2d-aware converters into the same boundary.
- `Runtime/` has its own isolated asmdef too — `UIEventRelay.cs` has zero tk2d dependency, so there's
  no reason not to isolate it.
- All comments, logs, README/CHANGELOG content, and commit messages in this repo are in English —
  even though the primary maintainer's day-to-day project work (NordStormSolitaire, where this code
  originated) is in Portuguese. This is a deliberate, explicit convention for every public
  `wagenheimer/Unity*` repo — see [[feedback-opensource-english]] if you have access to that memory.
- Version bump/tag/release is fully automated by `.github/workflows/bump-version.yml` on every push
  to `main` — do not manually edit `package.json`'s `version` or add CHANGELOG entries by hand for
  released versions; the workflow derives the bump type from Conventional Commit prefixes (`feat:` →
  minor, `fix:`/other → patch, `!`/`BREAKING CHANGE` → major).
