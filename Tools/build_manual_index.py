"""
build_manual_index.py — Index an OSE manual PDF into a structured JSON
that maps each page to its section heading, build stage, applicable
axes/instances, and best-fit assembly template.

Usage:
    python tools/build_manual_index.py "Assets/Resources/D3D Manuals/Axes - D3D v18.10.pdf"

Output: AgentAssistant/manual_indexes/<basename>.index.json

The index lets the manual->procedure pipeline answer in 1 lookup:
    "What template authors page 12?" → "IdlerHalves, instances=[y_left,z_back]"
"""

from __future__ import annotations
import json
import os
import re
import sys
from collections import defaultdict
from pypdf import PdfReader

# Heading regexes (left-to-right priority)
SECTION_HEADER = re.compile(
    r"^(?:I{1,3}V?|IV|V|VI{0,3}|VII|VIII|IX|X|[1-9]\d?\.)\s*\.?\s*"
    r"(?P<title>[A-Z][^\n]+)$",
    re.MULTILINE,
)
SUBSECTION = re.compile(
    r"^(?P<topic>Carriage|Idler|Motor|Frame|Tools|Cut List|Introduction|"
    r"Axis Assemblies|Functional Knowledge|Preparation)\s*[>\-]?\s*"
    r"(?P<scope>[A-Za-z\-/ ]+)?",
    re.MULTILINE,
)
STEP_LABEL = re.compile(r"^Step\s+(?P<num>\d+(?:\.\d+){0,2})\s*[:\-]\s*(?P<title>[^\n]+)",
                        re.MULTILINE)
AXIS_REF = re.compile(r"\b(Y[-\s]?Left|Y[-\s]?Right|Z[-\s]?Front|Z[-\s]?Back|X[-\s]?Axis)\b",
                      re.IGNORECASE)

# Template signatures — keyword fingerprints from CLAUDE.md
TEMPLATE_SIGS = {
    "BearingCarriage": {
        "any_of": [["bearing", "carriage", "shake"], ["LM8UU", "carriage"],
                   ["bearings", "carriage", "rod slide"]],
    },
    "IdlerHalves": {
        "any_of": [["idler", "M6x18", "bearing"], ["idler", "flange"],
                   ["idler", "frame", "loose"]],
    },
    "MotorHolder": {
        "any_of": [["motor", "pulley", "set screw"], ["motor", "belt", "channel"],
                   ["motor", "M3x25"], ["motor holder", "nut"]],
    },
    "RodAssembly": {
        "any_of": [["rod", "idler", "flush"], ["rod", "carriage", "slide"]],
    },
    "BeltThread": {
        "any_of": [["belt", "peg", "ribbed"], ["belt", "carriage", "thread"]],
    },
    # No-template categories (informational)
    "_PartsList": {"any_of": [["cut list"], ["parts:"], ["axes + bed holder"]]},
    "_Tools": {"any_of": [["tools & supplies"], ["power drill", "allen key"]]},
    "_Intro": {"any_of": [["introduction", "axis"], ["functional knowledge"]]},
    "_QC": {"any_of": [["quality control"], ["qc:"], ["plastic parts quality"]]},
    "_Prep": {"any_of": [["preparation"], ["clean up", "drill bit"]]},
}

AXIS_NORMALIZE = {
    "y-left": "y_left", "y left": "y_left", "yleft": "y_left",
    "y-right": "y_right", "y right": "y_right", "yright": "y_right",
    "z-front": "z_front", "z front": "z_front", "zfront": "z_front",
    "z-back": "z_back", "z back": "z_back", "zback": "z_back",
    "x-axis": "x_axis", "x axis": "x_axis", "xaxis": "x_axis",
}


def detect_template(text: str) -> str | None:
    """Pick the template whose any_of has the most fully-matched signature."""
    lc = text.lower()
    best = (None, 0)
    for tpl, spec in TEMPLATE_SIGS.items():
        for sig in spec["any_of"]:
            if all(kw.lower() in lc for kw in sig):
                score = len(sig)
                if score > best[1]:
                    best = (tpl, score)
    return best[0]


def detect_axes(text: str) -> list[str]:
    """Extract canonical axis IDs mentioned on the page."""
    found = set()
    for m in AXIS_REF.finditer(text):
        key = m.group(0).lower().replace("-", " ").strip()
        key = re.sub(r"\s+", " ", key)
        norm = AXIS_NORMALIZE.get(key) or AXIS_NORMALIZE.get(key.replace(" ", ""))
        if norm:
            found.add(norm)
    return sorted(found)


def extract_section(text: str) -> tuple[str | None, str | None]:
    """Return (toplevel_section, subtopic) by scanning the first N lines."""
    section = None
    subtopic = None
    lines = text.split("\n")[:8]
    for line in lines:
        line = line.strip()
        if not line:
            continue
        m = SECTION_HEADER.match(line)
        if m and not section:
            section = m.group("title").strip()
        m2 = SUBSECTION.match(line)
        if m2 and not subtopic:
            topic = m2.group("topic").strip()
            scope = (m2.group("scope") or "").strip()
            subtopic = f"{topic} > {scope}" if scope else topic
    return section, subtopic


def extract_steps(text: str) -> list[dict]:
    """Find Step X.Y: Title patterns."""
    return [{"num": m.group("num"), "title": m.group("title").strip()}
            for m in STEP_LABEL.finditer(text)]


def index_pdf(pdf_path: str) -> dict:
    reader = PdfReader(pdf_path)
    pages = []
    for i, page in enumerate(reader.pages, start=1):
        text = page.extract_text() or ""
        section, subtopic = extract_section(text)
        steps = extract_steps(text)
        axes = detect_axes(text)
        template = detect_template(text)
        pages.append({
            "page": i,
            "section": section,
            "subtopic": subtopic,
            "steps": steps,
            "applies_to": axes,
            "matches_template": template,
            "char_count": len(text),
        })
    # Roll up: which page ranges cover which (template, axes) tuple
    rollups = defaultdict(list)
    for p in pages:
        if p["matches_template"] and not p["matches_template"].startswith("_"):
            key = (p["matches_template"], tuple(p["applies_to"]))
            rollups[key].append(p["page"])
    rollup_list = [
        {"template": tpl, "axes": list(axes), "pages": sorted(pgs)}
        for (tpl, axes), pgs in sorted(rollups.items())
    ]
    return {
        "source": os.path.basename(pdf_path),
        "page_count": len(pages),
        "pages": pages,
        "rollups": rollup_list,
    }


def main():
    if len(sys.argv) < 2:
        print(__doc__)
        sys.exit(1)
    pdf_path = sys.argv[1]
    if not os.path.isfile(pdf_path):
        print(f"PDF not found: {pdf_path}")
        sys.exit(1)

    out_dir = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))),
                           "AgentAssistant", "manual_indexes")
    os.makedirs(out_dir, exist_ok=True)
    base = os.path.splitext(os.path.basename(pdf_path))[0]
    out_path = os.path.join(out_dir, base + ".index.json")

    idx = index_pdf(pdf_path)
    with open(out_path, "w", encoding="utf-8") as f:
        json.dump(idx, f, indent=2)

    print(f"Indexed {idx['page_count']} pages -> {out_path}")
    print(f"Rollups ({len(idx['rollups'])}):")
    for r in idx["rollups"]:
        ax = ",".join(r["axes"]) if r["axes"] else "(no axis)"
        pgs = r["pages"]
        rng = f"{pgs[0]}-{pgs[-1]}" if len(pgs) > 1 else str(pgs[0])
        print(f"  {r['template']:18} {ax:30} pages {rng} ({len(pgs)})")


if __name__ == "__main__":
    main()
