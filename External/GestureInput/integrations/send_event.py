#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""Pomocniczy skrypt do wysyłania pojedynczych zdarzeń do Unity.

Moduł jest cienką fasadą nad :class:`integrations.event_sender.EventSender`.
Można go użyć dwojako:

Przykład użycia: python integrations/send_event.py ability_triggered --ability Push
                 python integrations/send_event.py gesture_started --gesture Push --confidence 0.92
"""

from __future__ import annotations

import argparse
from typing import Any, Optional

try:
    from integrations.event_sender import EventSender
except ImportError:
    from event_sender import EventSender

__all__ = ["EventSender", "send_event", "main"]


def send_event(
    event: str,
    gesture: Optional[str] = None,
    confidence: Optional[float] = None,
    host: str = "127.0.0.1",
    port: int = 5555,
    connect_timeout: float = 3.0,
    **extra: Any,
) -> bool:
    """Łączy się z Unity, wysyła jedno zdarzenie i zamyka połączenie."""
    sender = EventSender(
        host=host,
        port=port,
        connect_timeout=connect_timeout,
        auto_reconnect=False,
    )

    if not sender.connect():
        return False

    try:
        return sender.send_event(
            event,
            gesture=gesture,
            confidence=confidence,
            **extra,
        )
    finally:
        sender.close()


def main(argv=None) -> int:
    parser = argparse.ArgumentParser(
        description="Wyślij pojedyncze zdarzenie do Unity przez TCP.")
    parser.add_argument("event", help="np. gesture_started / gesture_ended")
    parser.add_argument("--gesture", default=None)
    parser.add_argument("--ability", default=None,
                        help="dla zdarzeń ability_*: Push / Heal / Choke / Lightning")
    parser.add_argument("--confidence", type=float, default=None)
    parser.add_argument("--host", default="127.0.0.1")
    parser.add_argument("--port", type=int, default=5555)

    args = parser.parse_args(argv)

    extra = {}
    if args.ability is not None:
        extra["ability"] = args.ability
        extra["kind"] = "instant" if args.ability == "Push" else "sustained"
        extra["mode"] = "manual"

    sent = send_event(
        event=args.event,
        gesture=args.gesture,
        confidence=args.confidence,
        host=args.host,
        port=args.port,
        **extra,
    )

    return 0 if sent else 1


if __name__ == "__main__":
    raise SystemExit(main())
