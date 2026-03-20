"""
Compact schema rewrite for power_cube_frame/machine.json

Transforms:
1. Adds partTemplates array (5 templates for 12 tube parts)
2. Slims tube parts to use templateId (keeps only overrides)
3. Cleans all steps: removes assemblyId, subassemblyId, allowSkip,
   challengeFlags, empty arrays, tool action id fields, requiredCount=1
"""

import json
import os

FILE_PATH = os.path.join(
    os.path.dirname(__file__),
    "Assets", "_Project", "Data", "Packages",
    "power_cube_frame", "machine.json"
)

# ── Templates ──

TEMPLATES = [
    {
        "id": "template_base_tube_long",
        "category": "pipe",
        "material": "4\u00d74 tubular steel, 11-gauge wall",
        "structuralRole": "Primary frame member",
        "quantity": 1,
        "toolIds": ["tool_tape_measure", "tool_angle_grinder", "tool_welder"],
        "assetRef": "assets/parts/base_tube_long.glb",
        "allowPhysicalSubstitution": True,
        "tags": ["power_cube", "frame", "base"]
    },
    {
        "id": "template_base_tube_short",
        "category": "pipe",
        "material": "4\u00d74 tubular steel, 11-gauge wall",
        "structuralRole": "Primary frame member",
        "quantity": 1,
        "toolIds": ["tool_tape_measure", "tool_angle_grinder", "tool_welder"],
        "assetRef": "assets/parts/base_tube_short.glb",
        "allowPhysicalSubstitution": True,
        "tags": ["power_cube", "frame", "base"]
    },
    {
        "id": "template_vertical_post",
        "category": "pipe",
        "material": "4\u00d74 tubular steel, 11-gauge wall",
        "structuralRole": "Vertical frame member",
        "quantity": 1,
        "toolIds": ["tool_tape_measure", "tool_angle_grinder", "tool_welder"],
        "assetRef": "assets/parts/vertical_post.glb",
        "searchTerms": ["vertical post", "corner post", "upright"],
        "allowPhysicalSubstitution": True,
        "defaultOrientationHint": "Stand upright on the base corner.",
        "tags": ["power_cube", "frame", "vertical"]
    },
    {
        "id": "template_top_tube_long",
        "category": "pipe",
        "material": "4\u00d74 tubular steel, 11-gauge wall",
        "structuralRole": "Top frame member",
        "quantity": 1,
        "toolIds": ["tool_tape_measure", "tool_welder"],
        "assetRef": "assets/parts/base_tube_long.glb",
        "allowPhysicalSubstitution": True,
        "tags": ["power_cube", "frame", "top"]
    },
    {
        "id": "template_top_tube_short",
        "category": "pipe",
        "material": "4\u00d74 tubular steel, 11-gauge wall",
        "structuralRole": "Top frame member",
        "quantity": 1,
        "toolIds": ["tool_tape_measure", "tool_welder"],
        "assetRef": "assets/parts/base_tube_short.glb",
        "allowPhysicalSubstitution": True,
        "tags": ["power_cube", "frame", "top"]
    }
]

# Map part id -> template id
PART_TEMPLATE_MAP = {
    "base_tube_long_1": "template_base_tube_long",
    "base_tube_long_2": "template_base_tube_long",
    "base_tube_short_1": "template_base_tube_short",
    "base_tube_short_2": "template_base_tube_short",
    "vertical_post_1": "template_vertical_post",
    "vertical_post_2": "template_vertical_post",
    "vertical_post_3": "template_vertical_post",
    "vertical_post_4": "template_vertical_post",
    "top_tube_long_1": "template_top_tube_long",
    "top_tube_long_2": "template_top_tube_long",
    "top_tube_short_1": "template_top_tube_short",
    "top_tube_short_2": "template_top_tube_short",
}

# Fields that get filled from template by normalizer
TEMPLATE_FIELDS = {
    "category", "material", "structuralRole", "quantity",
    "toolIds", "assetRef", "allowPhysicalSubstitution", "tags"
}

# Fields that the normalizer checks with null/empty before filling
# (searchTerms, defaultOrientationHint) - only remove if they match template
OPTIONAL_TEMPLATE_FIELDS = {"searchTerms", "defaultOrientationHint"}


def build_template_lookup(templates):
    return {t["id"]: t for t in templates}


def compact_part(part, template):
    """Keep only id, templateId, name, displayName, function,
    and any fields that differ from the template."""
    result = {"id": part["id"], "templateId": template["id"]}

    # Always keep these part-specific fields
    for field in ("name", "displayName", "function"):
        if field in part and part[field]:
            result[field] = part[field]

    # Keep optional fields only if they differ from template or template doesn't have them
    for field in OPTIONAL_TEMPLATE_FIELDS:
        part_val = part.get(field)
        template_val = template.get(field)
        if part_val and part_val != template_val:
            result[field] = part_val

    return result


def clean_step(step):
    """Remove redundant fields from step."""
    # Remove fields inferred by normalizer or dead
    for key in ("assemblyId", "subassemblyId", "allowSkip", "challengeFlags"):
        step.pop(key, None)

    # Remove empty arrays
    for key in ("requiredPartIds", "validationRuleIds", "hintIds", "effectTriggerIds"):
        val = step.get(key)
        if val is not None and len(val) == 0:
            del step[key]

    # Clean tool actions
    actions = step.get("requiredToolActions")
    if actions:
        for action in actions:
            # Remove auto-generated id
            action.pop("id", None)
            # Remove requiredCount when it's 1 (normalizer defaults to 1)
            if action.get("requiredCount", 0) == 1:
                del action["requiredCount"]

    return step


def transform(data):
    # 1. Add partTemplates
    data["partTemplates"] = TEMPLATES

    # 2. Compact tube parts
    template_lookup = build_template_lookup(TEMPLATES)
    new_parts = []
    for part in data["parts"]:
        pid = part["id"]
        if pid in PART_TEMPLATE_MAP:
            template = template_lookup[PART_TEMPLATE_MAP[pid]]
            new_parts.append(compact_part(part, template))
        else:
            new_parts.append(part)
    data["parts"] = new_parts

    # 3. Clean all steps
    for step in data["steps"]:
        clean_step(step)

    # 4. Reorder top-level keys so partTemplates comes after machine, before assemblies
    ordered = {}
    for key in data:
        ordered[key] = data[key]
        if key == "machine":
            # partTemplates already added to data, will appear in iteration
            pass

    # Actually, let's explicitly order the keys
    key_order = [
        "schemaVersion", "packageVersion", "machine", "partTemplates",
        "assemblies", "subassemblies", "parts", "tools", "steps",
        "validationRules", "effects", "hints", "targets",
        "challengeConfig", "assetManifest", "previewConfig"
    ]
    result = {}
    for key in key_order:
        if key in data:
            result[key] = data[key]
    # Add any remaining keys not in the order list
    for key in data:
        if key not in result:
            result[key] = data[key]

    return result


def main():
    with open(FILE_PATH, "r", encoding="utf-8") as f:
        data = json.load(f)

    original_parts_count = len(data.get("parts", []))
    original_steps_count = len(data.get("steps", []))

    result = transform(data)

    with open(FILE_PATH, "w", encoding="utf-8") as f:
        json.dump(result, f, indent=2, ensure_ascii=False)
        f.write("\n")

    print(f"Done! Templates: {len(TEMPLATES)}")
    print(f"Parts: {original_parts_count} (12 compacted, {original_parts_count - 12} unchanged)")
    print(f"Steps cleaned: {original_steps_count}")

    # Verify the output is valid JSON
    with open(FILE_PATH, "r", encoding="utf-8") as f:
        verify = json.load(f)
    print(f"Verification: valid JSON with {len(verify)} top-level keys")
    print(f"partTemplates: {len(verify.get('partTemplates', []))}")

    # Check a templated part
    for p in verify["parts"]:
        if p["id"] == "base_tube_long_1":
            print(f"Sample part keys: {list(p.keys())}")
            break

    # Check a cleaned step
    for s in verify["steps"]:
        print(f"Sample step keys: {list(s.keys())}")
        break


if __name__ == "__main__":
    main()
