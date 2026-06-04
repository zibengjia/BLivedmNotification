# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Commands

```bash
# Setup (PDM primary — pyproject.toml uses pdm-backend)
pdm install

# Build package
pdm build

# Run sample (requires SESSDATA cookie from bilibili)
python sample.py
```

No test suite, linter config, or CI exists.

## Project Overview

**blivedm** v1.1.6 — Python library for fetching bilibili live danmaku over WebSocket.

- **BLiveClient** (`blivedm/clients/web.py`): cookie-based auth (`SESSDATA`), WBI signing, room init via bilibili APIs
- **OpenLiveClient** (`blivedm/clients/open_live.py`): HMAC auth for B站开放平台

## Architecture

```
blivedm/
├── __init__.py              # version = '1.1.6', re-exports
├── handlers.py              # HandlerInterface (abstract) + BaseHandler (command dispatch)
├── utils.py                 # reconnect policies, USER_AGENT constant
├── clients/
│   ├── __init__.py
│   ├── ws_base.py           # WebSocketClientBase — core: connect, reconnect, heartbeat, binary protocol parse, Brotli/zlib decompress
│   ├── web.py               # BLiveClient (cookie auth, WBI sign, room init via bilibili API)
│   └── open_live.py         # OpenLiveClient (HMAC auth, game lifecycle)
└── models/
    ├── __init__.py
    ├── web.py               # typed message dataclasses (DanmakuMessage, GiftMessage, etc.)
    ├── open_live.py         # typed message dataclasses for open platform protocol
    └── pb.py                # pure-protobuf definitions for InteractWordV2
```

## Protocol Flow

1. `start()` → `_network_coroutine` loop → `init_room()` (subclass) → auth handshake → message loop
2. Binary frame: 16-byte `HeaderTuple` (pack_len, raw_header_size, ver, operation, seq_id) → decompress (Brotli/ver=3, zlib/ver=2, none/ver=0) → JSON decode
3. `handle()` on attached handler → `BaseHandler` routes via `_CMD_CALLBACK_DICT` → typed `_on_*` callbacks
4. Auto-reconnect with pluggable policy

## Key Architectural Details (Not Obvious From File Listing)

### Protocol Quirks
- **Heartbeat Reply**: Server heartbeat reply uses `Operation.HEARTBEAT_REPLY`. First 4 bytes of body = popularity (big-endian int). `pack_len` does NOT include the client's original heartbeat body (potential B站 bug documented inline). The library synthesizes a fake `_HEARTBEAT` command to route through normal handler dispatch.
- **Multi-packet frames**: `SEND_MSG_REPLY` and `AUTH_REPLY` operations can batch multiple packets. Parser loops over `data[offset: offset+header.pack_len]` incrementally until full consumed.
- **Brotli decompression**: Runs in `run_in_executor` to avoid blocking the network thread (`asyncio.get_running_loop().run_in_executor(None, brotli.decompress, body)`).
- **Heartbeat timer**: Uses `loop.call_later()` for recurring heartbeat, not `asyncio.sleep` loop — avoids drift accumulation.

### Client Init Flow (BLiveClient)
`init_room()` performs 4 discovery calls with graceful degradation:
1. `_init_uid()` → GET `api.bilibili.com/x/web-interface/nav`. Falls back to uid=0 on failure.
2. `_init_buvid()` → GET `bilibili.com/`. If buvid3 cookie missing, tries to acquire one.
3. `_init_room_id_and_owner()` → GET `api.live.bilibili.com/room/v1/Room/get_info`. Falls back to short ID and owner_uid=0.
4. `_init_host_server()` → GET `api.live.bilibili.com/xlive/web-room/v1/index/getDanmuInfo` with WBI-signed params. Falls back to `['broadcastlv.chat.bilibili.com']` defaults.

### WBI Signing
- Per-session `_WbiSigner` stored in `_session_to_wbi_signer` (`WeakKeyDictionary` — auto-cleaned when session GC'd).
- Key refresh TTL: 11h 59m 30s (slightly under 12h to avoid edge cases).
- Signing: fetches img_key + sub_key from wbi_img endpoint, shuffles via index table, then MD5(urlencode(sorted(params)) + key).
- On -352 error (bad signature), resets cached key to force re-fetch.

### Reconnect Policy
- `set_reconnect_policy(get_interval)` — callable receives `(retry_count, total_retry_count)`, returns seconds.
- `retry_count` resets to 0 after 1+ successful messages. `total_retry_count` never resets.
- BLiveClient forces `init_room()` re-run every N reconnects (`max(3, len(host_server_list))`).

### Message Models
- **DanmakuMessage**: Parsed from JSON array (`info[0..16]`), not dict keys. Positional array indices map to fields — brittle to B站 schema changes.
- **InteractWordV2**: Body is base64-encoded protobuf (`data['pb']`). Decoded via `pure-protobuf` handwritten dataclass in `pb.py`.
- **`from_command` convention**: Every model has `@classmethod from_command(data: dict)` — web models live in `models/web.py`, open platform models in `models/open_live.py`.

### Handler System
- `_make_msg_callback(method_name, message_cls)` dynamically binds `_on_*` methods to typed message classes.
- `BaseHandler._CMD_CALLBACK_DICT`: dict mapping B站 cmd strings → callbacks. To add a new command, subclass must copy the dict and insert: `_CMD_CALLBACK_DICT = BaseHandler._CMD_CALLBACK_DICT.copy()`.
- **Unknown cmd dedup**: `logged_unknown_cmds` set at module level — each unknown cmd logged only once per process lifetime. Extend with new B站 commands freely.
- **Handler runs synchronously** in the network coroutine. `handle()` docstring explicitly warns: CPU-heavy work → thread pool, IO-heavy → `create_task`. Blocking the handler blocks the entire WebSocket receive loop.

### Client Lifecycle
```
start() → join() (blocks until stop) → stop_and_close()
```
Always call `stop_and_close()` in `finally` blocks. `stop()` cancels the network future; `join()` awaits it (wrapped in `asyncio.shield`); `close()` releases resources. Multi-client patterns use `asyncio.gather(*[client.join() for client in clients])` and `asyncio.gather(*[client.stop_and_close() for client in clients])`.

## Dependencies
- `aiohttp~=3.9.0` — async HTTP/WebSocket
- `Brotli~=1.1.0` — decompress bilibili brotli frames
- `pure-protobuf~=3.1.2` — Protobuf decode for InteractWordV2
- `yarl~=1.9.3` — URL handling

Python 3.8–3.13, no OS-specific code. MIT license.
