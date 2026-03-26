# Allen / Hex Driver Hardening

## Status

- `tool_allen_key_metric.glb` should be treated as a dimension-authored 5 mm L-key, not an AI-generated placeholder.
- `tool_hex_driver_metric_small.glb` should be treated as a dimension-authored small L-key for the smaller holder-side fasteners.

## Why

The earlier D3D package reused a generic line-wrench placeholder for both tools. That was not defensible for socket-head fastener work or torque-axis alignment.

For these tools, the correct workflow is dimension authoring, not image-to-3D:

- cross-section must remain a true hex
- the inserted tip axis must be explicit
- the bend corner and arm lengths need stable, predictable local coordinates
- runtime `toolPose` should be solved from exact geometry, not from an arbitrary generated mesh

## Working dimensions

Current first-pass working dimensions:

- `tool_allen_key_metric`
  - across flats: `5.0 mm`
  - short arm: `33 mm`
  - long arm: `85 mm`
  - bend radius: `7 mm`

- `tool_hex_driver_metric_small`
  - across flats: `2.5 mm`
  - short arm: `21 mm`
  - long arm: `91 mm`
  - bend radius: `4 mm`

These values are appropriate for the current D3D fastener slice:

- external `M6x30` axis mount bolts -> 5 mm Allen key
- smaller holder-side hex fasteners -> small metric hex key

## Remaining realism gap

The tool assets can now be exact without Meshy, but true insertion realism still needs:

- socket-head bolt geometry
- socket-center / engagement-depth target metadata
- tip-locked insert -> rotate -> retract tool action flow
