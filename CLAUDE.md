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
