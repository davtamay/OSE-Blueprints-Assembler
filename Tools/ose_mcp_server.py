#!/usr/bin/env python3
"""
ose_mcp_server.py — OSE Assembly MCP Server
============================================
Exposes the assembly package data as MCP tools so Claude (and other agents)
can query before writing — no more guessing schemas from examples.

Registration (already done in .claude/settings.json):
  Command: python tools/ose_mcp_server.py
  Working dir: repo root

Available tools:
  get_part            → part definition (assetRef, category, stagingPose, ...)
  list_parts          → all partIds in a package with names
  get_target          → target definition (position, associatedPartId, ...)
  list_targets        → all targetIds in a package with names
  list_templates      → template catalog with parameter signatures and step counts
  get_step_schema     → required/optional fields for a step family (Place/Confirm/Use)
  get_animation_cues  → all animation cue types with parameters and usage notes
  get_part_categories → valid part category values (matches Unity validator allowlist)
  validate_step       → checks a step dict for common errors before writing to file
  generate_steps      → template name + parts dict → step JSON array (calls generate_steps.py logic)
"""

import json
import os
import sys
from pathlib import Path

# Ensure repo root is on path so we can import generate_steps
REPO_ROOT = Path(__file__).parent.parent
sys.path.insert(0, str(REPO_ROOT / "tools"))

import mcp.types as types
from mcp.server import Server
from mcp.server.stdio import stdio_server

import generate_steps as gs

# ── Constants ─────────────────────────────────────────────────────────────────

PACKAGES_DIR = REPO_ROOT / "Assets" / "_Project" / "Data" / "Packages"

VALID_PART_CATEGORIES = ["plate", "bracket", "fastener", "shaft", "panel", "housing", "pipe", "custom"]

STEP_FAMILY_SCHEMA = {
    "Place": {
        "description": "Physical placement of a part into or onto a target. Hand operation — no tools.",
        "required": ["id", "name", "assemblyId", "subassemblyId", "sequenceIndex", "family", "requiredPartIds"],
        "optional": ["targetIds", "stagingPose", "workingOrientation", "optionalPartIds", "guidance"],
        "never_add": ["requiredToolActions"],
        "notes": [
            "requiredPartIds must list every part the trainee physically picks up in this step.",
            "A partId can only appear in requiredPartIds of ONE Place step — it can only be placed once.",
            "Close-halves steps are Place (not Confirm). The second half goes in requiredPartIds.",
            "Finger-tight nut threading is Place — do NOT add requiredToolActions.",
        ],
    },
    "Confirm": {
        "description": "Observation or measurement — no part movement. Tests, inspections, alignment checks.",
        "required": [
            "id", "name", "assemblyId", "subassemblyId", "sequenceIndex", "family",
            "guidance.instructionText", "validation.successCriteria", "validation.failureCriteria",
            "feedback.successMessage", "feedback.failureMessage",
        ],
        "optional": ["requiredPartIds", "guidance.whyItMattersText", "targetIds", "animationCues", "difficulty"],
        "never_add": ["requiredToolActions"],
        "notes": [
            "Every test keyword (shake-test, rod-slide-test, pop-check, dangle-test, travel-test) → Confirm.",
            "requiredPartIds keeps the parts visible at the workbench position during this step.",
            "animationCues.cues with type 'shake' demonstrates the shake motion to the user.",
            "Always include both successCriteria AND failureCriteria.",
        ],
    },
    "Use": {
        "description": "Tool-assisted operation: tighten, drill, cut, press.",
        "required": ["id", "name", "assemblyId", "subassemblyId", "sequenceIndex", "family", "requiredToolActions"],
        "optional": ["requiredPartIds", "workingOrientation", "relevantToolIds", "guidance", "validation", "feedback"],
        "notes": [
            "requiredToolActions must list toolId and actionType.",
            "Add profile: 'Torque' for power drill tightening operations.",
            "requiredPartIds keeps parts visible during the tool operation.",
        ],
    },
}

ANIMATION_CUE_SCHEMA = {
    "shake": {
        "description": "Oscillates target parts to demonstrate a shake test or bearing rattle check.",
        "fields": {
            "type": {"value": "shake", "required": True},
            "targetPartIds": {"type": "string[]", "required": "one of targetPartIds or targetSubassemblyId"},
            "targetSubassemblyId": {"type": "string", "required": "one of targetPartIds or targetSubassemblyId"},
            "shakeAmplitude": {"type": "float", "unit": "metres", "default": 0.01, "recommended": 0.006,
                               "note": "0.006 = 6mm — subtle enough to read, not cartoonish"},
            "shakeFrequency": {"type": "float", "unit": "Hz", "default": 3.0, "recommended": 8.0,
                               "note": "8 Hz = fast hand-shake feel; 3 Hz = slow sway"},
            "shakeAxis": {"type": "Vector3", "default": {"x": 1, "y": 0, "z": 0},
                          "note": "Local-space axis. x=1 = side-to-side, y=1 = up-down"},
            "loop": {"type": "bool", "default": False, "recommended": True,
                     "note": "Set true so animation continues until the step is dismissed"},
            "trigger": {"type": "string", "values": ["onActivate", "afterDelay"], "default": "onActivate"},
            "delaySeconds": {"type": "float", "default": 0.0, "note": "Only used when trigger=afterDelay"},
        },
        "when_to_use": "shake-test Confirm steps — demonstrates the hand-compression + shake motion",
        "example": {
            "type": "shake",
            "targetPartIds": ["y_left_carriage_half_a", "y_left_carriage_half_b"],
            "shakeAmplitude": 0.006,
            "shakeFrequency": 8,
            "shakeAxis": {"x": 1, "y": 0, "z": 0},
            "loop": True,
        },
    },
    "pulse": {
        "description": "Scales the target up and down rhythmically to draw attention.",
        "fields": {
            "type": {"value": "pulse", "required": True},
            "targetPartIds": {"type": "string[]"},
            "durationSeconds": {"type": "float", "default": 1.0},
            "loop": {"type": "bool", "default": False},
        },
        "when_to_use": "Highlight a specific part the user must pick up or inspect.",
    },
    "poseTransition": {
        "description": "Animates a part moving from its staging position to its assembled position.",
        "fields": {
            "type": {"value": "poseTransition", "required": True},
            "targetPartIds": {"type": "string[]"},
            "durationSeconds": {"type": "float", "default": 1.5},
            "easing": {"type": "string", "values": ["smoothStep", "linear", "easeInOut"], "default": "smoothStep"},
        },
        "when_to_use": "Place steps where showing the motion path helps the user understand where the part goes.",
    },
    "orientSubassembly": {
        "description": "Rotates the entire subassembly to a specific orientation for inspection.",
        "fields": {
            "type": {"value": "orientSubassembly", "required": True},
            "targetSubassemblyId": {"type": "string", "required": True},
            "durationSeconds": {"type": "float", "default": 1.0},
        },
        "when_to_use": "Confirm steps that require the user to flip or rotate the assembly to check a surface.",
    },
}

def _extract_params(fn):
    """Extract parameter names from a template function."""
    import inspect
    sig = inspect.signature(fn)
    return list(sig.parameters.keys())


def _step_count(template_name):
    counts = {
        "BearingCarriage": 6,
        "IdlerHalves": 4,
        "MotorHolder": 7,
        "RodAssembly": 5,
        "BeltThread": 7,
    }
    return counts.get(template_name, "?")


TEMPLATE_CATALOG = {
    name: {
        "description": fn.__doc__.strip().splitlines()[0] if fn.__doc__ else "",
        "params": _extract_params(fn) if hasattr(fn, "__code__") else [],
        "step_count": _step_count(name),
    }
    for name, fn in gs.TEMPLATES.items()
}


# ── Package data loader ───────────────────────────────────────────────────────

_cache: dict = {}


def load_package(package_id: str) -> dict:
    """Load and merge all assembly files for a package. Cached."""
    if package_id in _cache:
        return _cache[package_id]

    pkg_dir = PACKAGES_DIR / package_id
    asm_dir = pkg_dir / "assemblies"
    if not asm_dir.is_dir():
        raise ValueError(f"Package not found: {package_id}")

    parts, targets, steps, subassemblies = [], [], [], []
    for fpath in sorted(asm_dir.glob("*.json")):
        data = json.loads(fpath.read_text(encoding="utf-8"))
        parts.extend(data.get("parts", []))
        targets.extend(data.get("targets", []))
        steps.extend(data.get("steps", []))
        subassemblies.extend(data.get("subassemblies", []))

    result = {
        "parts": {p["id"]: p for p in parts},
        "targets": {t["id"]: t for t in targets},
        "steps": steps,
        "subassemblies": {sa["id"]: sa for sa in subassemblies},
    }
    _cache[package_id] = result
    return result


def invalidate_cache(package_id: str = None):
    if package_id:
        _cache.pop(package_id, None)
    else:
        _cache.clear()


# ── Step validator ────────────────────────────────────────────────────────────

def validate_step_dict(step: dict, package_id: str = None) -> list[str]:
    """Return a list of validation error strings for a step dict."""
    errors = []

    sid = step.get("id", "<missing id>")
    family = step.get("family", "")

    if not step.get("id"):
        errors.append("Missing required field: id")
    if not step.get("name"):
        errors.append("Missing required field: name")
    if not step.get("family"):
        errors.append("Missing required field: family")
    if step.get("sequenceIndex") is None:
        errors.append("Missing required field: sequenceIndex")
    if not step.get("assemblyId"):
        errors.append("Missing required field: assemblyId")
    if not step.get("subassemblyId"):
        errors.append("Missing required field: subassemblyId")

    if family == "Place":
        if not step.get("requiredPartIds"):
            errors.append("Place step must have requiredPartIds")
        if step.get("requiredToolActions"):
            errors.append("Place step must NOT have requiredToolActions (hand operation only)")

    elif family == "Confirm":
        guidance = step.get("guidance", {})
        validation = step.get("validation", {})
        feedback = step.get("feedback", {})
        if not guidance.get("instructionText"):
            errors.append("Confirm step must have guidance.instructionText")
        if not validation.get("successCriteria"):
            errors.append("Confirm step must have validation.successCriteria")
        if not validation.get("failureCriteria"):
            errors.append("Confirm step must have validation.failureCriteria")
        if not feedback.get("successMessage"):
            errors.append("Confirm step must have feedback.successMessage")
        if not feedback.get("failureMessage"):
            errors.append("Confirm step must have feedback.failureMessage")
        if step.get("requiredToolActions"):
            errors.append("Confirm step must NOT have requiredToolActions")

    elif family == "Use":
        if not step.get("requiredToolActions"):
            errors.append("Use step must have requiredToolActions")

    # Check part categories if parts are defined inline
    # Check against package if provided
    if package_id:
        try:
            pkg = load_package(package_id)
            for pid in step.get("requiredPartIds", []):
                if pid not in pkg["parts"]:
                    errors.append(f"requiredPartIds: '{pid}' not found in package parts")
            for tid in step.get("targetIds", []):
                if tid not in pkg["targets"]:
                    errors.append(f"targetIds: '{tid}' not found in package targets")
        except ValueError as e:
            errors.append(f"Package lookup failed: {e}")

    return errors


# ── MCP Server ────────────────────────────────────────────────────────────────

server = Server("ose-assembly")


@server.list_tools()
async def list_tools() -> list[types.Tool]:
    return [
        types.Tool(
            name="get_part",
            description="Get the full definition of a part by ID — assetRef, category, stagingPose, function, etc.",
            inputSchema={
                "type": "object",
                "properties": {
                    "package_id": {"type": "string", "description": "Package ID (e.g. d3d_v18_10)"},
                    "part_id": {"type": "string", "description": "Part ID to look up"},
                },
                "required": ["package_id", "part_id"],
            },
        ),
        types.Tool(
            name="list_parts",
            description="List all part IDs and names in a package. Use to discover part IDs before authoring steps.",
            inputSchema={
                "type": "object",
                "properties": {
                    "package_id": {"type": "string"},
                    "filter": {"type": "string", "description": "Optional substring filter on part ID or name"},
                },
                "required": ["package_id"],
            },
        ),
        types.Tool(
            name="get_target",
            description="Get the full definition of a target by ID — position, associatedPartId, orientation, etc.",
            inputSchema={
                "type": "object",
                "properties": {
                    "package_id": {"type": "string"},
                    "target_id": {"type": "string"},
                },
                "required": ["package_id", "target_id"],
            },
        ),
        types.Tool(
            name="list_targets",
            description="List all target IDs and their associated part IDs in a package.",
            inputSchema={
                "type": "object",
                "properties": {
                    "package_id": {"type": "string"},
                    "filter": {"type": "string", "description": "Optional substring filter on target ID"},
                },
                "required": ["package_id"],
            },
        ),
        types.Tool(
            name="list_templates",
            description="List all available procedure templates with parameter signatures and step counts.",
            inputSchema={"type": "object", "properties": {}},
        ),
        types.Tool(
            name="get_step_schema",
            description="Get the required/optional fields and rules for a step family (Place, Confirm, or Use).",
            inputSchema={
                "type": "object",
                "properties": {
                    "family": {"type": "string", "enum": ["Place", "Confirm", "Use"]},
                },
                "required": ["family"],
            },
        ),
        types.Tool(
            name="get_animation_cues",
            description="Get all animation cue types with their parameters, defaults, and usage notes. Query this before adding animationCues to any step.",
            inputSchema={
                "type": "object",
                "properties": {
                    "cue_type": {"type": "string", "description": "Optional: filter to one cue type (shake, pulse, poseTransition, orientSubassembly)"},
                },
            },
        ),
        types.Tool(
            name="get_part_categories",
            description="Get the list of valid part category values. These are enforced by the Unity validator.",
            inputSchema={"type": "object", "properties": {}},
        ),
        types.Tool(
            name="validate_step",
            description="Validate a step dict before writing it to an assembly file. Returns a list of errors (empty = valid).",
            inputSchema={
                "type": "object",
                "properties": {
                    "step": {"type": "object", "description": "The step JSON object to validate"},
                    "package_id": {"type": "string", "description": "Optional: also check part/target IDs exist in this package"},
                },
                "required": ["step"],
            },
        ),
        types.Tool(
            name="generate_steps",
            description="Generate a full step array from a template name and part ID mapping. Returns step JSON array ready to merge into an assembly file.",
            inputSchema={
                "type": "object",
                "properties": {
                    "template": {"type": "string", "description": "Template name (e.g. BearingCarriage)"},
                    "parts": {"type": "object", "description": "Part role -> partId mapping (e.g. {half_a: 'y_left_carriage_half_a', ...})"},
                    "start_seq": {"type": "integer", "description": "First sequenceIndex for generated steps"},
                    "prefix": {"type": "string", "description": "Short prefix for step IDs (e.g. y_left_carriage)"},
                    "orientation_cue": {"type": "string", "default": ""},
                    "tool": {"type": "string", "default": "tool_power_drill"},
                    "torque_setting": {"type": "string", "default": "lowest"},
                    "milestone": {"type": "string", "default": ""},
                },
                "required": ["template", "parts", "start_seq", "prefix"],
            },
        ),
    ]


@server.call_tool()
async def call_tool(name: str, arguments: dict) -> list[types.TextContent]:
    def respond(data) -> list[types.TextContent]:
        text = json.dumps(data, indent=2, ensure_ascii=False) if not isinstance(data, str) else data
        return [types.TextContent(type="text", text=text)]

    try:
        if name == "get_part":
            pkg = load_package(arguments["package_id"])
            part = pkg["parts"].get(arguments["part_id"])
            if not part:
                return respond({"error": f"Part '{arguments['part_id']}' not found in {arguments['package_id']}"})
            return respond(part)

        elif name == "list_parts":
            pkg = load_package(arguments["package_id"])
            f = (arguments.get("filter") or "").lower()
            results = [
                {"id": pid, "name": p.get("name", p.get("displayName", "")), "category": p.get("category", ""), "assetRef": p.get("assetRef", "")}
                for pid, p in sorted(pkg["parts"].items())
                if not f or f in pid.lower() or f in (p.get("name", "") + p.get("displayName", "")).lower()
            ]
            return respond({"count": len(results), "parts": results})

        elif name == "get_target":
            pkg = load_package(arguments["package_id"])
            target = pkg["targets"].get(arguments["target_id"])
            if not target:
                return respond({"error": f"Target '{arguments['target_id']}' not found"})
            return respond(target)

        elif name == "list_targets":
            pkg = load_package(arguments["package_id"])
            f = (arguments.get("filter") or "").lower()
            results = [
                {"id": tid, "associatedPartId": t.get("associatedPartId", ""), "toolActionType": t.get("toolActionType", "")}
                for tid, t in sorted(pkg["targets"].items())
                if not f or f in tid.lower()
            ]
            return respond({"count": len(results), "targets": results})

        elif name == "list_templates":
            catalog = {}
            for tname, fn in gs.TEMPLATES.items():
                doc = (fn.__doc__ or "").strip().splitlines()[0]
                catalog[tname] = {
                    "description": doc,
                    "step_count": _step_count(tname),
                    "usage": f"generate_steps template={tname} parts={{role: partId, ...}} start_seq=N prefix=my_prefix",
                }
            return respond(catalog)

        elif name == "get_step_schema":
            family = arguments["family"]
            schema = STEP_FAMILY_SCHEMA.get(family)
            if not schema:
                return respond({"error": f"Unknown family '{family}'. Valid: Place, Confirm, Use"})
            return respond(schema)

        elif name == "get_animation_cues":
            cue_type = arguments.get("cue_type")
            if cue_type:
                cue = ANIMATION_CUE_SCHEMA.get(cue_type)
                if not cue:
                    return respond({"error": f"Unknown cue type '{cue_type}'. Valid: {list(ANIMATION_CUE_SCHEMA.keys())}"})
                return respond(cue)
            return respond(ANIMATION_CUE_SCHEMA)

        elif name == "get_part_categories":
            return respond({
                "valid_categories": VALID_PART_CATEGORIES,
                "note": "These are enforced by Unity MachineJsonPrePlayValidator. Use 'shaft' for rods, 'fastener' for bolts/nuts, 'custom' for printed parts.",
            })

        elif name == "validate_step":
            step = arguments["step"]
            package_id = arguments.get("package_id")
            errors = validate_step_dict(step, package_id)
            return respond({
                "valid": len(errors) == 0,
                "errors": errors,
            })

        elif name == "generate_steps":
            template_name = arguments["template"]
            fn = gs.TEMPLATES.get(template_name)
            if not fn:
                return respond({"error": f"Unknown template '{template_name}'. Available: {list(gs.TEMPLATES.keys())}"})
            parts = arguments["parts"]
            start_seq = arguments["start_seq"]
            prefix = arguments["prefix"]
            orient = arguments.get("orientation_cue", "")
            tool = arguments.get("tool", "tool_power_drill")
            torque = arguments.get("torque_setting", "lowest")
            milestone = arguments.get("milestone", "")
            steps = fn(parts, start_seq, prefix, orient, tool, torque, milestone)
            return respond({"step_count": len(steps), "steps": steps})

        else:
            return respond({"error": f"Unknown tool: {name}"})

    except Exception as e:
        return respond({"error": str(e)})


# ── Entry point ───────────────────────────────────────────────────────────────

async def main():
    async with stdio_server() as (read_stream, write_stream):
        await server.run(read_stream, write_stream, server.create_initialization_options())


if __name__ == "__main__":
    import asyncio
    asyncio.run(main())
