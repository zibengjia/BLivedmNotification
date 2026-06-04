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

# Add parent to path so blivedm is importable
sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

import blivedm
from py_overlay.config import load_config
from py_overlay.handler import OverlayHandler

logger = logging.getLogger("blivedm_overlay")

PIPE_BUFFER_SIZE = 65536


class PipeWriter:
    """Wraps a Named Pipe handle for writing."""

    def __init__(self, pipe_handle):
        self.pipe = pipe_handle

    def send(self, data: bytes):
        try:
            win32file.WriteFile(self.pipe, data)
        except Exception:
            logger.exception("Pipe write failed")


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
        win32pipe.PIPE_TYPE_MESSAGE
        | win32pipe.PIPE_READMODE_MESSAGE
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

    # Set pipe to non-blocking mode
    win32pipe.SetNamedPipeHandleState(
        pipe, win32pipe.PIPE_NOWAIT, None, None
    )

    return PipeWriter(pipe)


async def main():
    logging.basicConfig(
        level=logging.INFO,
        format="[%(asctime)s] %(name)s: %(message)s",
        datefmt="%H:%M:%S",
    )

    parser = argparse.ArgumentParser(description="B站弹幕 Overlay 后端")
    parser.add_argument("--room", type=int, help="直播间ID (覆盖config.json)")
    parser.add_argument("--display", type=int, help="显示器索引 (覆盖config.json)")
    args = parser.parse_args()

    config = load_config()

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

    logger.info("Starting blivedm client for room %d", room_id)
    client.start()

    try:
        # Keep running until interrupted
        await client.join()
    except asyncio.CancelledError:
        pass
    except KeyboardInterrupt:
        logger.info("Shutting down ...")
    finally:
        client.stop_and_close()
        if session is not None:
            await session.close()
        logger.info("Done")


if __name__ == "__main__":
    try:
        asyncio.run(main())
    except KeyboardInterrupt:
        pass
