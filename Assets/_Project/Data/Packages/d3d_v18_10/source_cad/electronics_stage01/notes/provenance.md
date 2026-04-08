# Electronics Stage — Provenance & Conversion Status

## Source

| Part | File | Source | License | Size |
|------|------|--------|---------|------|
| RAMPS 1.4 Board | `RAMPS14_v1904.fcstd` | OSE wiki | CC BY-SA 4.0 | ~86 KB |
| ATX Power Supply | `Powersupply_v1904.fcstd` | OSE wiki | CC BY-SA 4.0 | ~31 KB |
| Smart Controller (LCD) | `Smartcontroller_v1904.fcstd` | OSE wiki | CC BY-SA 4.0 | ~43 KB |
| Control Panel | `Controlpanel_v1904.fcstd` | OSE wiki | CC BY-SA 4.0 | ~133 KB |

Downloaded via `scripts/download_ose_electronics.py`.

## Conversion Status

| Part | FCStd | STL | GLB Candidate | Approved | Unity | Notes |
|------|-------|-----|---------------|----------|-------|-------|
| ramps_14_board | ⬜ | ⬜ | ⬜ | ⬜ | ⬜ | |
| d3d_psu_atx | ⬜ | ⬜ | ⬜ | ⬜ | ⬜ | |
| d3d_smart_controller | ⬜ | ⬜ | ⬜ | ⬜ | ⬜ | |
| d3d_control_panel | ⬜ | ⬜ | ⬜ | ⬜ | ⬜ | |

Legend: ⬜ pending · ✅ done · ❌ blocked

## Authoring Notes

### Positions
These component FCStd files are **standalone** — not embedded in a master assembly.
Part positions within the D3D frame must be authored from:
- Final Assembly PDF slides 25–47 (control panel + wiring bay)
- Physical D3D dimensions (frame inner bay ~300mm wide, electronics mounted on back-left panel)

Run `scripts/extract_placements.py` to get bounding box sizes from each FCStd.
Use sizes to sanity-check assembledPosition values.

### Known Issues
- None yet — to be updated during conversion.

### Wiring Content
See `StepWireConnectPayload.cs` for the WireConnect profile schema.
Wiring steps use `family: "Connect"`, `profile: "WireConnect"`.
Port polarities authored in `wireConnect.wires[].portAPolarityType` / `portBPolarityType`.
