# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Commands

```bash
# C# WinForms overlay — Build
cd overlay-csharp && dotnet build

# C# WinForms overlay — Run (with config UI)
cd overlay-csharp && dotnet run --project Overlay

# C# WinForms overlay — Run overlay-only mode (skip launcher, for debugging)
cd overlay-csharp && dotnet run --project Overlay -- --overlay

# C# WPF overlay — Build (new, replaces WinUI 3)
cd overlay-csharp/WpfOverlay && dotnet build

# C# WPF overlay — Run (with launcher)
cd overlay-csharp/WpfOverlay && dotnet run

# C# WPF overlay — Run overlay-only mode (no launcher, for debugging)
cd overlay-csharp/WpfOverlay && dotnet run -- --overlay

# C# WinUI 3 overlay — Build (WIP, abandoned due to transparency issue)
cd overlay-csharp/WinUIOverlay && dotnet build

# Python — Setup (PDM or uv)
pdm install          # PDM
uv sync              # uv (uv.lock at repo root)

# Python — Run overlay backend directly (for testing without C# frontend)
python py_overlay/main.py --room 27484357

# Python — Run overlay backend via uv
uv run python py_overlay/main.py --room 27484357
```

No test suite, linter config, or CI exists.

## Project Overview

Dual-process bilibili live danmaku overlay for Windows:

```
B站 WebSocket ← blivedm (Python) → [Named Pipe] → PipeClient (C#) → DanmakuEngine → DanmakuRenderer → Direct2D overlay
```

Two processes communicate over a Windows Named Pipe with JSON-line protocol:

1. **Python backend** (`py_overlay/`): blivedm WebSocket client → OverlayHandler → Named Pipe server
2. **C# frontend** (`overlay-csharp/`): Two parallel versions —
   - **WinForms** (stable): `Overlay/` — WinForms launcher (MainForm) + Direct2D transparent overlay (OverlayForm)
   - **WinUI 3** (WIP, replaces WinForms): `WinUIOverlay/` — WinUI 3 launcher (MainWindow) + Win2D transparent overlay (OverlayWindow)

Both read/write `config.json` at repo root (snake_case fields, shared format).

**Config default values** differ by implementation (each falls back independently when file is missing):
- **Python fallback** (`py_overlay/config.py`): font_size=28, speed=300, opacity=0.9, track_count=12
- **C# WinForms** (Config.cs): font_size=21, speed=280, opacity=1.0, track_count=14
- **C# WinUI 3** (Config.cs): font_size=21, speed=300, opacity=1.0, track_count=12
The launcher UI populates from saved `config.json`, not from code defaults.

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
│   ├── __init__.py              # Package marker (enables `from py_overlay import ...`)
│   ├── main.py                  # Entry: starts pipe server + blivedm client
│   ├── handler.py               # OverlayHandler: streams events to pipe
│   └── config.py                # Loads config.json with defaults
├── uv.lock                      # uv lockfile (if using uv instead of pdm)
│
├── overlay-csharp/              # C# overlays (WinForms stable + WPF new)
│   ├── Overlay/                 # WinForms version (stable, Vortice.Direct2D1)
│   │   ├── Program.cs           # Entry: MainForm, or --overlay flag for direct overlay
│   │   ├── MainForm.cs          # Launcher: tabbed settings + start/stop buttons + tray
│   │   ├── OverlayForm.cs       # Transparent fullscreen overlay window (Escape to close)
│   │   ├── Config.cs            # Config POCO: Load/Save JSON, snake_case via JsonPropertyName
│   │   ├── DanmakuEngine.cs     # Track assignment, collision detection, animation update
│   │   ├── DanmakuRenderer.cs   # Direct2D/DirectWrite rendering (Vortice)
│   │   ├── DanmakuItem.cs       # Danmaku/SC display state
│   │   ├── PipeClient.cs        # Named Pipe consumer (reconnect on disconnect)
│   │   └── Overlay.csproj       # net8.0-windows, WinForms, Vortice.Direct2D1
│   │
│   └── WpfOverlay/              # WPF overlay (replaces WinUI 3)
│       ├── WpfOverlay.csproj    # net8.0-windows, UseWPF, UseWindowsForms (NotifyIcon)
│       ├── App.xaml / .cs       # Entry: --overlay → OverlayWindow, else → MainWindow
│       ├── Config.cs            # Config POCO (same schema, +PythonPath)
│       ├── DanmakuEngine.cs     # Track assignment, collision detection (shared)
│       ├── DanmakuItem.cs       # Danmaku/SC display state (shared)
│       ├── PipeClient.cs        # Named Pipe consumer (shared)
│       ├── ProcessManager.cs    # Python backend process lifecycle
│       ├── MainWindow.xaml/.cs  # Launcher: TabControl, status bar, log, NotifyIcon tray
│       ├── OverlayWindow.xaml/.cs # Transparent overlay via AllowsTransparency
│       └── DanmakuRenderer.cs   # DrawingVisual + VisualCollection, FormattedText
│
├── overlay-csharp/WinUIOverlay/ # WinUI 3 overlay (WIP, replaces WinForms)
│   ├── App.xaml{.cs}            # Entry: MainWindow or --overlay → OverlayWindow
│   ├── MainWindow.xaml{.cs}     # Launcher: NavView + status bar + tray (~400 lines)
│   ├── OverlayWindow.xaml{.cs}  # Transparent fullscreen overlay via Win2D (WIP: transparency)
│   ├── Win2DRenderer.cs         # Win2D rendering (CanvasDrawingSession, CanvasTextLayout)
│   ├── DanmakuEngine.cs         # Same track/collision logic as WinForms version
│   ├── DanmakuItem.cs           # Danmaku/SC display state
│   ├── PipeClient.cs            # Named Pipe consumer (identical protocol)
│   ├── Config.cs                # Config POCO (similar to WinForms, with ResolveConfigPath)
│   ├── TransparentBackdrop.cs   # Custom SystemBackdrop → ARGB(0,0,0,0) (fix WIP)
│   ├── Services/
│   │   ├── ConfigService.cs     # Config load/save + UI sync
│   │   └── ProcessManager.cs    # Python backend process lifecycle
│   ├── Pages/
│   │   ├── BasicSettingsPage.xaml{.cs}    # Room/pipe/SESSDATA/display/python
│   │   ├── DanmakuSettingsPage.xaml{.cs}  # Font/speed/opacity/tracks
│   │   ├── SuperChatSettingsPage.xaml{.cs}# Font/duration
│   │   ├── LogPage.xaml{.cs}             # Nav item — log output (1000 line buffer)
│   │   └── AboutPage.xaml{.cs}           # About info
│   └── WinUIOverlay.csproj      # net10.0-windows10.0.26100.0, WinAppSDK 2.1.3, Win2D 1.4.0
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

**Config JSON schema** (`config.json` at repo root, shared by both Python and C#):

```jsonc
{
  "room_id": 1814037294,       // B站直播间ID
  "display_index": 0,           // Monitor index for overlay fullscreen
  "pipe_name": "BlivedmOverlay",// Named Pipe name
  "sessdata": "",               // B站 SESSDATA cookie for auth (optional)
  "danmaku": {
    "font_size": 21,            // Font size in px
    "speed": 280,               // Scroll speed (px/s) — WinForms default 280, WinUI default 300
    "opacity": 1.0,            // 0.0–1.0
    "track_count": 14           // Number of danmaku tracks — WinForms default 14, WinUI default 12
  },
  "super_chat": {
    "font_size": 40,            // SC font size in px
    "duration_ms": 15000       // SC display duration in ms (fades last 2s)
  }
}
```

**Overlay.exe startup** (both WinForms and WinUI):
- Normal mode: Launcher window — config editor + start/stop buttons + system tray
- `--overlay` flag: direct `OverlayForm`/`OverlayWindow` using existing `config.json` (useful for debugging overlay in isolation when backend is already running separately)

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

1. `sys.path.insert(0, ...)` at line 21 adds repo root to path so `blivedm` (vendored at repo root) is importable — the backend runs outside the `blivedm` package
2. Creates Named Pipe server (win32pipe.CreateNamedPipe, blocking ConnectNamedPipe in executor)
3. Waits for C# overlay to connect
4. Creates BLiveClient with optional SESSDATA auth
5. Attaches OverlayHandler → streams events to pipe

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

## Key WinUI 3 Overlay Details (WIP, `WinUIOverlay/`)

WinUI 3 rewrite using WinAppSDK + Win2D instead of WinForms + Vortice. Shares same IPC protocol, danmaku engine logic, and config format.

### Architecture differences from WinForms version
- **Win2D** (`Win2DRenderer.cs`) replaces Vortice.Direct2D1 — `CanvasControl` for rendering, `CanvasTextLayout` for text measurement
- **DispatcherTimer** (60fps) replaces `System.Windows.Forms.Timer` — `Update()` + `CanvasControl.Invalidate()`
- **NavigationView** replaces `TabControl` — `LeftCompact` pane with 4 pages
- **AppWindow** replaces direct WinForms window management — `OverlappedPresenter` to hide title bar/borders
- **Window subclassing** via `SetWindowLongPtr(GWLP_WNDPROC)` for hotkey handling (same approach as WinForms)

### MainWindow.cs (launcher, ~400 lines)
- `NavigationView` with 4 pages: 基本设置, 弹幕设置, 醒目留言, 关于
- Status bar: colored dots for backend/overlay status + log area + control buttons
- Managed services: `ConfigService` (config load/save sync) + `ProcessManager` (Python backend)
- System tray: `Shell_NotifyIcon` (NIM_ADD/DELETE), hide/show via `AppWindow.Hide()/Show()`
- OnClose cleanup: stop backend, close overlay, remove tray icon

### OverlayWindow.xaml.cs (overlay, ~250 lines)
- `WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOPMOST | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW`
- Fullscreen on target monitor: `MonitorFromWindow` + `GetMonitorInfo` + `SetWindowPos`
- `RegisterHotKey(VK_ESCAPE)` — window proc subclassed to intercept `WM_HOTKEY`
- Same lifecycle: `pipe.ConnectAsync()` → `engine.Update()` → `canvas.Invalidate()` at 60fps

### Win2DRenderer.cs
- `CanvasTextFormat` cache keyed by font size
- `MeasureText()`: `CanvasTextLayout` → `layout.LayoutBounds.Width`
- `ColorFromInt()`: same brightness clamp as WinForms version (lum < 0.3 → raised to 0.3)
- Renders normal danmaku first, then SC on top; black shadow offset 2px

### Config.cs / ConfigService.cs
- Config.cs: similar to WinForms but with `ResolveConfigPath()` static method walking up from `AppContext.BaseDirectory`
- ConfigService.cs: wraps config load/save + `ApplyBasicSettings/ApplyDanmakuSettings/ApplySuperChatSettings` for UI binding

### ProcessManager.cs
- `Process` wrapper for Python backend: `py_overlay/main.py --room {room_id}`
- Redirects stdout/stderr, fires `LogMessage`/`StatusChanged` events
- `Kill(entireProcessTree: true)` on stop

### DanmakuEngine.cs (WinUI version)
- Same algorithms as WinForms version but uses a `ScZoneHeight = 50f` constant (SC reserved zone at top)
- Track height = `FontSize + 6px`, tracks start below SC zone
- `HandleMessage(JsonElement)` replaces the separate message-type dispatch from WinForms

### Dependencies

**C# — WinForms (overlay-csharp/Overlay/Overlay.csproj)**:
- `Vortice.Direct2D1` 2.4.2 — Direct2D/DirectWrite .NET bindings
- `System.Text.Json` 8.0.5 — JSON serialization
- Target: `net8.0-windows`, WinForms enabled

**C# — WinUI 3 (overlay-csharp/WinUIOverlay/WinUIOverlay.csproj)**:
- `Microsoft.WindowsAppSDK` 2.1.3 — WinUI 3 framework
- `Microsoft.Windows.SDK.BuildTools` 10.0.28000.1839 — Windows SDK
- `Microsoft.Graphics.Win2D` 1.4.0 — Win2D rendering (replaces Vortice)
- Target: `net10.0-windows10.0.26100.0`, x64, `WindowsPackageType=None` (unpackaged)

**Python (pyproject.toml)**:
- `aiohttp~=3.9.0` — async HTTP/WebSocket
- `Brotli~=1.1.0` — decompress bilibili brotli frames
- `pure-protobuf~=3.1.2` — Protobuf decode (InteractWordV2)
- `pywin32==310` — Windows Named Pipe (CreateNamedPipe, WriteFile)
- `yarl~=1.9.3` — URL handling
- Python 3.8–3.13, PDM build system

## WinUI Overlay Transparency (KNOWN ISSUE — WIP)

WinUI 3 overlay window (`OverlayWindow`) shows opaque white instead of transparent.
The WinForms overlay works with just `WS_EX_LAYERED` + Direct2D `Premultiplied` alpha.
WinUI 3's DirectComposition composition tree paints a white root visual behind CanvasControl.

**Already implemented** (all succeed in debug log, window still white):
- `OverlappedPresenter.SetBorderAndTitleBar(false)`
- `WS_CAPTION`/`WS_THICKFRAME`/`WS_SYSMENU`/`WS_MINIMIZEBOX`/`WS_MAXIMIZEBOX` cleared from `GWL_STYLE`
- `WS_EX_LAYERED` | `WS_EX_TOOLWINDOW` on `GWL_EXSTYLE`
- `SetLayeredWindowAttributes(hwnd, 0, 255, LWA_ALPHA)` — returns True
- `SetWindowCompositionAttribute` `ACCENT_ENABLE_ACRYLICBLURBEHIND` — gradient 0x00000000
- `DwmSetWindowAttribute` `DWMWCP_DONOTROUND` + `DWMWA_COLOR_NONE`
- `WM_ERASEBKGND` returns 1 in window proc
- Custom `TransparentBackdrop` (extends `SystemBackdrop`) with ARGB(0,0,0,0) composition brush

**Likely fix:** The `TransparentBackdrop` composition brush needs to be properly wired to the composition visual. Current implementation creates the brush but may not apply it. Check `DesktopAcrylicBackdrop` source for proper pattern.

**Reference doc provided by user:** Steps include transparent SystemBackdrop → Win32 style cleanup → WS_EX_LAYERED → SetLayeredWindowAttributes → DWM de-round/de-border → WM_ERASEBKGND. Follow order exactly.
