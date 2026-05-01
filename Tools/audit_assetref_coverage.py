"""
audit_assetref_coverage.py — diagnose editor-vs-build asset divergence

Builds use the baked `assetManifest` block in
StreamingAssets/MachinePackages/<pkg>/machine.json plus the GLBs copied
to that folder. The editor uses the AssetDatabase against the authoring
folder. When the two diverge, parts/tools render in the editor but
disappear in WebGL / standalone builds.

Reports:
  - Authoring vs StreamingAssets GLB count
  - Parts whose assetRef points at a filename not present in
    StreamingAssets/parts/
  - Parts whose assetRef is not listed in assetManifest.modelRefs
    (the runtime resolver consults the manifest first)
  - Same for tools

Usage: python Tools/audit_assetref_coverage.py [packageId]
"""

import json
import os
import sys


def list_glbs(d):
    if not os.path.isdir(d):
        return set()
    return set(f for f in os.listdir(d)
               if f.lower().endswith(('.glb', '.gltf', '.fbx')))


def filename_of(ref):
    return os.path.basename((ref or '').replace('\\', '/'))


def run(pkg):
    auth_parts = f'Assets/_Project/Data/Packages/{pkg}/assets/parts'
    auth_tools = f'Assets/_Project/Data/Packages/{pkg}/assets/tools'
    sa_parts   = f'Assets/StreamingAssets/MachinePackages/{pkg}/assets/parts'
    sa_tools   = f'Assets/StreamingAssets/MachinePackages/{pkg}/assets/tools'
    sa_machine = f'Assets/StreamingAssets/MachinePackages/{pkg}/machine.json'

    auth_part_glbs = list_glbs(auth_parts)
    auth_tool_glbs = list_glbs(auth_tools)
    sa_part_glbs   = list_glbs(sa_parts)
    sa_tool_glbs   = list_glbs(sa_tools)

    if not os.path.exists(sa_machine):
        print(f'ERROR: {sa_machine} missing — run OSE/Sync Packages first.')
        sys.exit(1)

    pkg_data = json.load(open(sa_machine, encoding='utf-8'))
    manifest = pkg_data.get('assetManifest', {}) or {}
    manifest_refs = set((manifest.get('modelRefs') or []))
    # As of fix 98da103 the manifest stores full relative paths
    # ("assets/parts/foo.glb"). Compare against filenames so a part's
    # bare-filename assetRef ("foo.glb") isn't flagged as missing.
    manifest_lower = {os.path.basename(m).lower() for m in manifest_refs}
    sa_part_lower  = {g.lower() for g in sa_part_glbs}
    sa_tool_lower  = {g.lower() for g in sa_tool_glbs}

    ref_by_part = {}
    for p in pkg_data.get('parts', []) or []:
        r = (p.get('assetRef') or '').strip()
        if r:
            ref_by_part[p['id']] = r
    ref_by_tool = {}
    for t in pkg_data.get('tools', []) or []:
        r = (t.get('assetRef') or '').strip()
        if r:
            ref_by_tool[t['id']] = r

    print(f'=== {pkg} ===')
    print(f'Authoring  parts/  : {len(auth_part_glbs)} GLBs')
    print(f'Authoring  tools/  : {len(auth_tool_glbs)} GLBs')
    print(f'Streaming  parts/  : {len(sa_part_glbs)} GLBs')
    print(f'Streaming  tools/  : {len(sa_tool_glbs)} GLBs')
    print(f'Manifest modelRefs : {len(manifest_refs)} entries')
    print(f'Parts.assetRef pop : {len(ref_by_part)} parts reference a GLB')
    print(f'Tools.assetRef pop : {len(ref_by_tool)} tools reference a GLB')
    print()

    missing_sa = []
    for pid, ref in ref_by_part.items():
        fn = filename_of(ref)
        if fn.lower() not in sa_part_lower:
            missing_sa.append((pid, ref, fn))

    missing_manifest = []
    for pid, ref in ref_by_part.items():
        fn = filename_of(ref)
        if fn.lower() not in manifest_lower:
            missing_manifest.append((pid, ref))

    print(f'PARTS missing GLB file in StreamingAssets/parts/: {len(missing_sa)}')
    for pid, ref, fn in missing_sa[:30]:
        also = ' (in authoring)' if fn in auth_part_glbs else ' (also missing from authoring)'
        print(f'  - {pid}  ref={ref}{also}')
    if len(missing_sa) > 30:
        print(f'  ... +{len(missing_sa)-30} more')
    print()

    print(f'PARTS with assetRef NOT in assetManifest.modelRefs: {len(missing_manifest)}')
    for pid, ref in missing_manifest[:30]:
        print(f'  - {pid}  ref={ref}')
    if len(missing_manifest) > 30:
        print(f'  ... +{len(missing_manifest)-30} more')
    print()

    tool_missing_sa = []
    tool_missing_manifest = []
    for tid, ref in ref_by_tool.items():
        fn = filename_of(ref)
        if fn.lower() not in sa_tool_lower:
            tool_missing_sa.append((tid, ref, fn))
        if fn.lower() not in manifest_lower:
            tool_missing_manifest.append((tid, ref))

    print(f'TOOLS missing GLB file in StreamingAssets/tools/: {len(tool_missing_sa)}')
    for tid, ref, fn in tool_missing_sa:
        also = ' (in authoring)' if fn in auth_tool_glbs else ' (also missing from authoring)'
        print(f'  - {tid}  ref={ref}{also}')
    print()

    print(f'TOOLS with assetRef NOT in assetManifest.modelRefs: {len(tool_missing_manifest)}')
    for tid, ref in tool_missing_manifest:
        print(f'  - {tid}  ref={ref}')


if __name__ == '__main__':
    args = sys.argv[1:]
    pkg = args[0] if args else 'd3d_v18_10'
    run(pkg)
