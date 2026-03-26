import json
import os
import sys

import FreeCAD as App
import MeshPart
import Part


def parse_args(argv):
    args = {
        "--input": os.environ.get("FCSTD_INPUT"),
        "--output": os.environ.get("FCSTD_OUTPUT"),
        "--report": os.environ.get("FCSTD_REPORT"),
        "--labels": os.environ.get("FCSTD_LABELS"),
    }
    index = 0
    while index < len(argv):
        token = argv[index]
        if token in args and index + 1 < len(argv):
            args[token] = argv[index + 1]
            index += 2
            continue
        index += 1
    if not args["--input"] or not args["--output"] or not args["--labels"]:
        raise SystemExit(
            "Usage: python export_fcstd_selection_to_stl.py --input <file.fcstd> --output <file.stl> --labels <comma-separated labels> [--report <file.json>]"
        )
    labels = [item.strip() for item in args["--labels"].split(",") if item.strip()]
    if not labels:
        raise SystemExit("At least one label or object name must be provided in --labels.")
    args["labels"] = labels
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
    )


def main():
    arguments = parse_args(sys.argv[1:])
    input_path = os.path.abspath(arguments["--input"])
    output_path = os.path.abspath(arguments["--output"])
    report_path = os.path.abspath(arguments["--report"]) if arguments["--report"] else None
    os.makedirs(os.path.dirname(output_path), exist_ok=True)
    if report_path:
        os.makedirs(os.path.dirname(report_path), exist_ok=True)

    document = App.openDocument(input_path)
    document.recompute()

    selected = []
    selected_labels = set(arguments["labels"])
    for obj in document.Objects:
        if obj.Name not in selected_labels and getattr(obj, "Label", None) not in selected_labels:
            continue
        shape = getattr(obj, "Shape", None)
        if not shape_is_valid(shape):
            continue
        selected.append(obj)

    if not selected:
        available = [{"name": obj.Name, "label": getattr(obj, "Label", "")} for obj in document.Objects]
        App.closeDocument(document.Name)
        raise RuntimeError(
            "No matching exportable objects found. Available objects: " + json.dumps(available, indent=2)
        )

    shapes = [obj.Shape for obj in selected]
    export_shape = shapes[0] if len(shapes) == 1 else Part.makeCompound(shapes)
    mesh = MeshPart.meshFromShape(
        Shape=export_shape,
        LinearDeflection=0.1,
        AngularDeflection=0.523599,
        Relative=False,
    )
    mesh.write(output_path)

    combined = shapes[0].BoundBox
    for shape in shapes[1:]:
        combined.add(shape.BoundBox)

    report = {
        "input_fcstd": input_path,
        "output_stl": output_path,
        "units": "mm",
        "selected_labels": arguments["labels"],
        "object_count": len(shapes),
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
        "objects": [
            {
                "name": obj.Name,
                "label": getattr(obj, "Label", ""),
                "bound_box": shape_bound_box(obj.Shape),
            }
            for obj in selected
        ],
    }

    App.closeDocument(document.Name)

    if report_path:
        with open(report_path, "w", encoding="utf-8") as handle:
            json.dump(report, handle, indent=2)

    print(json.dumps(report, indent=2))


if __name__ == "__main__":
    main()
