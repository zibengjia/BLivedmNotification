# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Commands

```bash
cd overlay-csharp/WpfOverlay && dotnet build                # Build WPF (primary)
cd overlay-csharp/WpfOverlay && dotnet run                   # Run with launcher
cd overlay-csharp/WpfOverlay && dotnet run -- --overlay      # Overlay-only mode
cd overlay-csharp/WpfOverlay && dotnet clean && dotnet build # Clean rebuild
cd overlay-csharp/Overlay && dotnet build                    # Build WinForms (legacy)
pdm install  # or: uv sync                                  # Python setup (uv.lock also maintained)
python py_overlay/main.py --room 27484357                    # Run backend standalone
python py_overlay/main.py --room 27484357 --display 1        # Backend targeting monitor 1
python py_overlay/fetch_room.py 27484357                     # Fetch room info (auto-fill name)
```

## Architecture

```
B站 WebSocket ← blivedm (Python) → [Named Pipe] → PipeClient (C#) → DanmakuEngine → DanmakuRenderer → transparent overlay
```

Two processes, JSON-line protocol over Named Pipe.

**Python backend** (`py_overlay/`): blivedm WebSocket client → OverlayHandler → Named Pipe **server** (creates pipe, waits for client)
**C# WPF frontend** (`overlay-csharp/WpfOverlay/`): PipeClient (Named Pipe **client**) → DanmakuEngine (track assign, update positions) → DanmakuRenderer (DrawingVisual+FormattedText) → OverlayWindow (transparent fullscreen)

Two processes started by launcher: `MainWindow` launches Python backend via `ProcessManager`, which creates pipe and waits. Overlay window (same process, separate thread-safe engine) connects as pipe client. Daemon doesn't own overlay — they communicate via IPC.

WinForms (`Overlay/`) = legacy stable. WinUI 3 (`WinUIOverlay/`) = abandoned (transparency unsolved).

`sample.py` = blivedm library demo script (not part of overlay app).

## Config (`config.json` at repo root)

```jsonc
{
  // 当前选中房间 (legacy, now derived from rooms[selected_room_index])
  "room_id": 13233348,
  "display_index": 0,          // Monitor index for overlay
  "pipe_name": "BlivedmOverlay",
  "sessdata": "",              // B站 SESSDATA cookie for authenticated access
  "python_path": "",           // Custom Python path (empty = auto-detect venv/uv)
  "rooms": [                   // Multi-room list
    { "room_id": 27484357, "label": "主播名" }
  ],
  "selected_room_index": 0,
  "danmaku": {
    "font_size": 28, "speed": 300, "opacity": 0.9, "track_count": 12,
    "font_weight": "Normal",           // Normal|Medium|Bold|SemiBold|Light
    "shadow_enabled": true, "shadow_opacity": 0.6, "shadow_offset": 2,
    "position_priority": "Top",        // Top|Center|Bottom — track fill order
    "density": "Medium",               // Low=sparse|Medium|High=dense
    "hover_hide_enabled": false,       // Mouse hover → hide danmaku
    "background_enabled": false,       // Enable danmaku background
    "background_color": "#000000",     // Background color (hex #RRGGBB)
    "background_opacity": 0.3,         // Background opacity 0-1
    "background_radius": 4.0           // Background corner radius (px)
  },
  "super_chat": {
    "font_size": 40, "duration_ms": 15000,
    "alignment": "Left",               // Left|Center|Right — SC text position
    "background_enabled": true,        // Enable SC background
    "background_color": "#000000",     // Background color (hex #RRGGBB)
    "background_opacity": 0.3,         // Background opacity 0-1
    "background_radius": 6.0           // Background corner radius (px)
  }
}
```

Config is resolved by walking up from exe dir. Both Python (`py_overlay/config.py`) and C# (`Config.cs`) load from same `config.json`.
C# saves back via `Config.Save()` — fields like `sessdata` are persisted from launcher UI.

## IPC Protocol (Named Pipe, `\n` delimited JSON)

```json
{"type": "danmaku", "uid": int, "uname": str, "msg": str, "color": int, "font_size": int, "timestamp": int}
{"type": "super_chat", "price": int, "uname": str, "message": str, "color": int}
```

## Key Files

| File | Role |
|------|------|
| `App.xaml.cs` | Entry: `--overlay` → OverlayWindow, else → MainWindow. Crash log hooks (DispatcherUnhandledException, TaskScheduler) |
| `OverlayWindow.xaml/.cs` | Transparent fullscreen, `AllowsTransparency=True`, `SetWindowDisplayAffinity` anti-capture, `GetCursorPos` hover, `CompositionTarget.Rendering` game loop |
| `DanmakuRenderer.cs` | `DrawingVisual`+`VisualCollection`. Opacity via `visual.Opacity` per-frame (not baked into color). Object pooling |
| `DanmakuEngine.cs` | Track assignment (`GetTrackScanOrder` density-aware), SC zone, `CheckHover()`. Thread-safe via `lock` |
| `DanmakuItem.cs` | Display state: X/Y/TextWidth/Opacity/IsHovered/IsSC/LifetimeMs |
| `PipeClient.cs` | `NamedPipeClientStream`, auto-reconnect 2s, `\n` framed JSON parsing, async continuous read loop |
| `ProcessManager.cs` | Python lifecycle, venv/uv auto-detect, generation counter race fix (Interlocked.CompareExchange guard) |
| `Config.cs` | `RoomEntry` class, `Rooms` list, auto-migration (legacy room_id → rooms[0]), walks dir tree to find config.json |
| `MainWindow.xaml/.cs` | 6-tab launcher (基本设置/房间/弹幕/醒目留言/日志/关于), room CRUD, tray icon, WPF-UI resources only in MainWindow |
| `py_overlay/handler.py` | `OverlayHandler(_on_danmaku, _on_super_chat)` — blivedm events → JSON pipe writes |
| `py_overlay/main.py` | `PipeWriter` + Named Pipe server, blivedm client lifecycle, pipe watchdog reconnects |
| `py_overlay/fetch_room.py` | Standalone script: fetch room name/title/status via API, used by launcher auto-fill |
| `config.json` | Shared config (repo root), loaded by both Python and C# |

## Key WPF Details

- **WPF-UI 4.3.0** (lepoco): Resource dictionaries in `MainWindow.Resources` **only** — NOT at Application level, to avoid leaking styles into `OverlayWindow`
- Type aliases needed due to WPF-UI bringing `System.Drawing`: `using Color = System.Windows.Media.Color; Brush Brushes Point FontFamily Application` — same pattern for all
- `ControlAppearance` valid values: Primary, Secondary, Info, Dark, Light, Danger, Success, Caution, Transparent
- `SymbolIcon` uses markup extension: `Icon="{ui:SymbolIcon Play24}"`
- **Anti-capture**: `SetWindowDisplayAffinity(hwnd, WDA_EXCLUDEFROMCAPTURE)` — prevents overlay from appearing in screenshots/screen recordings
- **Click-through**: `WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE` window styles so mouse events pass through to windows beneath
- **Render loop**: `CompositionTarget.Rendering` event (fires per VSYNC) drives `engine.Update()` → `renderer.Sync()` — no separate timer
- **Hover detection**: `GetCursorPos(user32.dll P/Invoke)` works because the window is transparent (NOT `IsHitTestVisible="True"` + hit-test region trick — actual Win32 click-through)

## Python Details

- `py_overlay/main.py`: `sys.path.insert(0, ...)` adds repo root so `blivedm` (vendored) is importable. `init_room()` fetches `room_uname`/`title`/`live_status` etc alongside room_id/uid. Creates Named Pipe **server** (opposite of typical client — Python is server, C# is client)
- `py_overlay/handler.py`: `_send()` = `json.dumps + "\n"` → pipe. Overrides `_on_danmaku`, `_on_super_chat`. `_on_heartbeat` and `_on_gift` stubs exist but are unused
- `py_overlay/config.py`: `load_config()` reads `../config.json` relative to py_overlay/
- `py_overlay/fetch_room.py`: Standalone script. Uses `blivedm.BLiveClient.init_room()` + fallback `live_user/v1/Master/info` API. Returns JSON to stdout — called by launcher when user clicks "获取主播名"
- **Pipe server vs client**: Python creates the Named Pipe with `CreateNamedPipe` + `ConnectNamedPipe` (server), C# connects with `NamedPipeClientStream` (client). Python waits for C# to connect before starting blivedm. On C# crash/restart, Python's `pipe_watchdog` re-creates the pipe every 1s
- **PIPE_NOWAIT note**: Intentionally using default `PIPE_WAIT` mode to avoid `ERROR_NO_DATA` race on connect (see comment in `main.py:113`)

## Vendored blivedm (`blivedm/`)

Third-party library [xfgryujk/blivedm](https://github.com/xfgryujk/blivedm), vendored at repo root. Uses `aiohttp` for WebSocket. Supports `SESSDATA` auth. `pyproject.toml` is the library's build config (pdm-backend). Not to be confused with application code in `py_overlay/`.

## Known Issues

- **WinUI overlay transparency**: Unresolved white background (DirectComposition). Abandoned.
- **Thread safety**: Engine+Renderer called from both `CompositionTarget.Rendering` (UI thread) and pipe callback (background thread dispatched to UI). `lock` on engine items, renderer accessed only on UI thread.
  - `MeasureTextWidth` delegate calls `DanmakuRenderer.MeasureText()` → `FormattedText`, which is **not** thread-safe. All pipe messages must dispatch to UI thread via `Dispatcher.InvokeAsync()` before calling `engine.HandleMessage()`.
- **Per-danmaku font weight**: Not supported — would need IPC protocol change + DanmakuItem field.
- **IPC protocol lacks type discriminator for unknown messages**: `HandleMessage` silently ignores types it doesn't recognize — add logging if debugging missing messages.
- **Density × TrackCount interaction**: Low density with high track count still fills top-to-bottom; it controls spacing between items on same track, not total tracks used.

## Debugging

- **Crash logs**: Written to `WpfOverlay_crash.log` in CWD (App.UnhandledException, DispatcherUnhandledException, TaskScheduler.UnobservedTaskException)
- **Overlay debug log**: `overlay_debug.log` in CWD — verbose trace of overlay init steps and pipe events. Toggle by commenting out `LogDebug()` in `OverlayWindow.xaml.cs`
- **Python backend logs**: Written to stdout/stderr, captured by `ProcessManager` and displayed in launcher Log tab
- **WinUIOverlay crash log**: `WinUIOverlay_crash.log` (from abandoned WinUI branch)
- **Overlay-only mode**: `dotnet run -- --overlay` starts overlay without launcher — useful when Python backend is started manually
- **Test room**: Room `27484357` (Nile Red — often streams) or `4350043` (一米的坤儿) for testing without SESSDATA
