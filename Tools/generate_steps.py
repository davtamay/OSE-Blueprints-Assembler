#!/usr/bin/env python3
"""
generate_steps.py — Assembly Step Generator
============================================
Reads a Translator Input YAML from AgentAssistant/inputs/, expands the named
template with provided part IDs, and writes a step JSON array to AgentAssistant/outputs/.

Usage:
    python tools/generate_steps.py AgentAssistant/inputs/my_carriage.yaml
    python tools/generate_steps.py AgentAssistant/inputs/my_carriage.yaml --output custom_out.json
    python tools/generate_steps.py --list-templates

The output JSON array can be merged directly into the target assembly file's "steps" array.
After merging, run: python tools/package_health.py <packageId> --fix-seqindex

Model-agnostic design:
    The LLM (Claude, GPT-4o, Gemini) writes the 15-line YAML.
    This script expands it deterministically — no LLM involved in expansion.
    Same YAML → same JSON, regardless of which model produced the YAML.
"""

import json
import os
import sys
from pathlib import Path

try:
    import yaml
except ImportError:
    # Fallback: minimal YAML parser for simple key: value / list structures
    yaml = None

REPO_ROOT = Path(__file__).parent.parent
INPUTS_DIR = REPO_ROOT / "AgentAssistant" / "inputs"
OUTPUTS_DIR = REPO_ROOT / "AgentAssistant" / "outputs"


# ── YAML loader fallback ──────────────────────────────────────────────────────

def load_yaml(path):
    """Load YAML file, falling back to a simple parser if PyYAML not installed."""
    text = Path(path).read_text(encoding="utf-8")
    if yaml is not None:
        return yaml.safe_load(text)
    return _simple_yaml_parse(text)


def _simple_yaml_parse(text):
    """Minimal YAML parser: handles flat keys, string lists, and one level of mapping."""
    result = {}
    current_key = None
    current_list = None
    current_map = None
    in_parts = False

    for raw_line in text.splitlines():
        line = raw_line.split("#")[0].rstrip()  # strip comments
        if not line.strip():
            continue

        indent = len(line) - len(line.lstrip())

        if indent == 0:
            in_parts = False
            current_list = None
            current_map = None
            if ":" in line:
                k, _, v = line.partition(":")
                k = k.strip()
                v = v.strip()
                if v:
                    result[k] = v.strip('"').strip("'")
                else:
                    current_key = k
                    if k == "parts":
                        in_parts = True
                        result["parts"] = {}
            continue

        if indent == 2 and in_parts:
            if ":" in line:
                k, _, v = line.partition(":")
                k = k.strip()
                v = v.strip()
                if v.startswith("["):
                    items = v.strip("[]").split(",")
                    result["parts"][k] = [i.strip().strip('"') for i in items if i.strip()]
                else:
                    result["parts"][k] = v.strip('"').strip("'")
            continue

        if indent == 2 and current_key and not in_parts:
            line_stripped = line.strip()
            if line_stripped.startswith("- "):
                val = line_stripped[2:].strip().strip('"')
                if not isinstance(result.get(current_key), list):
                    result[current_key] = []
                result[current_key].append(val)

    return result


# ── Template: BearingCarriage ─────────────────────────────────────────────────

def template_bearing_carriage(parts, start_seq, pfx, orientation_cue, tool, torque, milestone):
    """
    BearingCarriage(half_a, half_b, bearings[4], bolts_top[2], bolts_bot[2], nuts[4])

    Correct step order (enforced as code — not inferred):
      1. Place  → seat bearings in half_a, flanges-outward
      2. Place  → close half_b onto half_a (BEFORE tests)
      3. Confirm → shake-test (halves closed, hand-compressed)
      4. Confirm → rod-slide-test
      5. Place  → insert bolts finger-tight
      6. Use    → power-drill tighten, cross pattern
    """
    half_a     = _req(parts, "half_a", "BearingCarriage")
    half_b     = _req(parts, "half_b", "BearingCarriage")
    bearings   = _req_list(parts, "bearings", "BearingCarriage")
    bolts_top  = _req_list(parts, "bolts_top", "BearingCarriage")
    bolts_bot  = _req_list(parts, "bolts_bot", "BearingCarriage")
    nuts       = _req_list(parts, "nuts", "BearingCarriage")

    all_bolts  = bolts_top + bolts_bot
    n_bearings = len(bearings)
    n_top      = len(bolts_top)
    n_bot      = len(bolts_bot)
    n_nuts     = len(nuts)
    orient     = orientation_cue or "flanges facing outward"
    tool_id    = tool or "tool_power_drill"
    torque_s   = torque or "lowest"
    milestone_s = milestone or "Carriage assembly complete."

    return [
        {
            "id": f"step_{pfx}_place_bearings",
            "name": f"Seat {n_bearings} Bearings in Carriage Half A",
            "sequenceIndex": start_seq,
            "family": "Place",
            "guidance": {
                "instructionText": (
                    f"Place all {n_bearings} LM8UU linear bearings into carriage half A ({half_a}). "
                    f"Orient each bearing with {orient}."
                ),
                "whyItMattersText": (
                    "Flanges prevent axial migration of bearings under load. "
                    "An inverted bearing will walk out of its pocket within the first few print cycles."
                )
            },
            "validation": {
                "successCriteria": f"All {n_bearings} bearings seated flush. Flanges visible on both sides of each pocket.",
                "failureCriteria": "Any bearing recessed, flipped, or able to rock in its pocket."
            },
            "feedback": {
                "successMessage": "Bearings seated correctly — ready to close the carriage.",
                "failureMessage": "Re-seat any loose or inverted bearings before closing."
            },
            "requiredPartIds": [half_a] + bearings
        },
        {
            "id": f"step_{pfx}_close_halves",
            "name": "Close Carriage Half B — Align Belt Holes",
            "sequenceIndex": start_seq + 1,
            "family": "Place",
            "guidance": {
                "instructionText": (
                    f"Press carriage half B ({half_b}) firmly against half A, trapping all {n_bearings} bearings. "
                    "Align so the small ribbed belt hole sits beside the large smooth belt hole — "
                    "this is the ONLY correct orientation."
                ),
                "whyItMattersText": (
                    "Belt-hole alignment must be confirmed before bolting. "
                    "If small-ribbed ≠ beside large-smooth, the peg holes misalign and the belt cannot thread through."
                )
            },
            "validation": {
                "successCriteria": "Both halves flush along the full seam. Small ribbed hole is directly beside the large smooth hole.",
                "failureCriteria": "Gap visible along seam, or belt holes on wrong sides relative to each other."
            },
            "feedback": {
                "successMessage": "Halves closed and belt holes aligned.",
                "failureMessage": "Flip half B 180° and retry — small ribbed hole must be adjacent to the large smooth hole."
            },
            "requiredPartIds": [half_a, half_b]
        },
        {
            "id": f"step_{pfx}_shake_test",
            "name": "Shake Test — Bearing Retention Check",
            "sequenceIndex": start_seq + 2,
            "family": "Confirm",
            "guidance": {
                "instructionText": (
                    "Hold both halves compressed firmly by hand — do not bolt yet. "
                    "Shake the closed assembly firmly for 2–3 seconds. "
                    "Listen and feel for any bearing rattle or movement."
                ),
                "whyItMattersText": (
                    "A rattle means at least one bearing is not fully seated in its pocket. "
                    "Bolting before fixing locks in the defect — the carriage will develop play under load "
                    "and the axis will lose positional accuracy."
                )
            },
            "validation": {
                "successCriteria": "No audible rattle, no felt movement from bearings while halves are hand-compressed.",
                "failureCriteria": "Any rattle, clicking, or loose feeling during the shake."
            },
            "feedback": {
                "successMessage": "Shake test passed — bearings are solid.",
                "failureMessage": "Open the halves and identify the loose bearing. Re-seat it and retest before proceeding."
            },
            "requiredPartIds": [half_a, half_b]
        },
        {
            "id": f"step_{pfx}_rod_slide_test",
            "name": "Rod Slide Test — Bearing Alignment Check",
            "sequenceIndex": start_seq + 3,
            "family": "Confirm",
            "guidance": {
                "instructionText": (
                    "While still holding halves compressed, slide a smooth 8mm rod vertically through all "
                    f"{n_bearings} bearings. It should pass with slight, even resistance — "
                    "not too stiff, not freely loose."
                ),
                "whyItMattersText": (
                    "Uneven resistance exposes bearing misalignment. "
                    "If left uncorrected, the axis will bind under motor load and cause missed steps or stalls."
                )
            },
            "validation": {
                "successCriteria": f"Rod slides through all {n_bearings} bearings with slight, consistent resistance. No binding or tight spots.",
                "failureCriteria": "Rod catches or sticks at any bearing, OR drops through with zero resistance."
            },
            "feedback": {
                "successMessage": "Rod slide test passed — bearings aligned.",
                "failureMessage": "Open halves. Check that all bearings are fully seated and coplanar. Re-close and retest."
            },
            "requiredPartIds": [half_a, half_b]
        },
        {
            "id": f"step_{pfx}_place_bolts",
            "name": f"Insert Bolts and Thread {n_nuts} Nuts Finger-Tight",
            "sequenceIndex": start_seq + 4,
            "family": "Place",
            "guidance": {
                "instructionText": (
                    f"Insert the {n_top} shorter M6×18 bolts through the TOP holes "
                    f"and the {n_bot} longer M6×30 bolts through the BOTTOM holes. "
                    f"Thread all {n_nuts} M6 nuts finger-tight — hand only, no drill yet."
                ),
                "whyItMattersText": (
                    "Short bolts top, long bolts bottom — this is the load-distribution pattern. "
                    "Swapping them puts longer threads in the high-stress joint, which can strip faster under Y-axis acceleration."
                )
            },
            "validation": {
                "successCriteria": (
                    f"All {len(all_bolts)} bolts inserted and {n_nuts} nuts threaded finger-tight. "
                    "Short M6×18 bolts are in the top holes, long M6×30 in the bottom holes."
                ),
                "failureCriteria": "Wrong bolt length in wrong hole, any nut cross-threaded, or any bolt not yet threaded."
            },
            "feedback": {
                "successMessage": "Bolts and nuts placed correctly — ready to tighten.",
                "failureMessage": "Verify bolt lengths: M6×18 (shorter) → top holes, M6×30 (longer) → bottom holes."
            },
            "requiredPartIds": all_bolts + nuts
        },
        {
            "id": f"step_{pfx}_tighten",
            "name": "Tighten Carriage Bolts — Cross Pattern",
            "sequenceIndex": start_seq + 5,
            "family": "Use",
            "guidance": {
                "instructionText": (
                    f"Using the power drill at {torque_s} torque setting, tighten all {len(all_bolts)} carriage bolts "
                    "in a cross pattern: top-left → bottom-right → top-right → bottom-left. "
                    "Repeat the pattern once to equalize clamping."
                ),
                "whyItMattersText": (
                    "Cross-pattern tightening ensures even clamping force across the carriage body. "
                    "Sequential (one side first) tightening distorts the bearing bores and causes axis binding."
                )
            },
            "validation": {
                "successCriteria": (
                    f"All {len(all_bolts)} bolts tight at {torque_s} torque. "
                    "Carriage halves fully closed — no gap along seam. "
                    "Rod still slides smoothly through bearings after tightening. "
                    f"{milestone_s}"
                ),
                "failureCriteria": "Any bolt loose, gap visible between halves, or rod now binds after tightening."
            },
            "feedback": {
                "successMessage": milestone_s,
                "failureMessage": (
                    "If rod now binds: back off all bolts ¼ turn, re-run cross pattern at lower torque. "
                    "If gap persists: check for bearing oversize or debris between halves."
                )
            },
            "requiredPartIds": all_bolts,
            "requiredToolActions": [
                {
                    "toolId": tool_id,
                    "actionType": "Tighten",
                    "profile": "Torque"
                }
            ]
        }
    ]


# ── Template: IdlerHalves ─────────────────────────────────────────────────────

def template_idler_halves(parts, start_seq, pfx, orientation_cue, tool, torque, milestone):
    """
    IdlerHalves(half_a, half_b, bearings[2], bolt_inner, bolt_frame_mount)

    Step order (enforced as code):
      1. Place  → insert bolt_inner through half_a from outside
      2. Place  → stack 2 flanged bearings on bolt, flanges-outward
      3. Confirm → align rod holes on half_b before closing
      4. Place  → press half_b onto half_a, install bolt_frame_mount loose
    """
    half_a          = _req(parts, "half_a", "IdlerHalves")
    half_b          = _req(parts, "half_b", "IdlerHalves")
    bearings        = _req_list(parts, "bearings", "IdlerHalves")
    bolt_inner      = _req(parts, "bolt_inner", "IdlerHalves")
    bolt_frame_mount = _req(parts, "bolt_frame_mount", "IdlerHalves")
    n_bearings      = len(bearings)
    milestone_s     = milestone or "Idler assembly complete."

    return [
        {
            "id": f"step_{pfx}_insert_inner_bolt",
            "name": "Insert Inner Bolt Through Idler Half A",
            "sequenceIndex": start_seq,
            "family": "Place",
            "guidance": {
                "instructionText": (
                    f"Insert the inner bolt ({bolt_inner}) through the center hole of idler half A ({half_a}) "
                    "from the outside face inward. The bolt head should sit flush against the outer face."
                ),
                "whyItMattersText": (
                    "The inner bolt is the bearing axle. It must be inserted before the bearings are stacked — "
                    "once the bearings are on and the halves are closed, there is no access to thread it."
                )
            },
            "validation": {
                "successCriteria": "Bolt passes through the center hole; bolt head flush with the outer face of half A.",
                "failureCriteria": "Bolt head protruding past the outer face, or bolt inserted from the wrong side."
            },
            "feedback": {
                "successMessage": "Inner bolt in place — ready to stack bearings.",
                "failureMessage": "Remove and re-insert from the correct (outer) side."
            },
            "requiredPartIds": [half_a, bolt_inner]
        },
        {
            "id": f"step_{pfx}_place_bearings",
            "name": f"Stack {n_bearings} Flanged Bearings on Inner Bolt",
            "sequenceIndex": start_seq + 1,
            "family": "Place",
            "guidance": {
                "instructionText": (
                    f"Slide all {n_bearings} flanged bearings onto the inner bolt, one at a time. "
                    "Orient each with flanges facing outward (away from the belt channel). "
                    "The flanges prevent the bearings from sliding off the bolt axle."
                ),
                "whyItMattersText": (
                    "Flange orientation determines whether the belt rides centered on the bearing. "
                    "Inverted flanges let the belt drift off the pulley surface and fray under tension."
                )
            },
            "validation": {
                "successCriteria": f"All {n_bearings} bearings on bolt, flanges facing outward, snug against each other.",
                "failureCriteria": "Any bearing inverted, not seated flush, or flanges facing inward."
            },
            "feedback": {
                "successMessage": "Bearings stacked correctly — ready to close the idler.",
                "failureMessage": "Remove bearings, flip any inverted ones, and re-stack."
            },
            "requiredPartIds": bearings
        },
        {
            "id": f"step_{pfx}_align_half_b",
            "name": "Align Idler Half B — Check Rod Holes Before Closing",
            "sequenceIndex": start_seq + 2,
            "family": "Confirm",
            "guidance": {
                "instructionText": (
                    f"Hold idler half B ({half_b}) next to half A and verify the rod holes on both halves align. "
                    "The holes must be colinear — look straight through both halves before pressing them together."
                ),
                "whyItMattersText": (
                    "Misaligned rod holes create a pinch point that prevents rods from threading through "
                    "and puts lateral stress on the bearings, causing premature wear."
                )
            },
            "validation": {
                "successCriteria": "Rod holes on both halves line up visually when held together.",
                "failureCriteria": "Holes offset — one half rotated 180° from the correct orientation."
            },
            "feedback": {
                "successMessage": "Rod holes aligned — safe to close.",
                "failureMessage": "Flip half B 180° and check again. One orientation is correct; the other is not."
            },
            "requiredPartIds": [half_a, half_b]
        },
        {
            "id": f"step_{pfx}_close_and_mount_bolt",
            "name": "Close Idler Halves — Install Frame-Mount Bolt Loose",
            "sequenceIndex": start_seq + 3,
            "family": "Place",
            "guidance": {
                "instructionText": (
                    f"Press half B ({half_b}) firmly against half A, trapping the {n_bearings} bearings. "
                    f"Insert the frame-mount bolt ({bolt_frame_mount}) through the belt-side hole finger-tight only — "
                    "do NOT tighten. It will be fully tightened when the idler is mounted to the frame."
                ),
                "whyItMattersText": (
                    "Leaving the frame-mount bolt loose allows final positional adjustment at frame mounting. "
                    "Pre-tightening it now fixes the idler angle before the rod is threaded — misalignment becomes permanent."
                )
            },
            "validation": {
                "successCriteria": (
                    "Both halves closed flush. Belt-side bolt installed finger-tight. "
                    f"{milestone_s}"
                ),
                "failureCriteria": "Gap between halves, or frame-mount bolt torqued rather than finger-tight."
            },
            "feedback": {
                "successMessage": milestone_s,
                "failureMessage": "If gap persists, check that bearings are fully on the bolt before closing."
            },
            "requiredPartIds": [half_a, half_b, bolt_frame_mount]
        }
    ]


# ── Template: MotorHolder ─────────────────────────────────────────────────────

def template_motor_holder(parts, start_seq, pfx, orientation_cue, tool, torque, milestone):
    """
    MotorHolder(motor, pulley, belt, half_nuts[3], belt_bolt, motor_screws[4], close_bolts[3])

    Step order (enforced as code):
      1. Place  → seat pulley on shaft with spacer, tighten first set screw
      2. Use    → remove spacer, tighten second set screw until pop-check
      3. Place  → prep half-piece: long bolt + 3 nuts
      4. Place  → lay belt in channel, toothed-side inward
      5. Place  → close halves, cable plug facing bottom-right
      6. Confirm → dangle-test: motor hangs freely
      7. Use    → tighten motor screws then close bolts
    """
    motor        = _req(parts, "motor", "MotorHolder")
    pulley       = _req(parts, "pulley", "MotorHolder")
    belt         = _req(parts, "belt", "MotorHolder")
    half_nuts    = _req_list(parts, "half_nuts", "MotorHolder")
    belt_bolt    = _req(parts, "belt_bolt", "MotorHolder")
    motor_screws = _req_list(parts, "motor_screws", "MotorHolder")
    close_bolts  = _req_list(parts, "close_bolts", "MotorHolder")
    cable_orient = orientation_cue or "bottom-right"
    tool_id      = tool or "tool_power_drill"
    milestone_s  = milestone or "Motor holder assembly complete."

    return [
        {
            "id": f"step_{pfx}_seat_pulley",
            "name": "Seat Pulley on Motor Shaft — First Set Screw",
            "sequenceIndex": start_seq,
            "family": "Place",
            "guidance": {
                "instructionText": (
                    f"Slide the pulley ({pulley}) onto the motor shaft ({motor}) with the plastic spacer "
                    "between the pulley face and motor body. Hand-tighten the first set screw (the one "
                    "closest to the motor body) until snug — do not torque yet."
                ),
                "whyItMattersText": (
                    "The spacer sets the correct belt-height offset. Without it, the pulley sits too close "
                    "to the motor body and the belt rubs on the motor face under load."
                )
            },
            "validation": {
                "successCriteria": "Pulley on shaft with spacer visible. First set screw hand-tight.",
                "failureCriteria": "Spacer missing, or both set screws tightened before spacer is removed."
            },
            "feedback": {
                "successMessage": "Pulley seated with spacer — ready to set the second screw.",
                "failureMessage": "Remove pulley, add the spacer, and re-seat."
            },
            "requiredPartIds": [motor, pulley]
        },
        {
            "id": f"step_{pfx}_set_screw_pop",
            "name": "Remove Spacer — Tighten Second Set Screw to Pop-Check",
            "sequenceIndex": start_seq + 1,
            "family": "Use",
            "guidance": {
                "instructionText": (
                    "Slide the spacer out. Tighten the second set screw (far side) with the hex key, "
                    "increasing torque until you feel and hear a soft pop or click — that is the screw "
                    "seating into the shaft flat. Stop at the pop; do not overtighten."
                ),
                "whyItMattersText": (
                    "The pop-check confirms the set screw has bitten into the shaft flat, not the round. "
                    "A screw set on the round will loosen within hours of belt tension."
                )
            },
            "validation": {
                "successCriteria": "Spacer removed. Audible/tactile pop felt. Pulley does not rotate freely on shaft.",
                "failureCriteria": "No pop felt, or pulley still spins after tightening."
            },
            "feedback": {
                "successMessage": "Set screw locked — pulley secured to shaft.",
                "failureMessage": "Back off, rotate shaft slightly to align flat with screw, and re-tighten until pop."
            },
            "requiredPartIds": [motor, pulley],
            "requiredToolActions": [
                {
                    "toolId": "tool_hex_key",
                    "actionType": "Tighten",
                    "profile": "Torque"
                }
            ]
        },
        {
            "id": f"step_{pfx}_prep_half",
            "name": "Prep Motor Holder Half — Belt Bolt and Nuts",
            "sequenceIndex": start_seq + 2,
            "family": "Place",
            "guidance": {
                "instructionText": (
                    f"Insert the long belt bolt ({belt_bolt}) through the bottom-left hole of the motor holder "
                    f"half so it pokes upward into the belt channel. Thread the {len(half_nuts)} M6 nuts "
                    "into their recesses finger-tight — they capture the belt when the halves close."
                ),
                "whyItMattersText": (
                    "The belt bolt and nuts must be pre-positioned before the belt is laid in. "
                    "There is no access to thread them once the belt is in the channel."
                )
            },
            "validation": {
                "successCriteria": f"Belt bolt protruding into belt channel. All {len(half_nuts)} nuts in recesses.",
                "failureCriteria": "Belt bolt not inserted, or any nut missing from its recess."
            },
            "feedback": {
                "successMessage": "Half prepped — ready to lay the belt.",
                "failureMessage": "Check that the belt bolt head is on the outside face and shaft points into the channel."
            },
            "requiredPartIds": [belt_bolt] + half_nuts
        },
        {
            "id": f"step_{pfx}_lay_belt",
            "name": "Lay Belt in Channel — Toothed Side Inward",
            "sequenceIndex": start_seq + 3,
            "family": "Place",
            "guidance": {
                "instructionText": (
                    f"Lay the belt ({belt}) in the motor holder channel with the toothed side facing inward "
                    "(toward the pulley). Route the belt so it wraps around the pulley with the teeth "
                    "meshing into the pulley grooves."
                ),
                "whyItMattersText": (
                    "Belt teeth must engage the pulley grooves, not ride on the smooth back. "
                    "A smooth-side-inward belt will slip under any load and strip the teeth within minutes."
                )
            },
            "validation": {
                "successCriteria": "Belt seated in channel, toothed side toward pulley, teeth engaging pulley grooves.",
                "failureCriteria": "Belt flipped — smooth side faces pulley, or belt not reaching the pulley."
            },
            "feedback": {
                "successMessage": "Belt seated correctly — ready to close the holder.",
                "failureMessage": "Flip the belt end-for-end so teeth face the pulley, then re-lay."
            },
            "requiredPartIds": [belt]
        },
        {
            "id": f"step_{pfx}_close_halves",
            "name": f"Close Motor Holder Halves — Cable Plug Facing {cable_orient}",
            "sequenceIndex": start_seq + 4,
            "family": "Place",
            "guidance": {
                "instructionText": (
                    "Gently bring the second half of the motor holder against the first, capturing the belt. "
                    f"Before pressing fully closed, verify the motor cable plug faces {cable_orient}. "
                    "Press halves together until the seam is flush."
                ),
                "whyItMattersText": (
                    "Cable plug orientation determines whether the cable routes cleanly to the electronics bay. "
                    "Reversing it means the cable crosses the belt path and chafes under motion."
                )
            },
            "validation": {
                "successCriteria": f"Halves flush along full seam. Motor cable plug visible facing {cable_orient}.",
                "failureCriteria": "Gap in seam, or cable plug facing the wrong direction."
            },
            "feedback": {
                "successMessage": "Holder closed — proceed to dangle test.",
                "failureMessage": f"Open halves, rotate motor 180°, and re-close. Cable plug must face {cable_orient}."
            },
            "requiredPartIds": [motor]
        },
        {
            "id": f"step_{pfx}_dangle_test",
            "name": "Dangle Test — Motor Hangs Freely from Belt",
            "sequenceIndex": start_seq + 5,
            "family": "Confirm",
            "guidance": {
                "instructionText": (
                    "Hold the motor holder by the belt ends only — let the motor hang freely. "
                    "The motor should hang straight, with no torque or twist visible in the belt. "
                    "Gently swing the assembly: the motor should dangle without resistance."
                ),
                "whyItMattersText": (
                    "A motor that does not hang freely indicates internal binding in the holder or a "
                    "pinched belt. Bolting in this state transfers the binding to the axis and causes "
                    "irregular motion during prints."
                )
            },
            "validation": {
                "successCriteria": "Motor hangs straight. No twist in belt. Free swing with no resistance.",
                "failureCriteria": "Motor cants to one side, belt has a visible twist, or swing feels stiff."
            },
            "feedback": {
                "successMessage": "Dangle test passed — motor holder ready to bolt.",
                "failureMessage": "Open halves, check belt routing and cable position, re-close and retest."
            },
            "requiredPartIds": [motor, belt]
        },
        {
            "id": f"step_{pfx}_tighten",
            "name": "Tighten Motor Screws then Close Bolts",
            "sequenceIndex": start_seq + 6,
            "family": "Use",
            "guidance": {
                "instructionText": (
                    f"Step 1 — tighten all {len(motor_screws)} motor screws to ¾ tight (not full torque). "
                    f"Step 2 — tighten the {len(close_bolts)} M6 close bolts in a cross pattern. "
                    "This order prevents the motor from shifting inside the holder under bolt clamping."
                ),
                "whyItMattersText": (
                    "Motor screws before close bolts: the motor must be clamped first, or bolt clamping "
                    "torque shifts the motor position and the belt tension becomes uneven across the pulley."
                )
            },
            "validation": {
                "successCriteria": (
                    f"All {len(motor_screws)} motor screws ¾ tight. "
                    f"All {len(close_bolts)} close bolts torqued in cross pattern. "
                    f"{milestone_s}"
                ),
                "failureCriteria": "Close bolts tightened before motor screws, or any bolt missed."
            },
            "feedback": {
                "successMessage": milestone_s,
                "failureMessage": "Back off close bolts, tighten motor screws first, then re-tighten close bolts."
            },
            "requiredPartIds": motor_screws + close_bolts,
            "requiredToolActions": [
                {
                    "toolId": tool or "tool_power_drill",
                    "actionType": "Tighten",
                    "profile": "Torque"
                }
            ]
        }
    ]


# ── Template: RodAssembly ─────────────────────────────────────────────────────

def template_rod_assembly(parts, start_seq, pfx, orientation_cue, tool, torque, milestone):
    """
    RodAssembly(rod_a, rod_b, idler, carriage, motor_holder)

    Step order (enforced as code):
      1. Place  → insert both rods into idler (flush with idler bottom)
      2. Use    → tighten idler's two grip bolts
      3. Place  → slide carriage onto rods (long bolt ends toward idler)
      4. Place  → push motor_holder onto rods (motor facing carriage bolt heads)
      5. Confirm → verify rod flush at idler bottom; bolt heads same side
    """
    rod_a        = _req(parts, "rod_a", "RodAssembly")
    rod_b        = _req(parts, "rod_b", "RodAssembly")
    idler        = _req(parts, "idler", "RodAssembly")
    carriage     = _req(parts, "carriage", "RodAssembly")
    motor_holder = _req(parts, "motor_holder", "RodAssembly")
    tool_id      = tool or "tool_power_drill"
    milestone_s  = milestone or "Rod assembly complete."

    return [
        {
            "id": f"step_{pfx}_insert_rods",
            "name": "Insert Both Rods into Idler — Flush at Bottom",
            "sequenceIndex": start_seq,
            "family": "Place",
            "guidance": {
                "instructionText": (
                    f"Insert rod A ({rod_a}) and rod B ({rod_b}) into the completed idler ({idler}) "
                    "from the TOP. Push each rod down until it is flush with the bottom face of the idler. "
                    "Rods enter from above — not from the bottom."
                ),
                "whyItMattersText": (
                    "Rods flush at the idler bottom is the positional reference for the whole axis. "
                    "If rods protrude below the idler, the axis mounting height shifts and the bed level changes."
                )
            },
            "validation": {
                "successCriteria": "Both rods flush with the bottom face of the idler. No protrusion below.",
                "failureCriteria": "Any rod protruding below the idler bottom, or not fully inserted."
            },
            "feedback": {
                "successMessage": "Rods flush — ready to grip.",
                "failureMessage": "Push rods further down. They enter from the top and must be flush at the bottom."
            },
            "requiredPartIds": [rod_a, rod_b, idler]
        },
        {
            "id": f"step_{pfx}_grip_rods",
            "name": "Tighten Idler Grip Bolts — Lock Rods",
            "sequenceIndex": start_seq + 1,
            "family": "Use",
            "guidance": {
                "instructionText": (
                    f"Tighten the two shorter bolts on the idler ({idler}) that grip the rods. "
                    "These are the bolts that clamp across the rod channels — not the frame-mount bolt. "
                    "Tighten until rods are fully locked; they should not slide or rotate."
                ),
                "whyItMattersText": (
                    "Rod grip determines axis straightness. A rod that can rotate will cause "
                    "the carriage to twist on its bearings, producing a visible layer-shift artifact."
                )
            },
            "validation": {
                "successCriteria": "Both rods immovable — cannot slide or rotate by hand. Still flush at idler bottom.",
                "failureCriteria": "Any rod slides or rotates after tightening."
            },
            "feedback": {
                "successMessage": "Rods locked in idler.",
                "failureMessage": "Tighten the grip bolts further. Check you are tightening the rod-grip bolts, not the frame-mount bolt."
            },
            "requiredPartIds": [idler],
            "requiredToolActions": [
                {
                    "toolId": tool_id,
                    "actionType": "Tighten",
                    "profile": "Torque"
                }
            ]
        },
        {
            "id": f"step_{pfx}_slide_carriage",
            "name": "Slide Carriage onto Rods",
            "sequenceIndex": start_seq + 2,
            "family": "Place",
            "guidance": {
                "instructionText": (
                    f"Slide the completed carriage ({carriage}) onto both rods from the motor end. "
                    "Orient the carriage so the long bolt ends (M6×30) point toward the idler. "
                    "Push the carriage to the midpoint of the rods."
                ),
                "whyItMattersText": (
                    "Bolt-end orientation determines belt peg alignment. Reversed carriage means the "
                    "belt holes face the wrong direction and the belt cannot thread without crossing itself."
                )
            },
            "validation": {
                "successCriteria": "Carriage on rods, sliding freely, long bolt ends facing the idler.",
                "failureCriteria": "Carriage reversed (short bolt ends toward idler), or carriage binding on rods."
            },
            "feedback": {
                "successMessage": "Carriage on rods correctly.",
                "failureMessage": "Remove carriage, flip 180°, and re-slide so long bolt ends face the idler."
            },
            "requiredPartIds": [carriage]
        },
        {
            "id": f"step_{pfx}_mount_motor_holder",
            "name": "Push Motor Holder onto Rods",
            "sequenceIndex": start_seq + 3,
            "family": "Place",
            "guidance": {
                "instructionText": (
                    f"Push the motor holder ({motor_holder}) onto the rod ends. "
                    "Orient so the motor faces the carriage bolt heads. "
                    "The motor holder rod holes should accept both rods simultaneously — "
                    "do not force one rod at a time."
                ),
                "whyItMattersText": (
                    "Motor facing the bolt heads is the standard orientation for all D3D axis builds. "
                    "Reversed motor changes the belt routing geometry and makes the belt thread impossible."
                )
            },
            "validation": {
                "successCriteria": "Motor holder on rods. Motor face toward carriage bolt heads.",
                "failureCriteria": "Motor facing away from carriage, or only one rod inserted."
            },
            "feedback": {
                "successMessage": "Motor holder positioned correctly.",
                "failureMessage": "Remove, rotate 180°, and re-insert with motor facing the carriage bolt heads."
            },
            "requiredPartIds": [motor_holder]
        },
        {
            "id": f"step_{pfx}_verify_flush",
            "name": "Verify: Rods Still Flush at Idler — Bolt Heads Same Side",
            "sequenceIndex": start_seq + 4,
            "family": "Confirm",
            "guidance": {
                "instructionText": (
                    "Check two things: (1) rods are still flush with the idler bottom — adding the "
                    "carriage and motor holder should not have shifted them; (2) all long bolt heads "
                    f"on the carriage ({carriage}) are on the same side as the idler."
                ),
                "whyItMattersText": (
                    "Rod migration during component sliding is a common error. "
                    "A rod shifted even 2mm causes the idler grip bolt to sit off-center on the rod, "
                    "reducing grip strength by 40% and risking rod release under axis reversal load."
                )
            },
            "validation": {
                "successCriteria": (
                    "Rods flush at idler bottom (no protrusion). "
                    f"All carriage long-bolt heads on idler side. {milestone_s}"
                ),
                "failureCriteria": "Any rod protruding below idler, or bolt heads on motor-holder side."
            },
            "feedback": {
                "successMessage": milestone_s,
                "failureMessage": (
                    "If rods shifted: loosen idler grip bolts, push rods back to flush, re-tighten. "
                    "If bolt heads wrong side: remove carriage, flip 180°, re-slide."
                )
            },
            "requiredPartIds": [idler, carriage, motor_holder]
        }
    ]


# ── Template: BeltThread ──────────────────────────────────────────────────────

def template_belt_thread(parts, start_seq, pfx, orientation_cue, tool, torque, milestone):
    """
    BeltThread(belt, carriage, idler, peg_1, peg_2)

    Step order (enforced as code):
      1. Place  → insert belt end through large smooth belt hole in carriage
      2. Place  → route around idler bearing
      3. Place  → pull through small ribbed belt hole
      4. Confirm → orient peg foot away from axis center
      5. Use    → insert belt end into peg_1, press into ribbed hole
      6. Place  → place peg_2 loosely (tensioned at frame mount)
      7. Confirm → travel-test: belt moves full rod length, no rub
    """
    belt     = _req(parts, "belt", "BeltThread")
    carriage = _req(parts, "carriage", "BeltThread")
    idler    = _req(parts, "idler", "BeltThread")
    peg_1    = _req(parts, "peg_1", "BeltThread")
    peg_2    = _req(parts, "peg_2", "BeltThread")
    milestone_s = milestone or "Belt threaded — axis ready for frame mounting."

    return [
        {
            "id": f"step_{pfx}_insert_belt_smooth",
            "name": "Insert Belt End Through Large Smooth Belt Hole",
            "sequenceIndex": start_seq,
            "family": "Place",
            "guidance": {
                "instructionText": (
                    f"Find the large smooth (un-ribbed) belt hole on the carriage ({carriage}). "
                    f"Insert the toothed end of the belt ({belt}) through this hole from the outside, "
                    "leaving about 10 cm of belt tail on the inside."
                ),
                "whyItMattersText": (
                    "Starting with the smooth hole ensures the belt routes around the idler in the correct "
                    "direction. Starting from the ribbed hole reverses the peg orientation and the belt "
                    "cannot tension correctly."
                )
            },
            "validation": {
                "successCriteria": "Belt inserted through the large smooth hole. ~10 cm tail visible on the inside of carriage.",
                "failureCriteria": "Belt inserted through the wrong (ribbed) hole, or tail too short to route to idler."
            },
            "feedback": {
                "successMessage": "Belt end through smooth hole — ready to route to idler.",
                "failureMessage": "Withdraw and insert through the large smooth hole, not the ribbed one."
            },
            "requiredPartIds": [belt, carriage]
        },
        {
            "id": f"step_{pfx}_route_idler",
            "name": "Route Belt Around Idler Bearing",
            "sequenceIndex": start_seq + 1,
            "family": "Place",
            "guidance": {
                "instructionText": (
                    f"Route the belt tail around the idler bearing ({idler}). "
                    "A slight curve in the belt helps guide it over the bearing — "
                    "do not kink. The smooth (back) side of the belt rides on the bearing surface."
                ),
                "whyItMattersText": (
                    "The smooth belt back must contact the idler bearing, not the teeth. "
                    "Routing teeth-first on the idler creates tooth-on-bearing contact that "
                    "degrades both the belt teeth and the bearing race within the first hour of use."
                )
            },
            "validation": {
                "successCriteria": "Belt wrapped around idler bearing, smooth side on bearing. No kinks.",
                "failureCriteria": "Toothed side contacting idler bearing, or belt kinked at the bend."
            },
            "feedback": {
                "successMessage": "Belt routed around idler correctly.",
                "failureMessage": "Flip the belt so the smooth back face rides on the bearing."
            },
            "requiredPartIds": [idler]
        },
        {
            "id": f"step_{pfx}_pull_ribbed",
            "name": "Pull Belt Through Small Ribbed Belt Hole",
            "sequenceIndex": start_seq + 2,
            "family": "Place",
            "guidance": {
                "instructionText": (
                    f"Pull the belt tail through the small ribbed hole on the carriage ({carriage}). "
                    "The ribbed hole grips the belt peg. Pull until there is light tension in the belt loop "
                    "running around the idler — not tight, just no slack."
                ),
                "whyItMattersText": (
                    "The ribbed hole is narrower than the smooth hole by design — it is sized to grip "
                    "the belt peg shoulder. Threading backwards through the ribbed hole first "
                    "prevents peg insertion."
                )
            },
            "validation": {
                "successCriteria": "Belt tail exits the ribbed hole. Light tension in belt loop. No excess slack.",
                "failureCriteria": "Belt cannot exit the ribbed hole, or belt hangs loose (no loop tension)."
            },
            "feedback": {
                "successMessage": "Belt through both holes — ready for peg insertion.",
                "failureMessage": "If belt won't pass: check you are pushing through the small ribbed hole, not the smooth one."
            },
            "requiredPartIds": [carriage]
        },
        {
            "id": f"step_{pfx}_orient_peg",
            "name": "Confirm Peg Orientation — Foot Away from Axis Center",
            "sequenceIndex": start_seq + 3,
            "family": "Confirm",
            "guidance": {
                "instructionText": (
                    f"Before inserting peg 1 ({peg_1}), confirm its orientation: "
                    "the flat foot of the peg must point away from the axis center (toward the outside). "
                    "The ribbed grip end inserts into the belt tail."
                ),
                "whyItMattersText": (
                    "Peg foot direction determines where the belt locks. "
                    "Foot toward center means the belt tail points the wrong way and "
                    "the peg cannot be pressed home against the carriage shoulder."
                )
            },
            "validation": {
                "successCriteria": "Peg held with foot facing away from axis center, ribbed grip end toward belt tail.",
                "failureCriteria": "Peg foot facing axis center."
            },
            "feedback": {
                "successMessage": "Peg orientation correct — ready to insert.",
                "failureMessage": "Rotate peg 180° so the flat foot faces outward."
            },
            "requiredPartIds": [peg_1]
        },
        {
            "id": f"step_{pfx}_insert_peg_1",
            "name": "Insert Belt End into Peg 1 — Press into Ribbed Hole",
            "sequenceIndex": start_seq + 4,
            "family": "Use",
            "guidance": {
                "instructionText": (
                    f"Insert the belt tail approximately ¾ inch (20 mm) into peg 1 ({peg_1}). "
                    "Press the peg firmly into the ribbed hole until the shoulder seats against the carriage face. "
                    "Use thumb pressure — no tool needed. A click or definite stop indicates full seating."
                ),
                "whyItMattersText": (
                    "The ¾ inch depth gives the peg enough belt grip length to resist the full belt tension. "
                    "Shallow insertion (< ½ inch) allows the peg to pull free under motor load."
                )
            },
            "validation": {
                "successCriteria": "Peg shoulder flush with carriage face. Belt tail ≥ ¾ inch inside peg. Peg does not pull out by hand.",
                "failureCriteria": "Peg not fully seated (gap at shoulder), or belt tail shorter than ½ inch inside peg."
            },
            "feedback": {
                "successMessage": "Peg 1 locked.",
                "failureMessage": "Push peg in further. If it won't seat, pull more belt tail through the ribbed hole first."
            },
            "requiredPartIds": [peg_1]
        },
        {
            "id": f"step_{pfx}_place_peg_2",
            "name": "Place Peg 2 Loosely — Tension Set at Frame Mount",
            "sequenceIndex": start_seq + 5,
            "family": "Place",
            "guidance": {
                "instructionText": (
                    f"Insert the motor end of the belt into peg 2 ({peg_2}) and press loosely into the "
                    "corresponding carriage hole — do NOT fully seat. "
                    "Peg 2 tension is set during frame mounting when the motor holder position is adjusted."
                ),
                "whyItMattersText": (
                    "Pre-tensioning peg 2 now fixes the belt length before the motor holder is positioned. "
                    "Any motor holder adjustment after seating peg 2 will either over-tension or slack the belt."
                )
            },
            "validation": {
                "successCriteria": "Peg 2 inserted but not fully seated. Belt has slight slack on motor side.",
                "failureCriteria": "Peg 2 fully seated and belt already taut — tension is locked in prematurely."
            },
            "feedback": {
                "successMessage": "Peg 2 placed loosely — tension will be set at frame mounting.",
                "failureMessage": "Back peg 2 out until there is visible slack. It must be tensioned at frame mount."
            },
            "requiredPartIds": [peg_2]
        },
        {
            "id": f"step_{pfx}_travel_test",
            "name": "Travel Test — Belt Moves Full Rod Length Without Rub",
            "sequenceIndex": start_seq + 6,
            "family": "Confirm",
            "guidance": {
                "instructionText": (
                    "Move the carriage by hand from the idler end to the motor holder end and back. "
                    "The belt should move freely with no rub, no catching, and no lateral drift on "
                    "the idler bearing. Full travel = full rod length."
                ),
                "whyItMattersText": (
                    "A belt that rubs or drifts laterally will fray within the first 20 hours of printing. "
                    "This test catches belt routing errors, peg insertion angle, and idler alignment before frame mounting."
                )
            },
            "validation": {
                "successCriteria": (
                    "Carriage moves full rod length in both directions. Belt does not rub frame, halves, or idler edge. "
                    f"{milestone_s}"
                ),
                "failureCriteria": "Belt rubs on any surface, or carriage cannot complete full travel."
            },
            "feedback": {
                "successMessage": milestone_s,
                "failureMessage": (
                    "If belt rubs idler edge: check peg 1 insertion depth — too shallow lets belt drift. "
                    "If carriage binds: check rod alignment and bearing seating in carriage."
                )
            },
            "requiredPartIds": [belt, carriage, idler]
        }
    ]


# ── Template registry ─────────────────────────────────────────────────────────

TEMPLATES = {
    "BearingCarriage": template_bearing_carriage,
    "IdlerHalves": template_idler_halves,
    "MotorHolder": template_motor_holder,
    "RodAssembly": template_rod_assembly,
    "BeltThread": template_belt_thread,
}


# ── Helpers ───────────────────────────────────────────────────────────────────

def _req(parts, key, template_name):
    val = parts.get(key)
    if not val:
        raise ValueError(f"Template '{template_name}' requires parts.{key} — not found in input YAML")
    return val


def _req_list(parts, key, template_name):
    val = parts.get(key)
    if not val:
        raise ValueError(f"Template '{template_name}' requires parts.{key} (list) — not found in input YAML")
    if isinstance(val, str):
        val = [v.strip() for v in val.split(",")]
    return val


def _subassembly_prefix(config):
    """Derive a short prefix for step IDs from the subassembly ID."""
    sa = config.get("subassembly", config.get("assembly", "asm"))
    # e.g. subassembly_y_left_carriage_build → y_left_carriage
    sa = sa.replace("subassembly_", "").replace("assembly_d3d_", "")
    parts_prefix = sa.rstrip("_build").rstrip("_unit")
    return parts_prefix


# ── Main ─────────────────────────────────────────────────────────────────────

def generate(input_path, output_path=None):
    config = load_yaml(input_path)

    template_name = config.get("template")
    if not template_name:
        raise ValueError("Input YAML must specify 'template:' field")

    fn = TEMPLATES.get(template_name)
    if not fn:
        available = ", ".join(TEMPLATES.keys())
        raise ValueError(f"Unknown template '{template_name}'. Available: {available}")

    parts       = config.get("parts", {})
    start_seq   = int(config.get("start_seq", 1))
    pfx         = _subassembly_prefix(config)
    orient_cue  = config.get("orientation_cue", "")
    tool        = config.get("tool", "tool_power_drill")
    torque      = config.get("torque_setting", "lowest")
    milestone   = config.get("milestone", "")

    steps = fn(parts, start_seq, pfx, orient_cue, tool, torque, milestone)

    if output_path is None:
        stem = Path(input_path).stem
        OUTPUTS_DIR.mkdir(parents=True, exist_ok=True)
        output_path = OUTPUTS_DIR / f"{stem}.json"

    Path(output_path).write_text(
        json.dumps(steps, indent=2, ensure_ascii=False),
        encoding="utf-8"
    )

    print(f"Generated {len(steps)} steps  ->  {output_path}")
    print(f"  seqIndex range: {steps[0]['sequenceIndex']} – {steps[-1]['sequenceIndex']}")
    print(f"  Template: {template_name}")
    print(f"  Prefix: {pfx}")
    print()
    print("Next steps:")
    print(f"  1. Review {output_path}")
    print(f"  2. Merge steps[] array into your target assembly file")
    print(f"  3. python tools/package_health.py <packageId> --fix-seqindex")

    return steps


def list_templates():
    print("Available templates:")
    for name in TEMPLATES:
        fn = TEMPLATES[name]
        print(f"  {name} — {fn.__doc__.strip().splitlines()[0]}")


def main():
    args = sys.argv[1:]
    if not args or "--help" in args or "-h" in args:
        print(__doc__)
        sys.exit(0)

    if "--list-templates" in args:
        list_templates()
        sys.exit(0)

    input_file = args[0]
    output_file = None
    if "--output" in args:
        idx = args.index("--output")
        output_file = args[idx + 1]

    if not os.path.exists(input_file):
        # Try resolving relative to inputs dir
        candidate = INPUTS_DIR / input_file
        if candidate.exists():
            input_file = str(candidate)
        else:
            print(f"ERROR: Input file not found: {input_file}")
            sys.exit(1)

    generate(input_file, output_file)


if __name__ == "__main__":
    main()
