# Asset Resources — Royalty-Free 3D Content Sources

Quick reference for all royalty-free sources available for generating or sourcing 3D assets (parts, tools, environments) in this project.

---

## 1. Hyper3D Rodin Gen-2 (Mesh Generation)

| | |
|---|---|
| **What** | AI text-to-3D and image-to-3D mesh generator |
| **URL** | https://hyper3d.ai |
| **API Key** | `vibecoding` (free tier) |
| **License** | Generated assets are royalty-free for commercial use |
| **Formats** | GLB, USDZ, FBX, OBJ, STL |
| **Tiers** | Sketch (fast/low), Smooth, Regular (balanced), Detail (slow/high), Gen-2 |
| **Script** | `.github/skills/rodin3d-skills/skills/rodin3d-skill/scripts/generate_3d_model.py` |

### Modes

| Mode | Best For | Notes |
|------|----------|-------|
| `--prompt` | Simple/generic shapes | Text-to-3D. Works for basic tools, geometric parts. |
| `--image` | Distinctive/recognizable objects | Image-to-3D. Single clean photo, min 512×512. **Produces much better results** for tools with specific silhouettes (wrenches, grinders, etc.) |

### Lessons Learned

- **Image-to-3D >> Text-to-3D** for recognizable real-world tools
- Single photo only — composite images fuse into one mesh
- Expect diagonal mesh orientation — `ComputeUprightCorrection` handles this at runtime
- `Regular` tier with `--image` gives best fidelity; `Sketch` is fast but lower quality
- `--bbox-condition` is unreliable with the free key

### Example

```powershell
python .github/skills/rodin3d-skills/skills/rodin3d-skill/scripts/generate_3d_model.py `
  --image reference_images/torque_wrench.jpg `
  --tier Regular --geometry-file-format glb --quality high --material PBR `
  --api-key vibecoding --output ./generated_models
```

---

## 2. Meta Quest Asset Library (Pre-made Models)

| | |
|---|---|
| **What** | Curated library of 3D models optimized for Meta Quest |
| **Access** | Via MCP tool `mcp_hzosdevmcp_meta-assets-search` |
| **License** | Royalty-free for use in Meta Quest applications |
| **Formats** | GLB, FBX |
| **Quality** | High — hand-modeled, properly UV-mapped, PBR materials |

### When to Use

- When Rodin text-to-3D produces unrecognizable results
- For common real-world objects (tools, furniture, electronics)
- When you need clean topology and proper UV mapping
- Faster than generation — instant download

### Search + Download

```python
# Search (via MCP tool)
mcp_hzosdevmcp_meta-assets-search(prompt="yellow measuring tape", number_of_models=3)

# Returns GLB URLs — download with Invoke-WebRequest
Invoke-WebRequest -Uri $glbUrl -OutFile "model.glb"
```

### Assets Used in This Project

| Asset ID | Name | Used As |
|----------|------|---------|
| 1001128 | yellow measuring tape | `tool_tape_measure` |

---

## 3. Reference Images (for Image-to-3D)

| | |
|---|---|
| **Location** | `reference_images/` |
| **Purpose** | Source photos for Rodin image-to-3D generation |
| **Requirements** | Single object, clean background, min 512×512, no composites |

### Current Images

| File | Used For | Status |
|------|----------|--------|
| `torque_wrench.jpg` | `tool_torque_wrench` | Done |
| `torque_wrench_vertical.jpg` | (unused variant) | — |

---

## Decision Guide

```
Need a 3D asset?
│
├─ Is it a common real-world object?
│  ├─ YES → Search Meta Asset Library first
│  │         └─ Found good match? → Download GLB, deploy
│  │         └─ No match? → Continue below
│  └─ NO → Continue below
│
├─ Do you have a clean reference photo?
│  ├─ YES → Rodin Image-to-3D (Regular tier)
│  └─ NO → Can you find/take one?
│           ├─ YES → Save to reference_images/, use Rodin
│           └─ NO → Rodin Text-to-3D (Sketch tier for speed, Regular for quality)
│
└─ Verify result → Deploy to authoring + streaming paths
```

---

## Deployment Checklist

After obtaining a GLB from any source:

1. Check bounds: `python verify_proportions.py <part_id> <path_to_glb>`
2. Copy to authoring: `Assets/_Project/Data/Packages/<package>/assets/tools/`
3. Copy to streaming: `Assets/StreamingAssets/MachinePackages/<package>/assets/tools/`
4. If onboarding uses it: also copy to `onboarding_tutorial/assets/tools/` (may need `.gltf` extension)
5. Update status in [TOOL_AUTHORING_PIPELINE.md](TOOL_AUTHORING_PIPELINE.md) manifest table
