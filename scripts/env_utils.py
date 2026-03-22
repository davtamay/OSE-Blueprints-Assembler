from __future__ import annotations

import os
from pathlib import Path
from typing import Iterable, List


def find_project_root(start: str | Path | None = None) -> Path:
    current = Path(start or __file__).resolve()
    if current.is_file():
        current = current.parent

    for candidate in (current, *current.parents):
        if (candidate / "Assets").exists() and (candidate / ".gitignore").exists():
            return candidate

    return current


def load_env_files(
    project_root: str | Path | None = None,
    filenames: Iterable[str] = (".env.local", ".env"),
    override: bool = False,
) -> List[Path]:
    root = find_project_root(project_root)
    loaded: List[Path] = []

    for name in filenames:
        path = root / name
        if not path.is_file():
            continue

        for raw_line in path.read_text(encoding="utf-8").splitlines():
            line = raw_line.strip()
            if not line or line.startswith("#"):
                continue
            if line.startswith("export "):
                line = line[7:].strip()
            if "=" not in line:
                continue

            key, value = line.split("=", 1)
            key = key.strip()
            value = value.strip()
            if not key:
                continue

            if len(value) >= 2 and value[0] == value[-1] and value[0] in ("'", '"'):
                value = value[1:-1]

            if override or key not in os.environ:
                os.environ[key] = value

        loaded.append(path)

    return loaded
