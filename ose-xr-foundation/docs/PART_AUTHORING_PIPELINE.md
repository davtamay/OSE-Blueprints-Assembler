# PART_AUTHORING_PIPELINE.md

## Purpose

This document defines how content should be authored for the XR assembly training application.

Its goal is to turn raw source material into clean, data-driven machine packages without letting the workflow become chaotic, inconsistent, or tightly coupled to scene-specific logic.

This pipeline covers how to move from:

- Open Source Ecology blueprint material
- reference images and documentation
- part list definition
- tool list definition
- asset creation
- metadata authoring
- step authoring
- validation setup
- effects tagging
- challenge hooks
- machine package export

into a runtime-loadable machine experience.

This file should be used together with:

- `TECH_STACK.md`
- `docs/ARCHITECTURE.md`
- `docs/CONTENT_MODEL.md`
- `docs/ASSEMBLY_RUNTIME.md`
- `docs/UNITY_PROJECT_STRUCTURE.md`
- `docs/IMPLEMENTATION_CHECKLIST.md`
- `docs/VERTICAL_SLICE_SPEC.md`
- `docs/MACHINE_SELECTION_RESEARCH.md`
- `UI_ARCHITECTURE.md`
- `TASK_EXECUTION_PROTOCOL.md`

The canonical physical-fidelity requirements for dimensions, placement, orientation,
appearance, and AI asset prompting live in `docs/CONTENT_MODEL.md` under
`Physical Fidelity Standard`.

---

# 1. Core Principle

Content must be **data-driven**.

The runtime should interpret structured machine packages.

The runtime should not depend on hand-built scene logic for every machine.

That means content creation must produce:

- structured definitions
- reusable assets
- explicit metadata
- explicit steps
- explicit validation rules
- explicit optional effects
- explicit optional challenge metadata

not just a scene that “looks right.”

---

# 2. Authoring Philosophy

The content pipeline must prioritize:

- clarity
- repeatability
- modularity
- educational usefulness
- stable runtime integration
- scalability to many machines later

The first goal is not to perfectly recreate every engineering detail.

The first goal is to create **clear, teachable, interactive assembly content** that is structurally faithful enough to be educational and practically useful.

---

# 3. High-Level Pipeline Overview

The content pipeline should follow this sequence:

1. choose machine scope
2. gather and organize source material
3. define assembly teaching scope
4. extract part and tool lists
5. define subassemblies
6. create or acquire assets
7. author metadata
8. author step flow
9. define validation rules
10. tag optional effects and process cues
11. add challenge and multiplayer-ready metadata where useful
12. build machine package
13. validate in runtime
14. iterate based on usability and teaching quality

Do not skip directly from blueprints to scene assembly.

---

# 4. Content Scope Selection

Before making assets or writing steps, define the scope.

## 4.1 Scope Rule

Always choose the smallest meaningful scope first.

For early work, that means:

- tutorial micro-build first
- then one authentic OSE-aligned subassembly
- not a full machine
- not a giant system
- not a house-scale workflow

## 4.2 Scope Questions

Before authoring starts, answer:

- What exact assembly are we teaching?
- What is the learner supposed to understand by the end?
- What is the smallest complete teachable unit?
- How many parts are truly needed?
- Which tools matter for this slice?
- Which steps are essential?
- Does this slice support aha moments?

If those are unclear, the scope is still too vague.

---

# 5. Source Material Gathering

Content authoring should begin by collecting and organizing source material.

## 5.1 Source Types

Possible sources:

- OSE blueprint drawings
- OSE build docs
- OSE part lists
- photos of completed assemblies
- sketches or diagrams
- engineering approximations where source material is incomplete
- educator notes or simplifications

## 5.2 Source Package Folder

Each machine/subassembly should have a source folder or research record containing:

- reference images
- blueprint captures
- notes about unclear geometry
- notes about inferred dimensions
- notes about assembly order assumptions
- notes about missing information
- tool assumptions
- safety notes if relevant later

## 5.3 Source Interpretation Rule

Be honest about uncertainty.

If source material is incomplete:

- document assumptions
- simplify carefully
- do not silently invent critical mechanical relationships without noting them

---

# 6. Educational Framing Before Modeling

Before modeling, define what the learner should learn.

## 6.1 Learning Goal Template

For each authored assembly, define:

- what the user is building
- why it matters structurally
- what parts they should understand
- what tool knowledge they should gain
- what order or constraint matters
- what the aha moment is

## 6.2 Why This Matters

Without educational framing, authors often produce pretty assets but weak learning sequences.

The content pipeline must produce teachable content, not just geometry.

---

# 7. Part and Tool Extraction

After scope and source material are clear, extract the part and tool list.

## 7.1 Part Extraction

For each part, identify:

- unique id
- display name
- category
- material
- structural purpose
- geometry requirements
- quantity
- whether it can be physically substituted
- any required orientation knowledge
- search terms if practical

## 7.2 Tool Extraction

For each tool, identify:

- unique id
- name
- category
- purpose
- whether it is required or optional
- search terms
- optional safety note
- whether tool use is only informational or actually part of a process step

## 7.3 Extraction Rule

If two parts look similar but serve different roles, do not collapse them without good reason.

Part identity matters for instruction and validation.

---

# 8. Subassembly Definition

Before step authoring, group the content into meaningful subassemblies.

## 8.1 Why Subassemblies Matter

Subassemblies help:

- reduce cognitive load
- create milestones
- support reuse
- support scalable machine growth
- create clearer aha moments

## 8.2 Good Subassembly Characteristics

A good subassembly is:

- structurally meaningful
- teachable in a short sequence
- small enough to validate
- large enough to feel like progress

## 8.3 Rule

Do not define subassemblies purely by file organization.

Define them by instructional and structural meaning.

---

# 9. Asset Creation Pipeline

Assets should be created only after the instructional structure is understood.

## 9.1 Asset Sources

Possible sources include:

- Blender-authored geometry
- simplified CAD conversions
- hand-authored meshes
- Hyperhuman-generated assets where appropriate
- primitives for early vertical-slice testing

## 9.2 Asset Authoring Rules

Assets should be:

- lightweight
- web-friendly
- readable in instructional contexts
- simplified where necessary for clarity
- separate from step logic
- reusable across steps and packages where possible

## 9.3 Fidelity Rule

Do not over-model details that do not help the learner.

Instructional clarity is more important than maximum realism in the first slices.

## 9.4 Recommended Export Formats

Preferred model formats:

- GLB
- glTF

Preferred texture compression path:

- KTX2
- Basis Universal

## 9.5 Asset Naming Rule

Asset names should clearly expose what they are.

Examples:

- `powercube_frame_plate_a.glb`
- `powercube_corner_bracket_b.glb`
- `m8_hex_bolt.glb`

Avoid generic names like:

- `part1`
- `newmesh`
- `final_final`

---

# 10. Asset Simplification and Variants

Some parts may need multiple visual forms.

## 10.1 Useful Variants

Possible variants:

- normal assembly view
- ghost placement preview
- highlighted inspection view
- low-end simplified version if needed later

## 10.2 Rule

Visual variants should still map back to the same part identity in content.

Do not let multiple visual representations create identity confusion in the runtime.

---

# 11. Metadata Authoring

Once parts and tools are defined, author metadata.

## 11.1 Part Metadata

Each part should support:

- name
- category
- material
- function
- structural role
- search terms
- associated tool(s)
- quantity
- educational note
- physical substitution allowed flag

## 11.2 Tool Metadata

Each tool should support:

- name
- purpose
- usage notes
- optional search terms
- optional safety notes

## 11.3 Metadata Rule

Metadata should be concise, useful, and practical.

Avoid empty filler text.

If the user reads it, it should help them understand the build.

---

# 12. Step Authoring Pipeline

Step authoring is one of the most important parts of the pipeline.

## 12.1 Step Design Questions

For each step, define:

- what is the action
- what part(s) are involved
- what tool(s) are relevant
- what target(s) matter
- what order dependency exists
- what hint should appear if the user struggles
- what completion mode is used
- what the learner should understand after this step

## 12.2 Step Granularity Rule

Steps should not be:

- so large that the user is confused
- so tiny that the flow becomes tedious

A step should represent one coherent learner action.

## 12.3 Step Completion Modes

Use content-driven completion modes such as:

- virtual-only
- physical-only
- virtual-or-physical
- confirmation-only
- multi-part-required

These should come from the content model, not scene hacks.

---

# 13. Validation Authoring

Validation rules must be authored deliberately.

## 13.1 Validation Inputs

For each relevant step, define:

- target anchor
- position tolerance
- rotation tolerance
- correct part identity
- dependency requirements
- whether auto-snap is allowed
- whether leniency changes by mode

## 13.2 Validation Rule

Validation should reinforce learning.

It should not feel random or unfair.

If a part is rejected, the runtime should be able to explain why.

That requires good authored validation data.

---

# 14. Hint Authoring

Hints are part of content authoring, not an afterthought.

## 14.1 Hint Types

Possible hint types:

- highlight target
- ghost placement
- explanatory note
- directional cue
- “why this matters” reinforcement
- tool reminder

## 14.2 Hint Rule

Hints should reduce confusion without solving the entire problem automatically unless the mode allows it.

---

# 15. Effects Authoring

Some steps may benefit from process or feedback effects.

## 15.1 Effect Types

Possible effects include:

- placement confirmation glow
- success pulse
- welding sparks
- heat glow
- torch/fire cues
- dust
- process transition cues
- milestone celebration

## 15.2 Effects Rule

Effects should be tagged in content only when they improve instruction or clarity.

Do not add effects just because they are visually impressive.

## 15.3 Authoring Rule

Effects should be referenced by effect definitions or ids, not embedded as scene-only assumptions.

---

# 16. Challenge Metadata Authoring

The first content slices do not need full challenge systems, but authoring should stay open for them.

## 16.1 Useful Challenge Metadata

Possible fields:

- timer enabled
- retries counted
- hint usage counted
- strict validation mode support
- score payload compatibility

## 16.2 Rule

Challenge metadata should remain optional and should not pollute the core educational content.

---

# 17. Multiplayer-Ready Content Rules

Content should remain open for future multiplayer.

## 17.1 What This Means

Author content so important actions can be represented explicitly, such as:

- which step is active
- which part is being manipulated
- which placement is complete
- which hint was requested
- which physical substitution was confirmed

## 17.2 Rule

Do not hide critical authored content assumptions only inside UI text or only inside scene arrangement.

---

# 18. Machine Package Assembly

After parts, tools, metadata, steps, validation, and effects are authored, assemble the runtime package.

## 18.1 Package Contents

A machine package should include structured data such as:

- machine manifest
- assemblies
- subassemblies
- parts
- tools
- steps
- validation data
- optional effects data
- optional challenge metadata
- asset references

## 18.2 Package Rule

The package should be portable and loadable without machine-specific code changes.

That is one of the core goals of the architecture.

---

# 19. Recommended Folder Structure for Source Authoring

A source-authoring structure might look like this:

```text
ContentAuthoring/
  power_cube_frame_corner/
    Sources/
    Notes/
    Parts/
    Tools/
    Steps/
    Effects/
    Exports/
```

Inside Unity, the project-facing structure should still respect the official project layout, but the authoring mindset should remain organized like this.

---

# 20. Runtime Validation Pass

Before a machine package is accepted, validate it in runtime.

## 20.1 Required Checks

Check at minimum:

- package loads
- assets resolve
- metadata displays correctly
- step flow works
- validation behaves correctly
- hints appear correctly
- optional physical substitution path works
- completion summary works
- no content-specific hacks were required in generic runtime code

## 20.2 Teaching Check

Also ask:

- does the learner understand what they are doing?
- does the sequence create aha moments?
- is the part/tool information useful?
- is the assembly sequence too long or too granular?
- does the content feel satisfying to complete?

---

# 21. Iteration Loop

Content authoring should be iterative.

Recommended loop:

1. rough scope
2. rough content package
3. runtime test
4. simplify confusing areas
5. refine assets or metadata
6. refine step flow
7. refine hints and effects
8. validate again

Do not assume the first authored version is the best teaching version.

---

# 22. Recommended First Pipeline Target

For the first authentic slice, the recommended target remains:

- a small **Power Cube-aligned subassembly**
- likely a **frame corner or bracket-style subassembly**
- modest part count
- clear bolts / brackets / plate relationships
- simple tool story
- strong instructional clarity

This is the right place to prove the pipeline.

---

# 23. Authoring Roles and Responsibilities

Even if one person handles multiple roles, the pipeline should conceptually separate them.

## 23.1 Research Role

Owns:

- source gathering
- blueprint interpretation
- assumption notes

## 23.2 Content Design Role

Owns:

- educational framing
- subassembly definition
- step design
- hint design

## 23.3 Asset Role

Owns:

- mesh creation
- simplification
- export quality
- naming quality

## 23.4 Data Authoring Role

Owns:

- metadata
- validation definitions
- effect tags
- challenge metadata
- package assembly

Keeping these roles conceptually separate improves quality even for solo development.

---

# 24. Anti-Patterns to Avoid

Avoid these mistakes:

- building the machine only as a Unity scene with no package structure
- writing machine-specific logic into generic runtime systems
- modeling before understanding teaching goals
- overcomplicating the first authentic slice
- skipping metadata because “the model already shows it”
- authoring validation only by feel with no explicit data
- using effects without instructional purpose
- creating assets with messy naming and no identity mapping
- making the tutorial and authentic slices share content in confusing ways

---

# 25. Recommended First Implementation Order for the Pipeline

Use this order:

1. define machine scope
2. gather sources
3. write educational goals
4. extract parts and tools
5. define subassemblies
6. author rough content schema
7. create rough placeholder assets
8. author step flow
9. author validation
10. test in runtime
11. refine metadata and hints
12. refine assets
13. tag effects if justified
14. finalize machine package
15. validate and commit

This keeps the pipeline grounded in teaching and architecture, not only art production.

---

# 26. Validation Questions

Before approving authored content, ask:

- Is the scope small and teachable enough?
- Are the source assumptions documented?
- Are the parts and tools explicit?
- Are the steps clear and appropriately sized?
- Does validation reinforce learning?
- Are hints and effects justified?
- Is the package data-driven?
- Does it run without custom hacks?
- Does it remain open for future multiplayer and challenge features?
- Does it create practical understanding and aha moments?

If any of these are unclear, the content is not ready yet.

---

# 27. Final Guidance

The correct content authoring strategy is not:

“model something cool, drop it in Unity, and make it interactive somehow.”

The correct strategy is:

- start from scope and learning goals
- organize source material honestly
- define parts, tools, subassemblies, and steps clearly
- keep assets separate from instructional logic
- author metadata, validation, hints, and effects deliberately
- package content in a reusable data-driven way
- validate in runtime
- iterate until the experience is both teachable and stable

That is how the content pipeline becomes scalable, reliable, and useful for the long-term vision of the project.

---

# 28. 3D Model Asset Pipeline — AI Generation to Scene

This section is the **complete, self-contained reference** for replacing placeholder
primitive GLBs with production-quality AI-generated 3D models. Another agent or human
should be able to follow this document end-to-end without additional context.

## 28.1 Overview

The pipeline has four sequential stages:

```
Generate → Import → Normalize Scale → Optimize (optional)
```

Each stage can be run independently. Unity Editor tools are under the **OSE** menu.

## 28.2 Prerequisites

Before starting, ensure the following are available:

1. **Rodin skill scripts** — Clone into the project:
   ```bash
   git clone --depth 1 https://github.com/deemostech/rodin3d-skills.git .github/skills/rodin3d-skills
   ```
2. **Python 3.10+** with `requests`: `pip install requests`
3. **API key** — Free: `vibecoding` (rate-limited, may hit INSUFFICIENT_FUND).
   Paid: https://hyper3d.ai/api-dashboard
   - PowerShell: `$env:HYPER3D_API_KEY = "vibecoding"`
   - Bash: `export HYPER3D_API_KEY=vibecoding`
4. **glTFast package** in Unity — `com.unity.cloud.gltfast` via Package Manager.
   Without this, GLB files import as `DefaultImporter` and won't render.
5. **(Optional) gltfpack** for Stage 4 optimization.
   Download from https://github.com/zeux/meshoptimizer/releases
   Place `gltfpack.exe` in the project's `Tools/` folder.

## 28.3 File Paths

```
Assets/_Project/Data/Packages/<package_id>/assets/parts/   ← Source GLBs (authoritative)
Assets/_Project/Data/Packages/<package_id>/assets/tools/   ← Source tool GLBs
Assets/StreamingAssets/MachinePackages/<package_id>/        ← Auto-synced mirror (do NOT manually maintain)
Assets/_Project/Scripts/Editor/PartDimensionCatalog.cs      ← Real-world part dimensions
Assets/_Project/Scripts/Editor/PackageModelNormalizer.cs     ← Scale normalization tool
Assets/_Project/Scripts/Editor/PackageModelOptimizer.cs      ← Optimization tool
```

## 28.4 Parts Manifest — Power Cube Placeholder GLBs to Replace

The `power_cube_frame` package has 22 placeholder GLBs that need replacement (17 parts + 5 tools).

### Parts (17 placeholders)

| Part ID | Rodin Prompt |
|---------|-------------|
| engine_mount_plate | "Flat rectangular steel mounting plate with four bolt holes near corners and center cutout for shaft, 3/8 inch thick, industrial gray, isometric view" |
| engine | IMAGE-TO-3D recommended. "Briggs and Stratton 28HP V-twin gasoline engine, air-cooled, pull-start and electric starter, output shaft left, red/black. Proportions approximately 18 inches wide, 14 inches tall, 16 inches deep (roughly 1.3:1:1.1 W:H:D ratio). No text, no logos, no humans." |
| pump_coupling | "Lovejoy L-type jaw coupling, two aluminum hubs with red polyurethane spider insert, industrial metallic finish" |
| hydraulic_pump | IMAGE-TO-3D recommended. "Dual-section hydraulic gear pump, cast iron body, two inlet/outlet ports, SAE A mounting flange, industrial gray" |
| reservoir | "Rectangular welded steel hydraulic oil reservoir 5-gallon, fill cap top, sight glass side, suction port bottom, return port side, painted black" |
| pressure_hose | "3/4 inch hydraulic hose steel braided, black rubber, JIC swivel fittings both ends, 3 feet, coiled slightly" |
| return_hose | "1 inch hydraulic return hose, rubber, JIC swivel fittings both ends, black, 3 feet" |
| oil_cooler | "Aluminum hydraulic oil cooler, fin-and-tube radiator style, rectangular, inlet/outlet ports one side, silver finish" |
| fuel_tank | "7-gallon welded steel fuel tank, roughly rectangular rounded edges, threaded fill neck top with cap, outlet nipple bottom, two mounting tabs, industrial gray" |
| fuel_line | "3/8 inch fuel-rated rubber hose 2 feet, black, inline fuel filter in middle, brass barb fittings both ends" |
| fuel_shutoff_valve | "Quarter-turn brass fuel shutoff valve, inline barb fittings both ends, red lever handle, 2 inches long" |
| battery | "Group 26 12V lead-acid automotive battery, black case, red positive and black negative terminals top, carrying handle" |
| battery_cables | "Pair battery cables red positive black negative, 2-gauge copper, 18 inches each, ring terminals crimped both ends" |
| key_switch | "4-position automotive ignition key switch, zinc alloy, round panel-mount with lock nut, key inserted, three terminals back" |
| choke_cable | "Universal engine choke cable, black T-handle knob, steel cable in black plastic sheath, 4 feet, Z-bend fitting" |
| throttle_cable | "Universal throttle cable, black lever/slider control handle, steel cable in black sheath, 4 feet" |
| pressure_gauge | "0-5000 PSI hydraulic pressure gauge, 2.5 inch diameter, stainless case, glycerin-filled, white face, 1/4 NPT bottom mount" |

### Tools (5 placeholders)

| Tool ID | Rodin Prompt |
|---------|-------------|
| tool_torque_wrench | "1/2 inch drive click-type torque wrench, chrome, 18 inches, micrometer scale handle" |
| tool_line_wrench | "3/4 inch flare-nut wrench, chrome vanadium, open-end with slot for hose, 8 inches" |
| tool_wire_crimper | "Wire stripper crimper combo tool, red/black rubber handles, multiple gauge notches, crimping die for ring terminals, 8 inches" |
| tool_socket_set | "3/8 inch drive ratchet with 10mm socket, chrome, quick-release button, knurled handle" |
| tool_multimeter | "Yellow digital multimeter like Fluke, LCD display, red/black probe cables, rotary selector, rubber boot" |

### OSE Reference Resources (for IMAGE-TO-3D)

- Build photos: https://wiki.opensourceecology.org/wiki/Power_Cube
- CAD files: https://wiki.opensourceecology.org/wiki/Power_Cube/CAD
- BOM: https://wiki.opensourceecology.org/wiki/Power_Cube_VII/Bill_of_Materials

## 28.5 Stage 1: Generate 3D Model

### Text-to-3D (most parts)

```bash
python .github/skills/rodin3d-skills/skills/rodin3d-skill/scripts/generate_3d_model.py \
  --prompt "PROMPT FROM TABLE ABOVE" \
  --geometry-file-format glb --quality medium --tier Regular \
  --output ./generated_models --api-key $HYPER3D_API_KEY
```

### Image-to-3D (engine, hydraulic_pump, oil_cooler)

For visually complex parts, download a reference photo from the OSE wiki first:

```bash
python .github/skills/rodin3d-skills/skills/rodin3d-skill/scripts/generate_3d_model.py \
  --image path/to/reference.jpg \
  --geometry-file-format glb --quality high --tier Detail \
  --output ./generated_models --api-key $HYPER3D_API_KEY
```

### Prompt Guidelines

- Include material and finish details from machine.json's `material` field
- Mention real-world dimensions when possible ("2.5 inch diameter", "3 feet long")
- **Include the aspect ratio or explicit proportions** so the AI generates a model
  whose shape matches real-world proportions *before* scale normalization.
  Example: "approximately 18 inches wide, 14 inches tall, and 16 inches deep" or
  "roughly 1.3:1:1.1 width-to-height-to-depth proportions." See `PartDimensionCatalog.cs`
  for the real-world W×H×D values and convert to the units the prompt uses.
- Always add: "No text, no logos, no humans. Clean industrial design."
- Use `Regular` tier for standard parts, `Detail` for complex parts with textures

> **Why proportions matter:** The normalizer applies a *uniform* scale factor (same
> x/y/z) so the model's largest axis matches the real-world largest axis. It preserves
> whatever aspect ratio the model was generated with. If the AI model is too tall and
> too narrow compared to reality, uniform scaling makes it the right *size* but the
> wrong *shape* — it looks squished or stretched. The fix is to tell the generator
> the correct proportions up front. See Section 28.16 for the verification workflow.

### Output Structure

Rodin outputs to a UUID subfolder:
```
generated_models/
  8757daeb-6f9a-4627-8ab1-0337730196ef/
    base_basic_pbr.glb        ← this is the model
    preview.webp               ← preview image (ignore)
```

## 28.6 Stage 2: Import GLB into Package

### Copy the file

```powershell
# Find the generated GLB (Rodin nests output in a UUID subfolder)
$glb = Get-ChildItem ".\generated_models\" -Recurse -Filter "*.glb" | Sort-Object LastWriteTime | Select-Object -Last 1

# Copy to the package, preserving the filename machine.json expects
Copy-Item $glb.FullName "Assets\_Project\Data\Packages\power_cube_frame\assets\parts\<part_id>.glb" -Force
```

For tools:
```powershell
Copy-Item $glb.FullName "Assets\_Project\Data\Packages\power_cube_frame\assets\tools\<tool_id>.glb" -Force
```

### Fix the .meta file (critical)

After copying, check if Unity assigned the correct importer:

```powershell
# Check the meta file
Get-Content "Assets\_Project\Data\Packages\power_cube_frame\assets\parts\<part_id>.glb.meta"
```

If it says `DefaultImporter`, delete it and let Unity regenerate with glTFast:

```powershell
Remove-Item "Assets\_Project\Data\Packages\power_cube_frame\assets\parts\<part_id>.glb.meta" -Force
```

The correct meta file should contain `ScriptedImporter` with the glTFast script GUID.

### Force reimport in Unity

After copying and fixing .meta: press **Ctrl+R** in Unity, or right-click the file → **Reimport**.

### Clear cached scene objects

The scene caches old GameObjects. The spawner reuses existing objects by name:
```csharp
Transform existing = _setup.PreviewRoot.Find(part.id);
if (existing != null) { _spawnedParts.Add(existing.gameObject); continue; }
```

**Fix:** In the Unity Hierarchy, find and delete the old `<part_id>` GameObject under
the Preview Root, then toggle the package in SessionDriver inspector to force a reload.

## 28.7 Stage 3: Normalize Scale

AI generators export models at arbitrary scales (typically 1-2m bounding box).
Real parts range from 1cm (cables) to 1.2m (tubes). The normalizer corrects this.

### How it works

1. Reads each GLB's binary header to extract the POSITION accessor min/max bounds
2. Computes native bounding box size
3. Looks up real-world dimensions from `PartDimensionCatalog.cs`
4. Computes: `uniformScale = targetMaxDimension / nativeMaxDimension`
5. Writes the scale to both `startScale` and `playScale` in machine.json's previewConfig

### Run it

**Unity menu:** `OSE → Normalize Package Model Scales`

This processes all packages at once. No parameters needed.

### Adding dimensions for new parts

If adding a new part not yet in the catalog, edit
`Assets/_Project/Scripts/Editor/PartDimensionCatalog.cs`:

```csharp
// In the Dimensions dictionary, add:
{ "my_new_part",  new Vector3(0.30f, 0.20f, 0.15f) },  // W x H x D in meters
```

All Power Cube parts and tools already have entries.

### Manual scale override

If the automatic scale doesn't look right, manually edit the part's placement in
machine.json → `previewConfig` → `partPlacements`:

```json
{
    "partId": "fuel_tank",
    "startScale": { "x": 0.16, "y": 0.16, "z": 0.16 },
    "playScale":  { "x": 0.16, "y": 0.16, "z": 0.16 }
}
```

## 28.8 Stage 4: Optimize (optional but recommended)

Optimization reduces file size and runtime memory while preserving visual fidelity.
Uses **gltfpack** (from the meshoptimizer project by Arseny Kapoulkine).

### Fidelity Impact by Preset

| Preset | Mesh Changes | Texture Changes | Visual Impact | Recommended For |
|--------|-------------|-----------------|---------------|------------------|
| **Recommended** | Meshopt compression (lossless) | None | **Zero visible difference** | All parts — default choice |
| **Smaller** | Meshopt compression (lossless) | KTX2/Basis compression | **Usually imperceptible**, but can distort UVs on fine-detail textures | Parts with simple/uniform textures only |
| **Aggressive** | + 50% triangle simplification | KTX2/Basis | Same UV risk as Smaller + minor geometry loss | Background/low-visibility parts |

> **WARNING — KTX2 Texture Compression (`-tc`) and UV Fidelity**
>
> The Smaller and Aggressive presets use KTX2/Basis texture compression, which can
> **distort UV mapping on models with fine-detail textures** — gauge markings, text,
> labels, dial numbers, etc. This was confirmed with the pressure_gauge model where
> `-tc` visibly shifted UV coordinates. Always **visually verify** after using these
> presets. If UVs look wrong, re-optimize with the Recommended preset (mesh-only).

### Why this is safe

- **Meshopt compression** (`EXT_meshopt_compression`) is fully **lossless** — the decoded
  mesh is bit-identical to the original. It just compresses the binary buffer for storage.
- All presets use **`-noq`** (no vertex quantization) to preserve the coordinate space.
  Without this flag, gltfpack remaps float coordinates to integers, breaking the scale normalizer.
- **KTX2/Basis textures** (Smaller/Aggressive only) use a GPU-native compressed format.
  Quality is comparable to high-quality JPEG for most textures, but **can distort UVs
  on models with fine detail** (see warning above).

### Running the optimizer

**Unity menu options:**
- `OSE → Optimize Package Models (Recommended — Mesh Compression)` — safe for all models
- `OSE → Optimize Package Models (Smaller — With KTX2 Textures ⚠️)` — verify UVs after
- `OSE → Optimize Package Models (Aggressive — Simplify + KTX2 ⚠️)` — verify UVs after

These process all GLBs in all packages. Files smaller than 10KB (placeholders) are skipped.

### Safety net

The original GLB is backed up to `{name}.glb.bak` before overwriting.
To revert an optimization: rename `.bak` back to `.glb`.

### What NOT to use

**Draco** (`KHR_draco_mesh_compression`): Requires CPU decompression at load time,
adding 100-500ms per model. Meshopt compression is decoded on the GPU — zero load-time penalty.

## 28.9 Complete Step-by-Step Walkthrough — Single Part

This is the exact sequence to follow for each part. Demonstrated with `battery`:

```powershell
# ── Step 1: Generate ──
python .github/skills/rodin3d-skills/skills/rodin3d-skill/scripts/generate_3d_model.py `
  --prompt "Group 26 12V lead-acid automotive battery, black case, red positive and black negative terminals top, carrying handle" `
  --geometry-file-format glb --quality medium --tier Regular `
  --output ./generated_models --api-key $env:HYPER3D_API_KEY

# ── Step 2: Find and copy the output ──
$glb = Get-ChildItem ".\generated_models\" -Recurse -Filter "*.glb" | Sort-Object LastWriteTime | Select-Object -Last 1
Write-Host "Generated: $($glb.FullName) ($($glb.Length) bytes)"
Copy-Item $glb.FullName "Assets\_Project\Data\Packages\power_cube_frame\assets\parts\battery.glb" -Force

# ── Step 3: Fix meta if needed ──
$meta = "Assets\_Project\Data\Packages\power_cube_frame\assets\parts\battery.glb.meta"
if (Test-Path $meta) {
    $content = Get-Content $meta -Raw
    if ($content -match "DefaultImporter") {
        Remove-Item $meta -Force
        Write-Host "Deleted DefaultImporter .meta — Unity will regenerate with glTFast"
    } else {
        Write-Host "Meta OK (ScriptedImporter)"
    }
}
```

Then in Unity:
1. Press **Ctrl+R** to refresh assets
2. In Hierarchy, find and delete old `battery` GameObject under Preview Root
3. Run **OSE → Normalize Package Model Scales**
4. (Optional) Run **OSE → Optimize Package Models (Recommended)**
5. Toggle the SessionDriver package dropdown to reload
6. Confirm the model looks correct

## 28.10 Batch Processing — All 22 Parts

To process all remaining placeholder parts at once:

### Generate all models
```powershell
$env:HYPER3D_API_KEY = "vibecoding"  # or your paid key

$parts = @{
    "engine_mount_plate" = "Flat rectangular steel mounting plate with four bolt holes near corners and center cutout for shaft, 3/8 inch thick, industrial gray, isometric view"
    "engine" = "Briggs and Stratton 28HP V-twin gasoline engine, air-cooled, pull-start and electric starter, output shaft left, red/black. Proportions approximately 18 inches wide, 14 inches tall, 16 inches deep. No text, no logos, no humans."
    "pump_coupling" = "Lovejoy L-type jaw coupling, two aluminum hubs with red polyurethane spider insert, industrial metallic finish"
    "hydraulic_pump" = "Dual-section hydraulic gear pump, cast iron body, two inlet/outlet ports, SAE A mounting flange, industrial gray"
    "reservoir" = "Rectangular welded steel hydraulic oil reservoir 5-gallon, fill cap top, sight glass side, suction port bottom, return port side, painted black"
    "pressure_hose" = "3/4 inch hydraulic hose steel braided, black rubber, JIC swivel fittings both ends, 3 feet, coiled slightly"
    "return_hose" = "1 inch hydraulic return hose, rubber, JIC swivel fittings both ends, black, 3 feet"
    "oil_cooler" = "Aluminum hydraulic oil cooler, fin-and-tube radiator style, rectangular, inlet/outlet ports one side, silver finish"
    "fuel_line" = "3/8 inch fuel-rated rubber hose 2 feet, black, inline fuel filter in middle, brass barb fittings both ends"
    "fuel_shutoff_valve" = "Quarter-turn brass fuel shutoff valve, inline barb fittings both ends, red lever handle, 2 inches long"
    "battery" = "Group 26 12V lead-acid automotive battery, black case, red positive and black negative terminals top, carrying handle"
    "battery_cables" = "Pair battery cables red positive black negative, 2-gauge copper, 18 inches each, ring terminals crimped both ends"
    "key_switch" = "4-position automotive ignition key switch, zinc alloy, round panel-mount with lock nut, key inserted, three terminals back"
    "choke_cable" = "Universal engine choke cable, black T-handle knob, steel cable in black plastic sheath, 4 feet, Z-bend fitting"
    "throttle_cable" = "Universal throttle cable, black lever/slider control handle, steel cable in black sheath, 4 feet"
    "pressure_gauge" = "0-5000 PSI hydraulic pressure gauge, 2.5 inch diameter, stainless case, glycerin-filled, white face, 1/4 NPT bottom mount"
}

# NOTE: fuel_tank already done — not in this list

foreach ($name in $parts.Keys) {
    Write-Host "=== Generating $name ==="
    python .github/skills/rodin3d-skills/skills/rodin3d-skill/scripts/generate_3d_model.py `
        --prompt $parts[$name] `
        --geometry-file-format glb --quality medium --tier Regular `
        --output .\generated_models --api-key $env:HYPER3D_API_KEY

    $src = Get-ChildItem ".\generated_models\" -Recurse -Filter "*.glb" | Sort-Object LastWriteTime | Select-Object -Last 1
    if ($src) {
        Copy-Item $src.FullName "Assets\_Project\Data\Packages\power_cube_frame\assets\parts\$name.glb" -Force
        Write-Host "Copied: $($src.Name) → $name.glb ($($src.Length) bytes)"
    } else {
        Write-Host "WARNING: No GLB found for $name"
    }
}

# Tools
$tools = @{
    "tool_torque_wrench" = "1/2 inch drive click-type torque wrench, chrome, 18 inches, micrometer scale handle"
    "tool_line_wrench" = "3/4 inch flare-nut wrench, chrome vanadium, open-end with slot for hose, 8 inches"
    "tool_wire_crimper" = "Wire stripper crimper combo tool, red/black rubber handles, multiple gauge notches, crimping die for ring terminals, 8 inches"
    "tool_socket_set" = "3/8 inch drive ratchet with 10mm socket, chrome, quick-release button, knurled handle"
    "tool_multimeter" = "Yellow digital multimeter like Fluke, LCD display, red/black probe cables, rotary selector, rubber boot"
}

foreach ($name in $tools.Keys) {
    Write-Host "=== Generating $name ==="
    python .github/skills/rodin3d-skills/skills/rodin3d-skill/scripts/generate_3d_model.py `
        --prompt $tools[$name] `
        --geometry-file-format glb --quality medium --tier Regular `
        --output .\generated_models --api-key $env:HYPER3D_API_KEY

    $src = Get-ChildItem ".\generated_models\" -Recurse -Filter "*.glb" | Sort-Object LastWriteTime | Select-Object -Last 1
    if ($src) {
        Copy-Item $src.FullName "Assets\_Project\Data\Packages\power_cube_frame\assets\tools\$name.glb" -Force
        Write-Host "Copied: $($src.Name) → $name.glb ($($src.Length) bytes)"
    } else {
        Write-Host "WARNING: No GLB found for $name"
    }
}
```

### Clean up and normalize

```powershell
# Delete all old DefaultImporter .meta files
Get-ChildItem "Assets\_Project\Data\Packages\power_cube_frame\assets\**\*.glb.meta" | ForEach-Object {
    $content = Get-Content $_.FullName -Raw
    if ($content -match "DefaultImporter") {
        Remove-Item $_.FullName -Force
        Write-Host "Deleted DefaultImporter meta: $($_.Name)"
    }
}
```

Then in Unity:
1. Press **Ctrl+R** to refresh all assets
2. Run **OSE → Normalize Package Model Scales**
3. (Optional) Run **OSE → Optimize Package Models (Recommended)**
4. In Hierarchy, delete all old part GameObjects under Preview Root
5. Toggle SessionDriver package dropdown to reload
6. Visually inspect all parts

## 28.11 Troubleshooting

| Symptom | Cause | Fix |
|---------|-------|-----|
| GLB shows nothing in Inspector | Missing glTFast package | Install `com.unity.cloud.gltfast` via Package Manager |
| `.meta` says `DefaultImporter` | Meta created before glTFast was installed | Delete the `.glb.meta` file, let Unity regenerate |
| Model too big/small in scene | Native model scale doesn't match real world | Run `OSE → Normalize Package Model Scales` |
| Old cube still showing | Scene has cached instance — spawner reuses by name | Delete the old GameObject from Hierarchy, reload package |
| gltfpack not found | Executable not on PATH or in Tools/ | Download from meshoptimizer releases, place in `Tools/` |
| Textures look blocky after optimize | KTX2 compression artifacts at low res | Use Recommended preset instead (no texture compression) |
| UVs distorted after optimize | KTX2 (`-tc`) shifts UV coords on fine-detail textures | Use Recommended preset; restore from `.glb.bak` |
| `INSUFFICIENT_FUND` from Rodin | Free API key rate limit hit | Wait or get paid key at hyper3d.ai/api-dashboard |
| Download link expired from Rodin | Rodin download URLs expire after ~10 min | Re-run the generation command |
| Blobby/unrecognizable shape | Text prompt insufficient for complex part | Use image-to-3D with OSE wiki reference photo |
| Model looks squished/stretched | AI-generated proportions don't match real-world aspect ratio | Re-generate with explicit proportions in the prompt (see Section 28.16). Check `PartDimensionCatalog.cs` for the W×H×D values and include them as inches/ratios in the prompt |
| Wrong orientation | Model rotated vs expected in scene | Edit `playRotation` quaternion in machine.json previewConfig |
| Part not in PartDimensionCatalog | New part without size entry | Add entry to `PartDimensionCatalog.cs`, rebuild |
| Normalizer skips a part | Part has no entry in PartDimensionCatalog | Same as above |

## 28.12 Quality Checklist — Per Part

After importing and normalizing, verify each part against:

- [ ] **Recognizable**: Part is identifiable as what it represents
- [ ] **Proportions**: Model's aspect ratio matches real-world shape (not squished or stretched). Run `verify_proportions.py` or compare visually against reference photos. See Section 28.16
- [ ] **Scale**: Appears proportional to other parts in the scene
- [ ] **Materials**: Colors/finish match the real-world part description
- [ ] **Orientation**: Fill necks, handles, ports face expected directions
- [ ] **No artifacts**: No floating geometry, inside-out faces, or obvious mesh errors
- [ ] **Inspector**: GLB shows glTFast properties when selected (not blank)
- [ ] **File size**: Reasonable (typically 2-15 MB per generated model, <5 MB after optimization)

## 28.13 Auto-Reload on GLB Change

When a GLB file is added or replaced in a package folder, the editor automatically:

1. **Syncs to StreamingAssets** — `PackageAssetPostprocessor` calls `PackageSyncTool.Sync()`
   after detecting any GLB import under `Assets/_Project/Data/Packages/`.
2. **Refreshes the SessionDriver** — If the active `SessionDriver` in the scene is
   showing the same package, it triggers `NotifyPackageContentChanged(packageId)` which
   reloads the edit-mode preview from StreamingAssets.

This means you can replace a placeholder GLB, press Ctrl+R in Unity, and the scene
preview updates automatically — no manual sync or package toggle needed.

## 28.14 OSE Wiki Resources for Better Prompts

The OSE wiki provides reference material that can improve 3D generation quality,
especially for visually complex parts that benefit from image-to-3D:

### Useful Wiki Pages

| Resource | URL | Value |
|----------|-----|-------|
| Main Power Cube page | https://wiki.opensourceecology.org/wiki/Power_Cube | Overview, build photos, assembly video links |
| Power Cube VII BOM | https://wiki.opensourceecology.org/wiki/Power_Cube_VII/Bill_of_Materials | PDF BOM (linked) |
| Power Cube v17.08 | https://wiki.opensourceecology.org/wiki/Power_Cube_v17.08 | CAD modules, hydraulic specs, FreeCAD part files |
| Power Cube Library | https://wiki.opensourceecology.org/wiki/Power_Cube_Library | FreeCAD CAD files for engine, pump, coupler, cooler, fan, filter, reservoir, battery |
| Design Guide | https://wiki.opensourceecology.org/wiki/Power_Cube_Design_Guide | Motor→pump→coupler selection logic |
| CAD files | https://wiki.opensourceecology.org/wiki/Power_Cube/CAD | CAD downloads |

### What the Wiki Provides vs. What It Doesn't

**Available:**
- Build photos and assembly videos (good for image-to-3D reference)
- FreeCAD part files (engine as `Xp16hp.FCStd`, hydraulic pump, coupler, cooler, etc.)
- General specs: 28HP Briggs & Stratton V-twin, 2-stage 28/7 GPM pump, ISO 7241-1 A quick couplers
- Hydraulic specifications: 3/4" hex nipple, 1" hose barb fittings
- Supplier info for couplings ($32) and oil coolers ($208)

**Not available inline (broken links or in PDFs only):**
- Detailed BOM with exact manufacturer part numbers
- Precise per-part physical dimensions
- The main BOM link is noted as broken on the wiki itself

### Recommendation

For the 15 remaining placeholder parts, the existing Rodin text prompts in Section 28.4
are already well-crafted. Where text-to-3D produces unrecognizable shapes for complex
parts (engine, hydraulic pump, oil cooler), use **image-to-3D** with reference photos
from the OSE wiki build galleries or the FreeCAD CAD renders as input images.

## 28.15 3D Asset Strategy — Three Tiers

Not every part is a good fit for text-to-3D generation. Parts fall into three tiers
based on geometric complexity and how well AI text prompts describe them.

### Tier 1: Text-to-3D (Rodin text prompt pipeline)

Best for parts with **well-known, recognizable shapes** that AI models have seen in
training data. Simple geometry or common industrial hardware.

| Part | Why text works |
|------|---------------|
| `base_tube_long` | Simple square steel tube — trivial geometry |
| `base_tube_short` | Same tube, shorter length |
| `vertical_post` | Same tube, vertical orientation |
| `engine_mount_plate` | Flat steel plate with bolt holes |
| `fuel_shutoff_valve` | Common brass ball valve, recognizable form |
| `key_switch` | Standard ignition switch body |
| `fuel_tank` | Rectangular steel tank (already generated) |
| `battery` | Lead-acid battery (already generated) |
| `reservoir` | Welded steel oil tank (already generated) |
| `oil_cooler` | Fin-and-tube heat exchanger (already generated) |
| `engine` | Small gas engine (already generated) |
| `hydraulic_pump` | Gear pump housing (already generated) |
| `pressure_gauge` | Round gauge with dial face (already generated) |

### Tier 2: Image-to-3D (Rodin with reference photo)

Best for parts with **specific mechanical geometry** that text alone can't convey
accurately. The Rodin API accepts image input — use reference photos from the OSE wiki
build galleries, supplier product photos, or FreeCAD CAD renders.

| Part | Why image helps |
|------|----------------|
| `pump_coupling` | Lovejoy spider coupling has very specific jaw/spider geometry |
| `pressure_hose` | Steel-braided hose with crimped JIC fittings — braiding pattern matters |
| `return_hose` | Rubber hose with specific barb/flare fittings |
| `fuel_line` | Thin hose with an inline fuel filter — filter shape is specific |

**How to use image-to-3D with Rodin:**
```bash
python generate_3d_model.py \
  --image ./reference_images/pump_coupling.jpg \
  --prompt "Lovejoy L-100 spider coupling, cast iron hubs with orange polyurethane spider" \
  --api-key vibecoding \
  --output ./generated_models
```
Source images from:
- OSE wiki build photos
- Supplier product listings (McMaster-Carr, Grainger, Amazon)
- FreeCAD CAD renders from the Power Cube Library

### Tier 3: Procedural / Spline-Based (Unity runtime)

Best for parts that are essentially **thin, flexible cables or wires**. AI 3D generators
struggle with these because they're near-1D geometry and look bad as meshed surfaces.

| Part | Recommended approach |
|------|---------------------|
| `battery_cables` | Unity Spline package + tube mesh extrusion |
| `choke_cable` | Unity Spline package + tube mesh extrusion |
| `throttle_cable` | Unity Spline package + tube mesh extrusion |

**Unity packages for procedural cables:**
- **Unity Splines** (`com.unity.splines`) — built-in spline system with `SplineContainer`,
  `SplineExtrude`, and `SplineAnimate`. Create a tube mesh along a spline path with
  configurable radius, segment count, and material.
- **Spline Mesh Extrusion** — use `SplineExtrude` component to generate a tube mesh from
  a spline. Set radius to match cable gauge (e.g., 2-gauge = ~3.25mm radius for battery
  cables, ~2mm for control cables).
- **Physics (optional)** — for realistic cable drape, use `ConfigurableJoint` chains or
  Obi Rope/Cable for cloth-like cable simulation. This is optional polish — static splines
  are sufficient for instructional purposes.

**Implementation sketch:**
```csharp
// Attach to a GameObject with SplineContainer
[RequireComponent(typeof(SplineContainer), typeof(SplineExtrude))]
public class CableRenderer : MonoBehaviour
{
    [SerializeField] float radius = 0.005f; // 5mm default
    [SerializeField] int segments = 8;
    [SerializeField] Material cableMaterial;
}
```

These parts can keep placeholder GLBs for now and be upgraded to spline-based rendering
in a future pass. The placeholder cubes will still indicate cable placement and snap
targets in the assembly sequence.

## 28.16 Proportion Verification — Preventing Squished Models

### The Problem

The scale normalizer applies a **uniform** scale factor — it scales x, y, and z equally
so the model's largest dimension matches the real-world largest dimension. This means
the model keeps whatever proportions (aspect ratio) it was generated with.

If an AI generator produces an engine that is 1:1:1 (cube-shaped) but the real engine
is 1.3:1:1.1 (wider than tall), the normalizer makes it the correct *size* but it still
looks **squished** — too tall and too narrow compared to the real part.

The root cause is **bad proportions in the generated model**, not the normalizer.

### Prevention: Include Proportions in the Prompt

Before generating a model, look up the real-world dimensions from
`Assets/_Project/Scripts/Editor/PartDimensionCatalog.cs`:

```csharp
{ "engine", new Vector3(0.45f, 0.35f, 0.40f) },  // W x H x D in meters
```

Convert to a human-readable ratio or inches, and include it in the prompt:

```
"... approximately 18 inches wide, 14 inches tall, 16 inches deep ..."
```

or as a ratio:

```
"... roughly 1.3:1:1.1 width-to-height-to-depth proportions ..."
```

This gives the AI generator an explicit shape target instead of guessing.

### Verification: Check Proportions After Generation

After generating a model but **before importing** into the package, run the proportion
verification script to compare the model's aspect ratio against the catalog:

```powershell
python verify_proportions.py <part_id> <path_to_glb>
```

Example:
```powershell
python verify_proportions.py engine ./generated_models/latest/base_basic_pbr.glb
```

Output:
```
Part: engine
  Catalog (meters):  W=0.450  H=0.350  D=0.400
  Catalog ratio:     1.29 : 1.00 : 1.14

  Model bounds:      W=1.200  H=1.100  D=1.150
  Model ratio:       1.09 : 1.00 : 1.05

  Axis deviation:    W=+15.3%  H=+0.0%  D=+8.5%
  Max deviation:     15.3%

  ⚠ WARN: Proportions deviate by more than 10%.
  Consider re-generating with explicit dimensions in the prompt.
```

A deviation under 10% is generally acceptable — the visual difference is subtle. Over
15% is noticeable and the model should be regenerated with corrected proportions in the
prompt or with an image-to-3D reference that shows the correct shape.

### Recommended Workflow

1. **Look up** the part's W×H×D from `PartDimensionCatalog.cs`
2. **Add proportions** to the Rodin prompt (inches or ratio)
3. **Generate** the model
4. **Verify** proportions with the verification script
5. If deviation > 15%, **re-generate** with a more explicit prompt or use image-to-3D
6. If acceptable, **import** into the package and run the normalizer

### Quick Reference: Parts Requiring Proportion Attention

Parts with a nearly cubic aspect ratio (all axes within ~10%) are unlikely to look
squished. Focus proportion verification on parts with **asymmetric shapes**:

| Part ID | Catalog W×H×D (m) | Ratio | Notes |
|---------|-------------------|-------|-------|
| `engine` | 0.45 × 0.35 × 0.40 | 1.29:1:1.14 | V-twin engine — wider than tall |
| `reservoir` | 0.30 × 0.25 × 0.20 | 1.50:1.25:1 | Rectangular tank — widest axis matters |
| `oil_cooler` | 0.25 × 0.20 × 0.05 | 5.0:4.0:1 | Very flat radiator — depth is critical |
| `engine_mount_plate` | 0.30 × 0.006 × 0.30 | 50:1:50 | Extremely flat plate |
| `fuel_tank` | 0.30 × 0.25 × 0.20 | 1.50:1.25:1 | Similar to reservoir |
| `battery` | 0.21 × 0.18 × 0.17 | 1.24:1.06:1 | Nearly cubic — usually fine |
| `hydraulic_pump` | 0.20 × 0.15 × 0.15 | 1.33:1:1 | Wider than tall/deep |

Parts with ratios close to 1:1:1 (like `pump_coupling` at 1.25:1:1) are less likely
to have visible issues.

### Correcting Already-Imported Models

If a model is already in the package and looks squished:

1. Re-generate with explicit proportions in the prompt
2. Run `verify_proportions.py` to confirm the new model is closer
3. Copy the new GLB over the old one (same filename)
4. Press Ctrl+R in Unity — the auto-reload system will pick it up
5. Run `OSE → Normalize Package Model Scales` to set correct uniform scale
