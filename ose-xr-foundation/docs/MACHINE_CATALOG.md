# OSE Machine Catalog

Reference document tracking the machines available in the Blueprints Assembler, their real-world context, and authoring progress.

---

## Power Cube

**Category:** Hydraulic Power Unit
**Difficulty:** Intermediate
**Real-world build time:** 2-3 days (workshop format)
**Material cost:** ~$2,600

### What It Is

The Power Cube is a modular, self-contained hydraulic power unit — a gasoline engine coupled to a hydraulic pump that delivers pressurized hydraulic fluid through quick-connect hoses to drive any powered machine in the Global Village Construction Set (GVCS). It is the "universal engine" that powers the LifeTrac tractor, CEB press, ironworker, and dozens of other OSE machines.

The current version (Power Cube VII / 7s) uses a 28 HP Briggs & Stratton Professional Series engine with a fixed-displacement hydraulic pump, delivering 3,000 PSI max pressure and 15.2 GPM max flow. It has a 7-gallon fuel capacity and weighs approximately 400-500 lbs.

### Key Design Principles

- **Modularity** — Multiple Power Cubes can be strung together for increased hydraulic power
- **Flexibility** — Quick-connect hoses and quick-attach plates let you place power wherever needed
- **Repairability** — All engine components are easily accessible and use off-the-shelf parts
- **Open Source** — Full CAD, BOM, and manufacturing instructions published on the OSE wiki

### Full Assembly Stages

| # | Stage | Key Parts | Tools |
|---|-------|-----------|-------|
| 1 | Steel cutting & prep | 4x4 tubular steel, angle iron | Cutting torch, angle grinder |
| 2 | Frame welding | Frame plates, angle brackets, quick-attach mounts | Welder (MIG/stick), clamps, jigs |
| 3 | Fuel tank module | Tank body, mounting flanges | Welder, drill |
| 4 | Hydraulic reservoir module | Reservoir, fluid ports, flanges | Welder, drill |
| 5 | Oil cooler module | Cooling fan, radiator, expanded steel cover | Wrenches, drill |
| 6 | Pump module | Hydraulic pump, pump plate | Wrenches |
| 7 | Engine mounting | 28 HP engine, engine mount bolts | Wrenches, torque wrench |
| 8 | Pump-to-engine coupling | Coupling shaft, alignment hardware | Wrenches, feeler gauge |
| 9 | Module installation | All modules onto frame | Wrenches (3/4" bolts throughout) |
| 10 | Hydraulic plumbing | Hoses, compression fittings, quick-connects | Wrenches, teflon tape |
| 11 | Wiring harness | Key switch, choke, starter, battery | Wire strippers, crimpers |
| 12 | Testing & commissioning | — | Pressure gauge |

### Estimated Full Blueprint Scope

- **Steps:** 40-60
- **Unique parts:** 30-50
- **Tools:** 10-15

### Authoring Progress

| Package ID | Scope | Parts | Steps | Tools | Status |
|------------|-------|-------|-------|-------|--------|
| ~~`power_cube_frame_corner`~~ | ~~Frame corner subassembly (stage 2 subset)~~ | ~~3~~ | ~~3~~ | ~~2~~ | Deleted (superseded by full package) |
| `power_cube_frame` | **Complete Power Cube build - all 12 stages** | 29 | 43 | 10 | **Authored (primitive shapes, legacy package id retained for compatibility)** |

### Current Package Coverage (`power_cube_frame`)
- The package id `power_cube_frame` is legacy. The authored content is a full Power Cube trainer, not a frame-only subset.
- Current authored coverage spans all 12 catalog stages in a guided 43-step flow with 29 parts and 10 tools.
- Represented directly as standalone parts or major steps: structural frame tubes, vertical posts, top ring, engine mount plate, engine, pump coupling, hydraulic pump, reservoir, pressure hose, return hose, oil cooler, fuel tank, fuel line, fuel shutoff valve, battery, battery cables, key switch, choke cable, throttle cable, inline pressure gauge, and commissioning checks.
- Represented as composite or abstracted items rather than decomposed BOM entries: the fuel line includes the inline filter, the oil cooler is a single authored part rather than separate cooler/fan/guard pieces, battery cables include their terminal ends, and several mounting interactions are represented by placement/torque steps without separate hardware parts.
- Not separately authored today: quick-connect couplers and hydraulic manifolds, oil-cooler fan and guard as separate parts, individual brackets/straps/bolt sets, detailed wiring terminals/connectors, reservoir fabrication from raw flanges/ports, and frame accessory hardware such as quick-attach mounts.
- This is an intentional trainer-level compression of the machine. It is complete at the assembly-flow level, but not a one-to-one decomposition of the real-world bill of materials.

### OSE References

- [Power Cube - OSE Wiki](https://wiki.opensourceecology.org/wiki/Power_Cube)
- [Power Cube VII](https://wiki.opensourceecology.org/wiki/Power_Cube_VII)
- [Power Cube / Manufacturing Instructions](https://wiki.opensourceecology.org/wiki/Power_Cube/Manufacturing_Instructions)
- [Power Cube / Bill of Materials](https://wiki.opensourceecology.org/wiki/Power_Cube/Bill_of_Materials)
- [Power Cube VII / Bill of Materials](https://wiki.opensourceecology.org/wiki/Power_Cube_VII/Bill_of_Materials)
- [Structural Power Cube](https://wiki.opensourceecology.org/wiki/Structural_Power_Cube)
- [Power Cube Design Rationale](https://wiki.opensourceecology.org/wiki/Power_Cube_Design_Rationale)

---

## D3D 3D Printer

**Category:** Digital Fabrication
**Difficulty:** Beginner-Intermediate
**Real-world build time:** Workshop-scale, multi-session build
**Material cost:** Varies by build version and sourced components

### What It Is

The D3D is Open Source Ecology's open-source 3D printer platform. Its structure is built
around repeatable square frame geometry so the machine can be fabricated from common stock,
kept repairable, and adapted across workshop contexts.

The current authored package focuses on the frame-construction layer first because the
available OSE source material is strongest there: 1/8 inch x 1 inch flat stock cut to
13 inch lengths, overlapped to produce 14 inch square frame sides.

### Current Authoring Scope

| Package ID | Scope | Parts | Steps | Tools | Status |
|------------|-------|-------|-------|-------|--------|
| `d3d_v18_10` | **Frame fabrication, cube joining, axes stage 01, and extruder stages 01-02** | 38 | 67 | 8 | **Authored in data (exact side geometry, square-check, hold-down, sequential panel tack-weld, rigid-panel stacking, cube alignment check, opposite-corner hold-down, cube corner tack sequence, first seam-weld pass, grinder cleanup on key upper joints, post-cleanup square check, frame acceptance, Y-axis pair mounting, constrained X-axis fit, belt tension, first axis QC, a first Titan Aero nozzle / fan / sensor module slice, and the first carriage-side Titan Aero mount-stack slice)** |

### Current Package Coverage (`d3d_v18_10`)

- Covers all six frame sides as separate fabrication subassemblies.
- Uses source-backed stock dimensions: 13 inch x 1 inch x 1/8 inch mild-steel flat bar.
- Encodes the real 14 inch square side geometry and 1 inch corner overlap for every panel.
- Cube stacking now bakes to canonical integrated member poses so the final visible cube avoids overlapping coplanar panel-shell geometry.
- Includes square-check steps with the framing square before heat is introduced.
- Includes authored hold-down and corner tack-weld steps for each panel.
- Includes explicit learner placement of the finished panels into the open cube layout.
- Includes a first cube-join phase: stacked-cube square-check, opposite-corner hold-down, lower/upper corner tack-weld sequences, lower/upper seam-weld sequences, one grinder cleanup pass on key upper joints, one post-cleanup square check, and a short frame-acceptance handoff.
- Includes `Axes Stage 01`: Y-left mount, Y-right mount, staged X-axis prep, constrained X-axis span fitting, motor-holder lock-up, belt tension, belt-peg reinsertion, and first travel/tightness QC.
- Includes `Extruder Stage 01`: Titan Aero core staging, nozzle assembly placement, blower placement and secure, sensor-holder placement and secure, 8 mm sensor placement, and first nozzle-module clearance QC.
- Includes `Extruder Stage 02`: simplified carriage staging, Titan Aero mount bracket placement and secure, top-plate placement and secure, rigid nozzle-module mounting onto the carriage-side stack, and first carriage-side clearance/service-access QC.
- Uses separate teaching work zones for the six panels. This is a documented instructional staging choice, not a change to the real panel geometry.
- Still does not cover full weld-process detail, exact axis meshes and explicit external mount hardware, the later final carriage-to-X-axis extruder-mount variant, later rails, gantry, full motion system, electronics, or wiring.

### OSE References

- [D3D - OSE Wiki](https://wiki.opensourceecology.org/wiki/D3D)
- [3D Printer Manual](https://wiki.opensourceecology.org/wiki/3D_Printer_Manual)
- [Frame Construction Set](https://wiki.opensourceecology.org/wiki/Frame_Construction_Set)

---

## Future Machines

The GVCS includes 50 machines. Priority candidates for the Blueprints Assembler (based on documentation quality, part count, and educational value):

| Machine | Category | Complexity | Documentation Quality |
|---------|----------|------------|----------------------|
| **CEB Press** | Construction | Intermediate | High — well-documented, workshop-proven |
| **LifeTrac** | Agriculture/Transport | Advanced | High — flagship machine, extensive wiki |
| **Ironworker** | Fabrication | Intermediate | Medium |
| **Torch Table** | Fabrication | Beginner-Intermediate | Medium |
| **Microtrac** | Agriculture/Transport | Advanced | Medium |

---

## 3D Model Generation Strategy

### The Problem

Each machine package needs `.glb` models for every part and tool. The Power Cube alone needs 30-50 unique part models. Manual 3D modeling is the bottleneck — it would take weeks to model everything by hand.

### Recommended Approach: Tiered Generation Pipeline

Use a combination of sources, starting with the fastest/cheapest and escalating only when needed.

#### Tier 1: OSE CAD Files (Free, Highest Fidelity)

OSE publishes CAD files for many machines on their wiki (FreeCAD, OpenSCAD, STEP files).

**Strategy:**
1. Download existing CAD from the OSE wiki (e.g., [Power Cube CAD](https://wiki.opensourceecology.org/wiki/Power_Cube/CAD))
2. Import into Blender via STEP import plugin
3. Decimate to XR-friendly poly counts (target: 2K-10K tris per part)
4. Export as `.glb` with proper origins and scale

**Best for:** Structural parts (frame tubes, plates, brackets) where dimensional accuracy matters.

**Time per part:** 10-20 min (import, decimate, export)

#### Tier 2: AI 3D Generation (Fast, Good for Simple Parts)

Use AI 3D generation tools for parts that don't need exact dimensions — bolts, nuts, standard hardware, tools.

**Recommended tools:**
- **Hyper3D Rodin** (integrated via rodin3d-skill) — text or image to 3D mesh
- **Meshy.ai** — text-to-3D, good for mechanical parts
- **Tripo3D** — image-to-3D with good mesh topology

**Strategy:**
1. Generate from reference images (OSE build photos) or text prompts
2. Clean up in Blender: fix normals, remove interior faces, set proper origins
3. Scale to real-world dimensions using OSE BOM measurements
4. Export as `.glb`

**Best for:** Standard hardware (bolts, nuts, washers, hose fittings), tools (wrenches, drills), simple shapes.

**Time per part:** 5-15 min (generate + cleanup)

**Prompt tips for mechanical parts:**
- Be specific: "M8 hex bolt, zinc-plated steel, 40mm length, metric thread" not "bolt"
- Include material: helps with surface quality
- Reference images from OSE wiki drastically improve quality

#### Tier 3: Primitive Composition (Fastest, Placeholder Quality)

For rapid prototyping and testing the assembly flow before final models exist.

**Strategy:**
1. Compose parts from Unity primitives (cubes, cylinders) in the scene
2. Use our existing `previewConfig.partPlacements` with scale/color to approximate shape
3. Replace with real models later — the schema separates geometry from behavior

**Best for:** Early authoring, flow testing, proving out step sequences before investing in models.

**Time per part:** 2-5 min

### Maximizing Effort: The Assembly-First Workflow

The key insight: **author the machine.json first, test with primitives, then replace models.**

```
1. Author machine.json (steps, parts, tools, targets)     — 2-4 hours per machine
2. Test full assembly flow with primitive shapes            — 1-2 hours
3. Fix step ordering, validation, hints                     — 1-2 hours
4. Generate/source 3D models in batch                       — parallel effort
5. Drop .glb files into assets/ folders                     — minutes per part
6. Adjust previewConfig positions/scales for real geometry   — 1-2 hours
```

This means you can have a **fully functional assembly trainer** with placeholder shapes in a single day, then upgrade visuals incrementally without changing any logic.

### Batch Processing Tips

- **Group by category:** Do all bolts/nuts at once (same prompt template, tweak size), all plates at once, etc.
- **Reuse with material swaps:** Per the project convention, reuse the same mesh with different `MaterialHelper` colors rather than creating separate model files for similar parts
- **OSE reference photos:** The wiki has extensive build photos — use these as image-to-3D references
- **Standard hardware libraries:** Sites like GrabCAD have free M8 bolt models, 13mm wrench models, etc. — faster than generating

### Per-Machine Effort Estimates

| Machine | Parts to Model | Estimated Model Time | JSON Authoring | Total |
|---------|---------------|---------------------|----------------|-------|
| Power Cube (full) | 35-45 | 8-12 hours | 4-6 hours | 12-18 hours |
| Power Cube (frame only) | 8-12 | 2-3 hours | 2-3 hours | 4-6 hours |
| CEB Press | 25-35 | 6-10 hours | 3-5 hours | 9-15 hours |
| Onboarding Tutorial | 5 (done) | 1 hour | 1 hour (done) | done |

---

*This document is updated as new machine packages are authored or expanded.*
