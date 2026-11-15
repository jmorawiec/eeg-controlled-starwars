# -*- coding: utf-8 -*-
"""Rysowanie strefy spoczynku (RestZone) na podglądzie OpenCV.

Używane przez ``app.py`` i ``integrations/gesture_pipeline.py``.
"""
import cv2 as cv

NUM_HANDS = 2

REST_ZONE_COLOR_EMPTY = (200, 200, 200)   # no hand inside
REST_ZONE_COLOR_PARTIAL = (0, 255, 255)   # one hand inside
REST_ZONE_COLOR_FULL = (0, 0, 255)        # both hands inside -> inactive


def draw_rest_zone(image, rest_zone, prediction, alpha=0.25):
    """Draws the half-circle rest zone at the bottom of the frame.

    Colour: grey = empty, yellow = one hand inside, red = both hands
    inside (gesture forced to inactive).
    """
    if not rest_zone.enabled:
        return image

    image_height, image_width = image.shape[0], image.shape[1]
    center = rest_zone.center(image_width, image_height)

    hands_inside = sum(prediction.hands_in_rest_zone)
    if prediction.in_rest_zone:
        color = REST_ZONE_COLOR_FULL
    elif hands_inside > 0:
        color = REST_ZONE_COLOR_PARTIAL
    else:
        color = REST_ZONE_COLOR_EMPTY

    # Semi-transparent fill (the circle center may lie below the frame,
    # OpenCV clips it automatically).
    overlay = image.copy()
    cv.circle(overlay, center, rest_zone.radius, color, -1, cv.LINE_AA)
    cv.addWeighted(overlay, alpha, image, 1 - alpha, 0, dst=image)

    # Outline
    cv.circle(image, center, rest_zone.radius, (0, 0, 0), 4, cv.LINE_AA)
    cv.circle(image, center, rest_zone.radius, color, 2, cv.LINE_AA)

    # Label just above the top of the arc
    label = "REST ZONE {}/{}".format(hands_inside, NUM_HANDS)
    (text_w, _), _ = cv.getTextSize(label, cv.FONT_HERSHEY_SIMPLEX, 0.6, 2)
    text_org = (center[0] - text_w // 2,
                max(20, center[1] - rest_zone.radius - 8))
    cv.putText(image, label, text_org, cv.FONT_HERSHEY_SIMPLEX, 0.6,
               (0, 0, 0), 4, cv.LINE_AA)
    cv.putText(image, label, text_org, cv.FONT_HERSHEY_SIMPLEX, 0.6,
               color, 2, cv.LINE_AA)

    return image


def draw_hand_centers(image, prediction):
    """Marks the point used for the rest-zone test (hand center)."""
    for index, hand in enumerate(prediction.hands):
        if not hand.landmark_list:
            continue
        inside = (index < len(prediction.hands_in_rest_zone)
                  and prediction.hands_in_rest_zone[index])
        center = (int(hand.center[0]), int(hand.center[1]))
        cv.circle(image, center, 10, (0, 0, 0), 3, cv.LINE_AA)
        cv.circle(image, center, 10,
                  REST_ZONE_COLOR_FULL if inside else (0, 255, 0), 2,
                  cv.LINE_AA)
        cv.drawMarker(image, center, (255, 255, 255), cv.MARKER_CROSS, 12, 1)

    return image
