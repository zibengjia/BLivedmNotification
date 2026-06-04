# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Commands

```bash
# C# overlay — Build
cd overlay-csharp && dotnet build

# C# overlay — Run (with config UI)
cd overlay-csharp && dotnet run --project Overlay

# C# overlay — Run overlay-only mode (skip launcher, for debugging)
cd overlay-csharp && dotnet run --project Overlay -- --overlay

# Python — Setup (PDM)
pdm install

# Python — Run overlay backend directly (for testing without C# frontend)
python py_overlay/main.py --room 27484357
```

No test suite, linter config, or CI exists.

## Project Overview

Dual-process bilibili live danmaku overlay for Windows:

```
B站 WebSocket ← blivedm (Python) → [Named Pipe] → PipeClient (C#) → DanmakuEngine → DanmakuRenderer → Direct2D overlay
```

Two processes communicate over a Windows Named Pipe with JSON-line protocol:

1. **Python backend** (`py_overlay/`): blivedm WebSocket client → OverlayHandler → Named Pipe server
2. **C# frontend** (`overlay-csharp/`): WinForms launcher (MainForm) + Direct2D transparent overlay (OverlayForm)

Both read/write `config.json` at repo root (snake_case fields, shared format).

## Architecture

```
BLivedmNotification/
├── config.json                  # Shared config (both C# and Python read/write this)
├── blivedm/                     # blivedm v1.1.6 Python library (upstream, vendored)
│   ├── __init__.py              # version = '1.1.6', re-exports
│   ├── handlers.py              # HandlerInterface + BaseHandler (command dispatch)
│   ├── utils.py                 # reconnect policies, USER_AGENT
│   ├── clients/
│   │   ├── web.py               # BLiveClient (cookie auth, WBI sign, room init)
│   │   └── ws_base.py           # WebSocketClientBase (core protocol)
│   └── models/
│       ├── web.py               # typed message dataclasses
│       └── pb.py                # pure-protobuf (InteractWordV2)
│
├── py_overlay/                  # Python overlay bridge
│   ├── main.py                  # Entry: starts pipe server + blivedm client
│   ├── handler.py               # OverlayHandler: streams events to pipe
│   └── config.py                # Loads config.json with defaults
│
├── overlay-csharp/              # C# WinForms + Direct2D overlay
│   └── Overlay/
│       ├── Program.cs           # Entry: MainForm, or --overlay flag for direct overlay
│       ├── MainForm.cs          # Launcher: tabbed settings + start/stop buttons + tray
│       ├── OverlayForm.cs       # Transparent fullscreen overlay window (Escape to close)
│       ├── Config.cs            # Config POCO: Load/Save JSON, snake_case via JsonPropertyName
│       ├── DanmakuEngine.cs     # Track assignment, collision detection, animation update
│       ├── DanmakuRenderer.cs   # Direct2D/DirectWrite rendering (Vortice)
│       ├── DanmakuItem.cs       # Danmaku/SC display state
│       ├── PipeClient.cs        # Named Pipe consumer (reconnect on disconnect)
│       └── Overlay.csproj       # net8.0-windows, WinForms, Vortice.Direct2D1
│
├── pyproject.toml               # PDM build config (blivedm library)
└── sample.py                    # blivedm library usage example
```

### Telemetry / Protocol flows

**IPC protocol** (Named Pipe, JSON line format, `\n` delimited):

```python
# Danmaku message (python handler.py → C# DanmakuEngine)
{"type": "danmaku", "uid": int, "uname": str, "msg": str, "color": int, "font_size": int, "timestamp": int}
# Super Chat message
{"type": "super_chat", "price": int, "uname": str, "message": str, "color": int}
```

**Overlay.exe startup**:
- Normal mode: `MainForm` — config editor + start/stop buttons + system tray
- `--overlay` flag: direct `OverlayForm` using existing `config.json`

**User flow**:
1. MainForm launcher: configure room ID, SESSDATA, danmaku params
2. Click "全部启动" → SaveConfig() → StartBackend() (Python process) → ShowOverlay() (OverlayForm) → MainForm hides to tray
3. Escape in overlay → closes OverlayForm, restores MainForm
4. "停止后端" → Kill Python process tree
5. Close MainForm → kill backend + close overlay + exit

## Key C# Overlay Details

### MainForm.cs (launcher, ~740 lines)

- TabControl with 4 tabs: 基本设置 (room/pipe/SESSDATA/display/python path), 弹幕设置 (font/speed/opacity/tracks), 醒目留言 (font/duration), 关于
- Status panel: ● green/red indicators for backend + overlay, toggle buttons
- Process management: `System.Diagnostics.Process` for Python, `Kill(entireProcessTree: true)`
- Overlay management: `OverlayForm` created/destroyed per show — always fresh with latest config
- Window minimize → system tray (NotifyIcon with context menu)
- `OnFormClosing` cleans up: kill backend, close overlay, dispose tray
- Project root derived from `config.json` location (`_configPath`), not `Application.ExecutablePath`

### DanmakuEngine.cs

- `AssignTrack()`: top→bottom track scan. Track height = `FontSize + 6px`. Picks first track where rightmost existing text edge leaves `entryWidth × 1.2` free space. Fallback: track with most free space.
- `MeasureTextWidth` delegate: set by OverlayForm to DirectWrite measurement (accurate). Fallback = `text.Length * fontSize * 0.55`
- `Update()`: moves danmaku leftward by `Speed * dt`, fades at edges; SC items fade in last 2s of lifetime
- Thread safety: `lock (_lock)` on all item access (engine called from timer thread, pipe from pipe thread)

### DanmakuRenderer.cs

- Vortice.Direct2D1 + DirectWrite: creates `ID2D1HwndRenderTarget` with `Premultiplied` alpha, transparent clear
- Text: Microsoft YaHei UI, shadow (offset 2px), SC gets semi-transparent dark background
- `ColorFromInt()`: minimum brightness clamp (lum < 0.3 → raised to 0.3)
- `MeasureText()`: `CreateTextLayout` → `layout.Metrics.Width`

### OverlayForm.cs

- Extended style: `WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOPMOST | WS_EX_NOACTIVATE`
- 60fps timer: `Update()` + `Invalidate()`
- Suppressed `WM_ERASEBKGND` + empty `OnPaintBackground` to prevent flicker
- Not clickable (WS_EX_TRANSPARENT) — mouse passes through to windows below

### PipeClient.cs

- `NamedPipeClientStream`, auto-reconnect every 2s on disconnect
- Framing: `\n`-delimited JSON lines, buffers partial lines in `StringBuilder`
- Events: `OnMessage(JsonElement)`, `OnError(string)`, `OnConnected`, `OnDisconnected`

### Config.cs

- `[JsonPropertyName("snake_case")]` on all properties — shared format with Python-side `config.json`
- `ResolveConfigPath()`: walks up from exe dir looking for `config.json`
- `ConfigPath` property tracks where it was loaded from (used for Save + project root resolution)

## Key Python Overlay Details

### main.py

1. Creates Named Pipe server (win32pipe.CreateNamedPipe, blocking ConnectNamedPipe in executor)
2. Waits for C# overlay to connect
3. Creates BLiveClient with optional SESSDATA auth
4. Attaches OverlayHandler → streams events to pipe

### handler.py

- Subclasses `blivedm.BaseHandler`, overrides `_on_danmaku` and `_on_super_chat`
- `_send()`: `json.dumps(ensure_ascii=False) + "\n"` → pipe
- Gift/guard/heartbeat events: ignored (no-op handlers)

### config.py

- `DEFAULT_CONFIG` dict matches C# Config.cs defaults exactly
- `load_config()` reads `../config.json` relative to `py_overlay/` directory

## blivedm Protocol Notes (Python library)

### Protocol Quirks
- **Heartbeat Reply**: `Operation.HEARTBEAT_REPLY`. First 4 bytes = popularity (big-endian). `pack_len` does NOT include client's original heartbeat body (B站 bug). Library synthesizes fake `_HEARTBEAT` command.
- **Multi-packet frames**: `SEND_MSG_REPLY`/`AUTH_REPLY` batch multiple packets. Parser loops `data[offset: offset+header.pack_len]`.
- **Brotli decompression**: `run_in_executor` to avoid blocking.
- **Heartbeat timer**: `loop.call_later()`, not `asyncio.sleep` — avoids drift.

### Client Init (BLiveClient.init_room)
4 discovery calls with graceful degradation:
1. `_init_uid()` → `/x/web-interface/nav`, fallback uid=0
2. `_init_buvid()` → `bilibili.com/`, acquire buvid3 cookie
3. `_init_room_id_and_owner()` → `/room/v1/Room/get_info`, fallback short ID + owner_uid=0
4. `_init_host_server()` → `/xlive/web-room/v1/index/getDanmuInfo` (WBI-signed), fallback `broadcastlv.chat.bilibili.com`

### WBI Signing
- Per-session `_WbiSigner` in `WeakKeyDictionary` (auto-GC'd)
- Key refresh: 11h 59m 30s TTL
- `-352` error → reset cached key
- Algorithm: img_key + sub_key → shuffle via index table → MD5(urlencode(sorted(params)) + key)

### Handler System
- `BaseHandler._CMD_CALLBACK_DICT`: B站 cmd string → callback. Subclass must copy: `_CMD_CALLBACK_DICT = BaseHandler._CMD_CALLBACK_DICT.copy()`
- Unknown cmd dedup: `logged_unknown_cmds` set, each unknown cmd logged once per process lifetime
- Handler runs synchronously in network coroutine — blocking it blocks the entire WebSocket receive loop. CPU-heavy work → thread pool, IO-heavy → `create_task`

### Client Lifecycle
```
start() → join() (blocks until stop) → stop_and_close()
```
Always call `stop_and_close()` in `finally`. Multi-client: `asyncio.gather(*[client.join() for client in clients])`.

### Message Models
- `DanmakuMessage.from_command()`: parses JSON array `info[0..16]`, not dict keys — brittle to schema changes
- `InteractWordV2`: base64 protobuf in `data['pb']`, decoded via `pure-protobuf` in `pb.py`
- All models have `@classmethod from_command(data: dict)`

## Dependencies

**C# (overlay-csharp/Overlay/Overlay.csproj)**:
- `Vortice.Direct2D1` 2.4.2 — Direct2D/DirectWrite .NET bindings
- `System.Text.Json` 8.0.5 — JSON serialization
- Target: `net8.0-windows`, WinForms enabled

**Python (pyproject.toml)**:
- `aiohttp~=3.9.0` — async HTTP/WebSocket
- `Brotli~=1.1.0` — decompress bilibili brotli frames
- `pure-protobuf~=3.1.2` — Protobuf decode (InteractWordV2)
- `pywin32==310` — Windows Named Pipe (CreateNamedPipe, WriteFile)
- `yarl~=1.9.3` — URL handling
- Python 3.8–3.13, PDM build system
