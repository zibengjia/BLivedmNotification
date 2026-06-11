#!/usr/bin/env python3
"""
B站直播间弹幕 → Named Pipe → C# Overlay

Usage:
    python py_overlay/main.py                    # 使用 config.json
    python py_overlay/main.py --room 12345       # 指定房间
    python py_overlay/main.py --display 1        # 指定显示器
"""
import argparse
import asyncio
import http.cookies
import logging
import os
import sys
import aiohttp
import win32file
import win32pipe

from py_overlay.config import load_config, is_frozen, get_app_root

# Add parent to path so blivedm is importable (source mode only;
# when frozen, PyInstaller bundles blivedm via hiddenimports)
if not is_frozen():
    sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

import blivedm
from py_overlay.handler import OverlayHandler

logger = logging.getLogger("blivedm_overlay")

PIPE_BUFFER_SIZE = 65536


class PipeWriter:
    """Wraps a Named Pipe handle for writing. Auto-reconnects on disconnect."""

    def __init__(self, pipe_handle=None, pipe_path=None):
        self.pipe = pipe_handle
        self.pipe_path = pipe_path
        self._connected = pipe_handle is not None

    def send(self, data: bytes):
        if not self._connected or self.pipe is None:
            return
        try:
            win32file.WriteFile(self.pipe, data)
        except win32file.error as e:
            if e.winerror in (232, 109, 995):  # ERROR_NO_DATA, ERROR_BROKEN_PIPE, ERROR_OPERATION_ABORTED
                self._connected = False
                logger.info("Pipe disconnected")
            else:
                logger.exception("Pipe write failed")
        except Exception:
            logger.exception("Pipe write failed")

    async def ensure_connected(self):
        """Reconnect if disconnected. Called periodically from main loop."""
        if self._connected:
            return

        # Close old handle if still open
        if self.pipe is not None:
            try:
                win32file.CloseHandle(self.pipe)
            except Exception:
                pass
            self.pipe = None

        logger.info("Waiting for C# overlay to reconnect on %s ...", self.pipe_path)
        loop = asyncio.get_running_loop()
        new_pipe = win32pipe.CreateNamedPipe(
            self.pipe_path,
            win32pipe.PIPE_ACCESS_DUPLEX,
            win32pipe.PIPE_TYPE_BYTE | win32pipe.PIPE_READMODE_BYTE | win32pipe.PIPE_WAIT,
            win32pipe.PIPE_UNLIMITED_INSTANCES,
            PIPE_BUFFER_SIZE, PIPE_BUFFER_SIZE, 0, None,
        )
        await loop.run_in_executor(None, lambda: win32pipe.ConnectNamedPipe(new_pipe, None))
        self.pipe = new_pipe
        self._connected = True
        logger.info("C# overlay reconnected!")


async def pipe_server(config: dict) -> PipeWriter:
    """
    Create Named Pipe server, wait for C# overlay to connect,
    return a PipeWriter for sending data.
    """
    pipe_name = config.get("pipe_name", "BlivedmOverlay")
    pipe_path = rf"\\.\pipe\{pipe_name}"

    logger.info("Waiting for C# overlay to connect on %s ...", pipe_path)

    pipe = win32pipe.CreateNamedPipe(
        pipe_path,
        win32pipe.PIPE_ACCESS_DUPLEX,
        # Byte mode — C# NamedPipeClientStream doesn't support message mode.
        # The \n-delimited JSON line framing is handled at the application level.
        win32pipe.PIPE_TYPE_BYTE
        | win32pipe.PIPE_READMODE_BYTE
        | win32pipe.PIPE_WAIT,
        win32pipe.PIPE_UNLIMITED_INSTANCES,
        PIPE_BUFFER_SIZE,
        PIPE_BUFFER_SIZE,
        0,
        None,
    )

    # Wait for C# client to connect (blocking, run in executor)
    loop = asyncio.get_running_loop()
    await loop.run_in_executor(None, lambda: win32pipe.ConnectNamedPipe(pipe, None))

    logger.info("C# overlay connected!")

    # Note: intentionally NOT setting PIPE_NOWAIT here.
    # PIPE_NOWAIT causes WriteFile to fail with ERROR_NO_DATA if the C# client's
    # read loop hasn't started yet (race condition on connect).
    # Default PIPE_WAIT mode ensures writes succeed — the pipe buffer is 64KB
    # and messages are small (JSON lines), so there's no blocking concern.

    return PipeWriter(pipe, pipe_path=pipe_path)


async def main():
    logging.basicConfig(
        level=logging.INFO,
        format="[%(asctime)s] %(name)s: %(message)s",
        datefmt="%H:%M:%S",
    )

    parser = argparse.ArgumentParser(description="B站弹幕 Overlay 后端")
    parser.add_argument("--room", type=int, help="直播间ID (覆盖config.json)")
    parser.add_argument("--display", type=int, help="显示器索引 (覆盖config.json)")
    parser.add_argument("--config", type=str, default=None,
                        help="config.json 的完整路径 (打包模式下由启动器传入)")
    args = parser.parse_args()

    config = load_config(args.config)

    if args.room is not None:
        config["room_id"] = args.room
    if args.display is not None:
        config["display_index"] = args.display

    room_id = config["room_id"]
    logger.info("Connecting to room %d ...", room_id)

    # Create aiohttp session with optional SESSDATA cookie for authenticated access
    sessdata = config.get("sessdata", "")
    session = None
    if sessdata:
        cookies = http.cookies.SimpleCookie()
        cookies["SESSDATA"] = sessdata
        cookies["SESSDATA"]["domain"] = "bilibili.com"
        session = aiohttp.ClientSession()
        session.cookie_jar.update_cookies(cookies)
        logger.info("Using SESSDATA cookie for authenticated connection")
    else:
        logger.info("No SESSDATA provided — usernames may be masked")

    # Start Named Pipe server (blocks until C# connects)
    pipe_writer = await pipe_server(config)

    # Create blivedm client with optional session
    client = blivedm.BLiveClient(room_id, session=session)
    handler = OverlayHandler(pipe_writer)
    client.set_handler(handler)

    # Fetch room info (主播名, 标题, 状态等)
    await client.init_room()
    logger.info("房间: %s | 主播: %s | 标题: %s | 状态: %s | 在线: %d",
                client.room_id,
                client.room_uname or "(unknown)",
                client.room_title or "(无)",
                {0: "未开播", 1: "直播中", 2: "轮播"}.get(client.live_status, "?"),
                client.online)
    if client.live_status == 1 and client.live_time:
        logger.info("开播时间: %s", client.live_time)

    logger.info("Starting blivedm client for room %d", room_id)
    client.start()

    async def pipe_watchdog():
        """Periodically check pipe connection and reconnect if needed."""
        while True:
            await asyncio.sleep(1)
            await pipe_writer.ensure_connected()

    watchdog_task = asyncio.create_task(pipe_watchdog())

    try:
        # Keep running until interrupted
        await client.join()
    except asyncio.CancelledError:
        pass
    except KeyboardInterrupt:
        pass
    finally:
        watchdog_task.cancel()
        try:
            await watchdog_task
        except asyncio.CancelledError:
            pass
        await client.stop_and_close()
        if session is not None:
            await session.close()
        logger.info("Done")


if __name__ == "__main__":
    try:
        asyncio.run(main())
    except KeyboardInterrupt:
        pass
