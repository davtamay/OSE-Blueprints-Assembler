# SLICE_ACCEPTANCE_CHECKLIST.md

## Purpose

This document defines when an authored machine slice is good enough to stop hardening and
move on to the next slice.

It exists to prevent two failure modes:

- expanding procedure content on top of weak geometry or unstable interaction
- over-hardening the whole machine before the current slice is actually proven

The correct unit of progress is a source-backed procedural slice, not the entire machine.

---

# 1. Core Rule

Do not ask:

- Is the whole machine complete?
- Have we hardened every part yet?

Ask:

- Is the current slice defensible, teachable, stable, and specific enough to expand from?

If the answer is no, do not expand the procedure.

If the answer is yes, stop polishing and move to the next slice.

---

# 2. Acceptance Levels

Every slice should be judged at three levels:

1. source acceptance
2. runtime acceptance
3. teaching acceptance

A slice is not done if one of these fails.

---

# 3. Source Acceptance

Before approving a slice, confirm:

- the exact revision authority is identified
- the slice boundary is explicitly defined
- the part list is explicit
- the tool list is explicit
- the mounting / fit relationships are explicit enough for the authored interactions
- every meaningful inference is documented

Stop and do more source work if:

- the part/tool list is still ambiguous
- the order of operations is guessed rather than supported
- the mating relationship is not clear enough to place parts honestly

---

# 4. Geometry Acceptance

Only the fit-critical parts for the current slice need to be hardened.

Fit-critical means a part changes:

- where another part sits
- how a tool engages
- whether the learner understands the assembly
- whether collision, spacing, or contact looks believable

For every fit-critical part in the slice, confirm:

- dimensions are source-backed or explicitly dimension-authored
- the mesh scale is normalized
- the pivot/origin is sane for runtime use
- the orientation is stable and intentional
- the part no longer relies on an obviously wrong placeholder mesh

It is acceptable to defer:

- cosmetic-only parts
- far-future subsystem parts
- non-contact support detail that does not affect the current slice

---

# 5. Fastener And Tool Acceptance

If the slice includes fastening or tool-contact steps, confirm:

- the active fastener geometry is believable for the selected tool
- the selected tool is believable for the fastener
- tool size is correct enough for the receiving geometry
- target orientation is aligned with the real insertion / approach axis
- the tool does not obviously hover or miss the contact point by a large distance

For higher-fidelity torque slices, the target state should ultimately support:

- socket center
- insertion axis
- engagement depth
- tip-locked rotation
- retract path

A slice can still pass before full tip-lock realism if:

- the tool is the correct tool
- the fastener is the correct fastener type
- the approach orientation is honest
- the interaction is teachable and not misleading

---

# 6. Placement And Support Acceptance

For every staged or mounted part in the slice, confirm:

- loose parts rest on a believable support, tray, jig, or work surface
- mounted parts appear supported by the receiving structure
- persistent tools seat on believable contact points
- ghosts/targets do not imply physically impossible placement

Floating is only acceptable when:

- the step is explicitly a guided suspended fit
- the temporary in-air state is short and understandable

If the learner sees unexplained floating geometry, the slice is not ready.

---

# 7. Interaction Acceptance

Confirm:

- the correct parts/tools appear for the step
- step navigation works forward/backward across the slice
- restore/resume reconstructs the slice correctly
- camera framing lands on the active work area
- targets/ghosts are understandable
- the interaction to complete the step is discoverable
- persistent tools disappear when the data no longer calls for them

If the user has to guess the mechanic or fight the system, the slice is not ready.

---

# 8. Teaching Acceptance

Confirm:

- the steps are sized appropriately
- the sequence teaches the real assembly logic
- the learner can understand why the operation matters
- repeated steps are only repeated when the real process justifies it
- grouped steps are used where separate single-part steps add no value

If the answer is no, fix the slice before expanding.

---

# 9. Stop Conditions

Stop hardening the current slice and move on when all of these are true:

- source boundary is documented and stable
- fit-critical geometry is no longer placeholder-dependent
- active tools match the active fasteners/operations
- placements and supports are believable
- the runtime interaction is stable enough to demonstrate end-to-end
- the slice teaches the intended concept clearly

Do not keep polishing if the remaining issues are only:

- cosmetic finish detail
- non-interactive surrounding context
- later-module parts outside the current slice
- perfectionist improvements that do not change the learner’s understanding

---

# 10. Expansion Gate

Before adding the next slice, ask:

1. Would a learner trust the current slice as a real step in the machine build?
2. Would the next slice rely on geometry or interactions that are still weak here?
3. Are we about to expand because the slice is done, or because we are tired of fixing it?

Only expand if the current slice is a stable foundation.

---

# 11. Minimal Checklist

Use this as the fast approval pass:

- [ ] exact source boundary documented
- [ ] fit-critical parts hardened
- [ ] active fasteners and tools match each other
- [ ] staged and mounted parts are not implausibly floating
- [ ] camera / navigation / restore stable
- [ ] interaction discoverable and teachable
- [ ] step granularity feels right
- [ ] known remaining gaps are explicitly documented
- [ ] next slice would not inherit a known bad foundation

If any box fails, the slice is still in hardening.

---

# 12. D3D Example

For `d3d_v18_10`, this means:

- do not harden every future heated-bed or electronics part before those slices exist
- do harden the active axes fastener stack before trusting the axes slice
- do harden the active extruder mating parts before expanding printer-side extruder integration
- only move to the next module once the current slice is believable in-editor
