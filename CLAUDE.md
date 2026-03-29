# CLAUDE.md — Agent Instructions for OSE Blueprints Assembler

## Machine Package Content (machine.json)

**CRITICAL**: In the Unity Editor, machine.json is loaded from the **authoring folder**, NOT StreamingAssets:

- **Authoring (editor reads this):** `Assets/_Project/Data/Packages/<packageId>/machine.json`
- **StreamingAssets (runtime/build only):** `Assets/StreamingAssets/MachinePackages/<packageId>/machine.json`

When editing machine.json content (steps, parts, tools, targets, etc.), **always edit the authoring copy** under `Assets/_Project/Data/Packages/`. The StreamingAssets copy is for builds only and is synced during the build process. See `MachinePackageLoader.BuildMachineJsonPath()` for the resolution logic.

**Do NOT manually copy or sync to StreamingAssets.** Only edit the authoring copy. The build pipeline handles the rest.

## machine.json Authoring Conventions

**Float precision**: Position, rotation, and scale values are stored at 4 decimal places maximum (0.1 mm / 0.01° — sufficient for assembly training). The `PackageJsonUtils.WritePreviewConfig()` and `TryInjectBlock()` save paths enforce this automatically. After any bulk hand-edit, run `OSE / Package Builder / Normalize Float Precision (All Packages)` to normalize the entire file.

**Hint reuse**: Define one `HintDefinition` per *type of action*, not one per step instance. Hints that reference a specific `targetId` or `partId` for runtime highlighting must stay unique. Hints with no such context (generic reminders, safety notes) should be defined once and referenced by multiple steps via `hintIds`. Duplicate hint definitions with identical messages and no unique `targetId`/`partId` are a smell — consolidate them.
