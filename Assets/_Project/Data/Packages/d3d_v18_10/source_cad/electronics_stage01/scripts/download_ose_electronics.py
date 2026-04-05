"""
download_ose_electronics.py
Downloads the four official OSE FreeCAD source files for D3D electronics components
from wiki.opensourceecology.org into the raw/ directory alongside this script.

Usage:
    python download_ose_electronics.py

Files downloaded:
    RAMPS14_v1904.fcstd         — RAMPS 1.4 controller board
    Powersupply_v1904.fcstd     — ATX power supply housing
    Smartcontroller_v1904.fcstd — Smart Controller (LCD + encoder)
    Controlpanel_v1904.fcstd    — Control panel enclosure

All CC BY-SA 4.0 — Open Source Ecology.
"""

import os
import urllib.request

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
RAW_DIR = os.path.join(SCRIPT_DIR, "..", "raw")

OSE_BASE = "https://wiki.opensourceecology.org/images"

ASSETS = [
    {
        "url": f"{OSE_BASE}/d/da/RAMPS14_v1904.fcstd",
        "filename": "RAMPS14_v1904.fcstd",
        "part_id": "ramps_14_board",
        "description": "RAMPS 1.4 controller board",
    },
    {
        "url": f"{OSE_BASE}/7/74/Powersupply_v1904.fcstd",
        "filename": "Powersupply_v1904.fcstd",
        "part_id": "d3d_psu_atx",
        "description": "ATX power supply housing",
    },
    {
        "url": f"{OSE_BASE}/7/7f/Smartcontroller_v1904.fcstd",
        "filename": "Smartcontroller_v1904.fcstd",
        "part_id": "d3d_smart_controller",
        "description": "Smart Controller (LCD + rotary encoder)",
    },
    {
        "url": f"{OSE_BASE}/c/cc/Controlpanel_v1904.fcstd",
        "filename": "Controlpanel_v1904.fcstd",
        "part_id": "d3d_control_panel",
        "description": "Control panel enclosure",
    },
]


def download(url: str, dest: str, description: str) -> bool:
    if os.path.exists(dest):
        size = os.path.getsize(dest)
        print(f"  [skip] {os.path.basename(dest)} already exists ({size:,} bytes)")
        return True

    print(f"  Downloading {description}...")
    headers = {
        "User-Agent": "Mozilla/5.0 OSE-Blueprints-Assembler/1.0 (content pipeline; CC BY-SA 4.0 asset)",
        "Accept": "*/*",
    }
    req = urllib.request.Request(url, headers=headers)
    try:
        with urllib.request.urlopen(req, timeout=30) as resp:
            data = resp.read()
        with open(dest, "wb") as f:
            f.write(data)
        print(f"  [ok]   {os.path.basename(dest)} — {len(data):,} bytes")
        return True
    except Exception as exc:
        print(f"  [FAIL] {os.path.basename(dest)}: {exc}")
        return False


def main():
    os.makedirs(RAW_DIR, exist_ok=True)

    print("OSE Electronics FreeCAD Download")
    print(f"  Target: {RAW_DIR}")
    print()

    ok = 0
    for asset in ASSETS:
        dest = os.path.join(RAW_DIR, asset["filename"])
        if download(asset["url"], dest, asset["description"]):
            ok += 1

    print()
    print(f"Done: {ok}/{len(ASSETS)} files ready in raw/")
    if ok == len(ASSETS):
        print("Next step: run run_pipeline.ps1 to convert all FCStd → GLB")
    else:
        print("WARNING: Some downloads failed. Check your connection and retry.")


if __name__ == "__main__":
    main()
