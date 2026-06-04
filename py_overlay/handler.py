import json
import logging

import blivedm
import blivedm.models.web as web_models

logger = logging.getLogger("blivedm_overlay")


class OverlayHandler(blivedm.BaseHandler):
    """
    Handler that streams live events to C# overlay via Named Pipe.
    """

    def __init__(self, pipe_writer):
        self.pipe = pipe_writer

    def _on_danmaku(self, client: blivedm.BLiveClient, message: web_models.DanmakuMessage):
        """普通弹幕"""
        data = {
            "type": "danmaku",
            "uid": message.uid,
            "uname": message.uname,
            "msg": message.msg,
            "color": message.color,
            "font_size": message.font_size or 25,
            "timestamp": message.timestamp,
        }
        self._send(data)

    def _on_super_chat(self, client: blivedm.BLiveClient, message: web_models.SuperChatMessage):
        """醒目留言 (SC)"""
        data = {
            "type": "super_chat",
            "price": message.price,
            "uname": message.uname,
            "message": message.message,
            "color": 0xFFD700,  # gold
        }
        self._send(data)

    def _on_heartbeat(self, client: blivedm.BLiveClient, message: web_models.HeartbeatMessage):
        """心跳包 - 暂不处理，可用来传递人气值"""
        pass

    def _on_gift(self, client: blivedm.BLiveClient, message: web_models.GiftMessage):
        """礼物 - 暂不处理"""
        pass

    def _on_buy_guard(self, client: blivedm.BLiveClient, message: web_models.GuardBuyMessage):
        """上舰 - 暂不处理"""
        pass

    def _send(self, data: dict):
        """Send JSON line to C# overlay via pipe"""
        try:
            line = json.dumps(data, ensure_ascii=False) + "\n"
            self.pipe.send(line.encode("utf-8"))
        except Exception as e:
            logger.error("Failed to send to pipe: %s", e)
