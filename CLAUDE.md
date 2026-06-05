# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Commands

```bash
# C# WPF overlay — Build (primary, active development)
cd overlay-csharp/WpfOverlay && dotnet build

# C# WPF overlay — Run (with launcher)
cd overlay-csharp/WpfOverlay && dotnet run

# C# WPF overlay — Run overlay-only mode (no launcher, for debugging when backend is running separately)
cd overlay-csharp/WpfOverlay && dotnet run -- --overlay

# C# WinForms overlay — Build (legacy stable version)
cd overlay-csharp/Overlay && dotnet build

# C# WinForms overlay — Run (with config UI)
cd overlay-csharp/Overlay && dotnet run

# C# WinForms overlay — Run overlay-only mode
cd overlay-csharp/Overlay && dotnet run -- --overlay

# C# WinUI 3 overlay — Build (WIP, abandoned — transparency unsolved)
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
B站 WebSocket ← blivedm (Python) → [Named Pipe] → PipeClient (C#) → DanmakuEngine → DanmakuRenderer → transparent overlay
```

Two processes communicate over a Windows Named Pipe with JSON-line protocol:

1. **Python backend** (`py_overlay/`): blivedm WebSocket client → OverlayHandler → Named Pipe server
2. **C# frontend** (`overlay-csharp/`): Three versions by evolution —
   - **WPF** (primary, active): `WpfOverlay/` — WPF launcher (MainWindow) + transparent overlay (OverlayWindow) via `AllowsTransparency`
   - **WinForms** (legacy stable): `Overlay/` — WinForms launcher (MainForm) + Direct2D transparent overlay (OverlayForm)
   - **WinUI 3** (WIP, abandoned — transparency unsolved): `WinUIOverlay/`

All read/write `config.json` at repo root (snake_case fields, shared format).

**Config default values** differ by implementation (each falls back independently when file is missing):
- **Python fallback** (`py_overlay/config.py`): font_size=28, speed=300, opacity=0.9, track_count=12
- **C# WPF** (Config.cs): font_size=28, speed=300, opacity=0.9, track_count=12
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
├── overlay-csharp/              # C# overlays (WPF primary, WinForms legacy)
│   ├── WpfOverlay/              # WPF overlay (primary, active development)
│   │   ├── WpfOverlay.csproj    # net8.0-windows, UseWPF, UseWindowsForms (NotifyIcon)
│   │   ├── App.xaml / .cs       # Entry: --overlay → OverlayWindow, else → MainWindow
│   │   ├── Config.cs            # Config POCO (same schema, +PythonPath)
│   │   ├── DanmakuEngine.cs     # Track assignment, collision detection, animation update
│   │   ├── DanmakuItem.cs       # Danmaku/SC display state
│   │   ├── PipeClient.cs        # Named Pipe consumer (reconnect on disconnect)
│   │   ├── ProcessManager.cs    # Python backend process lifecycle (venv/uv auto-detect)
│   │   ├── MainWindow.xaml/.cs  # Launcher: TabControl, status bar, log, NotifyIcon tray
│   │   ├── OverlayWindow.xaml/.cs # Transparent overlay via AllowsTransparency=True
│   │   └── DanmakuRenderer.cs   # DrawingVisual + VisualCollection, FormattedText
│   │
│   ├── Overlay/                 # WinForms version (legacy stable, Vortice.Direct2D1)
│   │   ├── Program.cs           # Entry: MainForm, or --overlay flag for direct overlay
│   │   ├── MainForm.cs          # Launcher: tabbed settings + start/stop buttons + tray
│   │   ├── OverlayForm.cs       # Transparent fullscreen overlay window (Escape to close)
│   │   ├── Config.cs            # Config POCO: Load/Save JSON, snake_case via JsonPropertyName
│   │   ├── DanmakuEngine.cs     # Track assignment, collision detection, animation update
│   │   ├── DanmakuRenderer.cs   # Direct2D/DirectWrite rendering (Vortice)
│   │   ├── DanmakuItem.cs       # Danmaku/SC display state
│   │   ├── PipeClient.cs        # Named Pipe consumer (reconnect on disconnect)
│   │   └── Overlay.csproj       # net8.0-windows, WinForms, Vortice.Direct2D1
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
  "python_path": "",            // Python executable path (empty = auto-detect venv/uv)
  "danmaku": {
    "font_size": 21,            // Font size in px
    "speed": 280,               // Scroll speed (px/s) — WinForms default 280, WinUI default 300
    "opacity": 1.0,            // 0.0–1.0
    "track_count": 14,          // Number of danmaku tracks — WinForms default 14, WinUI default 12
    "font_weight": "Normal",    // Normal/Light/Medium/SemiBold/Bold
    "shadow_enabled": true,     // Enable text shadow
    "shadow_opacity": 0.6,      // Shadow opacity 0.0–1.0
    "shadow_offset": 2.0,       // Shadow offset in px
    "position_priority": "Top", // Track fill order: Top/Center/Bottom
    "density": "Medium"          // Spacing: Low (sparse)/Medium/High (dense)
  },
  "super_chat": {
    "font_size": 40,            // SC font size in px
    "duration_ms": 15000       // SC display duration in ms (fades last 2s)
  }
}
```

**Overlay.exe startup** (all C# versions):
- Normal mode: Launcher window — config editor + start/stop buttons + system tray
- `--overlay` flag: direct OverlayWindow using existing `config.json` (useful for debugging overlay in isolation when backend is already running separately)

**User flow**:
1. Launcher: configure room ID, SESSDATA, danmaku params
2. Click "全部启动" → SaveConfig() → StartBackend() (Python process) → ShowOverlay() → launcher hides to tray
3. Escape in overlay → closes overlay, restores launcher
4. "停止后端" → Kill Python process tree (entireProcessTree: true)
5. Close launcher → kill backend + close overlay + exit

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

## Key WPF Overlay Details (`WpfOverlay/`)

Primary overlay. Replaces WinForms/WinUI. No external rendering libs — pure WPF `DrawingVisual` + `FormattedText`.

### App.xaml.cs (entry point)

- `OnStartup()` checks `--overlay` flag (args or `Environment.CommandLine`)
- Overlay mode → `Config.Load()` → `OverlayWindow` → `Shutdown` on close
- Launcher mode → `MainWindow` (normal UI)
- Crash logging: writes unhandled exceptions to `WpfOverlay_crash.log` (AppDomain, Dispatcher, TaskScheduler handlers)

### OverlayWindow.xaml

```xml
AllowsTransparency="True"
Background="Transparent"
Topmost="True"
ShowInTaskbar="False"
IsHitTestVisible="False"   <!-- click-through, no WS_EX_TRANSPARENT needed -->
```

- `<Canvas>` child for visual rendering
- ResizeMode="NoResize", WindowStyle="None"
- No WS_EX_LAYERED (WPF handles transparency natively)

### OverlayWindow.xaml.cs

- `WindowInteropHelper` to get HWND → `SetWindowLong(GWL_EXSTYLE)` adds `WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE` (belt-and-suspenders with XAML `IsHitTestVisible`)
- Fullscreen on target monitor: `Screen.AllScreens[displayIndex]` → set `Left/Top/Width/Height`
- `RegisterHotKey(VK_ESCAPE)` → `WM_HOTKEY` → close overlay, fire `OverlayClosed`
- Render loop: `CompositionTarget.Rendering` event (WPF's per-frame callback) → `engine.Update()` → `renderer.Sync(engine.GetItems())`

### DanmakuRenderer.cs

- **`DrawingVisual` + `VisualCollection`**: no `OnRender` override, each danmaku is a cached `DrawingVisual` repositioned via `visual.Offset = new Vector(x, y)` (GPU transform, no redraw cost)
- **Object pooling**: recycled `DrawingVisual`s queued in `_pool`, reused via `GetOrCreateVisual()` — reduces GC pressure
- **`FormattedText`** for text measurement and rendering (DirectWrite-backed, accurate)
- Frame pipeline: `Sync(items)` → diff `_itemMap` against new items → add/update/remove visuals in one pass
- Items per frame: shadow (offset 2px, 60% opacity) + main text + optional SC dark background (rect)
- `ColorFromInt()`: same brightness clamp as WinForms (lum < 0.3 → raised to 0.3)

### ProcessManager.cs

- `Start(projectRoot, roomId, pythonCmd)`: runs `py_overlay/main.py --room {room_id}` with stdout/stderr redirect
- **Python auto-detect**: `ResolvePythonCmd()` checks in order:
  1. Configured path from `config.json` (non-empty and not "python")
  2. `.venv/Scripts/python.exe` (Windows venv)
  3. `.venv/bin/python` (Unix venv)
  4. `uv.exe` from `LocalAppData\Microsoft\WindowsApps`
  5. Fallback `"python"`
- `Stop()`: `Kill(entireProcessTree: true)` + Exited handler fires `StatusChanged(false)`
- Output/Error lines → `LogMessage` event (shown in launcher log area)

### MainWindow.xaml.cs (launcher)

- TabControl with 4 tabs: 基本设置 (room/pipe/SESSDATA/display/python path), 弹幕设置 (font/speed/opacity/tracks), 醒目留言 (font/duration), 关于
- Status bar: colored dots for backend + overlay, toggle buttons
- `NotifyIcon` system tray (using `System.Windows.Forms` interop), minimize → tray
- Log buffer: `StringBuilder` capped at 1000 lines
- `OnClosing`: kill backend → close overlay → dispose tray
- Project root resolved from `config.json` location, same as WinForms

### Shared-layer files (same logic across all C# versions)

- **DanmakuEngine.cs**: track assignment (`AssignTrack`), collision detection (rightmost-pixel), SC zone (50px top reserved). `Update()` moves items by `Speed * dt`, fades at screen edges. Thread-safe via `lock`.
- **DanmakuItem.cs**: danmaku/SC display state. `IsExpired()`: SC by `LifetimeMs >= MaxLifetimeMs`, danmaku by `X + TextWidth < -50`.
- **PipeClient.cs**: `NamedPipeClientStream`, JSON-line framing (`\n` delimiter), auto-reconnect every 2s. Events: `OnMessage`, `OnError`, `OnConnected`, `OnDisconnected`.

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

**C# — WPF (primary, overlay-csharp/WpfOverlay/WpfOverlay.csproj)**:
- `WPF-UI` 4.3.0 — WPF UI library (FluentWindow, themed controls, Mica backdrop)
- `System.Text.Json` 8.0.5 — JSON serialization
- Target: `net8.0-windows`, `UseWPF=true`, `UseWindowsForms=true` (for NotifyIcon tray)
- Rendering: pure WPF `DrawingVisual` + `FormattedText` (no external rendering lib)

**C# — WinForms (legacy, overlay-csharp/Overlay/Overlay.csproj)**:
- `Vortice.Direct2D1` 2.4.2 — Direct2D/DirectWrite .NET bindings
- `System.Text.Json` 8.0.5 — JSON serialization
- Target: `net8.0-windows`, WinForms enabled

**C# — WinUI 3 (WIP, overlay-csharp/WinUIOverlay/WinUIOverlay.csproj)**:
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

## Known Issues / TODOs

- **Packaging**: Create installer/package for distribution (currently requires build-from-source)
- **Per-danmaku font weight**: If Python backend sends `font_weight` per message, DanmakuItem needs a new field

Other notes:
- WPF overlay was introduced to fix WinUI transparency (unsolved). WPF `AllowsTransparency=True` works correctly.
- WinForms version is maintained but no longer active development — new features go into WPF first.
- WinUI 3 version is abandoned unless the DirectComposition white-background issue is resolved upstream.
