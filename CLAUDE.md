# CLAUDE.md — Agent Instructions for OSE Blueprints Assembler

## Machine Package Content (machine.json)

**CRITICAL**: In the Unity Editor, machine.json is loaded from the **authoring folder**, NOT StreamingAssets:

- **Authoring (editor reads this):** `Assets/_Project/Data/Packages/<packageId>/machine.json`
- **StreamingAssets (runtime/build only):** `Assets/StreamingAssets/MachinePackages/<packageId>/machine.json`

When editing machine.json content (steps, parts, tools, targets, etc.), **always edit the authoring copy** under `Assets/_Project/Data/Packages/`. The StreamingAssets copy is for builds only and is synced during the build process. See `MachinePackageLoader.BuildMachineJsonPath()` for the resolution logic.

**Do NOT manually copy or sync to StreamingAssets.** Only edit the authoring copy. The build pipeline handles the rest.
