# Contributing to OSE Blueprints Assembler

See [ARCHITECTURE.md](ARCHITECTURE.md) for system overview and ADRs before making changes.

---

## Adding a New Machine Package

1. Create folder: `Assets/_Project/Data/Packages/<machineId>/`
2. Add `machine.json` — use an existing package as template
3. Add mesh assets under the same folder (or reference shared meshes)
4. Open Unity Editor — `MachinePackageLoader` validates on load; check Console for errors
5. **Do not edit** `Assets/StreamingAssets/` — the build pipeline syncs it automatically

Validation errors are structured: `[path] message (severity)`. Fix all `Error` severity issues before shipping.

---

## Adding a New Step Family

1. Add enum value to `StepFamily` in `StepFamily.cs`
2. Add profile value to `StepProfile` if needed
3. Create `FooStepHandler : IStepFamilyHandler` in `Assets/_Project/Scripts/UI/Root/`
4. Register it in `StepExecutionRouter.Initialize()`:
   ```csharp
   Register(StepFamily.Foo, new FooStepHandler(context));
   ```
5. Update `MachinePackageValidator` if the new family has required JSON fields
6. No other files need changes

---

## Adding a New Tool Profile

1. Add value to `StepProfile` enum
2. Add entry to `ToolProfileRegistry` with desired behavior flags
3. Reference the profile string in `machine.json` steps via `"profile": "foo"`
4. Add migration in `PackageSchemaMigrator` if renaming an existing profile

---

## Adding a New Runtime Event

1. Add a `readonly struct` to `RuntimeEvents.cs`:
   ```csharp
   public readonly struct FooHappened
   {
       public readonly string Id;
       public FooHappened(string id) { Id = id; }
   }
   ```
2. Publish: `RuntimeEventBus.Publish(new FooHappened(id));`
3. Subscribe in `OnEnable`, unsubscribe in `OnDisable` — always both
4. Add a test in `RuntimeEventBusTests.cs` if the event has non-trivial logic

---

## Adding a New Service

1. Define an interface in `OSE.Core` (or the appropriate layer)
2. Register in `AppBootstrap.Awake()`:
   ```csharp
   ServiceRegistry.Register<IFooService>(new FooService());
   ```
3. Resolve in `OnEnable()` of consumers (not `Awake()`) — all services are registered by then
4. Add `ServiceRegistryTests` entry if the service has a required contract

---

## Code Standards

| Rule | Why |
|------|-----|
| Seal classes not designed for inheritance | Prevents accidental coupling |
| `internal` visibility by default for implementation classes | Minimizes public API surface |
| Subscribe in `OnEnable`, unsubscribe in `OnDisable` | Prevents event listener leaks |
| Use `TryGet` pattern for optional dependencies | Graceful degradation |
| Struct events only on `RuntimeEventBus` | No heap allocation in hot paths |
| `Time.realtimeSinceStartup` for cooldowns, not `Time.frameCount` | Frame-rate independent |
| Log with `OseLog` semantic methods, not `Debug.Log` | Structured, filterable output |
| Edit `machine.json` in `Assets/_Project/Data/Packages/` only | Build pipeline owns StreamingAssets |

---

## Branch Strategy

- `master` is the integration branch — always in a buildable state
- Feature branches: `feat/<short-slug>` (e.g. `feat/weld-pass-audio`)
- Bug fixes: `fix/<short-slug>`
- Content-only changes (machine.json, GLB): `content/<packageId>-<description>`
- Keep branches short-lived; merge or rebase within a few days

---

## Pull Request Conventions

- Title format: `feat:` / `fix:` / `refactor:` / `content:` prefix, imperative mood, ≤ 72 chars
- Link any related issue or design note in the PR description
- One logical change per PR — split content edits from code changes
- Do not force-push to `master`
- Resolve all conversations before merging

---

## Tests

Tests live under `Assets/_Tests/`. Each test file targets a specific class or contract:

| Test file | What it covers |
|-----------|---------------|
| `RuntimeEventBusTests.cs` | Subscribe, publish, unsubscribe, snapshot-before-invoke |
| `ServiceRegistryTests.cs` | Register, Get, TryGet, Unregister contracts |
| `MachinePackageValidatorTests.cs` | Validation pass execution, IPackageValidationPass extension |
| `StepPreflightValidatorTests.cs` | Pre-flight checks, StepReadinessChecked event |

**Requirements for new code:**
- Any new `RuntimeEventBus` event with non-trivial dispatch logic → add a `RuntimeEventBusTests` case
- Any new `IPackageValidationPass` → add a validator test
- Any new FSM state in `SessionLifecycle` → add a reachability and stuck-state test
- Tests must be self-contained: no shared static state, no editor dependencies

---

## machine.json Authoring

**Always edit the authoring copy, never StreamingAssets:**

```
Assets/_Project/Data/Packages/<packageId>/machine.json   ← edit here
Assets/StreamingAssets/MachinePackages/<packageId>/machine.json  ← build pipeline only
```

After any bulk hand-edit of position/rotation/scale values, normalize float precision:

> Unity menu: **OSE › Package Builder › Normalize Float Precision (All Packages)**

This enforces 4 decimal places (0.1 mm / 0.01°) and keeps diffs readable.

---

## Before Submitting

- [ ] Zero compiler warnings introduced
- [ ] `MachinePackageValidator` passes on affected packages (check Console on Editor load)
- [ ] Event subscriptions have matching unsubscriptions
- [ ] New public methods have XML `<summary>` docs
- [ ] No `Time.frameCount` comparisons introduced (use elapsed time)
- [ ] No magic numbers — extract to named constants
- [ ] Float precision normalized after any bulk machine.json edits
- [ ] Tests added or updated for new events, FSM states, or validation passes
