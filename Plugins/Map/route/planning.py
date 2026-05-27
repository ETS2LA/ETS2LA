import Plugins.Map.utils.math_helpers as math_helpers
from Plugins.Map.navigation.classes import RouteNode
import Plugins.Map.utils.road_helpers as rh
import Plugins.Map.utils.node_helpers as nh
import Plugins.Map.route.classes as rc
import Plugins.Map.data as data
import Plugins.Map.classes as c
import importlib
import logging
import math

importlib.reload(rc)

def GetRoadsBehindRoad(road: c.Road, include_self: bool = True) -> list[c.Road]:
    if include_self:
        roads = [road]
    else:
        roads = []

    node = data.map.get_node_by_uid(road.start_node_uid)
    if node is not None:
        if node.backward_item_uid != road.uid:
            item = data.map.get_item_by_uid(node.backward_item_uid)
            if isinstance(item, c.Road):
                if len(item.lanes) == len(road.lanes):
                    roads += GetRoadsBehindRoad(item)

    return roads


def GetRoadsInFrontOfRoad(road: c.Road, include_self: bool = True) -> list[c.Road]:
    if include_self:
        roads = [road]
    else:
        roads = []

    node = data.map.get_node_by_uid(road.end_node_uid)
    if node is not None:
        if node.forward_item_uid != road.uid:
            item = data.map.get_item_by_uid(node.forward_item_uid)
            if isinstance(item, c.Road):
                if len(item.lanes) == len(road.lanes):
                    roads += GetRoadsInFrontOfRoad(item)

    return roads


def PrefabToRouteSection(
    prefab: c.Prefab, lane_index: int, invert: bool = False, path_index: int = 0
) -> rc.RouteSection:
    route_section = rc.RouteSection()
    route_item = rc.RouteItem()
    route_item.item = prefab
    route_item.lane_index = lane_index
    route_section.items = [route_item]
    route_section.invert = invert
    route_section.lane_index = lane_index
    route_section.path_index = path_index
    points = route_section.lane_points
    if len(points) == 0:
        return None

    return route_section


def RoadToRouteSection(
    road: c.Road,
    lane_index: int,
    target_lanes: list[int] = None,
    invert: bool = False,
    path_index: int = 0,
) -> rc.RouteSection:
    if target_lanes is None:
        target_lanes = []

    route_section = rc.RouteSection()
    route_section.items = []

    behind_roads = GetRoadsBehindRoad(road, include_self=False)
    forward_roads = GetRoadsInFrontOfRoad(road, include_self=False)

    roads = behind_roads[::-1] + [road] + forward_roads
    accepted_roads = []
    for road in roads:
        if road not in accepted_roads:
            accepted_roads.append(road)

    for list_road in accepted_roads:
        route_item = rc.RouteItem()
        route_item.item = list_road
        route_item.lane_index = lane_index
        route_section.items.append(route_item)

    route_section.invert = invert
    if lane_index > len(route_section.items[0].item.lanes) - 1:
        return None

    route_section.path_index = path_index
    route_section.lane_index = lane_index

    # Prioritize lanes (valid) on the right side of the road
    if route_section.distance_left(from_index=0) > 200 and target_lanes:
        side = route_section.items[0].item.lanes[lane_index].side
        if side == "left":
            target_lanes = [min(target_lanes)]
        else:
            target_lanes = [max(target_lanes)]

    route_section.target_lanes = target_lanes
    return route_section


def GetClosestRouteSection() -> rc.RouteSection:
    items: list[c.Prefab | c.Road] = []
    items += data.current_sector_prefabs
    items += data.current_sector_roads

    closest_item, closest_lane_id = get_closest_route_item(items)
    if closest_item is None:
        return None

    return (
        PrefabToRouteSection(closest_item, closest_lane_id)
        if isinstance(closest_item, c.Prefab)
        else RoadToRouteSection(closest_item, closest_lane_id)
    )


def GetClosestLanesForPrefab(next_item: c.Prefab, end_point: c.Position) -> list[int]:
    closest_lane_ids = []
    closest_point_distance = math.inf
    for lane_id, lane in enumerate(next_item.nav_routes):
        for point in lane.points:
            distance = math_helpers.DistanceBetweenPoints(
                (end_point.x, end_point.y, end_point.z), point.tuple()
            )
            if distance == closest_point_distance:
                closest_lane_ids.append(lane_id)
            elif distance < closest_point_distance:
                closest_lane_ids = [lane_id]
                closest_point_distance = distance

    accepted_lane_ids = []
    for closest_lane_id in closest_lane_ids:
        points = next_item.nav_routes[closest_lane_id].points
        start_node = None
        start_node_distance = math.inf
        for node in [
            data.map.get_node_by_uid(node_uid) for node_uid in next_item.node_uids
        ]:
            distance = math_helpers.DistanceBetweenPoints(
                (points[0].x, points[0].z), (node.x, node.y)
            )
            if distance < start_node_distance:
                start_node_distance = distance
                start_node = node
        end_node = None
        end_node_distance = math.inf
        for node in [
            data.map.get_node_by_uid(node_uid) for node_uid in next_item.node_uids
        ]:
            distance = math_helpers.DistanceBetweenPoints(
                (points[-1].x, points[-1].z), (node.x, node.y)
            )
            if distance < end_node_distance:
                end_node_distance = distance
                end_node = node

        if start_node != end_node:
            accepted_lane_ids.append(closest_lane_id)

    closest_lane_ids = accepted_lane_ids
    return closest_lane_ids


def IsRoadValid(road: c.Road) -> bool:
    if road.hidden:
        return False

    if road.lanes == []:
        return False

    if (
        "traffic_lane.no_vehicles" in road.road_look.lanes_left
        or "traffic_lane.no_vehicles" in road.road_look.lanes_right
    ):
        return False

    return True


def GetNextRouteSection(route: list[rc.RouteSection] = None) -> rc.RouteSection:
    if not route:
        route = data.route_plan

    if len(route) == 0:
        return None  # No route sections found

    current_section = route[-1]

    if isinstance(current_section.items[0].item, c.Road):
        end_node = current_section.end_node
        start_node = current_section.start_node
        end_node_is_in_front = math_helpers.IsInFront(
            (end_node.x, end_node.y), data.truck_rotation, (data.truck_x, data.truck_z)
        )
        start_node_is_in_front = math_helpers.IsInFront(
            (start_node.x, start_node.y),
            data.truck_rotation,
            (data.truck_x, data.truck_z),
        )

        next_item = None
        if end_node_is_in_front and start_node_is_in_front:
            return None  # We don't need to plan a new route section
        elif end_node_is_in_front:
            next_item = data.map.get_item_by_uid(end_node.forward_item_uid)
        elif start_node_is_in_front:
            next_item = data.map.get_item_by_uid(start_node.backward_item_uid)

        if next_item is None:
            return None  # No next item found

        if isinstance(next_item, c.Prefab):
            if current_section.last_actual_points == []:
                current_section.get_points()
            points = current_section.last_actual_points
            if len(points) == 0:
                return None

            closest_lane_ids = GetClosestLanesForPrefab(next_item, points[-1])

            if len(closest_lane_ids) == 0:
                return None
            elif len(closest_lane_ids) == 1:
                return PrefabToRouteSection(next_item, closest_lane_ids[0])

            # Verify that the next road piece is not hidden (not drivable in game)
            candidate_sections = [
                PrefabToRouteSection(next_item, lane_id) for lane_id in closest_lane_ids
            ]
            for section in candidate_sections:
                section.get_points()
            next_roads = [
                GetNextRouteSection(route + [section]) for section in candidate_sections
            ]

            verified_lane_ids = []
            for i, section in enumerate(next_roads):
                if section is not None:
                    if not isinstance(
                        section.items[0].item, c.Prefab
                    ) and not IsRoadValid(section.items[0].item):
                        continue
                    if isinstance(section.items[0].item, c.Prefab):
                        section.get_points()
                        next_section = GetNextRouteSection(route + [section])
                        if isinstance(next_section.items[0].item, c.Road):
                            if not IsRoadValid(next_section.items[0].item):
                                continue

                    verified_lane_ids.append(closest_lane_ids[i])

            if len(verified_lane_ids) == 0:
                return None

            closest_lane_ids = verified_lane_ids

            # Get the lane that is most in the direction of the truck
            end_positions = [
                next_item.nav_routes[lane_id].points[-1].tuple()
                for lane_id in closest_lane_ids
            ]
            direction = (
                "left"
                if data.truck_indicating_left
                else "right"
                if data.truck_indicating_right
                else "straight"
            )
            best_lane = math_helpers.GetMostInDirection(
                end_positions,
                data.truck_rotation,
                (data.truck_x, data.truck_y, data.truck_z),
                direction=direction,
            )

            target_end_position = end_positions[best_lane]
            best_lane = 0
            shortest_distance = math.inf
            for i, position in enumerate(end_positions):
                if position == target_end_position:
                    distance = next_item.nav_routes[closest_lane_ids[i]].distance
                    if distance < shortest_distance:
                        best_lane = closest_lane_ids[i]
                        shortest_distance = distance

            return PrefabToRouteSection(next_item, best_lane)

    if isinstance(current_section.items[0].item, c.Prefab):
        end_node = current_section.end_node
        start_node = current_section.start_node

        end_node_is_in_front = math_helpers.IsInFront(
            (end_node.x, end_node.y), data.truck_rotation, (data.truck_x, data.truck_z)
        )
        start_node_is_in_front = math_helpers.IsInFront(
            (start_node.x, start_node.y),
            data.truck_rotation,
            (data.truck_x, data.truck_z),
        )

        if end_node_is_in_front and start_node_is_in_front:
            distance_to_start = math_helpers.DistanceBetweenPoints(
                (start_node.x, start_node.y), (data.truck_x, data.truck_z)
            )
            distance_to_end = math_helpers.DistanceBetweenPoints(
                (end_node.x, end_node.y), (data.truck_x, data.truck_z)
            )
            if distance_to_start < distance_to_end:
                start_node_is_in_front = False
            else:
                end_node_is_in_front = False

        node = end_node if end_node_is_in_front else start_node
        next_item = None

        forward_item = data.map.get_item_by_uid(node.forward_item_uid)
        backward_item = data.map.get_item_by_uid(node.backward_item_uid)

        if forward_item is None or backward_item is None:
            return None

        if (
            forward_item.uid != current_section.items[-1].item.uid
            and forward_item.uid != current_section.items[0].item.uid
        ):
            next_item = forward_item
        elif (
            backward_item.uid != current_section.items[0].item.uid
            and backward_item.uid != current_section.items[-1].item.uid
        ):
            next_item = backward_item
        else:
            return None

        if next_item is None:
            return None

        if current_section.last_actual_points == []:
            return None  # should not happen

        end_point = current_section.last_actual_points[-1]
        if isinstance(next_item, c.Road):
            closest_lane_id = 0
            closest_point_distance = math.inf
            for lane_id, lane in enumerate(next_item.lanes):
                distance = math_helpers.DistanceBetweenPoints(
                    (end_point.x, end_point.y, end_point.z), lane.points[0].tuple()
                )
                end_distance = math_helpers.DistanceBetweenPoints(
                    (end_point.x, end_point.y, end_point.z), lane.points[-1].tuple()
                )
                distance = min(distance, end_distance)
                if distance < closest_point_distance:
                    closest_lane_id = lane_id
                    closest_point_distance = distance

            return RoadToRouteSection(next_item, closest_lane_id)

        if isinstance(next_item, c.Prefab):
            if current_section.last_actual_points == []:
                current_section.get_points()
            points = current_section.last_actual_points
            if len(points) == 0:
                return None

            closest_lane_ids = GetClosestLanesForPrefab(next_item, end_point)

            if len(closest_lane_ids) == 0:
                return None
            elif len(closest_lane_ids) == 1:
                return PrefabToRouteSection(next_item, closest_lane_ids[0])

            end_positions = [
                next_item.nav_routes[lane_id].points[-1].tuple()
                for lane_id in closest_lane_ids
            ]
            direction = (
                "left"
                if data.truck_indicating_left
                else "right"
                if data.truck_indicating_right
                else "straight"
            )
            best_lane = math_helpers.GetMostInDirection(
                end_positions,
                data.truck_rotation,
                (data.truck_x, data.truck_y, data.truck_z),
                direction=direction,
            )

            target_end_position = end_positions[best_lane]
            best_lane = 0
            shortest_distance = math.inf
            for i, position in enumerate(end_positions):
                if position == target_end_position:
                    distance = next_item.nav_routes[closest_lane_ids[i]].distance
                    if distance < shortest_distance:
                        best_lane = closest_lane_ids[i]
                        shortest_distance = distance

            return PrefabToRouteSection(next_item, best_lane)


def GetCurrentNavigationPlan():
    path: list[RouteNode] = data.navigation_plan
    distance_ordered = sorted(
        path,
        key=lambda nav: math_helpers.DistanceBetweenPoints(
            (nav.node.x, nav.node.y), (data.truck_x, data.truck_z)
        ),
    )
    closest = distance_ordered[0]

    # Check if it's behind or in front
    in_front = math_helpers.IsInFront(
        (closest.node.x, closest.node.y),
        data.truck_rotation,
        (data.truck_x, data.truck_z),
    )
    index = path.index(closest)
    if not in_front:
        lower_index = index
        upper_index = min(len(path) - 1, index + 2)
    else:
        lower_index = max(0, index - 1)
        upper_index = min(len(path) - 1, index)

        # Check if the distance to the last node is under 12m.
        # This usually only happens in ProMods, where highways have
        # tiny prefabs.
        distance = math_helpers.DistanceBetweenPoints(
            (closest.node.x, closest.node.y),
            (path[lower_index].node.x, path[lower_index].node.y),
        )
        if distance < 12:
            lower_index = max(0, lower_index - 1)

    closest_nodes: list[RouteNode] = [path[lower_index], path[upper_index]]
    data.circles = [(nav.node.x, nav.node.z, nav.node.y) for nav in closest_nodes]
    in_front = [
        math_helpers.IsInFront(
            (nav.node.x, nav.node.y), data.truck_rotation, (data.truck_x, data.truck_z)
        )
        for nav in closest_nodes
    ]

    try:
        last = closest_nodes[in_front.index(False)]
        next = closest_nodes[in_front.index(True)]
    except Exception:
        return

    # find the item that connects both of the nodes
    next_item = last.item
    if next_item is None:
        next_item = nh.get_connecting_item_uid(last.node, next.node)
        next_item = data.map.get_item_by_uid(next_item)
        if next_item is None:
            return None

    # get the closest lane on that item
    if isinstance(next_item, c.Road):
        closest_lane = rh.get_closest_lane(next_item, data.truck_x, data.truck_z)
        target_lanes = []

        # This is the last road piece before a prefab
        if next.item_type() == c.Prefab:
            target_lanes = last.lanes

        # Find the last road piece before a prefab
        next_prefab_index = path.index(next)
        while path[next_prefab_index].item_type() == c.Road:
            next_prefab_index += 1
            if next_prefab_index >= len(path):
                break

        if next_prefab_index == len(path):  # Last one doesn't matter
            return RoadToRouteSection(
                next_item, closest_lane, target_lanes=target_lanes, path_index=index
            )

        start = next_item.lanes[closest_lane].points[0]
        end = next_item.lanes[closest_lane].points[-1]
        in_front_start = math_helpers.IsInFront(
            (start.x, start.z), data.truck_rotation, (data.truck_x, data.truck_z)
        )
        in_front_end = math_helpers.IsInFront(
            (end.x, end.z), data.truck_rotation, (data.truck_x, data.truck_z)
        )

        invert = False
        if in_front_start and not in_front_end:
            invert = True

        last_road = path[next_prefab_index - 1]
        target_lanes = last_road.lanes

        return RoadToRouteSection(
            next_item,
            closest_lane,
            target_lanes=target_lanes,
            path_index=index,
            invert=invert,
        )

    if isinstance(next_item, c.Prefab):
        closest_lanes = GetClosestLanesForPrefab(
            next_item, c.Position(data.truck_x, data.truck_y, data.truck_z)
        )

        if len(closest_lanes) == 0:
            return None

        closest_lane = closest_lanes[0]
        return PrefabToRouteSection(
            next_item, closest_lane, path_index=path.index(next)
        )


def GetNextNavigationItem():
    last_item = data.route_plan[-1]
    last_points = last_item.lane_points

    index = last_item.path_index + 1
    path: list[RouteNode] = data.navigation_plan

    if isinstance(last_item.items[0].item, c.Prefab):
        index -= 1
        # Check if we need to go back one more to get the root road.
        # TODO: Why is this needed?
        if (
            index > 0
            and isinstance(path[index - 1].item, c.Road)
            and path[index].item_type() == c.Road
        ):
            index -= 1

    try:
        if path[index].item == last_item.items[0].item:
            index += 1
    except Exception:
        data.route_plan = []
        return None

    if isinstance(last_item.items[0].item, c.Road):
        next_prefab_index = index - 1
        while path[next_prefab_index].item_type() == c.Road:
            next_prefab_index += 1
            if next_prefab_index >= len(path):
                break

        index = next_prefab_index

    try:
        last = None
        if index > 0:
            last = path[index - 1]

        next = None
        if index < len(path) - 1:
            next = path[index + 1]

        current = path[index]
    except Exception:
        logging.warning(
            "Failed to get current node in path (probably because the navigation has ended)"
        )
        return None

    if current.item_type() is None:
        if last is not None:
            print(f"No connection between nodes {current.node.uid} and {next.node.uid}")
        print("No connection between nodes")
        return None

    next_item = current.item
    if isinstance(next_item, c.Road):
        if last_points:
            closest_lane, distance = rh.get_closest_lane(
                next_item, last_points[-1].x, last_points[-1].z, return_distance=True
            )
            # print("Closest lane distance:", distance)
        else:
            closest_lane = rh.get_closest_lane(next_item, data.truck_x, data.truck_z)

        target_lanes = current.lanes  # accepted when pathfinding lanes

        # Find the last road piece before a prefab
        next_prefab_index = path.index(next)
        while path[next_prefab_index].item_type() == c.Road:
            next_prefab_index += 1
            if next_prefab_index >= len(path):
                break

        start = next_item.lanes[closest_lane].points[0]
        end = next_item.lanes[closest_lane].points[-1]
        start_distance = math_helpers.DistanceBetweenPoints(
            (start.x, start.z), (last_points[-1].x, last_points[-1].z)
        )
        end_distance = math_helpers.DistanceBetweenPoints(
            (end.x, end.z), (last_points[-1].x, last_points[-1].z)
        )

        invert = False
        if end_distance < start_distance:
            invert = True

        return RoadToRouteSection(
            next_item,
            closest_lane,
            target_lanes=target_lanes,
            path_index=next_prefab_index,
            invert=invert,
        )

    if isinstance(next_item, c.Prefab):
        accepted_lanes = current.lanes
        best_lane = math.inf
        best_distance = math.inf
        best_length = math.inf
        for lane in accepted_lanes:
            # Validate index bounds before accessing nav_routes
            if lane < 0 or lane >= len(next_item.nav_routes):
                continue

            length = next_item.nav_routes[lane].distance
            points = next_item.nav_routes[lane].points

            if last_points:
                ee_distance = math_helpers.DistanceBetweenPoints(
                    (points[-1].x, points[-1].z), (last_points[-1].x, last_points[-1].z)
                )
                es_distance = math_helpers.DistanceBetweenPoints(
                    (points[-1].x, points[-1].z), (last_points[0].x, last_points[0].z)
                )
                ss_distance = math_helpers.DistanceBetweenPoints(
                    (points[0].x, points[0].z), (last_points[0].x, last_points[0].z)
                )
                se_distance = math_helpers.DistanceBetweenPoints(
                    (points[0].x, points[0].z), (last_points[-1].x, last_points[-1].z)
                )
                distance = min(ee_distance, es_distance, ss_distance, se_distance)
            else:
                distance = math_helpers.DistanceBetweenPoints(
                    (points[0].x, points[0].z), (data.truck_x, data.truck_z)
                )

            if distance < best_distance or (
                distance < best_distance + 0.1 and length < best_length
            ):
                best_distance = distance
                best_length = length
                best_lane = lane

        if best_lane == math.inf:
            return None

        return PrefabToRouteSection(next_item, best_lane, path_index=index)    


def ResetState():
    if "indicate" in data.plugin.state.text or "lane change" in data.plugin.state.text:
        data.plugin.state.text = ""
        data.plugin.tags.lane_change_status = "idle"


was_indicating = False


def CheckForLaneChangeManual():
    global was_indicating
    if isinstance(data.route_plan[0].items[0].item, c.Prefab):
        was_indicating = False
        return

    current_index = data.route_plan[0].lane_index
    lanes = data.route_plan[0].items[0].item.lanes
    side = lanes[current_index].side
    left_lanes = len([lane for lane in lanes if lane.side == "left"])
    # lanes = left_lanes + right_lanes

    if (
        data.truck_indicating_right or data.truck_indicating_left
    ) and not was_indicating:
        was_indicating = True
        current_index = data.route_plan[0].lane_index
        side = lanes[current_index].side

        target_index = current_index
        change = 1 if data.truck_indicating_right else -1

        closest_item, closest_lane = get_closest_route_item(data.route_plan[0].items)
        end_node_in_front = math_helpers.IsInFront(
            (closest_item.end_node.x, closest_item.end_node.y),
            data.truck_rotation,
            (data.truck_x, data.truck_z),
        )

        if side == "left":
            if end_node_in_front:  # Normal lane change
                target_index += change
                if change == 1 and target_index >= left_lanes:
                    target_index = left_lanes - 1
            else:  # Inverted lane change
                target_index -= change
                if change == -1 and target_index >= left_lanes:
                    target_index = left_lanes - 1
        else:
            if end_node_in_front:
                target_index += change
                if change == -1 and target_index < left_lanes:
                    target_index = left_lanes
            else:
                target_index -= change
                if change == 1 and target_index < left_lanes:
                    target_index = left_lanes

        if target_index < 0:
            target_index = 0
        if target_index >= len(lanes):
            target_index = len(lanes) - 1

        data.route_plan[0].skip_indicate_state = True
        data.route_plan[0].lane_index = target_index
        data.route_plan[0].skip_indicate_state = False
        data.route_plan = [data.route_plan[0]]

    elif not data.truck_indicating_left and not data.truck_indicating_right:
        was_indicating = False


def CheckForLaneChange():
    if len(data.route_plan) < 2:
        ResetState()
        CheckForLaneChangeManual()
        return

    current = data.route_plan[0]
    if not isinstance(current.items[0].item, c.Road):
        ResetState()
        return
    if current.is_lane_changing:
        if current.lane_change_factor < 0.3:
            data.route_plan = [current]
        data.plugin.tags.lane_change_status = f"executing:{current.lane_change_factor}"
        data.plugin.state.text = "Executing lane change..."
        return

    ResetState()
    CheckForLaneChangeManual()


was_indicating = False


def UpdateRoutePlan():
    global was_indicating
    if not data.enabled:
        data.route_plan = []

    if (
        not data.use_navigation or len(data.navigation_plan) == 0
    ):  # No navigation plan / use only route planner
        if len(data.route_plan) == 0:
            data.route_plan.append(GetClosestRouteSection())

        if data.route_plan[0] is None:
            if len(data.route_plan) > 1:
                data.route_plan.pop(0)
            else:
                data.route_plan = []
            return  # No route sections found

        if data.route_plan[0].is_ended:
            data.route_plan.pop(0)

        if len(data.route_plan) < data.route_plan_length:
            try:
                next_route_section = GetNextRouteSection()
            except Exception:
                logging.exception("Failed to get next route section")
                next_route_section = None
            if next_route_section is not None:
                data.route_plan.append(next_route_section)

        if (
            data.truck_indicating_left or data.truck_indicating_right
        ) and not was_indicating:
            if len(data.route_plan) > 1:
                data.route_plan = [data.route_plan[0]]
            was_indicating = True
        elif (
            not (data.truck_indicating_left or data.truck_indicating_right)
            and was_indicating
        ):
            if len(data.route_plan) > 1:
                data.route_plan = [data.route_plan[0]]
            was_indicating = False

    else:  # We have a navigation plan that we can drive on.
        if len(data.route_plan) == 0:
            first_item = GetCurrentNavigationPlan()
            data.route_plan.append(first_item)

        if data.route_plan[0] is None:
            if len(data.route_plan) > 1:
                data.route_plan.pop(0)
            else:
                data.route_plan = []
            return

        if data.route_plan[0].is_ended:
            data.route_plan.pop(0)

        if len(data.route_plan) == 0:
            return

        if len(data.route_plan) < data.route_plan_length:
            item = GetNextNavigationItem()
            try:
                if item is not None and len(data.route_plan) > 0:
                    start = item.start_node.uid
                    end = item.end_node.uid
                    last_start = data.route_plan[-1].start_node.uid
                    last_end = data.route_plan[-1].end_node.uid
                    if start != last_start or end != last_end:
                        data.route_plan.append(item)
            except Exception:
                pass
        try:
            CheckForLaneChange()
        except Exception:
            logging.exception("Failed to check for lane change")
            pass


def get_closest_route_item(items: list[c.Prefab | c.Road] | list[rc.RouteItem]):
    in_bounding_box = []
    for item in items:
        if isinstance(item, rc.RouteItem):
            item = item.item

        if item.bounding_box.is_in(
            c.Position(data.truck_x, data.truck_z, data.truck_y)
        ):
            in_bounding_box.append(item)

    closest_lane_id = 0
    closest_item = None
    closest_point_distance = math.inf
    for item in in_bounding_box:
        if isinstance(item, c.Prefab):
            for lane_id, lane in enumerate(item.nav_routes):
                for point in lane.points:
                    point_tuple = point.tuple()
                    point_tuple = (point_tuple[0], point_tuple[2])
                    distance = math_helpers.DistanceBetweenPoints(
                        (data.truck_x, data.truck_z), point_tuple
                    )
                    if distance < closest_point_distance:
                        closest_point_distance = distance
                        closest_item = item
                        closest_lane_id = lane_id

        elif isinstance(item, c.Road):
            for lane_id, lane in enumerate(item.lanes):
                for point in lane.points:
                    point_tuple = point.tuple()
                    point_tuple = (point_tuple[0], point_tuple[2])
                    distance = math_helpers.DistanceBetweenPoints(
                        (data.truck_x, data.truck_z), point_tuple
                    )
                    if distance < closest_point_distance:
                        closest_point_distance = distance
                        closest_item = item
                        closest_lane_id = lane_id

    return closest_item, closest_lane_id
