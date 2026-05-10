"""Split a single-mesh GLB into separate GLBs by connected-component shells.

Used to repair Brackettop.glb, which fused the extruder-mount top plate and
the carriage body into one mesh. Produces uncompressed binary GLBs ready to
be re-packed with `gltfpack -noq -cc`.

Usage:
    python tools/split_glb_shells.py <source.glb> --out <dir> --groups <spec>

<spec> is a list of "name:shellIdx,shellIdx,..." separated by ';' where
shellIdx is the rank when shells are sorted by descending triangle count.
Example: "top_plate:1;carriage:0,2,3,4,5,6,7,8,9,10"
"""

import argparse
import json
import os
import struct
import sys
from collections import defaultdict


def read_glb(path):
    with open(path, "rb") as f:
        data = f.read()
    magic, ver, total = struct.unpack("<4sII", data[:12])
    if magic != b"glTF":
        raise SystemExit(f"not a GLB: {path}")
    jlen, jtype = struct.unpack("<I4s", data[12:20])
    js = data[20 : 20 + jlen].decode("utf-8").rstrip("\x00")
    gltf = json.loads(js)
    boff = 20 + jlen
    blen, btype = struct.unpack("<I4s", data[boff : boff + 8])
    bin_data = data[boff + 8 : boff + 8 + blen]
    return gltf, bin_data


def slice_view(gltf, bin_data, accessor_idx):
    acc = gltf["accessors"][accessor_idx]
    bv = gltf["bufferViews"][acc["bufferView"]]
    off = bv.get("byteOffset", 0) + acc.get("byteOffset", 0)
    stride = bv.get("byteStride") or component_size(acc) * type_count(acc)
    count = acc["count"]
    elem_size = component_size(acc) * type_count(acc)
    out = bytearray()
    for i in range(count):
        out += bin_data[off + i * stride : off + i * stride + elem_size]
    return bytes(out), acc, elem_size


def component_size(acc):
    return {5120: 1, 5121: 1, 5122: 2, 5123: 2, 5125: 4, 5126: 4}[acc["componentType"]]


def type_count(acc):
    return {"SCALAR": 1, "VEC2": 2, "VEC3": 3, "VEC4": 4, "MAT4": 16}[acc["type"]]


def build_shells(gltf, bin_data):
    prim = gltf["meshes"][0]["primitives"][0]
    pos_bytes, pos_acc, pos_elem = slice_view(gltf, bin_data, prim["attributes"]["POSITION"])
    nrm_bytes, nrm_acc, nrm_elem = slice_view(gltf, bin_data, prim["attributes"]["NORMAL"])
    idx_bytes, idx_acc, idx_elem = slice_view(gltf, bin_data, prim["indices"])
    n_verts = pos_acc["count"]
    n_idx = idx_acc["count"]
    fmt = "<H" if idx_acc["componentType"] == 5123 else "<I"
    idxs = [struct.unpack_from(fmt, idx_bytes, i * idx_elem)[0] for i in range(n_idx)]
    verts = [struct.unpack_from("<fff", pos_bytes, i * 12) for i in range(n_verts)]

    # spatial dedupe to avoid hard-edge seams splitting the shell
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

    shells = defaultdict(list)
    for ti in range(len(tris)):
        shells[find(ti)].append(ti)
    ranked = sorted(shells.values(), key=len, reverse=True)
    return tris, ranked, pos_bytes, nrm_bytes, idx_acc, n_verts


def write_glb(path, tri_subset, tris, pos_bytes, nrm_bytes, idx_acc, n_verts, name):
    used = sorted({tris[ti][0] for ti in tri_subset} | {tris[ti][1] for ti in tri_subset} | {tris[ti][2] for ti in tri_subset})
    new_idx = {old: new for new, old in enumerate(used)}
    new_pos = bytearray()
    new_nrm = bytearray()
    for old in used:
        new_pos += pos_bytes[old * 12 : old * 12 + 12]
        new_nrm += nrm_bytes[old * 12 : old * 12 + 12]

    new_indices = bytearray()
    use_uint32 = len(used) > 65535
    fmt = "<I" if use_uint32 else "<H"
    elem = 4 if use_uint32 else 2
    for ti in tri_subset:
        a, b, c = tris[ti][0], tris[ti][1], tris[ti][2]
        new_indices += struct.pack(fmt, new_idx[a]) + struct.pack(fmt, new_idx[b]) + struct.pack(fmt, new_idx[c])

    # compute pos min/max
    n_used = len(used)
    mins = [float("inf")] * 3
    maxs = [float("-inf")] * 3
    for i in range(n_used):
        for k in range(3):
            v = struct.unpack_from("<f", new_pos, i * 12 + k * 4)[0]
            mins[k] = min(mins[k], v)
            maxs[k] = max(maxs[k], v)

    # pad each view to 4-byte alignment
    def pad4(b):
        return b + bytes((4 - (len(b) % 4)) % 4)

    pos_pad = pad4(bytes(new_pos))
    nrm_pad = pad4(bytes(new_nrm))
    idx_pad = pad4(bytes(new_indices))
    bin_payload = pos_pad + nrm_pad + idx_pad

    gltf = {
        "asset": {"version": "2.0", "generator": "split_glb_shells.py"},
        "scene": 0,
        "scenes": [{"name": "Scene", "nodes": [0]}],
        "nodes": [{"name": name, "mesh": 0}],
        "materials": [
            {
                "name": "default",
                "pbrMetallicRoughness": {
                    "baseColorFactor": [0.8, 0.8, 0.8, 1.0],
                    "metallicFactor": 0.0,
                    "roughnessFactor": 0.9,
                },
                "doubleSided": True,
            }
        ],
        "meshes": [
            {
                "name": name,
                "primitives": [
                    {"attributes": {"POSITION": 0, "NORMAL": 1}, "indices": 2, "material": 0}
                ],
            }
        ],
        "accessors": [
            {
                "bufferView": 0,
                "componentType": 5126,
                "count": n_used,
                "type": "VEC3",
                "min": mins,
                "max": maxs,
            },
            {"bufferView": 1, "componentType": 5126, "count": n_used, "type": "VEC3"},
            {
                "bufferView": 2,
                "componentType": 5125 if use_uint32 else 5123,
                "count": len(tri_subset) * 3,
                "type": "SCALAR",
            },
        ],
        "bufferViews": [
            {"buffer": 0, "byteOffset": 0, "byteLength": len(pos_pad), "target": 34962},
            {
                "buffer": 0,
                "byteOffset": len(pos_pad),
                "byteLength": len(nrm_pad),
                "target": 34962,
            },
            {
                "buffer": 0,
                "byteOffset": len(pos_pad) + len(nrm_pad),
                "byteLength": len(idx_pad),
                "target": 34963,
            },
        ],
        "buffers": [{"byteLength": len(bin_payload)}],
    }

    js = json.dumps(gltf, separators=(",", ":")).encode("utf-8")
    js += b" " * ((4 - (len(js) % 4)) % 4)

    total = 12 + 8 + len(js) + 8 + len(bin_payload)
    with open(path, "wb") as f:
        f.write(struct.pack("<4sII", b"glTF", 2, total))
        f.write(struct.pack("<I4s", len(js), b"JSON"))
        f.write(js)
        f.write(struct.pack("<I4s", len(bin_payload), b"BIN\x00"))
        f.write(bin_payload)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("source")
    ap.add_argument("--out", required=True)
    ap.add_argument("--groups", required=True,
                    help="name:idx,idx;name:idx,idx — shell indices ranked by tri count desc")
    ap.add_argument("--list", action="store_true", help="list shells and exit")
    args = ap.parse_args()

    gltf, bin_data = read_glb(args.source)
    tris, ranked, pos_bytes, nrm_bytes, idx_acc, n_verts = build_shells(gltf, bin_data)
    print(f"shells found: {len(ranked)}")
    for i, s in enumerate(ranked):
        print(f"  shell[{i}] tris={len(s)}")
    if args.list:
        return

    os.makedirs(args.out, exist_ok=True)
    for group in args.groups.split(";"):
        name, ids = group.split(":")
        idxs = [int(x) for x in ids.split(",")]
        tri_subset = []
        for ix in idxs:
            tri_subset.extend(ranked[ix])
        out_path = os.path.join(args.out, f"{name}.glb")
        write_glb(out_path, tri_subset, tris, pos_bytes, nrm_bytes, idx_acc, n_verts, name)
        size = os.path.getsize(out_path)
        print(f"wrote {out_path} ({size} bytes, {len(tri_subset)} tris)")


if __name__ == "__main__":
    main()
