# D3D Extruder Stage 03 CAD Provenance

## Acquisition Status

Stage 03 carriage-side CAD files have been acquired into:

- `Assets/_Project/Data/Packages/d3d_v18_10/source_cad/extruder_stage03/raw/`

Acquired on:

- `2026-03-24`

## Files Acquired

### X-axis half carriage

- Local file:
  - `raw/Axis_half_carriage.fcstd`
- Intended package use:
  - clamp-side carriage half for printer-side extruder integration
- Source file page:
  - https://wiki.opensourceecology.org/wiki/File%3AAxis_half_carriage.fcstd
- Download URL used:
  - https://wiki.opensourceecology.org/images/2/22/Axis_half_carriage.fcstd
- File-page notes captured:
  - detailed file from a hacked full carriage
  - carriage width split from `52 mm`
  - half-width target roughly `27.5 mm` to fit `24 mm` bearings

### Extruder spacer

- Local file:
  - `raw/Extruderspacer.fcstd`
- Intended package use:
  - possible carriage-side spacer in the later Titan Aero mount path
- Source file page:
  - https://wiki.opensourceecology.org/wiki/File%3AExtruderspacer.fcstd
- Download URL used:
  - https://wiki.opensourceecology.org/images/3/3b/Extruderspacer.fcstd
- File-page notes captured:
  - no assembly notes beyond file presence and usage pages

### Universal axis carriage side

- Local file:
  - `raw/Universal_axis_carriage_side.fcstd`
- Intended package use:
  - printer-side X-axis carriage-side interface
- Source file page:
  - https://wiki.opensourceecology.org/wiki/File%3AUniversal_axis_carriage_side.fcstd
- Download URL used:
  - https://wiki.opensourceecology.org/images/9/9b/Universal_axis_carriage_side.fcstd
- File-page notes captured:
  - use the version with roughly `6.9 mm` belt hole
  - current file is the widened-belt-hole carriage compound
  - earlier history explicitly says the simplified carriage was positioned for the X-axis of the extruder

### OSE extruder with second bracket

- Local file:
  - `raw/Oseextruder1902_with_2nd_bracket.fcstd`
- Intended package use:
  - carriage-unit assembly reference for the printer-side mount
- Source file page:
  - https://wiki.opensourceecology.org/wiki/File%3AOseextruder1902.fcstd
- Download URL used:
  - https://wiki.opensourceecology.org/images/archive/0/06/20190224013417%21Oseextruder1902.fcstd
- Why the archive was used instead of current:
  - the current simplified file page notes it still needs the lower bracket added for completeness
  - the `2019-02-24` archive entry explicitly says `Added 2nd bracket for attaching extruder to carriage`

## Local File Sizes

- `Axis_half_carriage.fcstd` - `8,757` bytes
- `Extruderspacer.fcstd` - `11,000` bytes
- `Universal_axis_carriage_side.fcstd` - `61,099` bytes
- `Oseextruder1902_with_2nd_bracket.fcstd` - `961,771` bytes

## Immediate Outcome

This acquisition closes the biggest evidence gap that blocked the next D3D slice:

- we now have the carriage-side clamp half
- we now have the simplified carriage-side interface
- we now have the spacer file used in later Titan Aero paths
- we now have a carriage-unit assembly reference that includes the second bracket

The follow-up inspection and conversion status is recorded in:

- `STAGE03_CONVERSION_STATUS.md`
