#!/usr/bin/env python3
"""
Tool GLB Post-Processing Script
================================
Fixes common issues with AI-generated tool GLB models (e.g., from Rodin):
  1. Sets all materials to doubleSided=true (fixes inverted normals)
  2. Copies the fixed GLB to all deployment locations

Usage:
    python fix_tool_glb.py <source_glb> <tool_id> [--no-deploy]

Example:
    python fix_tool_glb.py generated_models/tape_measure_v4/.../base_basic_pbr.glb tool_tape_measure
"""

import argparse
import json
import os
import shutil
import struct
import sys


def fix_double_sided(glb_path: str, output_path: str) -> None:
    """Read a GLB, set all materials to doubleSided=true, write to output."""
    with open(glb_path, "rb") as f:
        data = f.read()

    # Parse GLB header
    if data[:4] != b"glTF":
        raise ValueError(f"Not a valid GLB file: {glb_path}")
    version = struct.unpack_from("<I", data, 4)[0]
    if version != 2:
        raise ValueError(f"Unsupported glTF version: {version}")

    # Parse chunks
    offset = 12
    json_chunk_data = None
    bin_chunk_data = None

    while offset < len(data):
        chunk_length = struct.unpack_from("<I", data, offset)[0]
        chunk_type = struct.unpack_from("<I", data, offset + 4)[0]
        chunk_data = data[offset + 8 : offset + 8 + chunk_length]

        if chunk_type == 0x4E4F534A:  # JSON
            json_chunk_data = chunk_data
        elif chunk_type == 0x004E4942:  # BIN
            bin_chunk_data = chunk_data

        offset += 8 + chunk_length

    if json_chunk_data is None:
        raise ValueError("No JSON chunk found in GLB")

    # Parse and modify the JSON
    gltf = json.loads(json_chunk_data.decode("utf-8"))

    materials = gltf.get("materials", [])
    fixed_count = 0
    for mat in materials:
        if not mat.get("doubleSided", False):
            mat["doubleSided"] = True
            fixed_count += 1

    print(f"  Fixed {fixed_count}/{len(materials)} materials -> doubleSided=true")

    # Re-encode JSON chunk (pad to 4-byte alignment with spaces)
    new_json_bytes = json.dumps(gltf, separators=(",", ":")).encode("utf-8")
    while len(new_json_bytes) % 4 != 0:
        new_json_bytes += b" "

    # Rebuild GLB
    total_length = 12  # header
    total_length += 8 + len(new_json_bytes)  # JSON chunk
    if bin_chunk_data is not None:
        # Pad binary chunk to 4-byte alignment with zero bytes
        bin_padded = bin_chunk_data
        while len(bin_padded) % 4 != 0:
            bin_padded += b"\x00"
        total_length += 8 + len(bin_padded)

    out = bytearray()
    # Header
    out += b"glTF"
    out += struct.pack("<I", 2)
    out += struct.pack("<I", total_length)
    # JSON chunk
    out += struct.pack("<I", len(new_json_bytes))
    out += struct.pack("<I", 0x4E4F534A)
    out += new_json_bytes
    # BIN chunk
    if bin_chunk_data is not None:
        out += struct.pack("<I", len(bin_padded))
        out += struct.pack("<I", 0x004E4942)
        out += bin_padded

    os.makedirs(os.path.dirname(output_path) or ".", exist_ok=True)
    with open(output_path, "wb") as f:
        f.write(out)

    src_size = os.path.getsize(glb_path)
    dst_size = len(out)
    print(f"  Source: {src_size:,} bytes -> Output: {dst_size:,} bytes")


def deploy_tool(fixed_glb: str, tool_id: str, project_root: str) -> list:
    """Copy fixed GLB to all package deployment locations."""
    glb_filename = f"{tool_id}.glb"
    deploy_targets = []

    # Find all machine package directories that reference this tool
    packages_dir = os.path.join(project_root, "Assets", "_Project", "Data", "Packages")
    streaming_dir = os.path.join(project_root, "Assets", "StreamingAssets", "MachinePackages")

    for base_dir in [packages_dir, streaming_dir]:
        if not os.path.isdir(base_dir):
            continue
        for pkg_name in os.listdir(base_dir):
            tools_dir = os.path.join(base_dir, pkg_name, "assets", "tools")
            target = os.path.join(tools_dir, glb_filename)
            if os.path.exists(target):
                deploy_targets.append(target)

    if not deploy_targets:
        # Default: deploy to known locations
        for base_dir in [packages_dir, streaming_dir]:
            for pkg_name in ["power_cube_frame", "onboarding_tutorial"]:
                tools_dir = os.path.join(base_dir, pkg_name, "assets", "tools")
                if os.path.isdir(tools_dir):
                    deploy_targets.append(os.path.join(tools_dir, glb_filename))

    deployed = []
    for target in deploy_targets:
        os.makedirs(os.path.dirname(target), exist_ok=True)
        shutil.copy2(fixed_glb, target)
        deployed.append(target)
        print(f"  Deployed: {target}")

        # Delete .meta file to force Unity re-import
        meta_file = target + ".meta"
        if os.path.exists(meta_file):
            os.remove(meta_file)
            print(f"  Deleted meta: {meta_file}")

    return deployed


def main():
    parser = argparse.ArgumentParser(description="Fix and deploy tool GLB models")
    parser.add_argument("source_glb", help="Path to the source GLB file")
    parser.add_argument("tool_id", help="Tool ID (e.g., tool_tape_measure)")
    parser.add_argument("--no-deploy", action="store_true", help="Fix only, don't deploy")
    parser.add_argument("--output", help="Custom output path (default: scripts/fixed_<tool_id>.glb)")
    args = parser.parse_args()

    project_root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

    source = os.path.abspath(args.source_glb)
    if not os.path.exists(source):
        print(f"ERROR: Source GLB not found: {source}")
        sys.exit(1)

    output = args.output or os.path.join(
        project_root, "scripts", f"fixed_{args.tool_id}.glb"
    )

    print(f"Processing: {source}")
    print(f"Tool ID:    {args.tool_id}")

    # Step 1: Fix materials
    fix_double_sided(source, output)
    print(f"Fixed GLB:  {output}")

    # Step 2: Deploy
    if not args.no_deploy:
        print("\nDeploying to package locations:")
        deployed = deploy_tool(output, args.tool_id, project_root)
        if deployed:
            print(f"\nDeployed to {len(deployed)} location(s).")
            print("Deleted .meta files to force Unity re-import on next focus.")
        else:
            print("WARNING: No deployment targets found.")
    else:
        print("\nSkipping deployment (--no-deploy)")

    print("\nDone!")


if __name__ == "__main__":
    main()
