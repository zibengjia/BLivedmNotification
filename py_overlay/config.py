import json
import os

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


def load_config(path: str = None) -> dict:
    if path is None:
        path = os.path.join(os.path.dirname(__file__), "..", "config.json")

    try:
        with open(path, "r", encoding="utf-8") as f:
            return json.load(f)
    except FileNotFoundError:
        print(f"[config] config.json not found at {path}, using defaults")
        return DEFAULT_CONFIG
    except json.JSONDecodeError as e:
        print(f"[config] invalid config.json: {e}, using defaults")
        return DEFAULT_CONFIG
