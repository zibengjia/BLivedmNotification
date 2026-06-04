# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Commands

```bash
# Setup (uv preferred — lockfile exists)
uv sync

# Or with PDM
pdm install

# Build package
pdm build

# Run sample (requires SESSDATA cookie from bilibili)
python sample.py
```

No test/lint/format commands available — project has no test suite, no linter config, no CI.

## Project Overview

**blivedm** v1.1.6 — Python library for fetching bilibili live danmaku (bullet comments) over WebSocket. Two auth paths:

**BLiveClient** (`blivedm/clients/web.py`): bilibili web API. Uses cookie auth (`SESSDATA`), WBI signing, fetches room config from bilibili APIs.

## Architecture

```
blivedm/
├── __init__.py              # version = '1.1.6', re-exports
├── handlers.py              # HandlerInterface (abstract) + BaseHandler (command dispatch)
├── utils.py                 # reconnect policies, user-agent constant
├── clients/
│   ├── __init__.py          # re-exports
│   ├── ws_base.py           # WebSocketClientBase — core: connect, reconnect, heartbeat, binary protocol parse, Brotli/zlib decompress
│   ├── web.py               # BLiveClient (cookie auth, WBI sign, room init via bilibili API)
│   └── open_live.py         # OpenLiveClient (HMAC auth, game lifecycle)
└── models/
    ├── __init__.py
    ├── web.py               # typed message dataclasses for web protocol (DanmakuMessage, GiftMessage, etc.)
    ├── open_live.py         # typed message dataclasses for open platform protocol
    └── pb.py                # Protobuf definitions (InteractWordV2 via pure-protobuf)
```

## Protocol Flow

1. `WebSocketClientBase.start()` → connects → `init_room()` (subclass) → auth handshake → `_network_coroutine` loop
2. Incoming binary frames: parse 16-byte header → decompress (Brotli=3, zlib=2, none=0) → JSON decode
3. `handle()` on attached handler → `BaseHandler` routes via `_CMD_CALLBACK_DICT` → typed `_on_*` callbacks
4. Auto-reconnect with pluggable policy (`make_constant_retry_policy` / `make_linear_retry_policy`)

## Key Patterns

- **Handler**: Subclass `BaseHandler`, override `_on_*` methods for events you care about. Add custom commands by copying `_CMD_CALLBACK_DICT` and inserting new entries.
- **Client lifecycle**: `start()` → `join()` (blocks until stopped) → `stop()` / `stop_and_close()`. Always call `stop_and_close()` in `finally` blocks.
- **Session**: Optionally pass an external `aiohttp.ClientSession` with cookies set; otherwise the client creates its own.
- **Reconnect**: Call `client.set_reconnect_policy(...)` before `start()` to configure retry behavior.

## Dependencies

- `aiohttp>=3.9,<3.10` — async HTTP/WebSocket
- `Brotli>=1.1,<2.0` — decompress B站's brotli-compressed frames
- `pure-protobuf>=3.1.2,<4.0` — Protobuf decode for InteractWordV2
- `yarl>=1.9.3,<2.0` — URL handling

Python 3.8–3.13 supported, no OS-specific code.