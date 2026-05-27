from ETS2LA.Plugin import ETS2LAPlugin, PluginDescription, Author
from ETS2LA.Utils.translator import _

from Modules.Traffic.classes import Vehicle, Position
from Plugins.AdaptiveCruiseControl.speed import get_maximum_speed_for_points
from Plugins.Map.utils.math_helpers import IsInFront
from Plugins.AR.classes import Coordinate, Color, Circle, Line

from Plugins.CollisionAvoidance.settings import settings
from Plugins.CollisionAvoidance.ui import SettingsPage

import math


class Plugin(ETS2LAPlugin):
    description = PluginDescription(
        name=_("Collision Avoidance"),
        version="1.0",
        description=_("Provide collision avoidance when inside intersections."),
        modules=["Traffic", "TruckSimAPI"],
        tags=["Base", "Speed Control"],
        listen=["*.py"],
        fps_cap=2,
    )

    author = Author(
        name="Tumppi066",
        url="https://github.com/Tumppi066",
        icon="https://avatars.githubusercontent.com/u/83072683?v=4",
    )

    pages = [SettingsPage]

    def init(self): ...

    def get_intersecting_vehicles(
        self, api, points: list[tuple[float, float, float]]
    ) -> tuple[list[Vehicle], list[tuple[float, float, float]]]:
        vehicles: list[Vehicle] = self.modules.Traffic.run()
        if not vehicles:
            return [], []

        rotationX = api["truckPlacement"]["rotationX"]
        angle = rotationX * 360
        if angle < 0:
            angle = 360 + angle
        truck_rotation = math.radians(angle)
        truck_position = (
            api["truckPlacement"]["coordinateX"],
            api["truckPlacement"]["coordinateY"],
            api["truckPlacement"]["coordinateZ"],
        )

        sensitivity = settings.sensitivity
        intersecting_vehicles = []
        impacts = []
        for vehicle in vehicles:
            if not IsInFront(vehicle.position.tuple(), truck_rotation, truck_position):
                continue  # only consider vehicles in front of the truck

            path: list[Position] = vehicle.get_path_for(settings.lookahead_time)
            if not path:
                continue

            start_to_end = math.sqrt(
                (path[0].x - path[-1].x) ** 2
                + (path[0].y - path[-1].y) ** 2
                + (path[0].z - path[-1].z) ** 2
            )
            if start_to_end < 2:
                continue  # not moving enough

            path = path[::2]  # 1/2 density for performance
            closest_distance = 999
            closest_index = -1
            for point in path:
                point = point.tuple()
                closest_this_point = 999
                for i, steering_point in enumerate(points):
                    if i > 20:
                        continue

                    distance = math.sqrt(
                        (point[0] - steering_point[0]) ** 2
                        + (point[1] - steering_point[1]) ** 2
                        + (point[2] - steering_point[2]) ** 2
                    )
                    if distance < closest_this_point:
                        closest_this_point = distance
                    if distance < closest_distance:
                        closest_distance = distance
                        closest_index = i
                        if (
                            steering_point not in impacts
                            and distance < sensitivity * closest_index
                        ):
                            impacts.append(steering_point)

                if closest_this_point > closest_distance + 5:
                    break  # moving away from the truck

            if closest_distance < sensitivity * closest_index:
                intersecting_vehicles.append(vehicle)

        return intersecting_vehicles, impacts

    def reset_tags(self) -> None:
        self.tags.stop_in = -1
        self.tags.vehicle_highlights = []
        self.tags.AR = []

    notified_enabled = False
    notified_disabled = False

    def run(self):
        api = self.modules.TruckSimAPI.run()
        if api["truckFloat"]["speed"] > settings.max_speed / 3.6:
            if not self.notified_disabled:
                self.notify(_("Collision avoidance disabled"), "error")
                self.notified_disabled = True
                self.notified_enabled = False

            self.reset_tags()
            return  # only run under 30kph

        if not self.notified_enabled:
            self.notify(_("Collision avoidance enabled"), "success")
            self.notified_enabled = True
            self.notified_disabled = False

        points = self.tags.steering_points
        points = self.tags.merge(points)
        if not points:
            return  # no steering points available

        truck_speed = api["truckFloat"]["speed"]
        mass = 10000 + api["configFloat"]["cargoMass"]
        max_speed = get_maximum_speed_for_points(
            points,
            api["truckPlacement"]["coordinateX"],
            api["truckPlacement"]["coordinateZ"],
            truck_speed,
            mass
        )

        if max_speed > settings.max_speed / 3.6:
            self.reset_tags()
            return  # not in a tight enough turn

        vehicles, impacts = self.get_intersecting_vehicles(api, points)

        if vehicles:
            self.tags.vehicle_highlights = [v.id for v in vehicles]
        else:
            self.reset_tags()
            return

        closest_hit_index = 999
        for i, point in enumerate(points):
            if point in impacts:
                closest_hit_index = i
                break

        if closest_hit_index < 999:
            # Use an earlier point to make sure we stop in time.
            if closest_hit_index > 2:
                closest_hit_index -= 2
            
            distance = math.sqrt(
                (points[closest_hit_index][0] - api["truckPlacement"]["coordinateX"])
                ** 2
                + (points[closest_hit_index][1] - api["truckPlacement"]["coordinateY"])
                ** 2
                + (points[closest_hit_index][2] - api["truckPlacement"]["coordinateZ"])
                ** 2
            )
            self.tags.stop_in = distance if closest_hit_index < 999 else -1
        else:
            self.tags.stop_in = -1

        if not settings.visualize:
            return

        ar_data = []
        for i, point in enumerate(points):
            if i == 0:
                continue

            has_stop_line = i == closest_hit_index - 2
            has_hit = i >= closest_hit_index - 1

            if has_stop_line:
                # vector to the next point
                vec = (
                    points[i][0] - points[i - 1][0],
                    points[i][1] - points[i - 1][1],
                    points[i][2] - points[i - 1][2],
                )
                length = math.sqrt(vec[0] ** 2 + vec[1] ** 2 + vec[2] ** 2)
                if length == 0:
                    length = 0.0001
                vec = (vec[0] / length, vec[1] / length, vec[2] / length)

                # now horizontal
                perp = (-vec[2], 0, vec[0])
                perp_length = math.sqrt(perp[0] ** 2 + perp[1] ** 2 + perp[2] ** 2)
                if perp_length == 0:
                    perp_length = 0.0001
                perp = (
                    perp[0] / perp_length,
                    perp[1] / perp_length,
                    perp[2] / perp_length,
                )

                ar_data.append(
                    Line(
                        start=Coordinate(
                            x=points[i][0] + perp[0] * 2,
                            y=points[i][1] + perp[1] * 2,
                            z=points[i][2] + perp[2] * 2,
                        ),
                        end=Coordinate(
                            x=points[i][0] - perp[0] * 2,
                            y=points[i][1] - perp[1] * 2,
                            z=points[i][2] - perp[2] * 2,
                        ),
                        color=Color(r=255, g=100, b=100, a=100),
                        thickness=10,
                    )
                )

            ar_data.append(
                Line(
                    start=Coordinate(
                        x=points[i - 1][0],
                        y=points[i - 1][1],
                        z=points[i - 1][2],
                    ),
                    end=Coordinate(
                        x=point[0],
                        y=point[1],
                        z=point[2],
                    ),
                    color=Color(r=100, g=255, b=100, a=100)
                    if not has_hit
                    else Color(r=255, g=100, b=100, a=100),
                    thickness=5,
                )
            )

        for point in impacts:
            ar_data.append(
                Circle(
                    center=Coordinate(
                        x=point[0],
                        y=point[1],
                        z=point[2],
                    ),
                    radius=12,
                    fill=Color(r=255, g=100, b=100, a=50),
                    color=Color(r=255, g=100, b=100, a=100),
                )
            )

        self.tags.AR = ar_data
