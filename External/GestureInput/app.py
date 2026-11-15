#!/usr/bin/env python
# -*- coding: utf-8 -*-
import copy
import csv
import argparse
import os
import time

import cv2 as cv
import numpy as np
import mediapipe as mp
from mediapipe.tasks import python as mp_tasks
from mediapipe.tasks.python import vision

from utils import CvFpsCalc, draw_hand_centers, draw_rest_zone
from model import (
    DEFAULT_GESTURE_COOLDOWN,
    DEFAULT_HEIGHT_DIFF_THRESHOLD,
    DEFAULT_REST_ZONE_RADIUS,
    DEFAULT_REST_ZONE_Y_OFFSET,
    LANDMARKS_PER_HAND,
    NUM_FEATURES,
    NUM_HANDS,
    HandSample,
    RestZone,
    TwoHandGestureClassifier,
    load_keypoint_classifier,
    load_labels,
)

try:
    import pygame
except ImportError:
    pygame = None

# Przyyciski gamepada LB=4, RB=5
BUMPER_BUTTONS = (4, 5)


def get_args():
    parser = argparse.ArgumentParser()

    parser.add_argument("--num_hands", type=int, default=2)

    parser.add_argument("--device", type=int, default=0)
    parser.add_argument("--width", help='cap width', type=int, default=960)
    parser.add_argument("--height", help='cap height', type=int, default=540)

    parser.add_argument("--model_path",
                        help='hand landmarker .task model path',
                        default='model/hand_landmarker.task')

    parser.add_argument("--classifier_path",
                        help='keypoint classifier model (.tflite or .keras)',
                        default='model/keypoint_classifier/'
                               'keypoint_classifier.tflite')
    parser.add_argument("--labels_path",
                        help='gesture labels csv path',
                        default='model/keypoint_classifier/'
                                'keypoint_classifier_label.csv')
    parser.add_argument("--no_classification",
                        help='run WITHOUT the keypoint classifier (hand '
                             'detection + sample recording only)',
                        action='store_true')
    parser.add_argument("--min_detection_confidence",
                        help='min_detection_confidence',
                        type=float,
                        default=0.7)
    parser.add_argument("--height_diff_threshold",
                        help='min allowed vertical distance (px) between the '
                             'two hands; a smaller difference forces the '
                             'gesture to inactive (0 = disabled)',
                        type=int,
                        default=DEFAULT_HEIGHT_DIFF_THRESHOLD)
    parser.add_argument("--rest_zone_radius",
                        help='radius (px) of the half-circle rest zone at the '
                             'bottom of the screen; both hands inside it force '
                             'the gesture to inactive (0 = disabled)',
                        type=int,
                        default=DEFAULT_REST_ZONE_RADIUS)
    parser.add_argument("--rest_zone_y_offset",
                        help='how far (px) BELOW the bottom edge of the screen '
                             'the rest zone circle center lies',
                        type=int,
                        default=DEFAULT_REST_ZONE_Y_OFFSET)
    parser.add_argument("--gesture_cooldown",
                        help='min time (s) between two gesture changes; a new '
                             'gesture is ignored until it elapses (removes '
                             'gesture flickering, 0 = disabled)',
                        type=float,
                        default=DEFAULT_GESTURE_COOLDOWN)
    parser.add_argument("--min_tracking_confidence",
                        help='min_tracking_confidence',
                        type=float,
                        default=0.5)

    args = parser.parse_args()

    return args


def main():
    # Argument parsing #################################################################
    args = get_args()

    number_of_hands = args.num_hands
    if number_of_hands < NUM_HANDS:
        print("--num_hands {} < {}: a gesture sample needs {} hands, "
              "raising to {}.".format(number_of_hands, NUM_HANDS, NUM_HANDS,
                                      NUM_HANDS))
        number_of_hands = NUM_HANDS
    cap_device = args.device
    cap_width = args.width
    cap_height = args.height

    model_path = args.model_path
    classifier_path = args.classifier_path
    labels_path = args.labels_path
    min_detection_confidence = args.min_detection_confidence
    min_tracking_confidence = args.min_tracking_confidence
    height_diff_threshold = args.height_diff_threshold
    rest_zone = RestZone(radius=args.rest_zone_radius,
                         y_offset=args.rest_zone_y_offset)

    # --no_classification: run the app WITHOUT the keypoint classifier
    use_classifier = not args.no_classification

    use_brect = True

    # Camera preparation ###############################################################
    cap = cv.VideoCapture(cap_device)
    cap.set(cv.CAP_PROP_FRAME_WIDTH, cap_width)
    cap.set(cv.CAP_PROP_FRAME_HEIGHT, cap_height)

    # Model load #############################################################
    hand_landmarker_options = vision.HandLandmarkerOptions(
        base_options=mp_tasks.BaseOptions(model_asset_path=model_path),
        running_mode=vision.RunningMode.VIDEO,
        num_hands=number_of_hands,
        min_hand_detection_confidence=min_detection_confidence,
        min_hand_presence_confidence=min_detection_confidence,
        min_tracking_confidence=min_tracking_confidence,
    )
    hand_landmarker = vision.HandLandmarker.create_from_options(
        hand_landmarker_options)

    # Read labels ###########################################################
    try:
        keypoint_classifier_labels = load_labels(labels_path)
    except (OSError, ValueError) as error:
        raise SystemExit(str(error))

    # Gesture classes are numbered 1..N (0 = not chosen yet)
    num_categories = len(keypoint_classifier_labels)
    inactive_label = ('inactive'
                      if 'inactive' in keypoint_classifier_labels
                      else keypoint_classifier_labels[-1])

    # Classifier + two-hand threshold #######################################
    if use_classifier:
        if not os.path.isfile(classifier_path):
            raise SystemExit(
                "Keypoint classifier not found: {} "
                "(train it first with keypoint_classification_EN.ipynb, or "
                "run with --no_classification to start without it).".format(
                    classifier_path))
        keypoint_classifier = load_keypoint_classifier(classifier_path)
        print("Keypoint classifier loaded: {}".format(classifier_path))
    else:
        keypoint_classifier = None
        print("--no_classification: running WITHOUT the keypoint "
              "classifier (detection + recording only).")

    gesture_classifier = TwoHandGestureClassifier(
        classifier=keypoint_classifier,
        labels=keypoint_classifier_labels,
        inactive_label=inactive_label,
        height_diff_threshold=height_diff_threshold,
        rest_zone=rest_zone,
        gesture_cooldown=args.gesture_cooldown,
    )

    # FPS Measurement ########################################################
    cvFpsCalc = CvFpsCalc(buffer_len=10)

    # Gamepad ####################################################
    gamepad = None
    if pygame is not None:
        pygame.init()
        pygame.joystick.init()
        if pygame.joystick.get_count() > 0:
            gamepad = pygame.joystick.Joystick(0)
            print("Gamepad connected: {}".format(gamepad.get_name()))
        else:
            print("No gamepad detected - keyboard control only.")
    elif pygame is None:
        print("pygame not installed - keyboard control only.")

    mode = 0
    category = 0            # movement category 1-N (0 = not chosen yet)
    prev_bumper = False     # bumper edge detection
    prev_hat = (0, 0)       # d-pad edge detection
    capture_pending = False # one-frame capture requested by bumper
    captured_count = 0      # frames saved since start
    capture_flash_until = 0.0
    capture_missing_hand_until = 0.0

    # Timestamps for the VIDEO running mode must be monotonically increasing
    start_time = time.monotonic()
    last_timestamp_ms = -1

    while True:
        fps = cvFpsCalc.get()

        # Process Key (ESC: end) #################################################
        key = cv.waitKey(10)
        if key == 27:  # ESC
            break
        number, mode = select_mode(key, mode)
        if 1 <= number <= 9:  # keyboard fallback for category selection
            category = min(number, num_categories)

        # Gamepad input ##########################################################
        if gamepad is not None:
            pygame.event.pump()

            # D-pad up/down: choose movement category (remembered + shown)
            hat = gamepad.get_hat(0)
            if hat[1] == 1 and prev_hat[1] != 1:
                category = (1 if category >= num_categories
                            else category + 1)
            elif hat[1] == -1 and prev_hat[1] != -1:
                category = (num_categories if category <= 1
                            else category - 1)
            prev_hat = hat

            # Bumper press (LB or RB): capture exactly one frame
            bumper = any(
                gamepad.get_button(b) for b in BUMPER_BUTTONS
                if b < gamepad.get_numbuttons())
            if bumper and not prev_bumper:
                if mode == 1:
                    capture_pending = True
                else:
                    print("Bumper pressed, but mode is not 1 "
                          "(press 'k' to start recording).")
            prev_bumper = bumper

        # Camera capture #####################################################
        ret, image = cap.read()
        if not ret:
            break
        image = cv.flip(image, 1)  # Mirror display
        debug_image = copy.deepcopy(image)

        # Detection implementation #############################################################
        timestamp_ms = int((time.monotonic() - start_time) * 1000)
        if timestamp_ms <= last_timestamp_ms:
            timestamp_ms = last_timestamp_ms + 1
        last_timestamp_ms = timestamp_ms

        rgb_image = cv.cvtColor(image, cv.COLOR_BGR2RGB)
        mp_image = mp.Image(image_format=mp.ImageFormat.SRGB, data=rgb_image)
        results = hand_landmarker.detect_for_video(mp_image, timestamp_ms)

        #  ####################################################################
        # Collect every detected hand first, then treat them as ONE sample:
        # one gesture = two hand graphs (84 features) stored in one class.
        hands = []
        if results.hand_landmarks:
            for hand_landmarks, handedness in zip(results.hand_landmarks,
                                                  results.handedness):
                brect = calc_bounding_rect(debug_image, hand_landmarks)
                hands.append(HandSample.from_mediapipe(
                    hand_landmarks,
                    handedness[0].category_name,
                    debug_image.shape[1],
                    debug_image.shape[0],
                    brect=brect,
                ))
        prediction = gesture_classifier.classify(hands)

        both_hands_visible = prediction.both_hands_visible
        hand_sign_text = prediction.label

        # Write to the dataset file
        # One frame per bumper press, class id = category - 1 (0-4)
        # Requires BOTH hands so every row keeps the same 84-feature shape.
        if capture_pending and mode == 1 and 1 <= category <= num_categories:
            if both_hands_visible and len(prediction.features) == NUM_FEATURES:
                logging_csv(category - 1, prediction.features)
                capture_pending = False
                captured_count += 1
                capture_flash_until = time.monotonic() + 0.6
                print("Saved frame -> category {} (class {}) (total {})"
                      .format(category, category - 1, captured_count))
            else:
                capture_missing_hand_until = time.monotonic() + 0.6

        # Drawing part
        debug_image = draw_rest_zone(debug_image, rest_zone, prediction)
        for hand in hands:
            debug_image = draw_bounding_rect(use_brect, debug_image,
                                             hand.brect)
            debug_image = draw_landmarks(debug_image, hand.landmark_list)
            debug_image = draw_info_text(
                debug_image,
                hand.brect,
                hand.handedness,
                hand_sign_text
                if prediction.classified
                and (prediction.held or prediction.height_threshold_met)
                else '',
            )
        debug_image = draw_hand_centers(debug_image, prediction)

        debug_image = draw_info(debug_image, fps, mode, number, category,
                                captured_count,
                                time.monotonic() < capture_flash_until,
                                capture_pending,
                                hand_sign_text,
                                time.monotonic() < capture_missing_hand_until,
                                both_hands_visible,
                                keypoint_classifier_labels,
                                hand_heights=[hand.height for hand in
                                              hands[:NUM_HANDS]],
                                height_diff=prediction.height_diff,
                                height_threshold=prediction.height_diff_threshold,
                                height_threshold_met=prediction.height_threshold_met,
                                in_rest_zone=prediction.in_rest_zone,
                                gesture_active=prediction.active,
                                held_label=(prediction.raw_label
                                            if prediction.held else None),
                                cooldown_remaining=prediction.cooldown_remaining)

        # Screen reflection #############################################################
        cv.imshow('Hand Gesture Recognition', debug_image)

    cap.release()
    hand_landmarker.close()
    cv.destroyAllWindows()
    if gamepad is not None:
        gamepad.quit()
    if pygame is not None:
        pygame.quit()


def select_mode(key, mode):
    number = -1
    if 48 <= key <= 57:  # 0 ~ 9
        number = key - 48
    if key == 110:  # n
        mode = 0
    if key == 107:  # k
        mode = 1
    return number, mode


def calc_bounding_rect(image, landmarks):
    image_width, image_height = image.shape[1], image.shape[0]

    landmark_array = np.empty((0, 2), int)

    for _, landmark in enumerate(landmarks):
        landmark_x = min(int(landmark.x * image_width), image_width - 1)
        landmark_y = min(int(landmark.y * image_height), image_height - 1)

        landmark_point = [np.array((landmark_x, landmark_y))]

        landmark_array = np.append(landmark_array, landmark_point, axis=0)

    x, y, w, h = cv.boundingRect(landmark_array)

    return [x, y, x + w, y + h]


def logging_csv(category, landmark_list):
    if len(landmark_list) != NUM_FEATURES:
        raise ValueError(
            "Refusing to save sample with {} features (expected {} = "
            "{} hands x {} landmarks x 2).".format(
                len(landmark_list), NUM_FEATURES, NUM_HANDS,
                LANDMARKS_PER_HAND))
    csv_path = 'model/keypoint_classifier/keypoint.csv'
    with open(csv_path, 'a', newline="") as f:
        writer = csv.writer(f)
        writer.writerow([category, *landmark_list])
    return


def draw_landmarks(image, landmark_point):
    if len(landmark_point) > 0:
        # Thumb
        cv.line(image, tuple(landmark_point[2]), tuple(landmark_point[3]),
                (0, 0, 0), 6)
        cv.line(image, tuple(landmark_point[2]), tuple(landmark_point[3]),
                (255, 255, 255), 2)
        cv.line(image, tuple(landmark_point[3]), tuple(landmark_point[4]),
                (0, 0, 0), 6)
        cv.line(image, tuple(landmark_point[3]), tuple(landmark_point[4]),
                (255, 255, 255), 2)

        # Index finger
        cv.line(image, tuple(landmark_point[5]), tuple(landmark_point[6]),
                (0, 0, 0), 6)
        cv.line(image, tuple(landmark_point[5]), tuple(landmark_point[6]),
                (255, 255, 255), 2)
        cv.line(image, tuple(landmark_point[6]), tuple(landmark_point[7]),
                (0, 0, 0), 6)
        cv.line(image, tuple(landmark_point[6]), tuple(landmark_point[7]),
                (255, 255, 255), 2)
        cv.line(image, tuple(landmark_point[7]), tuple(landmark_point[8]),
                (0, 0, 0), 6)
        cv.line(image, tuple(landmark_point[7]), tuple(landmark_point[8]),
                (255, 255, 255), 2)

        # Middle finger
        cv.line(image, tuple(landmark_point[9]), tuple(landmark_point[10]),
                (0, 0, 0), 6)
        cv.line(image, tuple(landmark_point[9]), tuple(landmark_point[10]),
                (255, 255, 255), 2)
        cv.line(image, tuple(landmark_point[10]), tuple(landmark_point[11]),
                (0, 0, 0), 6)
        cv.line(image, tuple(landmark_point[10]), tuple(landmark_point[11]),
                (255, 255, 255), 2)
        cv.line(image, tuple(landmark_point[11]), tuple(landmark_point[12]),
                (0, 0, 0), 6)
        cv.line(image, tuple(landmark_point[11]), tuple(landmark_point[12]),
                (255, 255, 255), 2)

        # Ring finger
        cv.line(image, tuple(landmark_point[13]), tuple(landmark_point[14]),
                (0, 0, 0), 6)
        cv.line(image, tuple(landmark_point[13]), tuple(landmark_point[14]),
                (255, 255, 255), 2)
        cv.line(image, tuple(landmark_point[14]), tuple(landmark_point[15]),
                (0, 0, 0), 6)
        cv.line(image, tuple(landmark_point[14]), tuple(landmark_point[15]),
                (255, 255, 255), 2)
        cv.line(image, tuple(landmark_point[15]), tuple(landmark_point[16]),
                (0, 0, 0), 6)
        cv.line(image, tuple(landmark_point[15]), tuple(landmark_point[16]),
                (255, 255, 255), 2)

        # Little finger
        cv.line(image, tuple(landmark_point[17]), tuple(landmark_point[18]),
                (0, 0, 0), 6)
        cv.line(image, tuple(landmark_point[17]), tuple(landmark_point[18]),
                (255, 255, 255), 2)
        cv.line(image, tuple(landmark_point[18]), tuple(landmark_point[19]),
                (0, 0, 0), 6)
        cv.line(image, tuple(landmark_point[18]), tuple(landmark_point[19]),
                (255, 255, 255), 2)
        cv.line(image, tuple(landmark_point[19]), tuple(landmark_point[20]),
                (0, 0, 0), 6)
        cv.line(image, tuple(landmark_point[19]), tuple(landmark_point[20]),
                (255, 255, 255), 2)

        # Palm
        cv.line(image, tuple(landmark_point[0]), tuple(landmark_point[1]),
                (0, 0, 0), 6)
        cv.line(image, tuple(landmark_point[0]), tuple(landmark_point[1]),
                (255, 255, 255), 2)
        cv.line(image, tuple(landmark_point[1]), tuple(landmark_point[2]),
                (0, 0, 0), 6)
        cv.line(image, tuple(landmark_point[1]), tuple(landmark_point[2]),
                (255, 255, 255), 2)
        cv.line(image, tuple(landmark_point[2]), tuple(landmark_point[5]),
                (0, 0, 0), 6)
        cv.line(image, tuple(landmark_point[2]), tuple(landmark_point[5]),
                (255, 255, 255), 2)
        cv.line(image, tuple(landmark_point[5]), tuple(landmark_point[9]),
                (0, 0, 0), 6)
        cv.line(image, tuple(landmark_point[5]), tuple(landmark_point[9]),
                (255, 255, 255), 2)
        cv.line(image, tuple(landmark_point[9]), tuple(landmark_point[13]),
                (0, 0, 0), 6)
        cv.line(image, tuple(landmark_point[9]), tuple(landmark_point[13]),
                (255, 255, 255), 2)
        cv.line(image, tuple(landmark_point[13]), tuple(landmark_point[17]),
                (0, 0, 0), 6)
        cv.line(image, tuple(landmark_point[13]), tuple(landmark_point[17]),
                (255, 255, 255), 2)
        cv.line(image, tuple(landmark_point[17]), tuple(landmark_point[0]),
                (0, 0, 0), 6)
        cv.line(image, tuple(landmark_point[17]), tuple(landmark_point[0]),
                (255, 255, 255), 2)

    # Key Points
    for index, landmark in enumerate(landmark_point):
        radius = 8 if index in (4, 8, 12, 16, 20) else 5  # fingertips are bigger
        cv.circle(image, (landmark[0], landmark[1]), radius, (255, 255, 255),
                  -1)
        cv.circle(image, (landmark[0], landmark[1]), radius, (0, 0, 0), 1)

    return image


def draw_bounding_rect(use_brect, image, brect):
    if use_brect:
        # Outer rectangle
        cv.rectangle(image, (brect[0], brect[1]), (brect[2], brect[3]),
                     (0, 0, 0), 1)

    return image


def draw_info_text(image, brect, handedness_label, hand_sign_text):
    cv.rectangle(image, (brect[0], brect[1]), (brect[2], brect[1] - 22),
                 (0, 0, 0), -1)

    info_text = handedness_label[0:]
    if hand_sign_text != "":
        info_text = info_text + ':' + hand_sign_text
    cv.putText(image, info_text, (brect[0] + 5, brect[1] - 4),
               cv.FONT_HERSHEY_SIMPLEX, 0.6, (255, 255, 255), 1, cv.LINE_AA)

    return image


def draw_info(image, fps, mode, number, category, captured_count, just_saved,
              capture_pending, gesture_text, missing_hand, both_hands_visible,
              category_labels=None, hand_heights=None, height_diff=0.0,
              height_threshold=0, height_threshold_met=True,
              in_rest_zone=False, gesture_active=None, held_label=None,
              cooldown_remaining=0.0):
    cv.putText(image, "FPS:" + str(fps), (10, 30), cv.FONT_HERSHEY_SIMPLEX,
               1.0, (0, 0, 0), 4, cv.LINE_AA)
    cv.putText(image, "FPS:" + str(fps), (10, 30), cv.FONT_HERSHEY_SIMPLEX,
               1.0, (255, 255, 255), 2, cv.LINE_AA)

    if mode == 1:
        cv.putText(image, "MODE:Logging Key Point", (10, 90),
                   cv.FONT_HERSHEY_SIMPLEX, 0.6, (255, 255, 255), 1,
                   cv.LINE_AA)
        if 0 <= number <= 9:
            cv.putText(image, "NUM:" + str(number), (10, 110),
                       cv.FONT_HERSHEY_SIMPLEX, 0.6, (255, 255, 255), 1,
                       cv.LINE_AA)

    # Movement category (1-N), remembered between frames
    if category > 0:
        category_text = "CATEGORY:" + str(category)
        if category_labels and category <= len(category_labels):
            category_text += " " + category_labels[category - 1]
        cv.putText(image, category_text, (10, 140),
                   cv.FONT_HERSHEY_SIMPLEX, 0.9, (0, 0, 0), 4, cv.LINE_AA)
        cv.putText(image, category_text, (10, 140),
                   cv.FONT_HERSHEY_SIMPLEX, 0.9, (0, 255, 255), 2,
                   cv.LINE_AA)
    else:
        cv.putText(image, "CATEGORY: - (d-pad up/down)", (10, 140),
                   cv.FONT_HERSHEY_SIMPLEX, 0.6, (0, 0, 0), 4, cv.LINE_AA)
        cv.putText(image, "CATEGORY: - (d-pad up/down)", (10, 140),
                   cv.FONT_HERSHEY_SIMPLEX, 0.6, (255, 255, 255), 1,
                   cv.LINE_AA)

    # Vertical position of both hands + distance between them
    if hand_heights:
        height_text = "HANDS Y:" + "/".join("{:.0f}".format(h)
                                             for h in hand_heights)
        if both_hands_visible:
            height_text += "  dY:{:.0f}px".format(height_diff)
            if height_threshold > 0:
                height_text += "/{}".format(height_threshold)
        cv.putText(image, height_text, (10, 60), cv.FONT_HERSHEY_SIMPLEX, 0.6,
                   (0, 0, 0), 4, cv.LINE_AA)
        same_height = (not both_hands_visible) or height_threshold_met
        cv.putText(image, height_text, (10, 60), cv.FONT_HERSHEY_SIMPLEX, 0.6,
                   (255, 255, 255) if same_height else (0, 165, 255), 1,
                   cv.LINE_AA)

    # Recognized gesture (both hands combined into one sample)
    gesture_line = "GESTURE:" + gesture_text
    if in_rest_zone and held_label is None:
        gesture_line += " (rest zone)"
    if gesture_active is None:
        gesture_active = (both_hands_visible and height_threshold_met
                          and not in_rest_zone)
    cv.putText(image, gesture_line, (10, 175), cv.FONT_HERSHEY_SIMPLEX, 0.9,
               (0, 0, 0), 4, cv.LINE_AA)
    cv.putText(image, gesture_line, (10, 175), cv.FONT_HERSHEY_SIMPLEX, 0.9,
               (0, 255, 0) if gesture_active else (0, 165, 255), 2,
               cv.LINE_AA)

    # Gesture change blocked by --gesture_cooldown
    if held_label is not None:
        hold_text = "HOLD {:.1f}s (next: {})".format(cooldown_remaining,
                                                    held_label)
        (gesture_w, _), _ = cv.getTextSize(gesture_line,
                                           cv.FONT_HERSHEY_SIMPLEX, 0.9, 2)
        hold_org = (10 + gesture_w + 15, 175)
        cv.putText(image, hold_text, hold_org, cv.FONT_HERSHEY_SIMPLEX, 0.6,
                   (0, 0, 0), 4, cv.LINE_AA)
        cv.putText(image, hold_text, hold_org, cv.FONT_HERSHEY_SIMPLEX, 0.6,
                   (255, 255, 0), 1, cv.LINE_AA)

    # Capture status
    if just_saved:
        cv.putText(image, "SAVED!", (10, 210), cv.FONT_HERSHEY_SIMPLEX, 1.0,
                   (0, 0, 0), 4, cv.LINE_AA)
        cv.putText(image, "SAVED!", (10, 210), cv.FONT_HERSHEY_SIMPLEX, 1.0,
                   (0, 255, 0), 2, cv.LINE_AA)
    elif capture_pending and mode == 1:
        if missing_hand:
            waiting_text = "both hands required!"
        else:
            waiting_text = "waiting for both hands..."
        cv.putText(image, waiting_text, (10, 210),
                   cv.FONT_HERSHEY_SIMPLEX, 0.6, (0, 0, 0), 4, cv.LINE_AA)
        cv.putText(image, waiting_text, (10, 210),
                   cv.FONT_HERSHEY_SIMPLEX, 0.6, (0, 255, 255), 1,
                   cv.LINE_AA)

    if captured_count > 0:
        cv.putText(image, "SAVED FRAMES:" + str(captured_count), (10, 240),
                   cv.FONT_HERSHEY_SIMPLEX, 0.6, (0, 0, 0), 4, cv.LINE_AA)
        cv.putText(image, "SAVED FRAMES:" + str(captured_count), (10, 240),
                   cv.FONT_HERSHEY_SIMPLEX, 0.6, (255, 255, 255), 1,
                   cv.LINE_AA)

    return image


if __name__ == '__main__':
    main()
