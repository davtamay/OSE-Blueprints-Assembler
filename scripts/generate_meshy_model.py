from __future__ import annotations

import argparse
import base64
import json
import mimetypes
import os
import sys
import time
import urllib.error
import urllib.request
from pathlib import Path
from typing import Any, Dict

from env_utils import load_env_files


TEXT_ENDPOINT = "https://api.meshy.ai/openapi/v2/text-to-3d"
IMAGE_ENDPOINT = "https://api.meshy.ai/openapi/v1/image-to-3d"
TERMINAL_STATUSES = {"SUCCEEDED", "FAILED", "CANCELED", "EXPIRED"}


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Generate a Meshy model using a prompt or reference image."
    )
    source_group = parser.add_mutually_exclusive_group(required=True)
    source_group.add_argument("--prompt", help="Exact, measurement-backed text prompt.")
    source_group.add_argument("--image", help="Reference image path for image-to-3d.")

    parser.add_argument(
        "--output",
        required=True,
        help="Destination .glb file path for the downloaded model.",
    )
    parser.add_argument(
        "--api-key",
        help="Meshy API key. Defaults to MESHY_API_KEY from .env.local/.env or the environment.",
    )
    parser.add_argument(
        "--art-style",
        default="realistic",
        help="Meshy text-to-3d art_style value. Default: realistic.",
    )
    parser.add_argument(
        "--ai-model",
        help="Optional Meshy text-to-3d ai_model override.",
    )
    parser.add_argument(
        "--model-type",
        default="standard",
        choices=["standard", "lowpoly"],
        help="Meshy image-to-3d model_type. Default: standard.",
    )
    parser.add_argument(
        "--texture-prompt",
        help="Optional refine-stage texture prompt for text-to-3d.",
    )
    parser.add_argument(
        "--poll-interval",
        type=float,
        default=10.0,
        help="Seconds between task status checks. Default: 10.",
    )
    parser.add_argument(
        "--timeout-seconds",
        type=int,
        default=1800,
        help="Maximum time to wait for Meshy to finish. Default: 1800.",
    )
    parser.add_argument(
        "--preview-only",
        action="store_true",
        help="For text-to-3d, stop after preview generation and download the preview GLB.",
    )
    parser.add_argument(
        "--no-texture",
        action="store_true",
        help="Disable texture generation where supported.",
    )
    parser.add_argument(
        "--disable-remesh",
        action="store_true",
        help="Disable Meshy remeshing.",
    )
    parser.add_argument(
        "--disable-pbr",
        action="store_true",
        help="Disable PBR texture generation where supported.",
    )
    return parser.parse_args()


def require_api_key(explicit_key: str | None) -> str:
    load_env_files()
    api_key = explicit_key or os.environ.get("MESHY_API_KEY")
    if api_key:
        return api_key
    raise SystemExit(
        "Missing Meshy API key. Set MESHY_API_KEY in .env.local/.env or pass --api-key."
    )


def api_request(
    method: str,
    url: str,
    api_key: str,
    payload: Dict[str, Any] | None = None,
) -> Dict[str, Any]:
    data = None
    headers = {
        "Authorization": f"Bearer {api_key}",
        "Accept": "application/json",
    }

    if payload is not None:
        data = json.dumps(payload).encode("utf-8")
        headers["Content-Type"] = "application/json"

    request = urllib.request.Request(url, data=data, headers=headers, method=method)

    try:
        with urllib.request.urlopen(request, timeout=120) as response:
            return json.loads(response.read().decode("utf-8"))
    except urllib.error.HTTPError as error:
        body = error.read().decode("utf-8", errors="replace")
        raise RuntimeError(f"Meshy API error {error.code} for {url}: {body}") from error
    except urllib.error.URLError as error:
        raise RuntimeError(f"Meshy request failed for {url}: {error}") from error


def wait_for_task(url: str, api_key: str, poll_interval: float, timeout_seconds: int) -> Dict[str, Any]:
    started = time.time()

    while True:
        task = api_request("GET", url, api_key)
        status = str(task.get("status", "")).upper()
        progress = task.get("progress")
        if progress is None:
            print(f"[meshy] status={status}")
        else:
            print(f"[meshy] status={status} progress={progress}")

        if status in TERMINAL_STATUSES:
            return task

        if time.time() - started > timeout_seconds:
            raise TimeoutError(f"Timed out waiting for Meshy task: {url}")

        time.sleep(max(poll_interval, 1.0))


def image_path_to_data_uri(image_path: Path) -> str:
    mime_type, _ = mimetypes.guess_type(image_path.name)
    if not mime_type:
        mime_type = "application/octet-stream"

    encoded = base64.b64encode(image_path.read_bytes()).decode("ascii")
    return f"data:{mime_type};base64,{encoded}"


def create_text_task(args: argparse.Namespace, api_key: str) -> Dict[str, Any]:
    preview_payload: Dict[str, Any] = {
        "mode": "preview",
        "prompt": args.prompt,
        "art_style": args.art_style,
        "should_remesh": not args.disable_remesh,
        "moderation": False,
    }
    if args.ai_model:
        preview_payload["ai_model"] = args.ai_model

    preview_response = api_request("POST", TEXT_ENDPOINT, api_key, preview_payload)
    preview_id = preview_response["result"]
    print(f"[meshy] created text preview task {preview_id}")

    preview_task = wait_for_task(
        f"{TEXT_ENDPOINT}/{preview_id}",
        api_key,
        args.poll_interval,
        args.timeout_seconds,
    )
    ensure_task_succeeded(preview_task)

    if args.preview_only or args.no_texture:
        return preview_task

    refine_payload: Dict[str, Any] = {
        "mode": "refine",
        "preview_task_id": preview_id,
        "enable_pbr": not args.disable_pbr,
        "moderation": False,
        "remove_lighting": True,
    }
    if args.texture_prompt:
        refine_payload["texture_prompt"] = args.texture_prompt
    if args.ai_model:
        refine_payload["ai_model"] = args.ai_model

    refine_response = api_request("POST", TEXT_ENDPOINT, api_key, refine_payload)
    refine_id = refine_response["result"]
    print(f"[meshy] created text refine task {refine_id}")

    refine_task = wait_for_task(
        f"{TEXT_ENDPOINT}/{refine_id}",
        api_key,
        args.poll_interval,
        args.timeout_seconds,
    )
    ensure_task_succeeded(refine_task)
    return refine_task


def create_image_task(args: argparse.Namespace, api_key: str) -> Dict[str, Any]:
    image_path = Path(args.image).expanduser().resolve()
    if not image_path.is_file():
        raise FileNotFoundError(f"Reference image not found: {image_path}")

    payload: Dict[str, Any] = {
        "image_url": image_path_to_data_uri(image_path),
        "model_type": args.model_type,
        "enable_pbr": not args.disable_pbr,
        "should_remesh": not args.disable_remesh,
        "should_texture": not args.no_texture,
        "save_pre_remeshed_model": True,
    }

    response = api_request("POST", IMAGE_ENDPOINT, api_key, payload)
    task_id = response["result"]
    print(f"[meshy] created image task {task_id}")

    task = wait_for_task(
        f"{IMAGE_ENDPOINT}/{task_id}",
        api_key,
        args.poll_interval,
        args.timeout_seconds,
    )
    ensure_task_succeeded(task)
    return task


def ensure_task_succeeded(task: Dict[str, Any]) -> None:
    status = str(task.get("status", "")).upper()
    if status == "SUCCEEDED":
        return

    message = ""
    task_error = task.get("task_error")
    if isinstance(task_error, dict):
        message = str(task_error.get("message", "")).strip()

    if message:
        raise RuntimeError(f"Meshy task failed with status {status}: {message}")
    raise RuntimeError(f"Meshy task failed with status {status}.")


def download_glb(task: Dict[str, Any], output_path: Path) -> Path:
    model_urls = task.get("model_urls") or {}
    glb_url = model_urls.get("glb") or model_urls.get("pre_remeshed_glb")
    if not glb_url:
        raise RuntimeError("Meshy task succeeded but no GLB URL was returned.")

    output_path.parent.mkdir(parents=True, exist_ok=True)
    with urllib.request.urlopen(glb_url, timeout=300) as response:
        output_path.write_bytes(response.read())

    return output_path


def main() -> int:
    args = parse_args()
    api_key = require_api_key(args.api_key)
    output_path = Path(args.output).expanduser().resolve()

    if output_path.suffix.lower() != ".glb":
        raise SystemExit("--output must point to a .glb file.")

    try:
        task = create_text_task(args, api_key) if args.prompt else create_image_task(args, api_key)
        written = download_glb(task, output_path)
    except Exception as error:
        print(f"[meshy] {error}", file=sys.stderr)
        return 1

    print(f"[meshy] downloaded {written}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
