from Plugins.AdaptiveCruiseControl.settings import settings
import numpy as np
import logging

MU = settings.MU
G = 9.81  # Gravitational acceleration (m/s^2)
MIN_DISTANCE = 30  # Distance at which curvature affects 100%


def ListenSettings():
    global MU
    MU = settings.MU


settings.listen(ListenSettings)


def distance_to_point(x1, y1, x2, y2):
    return np.sqrt((x2 - x1) ** 2 + (y2 - y1) ** 2)


def calculate_curvature(points, x, z, speed=25.0, mass=40000.0):
    """Calculate the curvature for each point in the road segment."""
    safe_decel = min(3.0, 100000 / max(1000, mass))
    max_distance = speed * 2 + (speed ** 2) / (2 * safe_decel)
    max_distance = max(max_distance, MIN_DISTANCE + 10)

    curvatures = []
    for i in range(1, len(points) - 1):
        # Direction vectors
        v1 = np.array(
            [points[i][0] - points[i - 1][0], points[i][2] - points[i - 1][2]]
        )
        v2 = np.array(
            [points[i + 1][0] - points[i][0], points[i + 1][2] - points[i][2]]
        )

        # Angle change
        dot_product = np.dot(v1, v2)
        norm_v1 = np.linalg.norm(v1)
        norm_v2 = np.linalg.norm(v2)
        norm_product = norm_v1 * norm_v2
        if norm_product == 0:
            continue  # Skip to avoid division by zero
        cos_angle = dot_product / norm_product
        cos_angle = np.clip(cos_angle, -1, 1)  # Ensure valid arccos input
        delta_theta = np.arccos(cos_angle)

        # Arc length (average of the two segment lengths)
        delta_s = (norm_v1 + norm_v2) / 2

        # Curvature (1/m)
        if delta_s == 0:
            continue  # Skip to avoid division by zero
        kappa = delta_theta / delta_s

        distance = distance_to_point(x, z, points[i][0], points[i][2])
        multiplier = 1 - (distance - MIN_DISTANCE) / (max_distance - MIN_DISTANCE)
        multiplier = np.clip(multiplier, 0, 1)

        curvatures.append(kappa * multiplier)

    return curvatures


def get_maximum_speed_for_points(points, x, z, speed=25.0, mass=40000.0) -> float:
    """Calculate the maximum safe speed based on road curvature."""
    if len(points) < 3:  # Need at least 3 points to calculate curvature
        return 999

    try:
        # Calculate curvatures for all points
        curvatures = calculate_curvature(points, x, z, speed, mass)

        if not curvatures:
            return 999

        max_curvature = max(curvatures)
        if max_curvature == 0:
            return 999

        try:
            max_speed = np.sqrt(MU * G / max_curvature)  # In m/s
        except Exception:
            return 999

        if max_speed == 0:
            return 999

        return max_speed

    except Exception:
        logging.exception("Failed to calculate maximum speed")
        return 999
