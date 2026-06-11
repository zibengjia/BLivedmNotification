import json
import os
import sys

DEFAULT_CONFIG = {
    "room_id": 510,
    "display_index": 0,
    "pipe_name": "BlivedmOverlay",
    "sessdata": "",
    "danmaku": {
        "font_size": 28,
        "speed": 300,
        "opacity": 0.9,
        "track_count": 12,
    },
    "super_chat": {
        "font_size": 40,
        "duration_ms": 15000,
    },
}


def is_frozen():
    """True when running as a PyInstaller frozen exe."""
    return getattr(sys, 'frozen', False)


def get_app_root():
    """
    Resolve the application root directory.
    - Frozen exe: directory containing the exe (where config.json lives)
    - Source mode: project root (parent of py_overlay/)
    """
    if is_frozen():
        return os.path.dirname(sys.executable)
    return os.path.dirname(os.path.dirname(os.path.abspath(__file__)))


def _default_config_path() -> str:
    """
    Resolve the default config.json path.
    - Frozen exe: next to the exe file
    - Source mode: relative to this file (../config.json)
    """
    if is_frozen():
        return os.path.join(os.path.dirname(sys.executable), "config.json")
    return os.path.join(os.path.dirname(__file__), "..", "config.json")


def load_config(path: str = None) -> dict:
    if path is None:
        path = _default_config_path()

    try:
        with open(path, "r", encoding="utf-8") as f:
            return json.load(f)
    except FileNotFoundError:
        print(f"[config] config.json not found at {path}, using defaults")
        return DEFAULT_CONFIG
    except json.JSONDecodeError as e:
        print(f"[config] invalid config.json: {e}, using defaults")
        return DEFAULT_CONFIG
