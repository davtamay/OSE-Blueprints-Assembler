#!/usr/bin/env python3
"""Re-runnable fix: c-N carriage clones have inconsistent translation
deltas across part subsets — the cloner translated bolts/bearings into
the right physical location but left carriage halves elsewhere (often
on top of c1). Result: close_halves ghost appears off to the side, and
shake_test rotation pivot is wrong because the centroid spans two
different locations.

Strategy: for each clone (y_right, z_back, z_front), use the bolt/bearing
parts as the "ground truth" location of where the carriage actually is in
the machine. Compute the canonical delta from c1 to cN by averaging the
(cN_part − c1_part) vectors across all hardware parts. Then for ANY
carriage part whose current delta doesn't match the canonical, rewrite
both startPosition and assembledPosition to canonical.

This makes each clone a true translation of c1.
"""
import json, sys, glob

PKG = "Assets/_Project/Data/Packages/d3d_v18_10"
PC  = f"{PKG}/preview_config.json"
ASM_GLOB = f"{PKG}/assemblies/*.json"

CANONICAL = "y_left"
CLONES    = ["y_right", "z_back", "z_front"]

# Roles we treat as "ground truth" for computing the canonical delta —
# bolts, nuts, bearings consistently appear correctly translated by the
# cloner; halves are the buggy ones. m6x18_b has a name asymmetry so we
# include both spellings.
GROUND_TRUTH_ROLES = {
    "lm8uu_a", "lm8uu_b", "lm8uu_c", "lm8uu_d",
    "m6_nut_a", "m6_nut_b", "m6_nut_c", "m6_nut_d",
    "m6x18_a", "m6x30_a", "m6x30_b",
}
TOL = 0.01  # 1 cm divergence triggers a rewrite

def role_of(pid, prefix):
    if pid.startswith(prefix + "_carriage_"):
        return "carriage_" + pid[len(prefix) + len("_carriage_"):]
    if pid.startswith(prefix + "_"):
        return pid[len(prefix) + 1:]
    return None

def gather_groups():
    """Map prefix -> {role: partId} across all assembly files."""
    out = {p: {} for p in [CANONICAL] + CLONES}
    for fpath in glob.glob(ASM_GLOB):
        with open(fpath, "r", encoding="utf-8") as f:
            asm = json.load(f)
        for p in asm.get("parts", []):
            for pgid in p.get("partGroupIds", []):
                for pref in out:
                    if pgid == f"partGroup_carriage_{pref}":
                        r = role_of(p["id"], pref)
                        if r:
                            out[pref][r] = p["id"]
    return out

def vec(d):
    return (float(d.get("x", 0)), float(d.get("y", 0)), float(d.get("z", 0)))

def sub(a, b): return (a[0]-b[0], a[1]-b[1], a[2]-b[2])
def add(a, b): return (a[0]+b[0], a[1]+b[1], a[2]+b[2])
def to_dict(v): return {"x": round(v[0], 4), "y": round(v[1], 4), "z": round(v[2], 4)}

def avg(vecs):
    n = len(vecs)
    return (sum(v[0] for v in vecs)/n, sum(v[1] for v in vecs)/n, sum(v[2] for v in vecs)/n)

def main():
    apply = "--apply" in sys.argv
    with open(PC, "r", encoding="utf-8") as f:
        pc = json.load(f)
    inner = pc["previewConfig"]
    pp = {p.get("partId"): p for p in inner.get("partPlacements", [])}

    groups = gather_groups()
    canonical_parts = groups[CANONICAL]

    changes = 0
    for clone in CLONES:
        clone_parts = groups[clone]
        # Compute canonical delta from ground-truth parts
        deltas = []
        for r in GROUND_TRUTH_ROLES:
            cpid = canonical_parts.get(r)
            xpid = clone_parts.get(r)
            if not (cpid and xpid and cpid in pp and xpid in pp):
                continue
            ca = pp[cpid].get("assembledPosition")
            xa = pp[xpid].get("assembledPosition")
            if ca and xa:
                deltas.append(sub(vec(xa), vec(ca)))
        if not deltas:
            print(f"  SKIP {clone} (no ground-truth parts found)")
            continue
        canonical_delta = avg(deltas)
        spread = max(max(abs(d[i] - canonical_delta[i]) for d in deltas) for i in range(3))
        print(f"\n=== {clone} ===")
        print(f"  Canonical delta from c1 (avg of {len(deltas)} bolt/bearing parts): "
              f"({canonical_delta[0]:+.4f}, {canonical_delta[1]:+.4f}, {canonical_delta[2]:+.4f})")
        print(f"  Spread among ground-truth parts: {spread:.4f}m  ({'OK' if spread < TOL else 'INCONSISTENT'})")

        # Walk every clone part and check both start + assembled vs canonical
        for r, xpid in sorted(clone_parts.items()):
            # alias: c1 has carriage_m6x18_b but clones have m6x18_b
            cpid = canonical_parts.get(r) or canonical_parts.get(
                "carriage_m6x18_b" if r == "m6x18_b" else
                ("m6x18_b" if r == "carriage_m6x18_b" else r))
            if not cpid or cpid not in pp or xpid not in pp:
                continue
            cpp = pp[cpid]; xpp = pp[xpid]
            for key in ("startPosition", "assembledPosition"):
                cv = cpp.get(key); xv = xpp.get(key)
                if not (cv and xv): continue
                expected = add(vec(cv), canonical_delta)
                actual = vec(xv)
                err = max(abs(actual[i] - expected[i]) for i in range(3))
                if err > TOL:
                    new_pose = to_dict(expected)
                    print(f"  PATCH {xpid:35s} {key:18s} "
                          f"actual={tuple(round(a,4) for a in actual)} -> {new_pose} "
                          f"(err {err:.4f}m)")
                    if apply:
                        xpp[key] = new_pose
                    changes += 1

    if apply and changes:
        with open(PC, "w", encoding="utf-8", newline="\n") as f:
            json.dump(pc, f, indent=4)
            f.write("\n")
    print(f"\n{'APPLIED' if apply else 'DRY-RUN'}: {changes} pose corrections")
    if not apply and changes:
        print("Re-run with --apply to write.")

if __name__ == "__main__":
    main()
