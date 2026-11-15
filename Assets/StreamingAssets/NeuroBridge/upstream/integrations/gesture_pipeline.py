#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""Pipeline rozpoznawania gestów dłoni i wysyłania ich do Unity.

Pipeline:

1. pobiera klatki z kamery,
2. wykrywa dłonie przez MediaPipe Hand Landmarker,
3. składa landmarki obu dłoni i klasyfikuje gest przez
   :class:`model.keypoint_classifier.keypoint_classifier.TwoHandGestureClassifier`
   (wspólny próg ``height_diff_threshold`` i strefa spoczynku
   ``RestZone`` z ``app.py`` oraz blokada zmiany gestu
   ``gesture_cooldown``),
4. łączy gest z kamery z pomiarem EEG (g.tec BCI Core-8, folder ``eeg/``):
   umiejętność odpala się tylko, gdy gestowi towarzyszy wyobrażenie
   (patrz :class:`eeg.spell_gate.SpellGate`),
5. wysyła do Unity przez :class:`integrations.event_sender.EventSender`
   wyłącznie zdarzenia-wyzwalacze umiejętności:
   ``ability_triggered`` (Push, jednorazowo) oraz ``ability_started`` /
   ``ability_ended`` (Heal, Choke, Lightning - trwają, dopóki trwa gest).
   ``--legacy_events`` przywraca stary protokół ``gesture_started`` /
   ``gesture_ended`` (sama kamera, bez EEG).

Uruchomienie::

    python integrations/gesture_pipeline.py --host 127.0.0.1 --port 5555

bez headsetu EEG (sama kamera, nowe zdarzenia)::

    python integrations/gesture_pipeline.py --eeg off

lub bez wysyłania (podgląd w konsoli)::

    python integrations/gesture_pipeline.py --dry_run

nagrywanie EEG w grze do douczania / kalibracji modeli (``eeg/TRAINING.md``)::

    python integrations/gesture_pipeline.py --calibrate 4      # podpowiadane próby z / bez wyobrażenia
    python integrations/gesture_pipeline.py --eeg_record       # zwykła gra, klawisze y / n = etykieta gestu
"""

from __future__ import annotations

import argparse
import sys
import time
from pathlib import Path
from typing import Optional

import cv2 as cv
import mediapipe as mp
from mediapipe.tasks import python as mp_tasks
from mediapipe.tasks.python import vision

PROJECT_ROOT = Path(__file__).resolve().parents[1]
if str(PROJECT_ROOT) not in sys.path:
    sys.path.insert(0, str(PROJECT_ROOT))

from integrations.send_event import EventSender
from eeg import ABILITIES, GateConfig, SpellGate
from eeg.fusion import EEGMonitor, FusionConfig, cv_ability_probs, fuse
from eeg.instant_check import InstantEEGCheck
from utils import draw_hand_centers, draw_rest_zone
try:
    from utils import draw_prediction_panel
except ImportError:            # utils/prediction_panel not in the repo yet -> old overlay (--no_panel)
    draw_prediction_panel = None
from model import (
    DEFAULT_GESTURE_COOLDOWN,
    DEFAULT_HEIGHT_DIFF_THRESHOLD,
    DEFAULT_REST_ZONE_RADIUS,
    DEFAULT_REST_ZONE_Y_OFFSET,
    INACTIVE_GESTURE_LABEL,
    NUM_HANDS,
    GesturePrediction,
    HandSample,
    RestZone,
    TwoHandGestureClassifier,
    load_keypoint_classifier,
    load_labels,
)

DEFAULT_MODEL_PATH = PROJECT_ROOT / "model" / "hand_landmarker.task"
DEFAULT_CLASSIFIER_PATH = (PROJECT_ROOT / "model" / "keypoint_classifier"
                           / "keypoint_classifier.tflite")
DEFAULT_LABELS_PATH = (PROJECT_ROOT / "model" / "keypoint_classifier"
                       / "keypoint_classifier_label.csv")



def resolve_path(path) -> Path:
    """Zamienia ścieżkę na absolutną, niezależnie od katalogu startowego.

    Ścieżka bezwzględna zostaje bez zmian. Względna jest szukana najpierw
    względem bieżącego katalogu, a jeśli tam nie istnieje - względem
    katalogu głównego projektu (PROJECT_ROOT). Dzięki temu można podać
    ``model/hand_landmarker.task`` zarówno z korzenia projektu, jak i
    z folderu ``integrations/`` (np. z notebooka).
    """
    path = Path(path).expanduser()
    if path.is_absolute():
        return path

    candidates = (Path.cwd() / path, PROJECT_ROOT / path)
    for candidate in candidates:
        if candidate.exists():
            return candidate.resolve()
    return (PROJECT_ROOT / path).resolve()


def require_file(path, what: str) -> Path:
    resolved = resolve_path(path)
    if not resolved.is_file():
        raise SystemExit("Nie znaleziono pliku ({}): {}".format(what, resolved))
    return resolved


DEFAULT_UNITY_HOST = "127.0.0.1"
DEFAULT_UNITY_PORT = 5555


class GesturePipeline:
    """Wykrywanie dłoni + klasyfikacja gestu dla pojedynczej klatki."""

    def __init__(
        self,
        model_path=DEFAULT_MODEL_PATH,
        classifier_path=DEFAULT_CLASSIFIER_PATH,
        labels_path=DEFAULT_LABELS_PATH,
        height_diff_threshold=DEFAULT_HEIGHT_DIFF_THRESHOLD,
        rest_zone_radius=DEFAULT_REST_ZONE_RADIUS,
        rest_zone_y_offset=DEFAULT_REST_ZONE_Y_OFFSET,
        gesture_cooldown=DEFAULT_GESTURE_COOLDOWN,
        min_detection_confidence=0.7,
        min_tracking_confidence=0.5,
        num_hands=NUM_HANDS,
    ) -> None:
        model_path = require_file(model_path, "model dłoni")
        classifier_path = require_file(classifier_path, "klasyfikator")
        labels_path = require_file(labels_path, "etykiety")

        self.labels = load_labels(labels_path)
        self.inactive_label = (
            INACTIVE_GESTURE_LABEL
            if INACTIVE_GESTURE_LABEL in self.labels
            else self.labels[-1]
        )

        classifier = load_keypoint_classifier(classifier_path)
        self.rest_zone = RestZone(radius=int(rest_zone_radius),
                                  y_offset=int(rest_zone_y_offset))
        self.gesture_classifier = TwoHandGestureClassifier(
            classifier=classifier,
            labels=self.labels,
            inactive_label=self.inactive_label,
            height_diff_threshold=height_diff_threshold,
            rest_zone=self.rest_zone,
            gesture_cooldown=gesture_cooldown,
        )

        num_hands = max(int(num_hands), NUM_HANDS)

        hand_landmarker_options = vision.HandLandmarkerOptions(
            base_options=mp_tasks.BaseOptions(
                model_asset_path=str(model_path)),
            running_mode=vision.RunningMode.VIDEO,
            num_hands=num_hands,
            min_hand_detection_confidence=min_detection_confidence,
            min_hand_presence_confidence=min_detection_confidence,
            min_tracking_confidence=min_tracking_confidence,
        )
        self.hand_landmarker = (
            vision.HandLandmarker.create_from_options(
                hand_landmarker_options))

        self._start_time = time.monotonic()
        self._last_timestamp_ms = -1

    def detect(
        self,
        image_bgr,
        timestamp_ms: Optional[int] = None,
    ) -> GesturePrediction:
        """Przetwarza klatkę BGR i zwraca wynik klasyfikacji gestu."""
        if timestamp_ms is None:
            timestamp_ms = int((time.monotonic() - self._start_time) * 1000)

        # Timestamp musi rosnąć monotonicznie w trybie VIDEO.
        if timestamp_ms <= self._last_timestamp_ms:
            timestamp_ms = self._last_timestamp_ms + 1
        self._last_timestamp_ms = timestamp_ms

        rgb_image = cv.cvtColor(image_bgr, cv.COLOR_BGR2RGB)
        mp_image = mp.Image(image_format=mp.ImageFormat.SRGB, data=rgb_image)
        results = self.hand_landmarker.detect_for_video(
            mp_image, timestamp_ms)

        image_height, image_width = image_bgr.shape[:2]
        hands = []
        for hand_landmarks, handedness in zip(results.hand_landmarks,
                                              results.handedness):
            hands.append(HandSample.from_mediapipe(
                hand_landmarks,
                handedness[0].category_name,
                image_width,
                image_height,
            ))

        return self.gesture_classifier.classify(hands)

    def close(self) -> None:
        self.hand_landmarker.close()

    def __enter__(self):
        return self

    def __exit__(self, exc_type, exc_value, traceback):
        self.close()


class UnityGestureBridge:
    """Zamienia wyniki klasyfikacji na zdarzenia start/koniec gestu."""

    def __init__(
        self,
        sender,
        min_confidence: float = 0.0,
        inactive_label: str = INACTIVE_GESTURE_LABEL,
        verbose: bool = False,
    ) -> None:
        self.sender = sender
        self.min_confidence = float(min_confidence)
        self.inactive_label = inactive_label
        self.verbose = verbose

        self._current_gesture: Optional[str] = None
        self._current_confidence = 0.0
        self._started_at = 0.0

    def update(self, prediction: GesturePrediction) -> Optional[str]:
        """Aktualizuje stan gestu; zwraca nazwę wysłanego zdarzenia."""
        now = time.monotonic()

        gesture = None
        if (prediction.active
                and prediction.label != self.inactive_label
                and prediction.confidence >= self.min_confidence):
            gesture = prediction.label

        if gesture == self._current_gesture:
            return None

        event_name = None

        if self._current_gesture is not None:
            duration = max(0.0, now - self._started_at)
            self.sender.send_gesture_ended(
                self._current_gesture,
                duration=duration,
                confidence=self._current_confidence,
            )
            self._log("gesture_ended", self._current_gesture, duration)
            event_name = "gesture_ended"

        if gesture is not None:
            self.sender.send_gesture_started(
                gesture,
                confidence=prediction.confidence,
                hand_sign_id=prediction.hand_sign_id,
            )
            self._log("gesture_started", gesture, prediction.confidence)
            event_name = "gesture_started"

        self._current_gesture = gesture
        self._current_confidence = prediction.confidence
        self._started_at = now

        return event_name

    def close(self) -> None:
        """Kończy aktualnie trwający gest przed zamknięciem pipeline'u."""
        if self._current_gesture is None:
            return

        duration = max(0.0, time.monotonic() - self._started_at)
        self.sender.send_gesture_ended(
            self._current_gesture,
            duration=duration,
            confidence=self._current_confidence,
        )
        self._log("gesture_ended", self._current_gesture, duration)
        self._current_gesture = None

    def _log(self, event: str, gesture: str, value: float) -> None:
        if not self.verbose:
            return

        print("[UnityGestureBridge] {} gesture={} ({:.3f})".format(
            event, gesture, value))


class AbilityBridge:
    """Kamera + EEG -> zdarzenia umiejętności wysyłane do Unity.

    Co klatkę: odbiera nowe okna EEG, podaje gest z kamery do
    :class:`eeg.spell_gate.SpellGate` i wysyła tylko zdarzenia, które bramka
    zwróci (``ability_triggered`` / ``ability_started`` / ``ability_ended``).
    """

    def __init__(
        self,
        sender,
        gate: SpellGate,
        eeg_stream=None,
        min_confidence: float = 0.0,
        inactive_label: str = INACTIVE_GESTURE_LABEL,
        verbose: bool = False,
        fusion: Optional[FusionConfig] = None,
    ) -> None:
        self.sender = sender
        self.gate = gate
        self.eeg = eeg_stream
        self.min_confidence = float(min_confidence)
        self.inactive_label = inactive_label
        self.verbose = verbose
        self.status = None
        self._last_status_text = None
        self.eeg_available = eeg_stream is None
        # Ciągła predykcja EEG do wizualizacji (niezależna od bieżącego gestu).
        self.fusion = fusion or FusionConfig()
        self.monitor = EEGMonitor({spell: gate.threshold(spell) for spell, _ in ABILITIES.values()},
                                  n_windows=self.fusion.eeg_windows, stale_s=self.fusion.eeg_stale_s)
        self.recorder = None          # eeg.recorder.SessionRecorder (--eeg_record / --calibrate)
        self.calibration = None       # eeg.recorder.Calibration (--calibrate)

    def update(self, prediction: GesturePrediction) -> list:
        now = time.monotonic()
        eeg_available = True
        if self.eeg is not None:
            windows = self.eeg.poll()
            self.gate.add_windows(windows)
            self.monitor.add_windows(windows)
            self.status = self.eeg.status()
            eeg_available = self.status.available
            text = self.status.text()
            if text != self._last_status_text:      # zmiana stanu EEG -> konsola
                print("[EEG] {}".format(text))
                self._last_status_text = text

        label = None
        if (prediction.active
                and prediction.label != self.inactive_label
                and prediction.confidence >= self.min_confidence):
            label = prediction.label

        self.eeg_available = eeg_available
        events = self.gate.update(now, label, prediction.confidence, eeg_available)
        for event in events:
            self.sender.send_json(event)
            self._log(event)
        if self.recorder is not None:
            finished = self.recorder.gesture(now, label)
            for event in events:
                self.recorder.event(now, event)
            if self.calibration is not None:
                self.calibration.update(now, finished)
        if isinstance(self.gate, InstantEEGCheck):          # --decision instant
            for decision in self.gate.pop_decisions():
                if decision["event"] == "ignored":
                    self._log_ignored(decision)
        return events

    def fused_prediction(self, prediction: GesturePrediction, labels):
        """Kamera + EEG -> :class:`eeg.fusion.FusedPrediction` (tylko wizualizacja)."""
        cv = cv_ability_probs(labels, prediction.probabilities)
        if self.eeg is None:                                  # --eeg off
            return fuse(cv, {}, self.monitor.ability_thresholds(), eeg_available=False,
                        config=self.fusion)
        return fuse(cv, self.monitor.scores(time.monotonic()), self.monitor.ability_thresholds(),
                    eeg_available=self.eeg_available, config=self.fusion,
                    eeg_artifact=self.monitor.last_artifact)

    def close(self) -> None:
        for event in self.gate.close(time.monotonic()):
            self.sender.send_json(event)
            self._log(event)
        if self.recorder is not None:
            try:
                self.eeg.poll()                       # samples still waiting in gpype
            except Exception:
                pass
            self.recorder.save(sim=self.eeg.mode == "sim")
            self.recorder = None

    def label_last(self, label: str) -> None:
        """Klawisz y / n: etykieta ostatniego gestu do nagrania."""
        if self.recorder is None:
            return
        ep = self.recorder.label_last(label, time.monotonic())
        if ep is None:
            print("[EEG record] brak gestu do oznaczenia")
        else:
            print("[EEG record] {} -> {}".format(ep.ability, "imagination" if label == "imagination"
                                                 else "gesture only"))

    @staticmethod
    def _log_ignored(decision: dict) -> None:
        reason = {"below_threshold": "EEG {} < próg {}".format(decision.get("eeg_score"),
                                                               decision.get("eeg_threshold")),
                  "no_eeg_data": "brak świeżych danych EEG",
                  "eeg_unavailable": "EEG niedostępne"}.get(decision["reason"], decision["reason"])
        print("[EEG check] {} zignorowany - {}".format(decision["ability"], reason))

    def _log(self, event: dict) -> None:
        if self.verbose:
            print("[AbilityBridge] {event} {ability} (mode={mode}, eeg={score}/{thr})".format(
                event=event["event"], ability=event["ability"], mode=event["mode"],
                score=event.get("eeg_score"), thr=event.get("eeg_threshold")))


class DryRunSender:
    """Zamiennik EventSender do testowania pipeline'u bez Unity."""

    def send_json(self, payload) -> bool:
        import json
        print("[dry-run] {}".format(json.dumps(payload, ensure_ascii=False)))
        return True

    def send_gesture_started(self, gesture, confidence=None, **extra) -> bool:
        print("[dry-run] gesture_started gesture={} confidence={}".format(
            gesture, confidence))
        return True

    def send_gesture_ended(self, gesture, duration=None, **extra) -> bool:
        print("[dry-run] gesture_ended gesture={} duration={}".format(
            gesture, duration))
        return True

    def close(self) -> None:
        pass


def get_args(argv=None):
    parser = argparse.ArgumentParser(
        description="Rozpoznawanie gestów dłoni i wysyłanie ich do Unity.")

    parser.add_argument("--host", default=DEFAULT_UNITY_HOST,
                        help="adres Unity TCP (default: %(default)s)")
    parser.add_argument("--port", type=int, default=DEFAULT_UNITY_PORT,
                        help="port Unity TCP (default: %(default)s)")
    parser.add_argument("--reconnect_delay", type=float, default=2.0)

    parser.add_argument("--device", type=int, default=0)
    parser.add_argument("--width", type=int, default=960)
    parser.add_argument("--height", type=int, default=540)

    parser.add_argument("--model_path", default=str(DEFAULT_MODEL_PATH))
    parser.add_argument("--classifier_path",
                        default=str(DEFAULT_CLASSIFIER_PATH))
    parser.add_argument("--labels_path", default=str(DEFAULT_LABELS_PATH))

    parser.add_argument("--num_hands", type=int, default=NUM_HANDS)
    parser.add_argument("--min_detection_confidence", type=float, default=0.7)
    parser.add_argument("--min_tracking_confidence", type=float, default=0.5)
    parser.add_argument("--height_diff_threshold", type=int,
                        default=DEFAULT_HEIGHT_DIFF_THRESHOLD,
                        help="minimalna różnica pionowa (px) między dłońmi; "
                             "mniejsza => inactive (0 = wyłączone)")
    parser.add_argument("--rest_zone_radius", type=int,
                        default=DEFAULT_REST_ZONE_RADIUS,
                        help="promień (px) półokrągłej strefy spoczynku przy "
                             "dolnej krawędzi obrazu; obie dłonie w strefie "
                             "=> inactive (0 = wyłączone)")
    parser.add_argument("--rest_zone_y_offset", type=int,
                        default=DEFAULT_REST_ZONE_Y_OFFSET,
                        help="o ile px PONIŻEJ dolnej krawędzi obrazu leży "
                             "środek okręgu strefy spoczynku")
    parser.add_argument("--gesture_cooldown", type=float,
                        default=DEFAULT_GESTURE_COOLDOWN,
                        help="minimalny czas (s) między zmianami gestu; nowy "
                             "gest jest ignorowany, dopóki nie minie "
                             "(eliminuje migotanie, 0 = wyłączone)")
    parser.add_argument("--min_gesture_confidence", type=float, default=0.0,
                        help="minimalna pewność, aby wysłać gest do Unity")

    # EEG (folder eeg/) ##################################################
    defaults = GateConfig()
    parser.add_argument("--eeg", choices=("headset", "sim", "off"), default="headset",
                        help="headset = BCI Core-8; sim = losowy sygnał (test "
                             "połączeń, decyzje bez znaczenia); off = sama "
                             "kamera (default: %(default)s)")
    parser.add_argument("--eeg_serial", default="U8-2025.08.21",
                        help="numer seryjny BCI Core-8")
    parser.add_argument("--eeg_model", default=str(PROJECT_ROOT / "eeg" / "models"
                                                   / "live_models.joblib"))
    parser.add_argument("--eeg_fallback", choices=("block", "cv"), default="block",
                        help="gdy EEG niedostępne (brak danych / elektroda bez "
                             "kontaktu): block = żadnych umiejętności, cv = "
                             "sama kamera (default: %(default)s)")
    parser.add_argument("--eeg_min_windows", type=int, default=defaults.min_windows_sustained,
                        help="Heal/Choke/Lightning: min. liczba okien EEG (co 0.25 s) "
                             "przed startem; więcej = mniej fałszywych, później")
    parser.add_argument("--eeg_min_windows_push", type=int, default=defaults.min_windows_instant,
                        help="Push: min. liczba okien EEG przed wyzwoleniem")
    parser.add_argument("--eeg_threshold_offset", type=float, default=defaults.threshold_offset,
                        help="dodawane do progów modeli (+ = ostrzej)")
    parser.add_argument("--eeg_release", type=float, default=defaults.release_s,
                        help="s poniżej progu EEG, po których trwająca umiejętność "
                             "gaśnie mimo gestu (0 = tylko gest ją kończy)")
    parser.add_argument("--cv_stable", type=float, default=defaults.cv_on_s,
                        help="s stabilnego gestu z kamery, zanim się liczy")
    parser.add_argument("--cv_grace", type=float, default=defaults.cv_grace_s,
                        help="przerwy w rozpoznaniu gestu krótsze niż tyle s są "
                             "ignorowane (antymigotanie)")
    parser.add_argument("--cv_rejoin", type=float, default=defaults.cv_rejoin_s,
                        help="ten sam gest wraca w ciągu tylu s po dłuższej "
                             "przerwie = ten sam gest (EEG się sumuje, Push nie "
                             "odpala drugi raz)")
    fusion_defaults = FusionConfig()
    parser.add_argument("--fusion_cv_weight", type=float, default=fusion_defaults.cv_weight,
                        help="waga kamery w predykcji łączonej na panelu (0..1; "
                             "wynik = cv^w * eeg^(1-w))")
    parser.add_argument("--fusion_threshold", type=float,
                        default=fusion_defaults.decision_threshold,
                        help="min. wynik łączny, by panel wskazał umiejętność")
    parser.add_argument("--eeg_display_windows", type=int, default=fusion_defaults.eeg_windows,
                        help="panel EEG: średnia z ostatnich N okien (co 0.25 s)")
    parser.add_argument("--no_panel", action="store_true",
                        help="bez panelu predykcji (kamera | EEG | łącznie) obok obrazu")
    parser.add_argument("--decision", choices=("gate", "instant"), default="gate",
                        help="gate = SpellGate (czeka na okna EEG z czasu gestu); "
                             "instant = w chwili wykrycia ruchu z kamery porównaj bieżący "
                             "wynik regresji EEG tego ruchu z progiem: powyżej -> TCP, "
                             "poniżej -> ignoruj (default: %(default)s)")
    parser.add_argument("--eeg_check_windows", type=int, default=4,
                        help="--decision instant: średnia z ostatnich N okien EEG "
                             "(co 0.25 s) w chwili wykrycia ruchu")
    parser.add_argument("--legacy_events", action="store_true",
                        help="stary protokół gesture_started/gesture_ended (bez EEG)")

    # Nagrywanie w grze -> douczanie / kalibracja (eeg/TRAINING.md) ###########
    parser.add_argument("--eeg_record", action="store_true",
                        help="nagraj EEG + gesty z kamery do eeg/data/game/game_*.csv.gz; "
                             "w oknie podglądu y = gest z wyobrażeniem, n = sam gest")
    parser.add_argument("--record_label", choices=("none", "imagination", "only"), default="none",
                        help="--eeg_record: etykieta gestów nieoznaczonych klawiszem "
                             "(imagination = w tej sesji każdy gest był z wyobrażeniem)")
    parser.add_argument("--calibrate", type=int, default=0, metavar="N",
                        help="sesja kalibracyjna: N prób z i N bez wyobrażenia na umiejętność, "
                             "podpowiadanych w podglądzie (włącza --eeg_record)")
    parser.add_argument("--calibrate_abilities", nargs="+", choices=list(ABILITIES),
                        help="--calibrate: tylko te umiejętności")
    parser.add_argument("--record_dir", default=str(PROJECT_ROOT / "eeg" / "data" / "game"),
                        help="folder nagrań z gry (default: %(default)s)")

    parser.add_argument("--no_display", action="store_true",
                        help="nie otwieraj okna podglądu OpenCV")
    parser.add_argument("--no_flip", action="store_true",
                        help="nie odbijaj lustrzanie obrazu z kamery")
    parser.add_argument("--max_frames", type=int, default=0,
                        help="zakończ po N klatkach (0 = bez limitu)")
    parser.add_argument("--dry_run", action="store_true",
                        help="nie łącz się z Unity, wypisuj zdarzenia")
    parser.add_argument("--verbose", action="store_true")

    return parser.parse_args(argv)


def run_camera(args) -> None:
    if draw_prediction_panel is None and not args.no_panel and not args.no_display:
        print("[gesture_pipeline] utils.draw_prediction_panel not found - showing the old overlay")
    pipeline = GesturePipeline(
        model_path=args.model_path,
        classifier_path=args.classifier_path,
        labels_path=args.labels_path,
        height_diff_threshold=args.height_diff_threshold,
        rest_zone_radius=args.rest_zone_radius,
        rest_zone_y_offset=args.rest_zone_y_offset,
        gesture_cooldown=args.gesture_cooldown,
        min_detection_confidence=args.min_detection_confidence,
        min_tracking_confidence=args.min_tracking_confidence,
        num_hands=args.num_hands,
    )

    if args.dry_run:
        sender = DryRunSender()
    else:
        sender = EventSender(
            host=args.host,
            port=args.port,
            reconnect_delay=args.reconnect_delay,
        )
        if not sender.connect():
            print("[gesture_pipeline] Unity niedostępne - będzie ponawiane "
                  "przy wysyłaniu gestów.")

    cap = cv.VideoCapture(args.device)
    cap.set(cv.CAP_PROP_FRAME_WIDTH, args.width)
    cap.set(cv.CAP_PROP_FRAME_HEIGHT, args.height)

    if not cap.isOpened():
        pipeline.close()
        sender.close()
        raise SystemExit("Nie można otworzyć kamery {}".format(args.device))

    eeg_stream = None
    try:
        bridge, eeg_stream = make_bridge(args, sender, pipeline.inactive_label)
    except BaseException:
        pipeline.close()
        cap.release()
        sender.close()
        raise

    frame_count = 0

    try:
        while True:
            ok, image = cap.read()
            if not ok:
                print("[gesture_pipeline] Brak klatki z kamery - koniec.")
                break

            if not args.no_flip:
                image = cv.flip(image, 1)

            prediction = pipeline.detect(image)
            bridge.update(prediction)

            frame_count += 1

            if not args.no_display:
                draw_rest_zone(image, pipeline.rest_zone, prediction)
                draw_hand_centers(image, prediction)
                draw_status(image, prediction)
                if not args.no_panel and draw_prediction_panel is not None:
                    image = draw_panel(image, prediction, pipeline.labels, bridge, args)
                elif isinstance(bridge, AbilityBridge):
                    draw_ability_status(image, bridge)
                draw_recording(image, bridge)
                cv.imshow("Gesture Pipeline", image)
                key = cv.waitKey(1) & 0xFF
                if key == 27:  # ESC
                    break
                if key in (ord("y"), ord("n")) and isinstance(bridge, AbilityBridge):
                    bridge.label_last("imagination" if key == ord("y") else "only")

            if args.max_frames > 0 and frame_count >= args.max_frames:
                break

    except KeyboardInterrupt:
        print("\n[gesture_pipeline] Przerwano przez użytkownika.")
    finally:
        bridge.close()
        if eeg_stream is not None:
            eeg_stream.stop()
        pipeline.close()
        cap.release()
        cv.destroyAllWindows()
        sender.close()


def make_bridge(args, sender, inactive_label):
    """Stary most (gesture_started/ended) albo kamera + EEG (zdarzenia umiejętności)."""
    verbose = args.verbose or args.dry_run
    if (args.eeg_record or args.calibrate) and (args.legacy_events or args.eeg == "off"):
        raise SystemExit("--eeg_record / --calibrate need EEG (--eeg headset or sim, no --legacy_events)")
    if args.legacy_events:
        return UnityGestureBridge(sender=sender, min_confidence=args.min_gesture_confidence,
                                  inactive_label=inactive_label, verbose=verbose), None

    config = GateConfig(
        cv_on_s=args.cv_stable,
        cv_grace_s=args.cv_grace,
        cv_rejoin_s=args.cv_rejoin,
        min_windows_sustained=args.eeg_min_windows,
        min_windows_instant=args.eeg_min_windows_push,
        threshold_offset=args.eeg_threshold_offset,
        release_s=args.eeg_release,
    )
    eeg_stream = None
    if args.eeg == "off":
        thresholds = {spell: 0.5 for spell, _ in ABILITIES.values()}   # nieużywane w trybie cv_only
        print("[gesture_pipeline] EEG wyłączone - umiejętności tylko z kamery.")
    else:
        from eeg import EEGSpellStream
        eeg_stream = EEGSpellStream(model_path=resolve_path(args.eeg_model), mode=args.eeg,
                                    serial=args.eeg_serial)
        print("[gesture_pipeline] Łączenie z EEG ({}) ...".format(args.eeg))
        eeg_stream.start()
        thresholds = eeg_stream.thresholds
        print("[gesture_pipeline] EEG działa; progi: {}".format(
            ", ".join("{} {:.2f}".format(k, v) for k, v in thresholds.items())))
    gate_mode = "cv_only" if eeg_stream is None else "eeg"
    if args.decision == "instant":
        gate = InstantEEGCheck(thresholds, config, mode=gate_mode, fallback=args.eeg_fallback,
                               n_windows=args.eeg_check_windows)
        print("[gesture_pipeline] Decyzja: instant - wynik EEG ruchu sprawdzany w chwili "
              "wykrycia ({} okien).".format(args.eeg_check_windows))
    else:
        gate = SpellGate(thresholds, config, mode=gate_mode, fallback=args.eeg_fallback)
    fusion = FusionConfig(eeg_windows=args.eeg_display_windows, cv_weight=args.fusion_cv_weight,
                          decision_threshold=args.fusion_threshold)
    bridge = AbilityBridge(sender, gate, eeg_stream, min_confidence=args.min_gesture_confidence,
                           inactive_label=inactive_label, verbose=verbose, fusion=fusion)
    if args.eeg_record or args.calibrate:
        from eeg.recorder import Calibration, SessionRecorder
        rec = SessionRecorder(Path(args.record_dir), cfg=config,
                              default_label=None if args.record_label == "none" else args.record_label)
        eeg_stream.recorder = bridge.recorder = rec
        print("[EEG record] nagrywanie -> {} (zapis przy wyjściu, ESC)".format(rec.out_dir))
        if args.calibrate:
            bridge.calibration = Calibration(rec, reps=args.calibrate, abilities=args.calibrate_abilities)
            print("[calibration] {} prób - patrz podpowiedzi w podglądzie / konsoli".format(
                len(bridge.calibration.plan)))
        else:
            print("[EEG record] y = ostatni gest Z wyobrażeniem, n = BEZ (okno podglądu musi mieć fokus)")
    return bridge, eeg_stream


def draw_panel(image, prediction, labels, bridge, args):
    """Dokleja z prawej panel: predykcja z kamery, z EEG i łączna (+ decyzja bramki)."""
    if isinstance(bridge, AbilityBridge):
        return draw_prediction_panel(
            image, prediction, labels,
            fused=bridge.fused_prediction(prediction, labels),
            eeg_status=bridge.status, eeg_mode=args.eeg, gate_view=bridge.gate.view(),
            cv_weight=bridge.fusion.cv_weight, decision_threshold=bridge.fusion.decision_threshold)
    # --legacy_events: sama kamera
    fusion = FusionConfig(cv_weight=args.fusion_cv_weight, decision_threshold=args.fusion_threshold)
    fused = fuse(cv_ability_probs(labels, prediction.probabilities), {}, {},
                 eeg_available=False, config=fusion)
    return draw_prediction_panel(image, prediction, labels, fused=fused, eeg_mode="off",
                                 cv_weight=fusion.cv_weight,
                                 decision_threshold=fusion.decision_threshold)


def draw_ability_status(image, bridge: "AbilityBridge") -> None:
    """EEG: stan headsetu + bieżący gest, wynik EEG względem progu, stan umiejętności."""
    lines = []
    if bridge.status is not None:
        ok = bridge.status.available
        lines.append((bridge.status.text(), (0, 255, 0) if ok else (0, 0, 255)))
    elif bridge.gate.mode == "cv_only":
        lines.append(("EEG off - camera only", (200, 200, 200)))

    view = bridge.gate.view()
    if view is not None:
        if view["mode"] == "cv":
            state = "ACTIVE" if view["active"] else ("FIRED" if view["triggered"] else "-")
            lines.append(("{}: camera only  {}".format(view["ability"], state), (255, 255, 0)))
        else:
            score = view["score"]
            state = ("ACTIVE" if view["active"] else
                     "FIRED" if view["triggered"] else
                     "IGNORED" if view.get("rejected") else
                     "waiting for EEG" if view["gesture_on"] else "-")
            lines.append(("{}: EEG {} / {:.2f}  ({} win)  {}".format(
                view["ability"], "--" if score is None else "{:.2f}".format(score),
                view["threshold"], view["n"], state),
                (0, 255, 0) if view["active"] or view["triggered"] else (0, 200, 255)))

    y = 115
    for text, color in lines:
        cv.putText(image, text, (10, y), cv.FONT_HERSHEY_SIMPLEX, 0.6, (0, 0, 0), 4, cv.LINE_AA)
        cv.putText(image, text, (10, y), cv.FONT_HERSHEY_SIMPLEX, 0.6, color, 1, cv.LINE_AA)
        y += 25

    if view is not None and view["mode"] == "eeg" and view["score"] is not None:
        x0, w, h = 10, 220, 10                        # pasek: wynik EEG vs próg
        cv.rectangle(image, (x0, y - 8), (x0 + w, y - 8 + h), (80, 80, 80), -1)
        cv.rectangle(image, (x0, y - 8), (x0 + int(w * min(1.0, view["score"])), y - 8 + h),
                     (0, 255, 0) if view["score"] >= view["threshold"] else (0, 200, 255), -1)
        tx = x0 + int(w * view["threshold"])
        cv.line(image, (tx, y - 11), (tx, y + 5), (255, 255, 255), 2)


def draw_recording(image, bridge) -> None:
    """Nagrywanie / kalibracja: podpowiedź na górze podglądu."""
    rec = getattr(bridge, "recorder", None)
    if rec is None:
        return
    cal = bridge.calibration
    if cal is not None:
        lines = cal.lines(time.monotonic())
        colour = (0, 220, 255) if cal.state == "go" and not cal.finished else (255, 255, 255)
    else:
        n = sum(rec.counts().values()) + (rec.tracker.current is not None)
        lines = ["REC  {} gestures   y = with imagination, n = gesture only".format(n)]
        colour = (80, 80, 255)
    h = 34 * len(lines) + 12
    cv.rectangle(image, (0, 0), (image.shape[1], h), (0, 0, 0), -1)
    for i, line in enumerate(lines):
        cv.putText(image, line, (12, 32 + 34 * i), cv.FONT_HERSHEY_SIMPLEX, 0.85,
                   colour if i == 0 else (230, 230, 230), 2, cv.LINE_AA)


def draw_status(image, prediction: GesturePrediction) -> None:
    color = (0, 255, 0) if prediction.active else (0, 165, 255)
    gesture_text = "GESTURE: {}".format(prediction.label)
    if prediction.held:
        gesture_text += "  HOLD {:.1f}s (next: {})".format(
            prediction.cooldown_remaining, prediction.raw_label)
    elif prediction.in_rest_zone:
        gesture_text += " (rest zone)"

    cv.putText(image, gesture_text, (10, 30), cv.FONT_HERSHEY_SIMPLEX, 0.9,
               (0, 0, 0), 4, cv.LINE_AA)
    cv.putText(image, gesture_text, (10, 30), cv.FONT_HERSHEY_SIMPLEX, 0.9,
               color, 2, cv.LINE_AA)

    if prediction.both_hands_visible:
        height_text = "dY: {:.0f}px / {}".format(
            prediction.height_diff, prediction.height_diff_threshold)
    else:
        height_text = "waiting for both hands..."

    cv.putText(image, height_text, (10, 60), cv.FONT_HERSHEY_SIMPLEX, 0.6,
               (255, 255, 255), 1, cv.LINE_AA)

    if prediction.classified:
        confidence_text = "CONF: {:.2f}".format(prediction.confidence)
        cv.putText(image, confidence_text, (10, 85), cv.FONT_HERSHEY_SIMPLEX,
                   0.6, (255, 255, 255), 1, cv.LINE_AA)


def main(argv=None) -> int:
    args = get_args(argv)
    run_camera(args)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
