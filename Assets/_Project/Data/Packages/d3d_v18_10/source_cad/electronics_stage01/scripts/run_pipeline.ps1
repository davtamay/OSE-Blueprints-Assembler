# run_pipeline.ps1 - FreeCAD -> STL -> GLB -> gltfpack -> placements.json for D3D electronics
# Usage:  .\run_pipeline.ps1
#         .\run_pipeline.ps1 -Only d3d_control_panel
#         .\run_pipeline.ps1 -SkipGeometry   (recompute placements.json only)
#
# Assembly anchors (asm_x/y/z) are in FreeCAD D3D assembly space (mm).
# compute_play_position.py converts these to Unity playPosition.
# See PART_AUTHORING_PIPELINE.md SS9.12 for how to measure anchors from CAD.
param([string]$Only = "", [switch]$SkipGeometry)
$ErrorActionPreference = "Stop"

$ScriptDir   = Split-Path -Parent $MyInvocation.MyCommand.Path
$StageDir    = Split-Path -Parent $ScriptDir
$PackageDir  = Split-Path -Parent (Split-Path -Parent $StageDir)
$RepoRoot    = Split-Path -Parent (Split-Path -Parent (Split-Path -Parent $PackageDir))

$SharedScripts  = Join-Path $StageDir "..\extruder\scripts"
$FcScript       = Join-Path $SharedScripts "export_fcstd_to_stl.py"
$BlScript       = Join-Path $SharedScripts "stl_to_glb.py"
$PlacementScript= Join-Path $SharedScripts "compute_play_position.py"
$GltfpackExe    = Join-Path $RepoRoot "Tools\gltfpack.exe"
$PartsOutDir    = Join-Path $PackageDir "assets\parts"
$RawDir         = Join-Path $StageDir "raw"
$StlDir         = Join-Path $StageDir "exported\stl"
$CandidateDir   = Join-Path $StageDir "exported\glb_candidates"
$ReportDir      = Join-Path $StageDir "exported\reports"
$PlacementsJson = Join-Path $StageDir "placements.json"

$FreeCADCmd = "C:\Program Files\FreeCAD 1.0\bin\freecadcmd.exe"
$BlenderExe = "C:\Program Files\Blender Foundation\Blender 5.0\blender.exe"

# Assembly anchors: position of the part's pivot in D3D assembly space (FreeCAD mm).
#   base_center -> bottom-center of the part bbox in the assembled machine
#   center      -> geometric centroid of the part in the assembled machine
#
# Electronics mount on the LEFT face of the D3D frame (FreeCAD X < 0).
# Frame X range: 0-304.8mm. Frame LEFT face: X=0. Panel right face at X=0, center at X=-68mm.
# Frame Y range: 0-304.8mm. Panel aligns with front half of frame depth (Y~76mm).
# Frame Z: starts at 0. Panel base at ground (Z=0, base_center).
#
# Layout (from Ontrolpanelseated.png CAD render):
#   - Control panel enclosure: left face of frame, ground level, door opens outward (-X)
#   - PSU: inside enclosure upper section (Z~65mm)
#   - RAMPS board: inside enclosure lower section (Z~15mm)
#   - Smart controller LCD: on door face of enclosure (Y = panel_center_Y - 68mm, Z~68mm)
#
# SOURCE: wiki.opensourceecology.org/wiki/D3D_Pro Ontrolpanelseated.png CAD render
# NOTE: X/Y/Z confirmed from image; sub-mm fine-tuning via Target Authoring tool in Unity.
$Components = @(
    @{ part_id="d3d_control_panel";    fcstd="Controlpanel_v1904.fcstd";   color="0.18,0.18,0.20,1.0"; rough="0.65"; metal="0.0"; mat="OSE Panel";      cx="base_center"; asm_x=-68.0; asm_y=76.2;  asm_z=0.0   },
    @{ part_id="d3d_psu_atx";          fcstd="Powersupply_v1904.fcstd";    color="0.15,0.15,0.18,1.0"; rough="0.60"; metal="0.0"; mat="OSE PSU";        cx="base_center"; asm_x=-68.0; asm_y=76.2;  asm_z=65.0  },
    @{ part_id="d3d_ramps_14_board";   fcstd="RAMPS14_v1904.fcstd";        color="0.05,0.25,0.10,1.0"; rough="0.65"; metal="0.0"; mat="OSE PCB";        cx="base_center"; asm_x=-68.0; asm_y=76.2;  asm_z=15.0  },
    @{ part_id="d3d_smart_controller"; fcstd="Smartcontroller_v1904.fcstd";color="0.10,0.10,0.12,1.0"; rough="0.70"; metal="0.0"; mat="OSE Controller"; cx="center";      asm_x=-136.0; asm_y=58.9; asm_z=67.5  }
)

New-Item -ItemType Directory -Force -Path $StlDir       | Out-Null
New-Item -ItemType Directory -Force -Path $CandidateDir | Out-Null
New-Item -ItemType Directory -Force -Path $ReportDir    | Out-Null
New-Item -ItemType Directory -Force -Path $PartsOutDir  | Out-Null

if (-not $SkipGeometry) {
    if (-not (Test-Path $FreeCADCmd)) { Write-Error "FreeCADCmd not found: $FreeCADCmd"; exit 1 }
    if (-not (Test-Path $BlenderExe)) { Write-Error "Blender not found: $BlenderExe"; exit 1 }
    if (-not (Test-Path $FcScript))   { Write-Error "export_fcstd_to_stl.py not found: $FcScript"; exit 1 }
    if (-not (Test-Path $BlScript))   { Write-Error "stl_to_glb.py not found: $BlScript"; exit 1 }
}

$processed = 0; $skipped = 0; $failed = 0

foreach ($c in $Components) {
    if ($Only -and $c.part_id -ne $Only) { continue }
    $fcstd     = Join-Path $RawDir       $c.fcstd
    $stl       = Join-Path $StlDir       "$($c.part_id).stl"
    $rpFc      = Join-Path $ReportDir    "$($c.part_id)_freecad.json"
    $cand      = Join-Path $CandidateDir "$($c.part_id)_candidate.glb"
    $rpBl      = Join-Path $ReportDir    "$($c.part_id)_blender.json"
    $approved  = Join-Path $PartsOutDir  "$($c.part_id)_approved.glb"

    Write-Host ""; Write-Host ("=" * 55); Write-Host "  $($c.part_id)"; Write-Host ("=" * 55)

    if (-not $SkipGeometry) {
        if (-not (Test-Path $fcstd)) { Write-Warning "  [skip] $fcstd not found"; $skipped++; continue }

        # Step 1: FreeCAD -> STL (pass args via env vars - reliable across versions)
        Write-Host "  [1/3] FreeCAD -> STL"
        try {
            $env:FCSTD_INPUT  = $fcstd
            $env:FCSTD_OUTPUT = $stl
            $env:FCSTD_REPORT = $rpFc
            & $FreeCADCmd $FcScript
            $env:FCSTD_INPUT = $null; $env:FCSTD_OUTPUT = $null; $env:FCSTD_REPORT = $null
            if ($LASTEXITCODE -ne 0) { throw "exit $LASTEXITCODE" }
            Write-Host "        ok: $stl"
        } catch {
            $env:FCSTD_INPUT = $null; $env:FCSTD_OUTPUT = $null; $env:FCSTD_REPORT = $null
            Write-Warning "  [FAIL] FreeCAD: $_"; $failed++; continue
        }

        # Step 2: Blender STL -> GLB (pass args via env vars)
        Write-Host "  [2/3] Blender -> GLB"
        try {
            $env:BLENDER_INPUT         = $stl
            $env:BLENDER_OUTPUT        = $cand
            $env:BLENDER_REPORT        = $rpBl
            $env:BLENDER_MATERIAL_NAME = $c.mat
            $env:BLENDER_BASE_COLOR    = $c.color
            $env:BLENDER_ROUGHNESS     = $c.rough
            $env:BLENDER_METALLIC      = $c.metal
            $env:BLENDER_CENTER_MODE   = $c.cx
            & $BlenderExe -b -P $BlScript
            $env:BLENDER_INPUT=$null; $env:BLENDER_OUTPUT=$null; $env:BLENDER_REPORT=$null; $env:BLENDER_MATERIAL_NAME=$null; $env:BLENDER_BASE_COLOR=$null; $env:BLENDER_ROUGHNESS=$null; $env:BLENDER_METALLIC=$null; $env:BLENDER_CENTER_MODE=$null
            if ($LASTEXITCODE -ne 0) { throw "exit $LASTEXITCODE" }
            Write-Host "        ok: $cand"
        } catch {
            $env:BLENDER_INPUT=$null; $env:BLENDER_OUTPUT=$null; $env:BLENDER_REPORT=$null; $env:BLENDER_MATERIAL_NAME=$null; $env:BLENDER_BASE_COLOR=$null; $env:BLENDER_ROUGHNESS=$null; $env:BLENDER_METALLIC=$null; $env:BLENDER_CENTER_MODE=$null
            Write-Warning "  [FAIL] Blender: $_"; $failed++; continue
        }

        # Step 3: gltfpack
        Write-Host "  [3/3] gltfpack -> approved"
        if (Test-Path $GltfpackExe) {
            try {
                & $GltfpackExe -i $cand -o $approved -noq -cc
                if ($LASTEXITCODE -ne 0) { throw "exit $LASTEXITCODE" }
                Write-Host "        ok: $approved"
            } catch { Write-Warning "  gltfpack failed, using candidate"; Copy-Item $cand $approved -Force }
        } else {
            Write-Warning "  gltfpack not found, using candidate"
            Copy-Item $cand $approved -Force
        }
    }

    # Step 4: Compute playPosition from assembly anchor + Blender report
    if (Test-Path $rpBl) {
        Write-Host "  [4/4] compute_play_position -> placements.json"
        try {
            & python3 $PlacementScript `
                --part-id    $c.part_id `
                --blender-report $rpBl `
                --assembly-x $c.asm_x `
                --assembly-y $c.asm_y `
                --assembly-z $c.asm_z `
                --output     $PlacementsJson | Out-Null
            Write-Host "        ok: $PlacementsJson"
        } catch {
            Write-Warning "  [WARN] compute_play_position failed: $_"
        }
    } else {
        Write-Warning "  [skip placement] no blender report at $rpBl"
    }

    $processed++
}

Write-Host ""; Write-Host ("=" * 55)
Write-Host "  Done: $processed processed  $skipped skipped  $failed failed"
Write-Host ("=" * 55)
