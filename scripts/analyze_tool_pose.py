#!/usr/bin/env python3
"""Auto-detect toolPose spatial metadata for OSE Blueprints Assembler tool models.

Hybrid pipeline:
  Step 1: Geometric PCA analysis (trimesh) — detects grip, tip, axes
  Step 2: Multi-angle Blender render with candidate markers (optional)
  Step 3: Claude Vision validation/correction (optional, requires API key)
  Step 4: Write results to machine.json

Usage:
  # Single tool, geometry only (free, fast)
  python analyze_tool_pose.py \
    --glb "Assets/_Project/Data/Packages/d3d_v18_10/assets/tools/tool_torque_wrench.glb" \
    --tool-id tool_torque_wrench \
    --geo-only

  # Single tool, full hybrid (PCA + render + Claude Vision)
  python analyze_tool_pose.py \
    --glb "Assets/_Project/Data/Packages/d3d_v18_10/assets/tools/tool_torque_wrench.glb" \
    --tool-id tool_torque_wrench \
    --tool-name "Torque Wrench" \
    --purpose "Tightens bolts to precise torque specification" \
    --package-json "Assets/_Project/Data/Packages/d3d_v18_10/machine.json" \
    --api-key $ANTHROPIC_API_KEY

  # Batch all tools in a package
  python analyze_tool_pose.py \
    --package-json "Assets/_Project/Data/Packages/d3d_v18_10/machine.json" \
    --batch \
    --api-key $ANTHROPIC_API_KEY

  # Skip Blender render (PCA + Claude text-only)
  python analyze_tool_pose.py \
    --glb "..." --tool-id "..." \
    --no-render --api-key $ANTHROPIC_API_KEY
"""

import argparse
import json
import os
import subprocess
import sys
import tempfile
from pathlib import Path

import numpy as np

try:
    import trimesh
except ImportError:
    print("ERROR: trimesh is required. Install with: pip install trimesh", file=sys.stderr)
    sys.exit(1)


# ─── Geometric Analysis (PCA + heuristics) ──────────────────────────────────


def analyze_geometry(glb_path: str) -> dict:
    """Run PCA on mesh vertices to detect grip, tip, axes, and shape class."""
    loaded = trimesh.load(glb_path)

    # Collect all vertices — handle both Scene and single Trimesh
    all_verts = []
    if isinstance(loaded, trimesh.Scene):
        for name, geom in loaded.geometry.items():
            if not hasattr(geom, "vertices"):
                continue
            # Try to get the transform; fall back to identity if graph lookup fails
            try:
                transform = loaded.graph.get(name)[0]
            except (ValueError, KeyError, TypeError):
                transform = np.eye(4)
            verts = np.hstack([geom.vertices, np.ones((len(geom.vertices), 1))])
            world_verts = (transform @ verts.T).T[:, :3]
            all_verts.append(world_verts)
    elif hasattr(loaded, "vertices"):
        all_verts.append(np.array(loaded.vertices))

    if not all_verts:
        raise ValueError(f"No mesh vertices found in {glb_path}")

    points = np.vstack(all_verts)
    centroid = points.mean(axis=0)

    # PCA via covariance eigendecomposition
    centered = points - centroid
    cov = np.cov(centered.T)
    eigenvalues, eigenvectors = np.linalg.eigh(cov)

    # Sort by variance (descending): primary axis has most spread
    order = np.argsort(eigenvalues)[::-1]
    eigenvalues = eigenvalues[order]
    axes = eigenvectors[:, order].T  # rows = principal axes

    # Anisotropy: how elongated is the model?
    anisotropy = eigenvalues[0] / max(eigenvalues[2], 1e-10)

    # Shape classification
    ratio_01 = eigenvalues[0] / max(eigenvalues[1], 1e-10)
    ratio_12 = eigenvalues[1] / max(eigenvalues[2], 1e-10)
    if ratio_01 < 1.5:
        shape_class = "sphere"  # roughly equal spread in all directions
    elif ratio_01 < 3.0 and ratio_12 < 1.5:
        shape_class = "disc"    # flat but wide
    else:
        shape_class = "shaft"   # elongated

    # Primary axis (longest spread)
    primary = axes[0]

    # Tip: farthest point from centroid along primary axis (positive direction)
    projections = centered @ primary
    tip_idx = np.argmax(projections)
    grip_idx = np.argmin(projections)

    tip_point = points[tip_idx]
    grip_point_candidate = points[grip_idx]

    # Refine grip: shift from extreme vertex toward centroid (hand wraps around handle)
    grip_to_centroid = centroid - grip_point_candidate
    grip_point = grip_point_candidate + grip_to_centroid * 0.3

    # Tip axis: from grip toward tip, normalized
    tip_axis = (tip_point - grip_point)
    tip_axis = tip_axis / max(np.linalg.norm(tip_axis), 1e-10)

    # Action axis: secondary principal component (perpendicular to shaft)
    action_axis = axes[1]

    # Ensure tip axis points in consistent direction (positive along primary)
    if np.dot(tip_axis, primary) < 0:
        tip_axis = -tip_axis
        tip_point, grip_point = grip_point, tip_point

    # Bounding box
    bmin = points.min(axis=0)
    bmax = points.max(axis=0)

    return {
        "gripPoint": grip_point.tolist(),
        "tipPoint": tip_point.tolist(),
        "tipAxis": tip_axis.tolist(),
        "actionAxis": action_axis.tolist(),
        "centroid": centroid.tolist(),
        "bounds": {"min": bmin.tolist(), "max": bmax.tolist()},
        "shapeClass": shape_class,
        "anisotropy": float(anisotropy),
        "eigenvalues": eigenvalues.tolist(),
        "principalAxes": axes.tolist(),
    }


# ─── Blender Rendering ──────────────────────────────────────────────────────


def find_blender() -> str:
    """Find Blender executable."""
    candidates = [
        r"C:\Program Files\Blender Foundation\Blender 5.0\blender.exe",
        r"C:\Program Files\Blender Foundation\Blender 4.3\blender.exe",
        r"C:\Program Files\Blender Foundation\Blender 4.2\blender.exe",
        "blender",
    ]
    for c in candidates:
        if os.path.isfile(c):
            return c
    # Try PATH
    import shutil
    found = shutil.which("blender")
    if found:
        return found
    return None


def render_views(glb_path: str, markers: dict, output_dir: str, blender_path: str = None) -> list:
    """Render 4 orthographic views with PCA markers overlaid."""
    if blender_path is None:
        blender_path = find_blender()
    if blender_path is None:
        print("WARNING: Blender not found. Skipping render step.", file=sys.stderr)
        return []

    script_dir = Path(__file__).parent
    render_script = script_dir / "render_tool_views.py"
    if not render_script.exists():
        print(f"WARNING: render_tool_views.py not found at {render_script}", file=sys.stderr)
        return []

    os.makedirs(output_dir, exist_ok=True)

    # Write markers to temp file
    markers_file = os.path.join(output_dir, "markers.json")
    with open(markers_file, "w") as f:
        json.dump(markers, f, indent=2)

    cmd = [
        blender_path, "-b", "-P", str(render_script), "--",
        "--input", glb_path,
        "--output-dir", output_dir,
        "--markers", markers_file,
    ]

    print(f"Rendering 4 views with Blender...")
    result = subprocess.run(cmd, capture_output=True, text=True, timeout=120)
    if result.returncode != 0:
        print(f"Blender render failed:\n{result.stderr}", file=sys.stderr)
        return []

    views = ["front.png", "right.png", "top.png", "perspective.png"]
    return [os.path.join(output_dir, v) for v in views if os.path.exists(os.path.join(output_dir, v))]


# ─── Claude Vision Validation ────────────────────────────────────────────────


def validate_with_vision(
    tool_name: str,
    tool_purpose: str,
    geo_results: dict,
    image_paths: list,
    api_key: str,
) -> dict:
    """Send annotated renders to Claude Vision for validation/correction."""
    try:
        import anthropic
    except ImportError:
        print("WARNING: anthropic SDK not installed. Skipping Vision validation.", file=sys.stderr)
        print("Install with: pip install anthropic", file=sys.stderr)
        return geo_results

    import base64

    client = anthropic.Anthropic(api_key=api_key)

    # Build image content blocks
    content = []
    for img_path in image_paths:
        with open(img_path, "rb") as f:
            img_data = base64.standard_b64encode(f.read()).decode("utf-8")
        view_name = Path(img_path).stem
        content.append({"type": "text", "text": f"View: {view_name}"})
        content.append({
            "type": "image",
            "source": {"type": "base64", "media_type": "image/png", "data": img_data},
        })

    # Structured prompt
    prompt = f"""You are analyzing a 3D tool model for an XR assembly training application.

Tool: {tool_name}
Purpose: {tool_purpose}

The images show 4 orthographic views (front, right, top, perspective) of the tool with auto-detected spatial markers:
- BLUE sphere: detected grip center (where the user's hand holds the tool)
- RED sphere: detected tip/business end (where the tool contacts the workpiece)
- GREEN arrow from grip: detected tip axis direction (business end faces this way)
- YELLOW arrow from grip: detected action axis (primary motion direction during use)

Auto-detected values (model local space, meters):
- gripPoint: {json.dumps(geo_results['gripPoint'])}
- tipPoint: {json.dumps(geo_results['tipPoint'])}
- tipAxis: {json.dumps(geo_results['tipAxis'])}
- actionAxis: {json.dumps(geo_results['actionAxis'])}
- Shape class: {geo_results['shapeClass']}

Please evaluate each detected point:
1. Is the GRIP point at a natural hand-hold location? If not, where should it be?
2. Is the TIP point at the tool's business end (where it contacts the workpiece)? If not, where?
3. Is the TIP AXIS pointing from grip toward the business end? Correct?
4. Is the ACTION AXIS appropriate for this tool's primary motion?
   - For wrenches/screwdrivers: rotation axis (perpendicular to shaft)
   - For welding torches/grinders: travel direction (along shaft toward tip)
   - For tape measures: extension direction (along shaft)
   - For hammers: impact direction (along shaft toward head)
5. What handedness is natural? ("right", "left", or "either")
6. What grip type? ("power_grip", "pinch", "precision", or "two_hand")

Respond with ONLY a JSON object (no markdown, no explanation):
{{
  "gripPoint": [x, y, z],
  "tipPoint": [x, y, z],
  "tipAxis": [x, y, z],
  "actionAxis": [x, y, z],
  "handedness": "right|left|either",
  "poseHint": "power_grip|pinch|precision|two_hand",
  "confidence": "high|medium|low",
  "notes": "brief explanation of any corrections made"
}}
"""
    content.append({"type": "text", "text": prompt})

    print("Sending to Claude Vision for validation...")
    response = client.messages.create(
        model="claude-sonnet-4-20250514",
        max_tokens=1024,
        messages=[{"role": "user", "content": content}],
    )

    # Parse response
    response_text = response.content[0].text.strip()
    # Strip markdown code fences if present
    if response_text.startswith("```"):
        lines = response_text.split("\n")
        lines = [l for l in lines if not l.startswith("```")]
        response_text = "\n".join(lines)

    try:
        validated = json.loads(response_text)
        print(f"Vision validation: confidence={validated.get('confidence', '?')}")
        if validated.get("notes"):
            print(f"  Notes: {validated['notes']}")
        return validated
    except json.JSONDecodeError:
        print(f"WARNING: Could not parse Vision response. Using geometric results.", file=sys.stderr)
        print(f"  Response: {response_text[:200]}", file=sys.stderr)
        return geo_results


# ─── machine.json Integration ────────────────────────────────────────────────


def round_vec(v, decimals=5):
    """Round a list of floats for clean JSON output."""
    return [round(x, decimals) for x in v]


def build_tool_pose(results: dict) -> dict:
    """Convert analysis results to machine.json toolPose format."""
    pose = {}

    if "gripPoint" in results:
        gp = round_vec(results["gripPoint"])
        pose["gripPoint"] = {"x": gp[0], "y": gp[1], "z": gp[2]}

    if "tipPoint" in results:
        tp = round_vec(results["tipPoint"])
        pose["tipPoint"] = {"x": tp[0], "y": tp[1], "z": tp[2]}

    if "tipAxis" in results:
        ta = round_vec(results["tipAxis"])
        pose["tipAxis"] = {"x": ta[0], "y": ta[1], "z": ta[2]}

    if "actionAxis" in results:
        aa = round_vec(results["actionAxis"])
        pose["actionAxis"] = {"x": aa[0], "y": aa[1], "z": aa[2]}

    if "handedness" in results:
        pose["handedness"] = results["handedness"]

    if "poseHint" in results:
        pose["poseHint"] = results["poseHint"]

    return pose


def write_to_machine_json(package_json_path: str, tool_id: str, tool_pose: dict):
    """Merge toolPose into the tool's definition in machine.json."""
    with open(package_json_path, "r", encoding="utf-8") as f:
        package = json.load(f)

    # Find the tool definition
    tools = package.get("tools", [])
    tool_found = False
    for tool in tools:
        if tool.get("id") == tool_id:
            tool["toolPose"] = tool_pose
            tool_found = True
            print(f"Updated toolPose for '{tool_id}' in {package_json_path}")
            break

    if not tool_found:
        print(f"WARNING: Tool '{tool_id}' not found in {package_json_path}", file=sys.stderr)
        return False

    with open(package_json_path, "w", encoding="utf-8") as f:
        json.dump(package, f, indent=2, ensure_ascii=False)

    return True


# ─── Batch Processing ────────────────────────────────────────────────────────


def get_tools_from_package(package_json_path: str) -> list:
    """Extract tool definitions from machine.json."""
    with open(package_json_path, "r", encoding="utf-8") as f:
        package = json.load(f)

    tools = package.get("tools", [])
    package_dir = os.path.dirname(package_json_path)
    result = []
    for tool in tools:
        asset_ref = tool.get("assetRef", "")
        glb_path = os.path.join(package_dir, asset_ref) if asset_ref else None
        result.append({
            "id": tool.get("id"),
            "name": tool.get("name", tool.get("id", "Unknown")),
            "purpose": tool.get("purpose", ""),
            "glb_path": glb_path,
            "has_tool_pose": "toolPose" in tool,
        })
    return result


# ─── Main ────────────────────────────────────────────────────────────────────


def main():
    parser = argparse.ArgumentParser(description="Auto-detect toolPose spatial metadata for OSE tool models")
    parser.add_argument("--glb", help="Path to GLB tool model")
    parser.add_argument("--tool-id", help="Tool ID (e.g. tool_torque_wrench)")
    parser.add_argument("--tool-name", help="Human-readable tool name")
    parser.add_argument("--purpose", help="Tool purpose description")
    parser.add_argument("--package-json", help="Path to machine.json (for writing results or batch)")
    parser.add_argument("--api-key", help="Anthropic API key for Vision validation")
    parser.add_argument("--blender", help="Path to Blender executable")
    parser.add_argument("--output-dir", help="Directory for renders and reports")
    parser.add_argument("--geo-only", action="store_true", help="Skip render and Vision; geometry only")
    parser.add_argument("--no-render", action="store_true", help="Skip Blender render; use text-only Vision")
    parser.add_argument("--batch", action="store_true", help="Process all tools in package-json")
    parser.add_argument("--force", action="store_true", help="Re-analyze tools that already have toolPose")
    parser.add_argument("--dry-run", action="store_true", help="Print results without writing to machine.json")
    args = parser.parse_args()

    if args.batch:
        if not args.package_json:
            parser.error("--batch requires --package-json")
        tools = get_tools_from_package(args.package_json)
        print(f"Found {len(tools)} tools in {args.package_json}")

        for tool_info in tools:
            if tool_info["has_tool_pose"] and not args.force:
                print(f"  SKIP {tool_info['id']} (already has toolPose, use --force to re-analyze)")
                continue
            if not tool_info["glb_path"] or not os.path.exists(tool_info["glb_path"]):
                print(f"  SKIP {tool_info['id']} (GLB not found: {tool_info['glb_path']})")
                continue

            print(f"\n{'='*60}")
            print(f"  Analyzing: {tool_info['id']}")
            print(f"{'='*60}")

            process_single_tool(
                glb_path=tool_info["glb_path"],
                tool_id=tool_info["id"],
                tool_name=tool_info["name"],
                purpose=tool_info["purpose"],
                package_json=args.package_json,
                api_key=args.api_key,
                blender_path=args.blender,
                output_dir=args.output_dir,
                geo_only=args.geo_only,
                no_render=args.no_render,
                dry_run=args.dry_run,
            )
    else:
        if not args.glb or not args.tool_id:
            parser.error("--glb and --tool-id required (or use --batch with --package-json)")

        process_single_tool(
            glb_path=args.glb,
            tool_id=args.tool_id,
            tool_name=args.tool_name or args.tool_id,
            purpose=args.purpose or "",
            package_json=args.package_json,
            api_key=args.api_key,
            blender_path=args.blender,
            output_dir=args.output_dir,
            geo_only=args.geo_only,
            no_render=args.no_render,
            dry_run=args.dry_run,
        )


def process_single_tool(
    glb_path: str,
    tool_id: str,
    tool_name: str,
    purpose: str,
    package_json: str = None,
    api_key: str = None,
    blender_path: str = None,
    output_dir: str = None,
    geo_only: bool = False,
    no_render: bool = False,
    dry_run: bool = False,
):
    """Full pipeline for a single tool."""
    # Step 1: Geometric analysis
    print(f"Step 1: PCA geometric analysis of {glb_path}")
    geo = analyze_geometry(glb_path)
    print(f"  Shape: {geo['shapeClass']} (anisotropy: {geo['anisotropy']:.1f})")
    print(f"  Grip: [{geo['gripPoint'][0]:.4f}, {geo['gripPoint'][1]:.4f}, {geo['gripPoint'][2]:.4f}]")
    print(f"  Tip:  [{geo['tipPoint'][0]:.4f}, {geo['tipPoint'][1]:.4f}, {geo['tipPoint'][2]:.4f}]")

    results = geo

    if not geo_only:
        # Step 2: Render views
        image_paths = []
        if not no_render:
            if output_dir is None:
                output_dir = os.path.join(tempfile.gettempdir(), f"toolpose_{tool_id}")

            render_dir = os.path.join(output_dir, tool_id)
            markers = {
                "gripPoint": geo["gripPoint"],
                "tipPoint": geo["tipPoint"],
                "tipAxis": geo["tipAxis"],
                "actionAxis": geo["actionAxis"],
            }
            image_paths = render_views(glb_path, markers, render_dir, blender_path)
            if image_paths:
                print(f"Step 2: Rendered {len(image_paths)} views to {render_dir}")
            else:
                print("Step 2: Render skipped (Blender not available)")

        # Step 3: Vision validation
        if api_key:
            results = validate_with_vision(tool_name, purpose, geo, image_paths, api_key)
        else:
            print("Step 3: Vision validation skipped (no --api-key)")

    # Build toolPose
    tool_pose = build_tool_pose(results)

    print(f"\nResult toolPose for '{tool_id}':")
    print(json.dumps(tool_pose, indent=2))

    # Step 4: Write to machine.json
    if package_json and not dry_run:
        write_to_machine_json(package_json, tool_id, tool_pose)
    elif dry_run:
        print("(dry-run: not writing to machine.json)")

    # Save analysis report
    if output_dir:
        report_dir = os.path.join(output_dir, tool_id)
        os.makedirs(report_dir, exist_ok=True)
        report_path = os.path.join(report_dir, "analysis_report.json")
        report = {
            "toolId": tool_id,
            "toolName": tool_name,
            "glbPath": glb_path,
            "geometry": {
                "shapeClass": geo["shapeClass"],
                "anisotropy": geo["anisotropy"],
                "bounds": geo["bounds"],
                "centroid": geo["centroid"],
            },
            "toolPose": tool_pose,
        }
        with open(report_path, "w") as f:
            json.dump(report, f, indent=2)
        print(f"Report saved to {report_path}")


if __name__ == "__main__":
    main()
