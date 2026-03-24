import json
import os
import sys
import tempfile
import zipfile

import FreeCAD as App
import Mesh
import MeshPart
import Part


def parse_args(argv):
    args = {
        "--input": os.environ.get("FCSTD_INPUT"),
        "--output": os.environ.get("FCSTD_OUTPUT"),
        "--report": os.environ.get("FCSTD_REPORT"),
    }
    index = 0
    while index < len(argv):
        token = argv[index]
        if token in args and index + 1 < len(argv):
            args[token] = argv[index + 1]
            index += 2
            continue
        index += 1
    if not args["--input"] or not args["--output"]:
        raise SystemExit(
            "Usage: freecadcmd.exe export_fcstd_to_stl.py --input <file.fcstd> --output <file.stl> [--report <file.json>] or set FCSTD_INPUT/FCSTD_OUTPUT/FCSTD_REPORT"
        )
    return args


def shape_bound_box(shape):
    box = shape.BoundBox
    return {
        "xmin_mm": box.XMin,
        "xmax_mm": box.XMax,
        "ymin_mm": box.YMin,
        "ymax_mm": box.YMax,
        "zmin_mm": box.ZMin,
        "zmax_mm": box.ZMax,
        "xlen_mm": box.XLength,
        "ylen_mm": box.YLength,
        "zlen_mm": box.ZLength,
    }


def shape_is_valid(shape):
    return (
        shape is not None
        and not shape.isNull()
        and len(shape.Faces) > 0
        and shape.BoundBox.XLength == shape.BoundBox.XLength
        and shape.BoundBox.YLength == shape.BoundBox.YLength
        and shape.BoundBox.ZLength == shape.BoundBox.ZLength
        and shape.BoundBox.XLength >= 0
        and shape.BoundBox.YLength >= 0
        and shape.BoundBox.ZLength >= 0
    )


def load_shapes_from_archive(input_path):
    loaded = []
    with zipfile.ZipFile(input_path, "r") as archive:
        brp_entries = [
            info.filename
            for info in archive.infolist()
            if info.filename.lower().endswith((".brp", ".brep"))
        ]
        for entry_name in brp_entries:
            data = archive.read(entry_name)
            with tempfile.NamedTemporaryFile(delete=False, suffix=".brp") as handle:
                handle.write(data)
                temp_path = handle.name
            try:
                shape = Part.Shape()
                shape.read(temp_path)
                if shape_is_valid(shape):
                    loaded.append((entry_name, shape))
            finally:
                os.unlink(temp_path)
    return loaded


def load_shapes_from_document(input_path):
    document = App.openDocument(input_path)
    document.recompute()
    loaded = []
    for obj in document.Objects:
        shape = getattr(obj, "Shape", None)
        if not shape_is_valid(shape):
            continue
        loaded.append((getattr(obj, "Name", "Object"), shape))
    App.closeDocument(document.Name)
    return loaded


def main():
    arguments = parse_args(sys.argv[1:])
    input_path = os.path.abspath(arguments["--input"])
    output_path = os.path.abspath(arguments["--output"])
    report_path = (
        os.path.abspath(arguments["--report"]) if arguments["--report"] else None
    )

    os.makedirs(os.path.dirname(output_path), exist_ok=True)
    if report_path:
        os.makedirs(os.path.dirname(report_path), exist_ok=True)

    loaded_shapes = load_shapes_from_archive(input_path)
    source_mode = "archive_brep"
    if not loaded_shapes:
        loaded_shapes = load_shapes_from_document(input_path)
        source_mode = "document_shape"
    if not loaded_shapes:
        raise RuntimeError("No exportable shapes found in document or embedded BREP payloads.")

    export_shapes = [shape for _, shape in loaded_shapes]
    object_reports = []
    for name, shape in loaded_shapes:
        object_reports.append(
            {
                "name": name,
                "bound_box": shape_bound_box(shape),
            }
        )

    export_shape = export_shapes[0] if len(export_shapes) == 1 else Part.makeCompound(export_shapes)
    mesh = MeshPart.meshFromShape(
        Shape=export_shape,
        LinearDeflection=0.1,
        AngularDeflection=0.523599,
        Relative=False,
    )
    mesh.write(output_path)

    combined = export_shapes[0].BoundBox
    for shape in export_shapes[1:]:
        combined.add(shape.BoundBox)

    report = {
        "input_fcstd": input_path,
        "output_stl": output_path,
        "source_mode": source_mode,
        "units": "mm",
        "object_count": len(export_shapes),
        "mesh_facets": mesh.CountFacets,
        "mesh_points": mesh.CountPoints,
        "combined_bound_box": {
            "xmin_mm": combined.XMin,
            "xmax_mm": combined.XMax,
            "ymin_mm": combined.YMin,
            "ymax_mm": combined.YMax,
            "zmin_mm": combined.ZMin,
            "zmax_mm": combined.ZMax,
            "xlen_mm": combined.XLength,
            "ylen_mm": combined.YLength,
            "zlen_mm": combined.ZLength,
        },
        "objects": object_reports,
    }

    if report_path:
        with open(report_path, "w", encoding="utf-8") as handle:
            json.dump(report, handle, indent=2)
    print(json.dumps(report, indent=2))


if __name__ == "__main__":
    main()
