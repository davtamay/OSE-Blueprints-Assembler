#!/usr/bin/env python3
"""
validate_machine_json.py — Python mirror of MachinePackageValidator.cs

Usage (CLI):
    python3 validate_machine_json.py d3d_v18_10/machine.json
    python3 validate_machine_json.py          # auto-discovers all machine.json files

Usage (module):
    from validate_machine_json import validate
    result = validate(data_dict)   # returns ValidationResult
    result = validate_file("path/to/machine.json")

Exit codes:
    0  — clean (no errors, no warnings)
    1  — errors present
    2  — warnings only
"""

from __future__ import annotations

import io
import json
import math
import os
import sys
from dataclasses import dataclass, field
from pathlib import Path
from typing import Any

# Ensure stdout can handle Unicode on Windows (e.g. cp1252 terminals)
if sys.stdout and hasattr(sys.stdout, "buffer"):
    sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8", errors="replace")

# ── ANSI colour helpers ───────────────────────────────────────────────────────

_NO_COLOR = not sys.stdout.isatty() or os.environ.get("NO_COLOR")

def _red(s: str) -> str:     return s if _NO_COLOR else f"\033[31m{s}\033[0m"
def _yellow(s: str) -> str:  return s if _NO_COLOR else f"\033[33m{s}\033[0m"
def _green(s: str) -> str:   return s if _NO_COLOR else f"\033[32m{s}\033[0m"
def _bold(s: str) -> str:    return s if _NO_COLOR else f"\033[1m{s}\033[0m"
def _dim(s: str) -> str:     return s if _NO_COLOR else f"\033[2m{s}\033[0m"


# ── Domain constants (mirrors C# HashSet fields) ─────────────────────────────

DIFFICULTY_VALUES       = {"beginner", "intermediate", "advanced"}
RECOMMENDED_MODE_VALUES = {"tutorial", "guided", "standard", "challenge"}
PART_CATEGORY_VALUES    = {"plate", "bracket", "fastener", "shaft", "panel", "housing", "pipe", "custom"}
TOOL_CATEGORY_VALUES    = {"hand_tool", "power_tool", "measurement", "safety", "specialty"}
COMPLETION_TYPE_VALUES  = {"placement", "tool_action", "confirmation", "pipe_connection"}
FAMILY_VALUES           = {"Place", "Use", "Connect", "Confirm"}
VIEW_MODE_VALUES        = {"SourceAndTarget", "PairEndpoints", "WorkZone", "PathView", "Overview", "Inspect"}
TARGET_ORDER_VALUES     = {"sequential", "parallel"}
VALIDATION_TYPE_VALUES  = {"placement", "orientation", "part_identity", "dependency", "multi_part", "confirmation"}
HINT_TYPE_VALUES        = {"text", "highlight", "ghost", "directional", "explanatory", "tool_reminder"}
HINT_PRIORITY_VALUES    = {"low", "medium", "high"}
TOOL_ACTION_TYPE_VALUES = {"measure", "tighten", "strike", "weld_pass", "grind_pass"}
EFFECT_TYPE_VALUES      = {"placement_feedback", "success_feedback", "error_feedback", "welding", "sparks", "heat_glow", "fire", "dust", "milestone"}
EFFECT_TRIGGER_VALUES   = {"on_step_enter", "on_valid_candidate", "on_success", "on_failure", "on_completion"}
SOURCE_TYPE_VALUES      = {"blueprint", "photo", "diagram", "author_note", "reference_doc"}
HINT_AVAILABILITY_VALUES = {"always", "limited", "none"}
PROFILE_VALUES          = {"Clamp", "AxisFit", "Torque", "Weld", "Cut", "Strike", "Measure", "SquareCheck", "Cable", "WireConnect"}

POSITION_TOLERANCE = 0.001


# ── Issue model ───────────────────────────────────────────────────────────────

@dataclass
class Issue:
    severity: str   # "error" | "warning"
    path: str
    message: str

    def category(self) -> str:
        """Top-level collection name for grouping (e.g. 'hints', 'steps', '$')."""
        return self.path.split("[")[0].split(".")[0]


@dataclass
class ValidationResult:
    issues: list[Issue] = field(default_factory=list)

    @property
    def errors(self) -> list[Issue]:
        return [i for i in self.issues if i.severity == "error"]

    @property
    def warnings(self) -> list[Issue]:
        return [i for i in self.issues if i.severity == "warning"]

    def ok(self) -> bool:
        return not self.issues

    def exit_code(self) -> int:
        if self.errors:
            return 1
        if self.warnings:
            return 2
        return 0


# ── Low-level primitives ──────────────────────────────────────────────────────

def _err(path: str, msg: str) -> Issue:
    return Issue("error", path, msg)

def _warn(path: str, msg: str) -> Issue:
    return Issue("warning", path, msg)

def _is_blank(v: Any) -> bool:
    return v is None or (isinstance(v, str) and not v.strip())

def _check_required_text(value: Any, path: str, issues: list[Issue]) -> None:
    if _is_blank(value):
        issues.append(_err(path, "A non-empty value is required."))

def _check_required_enum(value: Any, allowed: set, path: str, issues: list[Issue]) -> None:
    if _is_blank(value):
        issues.append(_err(path, "A non-empty enum value is required."))
        return
    if not isinstance(value, str) or value not in allowed:
        issues.append(_err(path, f"Value '{value}' is not allowed. Expected: {'|'.join(sorted(allowed))}"))

def _check_optional_enum(value: Any, allowed: set, path: str, issues: list[Issue]) -> None:
    if value is None or (isinstance(value, str) and not value.strip()):
        return
    if not isinstance(value, str) or value not in allowed:
        issues.append(_err(path, f"Value '{value}' is not allowed. Expected: {'|'.join(sorted(allowed))}"))

def _check_single_ref(id_val: Any, ref_set: set, path: str, issues: list[Issue]) -> None:
    """Required single reference: must be non-blank AND exist in ref_set."""
    _check_required_text(id_val, path, issues)
    if not _is_blank(id_val) and id_val not in ref_set:
        issues.append(_err(path, f"Reference '{id_val}' does not resolve."))

def _check_optional_ref(id_val: Any, ref_set: set, path: str, issues: list[Issue]) -> None:
    if not _is_blank(id_val) and id_val not in ref_set:
        issues.append(_err(path, f"Reference '{id_val}' does not resolve."))

def _check_required_refs(ids: Any, ref_set: set, path: str, issues: list[Issue]) -> None:
    if not ids:
        issues.append(_err(path, "At least one reference is required."))
        return
    _check_optional_refs(ids, ref_set, path, issues)

def _check_optional_refs(ids: Any, ref_set: set, path: str, issues: list[Issue]) -> None:
    if ids is None:
        return
    for i, id_val in enumerate(ids):
        item_path = f"{path}[{i}]"
        if _is_blank(id_val):
            issues.append(_err(item_path, "Reference id cannot be empty."))
            continue
        if id_val not in ref_set:
            issues.append(_err(item_path, f"Reference '{id_val}' does not resolve."))

def _build_id_set(items: list, collection: str, issues: list[Issue]) -> set:
    """Build a set of ids from a list of dicts; report missing/duplicate ids."""
    ids: set = set()
    if not items:
        return ids
    for i, item in enumerate(items):
        path = f"{collection}[{i}]"
        if item is None:
            issues.append(_err(path, "Collection entry is null."))
            continue
        id_val = item.get("id", "")
        if _is_blank(id_val):
            issues.append(_err(f"{path}.id", "A stable id is required."))
            continue
        if id_val in ids:
            issues.append(_err(f"{path}.id", f"Duplicate id '{id_val}' found in {collection}."))
        else:
            ids.add(id_val)
    return ids


# ── Section validators ────────────────────────────────────────────────────────

def _validate_machine(machine: dict, issues: list[Issue]) -> None:
    _check_required_text(machine.get("id"), "machine.id", issues)
    _check_required_text(machine.get("name"), "machine.name", issues)
    _check_required_text(machine.get("description"), "machine.description", issues)
    _check_required_enum(machine.get("difficulty"), DIFFICULTY_VALUES, "machine.difficulty", issues)
    _check_optional_enum(machine.get("recommendedMode"), RECOMMENDED_MODE_VALUES, "machine.recommendedMode", issues)

    objectives = machine.get("learningObjectives") or []
    if not objectives:
        issues.append(_warn("machine.learningObjectives", "At least one learning objective is recommended."))


def _validate_machine_references(machine: dict, assembly_ids: set, issues: list[Issue]) -> None:
    if machine is None:
        return
    _check_required_refs(machine.get("entryAssemblyIds"), assembly_ids, "machine.entryAssemblyIds", issues)

    source_refs = machine.get("sourceReferences") or []
    for i, sr in enumerate(source_refs):
        path = f"machine.sourceReferences[{i}]"
        if sr is None:
            issues.append(_err(path, "Source reference entry is null."))
            continue
        _check_required_text(sr.get("title"), f"{path}.title", issues)
        _check_required_enum(sr.get("type"), SOURCE_TYPE_VALUES, f"{path}.type", issues)


def _validate_assemblies(
    assemblies: list,
    machine_id: str,
    subassembly_ids: set,
    step_ids: set,
    assembly_ids: set,
    issues: list[Issue],
) -> None:
    for i, asm in enumerate(assemblies):
        path = f"assemblies[{i}]"
        if asm is None:
            issues.append(_err(path, "Assembly definition is null."))
            continue
        _check_required_text(asm.get("name"), f"{path}.name", issues)
        _check_required_text(asm.get("machineId"), f"{path}.machineId", issues)
        mid = asm.get("machineId", "")
        if machine_id and mid and mid.lower() != machine_id.lower():
            issues.append(_err(f"{path}.machineId",
                               f"Assembly '{asm.get('id')}' references machine '{mid}', expected '{machine_id}'."))
        _check_required_refs(asm.get("subassemblyIds"), subassembly_ids, f"{path}.subassemblyIds", issues)
        _check_required_refs(asm.get("stepIds"), step_ids, f"{path}.stepIds", issues)
        _check_optional_refs(asm.get("dependencyAssemblyIds"), assembly_ids, f"{path}.dependencyAssemblyIds", issues)


def _validate_subassemblies(
    subassemblies: list,
    assembly_ids: set,
    part_ids: set,
    step_ids: set,
    issues: list[Issue],
) -> None:
    for i, sa in enumerate(subassemblies):
        path = f"subassemblies[{i}]"
        if sa is None:
            issues.append(_err(path, "Subassembly definition is null."))
            continue
        _check_required_text(sa.get("name"), f"{path}.name", issues)
        _check_single_ref(sa.get("assemblyId"), assembly_ids, f"{path}.assemblyId", issues)
        _check_required_refs(sa.get("partIds"), part_ids, f"{path}.partIds", issues)
        _check_required_refs(sa.get("stepIds"), step_ids, f"{path}.stepIds", issues)


def _validate_parts(parts: list, tool_ids: set, issues: list[Issue]) -> None:
    for i, part in enumerate(parts):
        path = f"parts[{i}]"
        if part is None:
            issues.append(_err(path, "Part definition is null."))
            continue
        _check_required_text(part.get("name"), f"{path}.name", issues)
        _check_required_enum(part.get("category"), PART_CATEGORY_VALUES, f"{path}.category", issues)
        _check_required_text(part.get("material"), f"{path}.material", issues)
        _check_required_text(part.get("function"), f"{path}.function", issues)
        qty = part.get("quantity")
        if qty is not None and (not isinstance(qty, (int, float)) or qty < 1):
            issues.append(_err(f"{path}.quantity", "Part quantity must be at least 1."))


def _validate_tools(tools: list, issues: list[Issue]) -> None:
    for i, tool in enumerate(tools):
        path = f"tools[{i}]"
        if tool is None:
            issues.append(_err(path, "Tool definition is null."))
            continue
        _check_required_text(tool.get("name"), f"{path}.name", issues)
        _check_required_enum(tool.get("category"), TOOL_CATEGORY_VALUES, f"{path}.category", issues)
        _check_required_text(tool.get("purpose"), f"{path}.purpose", issues)
        _check_required_text(tool.get("assetRef"), f"{path}.assetRef", issues)


def _validate_tool_actions(
    actions: list,
    tool_ids: set,
    target_ids: set,
    path: str,
    issues: list[Issue],
) -> None:
    if not actions:
        return
    for i, action in enumerate(actions):
        ap = f"{path}[{i}]"
        if action is None:
            issues.append(_err(ap, "Tool action definition is null."))
            continue
        _check_single_ref(action.get("toolId"), tool_ids, f"{ap}.toolId", issues)
        _check_required_enum(action.get("actionType"), TOOL_ACTION_TYPE_VALUES, f"{ap}.actionType", issues)
        _check_optional_ref(action.get("targetId"), target_ids, f"{ap}.targetId", issues)
        rc = action.get("requiredCount")
        if rc is not None and (not isinstance(rc, (int, float)) or rc < 1):
            issues.append(_err(f"{ap}.requiredCount", "Tool action requiredCount must be at least 1."))


def _validate_steps(
    steps: list,
    assembly_ids: set,
    subassembly_ids: set,
    part_ids: set,
    tool_ids: set,
    target_ids: set,
    targets_list: list,
    validation_rule_ids: set,
    hint_ids: set,
    effect_ids: set,
    tool_defs: dict,
    issues: list[Issue],
) -> None:
    # Build target lookup: id -> target dict
    target_lookup: dict[str, dict] = {}
    for t in (targets_list or []):
        if t and not _is_blank(t.get("id")):
            target_lookup[t["id"]] = t

    sequence_by_assembly: dict[str, set] = {}

    for i, step in enumerate(steps):
        path = f"steps[{i}]"
        if step is None:
            issues.append(_err(path, "Step definition is null."))
            continue

        _check_required_text(step.get("name"), f"{path}.name", issues)
        _check_single_ref(step.get("assemblyId"), assembly_ids, f"{path}.assemblyId", issues)
        _check_optional_ref(step.get("subassemblyId"), subassembly_ids, f"{path}.subassemblyId", issues)
        _check_required_text(step.get("instructionText"), f"{path}.instructionText", issues)

        family = step.get("family", "")
        completion_type = step.get("completionType", "")

        # completionType is required when family is absent
        if _is_blank(family):
            _check_required_enum(completion_type, COMPLETION_TYPE_VALUES, f"{path}.completionType", issues)
        else:
            _check_optional_enum(completion_type, COMPLETION_TYPE_VALUES, f"{path}.completionType", issues)

        _check_optional_enum(family, FAMILY_VALUES, f"{path}.family", issues)
        _check_optional_enum(step.get("profile"), PROFILE_VALUES, f"{path}.profile", issues)
        _check_optional_enum(step.get("viewMode"), VIEW_MODE_VALUES, f"{path}.viewMode", issues)
        _check_optional_enum(step.get("targetOrder"), TARGET_ORDER_VALUES, f"{path}.targetOrder", issues)

        _check_optional_refs(step.get("requiredPartIds"), part_ids, f"{path}.requiredPartIds", issues)
        _check_optional_ref(step.get("requiredSubassemblyId"), subassembly_ids, f"{path}.requiredSubassemblyId", issues)
        _check_optional_refs(step.get("optionalPartIds"), part_ids, f"{path}.optionalPartIds", issues)
        _check_optional_refs(step.get("relevantToolIds"), tool_ids, f"{path}.relevantToolIds", issues)
        _check_optional_refs(step.get("targetIds"), target_ids, f"{path}.targetIds", issues)
        _check_optional_refs(step.get("validationRuleIds"), validation_rule_ids, f"{path}.validationRuleIds", issues)
        _check_optional_refs(step.get("hintIds"), hint_ids, f"{path}.hintIds", issues)
        _check_optional_refs(step.get("effectTriggerIds"), effect_ids, f"{path}.effectTriggerIds", issues)

        _validate_tool_actions(step.get("requiredToolActions"), tool_ids, target_ids, f"{path}.requiredToolActions", issues)

        # Payload sub-objects
        guidance = step.get("guidance")
        if guidance:
            _check_optional_refs(guidance.get("hintIds"), hint_ids, f"{path}.guidance.hintIds", issues)
        validation_payload = step.get("validation")
        if validation_payload:
            _check_optional_refs(validation_payload.get("validationRuleIds"), validation_rule_ids, f"{path}.validation.validationRuleIds", issues)
        feedback = step.get("feedback")
        if feedback:
            _check_optional_refs(feedback.get("effectTriggerIds"), effect_ids, f"{path}.feedback.effectTriggerIds", issues)
        difficulty = step.get("difficulty")
        if difficulty:
            _check_optional_enum(difficulty.get("hintAvailability"), HINT_AVAILABILITY_VALUES, f"{path}.difficulty.hintAvailability", issues)
            tls = difficulty.get("timeLimitSeconds")
            if tls is not None and isinstance(tls, (int, float)) and tls < 0:
                issues.append(_err(f"{path}.difficulty.timeLimitSeconds", "Time limit cannot be negative."))

        # sequenceIndex
        seq = step.get("sequenceIndex")
        if seq is None or not isinstance(seq, int) or seq < 1:
            issues.append(_err(f"{path}.sequenceIndex", "Step sequenceIndex must be at least 1."))
        else:
            asm_key = step.get("assemblyId") or "__missing__"
            if asm_key not in sequence_by_assembly:
                sequence_by_assembly[asm_key] = set()
            if seq in sequence_by_assembly[asm_key]:
                issues.append(_warn(f"{path}.sequenceIndex",
                                    f"Sequence index '{seq}' is reused inside assembly '{asm_key}'."))
            else:
                sequence_by_assembly[asm_key].add(seq)

        # Tool action cross-references
        _validate_tool_action_cross_references(step, path, issues)

        # requiredSubassemblyId + requiredPartIds mutual exclusion
        rsa_id = step.get("requiredSubassemblyId", "")
        rpart_ids = step.get("requiredPartIds") or []
        if not _is_blank(rsa_id) and rpart_ids:
            issues.append(_err(path, "A step may define either requiredPartIds or requiredSubassemblyId, not both."))

        if not _is_blank(rsa_id):
            resolved_family = _resolve_family(step)
            if resolved_family != "Place":
                issues.append(_err(f"{path}.requiredSubassemblyId",
                                   "Subassembly placement is only supported on Place-family steps."))
            step_target_ids = step.get("targetIds") or []
            if len(step_target_ids) != 1:
                issues.append(_err(f"{path}.targetIds",
                                   "A subassembly placement step must reference exactly one target in v1."))
            elif step_target_ids[0] in target_lookup:
                tgt = target_lookup[step_target_ids[0]]
                if (tgt.get("associatedSubassemblyId") or "").lower() != rsa_id.lower():
                    issues.append(_err(f"{path}.targetIds[0]",
                                       f"Target '{tgt.get('id')}' must reference associatedSubassemblyId '{rsa_id}'."))

        # AxisFit without subassembly
        profile = step.get("profile", "")
        resolved_family = _resolve_family(step)
        if profile == "AxisFit" and resolved_family == "Place" and _is_blank(rsa_id):
            issues.append(_err(f"{path}.profile",
                               "AxisFit is only supported on Place-family subassembly placement steps."))

        # Persistent tool check for Clamp / AxisFit placement
        if resolved_family == "Place" and profile in ("Clamp", "AxisFit"):
            for tid in (step.get("relevantToolIds") or []):
                if not _is_blank(tid) and tid in tool_defs:
                    if not tool_defs[tid].get("persistent", False):
                        issues.append(_warn(f"{path}.relevantToolIds",
                                            f"Tool '{tid}' is used in a {profile} step but ToolDefinition.persistent is false. "
                                            f"Set persistent = true in machine.json so PersistentToolController tracks it."))


def _resolve_family(step: dict) -> str:
    """Returns the resolved family string ('Place'|'Use'|'Connect'|'Confirm')."""
    family = step.get("family", "")
    if not _is_blank(family) and family in FAMILY_VALUES:
        return family
    ct = step.get("completionType", "")
    legacy = {
        "placement": "Place",
        "tool_action": "Use",
        "pipe_connection": "Connect",
        "confirmation": "Confirm",
    }
    return legacy.get(ct.lower() if ct else "", "Place")


def _validate_tool_action_cross_references(step: dict, path: str, issues: list[Issue]) -> None:
    actions = step.get("requiredToolActions")
    if not actions:
        return
    step_target_ids = {t.lower() for t in (step.get("targetIds") or []) if t}
    step_tool_ids = {t.lower() for t in (step.get("relevantToolIds") or []) if t}

    for i, action in enumerate(actions):
        if action is None:
            continue
        target_id = action.get("targetId", "")
        tool_id = action.get("toolId", "")
        if not _is_blank(target_id) and target_id.lower() not in step_target_ids:
            issues.append(_warn(f"{path}.requiredToolActions[{i}].targetId",
                                f"Tool action target '{target_id}' is not listed in step's targetIds. Preview/marker may not spawn."))
        if not _is_blank(tool_id) and tool_id.lower() not in step_tool_ids:
            issues.append(_warn(f"{path}.requiredToolActions[{i}].toolId",
                                f"Tool action tool '{tool_id}' is not listed in step's relevantToolIds. Tool may not be offered to user."))


def _validate_validation_rules(
    rules: list,
    part_ids: set,
    step_ids: set,
    target_ids: set,
    hint_ids: set,
    issues: list[Issue],
) -> None:
    for i, rule in enumerate(rules):
        path = f"validationRules[{i}]"
        if rule is None:
            issues.append(_err(path, "Validation rule definition is null."))
            continue
        _check_required_enum(rule.get("type"), VALIDATION_TYPE_VALUES, f"{path}.type", issues)
        _check_optional_ref(rule.get("targetId"), target_ids, f"{path}.targetId", issues)
        _check_optional_ref(rule.get("expectedPartId"), part_ids, f"{path}.expectedPartId", issues)
        _check_optional_refs(rule.get("requiredStepIds"), step_ids, f"{path}.requiredStepIds", issues)
        _check_optional_refs(rule.get("requiredPartIds"), part_ids, f"{path}.requiredPartIds", issues)
        _check_optional_ref(rule.get("correctionHintId"), hint_ids, f"{path}.correctionHintId", issues)


def _validate_hints(hints: list, part_ids: set, tool_ids: set, target_ids: set, issues: list[Issue]) -> None:
    for i, hint in enumerate(hints):
        path = f"hints[{i}]"
        if hint is None:
            issues.append(_err(path, "Hint definition is null."))
            continue
        _check_required_enum(hint.get("type"), HINT_TYPE_VALUES, f"{path}.type", issues)
        # C# checks hint.message; the JSON field is "message"
        _check_required_text(hint.get("message"), f"{path}.message", issues)
        _check_optional_enum(hint.get("priority"), HINT_PRIORITY_VALUES, f"{path}.priority", issues)
        _check_optional_ref(hint.get("targetId"), target_ids, f"{path}.targetId", issues)
        _check_optional_ref(hint.get("partId"), part_ids, f"{path}.partId", issues)
        _check_optional_ref(hint.get("toolId"), tool_ids, f"{path}.toolId", issues)


def _validate_effects(effects: list, issues: list[Issue]) -> None:
    for i, effect in enumerate(effects):
        path = f"effects[{i}]"
        if effect is None:
            issues.append(_err(path, "Effect definition is null."))
            continue
        _check_required_enum(effect.get("type"), EFFECT_TYPE_VALUES, f"{path}.type", issues)
        _check_optional_enum(effect.get("triggerPolicy"), EFFECT_TRIGGER_VALUES, f"{path}.triggerPolicy", issues)


def _validate_targets(targets: list, part_ids: set, subassembly_ids: set, issues: list[Issue]) -> None:
    for i, target in enumerate(targets):
        path = f"targets[{i}]"
        if target is None:
            issues.append(_err(path, "Target definition is null."))
            continue
        _check_required_text(target.get("anchorRef"), f"{path}.anchorRef", issues)
        _check_optional_ref(target.get("associatedPartId"), part_ids, f"{path}.associatedPartId", issues)
        _check_optional_ref(target.get("associatedSubassemblyId"), subassembly_ids, f"{path}.associatedSubassemblyId", issues)
        if not _is_blank(target.get("associatedPartId")) and not _is_blank(target.get("associatedSubassemblyId")):
            issues.append(_err(path, "A target may define either associatedPartId or associatedSubassemblyId, not both."))


def _detect_orphan_parts(
    parts: list,
    steps: list,
    targets: list,
    issues: list[Issue],
) -> None:
    if not parts:
        return
    referenced: set = set()
    for step in (steps or []):
        if step is None:
            continue
        for pid in (step.get("requiredPartIds") or []):
            if pid:
                referenced.add(pid)
        for pid in (step.get("optionalPartIds") or []):
            if pid:
                referenced.add(pid)
    for t in (targets or []):
        if t and not _is_blank(t.get("associatedPartId")):
            referenced.add(t["associatedPartId"])
    for i, part in enumerate(parts):
        if part and not _is_blank(part.get("id")) and part["id"] not in referenced:
            issues.append(_warn(f"parts[{i}]",
                                f"Part '{part['id']}' is defined but never referenced by any step or target."))


def _detect_orphan_targets(targets: list, steps: list, issues: list[Issue]) -> None:
    if not targets:
        return
    referenced: set = set()
    for step in (steps or []):
        if step is None:
            continue
        for tid in (step.get("targetIds") or []):
            if tid:
                referenced.add(tid)
    for i, target in enumerate(targets):
        if target and not _is_blank(target.get("id")) and target["id"] not in referenced:
            issues.append(_warn(f"targets[{i}]",
                                f"Target '{target['id']}' is defined but never referenced by any step."))


def _detect_orphan_steps(steps: list, assembly_ids: set, issues: list[Issue]) -> None:
    if not steps:
        return
    for i, step in enumerate(steps):
        if step is None:
            continue
        asm_id = step.get("assemblyId", "")
        if _is_blank(asm_id):
            issues.append(_warn(f"steps[{i}]",
                                f"Step '{step.get('id')}' has no assemblyId — it won't appear in any assembly."))
        elif asm_id not in assembly_ids:
            issues.append(_warn(f"steps[{i}]",
                                f"Step '{step.get('id')}' references assemblyId '{asm_id}' which does not exist."))


def _validate_contiguous_sequence(steps: list, issues: list[Issue]) -> None:
    if not steps:
        return
    valid_steps = [s for s in steps if s is not None and isinstance(s.get("sequenceIndex"), int)]
    sorted_steps = sorted(valid_steps, key=lambda s: s["sequenceIndex"])
    for i, step in enumerate(sorted_steps):
        expected = i + 1
        if step["sequenceIndex"] != expected:
            issues.append(_err("steps",
                               f"sequenceIndex gap or shift: step '{step.get('id')}' has sequenceIndex {step['sequenceIndex']}, "
                               f"expected {expected}. Indices must be contiguous 1..{len(sorted_steps)}."))
            break


def _validate_preview_config(
    data: dict,
    part_ids: set,
    target_ids: set,
    subassembly_ids: set,
    issues: list[Issue],
) -> None:
    pc = data.get("previewConfig")
    if pc is None:
        if part_ids:
            issues.append(_warn("previewConfig",
                                "No previewConfig defined but package has parts. Parts will use fallback positioning."))
        return

    # Part placement coverage
    covered_parts: set = set()
    for pp in (pc.get("partPlacements") or []):
        if pp and not _is_blank(pp.get("partId")):
            covered_parts.add(pp["partId"])
    for pid in part_ids:
        if pid not in covered_parts:
            issues.append(_warn("previewConfig.partPlacements",
                                f"Part '{pid}' has no placement entry. It will use fallback positioning."))

    # Target placement coverage
    covered_targets: set = set()
    for tp in (pc.get("targetPlacements") or []):
        if tp and not _is_blank(tp.get("targetId")):
            covered_targets.add(tp["targetId"])
    for tid in target_ids:
        if tid not in covered_targets:
            issues.append(_warn("previewConfig.targetPlacements",
                                f"Target '{tid}' has no placement entry. Preview will use fallback positioning."))

    # Subassembly placements
    covered_subassemblies: set = set()
    for i, sp in enumerate((pc.get("subassemblyPlacements") or [])):
        if sp and not _is_blank(sp.get("subassemblyId")):
            covered_subassemblies.add(sp["subassemblyId"])

    # Completed subassembly parking placements
    for i, sp in enumerate((pc.get("completedSubassemblyParkingPlacements") or [])):
        if sp and not _is_blank(sp.get("subassemblyId")):
            if sp["subassemblyId"] not in subassembly_ids:
                issues.append(_err(f"previewConfig.completedSubassemblyParkingPlacements[{i}].subassemblyId",
                                   f"Reference '{sp['subassemblyId']}' does not resolve."))

    # Integrated subassembly placements
    subassembly_lookup: dict[str, dict] = {}
    for sa in (data.get("subassemblies") or []):
        if sa and not _is_blank(sa.get("id")):
            subassembly_lookup[sa["id"]] = sa

    for i, isp in enumerate((pc.get("integratedSubassemblyPlacements") or [])):
        path = f"previewConfig.integratedSubassemblyPlacements[{i}]"
        if isp is None:
            issues.append(_err(path, "Integrated subassembly placement entry is null."))
            continue
        _check_required_text(isp.get("subassemblyId"), f"{path}.subassemblyId", issues)
        _check_required_text(isp.get("targetId"), f"{path}.targetId", issues)
        sa_id = isp.get("subassemblyId", "")
        t_id = isp.get("targetId", "")
        if not _is_blank(sa_id) and sa_id not in subassembly_ids:
            issues.append(_err(f"{path}.subassemblyId", f"Reference '{sa_id}' does not resolve."))
        if not _is_blank(t_id) and t_id not in target_ids:
            issues.append(_err(f"{path}.targetId", f"Reference '{t_id}' does not resolve."))
        members = isp.get("memberPlacements") or []
        if not members:
            issues.append(_warn(f"{path}.memberPlacements",
                                "Integrated subassembly placement has no member placements."))
            continue
        sa_part_ids: set | None = None
        if not _is_blank(sa_id) and sa_id in subassembly_lookup:
            sa_part_ids = set(subassembly_lookup[sa_id].get("partIds") or [])
        if sa_part_ids is not None and len(members) != len(sa_part_ids):
            issues.append(_warn(f"{path}.memberPlacements",
                                f"Integrated placement has {len(members)} member(s) but subassembly '{sa_id}' "
                                f"defines {len(sa_part_ids)} part(s). Some members may be missing or extraneous."))
        for j, member in enumerate(members):
            mp = f"{path}.memberPlacements[{j}]"
            if member is None:
                issues.append(_err(mp, "Integrated member placement entry is null."))
                continue
            _check_required_text(member.get("partId"), f"{mp}.partId", issues)
            mid = member.get("partId", "")
            if not _is_blank(mid) and mid not in part_ids:
                issues.append(_err(f"{mp}.partId", f"Reference '{mid}' does not resolve."))
            elif sa_part_ids is not None and not _is_blank(mid) and mid not in sa_part_ids:
                issues.append(_err(f"{mp}.partId",
                                   f"Part '{mid}' is not a member of subassembly '{sa_id}'."))

    # Constrained subassembly fit placements
    for i, csp in enumerate((pc.get("constrainedSubassemblyFitPlacements") or [])):
        path = f"previewConfig.constrainedSubassemblyFitPlacements[{i}]"
        if csp is None:
            issues.append(_err(path, "Constrained subassembly fit entry is null."))
            continue
        _check_required_text(csp.get("subassemblyId"), f"{path}.subassemblyId", issues)
        _check_required_text(csp.get("targetId"), f"{path}.targetId", issues)
        sa_id = csp.get("subassemblyId", "")
        t_id = csp.get("targetId", "")
        if not _is_blank(sa_id) and sa_id not in subassembly_ids:
            issues.append(_err(f"{path}.subassemblyId", f"Reference '{sa_id}' does not resolve."))
        if not _is_blank(t_id) and t_id not in target_ids:
            issues.append(_err(f"{path}.targetId", f"Reference '{t_id}' does not resolve."))
        driven = csp.get("drivenPartIds") or []
        if not driven:
            issues.append(_warn(f"{path}.drivenPartIds",
                                "Constrained subassembly fit has no drivenPartIds. The fit will behave like a rigid placement."))
        sa_part_ids = None
        if not _is_blank(sa_id) and sa_id in subassembly_lookup:
            sa_part_ids = set(subassembly_lookup[sa_id].get("partIds") or [])
        for j, dpid in enumerate(driven):
            dp = f"{path}.drivenPartIds[{j}]"
            _check_required_text(dpid, dp, issues)
            if not _is_blank(dpid) and dpid not in part_ids:
                issues.append(_err(dp, f"Reference '{dpid}' does not resolve."))
            elif sa_part_ids is not None and not _is_blank(dpid) and dpid not in sa_part_ids:
                issues.append(_err(dp, f"Part '{dpid}' is not a member of subassembly '{sa_id}'."))

    # Steps that use subassembly placement but lack authored preview frame
    for step in (data.get("steps") or []):
        if step is None:
            continue
        rsa = step.get("requiredSubassemblyId", "")
        if _is_blank(rsa):
            continue
        if rsa not in covered_subassemblies:
            issues.append(_warn("previewConfig.subassemblyPlacements",
                                f"Subassembly '{rsa}' is used by a placement step but has no authored subassembly placement frame."))
        profile = step.get("profile", "")
        resolved_family = _resolve_family(step)
        if profile == "AxisFit" and resolved_family == "Place":
            step_target_ids = step.get("targetIds") or []
            target_id = step_target_ids[0] if len(step_target_ids) == 1 else None
            constrained = pc.get("constrainedSubassemblyFitPlacements") or []
            found = any(
                c and c.get("subassemblyId") == rsa and c.get("targetId") == target_id
                for c in constrained
            )
            if not found:
                issues.append(_warn("previewConfig.constrainedSubassemblyFitPlacements",
                                    f"AxisFit step '{step.get('id')}' has no matching constrained fit preview payload "
                                    f"for subassembly '{rsa}' and target '{target_id or '<missing>'}'."))

    # Preview/play position consistency
    _validate_preview_assembled_position_consistency(data, pc, covered_parts, issues)


def _validate_preview_assembled_position_consistency(
    data: dict,
    pc: dict,
    covered_part_ids: set,
    issues: list[Issue],
) -> None:
    target_placements = pc.get("targetPlacements") or []
    part_placements = pc.get("partPlacements") or []
    if not target_placements or not part_placements:
        return

    # Collect targetIds used in placement steps
    placement_target_ids: set = set()
    for step in (data.get("steps") or []):
        if step is None:
            continue
        ct = (step.get("completionType") or "").lower()
        if ct == "placement" or _resolve_family(step) == "Place":
            for tid in (step.get("targetIds") or []):
                if tid:
                    placement_target_ids.add(tid)

    # partId -> assembledPosition
    part_play_pos: dict[str, dict] = {}
    for pp in part_placements:
        if pp and not _is_blank(pp.get("partId")):
            pp_pos = pp.get("assembledPosition")
            if pp_pos:
                part_play_pos[pp["partId"]] = pp_pos

    # targetId -> associatedPartId
    target_part_lookup: dict[str, str] = {}
    for t in (data.get("targets") or []):
        if t and not _is_blank(t.get("id")) and not _is_blank(t.get("associatedPartId")) and _is_blank(t.get("associatedSubassemblyId")):
            target_part_lookup[t["id"]] = t["associatedPartId"]

    for tp in target_placements:
        if tp is None or _is_blank(tp.get("targetId")):
            continue
        tid = tp["targetId"]
        if tid not in placement_target_ids:
            continue
        if tid not in target_part_lookup:
            continue
        part_id = target_part_lookup[tid]
        if part_id not in part_play_pos:
            continue
        tp_pos = tp.get("position", {}) or {}
        pp_pos = part_play_pos[part_id]
        dx = tp_pos.get("x", 0) - pp_pos.get("x", 0)
        dy = tp_pos.get("y", 0) - pp_pos.get("y", 0)
        dz = tp_pos.get("z", 0) - pp_pos.get("z", 0)
        dist = math.sqrt(dx * dx + dy * dy + dz * dz)
        if dist > POSITION_TOLERANCE:
            issues.append(_warn(
                f"previewConfig.targetPlacements[{tid}]",
                f"Preview position ({tp_pos.get('x', 0):.3f}, {tp_pos.get('y', 0):.3f}, {tp_pos.get('z', 0):.3f}) "
                f"differs from part '{part_id}' assembledPosition "
                f"({pp_pos.get('x', 0):.3f}, {pp_pos.get('y', 0):.3f}, {pp_pos.get('z', 0):.3f}) "
                f"by {dist:.4f}m. Preview will appear at the wrong location. "
                f"Update targetPlacement to match assembledPosition or the preview code will override it."))


# ── Public API ────────────────────────────────────────────────────────────────

def validate(data: dict) -> ValidationResult:
    """
    Validate a machine.json dict. Returns a ValidationResult.
    This is the importable entry point:
        from validate_machine_json import validate
        result = validate(data)
    """
    issues: list[Issue] = []

    if data is None:
        issues.append(_err("$", "Machine package data is null."))
        return ValidationResult(issues)

    _check_required_text(data.get("schemaVersion"), "schemaVersion", issues)
    _check_required_text(data.get("packageVersion"), "packageVersion", issues)

    machine = data.get("machine")
    if machine is None:
        issues.append(_err("machine", "Machine definition is required."))
    else:
        _validate_machine(machine, issues)

    # Build ID sets
    assemblies       = data.get("assemblies") or []
    subassemblies    = data.get("subassemblies") or []
    parts            = data.get("parts") or []
    tools            = data.get("tools") or []
    steps            = data.get("steps") or []
    validation_rules = data.get("validationRules") or []
    hints            = data.get("hints") or []
    effects          = data.get("effects") or []
    targets          = data.get("targets") or []

    assembly_ids       = _build_id_set(assemblies, "assemblies", issues)
    subassembly_ids    = _build_id_set(subassemblies, "subassemblies", issues)
    part_ids           = _build_id_set(parts, "parts", issues)
    tool_ids           = _build_id_set(tools, "tools", issues)
    step_ids           = _build_id_set(steps, "steps", issues)
    validation_rule_ids = _build_id_set(validation_rules, "validationRules", issues)
    hint_ids           = _build_id_set(hints, "hints", issues)
    effect_ids         = _build_id_set(effects, "effects", issues)
    target_ids         = _build_id_set(targets, "targets", issues)

    machine_id = machine.get("id", "") if machine else ""

    _validate_machine_references(machine, assembly_ids, issues)
    _validate_assemblies(assemblies, machine_id, subassembly_ids, step_ids, assembly_ids, issues)
    _validate_subassemblies(subassemblies, assembly_ids, part_ids, step_ids, issues)
    _validate_parts(parts, tool_ids, issues)
    _validate_tools(tools, issues)

    tool_defs = {t["id"]: t for t in tools if t and not _is_blank(t.get("id"))}

    _validate_steps(steps, assembly_ids, subassembly_ids, part_ids, tool_ids,
                    target_ids, targets, validation_rule_ids, hint_ids, effect_ids,
                    tool_defs, issues)
    _validate_validation_rules(validation_rules, part_ids, step_ids, target_ids, hint_ids, issues)
    _validate_hints(hints, part_ids, tool_ids, target_ids, issues)
    _validate_effects(effects, issues)
    _validate_targets(targets, part_ids, subassembly_ids, issues)
    _validate_preview_config(data, part_ids, target_ids, subassembly_ids, issues)

    # Orphan detection
    _detect_orphan_parts(parts, steps, targets, issues)
    _detect_orphan_targets(targets, steps, issues)
    _detect_orphan_steps(steps, assembly_ids, issues)

    # Structural integrity
    _validate_contiguous_sequence(steps, issues)

    return ValidationResult(issues)


def validate_file(path: str) -> ValidationResult:
    """Load a machine.json from disk and validate it."""
    with open(path, encoding="utf-8") as f:
        data = json.load(f)
    return validate(data)


# ── Report formatting ─────────────────────────────────────────────────────────

def _format_report(result: ValidationResult, package_id: str, data: dict | None = None) -> str:
    errors   = result.errors
    warnings = result.warnings

    # Header
    if not errors and not warnings:
        n_steps   = len(data.get("steps", []))   if data else 0
        n_targets = len(data.get("targets", [])) if data else 0
        n_hints   = len(data.get("hints", []))   if data else 0
        n_parts   = len(data.get("parts", []))   if data else 0
        header = (
            f"[machine.json] {_bold(package_id)}  "
            + _green(f"✓ Clean")
            + f" ({n_steps} steps, {n_targets} targets, {n_hints} hints, {n_parts} parts)"
        )
        return header

    err_tag  = _red(f"✗ {len(errors)} error{'s' if len(errors) != 1 else ''}")   if errors   else ""
    warn_tag = _yellow(f"⚠ {len(warnings)} warning{'s' if len(warnings) != 1 else ''}") if warnings else ""
    tags = "  ".join(t for t in [err_tag, warn_tag] if t)
    header = f"[machine.json] {_bold(package_id)}  {tags}"

    lines = [header, ""]

    def _group_issues(issue_list: list[Issue]) -> dict[str, list[Issue]]:
        groups: dict[str, list[Issue]] = {}
        for issue in issue_list:
            cat = issue.category()
            groups.setdefault(cat, []).append(issue)
        return groups

    if errors:
        lines.append(_bold(_red(f"ERRORS ({len(errors)})")))
        for cat, group in _group_issues(errors).items():
            lines.append(f"  {cat} ({len(group)})")
            for issue in group:
                id_part = _dim(f"[{issue.path}]")
                lines.append(f"    {id_part} {issue.message}")
        lines.append("")

    if warnings:
        lines.append(_bold(_yellow(f"WARNINGS ({len(warnings)})")))
        for cat, group in _group_issues(warnings).items():
            lines.append(f"  {cat} ({len(group)})")
            for issue in group:
                id_part = _dim(f"[{issue.path}]")
                lines.append(f"    {id_part} {issue.message}")
        lines.append("")

    exit_code = result.exit_code()
    if exit_code == 1:
        lines.append(f"Exit: {_red(str(len(errors)) + ' error(s) found')}")
    elif exit_code == 2:
        lines.append(f"Exit: {_yellow(str(len(warnings)) + ' warning(s) only')}")

    return "\n".join(lines)


# ── CLI entry point ───────────────────────────────────────────────────────────

def _find_machine_jsons(root: Path) -> list[Path]:
    return sorted(root.rglob("machine.json"))


def main(argv: list[str] | None = None) -> int:
    import argparse

    parser = argparse.ArgumentParser(
        description="Validate machine.json package(s) against the OSE schema rules."
    )
    parser.add_argument(
        "files",
        nargs="*",
        metavar="machine.json",
        help="One or more machine.json paths. If omitted, discovers all in subdirectories.",
    )
    parser.add_argument(
        "--no-color",
        action="store_true",
        help="Disable ANSI color output.",
    )
    args = parser.parse_args(argv)

    global _NO_COLOR
    if args.no_color:
        _NO_COLOR = True

    # Resolve files
    script_dir = Path(__file__).parent
    if args.files:
        paths = [Path(f) for f in args.files]
    else:
        paths = _find_machine_jsons(script_dir)
        if not paths:
            print(f"No machine.json files found under {script_dir}")
            return 0

    overall_exit = 0

    for path in paths:
        if not path.exists():
            print(_red(f"File not found: {path}"))
            overall_exit = 1
            continue

        try:
            with open(path, encoding="utf-8") as f:
                data = json.load(f)
        except json.JSONDecodeError as exc:
            print(_red(f"[{path}] JSON parse error: {exc}"))
            overall_exit = 1
            continue

        package_id = path.parent.name
        result = validate(data)
        print(_format_report(result, package_id, data))
        print()

        ec = result.exit_code()
        if ec == 1:
            overall_exit = 1
        elif ec == 2 and overall_exit == 0:
            overall_exit = 2

    return overall_exit


if __name__ == "__main__":
    sys.exit(main())
