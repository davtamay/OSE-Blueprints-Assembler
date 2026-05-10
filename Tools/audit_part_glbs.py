"""Audit *_approved.glb files for the "two-parts-fused" smell.

Catches the Brackettop.glb regression where two physically distinct parts
(extruder mount top plate + carriage body) shipped inside a single GLB
with coincident faces causing visible z-fighting.

A part GLB is flagged when:
  - it has 2+ shells whose triangle counts are both above SIGNIFICANT_TRI_THRESHOLD
    AND whose AABBs overlap by more than OVERLAP_VOLUME_RATIO of the smaller AABB;
  - OR it has 2+ shells that share a near-coincident face (max-axis bbox spans
    overlap and the perpendicular extent overlap is < COINCIDENT_FACE_TOLERANCE m).

These thresholds are tuned to permit standoffs/bosses (small disjoint shells
inside the same body) while catching fused composite parts.

Exit code 0 = clean, 1 = at least one anomaly. Run as part of CI / package_health.
"""

from __future__ import annotations

import argparse
import json
import os
import struct
import sys
from collections import defaultdict
from dataclasses import dataclass

SIGNIFICANT_TRI_THRESHOLD = 200
OVERLAP_VOLUME_RATIO = 0.30
COINCIDENT_FACE_TOLERANCE = 0.0005  # 0.5 mm
# A legit part has a body plus maybe 1-2 internal reinforcements. ≥4 disjoint
# shells almost always means modeled-in hardware (bolts, nuts) or two fused
# parts that should be split.
MAX_DISJOINT_SHELLS = 3


@dataclass
class Shell:
    tri_count: int
    bbox_min: tuple
    bbox_max: tuple

    def volume(self):
        return max(0.0, (self.bbox_max[0] - self.bbox_min[0])) * \
               max(0.0, (self.bbox_max[1] - self.bbox_min[1])) * \
               max(0.0, (self.bbox_max[2] - self.bbox_min[2]))


def read_glb(path):
    with open(path, "rb") as f:
        data = f.read()
    if len(data) < 12 or data[:4] != b"glTF":
        return None
    jlen, = struct.unpack("<I", data[12:16])
    js = data[20 : 20 + jlen].decode("utf-8", errors="replace").rstrip("\x00")
    gltf = json.loads(js)
    boff = 20 + jlen
    blen, = struct.unpack("<I", data[boff : boff + 4])
    bin_data = data[boff + 8 : boff + 8 + blen]
    return gltf, bin_data


def decode_attrs(gltf, bin_data, accessor_idx):
    """Return raw element bytes for an accessor, decoding meshopt if needed."""
    acc = gltf["accessors"][accessor_idx]
    bv = gltf["bufferViews"][acc["bufferView"]]
    if "EXT_meshopt_compression" in bv.get("extensions", {}):
        return None  # caller falls back to source candidate
    off = bv.get("byteOffset", 0) + acc.get("byteOffset", 0)
    count = acc["count"]
    elem_size = 12 if acc["type"] == "VEC3" else (
        2 if acc["componentType"] == 5123 else 4
    )
    stride = bv.get("byteStride") or elem_size
    out = bytearray()
    for i in range(count):
        out += bin_data[off + i * stride : off + i * stride + elem_size]
    return bytes(out), elem_size, count, acc.get("componentType")


def shells_for(gltf, bin_data) -> list[Shell] | None:
    if not gltf.get("meshes"):
        return []
    prim = gltf["meshes"][0]["primitives"][0]
    pos_acc = prim["attributes"].get("POSITION")
    idx_acc = prim.get("indices")
    if pos_acc is None or idx_acc is None:
        return []
    pos = decode_attrs(gltf, bin_data, pos_acc)
    idx = decode_attrs(gltf, bin_data, idx_acc)
    if pos is None or idx is None:
        return None  # meshopt-compressed; need uncompressed source

    pos_bytes, _, n_verts, _ = pos
    idx_bytes, idx_elem, n_idx, idx_ct = idx
    fmt = "<H" if idx_ct == 5123 else "<I"
    idxs = [struct.unpack_from(fmt, idx_bytes, i * idx_elem)[0] for i in range(n_idx)]
    verts = [struct.unpack_from("<fff", pos_bytes, i * 12) for i in range(n_verts)]

    keys = {}
    remap = [0] * n_verts
    for i, v in enumerate(verts):
        k = (round(v[0], 6), round(v[1], 6), round(v[2], 6))
        if k not in keys:
            keys[k] = len(keys)
        remap[i] = keys[k]
    tris = []
    for t in range(0, n_idx, 3):
        a, b, c = remap[idxs[t]], remap[idxs[t + 1]], remap[idxs[t + 2]]
        if len({a, b, c}) == 3:
            tris.append((idxs[t], idxs[t + 1], idxs[t + 2], a, b, c))

    edge_to_tri = defaultdict(list)
    for ti, (_, _, _, a, b, c) in enumerate(tris):
        for e in [(a, b), (b, c), (a, c)]:
            edge_to_tri[tuple(sorted(e))].append(ti)
    parent = list(range(len(tris)))

    def find(x):
        while parent[x] != x:
            parent[x] = parent[parent[x]]
            x = parent[x]
        return x

    for ts in edge_to_tri.values():
        for i in range(1, len(ts)):
            ra, rb = find(ts[0]), find(ts[i])
            if ra != rb:
                parent[ra] = rb

    groups = defaultdict(list)
    for ti in range(len(tris)):
        groups[find(ti)].append(ti)

    shells = []
    for _, tlist in groups.items():
        used = set()
        for ti in tlist:
            used.update(tris[ti][:3])
        pts = [verts[i] for i in used]
        mn = (min(p[0] for p in pts), min(p[1] for p in pts), min(p[2] for p in pts))
        mx = (max(p[0] for p in pts), max(p[1] for p in pts), max(p[2] for p in pts))
        shells.append(Shell(tri_count=len(tlist), bbox_min=mn, bbox_max=mx))
    shells.sort(key=lambda s: -s.tri_count)
    return shells


def aabb_overlap_volume(a: Shell, b: Shell) -> float:
    dx = min(a.bbox_max[0], b.bbox_max[0]) - max(a.bbox_min[0], b.bbox_min[0])
    dy = min(a.bbox_max[1], b.bbox_max[1]) - max(a.bbox_min[1], b.bbox_min[1])
    dz = min(a.bbox_max[2], b.bbox_max[2]) - max(a.bbox_min[2], b.bbox_min[2])
    if dx <= 0 or dy <= 0 or dz <= 0:
        return 0.0
    return dx * dy * dz


def has_coincident_face(a: Shell, b: Shell) -> tuple[bool, str]:
    """Detect a near-coincident planar face: two shells whose extents on two axes
    overlap substantially while the third axis bounds are within tolerance."""
    for axis in range(3):
        a_lo, a_hi = a.bbox_min[axis], a.bbox_max[axis]
        b_lo, b_hi = b.bbox_min[axis], b.bbox_max[axis]
        gap = max(a_lo, b_lo) - min(a_hi, b_hi)
        # we want them overlapping or barely touching
        if abs(min(abs(a_hi - b_hi), abs(a_lo - b_lo), abs(a_hi - b_lo), abs(a_lo - b_hi))) < COINCIDENT_FACE_TOLERANCE:
            # check the other two axes overlap
            other = [i for i in range(3) if i != axis]
            ok = True
            for o in other:
                ovr = min(a.bbox_max[o], b.bbox_max[o]) - max(a.bbox_min[o], b.bbox_min[o])
                a_ext = a.bbox_max[o] - a.bbox_min[o]
                b_ext = b.bbox_max[o] - b.bbox_min[o]
                if ovr <= 0 or ovr < 0.5 * min(a_ext, b_ext):
                    ok = False
                    break
            if ok:
                return True, "xyz"[axis]
    return False, ""


def audit(path: str) -> list[str]:
    issues = []
    parsed = read_glb(path)
    if parsed is None:
        return [f"{path}: not a valid GLB"]
    gltf, bin_data = parsed
    shells = shells_for(gltf, bin_data)
    if shells is None:
        # meshopt-compressed; fall back to source candidate if available
        report_path = path + ".gltfpack_report.json"
        if os.path.exists(report_path):
            try:
                rep = json.load(open(report_path))
                src = rep.get("source")
                if src:
                    pkg_root = path
                    while pkg_root and not pkg_root.endswith("d3d_v18_10") and os.path.dirname(pkg_root) != pkg_root:
                        pkg_root = os.path.dirname(pkg_root)
                    src_full = os.path.join(pkg_root, src.replace("/", os.sep))
                    if os.path.exists(src_full):
                        parsed2 = read_glb(src_full)
                        if parsed2:
                            shells = shells_for(*parsed2)
            except Exception as e:
                return [f"{path}: error reading source candidate: {e}"]
    if shells is None:
        return [f"{path}: meshopt-compressed and no uncompressed source candidate available — cannot audit"]

    if len(shells) > MAX_DISJOINT_SHELLS:
        small = [s for s in shells[1:] if s.tri_count < SIGNIFICANT_TRI_THRESHOLD]
        # If most extra shells are small AND fall inside the largest shell's
        # AABB, that's the "hardware modeled into the part" smell (bolts, nuts).
        largest = shells[0]
        contained_small = 0
        for s in small:
            inside = all(
                largest.bbox_min[i] - 0.002 <= s.bbox_min[i]
                and s.bbox_max[i] <= largest.bbox_max[i] + 0.002
                for i in range(3)
            )
            if inside:
                contained_small += 1
        if contained_small >= 3:
            issues.append(
                f"{os.path.basename(path)}: {len(shells)} disjoint shells "
                f"({contained_small} small ones inside the main body's AABB) — "
                f"likely hardware (bolts, nuts) modeled into the part instead of "
                f"authored as separate fastener parts"
            )

    significant = [s for s in shells if s.tri_count >= SIGNIFICANT_TRI_THRESHOLD]
    if len(significant) >= 2:
        # check pairwise overlap
        for i in range(len(significant)):
            for j in range(i + 1, len(significant)):
                a, b = significant[i], significant[j]
                ovl = aabb_overlap_volume(a, b)
                smaller = min(a.volume(), b.volume())
                if smaller > 0 and ovl / smaller >= OVERLAP_VOLUME_RATIO:
                    issues.append(
                        f"{os.path.basename(path)}: shells {i} ({a.tri_count} tris) "
                        f"and {j} ({b.tri_count} tris) AABBs overlap "
                        f"{100 * ovl / smaller:.0f}% of the smaller — likely two fused parts"
                    )
                else:
                    coin, axis = has_coincident_face(a, b)
                    if coin:
                        issues.append(
                            f"{os.path.basename(path)}: shells {i} and {j} share a "
                            f"near-coincident face along {axis}-axis — z-fighting risk"
                        )
    return issues


def iter_glbs(root: str):
    for dirpath, _, files in os.walk(root):
        for f in files:
            if f.endswith("_approved.glb"):
                yield os.path.join(dirpath, f)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("root", nargs="?", default="Assets/_Project/Data/Packages",
                    help="package root to scan")
    ap.add_argument("--file", help="audit a single GLB instead of scanning")
    args = ap.parse_args()

    paths = [args.file] if args.file else list(iter_glbs(args.root))
    all_issues = []
    for p in paths:
        issues = audit(p)
        if issues:
            all_issues.extend(issues)
    if all_issues:
        print("PART GLB AUDIT — issues found:")
        for i in all_issues:
            print(f"  ! {i}")
        print(f"\n{len(all_issues)} issue(s) across {len(paths)} GLB(s).")
        sys.exit(1)
    print(f"PART GLB AUDIT — clean ({len(paths)} GLB(s) scanned).")


if __name__ == "__main__":
    main()
