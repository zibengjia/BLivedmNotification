#!/usr/bin/env python3
"""
Fetch Bilibili room info and print as JSON.
Used by the C# launcher to auto-fill streamer names.

Usage:
    python py_overlay/fetch_room.py <room_id>
    python py_overlay/fetch_room.py 12345
"""
import asyncio
import json
import os
import sys

# Add parent to path so blivedm is importable
sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

import aiohttp
import blivedm

MASTER_INFO_API = "https://api.live.bilibili.com/live_user/v1/Master/info"
HEADERS = {
    "User-Agent": "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36",
    "Referer": "https://live.bilibili.com/",
}


async def fetch_uname_by_uid(uid: int, session: aiohttp.ClientSession) -> str:
    """Fallback: get username from Master/info API using UID."""
    try:
        async with session.get(
            f"{MASTER_INFO_API}?uid={uid}",
            headers=HEADERS,
        ) as resp:
            data = await resp.json()
            if data.get("code") == 0:
                return data["data"]["info"].get("uname", "")
    except Exception:
        pass
    return ""


async def main():
    if len(sys.argv) < 2:
        print(json.dumps({"error": "Missing room_id argument"}))
        sys.exit(1)

    try:
        room_id = int(sys.argv[1])
    except ValueError:
        print(json.dumps({"error": f"Invalid room_id: {sys.argv[1]}"}))
        sys.exit(1)

    session = aiohttp.ClientSession(headers=HEADERS)
    client = blivedm.BLiveClient(room_id, session=session)

    try:
        await client.init_room()

        uname = client.room_uname
        # Some rooms return empty uname from room init API;
        # fallback to space API using the streamer UID
        if not uname and client.room_owner_uid:
            uname = await fetch_uname_by_uid(client.room_owner_uid, session)

        print(json.dumps({
            "room_id": client.room_id,
            "uname": uname,
            "title": client.room_title,
            "live_status": client.live_status,
            "online": client.online,
        }))
    except Exception as e:
        print(json.dumps({"error": str(e)}))
        sys.exit(1)
    finally:
        await client.stop_and_close()


if __name__ == "__main__":
    asyncio.run(main())
