import json
import socket
import threading
import time
from typing import Any, Optional


class EventSender:
    """
    Wysyła zdarzenia z Pythona do Unity przez TCP.

    Format wiadomości: jedna linia JSON zakończona znakiem '\n'.
    Przykład:
    {"event": "gesture_started", "gesture": "Push", "confidence": 0.94}
    """

    def __init__(
        self,
        host: str = "127.0.0.1",
        port: int = 5555,
        reconnect_delay: float = 2.0,
        connect_timeout: float = 3.0,
        auto_reconnect: bool = True,
    ) -> None:
        self.host = host
        self.port = port
        self.reconnect_delay = reconnect_delay
        self.connect_timeout = connect_timeout
        self.auto_reconnect = auto_reconnect

        self._socket: Optional[socket.socket] = None
        self._lock = threading.Lock()
        self._closed = False

    def connect(self) -> bool:
        """Nawiązuje połączenie z Unity. Zwraca True po sukcesie."""
        with self._lock:
            if self._closed:
                raise RuntimeError("EventSender został zamknięty.")

            self._close_socket()

            try:
                sock = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
                sock.settimeout(self.connect_timeout)
                sock.connect((self.host, self.port))
                sock.settimeout(None)

                sock.setsockopt(socket.IPPROTO_TCP, socket.TCP_NODELAY, 1)
                self._socket = sock
                print(f"[EventSender] Połączono z Unity: {self.host}:{self.port}")
                return True

            except (OSError, socket.timeout) as error:
                self._close_socket()
                print(f"[EventSender] Nie udało się połączyć: {error}")
                return False

    def send_event(
        self,
        event: str,
        gesture: Optional[str] = None,
        confidence: Optional[float] = None,
        **extra: Any,
    ) -> bool:
        """
        Wysyła zdarzenie do Unity.

        Przykład:
            sender.send_event("gesture_started", "swipe_left", 0.94)
        """
        payload: dict[str, Any] = {
            "event": event,
            "timestamp": time.time(),
        }

        if gesture is not None:
            payload["gesture"] = gesture

        if confidence is not None:
            payload["confidence"] = confidence

        payload.update(extra)

        return self.send_json(payload)

    def send_json(self, payload: dict[str, Any]) -> bool:
        """Wysyła dowolny słownik jako jedną linię JSON."""
        message = (json.dumps(payload, ensure_ascii=False) + "\n").encode("utf-8")

        with self._lock:
            if self._closed:
                return False

            if not self._is_connected():
                if not self.auto_reconnect or not self._reconnect():
                    return False

            try:
                self._socket.sendall(message)
                return True

            except (OSError, socket.timeout) as error:
                print(f"[EventSender] Błąd wysyłania: {error}")
                self._close_socket()

                if self.auto_reconnect:
                    self._reconnect()

                return False

    def send_gesture_started(
        self,
        gesture: str,
        confidence: Optional[float] = None,
        **extra: Any,
    ) -> bool:
        """Skrót dla rozpoczęcia gestu."""
        return self.send_event(
            "gesture_started",
            gesture=gesture,
            confidence=confidence,
            **extra,
        )

    def send_gesture_ended(
        self,
        gesture: str,
        duration: Optional[float] = None,
        **extra: Any,
    ) -> bool:
        """Skrót dla zakończenia gestu, np. pinch, grab albo hold."""
        extra_data = {"duration": duration} if duration is not None else {}
        extra_data.update(extra)

        return self.send_event(
            "gesture_ended",
            gesture=gesture,
            **extra_data,
        )

    def close(self) -> None:
        """Zamyka połączenie i blokuje dalsze wysyłanie."""
        with self._lock:
            self._closed = True
            self._close_socket()
            print("[EventSender] Połączenie zamknięte.")

    def _is_connected(self) -> bool:
        return self._socket is not None

    def _reconnect(self) -> bool:
        print(f"[EventSender] Ponawiam połączenie za {self.reconnect_delay:.1f}s...")

        # Blokada jest już zajęta przez send_json, więc nie używamy locka ponownie.
        time.sleep(self.reconnect_delay)
        return self._connect_without_lock()

    def _connect_without_lock(self) -> bool:
        if self._closed:
            return False

        self._close_socket()

        try:
            sock = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
            sock.settimeout(self.connect_timeout)
            sock.connect((self.host, self.port))
            sock.settimeout(None)
            sock.setsockopt(socket.IPPROTO_TCP, socket.TCP_NODELAY, 1)

            self._socket = sock
            print(f"[EventSender] Ponownie połączono z Unity: {self.host}:{self.port}")
            return True

        except (OSError, socket.timeout) as error:
            self._close_socket()
            print(f"[EventSender] Ponowne połączenie nie powiodło się: {error}")
            return False

    def _close_socket(self) -> None:
        if self._socket is not None:
            try:
                self._socket.close()
            except OSError:
                pass

            self._socket = None