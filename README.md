# BLivedmNotification

[![License](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet)](https://dotnet.microsoft.com/)
[![Python](https://img.shields.io/badge/Python-3.8+-3776AB?logo=python)](https://www.python.org/)
[![Platform](https://img.shields.io/badge/platform-Windows-lightgrey)]()

一个 B 站直播弹幕桌面叠加层工具。弹幕以透明 Overlay 的形式直接漂浮在屏幕上，支持多房间管理、丰富的弹幕自定义选项，适合直播观看与录屏场景。

## 特性

- **透明桌面叠加层** — 弹幕直接渲染在屏幕最上层，透明背景、鼠标穿透，不影响日常操作
- **多房间管理** — 同时管理多个直播间，备注自动获取主播名，独立 Tab 切换
- **弹幕高度自定义** — 字体大小 / 速度 / 透明度 / 轨道数 / 字重 / 阴影 / 密度 / 位置优先级，全部可调
- **鼠标悬停隐藏** — 光标经过弹幕区域时自动淡出，避免遮挡
- **防截屏捕获** — 基于 `SetWindowDisplayAffinity`，Overlay 不会出现在截图和录屏中（可选开启）
- **WPF-UI 启动器** — 基于 [WPF-UI 4.3.0](https://github.com/lepoco/wpfui) 的现代 Fluent 风格管理界面
- **醒目留言 (Super Chat)** — 独立样式高亮展示 SC 消息

## 架构

```
┌─────────────┐   WebSocket    ┌──────────────────┐   Named Pipe    ┌──────────────────────────┐
│  B站直播间   │ ──────────────▶│  Python Backend   │ ──────────────▶│  C# WPF Overlay (前端)   │
│             │   blivedm      │  py_overlay/      │   JSON Lines    │  DanmakuEngine           │
└─────────────┘                └──────────────────┘                 │  DanmakuRenderer         │
                                                                    │  OverlayWindow (透明全屏) │
                                                                    └──────────────────────────┘
```

双进程架构，通过 Named Pipe 进行 IPC 通信：

| 进程 | 技术栈 | 职责 |
|------|--------|------|
| **Python 后端** | Python 3.8+ / aiohttp / blivedm | 连接 B 站 WebSocket，解析弹幕协议，通过管道推送消息 |
| **C# 前端** | .NET 8 / WPF / WPF-UI | 启动器 UI + 透明 Overlay 渲染，管道客户端接收并渲染弹幕 |

## 环境要求

- **Windows 10/11**
- **[.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)**
- **Python 3.8+**（推荐使用 [uv](https://github.com/astral-sh/uv) 管理依赖）

## 快速开始

### 1. 克隆项目

```bash
git clone https://github.com/your-username/BLivedmNotification.git
cd BLivedmNotification
```

### 2. 安装 Python 依赖

```bash
uv sync
# 或
pdm install
```

### 3. 配置

编辑项目根目录的 `config.json`：

```jsonc
{
  "room_id": 0,                    // 已弃用，由 rooms 数组管理
  "display_index": 0,              // Overlay 显示在哪个显示器（多屏场景）
  "pipe_name": "BlivedmOverlay",   // 管道名称，保持默认即可
  "sessdata": "",                  // B 站 SESSDATA cookie（可选，用于获取完整弹幕权限）
  "python_path": "",               // 自定义 Python 路径（留空则自动检测 venv/uv）
  "rooms": [                       // 直播间列表
    { "room_id": 27484357, "label": "主播名" }
  ],
  "selected_room_index": 0,
  "danmaku": {
    "font_family": "Microsoft YaHei UI",  // 弹幕字体（需系统已安装）
    "font_size": 28,               // 弹幕字号
    "speed": 300,                  // 弹幕速度（像素/秒）
    "opacity": 0.9,                // 弹幕不透明度
    "track_count": 12,             // 轨道数
    "font_weight": "Normal",       // 字重：Normal | Medium | Bold | SemiBold | Light
    "shadow_enabled": true,        // 是否启用阴影
    "shadow_opacity": 0.6,
    "shadow_offset": 2,
    "position_priority": "Top",    // 轨道填充方向：Top | Center | Bottom
    "density": "Medium",           // 弹幕密度：Low | Medium | High
    "hover_hide_enabled": false    // 鼠标悬停时隐藏弹幕
  },
  "super_chat": {
    "font_size": 40,               // SC 字号
    "duration_ms": 15000           // SC 显示时长（毫秒）
  }
}
```

> [!TIP]
> 大部分配置都可以在启动器的图形界面中调整，无需手动编辑 JSON。

### 4. 构建 & 运行

```bash
# 构建
cd overlay-csharp/WpfOverlay
dotnet build

# 启动（带启动器 UI）
dotnet run

# 仅启动 Overlay（需手动启动 Python 后端）
dotnet run -- --overlay
```

也可以单独启动 Python 后端进行调试：

```bash
python py_overlay/main.py --room 27484357
python py_overlay/main.py --room 27484357 --display 1    # 指定显示器
python py_overlay/fetch_room.py 27484357                  # 获取直播间信息
```

## 项目结构

```
BLivedmNotification/
├── blivedm/                    # blivedm 库（vendored，来自 xfgryujk/blivedm）
├── overlay-csharp/
│   ├── WpfOverlay/             # WPF 主项目（启动器 + Overlay）
│   │   ├── App.xaml.cs         # 入口，区分启动器 / Overlay 模式
│   │   ├── MainWindow.xaml/.cs # 启动器 UI（6 标签页）
│   │   ├── OverlayWindow.xaml/.cs  # 透明全屏叠加层
│   │   ├── DanmakuEngine.cs    # 弹幕引擎（轨道分配、碰撞、线程安全）
│   │   ├── DanmakuRenderer.cs  # 弹幕渲染器（DrawingVisual + 对象池）
│   │   ├── DanmakuItem.cs      # 弹幕数据模型
│   │   ├── PipeClient.cs       # Named Pipe 客户端（自动重连）
│   │   ├── ProcessManager.cs   # Python 后端进程管理
│   │   └── Config.cs           # 配置读写
│   ├── Overlay/                # WinForms 版本（legacy，已弃用）
│   └── WinUIOverlay/           # WinUI 3 版本（abandoned，透明度未解决）
├── py_overlay/
│   ├── main.py                 # Python 后端入口（Pipe Server + blivedm 客户端）
│   ├── handler.py              # 弹幕消息处理器
│   ├── config.py               # 配置加载
│   └── fetch_room.py           # 直播间信息查询脚本
├── config.json                 # 共享配置文件
└── sample.py                   # blivedm 示例脚本
```

## IPC 协议

双进程通过 Named Pipe 传输 `\n` 分隔的 JSON 消息：

```json
{"type": "danmaku", "uid": 12345, "uname": "用户名", "msg": "弹幕内容", "color": 16777215, "font_size": 25, "timestamp": 1700000000}
{"type": "super_chat", "price": 30, "uname": "用户名", "message": "SC 内容", "color": 16777215}
```

## 技术栈

| 组件 | 技术 |
|------|------|
| 弹幕抓取 | [blivedm](https://github.com/xfgryujk/blivedm) (aiohttp WebSocket) |
| 后端 | Python 3.8+ / Named Pipe Server |
| 前端框架 | .NET 8 / WPF |
| UI 组件库 | [WPF-UI 4.3.0](https://github.com/lepoco/wpfui) (Fluent Design) |
| 渲染 | `DrawingVisual` + `FormattedText` + `CompositionTarget.Rendering` (VSYNC 同步) |
| IPC | Named Pipe，JSON Lines 协议 |

## 调试

| 日志 | 路径 | 说明 |
|------|------|------|
| 崩溃日志 | `WpfOverlay_crash.log` | 未捕获异常（App 级别） |
| Overlay 调试 | `overlay_debug.log` | Overlay 初始化和管道事件跟踪 |
| 后端日志 | 启动器「日志」标签页 | Python stdout/stderr 实时输出 |

## 致谢

- [xfgryujk/blivedm](https://github.com/xfgryujk/blivedm) — B 站直播弹幕抓取库
- [lepoco/wpfui](https://github.com/lepoco/wpfui) — WPF Fluent Design UI 组件库

## License

本项目中 vendored 的 blivedm 库采用 [MIT License](LICENSE)。
