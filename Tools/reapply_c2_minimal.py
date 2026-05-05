#!/usr/bin/env python3
"""One-shot: re-apply c2 surgery to assembly_d3d_batch_carriage_build with
minimal-field animation cues (loader inflates defaults at runtime)."""
import json, re
from pathlib import Path

PATH = Path("Assets/_Project/Data/Packages/d3d_v18_10/assemblies/assembly_d3d_batch_carriage_build.json")
text = PATH.read_text(encoding="utf-8")

ZERO = ('{\n            "position": {"x": 0.0, "y": 0.0, "z": 0.0},'
        '\n            "rotation": {"x": 0.0, "y": 0.0, "z": 0.0, "w": 0.0},'
        '\n            "scale": {"x": 0.0, "y": 0.0, "z": 0.0}\n          }')

def task_part(pid, await_cues=False):
    extra = '\n          "awaitCues": true,' if await_cues else ''
    return ('{\n          "kind": "part",\n          "id": "' + pid + '",'
            + extra + '\n          "endTransform": ' + ZERO + '\n        }')

def task_confirm():
    return ('{\n          "kind": "confirm_action",\n          "id": "confirm",'
            '\n          "endTransform": ' + ZERO + '\n        }')

# Step 60: drop half_a from requiredPartIds + taskOrder
m = re.search(r'"id":\s*"step_batch_c2_place_bearings"', text)
end = min(len(text), m.end()+8000)
rp = re.compile(r'("requiredPartIds":\s*\[)([^\]]*)(\])', re.DOTALL).search(text, m.end(), end)
inner = re.sub(r'\n\s*"y_right_carriage_half_a"\s*,?', '', rp.group(2), count=1)
inner = re.sub(r',(\s*)$', r'\1', inner)
text = text[:rp.start(2)] + inner + text[rp.end(2):]
m = re.search(r'"id":\s*"step_batch_c2_place_bearings"', text)
end = min(len(text), m.end()+8000)
to_pat = re.compile(r'("taskOrder":\s*\[)([\s\S]*?)(\n\s*\])', re.DOTALL)
to = to_pat.search(text, m.end(), end)
new_inner = re.sub(r',\s*\{\s*"kind":\s*"part",\s*"id":\s*"y_right_carriage_half_a"\s*\}', '', to.group(2), count=1)
text = text[:to.start(2)] + new_inner + text[to.end(2):]
print("Step 60: dropped half_a")

# Step 61: half_b -> half_a
m = re.search(r'"id":\s*"step_batch_c2_close_halves"', text)
end = min(len(text), m.end()+5000)
rp = re.compile(r'("requiredPartIds":\s*\[)([^\]]*)(\])', re.DOTALL).search(text, m.end(), end)
text = text[:rp.start(2)] + rp.group(2).replace("y_right_carriage_half_b","y_right_carriage_half_a") + text[rp.end(2):]
m = re.search(r'"id":\s*"step_batch_c2_close_halves"', text)
end = min(len(text), m.end()+5000)
to = to_pat.search(text, m.end(), end)
text = text[:to.start(2)] + to.group(2).replace('"y_right_carriage_half_b"','"y_right_carriage_half_a"') + text[to.end(2):]
print("Step 61: swapped half_b -> half_a")

def rebuild_step(step_id, new_reqParts, new_taskOrder):
    global text
    m = re.search(r'"id":\s*"' + re.escape(step_id) + r'"', text)
    end = min(len(text), m.end()+30000)
    rp_full = re.compile(r'("requiredPartIds":\s*)\[[\s\S]*?\]', re.DOTALL).search(text, m.end(), end)
    text = text[:rp_full.start()] + rp_full.group(1) + new_reqParts + text[rp_full.end():]
    m = re.search(r'"id":\s*"' + re.escape(step_id) + r'"', text)
    end = min(len(text), m.end()+30000)
    to_full = re.compile(r'("taskOrder":\s*)\[[\s\S]*?\n\s*\]', re.DOTALL).search(text, m.end(), end)
    text = text[:to_full.start()] + to_full.group(1) + new_taskOrder + text[to_full.end():]

to62 = "[\n        " + task_part("partGroup_carriage_y_right") + ",\n        " + task_confirm() + "\n      ]"
rebuild_step("step_batch_c2_shake_test", "[]", to62)

to63 = ("[\n        " + task_part("batch_test_rod", await_cues=True)
        + ",\n        " + task_part("partGroup_carriage_y_right", await_cues=True)
        + ",\n        " + task_confirm() + "\n      ]")
rebuild_step("step_batch_c2_rod_slide_test", "[]", to63)

m = re.search(r'"id":\s*"step_batch_c2_tighten"', text)
end = min(len(text), m.end()+10000)
rp65 = re.compile(r'("requiredPartIds":\s*)\[[\s\S]*?\]', re.DOTALL).search(text, m.end(), end)
text = text[:rp65.start()] + rp65.group(1) + "[]" + text[rp65.end():]
print("Steps 62, 63, 65: rebuilt")

# Trim helper
def trim(c):
    out = {}
    for k, v in c.items():
        if v in (None, 0, 0.0, "", False, [], {}):
            continue
        if isinstance(v, dict):
            if all(isinstance(x,(int,float)) and x==0 for x in v.values()):
                continue
            out[k] = v
        else:
            out[k] = v
    return out

ac_pat = re.compile(r'("animationCues":\s*)\[', re.DOTALL)

def find_array_bounds(start_idx):
    depth=0; i=start_idx
    while i < len(text):
        if text[i]=="[": depth+=1
        elif text[i]=="]":
            depth-=1
            if depth==0: return start_idx, i+1
        i+=1
    raise SystemExit("unterminated array")

# Rod cues: append c2 cues, minimal
m_rod = re.search(r'"id":\s*"batch_test_rod"', text)
ac_m = ac_pat.search(text, m_rod.end(), m_rod.end()+30000)
sr, er = find_array_bounds(ac_m.end()-1)
existing = json.loads(text[sr:er])
def offset_pose(p, dx):
    return {
        "position": {"x": round(p["position"]["x"]+dx,4), "y": p["position"]["y"], "z": p["position"]["z"]},
        "rotation": dict(p["rotation"]),
        "scale": dict(p["scale"]),
    }
pairs = {
    "step_batch_c1_rod_slide_test": "step_batch_c2_rod_slide_test",
    "step_batch_c1_place_bolts":    "step_batch_c2_place_bolts",
}
new_c2 = []
for c in existing:
    tgt = next((pairs[s] for s in c.get("stepIds",[]) if s in pairs), None)
    if not tgt: continue
    nc = json.loads(json.dumps(c))
    nc["stepIds"] = [tgt]
    nc["fromPose"] = offset_pose(c["fromPose"], 0.30)
    nc["toPose"]   = offset_pose(c["toPose"],   0.30)
    new_c2.append(trim(nc))
# Append-only: keep existing cue text verbatim, insert new cues before closing ]
# Find the position of the LAST `}` before the closing `]` to insert after it.
arr_text = text[sr:er]
# Insert new cues right before the closing ']'
close_idx = arr_text.rfind("]")
# Find the last `}` before that
last_brace = arr_text.rfind("}", 0, close_idx)
prefix = arr_text[:last_brace+1]
suffix = arr_text[last_brace+1:]
to_insert_lines = []
for nc in new_c2:
    s = json.dumps(nc, indent=2, ensure_ascii=False)
    s_lines = s.split("\n")
    s_indented = s_lines[0] + "\n" + "\n".join("        "+ln for ln in s_lines[1:])
    to_insert_lines.append(s_indented)
to_insert = ",\n      " + ",\n      ".join(to_insert_lines)
new_arr_text = prefix + to_insert + suffix
text = text[:sr] + new_arr_text + text[er:]
print("Rod cues: append-only added", len(new_c2), "minimal c2 cues to existing", len(existing))

# partGroup_carriage_y_right cues: clone y_left, minimal
m_yl = re.search(r'"id":\s*"partGroup_carriage_y_left"', text)
ac_yl = ac_pat.search(text, m_yl.end(), m_yl.end()+30000)
sl, el = find_array_bounds(ac_yl.end()-1)
yleft = json.loads(text[sl:el])
pg_map = {
    "step_batch_c1_shake_test":     "step_batch_c2_shake_test",
    "step_batch_c1_rod_slide_test": "step_batch_c2_rod_slide_test",
    "step_batch_c1_place_bolts":    "step_batch_c2_place_bolts",
}
yright_cues = []
for c in yleft:
    nc = json.loads(json.dumps(c))
    nc["stepIds"] = [pg_map.get(s,s) for s in nc.get("stepIds",[])]
    if nc.get("targetPartGroupId") == "partGroup_carriage_y_left":
        nc["targetPartGroupId"] = "partGroup_carriage_y_right"
    # Do NOT trim — keep all fields so behavior matches c1 exactly.
    # Previous trim accidentally dropped a field the runtime needs for the
    # centroid rotation during shake_test, breaking the "carriage goes up" effect.
    yright_cues.append(nc)

m_yr = re.search(r'"id":\s*"partGroup_carriage_y_right"', text)
ac_yr = ac_pat.search(text, m_yr.end(), m_yr.end()+30000)
sr, er = find_array_bounds(ac_yr.end()-1)
new_arr = json.dumps(yright_cues, indent=2, ensure_ascii=False)
lines = new_arr.split("\n")
indented = lines[0] + "\n" + "\n".join("        "+ln for ln in lines[1:])
text = text[:sr] + indented + text[er:]
print("y_right partGroup cues:", len(yright_cues), "(minimal)")

PATH.write_text(text, encoding="utf-8")
print("Saved.")
