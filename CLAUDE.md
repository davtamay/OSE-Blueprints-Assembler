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

## Agent Rules for machine.json Edits

After ANY machine.json edit, the agent MUST follow these rules:

**No orphans:** Every new part, target, or hint must be referenced by at least one step. Do not leave dead definitions. If a step is removed, remove any parts, targets, and hints that become unreferenced.

**No empty arrays or nulls:** Omit optional fields (`requiredPartIds`, `hintIds`, `targetIds`, `optionalPartIds`, `relevantToolIds`, `effectTriggerIds`, `validationRuleIds`, `requiredToolActions`, `eventTags`) entirely rather than writing `[]` or `null`. The runtime defaults are safe.

**No template duplication:** If a part uses `templateId`, do not repeat `category`, `material`, or `assetRef` when the value matches the template. The runtime inherits these from the template.

**Use `family`, not `completionType`:** Always set `family` on new steps. Do not add `completionType` — it is a legacy fallback only. Omit `profile` rather than writing `""` (absence = family default).

**Reuse hints:** Before creating a new hint, search existing hints for one with the same message or intent. Only create a unique hint when it carries a specific `targetId` or `partId` for runtime highlighting.

**Payload-first for new steps:** New steps should use `guidance`, `validation`, `feedback`, `reinforcement`, and `difficulty` payloads rather than flat fields (`instructionText`, `hintIds`, `validationRuleIds`, etc.). Flat fields are legacy — never add them to new content.

**Validate after edits:** After bulk edits, tell the user to run `OSE > Validate All Packages` in Unity. The pre-play validator blocks Play mode on errors automatically.

**Staging positions go in `parts[].stagingPose`, not in `previewConfig`:** The agent-authored source of truth for where a part floats before the trainee places it is `parts[].stagingPose` (position, rotation, scale, color). `previewConfig.partPlacements[].startPosition` is derived — baked by `MachinePackageNormalizer.BakeStagingPoses()` at load time. Never edit `startPosition` in previewConfig directly.

## Machine Package File Architecture (Phase 2)

When the per-assembly file split is in place, each machine package has this layout:

```
Assets/_Project/Data/Packages/{packageId}/
  machine.json          ← metadata only: name, version, entryAssemblyIds, schemaVersion
  shared.json           ← tools, partTemplates, global hints (no targetId/partId), global validationRules
  assemblies/           ← one JSON per assembly, self-contained
    frame.json
    y_left.json
    x_axis.json
    ...
  preview_config.json   ← TTAW/Blender-generated ONLY — agents never open this
  assets/
    parts/*.glb
    tools/*.glb
```

**Agent routing rules:**

| What you need to do | File to read | File to write |
|---------------------|-------------|--------------|
| Edit steps for an assembly | `assemblies/{assemblyId}.json` | Same file |
| Define a new part (or set `stagingPose`) | `assemblies/{firstUseAssemblyId}.json` | Same file |
| Define a tool or partTemplate | `shared.json` | `shared.json` |
| Edit a global hint (no `targetId`/`partId`) | `shared.json` | `shared.json` |
| Read machine name/version | `machine.json` | `machine.json` |
| Read assembled/step poses | `preview_config.json` (read-only) | **Never write** |

**Never re-define a part in a second assembly file.** A part is defined once in the assembly where it is first physically assembled; later assemblies reference it by `id` only. The loader merges all files — all IDs are globally visible.

**`preview_config.json` is 100% TTAW/Blender-generated.** If a part's start position needs changing, edit `parts[].stagingPose` in the assembly file. The normalizer bakes it into previewConfig at load time.

**Backward-compatible:** If `assemblies/` is absent, the loader falls back to reading a single monolithic `machine.json`. No other system needs to change.

## Agent Rules for Bug Fixes

When fixing any bug, the agent MUST also prevent that class of bug from recurring:

**Eliminate the root cause, not just the symptom.** If the same mistake could be made in another call site, file, or future feature, fix the architecture so it can't happen. Examples:
- If multiple callers need the same derived data → bake it at load time in `MachinePackageNormalizer` so every caller gets the right answer automatically.
- If cleanup is missed during a lifecycle transition → add a centralized cleanup hook (e.g. `playModeStateChanged`) rather than patching individual call sites.
- If a visual state isn't applied because a code path skips a step → ensure the authoritative method always runs, not just in some paths.

**Never scatter the same derivation logic across callers.** If you find yourself writing the same resolution/fallback in multiple places, centralize it into a single method or normalizer pass. Then update all callers to use the single source of truth.

**Add structural prevention, not just a point fix.** After fixing a bug, ask: "Could a developer adding a new feature hit this same issue?" If yes, make the fix structural — change the API, add a normalizer pass, or update a base method so the new feature would work correctly by default.

**Learn from every error.** After fixing a bug, save the lesson to memory so the same mistake is never repeated in future conversations. Record what went wrong, why it was hard to find, and what pattern to follow going forward. Each bug is a signal — use it to permanently improve how you approach this codebase.
