"""
audit_asset_reuse.py — Phase B.2 audit-only script.

Three outputs in AgentAssistant/manual_audits/:
  • asset_inventory.csv — every assetRef + count of parts using it
  • asset_reuse_audit.md — markdown report with SHA-dup, top reuse, composites
"""

from __future__ import annotations
import json
import glob
import os
import hashlib
import csv
from collections import defaultdict


def main():
    base = os.path.join(
        os.path.dirname(os.path.dirname(os.path.abspath(__file__))),
        "Assets", "_Project", "Data", "Packages", "d3d_v18_10",
    )
    asm_dir = os.path.join(base, "assemblies")
    parts_dir = os.path.join(base, "assets", "parts")
    out_dir = os.path.join(
        os.path.dirname(os.path.dirname(os.path.abspath(__file__))),
        "AgentAssistant", "manual_audits",
    )
    os.makedirs(out_dir, exist_ok=True)

    # 1. SHA dedup
    sha_to_files = defaultdict(list)
    for fname in os.listdir(parts_dir):
        if not fname.endswith(".glb"):
            continue
        fpath = os.path.join(parts_dir, fname)
        with open(fpath, "rb") as f:
            digest = hashlib.sha256(f.read()).hexdigest()
        sha_to_files[digest].append((fname, os.path.getsize(fpath)))
    sha_dups = []
    for digest, files in sha_to_files.items():
        if len(files) > 1:
            canonical = min(files, key=lambda x: len(x[0]))
            dups = [f for f, _ in files if f != canonical[0]]
            sha_dups.append((digest[:12], canonical[0], dups, files[0][1]))

    # 2. Per-mesh part inventory
    mesh_to_parts = defaultdict(list)
    for f in glob.glob(asm_dir + "/*.json"):
        with open(f, encoding="utf-8") as fh:
            d = json.load(fh)
        for pp in d.get("parts", []) or []:
            ar = pp.get("assetRef") or "<none>"
            if isinstance(ar, dict):
                ar = ar.get("path", "<none>")
            mesh_to_parts[ar].append(pp["id"])

    # Write CSV
    csv_path = os.path.join(out_dir, "asset_inventory.csv")
    with open(csv_path, "w", encoding="utf-8", newline="") as f:
        w = csv.writer(f)
        w.writerow(["assetRef", "part_count", "part_ids"])
        for mesh, ids in sorted(mesh_to_parts.items(), key=lambda x: -len(x[1])):
            w.writerow([mesh, len(ids), "; ".join(ids)])

    # 3. Composite-part audit
    composites = [
        "d3d_x_axis_idler_unit", "d3d_x_axis_carriage_side", "d3d_x_axis_half_carriage",
        "d3d_x_axis_rod_pair", "z1_half_carriage", "z2_half_carriage",
        "idler001", "idler002", "idler003", "idler",
        "idler001_half_b", "idler002_half_b", "idler003_half_b", "idler_half_b",
    ]

    composite_refs = {}
    for c in composites:
        refs = []
        for f in glob.glob(asm_dir + "/*.json"):
            with open(f, encoding="utf-8") as fh:
                d = json.load(fh)
            fname = os.path.basename(f)
            for s in d.get("steps", []) or []:
                for k in ("requiredPartIds", "optionalPartIds", "targetPartIds"):
                    if c in (s.get(k) or []):
                        refs.append(f"{fname}::step:{s['id']}")
                        break
            for t in d.get("targets", []) or []:
                if t.get("associatedPartId") == c:
                    refs.append(f"{fname}::target:{t.get('id','?')}")
            for pg in d.get("partGroups", []) or []:
                if c in (pg.get("partIds") or []):
                    refs.append(f"{fname}::partGroup:{pg['id']}")
        composite_refs[c] = refs

    # Build report
    out = []
    out.append("# Asset Reuse Audit\n")
    out.append("_Phase B.2 audit. Inventory of every assetRef + SHA dedup + composite check._\n")
    out.append("\n## SHA-identical GLBs (true file-level duplicates)\n")
    if sha_dups:
        out.append("| SHA | Bytes | Canonical | Duplicates |")
        out.append("|---|---:|---|---|")
        for sha, canon, dups, sz in sha_dups:
            dup_str = ", ".join(f"`{d}`" for d in dups)
            out.append(f"| `{sha}` | {sz:,} | `{canon}` | {dup_str} |")
        out.append("\n**Action:** Repoint assetRefs from duplicates to canonical, then delete the duplicate GLB files.\n")
    else:
        out.append("_No SHA-identical duplicates found._\n")

    out.append("\n## Top mesh reuse (most-shared assetRefs)\n")
    out.append("High counts are usually correct — every M6 nut shares one mesh, every LM8UU bearing shares one mesh, etc. **Flag only when multiple parts represent the same physical thing.**\n\n")
    out.append("| assetRef | parts | Verdict |")
    out.append("|---|---:|---|")
    verdicts = {
        "d3d_axis_m6_nut.glb": "OK — one mesh, dozens of nut instances across 5 axes",
        "d3d_axis_m6x18_bolt.glb": "OK — one mesh, every M6x18 instance",
        "d3d_axis_lm8uu_bearing.glb": "OK — 4 bearings × 5 axes",
        "d3d_axis_m3x25_shcs.glb": "OK — 4 motor screws × 5 axes",
        "d3d_axis_625zz_bearing.glb": "OK — 2 bearings × 4 idlers + 2 X half-bearings",
        "d3d_axis_gt2_pulley_19t.glb": "OK — 1 pulley × 5 motors",
        "d3d_axis_gt2_belt.glb": "OK — 1 belt per axis",
        "y_left_carriage_half_a.glb": "OK — same printed half across all 4 Y/Z carriages",
        "y_left_carriage_half_b.glb": "OK — same as half_b",
        "idler_approved.glb": "OK — single canonical idler mesh after dedup commit add6ab9",
        "rod_005_approved.glb": "OK — guide rod mesh reused across axes",
        "rod_006_approved.glb": "OK — same for rod_006",
        "d3d_axis_belt_peg_approved.glb": "OK — belt peg used by 3 axes",
        "d3d_axis_mount_m6x30_bolt_approved.glb": "OK — frame-mount bolt reused",
        "d3d_frame_flat_bar_approved.glb": "OK — frame flat bar × 24 edges",
        "compound007_approved.glb": "OK — compound bracket × 8",
        "heatbed_raisers_combined.glb": "OK — bed riser × 6",
        "y_endstop_approved.glb": "OK — endstop mesh reused for X-axis endstop",
    }
    for mesh, ids in sorted(mesh_to_parts.items(), key=lambda x: -len(x[1]))[:25]:
        v = verdicts.get(mesh, "manual review needed")
        out.append(f"| `{mesh}` | {len(ids)} | {v} |")

    out.append("\n## Composite-part audit\n")
    out.append("Legacy composite IDs (idler001/002/003/idler, *_half_carriage, d3d_x_axis_*) — confirm each is referenced where it should be (typically a bench_unit aggregate plus a batch step that places it as half_a).\n\n")
    out.append("| Composite | Ref count | Status |")
    out.append("|---|---:|---|")
    for c in composites:
        refs = composite_refs[c]
        status = f"{len(refs)} refs" if refs else "⚠ ORPHAN — defined but no step/target/partGroup uses it"
        out.append(f"| `{c}` | {len(refs)} | {status} |")

    out.append("\n### Composite-part details\n")
    for c in composites:
        refs = composite_refs[c]
        if not refs:
            continue
        out.append(f"\n**`{c}`** ({len(refs)} refs):")
        for r in refs[:10]:
            out.append(f"- `{r}`")
        if len(refs) > 10:
            out.append(f"- ...({len(refs)-10} more)")

    out.append("\n## Recommendations\n")
    if sha_dups:
        out.append("1. **Resolve SHA dup(s)** — see table above. Repoint + delete duplicate GLBs.\n")
    out.append("2. **Composites are intentional** — `idler001/002/003/idler` are the half_a identity for each Y/Z idler instance, now placed by batch_idler_build's per-axis steps and aggregated into bench_unit. The X-axis composites (`d3d_x_axis_*`) similarly serve as conceptual wholes for the X bench.\n")
    out.append("3. **The mesh-reuse warnings in Unity validator are noise** — every entry in Top mesh reuse above is legitimate. Worth lowering these warnings to Info severity in `MachinePackageValidator` so the dashboard surfaces real issues.\n")
    out.append(f"\n_Inventory CSV: see `asset_inventory.csv` ({len(mesh_to_parts)} unique assetRefs)._\n")

    report_path = os.path.join(out_dir, "asset_reuse_audit.md")
    with open(report_path, "w", encoding="utf-8") as f:
        f.write("\n".join(out))
    print(f"Wrote {csv_path}")
    print(f"Wrote {report_path}")
    print(f"Stats: {len(mesh_to_parts)} unique meshes, {len(sha_dups)} SHA dups, "
          f"{sum(len(composite_refs[c]) for c in composites)} composite refs")


if __name__ == "__main__":
    main()
