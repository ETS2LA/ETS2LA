using ETS2LA.Notifications;
using TruckLib.ScsMap;
using TruckLib;

using ETS2LA.Game.Utils;
using ETS2LA.Game.PpdFiles;
using System.Numerics;
using TruckLib.Models.Ppd;
using ETS2LA.Settings.Global;
using ETS2LA.Logging;
using Avalonia.Controls.Embedding.Offscreen;

namespace ETS2LA.Game.Data;

/// <summary>
///  List of item types ETS2LA should ignore when
///  reading map data. These item types are ignored
///  when DataFidelity is High or lower. 
///  Extreme loads everything.
/// </summary>
public enum IgnoredItemTypes
{
    Terrain = 0x01,
    Model = 0x05,
    Company = 0x06,
    Service = 0x07,
    CutPlane = 0x08,
    Mover = 0x09,
    EnvironmentArea = 0x0B,
    CityArea = 0x0C,
    Hinge = 0x0D,
    AnimatedModel = 0x0F,
    MapOverlay = 0x12,
    Sound = 0x15,
    Garage = 0x16,
    CameraPoint = 0x17,
    Walker = 0x1C,
    TrafficArea = 0x26,
    BezierPatch = 0x27,
    Compound = 0x28,
    Trajectory = 0x29,
    MapArea = 0x2A,
    FarModel = 0x2B,
    Curve = 0x2C,
    CameraPath = 0x2D,
    Cutscene = 0x2E,
    Hookup = 0x2F,
    VisibilityArea = 0x30,
};

public enum Direction
{
    Forward,
    Backward
}

/// <summary>
///  Contains all map data for the game. This class overrides TruckLib.ScsMap.Map
///  with ETS2LA specific notification handling and dropping of items.
/// </summary>
public class MapData : Map
{
    protected override bool PostProcessItem(MapItem item) 
    {
        DataFidelity fidelity = DataSettings.Current.DataFidelity;
        switch (fidelity)
        {
            case DataFidelity.Low:
                // For low fidelity, drop all items that aren't roads or prefabs since
                // they aren't relevant for driving.
                if (item is not Prefab && item is not Road)
                {
                    return false;
                }
                break;
            case DataFidelity.Medium:
            case DataFidelity.High:
                // For medium (and high) fidelity drop all items that aren't in the default enum.
                if (typeof(IgnoredItemTypes).GetEnumNames().Contains(item.ItemType.ToString()))
                {
                    return false;
                }
                break;
            
            // Extreme just keeps everything.
        }
        
        // Additionally we drop terrain data of prefabs and roads as it
        // also saves some memory. Dropping entire prefabs/roads can also be
        // done for items that are "secret" (i.e. not show in the UI map) since
        // they aren't relevant for driving. (in Medium and Low fidelity levels)
        if (item is Prefab p)
        {
            if (fidelity < DataFidelity.High && !p.ShowInUiMap)
                return false;
            
            if (fidelity < DataFidelity.Extreme)
            {
                foreach (var node in p.PrefabNodes)
                {
                    node.Terrain = null;
                }
            }
        }
        else if (item is Road r)
        {
            if (fidelity < DataFidelity.High && !r.ShowInUiMap) 
                return false;
            
            if (fidelity < DataFidelity.Extreme)
            {
                r.Left.Terrain = null;
                r.Right.Terrain = null;
            }
        }         
        return true;
    }

    protected override void OnSectorLoading(Sector sector, int index, int total)
    {
        NotificationHandler.Current.SendNotification(new Notification
        {
            Id = "ETS2LA.Game.Parsing",
            Title = "Parsing Map Data",
            Content = $"Parsing sector {index + 1} of {total}...",
            IsProgressIndeterminate = false,
            Progress = ((index + 1) / (float)total) * 100f,
            CloseAfter = 0
        });
    }
}

public enum Side
{
    Left,
    Right
}

/// <summary>
///  Wrapper to represent any item we parse ourselves. Equal to IMapItem / IMapObject
///  in usage. Check the actual class using "is" e.g. `if (item is ParsedRoad)`.
/// </summary>
public interface IParsedItem { }

/// <summary>
///  Parsed road that takes into account all ETS2LA specific adjustments,
///  such as lane offsets and Next/Last items etc... <br/><br/>
///  You can get a ParsedRoad by instantiating it with a base Road.
/// </summary>
public class ParsedRoad : IParsedItem
{
    /// <summary>
    ///  This is the original base road data as parsed from the map.
    ///  You may use it to access information we don't provide in this parsed version,
    ///  just keep in mind that the offset values won't be accurate (well available at all)
    ///  in the base functions.
    /// </summary>
    public Road Road;
    
    /// <summary>
    ///  Start node for the current road piece. Note that this doesn't necessarily
    ///  mean that Start -> End means the right side is going that direction.
    ///  Sometimes roads can be "reversed" in the map data.
    /// </summary>
    public Node StartNode;
    /// <summary>
    ///  End node for the current road piece. Note that this doesn't necessarily
    ///  mean that Start -> End means the right side is going that direction.
    ///  Sometimes roads can be "reversed" in the map data.
    /// </summary>
    public Node EndNode;

    /// <summary>
    ///  The MapItem that connects to the StartNode from the other side.
    ///  Use `Last is Road` or `Last is Prefab` to check what type it is before
    ///  casting.
    /// </summary>    
    public MapItem? Last = null;
    /// <summary>
    ///  The MapItem that connects to the EndNode from the other side.
    ///  Use `Next is Road` or `Next is Prefab` to check what type it is before
    ///  casting.    
    /// </summary>
    public MapItem? Next = null;

    public float[]? LeftLaneOffsetsStart;
    public float[] LeftLaneOffsetsEnd;
    public float[]? RightLaneOffsetsStart;
    public float[] RightLaneOffsetsEnd;

    public ParsedRoad(Road road)
    {
        Road = road;
        Last = road.BackwardItem as MapItem;
        Next = road.ForwardItem as MapItem;
        LeftLaneOffsetsEnd = RoadUtils.CalculateRoadLaneCenters(road).Left;
        RightLaneOffsetsEnd = RoadUtils.CalculateRoadLaneCenters(road).Right;

        EndNode = (Node)road.ForwardNode;
        StartNode = (Node)road.Node;

        if (Last is Road lastRoad)
        {
            LeftLaneOffsetsStart = RoadUtils.CalculateRoadLaneCenters(lastRoad).Left;
            RightLaneOffsetsStart = RoadUtils.CalculateRoadLaneCenters(lastRoad).Right;
        }
    }

    public Node GetOtherNode(Node node)
    {
        if (node.Uid == StartNode.Uid)
            return EndNode;
        else if (node.Uid == EndNode.Uid)
            return StartNode;
        else
            throw new ArgumentException("Node is not part of this road");
    }

    public Node GetNodeInCommon(ParsedRoad other)
    {
        if (StartNode.Uid == other.StartNode.Uid || StartNode.Uid == other.EndNode.Uid)
            return StartNode;
        else if (EndNode.Uid == other.StartNode.Uid || EndNode.Uid == other.EndNode.Uid)
            return EndNode;
        else
            throw new ArgumentException("No common node between the two roads");
    }

    public Node GetNodeInCommon(ParsedPrefab prefab)
    {
        foreach (var node in prefab.Prefab.Nodes)
        {
            if (node.Uid == StartNode.Uid || node.Uid == EndNode.Uid)
                return (Node)node;
        }
        throw new ArgumentException("No common node between the road and the prefab");
    }

    /// <summary>
    ///  Get the lane count for the specified side. Note that lane counts can be different on each side,
    ///  so for figuring out the total lane count please use `GetTotalLaneCount()`, or tally up the two sides.
    /// </summary>
    /// <param name="side">The side for which to get lane count.</param>
    /// <returns>The number of lanes on the specified side.</returns>
    public int GetLaneCount(Side side) => side == Side.Left ? LeftLaneOffsetsEnd.Length 
                                                            : RightLaneOffsetsEnd.Length;
    /// <summary>
    ///  Get the total lane count for the road, summing up both sides. Note that lane counts can be different
    ///  on each side, so use `GetLaneCount(Side side)` if you want to differentiate between them.
    /// </summary>
    /// <returns>The total number of lanes on the road.</returns>
    public int GetTotalLaneCount() => LeftLaneOffsetsEnd.Length + RightLaneOffsetsEnd.Length;

    /// <summary>
    ///  Get the best lane for a specific position. Negative lanes indicate left-side lanes, while positives <br/>
    ///  right side lanes. Each lane is from 1 to X (so -1, -2, -3 etc...)
    /// </summary>
    /// <param name="Position">The input position to check.</param>
    /// <returns>The best lane for the given position.</returns>
    public int GetBestLaneFor(Vector3 Position, bool inverted = false)
    {
        float closestFactor = GetFactorForPoint(Position);
        if (inverted) closestFactor = 1 - closestFactor;

        int closestLane = -1;
        float closestLaneDistance = float.MaxValue;
        for (int i = 0; i < GetLaneCount(Side.Left); i++)
        {
            OrientedPoint point = InterpolateLane(closestFactor, Side.Left, i);
            float distance = Vector3.Distance(point.Position, Position);
            Logger.Info($"Left lane {i + 1}: distance={distance}, position={point.Position}");
            if (distance < closestLaneDistance)
            {
                closestLaneDistance = distance;
                closestLane = -(i + 1);
            }
        }
        for (int i = 0; i < GetLaneCount(Side.Right); i++)
        {
            OrientedPoint point = InterpolateLane(closestFactor, Side.Right, i);
            float distance = Vector3.Distance(point.Position, Position);
            Logger.Info($"Right lane {i + 1}: distance={distance}, position={point.Position}");
            if (distance < closestLaneDistance)
            {
                closestLaneDistance = distance;
                closestLane = i + 1;
            }
        }

        if (closestLaneDistance > 4.5f * 3) // 3 lanes
            closestLane = GetBestLaneFor(Position, true);

        return closestLane;
    }

    // The functions are all wrappers around the base Road functions.
    // They just take into account the last road's offset values, this way
    // we have accurate transitions between roads everywhere.

    /// <summary>
    ///  Interpolate the middle of the road at the t value (between 0 and 1).
    /// </summary>
    /// <param name="t">The interpolation factor/parameter (0 to 1)</param>
    /// <returns>TruckLib.OrientedPoint at this location.</returns>
    /// <exception cref="ArgumentOutOfRangeException">When t is not between 0 and 1.</exception>
    public OrientedPoint Interpolate(float t)
    {
        if (t < 0 || t > 1) throw new ArgumentOutOfRangeException(nameof(t), "t must be between 0 and 1");
        return Road.InterpolateCurve(t);
    }

    /// <summary>
    ///  Interpolate the middle of the road at the distance along the road. Distance must be between 0 and road length.
    /// </summary>
    /// <param name="dist">The distance along the road (0 to road length)</param>
    /// <returns>TruckLib.OrientedPoint at this location.</returns>
    /// <exception cref="ArgumentOutOfRangeException">When dist is not between 0 and road length.</exception>
    public OrientedPoint? InterpolateDist(float dist)
    {
        if (dist < 0 || dist > Road.Length) throw new ArgumentOutOfRangeException(nameof(dist), "dist must be between 0 and road length");
        return Road.InterpolateCurveDist(dist);
    }

    /// <summary>
    ///  Interpolate the lane position at the t value (between 0 and 1) for the specified lane index and side. 
    ///  Lane index must be between 0 and lane count for the specified side.
    /// </summary>
    /// <param name="t">The interpolation factor/parameter (0 to 1)</param>
    /// <param name="side">The side for which to interpolate.</param>
    /// <param name="laneIndex">The index of the lane for which to interpolate.</param>
    /// <param name="additionalOffset">Additional offset in meters to add to the lane offset. Positive values go to the right, negative to the left.</param>
    /// <returns>TruckLib.OrientedPoint at this location.</returns>
    /// <exception cref="ArgumentOutOfRangeException">When t is not between 0 and 1, or when laneIndex is not valid for the specified side.</exception>
    public OrientedPoint InterpolateLane(float t, Side side, int laneIndex, float additionalOffset = 0)
    {
        if (t < 0 || t > 1) throw new ArgumentOutOfRangeException(nameof(t), "t must be between 0 and 1");
        if (side == Side.Left && (laneIndex < 0 || laneIndex >= LeftLaneOffsetsEnd.Length)) 
            throw new ArgumentOutOfRangeException(nameof(laneIndex), $"laneIndex ({laneIndex}) must be between 0 and {LeftLaneOffsetsEnd.Length - 1} for left side");
        if (side == Side.Right && (laneIndex < 0 || laneIndex >= RightLaneOffsetsEnd.Length)) 
            throw new ArgumentOutOfRangeException(nameof(laneIndex), $"laneIndex ({laneIndex}) must be between 0 and {RightLaneOffsetsEnd.Length - 1} for right side");

        float offset = side == Side.Left ? LeftLaneOffsetsEnd[laneIndex] : RightLaneOffsetsEnd[laneIndex];
        float lastOffset = side == Side.Left ? (LeftLaneOffsetsStart != null ? LeftLaneOffsetsStart[laneIndex] : offset) 
                                             : (RightLaneOffsetsStart != null ? RightLaneOffsetsStart[laneIndex] : offset);

        if (offset != lastOffset)
            offset = RoadUtils.Lerp(lastOffset, offset, t);
        
        OrientedPoint point = Road.InterpolateCurve(t);
        Vector3 normal = Vector3.Normalize(Vector3.Transform(Vector3.UnitX, point.Rotation));
        point.Position = point.Position + normal * -(offset + additionalOffset);

        return point;
    }

    /// <summary>
    ///  Interpolate the lane position at the t value (between 0 and 1) for the specified lane index. 
    ///  Lane index must be between 0 and total lane count.
    /// </summary>
    /// <param name="t">The interpolation factor/parameter (0 to 1)</param>
    /// <param name="laneIndex">The index of the lane for which to interpolate.</param>
    /// <param name="additionalOffset">Additional offset to apply on top of the lane offset, in meters. Positive values go to the right, negative values go to the left.</param>
    /// <returns>TruckLib.OrientedPoint at this location.</returns>
    /// <exception cref="ArgumentOutOfRangeException">When t is not between 0 and 1, or when laneIndex is not valid for the specified side.</exception>
    public OrientedPoint InterpolateLane(float t, int laneIndex, float additionalOffset = 0)
    {
        int leftLaneCount = LeftLaneOffsetsEnd.Length;
        if (laneIndex < 0 || laneIndex >= leftLaneCount + RightLaneOffsetsEnd.Length) 
            throw new ArgumentOutOfRangeException(nameof(laneIndex), $"laneIndex ({laneIndex}) must be between 0 and {leftLaneCount + RightLaneOffsetsEnd.Length - 1}");
        
        if (laneIndex < leftLaneCount)
            return InterpolateLane(t, Side.Left, laneIndex, additionalOffset);
        else
            return InterpolateLane(t, Side.Right, laneIndex - leftLaneCount, additionalOffset);
    }

    /// <summary>
    ///  Interpolate the lane position at the distance along the road for the specified lane index and side. 
    ///  Distance must be between 0 and road length.
    /// </summary>
    /// <param name="dist">The distance along the road (0 to road length)</param>
    /// <param name="side">The side of the road (left or right)</param>
    /// <param name="laneIndex">The index of the lane for which to interpolate.</param>
    /// <param name="additionalOffset">Additional offset to apply on top of the lane offset, in meters. Positive values go to the right, negative values go to the left.</param>
    /// <returns>TruckLib.OrientedPoint at this location.</returns>
    /// <exception cref="ArgumentOutOfRangeException">When dist is not between 0 and road length.</exception>
    /// <exception cref="ArgumentOutOfRangeException">When laneIndex is not valid for the specified side.</exception>
    public OrientedPoint? InterpolateLaneDist(float dist, Side side, int laneIndex, float additionalOffset = 0)
    {
        float t = DistanceToFactor(dist);
        return InterpolateLane(t, side, laneIndex, additionalOffset);
    }

    /// <summary>
    ///  Interpolate the lane position at the distance along the road for the specified lane index. 
    ///  Distance must be between 0 and road length. Lane index must be between 0 and total lane count.
    /// </summary>
    /// <param name="dist">The distance along the road (0 to road length)</param>
    /// <param name="laneIndex">The index of the lane for which to interpolate.</param>
    /// <param name="additionalOffset">Additional offset to apply on top of the lane offset, in meters. Positive values go to the right, negative values go to the left.</param>
    /// <returns>TruckLib.OrientedPoint at this location.</returns>
    /// <exception cref="ArgumentOutOfRangeException">When dist is not between 0 and road length.</exception>
    /// <exception cref="ArgumentOutOfRangeException">When laneIndex is not valid for the specified side.</exception>
    public OrientedPoint? InterpolateLaneDist(float dist, int laneIndex, float additionalOffset = 0)
    {
        int leftLaneCount = LeftLaneOffsetsEnd.Length;
        if (laneIndex < 0 || laneIndex >= leftLaneCount + RightLaneOffsetsEnd.Length) 
            throw new ArgumentOutOfRangeException(nameof(laneIndex), $"laneIndex ({laneIndex}) must be between 0 and {leftLaneCount + RightLaneOffsetsEnd.Length - 1}");
        
        if (laneIndex < leftLaneCount)
            return InterpolateLaneDist(dist, Side.Left, laneIndex, additionalOffset);
        else
            return InterpolateLaneDist(dist, Side.Right, laneIndex - leftLaneCount, additionalOffset);
    }

    /// <summary>
    ///  Interpolate the lane position at the t value (between 0 and 1) for a non-integer lane index by interpolating between the two closest lanes.
    /// </summary>
    /// <param name="t">The t value (between 0 and 1) for interpolation.</param>
    /// <param name="side">The side of the road (Left or Right).</param>
    /// <param name="laneIndexFloat">The floating-point lane index.</param>
    /// <param name="additionalOffset">Additional offset to apply on top of the lane offset, in meters. Positive values go to the right, negative values go to the left.</param>
    /// <returns>The interpolated oriented point.</returns>
    public OrientedPoint InterpolateBetweenLanes(float t, Side side, float laneIndexFloat, float additionalOffset = 0)
    {
        int laneCount = GetLaneCount(side);
        
        // No lanes on this side — return road center
        if (laneCount == 0)
            return Interpolate(t);
        
        int laneIndexFloor = Math.Clamp((int)Math.Floor(laneIndexFloat), 0, laneCount - 1);
        int laneIndexCeil = Math.Clamp((int)Math.Ceiling(laneIndexFloat), 0, laneCount - 1);
        if (laneIndexFloor == laneIndexCeil)
            return InterpolateLane(t, side, laneIndexFloor, additionalOffset);

        OrientedPoint floorPoint = InterpolateLane(t, side, laneIndexFloor, additionalOffset);
        OrientedPoint ceilPoint = InterpolateLane(t, side, laneIndexCeil, additionalOffset);
        float factor = laneIndexFloat - laneIndexFloor;

        Vector3 position = Vector3.Lerp(floorPoint.Position, ceilPoint.Position, factor);
        Quaternion rotation = Quaternion.Slerp(floorPoint.Rotation, ceilPoint.Rotation, factor);
        return new OrientedPoint(position, rotation);
    }

    /// <summary>
    ///  Interpolate the lane position at the distance along the road for a non-integer lane index by interpolating between the two closest lanes. Distance must be between 0 and road length.
    /// </summary>
    /// <param name="dist">The distance along the road (between 0 and road length) for interpolation.</param>
    /// <param name="side">The side of the road (Left or Right).</param>
    /// <param name="laneIndexFloat">The floating-point lane index.</param>
    /// <param name="additionalOffset">Additional offset to apply on top of the lane offset, in meters. Positive values go to the right, negative values go to the left.</param>
    /// <returns>The interpolated oriented point.</returns>
    public OrientedPoint InterpolateBetweenLanesDist(float dist, Side side, float laneIndexFloat, float additionalOffset = 0)
    {
        float t = DistanceToFactor(dist);
        return InterpolateBetweenLanes(t, side, laneIndexFloat, additionalOffset);
    }

    /// <summary>
    ///  This method calculates the factor for a given point on the road.
    ///  Used when you need the equivalent location on a road, for whatever
    ///  starting point.
    /// </summary>
    /// <param name="point">Point to project onto the road.</param>
    /// <returns>Factor from 0-1 along the road.</returns>
    public float GetFactorForPoint(Vector3 point)
    {
        // The road is split into N segments, then we find the best segment and do
        // a projection to that. After that there's a slight refining step to avoid twiching
        // when going from segment to segment.

        // The reason we can't project across the start / end points, is that on curved roads that will
        // result in incorrect projections. Imagine a 180 degree curve, if we project across the start / end
        // you'll result in a half circle, where the start and end move "faster" than the middle.

        const int SEGMENTS = 8;
        const float POINT_DIST = 1f / SEGMENTS;
        
        float bestT = 0;
        float minDistanceSq = float.MaxValue;
    
        Vector3 prevPoint = Interpolate(0).Position; 
        for (int i = 0; i < SEGMENTS; i++)
        {
            Vector3 nextPoint = Interpolate((i + 1) * POINT_DIST).Position;
            Vector3 v = nextPoint - prevPoint;
            float lenSq = Vector3.Dot(v, v);
            
            if (lenSq > 0)
            {
                float tLocal = Math.Clamp(Vector3.Dot(point - prevPoint, v) / lenSq, 0, 1);
                Vector3 projected = prevPoint + tLocal * v;
                float distSq = Vector3.DistanceSquared(point, projected);
    
                if (distSq < minDistanceSq)
                {
                    minDistanceSq = distSq;
                    bestT = (i + tLocal) * POINT_DIST;
                }
            }
            prevPoint = nextPoint;
        }
    
        // Four iterations around bestT
        float searchRange = POINT_DIST; 
        for (int r = 0; r < 4; r++)
        {
            float step = searchRange * 0.25f;
            float t1 = Math.Clamp(bestT - step, 0, 1);
            float t2 = Math.Clamp(bestT + step, 0, 1);
    
            float d1 = Vector3.DistanceSquared(point, Interpolate(t1).Position);
            float dMid = Vector3.DistanceSquared(point, Interpolate(bestT).Position);
            float d2 = Vector3.DistanceSquared(point, Interpolate(t2).Position);
    
            if (d1 < dMid && d1 < d2) { bestT = t1; }
            else if (d2 < dMid) { bestT = t2; }
            
            searchRange *= 0.5f;
        }
    
        return bestT;
    }

    /// <summary>
    ///  Converts a factor to a distance along the road.
    ///  Note that this always goes from Start - End, meaning
    ///  the distance and factor can *decrease* as you drive along
    ///  the road.
    /// </summary>
    /// <param name="factor">Input factor.</param>
    /// <returns>Distance in meters along the road.</returns>
    public float FactorToDistance(float factor)
    {
        return factor * Road.Length;
    }

    /// <summary>
    ///  Converts a distance along the road to a factor.
    /// </summary>
    /// <param name="distance">Input distance in meters.</param>
    /// <returns>Factor from 0-1 along the road.</returns>
    public float DistanceToFactor(float distance)
    {
        return distance / Road.Length;
    }
}

public class ParsedRoadList : IParsedItem
{
    /// <summary>
    ///  List of parsed roads that are connected together. This list is ordered from
    ///  the StartNode of the first road, to the EndNode of the last road. All roads
    ///  are assumed to have the same lane count, as otherwise there would be a 
    ///  prefab in between them.
    /// </summary>
    public List<ParsedRoad> Roads;

    /// <summary>
    ///  Start node for the current road piece. Note that this doesn't necessarily
    ///  mean that Start -> End means the right side is going that direction.
    ///  Sometimes roads can be "reversed" in the map data.
    /// </summary>
    public Node StartNode;
    /// <summary>
    ///  End node for the current road piece. Note that this doesn't necessarily
    ///  mean that Start -> End means the right side is going that direction.
    ///  Sometimes roads can be "reversed" in the map data.
    /// </summary>
    public Node EndNode;

    /// <summary>
    ///  The MapItem that connects to the StartNode from the other side.
    ///  Use `Last is Road` or `Last is Prefab` to check what type it is before
    ///  casting.
    /// </summary>    
    public MapItem? Last = null;
    /// <summary>
    ///  The MapItem that connects to the EndNode from the other side.
    ///  Use `Next is Road` or `Next is Prefab` to check what type it is before
    ///  casting.    
    /// </summary>
    public MapItem? Next = null;

    /// <summary>
    ///  Total length of all roads in the list combined, in meters.
    /// </summary>
    public float TotalLength { get; }

    public ParsedRoadList(List<ParsedRoad> roads)
    {
        Roads = roads;

        Last = roads[0].Road.BackwardItem as MapItem;
        Next = roads[roads.Count - 1].Road.ForwardItem as MapItem;

        EndNode = (Node)roads[roads.Count - 1].Road.ForwardNode;
        StartNode = (Node)roads[0].Road.Node;

        TotalLength = roads.Sum(r => r.Road.Length);
    }

    public Node GetOtherNode(Node node)
    {
        if (node.Uid == StartNode.Uid)
            return EndNode;
        else if (node.Uid == EndNode.Uid)
            return StartNode;
        else
            throw new ArgumentException("Node is not part of this road");
    }

    public Node GetNodeInCommon(ParsedRoad other)
    {
        if (StartNode.Uid == other.StartNode.Uid || StartNode.Uid == other.EndNode.Uid)
            return StartNode;
        else if (EndNode.Uid == other.StartNode.Uid || EndNode.Uid == other.EndNode.Uid)
            return EndNode;
        else
            throw new ArgumentException("No common node between the two roads");
    }

    public Node GetNodeInCommon(ParsedPrefab prefab)
    {
        foreach (var node in prefab.Prefab.Nodes)
        {
            if (node.Uid == StartNode.Uid || node.Uid == EndNode.Uid)
                return (Node)node;
        }
        throw new ArgumentException("No common node between the road and the prefab");
    }

    /// <summary>
    ///  Get the lane count for the specified side. Note that lane counts can be different on each side,
    ///  so for figuring out the total lane count please use `GetTotalLaneCount()`, or tally up the two sides.
    /// </summary>
    /// <param name="side">The side for which to get lane count.</param>
    /// <returns>The number of lanes on the specified side.</returns>
    public int GetLaneCount(Side side) => Roads[0].GetLaneCount(side);
    /// <summary>
    ///  Get the total lane count for the road, summing up both sides. Note that lane counts can be different
    ///  on each side, so use `GetLaneCount(Side side)` if you want to differentiate between them.
    /// </summary>
    /// <returns>The total number of lanes on the road.</returns>
    public int GetTotalLaneCount() => Roads[0].GetTotalLaneCount();

    public ParsedRoad GetParsedRoadForFactor(float factor)
    {
        float distance = factor * TotalLength;
        float accumulatedLength = 0;
        foreach (var road in Roads)
        {
            if (accumulatedLength + road.Road.Length >= distance - 1f)
                return road;
            accumulatedLength += road.Road.Length;
        }
        return Roads[Roads.Count - 1];
    }

    public float FactorToRoadFactor(float factor, ParsedRoad road)
    {
        float distance = factor * TotalLength;
        float accumulatedLength = 0;
        foreach (var r in Roads)
        {
            if (accumulatedLength + r.Road.Length >= distance - 1f)
            {
                float localDistance = distance - accumulatedLength;
                float localFactor = localDistance / r.Road.Length;
                return localFactor;
            }
            accumulatedLength += r.Road.Length;
        }
        return 1f; // Return 1f if no road is found (should not happen)
    }

    /// <summary>
    ///  Get the best lane for a specific position. Negative lanes indicate left-side lanes, while positives <br/>
    ///  right side lanes. Each lane is from 1 to X (so -1, -2, -3 etc...)
    /// </summary>
    /// <param name="Position">The input position to check.</param>
    /// <returns>The best lane for the given position.</returns>
    public int GetBestLaneFor(Vector3 Position, bool inverted = false)
    {
        float closestFactor = GetFactorForPoint(Position);
        if (inverted) closestFactor = 1 - closestFactor;

        int closestLane = 0;
        float closestLaneDistance = float.MaxValue;
        for (int i = 0; i < GetLaneCount(Side.Left); i++)
        {
            OrientedPoint point = InterpolateLane(closestFactor, Side.Left, i);
            float distance = Vector3.Distance(point.Position, Position);
            Logger.Info($"Left lane {i + 1}: distance={distance}, position={point.Position}");
            if (distance < closestLaneDistance)
            {
                closestLaneDistance = distance;
                closestLane = -(i + 1);
            }
        }
        for (int i = 0; i < GetLaneCount(Side.Right); i++)
        {
            OrientedPoint point = InterpolateLane(closestFactor, Side.Right, i);
            float distance = Vector3.Distance(point.Position, Position);
            Logger.Info($"Right lane {i + 1}: distance={distance}, position={point.Position}");
            if (distance < closestLaneDistance)
            {
                closestLaneDistance = distance;
                closestLane = i + 1;
            }
        }

        if (closestLaneDistance > 4.5f * 3) // 3 lanes
            closestLane = GetBestLaneFor(Position, true);

        return closestLane;
    }

    // The functions are all wrappers around the base Road functions.
    // They just take into account the last road's offset values, this way
    // we have accurate transitions between roads everywhere.

    /// <summary>
    ///  Interpolate the middle of the road at the t value (between 0 and 1).
    /// </summary>
    /// <param name="t">The interpolation factor/parameter (0 to 1)</param>
    /// <returns>TruckLib.OrientedPoint at this location.</returns>
    /// <exception cref="ArgumentOutOfRangeException">When t is not between 0 and 1.</exception>
    public OrientedPoint Interpolate(float t)
    {
        if (t < 0 || t > 1) throw new ArgumentOutOfRangeException(nameof(t), "t must be between 0 and 1");
        ParsedRoad road = GetParsedRoadForFactor(t);
        return road.Road.InterpolateCurve(t);
    }

    /// <summary>
    ///  Interpolate the middle of the road at the distance along the road. Distance must be between 0 and road length.
    /// </summary>
    /// <param name="dist">The distance along the road (0 to road length)</param>
    /// <returns>TruckLib.OrientedPoint at this location.</returns>
    /// <exception cref="ArgumentOutOfRangeException">When dist is not between 0 and road length.</exception>
    public OrientedPoint? InterpolateDist(float dist)
    {
        if (dist < 0 || dist > TotalLength) throw new ArgumentOutOfRangeException(nameof(dist), "dist must be between 0 and total length");
        float factor = dist / TotalLength;
        ParsedRoad road = GetParsedRoadForFactor(factor);
        return road.Road.InterpolateCurveDist(dist);
    }

    /// <summary>
    ///  Interpolate the lane position at the t value (between 0 and 1) for the specified lane index and side. 
    ///  Lane index must be between 0 and lane count for the specified side.
    /// </summary>
    /// <param name="t">The interpolation factor/parameter (0 to 1)</param>
    /// <param name="side">The side for which to interpolate.</param>
    /// <param name="laneIndex">The index of the lane for which to interpolate.</param>
    /// <param name="additionalOffset">Additional offset in meters to add to the lane offset. Positive values go to the right, negative to the left.</param>
    /// <returns>TruckLib.OrientedPoint at this location.</returns>
    /// <exception cref="ArgumentOutOfRangeException">When t is not between 0 and 1, or when laneIndex is not valid for the specified side.</exception>
    public OrientedPoint InterpolateLane(float t, Side side, int laneIndex, float additionalOffset = 0)
    {
        if (t < 0 || t > 1) throw new ArgumentOutOfRangeException(nameof(t), "t must be between 0 and 1");
        if (side == Side.Left && (laneIndex < 0 || laneIndex >= GetLaneCount(Side.Left))) 
            throw new ArgumentOutOfRangeException(nameof(laneIndex), $"laneIndex ({laneIndex}) must be between 0 and {GetLaneCount(Side.Left) - 1} for left side");
        if (side == Side.Right && (laneIndex < 0 || laneIndex >= GetLaneCount(Side.Right))) 
            throw new ArgumentOutOfRangeException(nameof(laneIndex), $"laneIndex ({laneIndex}) must be between 0 and {GetLaneCount(Side.Right) - 1} for right side");

        ParsedRoad closestRoad = GetParsedRoadForFactor(t);
        t = FactorToRoadFactor(t, closestRoad);

        float offset = side == Side.Left ? closestRoad.LeftLaneOffsetsEnd[laneIndex] : closestRoad.RightLaneOffsetsEnd[laneIndex];
        float lastOffset = side == Side.Left ? (closestRoad.LeftLaneOffsetsStart != null ? closestRoad.LeftLaneOffsetsStart[laneIndex] : offset) 
                                             : (closestRoad.RightLaneOffsetsStart != null ? closestRoad.RightLaneOffsetsStart[laneIndex] : offset);

        if (offset != lastOffset)
            offset = RoadUtils.Lerp(lastOffset, offset, t);
        
        OrientedPoint point = closestRoad.Road.InterpolateCurve(t);
        Vector3 normal = Vector3.Normalize(Vector3.Transform(Vector3.UnitX, point.Rotation));
        point.Position = point.Position + normal * -(offset + additionalOffset);

        return point;
    }

    /// <summary>
    ///  Interpolate the lane position at the t value (between 0 and 1) for the specified lane index. 
    ///  Lane index must be between 0 and total lane count.
    /// </summary>
    /// <param name="t">The interpolation factor/parameter (0 to 1)</param>
    /// <param name="laneIndex">The index of the lane for which to interpolate.</param>
    /// <param name="additionalOffset">Additional offset to apply on top of the lane offset, in meters. Positive values go to the right, negative values go to the left.</param>
    /// <returns>TruckLib.OrientedPoint at this location.</returns>
    /// <exception cref="ArgumentOutOfRangeException">When t is not between 0 and 1, or when laneIndex is not valid for the specified side.</exception>
    public OrientedPoint InterpolateLane(float t, int laneIndex, float additionalOffset = 0)
    {
        int leftLaneCount = GetLaneCount(Side.Left);
        if (laneIndex < 0 || laneIndex >= leftLaneCount + GetLaneCount(Side.Right)) 
            throw new ArgumentOutOfRangeException(nameof(laneIndex), $"laneIndex ({laneIndex}) must be between 0 and {leftLaneCount + GetLaneCount(Side.Right) - 1}");
        
        if (laneIndex < leftLaneCount)
            return InterpolateLane(t, Side.Left, laneIndex, additionalOffset);
        else
            return InterpolateLane(t, Side.Right, laneIndex - leftLaneCount, additionalOffset);
    }

    /// <summary>
    ///  Interpolate the lane position at the distance along the road for the specified lane index and side. 
    ///  Distance must be between 0 and road length.
    /// </summary>
    /// <param name="dist">The distance along the road (0 to road length)</param>
    /// <param name="side">The side of the road (left or right)</param>
    /// <param name="laneIndex">The index of the lane for which to interpolate.</param>
    /// <param name="additionalOffset">Additional offset to apply on top of the lane offset, in meters. Positive values go to the right, negative values go to the left.</param>
    /// <returns>TruckLib.OrientedPoint at this location.</returns>
    /// <exception cref="ArgumentOutOfRangeException">When dist is not between 0 and road length.</exception>
    /// <exception cref="ArgumentOutOfRangeException">When laneIndex is not valid for the specified side.</exception>
    public OrientedPoint? InterpolateLaneDist(float dist, Side side, int laneIndex, float additionalOffset = 0)
    {
        float t = DistanceToFactor(dist);
        return InterpolateLane(t, side, laneIndex, additionalOffset);
    }

    /// <summary>
    ///  Interpolate the lane position at the distance along the road for the specified lane index. 
    ///  Distance must be between 0 and road length. Lane index must be between 0 and total lane count.
    /// </summary>
    /// <param name="dist">The distance along the road (0 to road length)</param>
    /// <param name="laneIndex">The index of the lane for which to interpolate.</param>
    /// <param name="additionalOffset">Additional offset to apply on top of the lane offset, in meters. Positive values go to the right, negative values go to the left.</param>
    /// <returns>TruckLib.OrientedPoint at this location.</returns>
    /// <exception cref="ArgumentOutOfRangeException">When dist is not between 0 and road length.</exception>
    /// <exception cref="ArgumentOutOfRangeException">When laneIndex is not valid for the specified side.</exception>
    public OrientedPoint? InterpolateLaneDist(float dist, int laneIndex, float additionalOffset = 0)
    {
        int leftLaneCount = GetLaneCount(Side.Left);
        if (laneIndex < 0 || laneIndex >= leftLaneCount + GetLaneCount(Side.Right)) 
            throw new ArgumentOutOfRangeException(nameof(laneIndex), $"laneIndex ({laneIndex}) must be between 0 and {leftLaneCount + GetLaneCount(Side.Right) - 1}");
        
        if (laneIndex < leftLaneCount)
            return InterpolateLaneDist(dist, Side.Left, laneIndex, additionalOffset);
        else
            return InterpolateLaneDist(dist, Side.Right, laneIndex - leftLaneCount, additionalOffset);
    }

    /// <summary>
    ///  Interpolate the lane position at the t value (between 0 and 1) for a non-integer lane index by interpolating between the two closest lanes.
    /// </summary>
    /// <param name="t">The t value (between 0 and 1) for interpolation.</param>
    /// <param name="side">The side of the road (Left or Right).</param>
    /// <param name="laneIndexFloat">The floating-point lane index.</param>
    /// <param name="additionalOffset">Additional offset to apply on top of the lane offset, in meters. Positive values go to the right, negative values go to the left.</param>
    /// <returns>The interpolated oriented point.</returns>
    public OrientedPoint InterpolateBetweenLanes(float t, Side side, float laneIndexFloat, float additionalOffset = 0)
    {
        int laneCount = GetLaneCount(side);
        
        // No lanes on this side — return road center
        if (laneCount == 0)
            return Interpolate(t);
        
        int laneIndexFloor = Math.Clamp((int)Math.Floor(laneIndexFloat), 0, laneCount - 1);
        int laneIndexCeil = Math.Clamp((int)Math.Ceiling(laneIndexFloat), 0, laneCount - 1);
        if (laneIndexFloor == laneIndexCeil)
            return InterpolateLane(t, side, laneIndexFloor, additionalOffset);

        OrientedPoint floorPoint = InterpolateLane(t, side, laneIndexFloor, additionalOffset);
        OrientedPoint ceilPoint = InterpolateLane(t, side, laneIndexCeil, additionalOffset);
        float factor = laneIndexFloat - laneIndexFloor;

        Vector3 position = Vector3.Lerp(floorPoint.Position, ceilPoint.Position, factor);
        Quaternion rotation = Quaternion.Slerp(floorPoint.Rotation, ceilPoint.Rotation, factor);
        return new OrientedPoint(position, rotation);
    }

    /// <summary>
    ///  Interpolate the lane position at the distance along the road for a non-integer lane index by interpolating between the two closest lanes. Distance must be between 0 and road length.
    /// </summary>
    /// <param name="dist">The distance along the road (between 0 and road length) for interpolation.</param>
    /// <param name="side">The side of the road (Left or Right).</param>
    /// <param name="laneIndexFloat">The floating-point lane index.</param>
    /// <param name="additionalOffset">Additional offset to apply on top of the lane offset, in meters. Positive values go to the right, negative values go to the left.</param>
    /// <returns>The interpolated oriented point.</returns>
    public OrientedPoint InterpolateBetweenLanesDist(float dist, Side side, float laneIndexFloat, float additionalOffset = 0)
    {
        float t = DistanceToFactor(dist);
        return InterpolateBetweenLanes(t, side, laneIndexFloat, additionalOffset);
    }

    /// <summary>
    ///  This method calculates the factor for a given point on the road.
    ///  Used when you need the equivalent location on a road, for whatever
    ///  starting point.
    /// </summary>
    /// <param name="point">Point to project onto the road.</param>
    /// <returns>Factor from 0-1 along the road.</returns>
    public float GetFactorForPoint(Vector3 point)
    {
        float closestFactor = 0;
        float closestDistanceSq = float.MaxValue;

        float curFactor = 0;
        foreach (var road in Roads)
        {
            float factor = road.GetFactorForPoint(point);
            OrientedPoint roadPoint = road.Interpolate(factor);
            float distanceSq = Vector3.DistanceSquared(roadPoint.Position, point);
            if (distanceSq < closestDistanceSq)
            {
                closestDistanceSq = distanceSq;
                closestFactor = curFactor + factor * (road.Road.Length / TotalLength);
            }
            curFactor += road.Road.Length / TotalLength;
        }
        
        return Math.Clamp(closestFactor, 0, 1);
    }

    /// <summary>
    ///  Converts a factor to a distance along the road.
    ///  Note that this always goes from Start - End, meaning
    ///  the distance and factor can *decrease* as you drive along
    ///  the road.
    /// </summary>
    /// <param name="factor">Input factor.</param>
    /// <returns>Distance in meters along the road.</returns>
    public float FactorToDistance(float factor)
    {
        return factor * TotalLength;
    }

    /// <summary>
    ///  Converts a distance along the road to a factor.
    /// </summary>
    /// <param name="distance">Input distance in meters.</param>
    /// <returns>Factor from 0-1 along the road.</returns>
    public float DistanceToFactor(float distance)
    {
        return distance / TotalLength;
    }
}

public class PrefabPath
{
    public Node StartNode;
    public Node EndNode;
    public List<NavCurve> Curves;
    public float Length;
    public Direction CurveDirection;

    // This is set from the source prefab, it's used to a point
    // to world space once it's generated.
    private Vector3 prefabStart;
    private Matrix4x4 rotationMatrix;

    public PrefabPath(Node startNode, Node endNode, List<NavCurve> curves, Direction dir, Matrix4x4 rotationMatrix, Vector3 prefabStart)
    {
        StartNode = startNode;
        EndNode = endNode;
        Curves = curves;
        Length = curves.Sum(c => c.Length);
        CurveDirection = dir;
        this.prefabStart = prefabStart;
        this.rotationMatrix = rotationMatrix;
    }

    /// <summary>
    ///  Get the point at the specified distance along the path. Distance must be between 0 and path length.
    /// </summary>
    /// <param name="distance">Distance along the path to interpolate.</param>
    /// <returns></returns>
    /// <exception cref="ArgumentOutOfRangeException">Distance is out of bounds.</exception>
    public OrientedPoint? InterpolateDist(float distance)
    {
        if (distance < 0 || distance > Length) throw new ArgumentOutOfRangeException(nameof(distance), "distance must be between 0 and path length");
    
        float distCovered = CurveDirection == Direction.Forward ? 0 : Length;
        IEnumerable<NavCurve> orientedCurves = Curves.AsEnumerable();
        if (CurveDirection == Direction.Backward) orientedCurves = orientedCurves.Reverse();

        foreach (var curve in orientedCurves)
        {
            if (
                (CurveDirection == Direction.Forward && distCovered + curve.Length >= distance) ||
                (CurveDirection == Direction.Backward && distCovered - curve.Length <= distance)
            )
            {
                float distInCurve = CurveDirection == Direction.Forward ? distance - distCovered : distCovered - distance;
                float t = distInCurve / curve.Length;
                if (CurveDirection == Direction.Backward)
                    t = 1 - t;

                var point = PrefabUtils.InterpolateNavCurveOriented(curve, t);
                point.Position = Vector3.Transform(point.Position + prefabStart, rotationMatrix);
                point.Rotation = Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(rotationMatrix) * point.Rotation);
                return point;
            }
            if (CurveDirection == Direction.Forward)
                distCovered += curve.Length;
            else
                distCovered -= curve.Length;
        }
        return null;
    }

    /// <summary>
    ///  Get the point at the specified factor along the path. Factor must be between 0 and 1.
    /// </summary>
    /// <param name="t">Factor along the path to interpolate.</param>
    /// <returns></returns>
    /// <exception cref="ArgumentOutOfRangeException">Factor is out of bounds.</exception>
    public OrientedPoint? Interpolate(float t)
    {
        if (t < 0 || t > 1) throw new ArgumentOutOfRangeException(nameof(t), "t must be between 0 and 1");
        return InterpolateDist(t * Length);
    }

    /// <summary>
    ///  This method calculates the factor for a given along the path.
    ///  Used when you need the equivalent location on a path, for whatever
    ///  starting point.
    /// </summary>
    /// <param name="point">Point to project onto the path.</param>
    /// <returns>Factor from 0-1 along the path.</returns>
    public float GetFactorForPoint(Vector3 point)
    {
        if (Length <= 0) return 0;

        float minDistanceSq = float.MaxValue;
        float closestDistAlongPath = 0;
        float cumulativeDist = 0;

        IEnumerable<NavCurve> curvesToConsider = Curves;
        if (CurveDirection == Direction.Backward) curvesToConsider = curvesToConsider.Reverse();

        foreach (var curve in curvesToConsider)
        {
            Vector3 startPos = Vector3.Transform(curve.StartPosition + prefabStart, rotationMatrix);
            Vector3 endPos = Vector3.Transform(curve.EndPosition + prefabStart, rotationMatrix);

            Vector3 line = endPos - startPos;
            float lineLenSq = line.LengthSquared();
            
            float t = 0;
            if (lineLenSq > 0)
            {
                t = Vector3.Dot(point - startPos, line) / lineLenSq;
                t = Math.Clamp(t, 0, 1);
            }

            Vector3 closestPointOnSegment = startPos + (line * t);
            float distSq = Vector3.DistanceSquared(point, closestPointOnSegment);

            if (distSq < minDistanceSq)
            {
                minDistanceSq = distSq;

                float curveT = (CurveDirection == Direction.Backward) ? (1 - t) : t;
                closestDistAlongPath = cumulativeDist + (curveT * curve.Length);
            }

            cumulativeDist += curve.Length;
        }

        float finalT = Math.Clamp(closestDistAlongPath / Length, 0, 1);
        if (CurveDirection == Direction.Backward)
            finalT = 1 - finalT;
        return finalT;
    }
}

public class ParsedPrefab : IParsedItem
{
    public Prefab Prefab;
    public PrefabDescriptor? Descriptor;

    private Vector3 prefabStart;
    private Vector3 prefabRotation;
    Matrix4x4 rotationMatrix;

    public ParsedPrefab(Prefab prefab)
    {
        Prefab = prefab;
        Descriptor = (PrefabDescriptor?)PpdFileHandler.Current.GetPpdFile(prefab.Model.ToString());

        prefabStart = prefab.Nodes[0].Position - Descriptor.Nodes[prefab.Origin].Position;
        prefabRotation = prefab.Nodes[0].Rotation.ToEuler() - MathEx.GetNodeRotation(Descriptor.Nodes[prefab.Origin].Direction).ToEuler();
        rotationMatrix = Matrix4x4.CreateRotationY(prefabRotation.Y, prefab.Nodes[0].Position);
    }

    public ControlNode GetControlNodeForNode(Node node)
    {
        int index = Prefab.Nodes.IndexOf(node);
        int newIndex = index + Prefab.Origin;
        if (newIndex >= Descriptor.Nodes.Count)
            newIndex -= Descriptor.Nodes.Count;
        
        return Descriptor.Nodes[newIndex];
    }

    public Node GetNodeForControlNode(ControlNode node)
    {
        int index = Descriptor.Nodes.IndexOf(node);
        int newIndex = index - Prefab.Origin;
        if (newIndex < 0)
            newIndex += Descriptor.Nodes.Count;
        
        return (Node)Prefab.Nodes[newIndex];
    }

    public Node GetNodeInCommon(ParsedPrefab other)
    {
        foreach (var node in Prefab.Nodes)
        {
            if (other.Prefab.Nodes.Contains(node))
                return (Node)node;
        }
        throw new ArgumentException("No common node between the two prefabs");
    }

    public Node GetNodeInCommon(ParsedRoadList roadList)
    {
        foreach (var node in Prefab.Nodes)
        {
            if (node.Uid == roadList.StartNode.Uid || node.Uid == roadList.EndNode.Uid)
                return (Node)node;
        }
        throw new ArgumentException("No common node between the prefab and the road list");
    }

    public Node GetNodeInCommon(ParsedRoad road)
    {
        foreach (var node in Prefab.Nodes)
        {
            if (node.Uid == road.StartNode.Uid || node.Uid == road.EndNode.Uid)
                return (Node)node;
        }
        throw new ArgumentException("No common node between the prefab and the road");
    }

    public Node GetNodeMostInFront(Vector3 position, Quaternion rotation, List<Node>? ignoreNodes = null, bool inverted = false)
    {
        Logger.Info($"Finding most in front node at {position} rot: {rotation}. Inverted: {inverted}");
        Vector3 forward = Vector3.Transform(Vector3.UnitX, rotation);
        Logger.Info($"Forward vector: {forward}");
        Node? mostInFront = null;
        float bestDot = inverted ? float.MinValue : float.MaxValue;
        foreach (var node in Prefab.Nodes)
        {
            if (ignoreNodes != null && ignoreNodes.Contains(node)) continue;

            Vector3 toNode = node.Position - position;
            float dot = Math.Abs(Vector3.Dot(forward, toNode));
            Logger.Info($"Node {node.Uid}: dot={dot}");
            if ((!inverted && dot < bestDot) || (inverted && dot > bestDot))
            {
                Logger.Info($"Node {node.Uid} is now the most in front");
                bestDot = dot;
                mostInFront = (Node)node;
            }
        }
        if (mostInFront == null) throw new ArgumentException("Prefab has no nodes");
        return mostInFront;
    }

    private List<int> GetCurveIdsForControlNode(ControlNode node, Direction dir)
    {
        List<int> curveIds = dir == Direction.Forward ? node.OutputLines.ToList() : node.InputLines.ToList();
        curveIds.RemoveAll(id => id == -1);
        return curveIds;
    }

    private List<NavCurve> TraverseCurveUntilTarget(NavCurve start, NavCurve target)
    {
        // If start and target are the same, return the simple path immediately
        if (start == target) return new List<NavCurve> { start };

        // Queue stores the next curves to explore
        Queue<NavCurve> queue = new Queue<NavCurve>();
        
        // Dictionary tracks the parent of each visited curve to reconstruct the path later
        // Key = Next Curve, Value = The Curve we came from
        Dictionary<NavCurve, NavCurve> cameFrom = new Dictionary<NavCurve, NavCurve>();

        queue.Enqueue(start);
        cameFrom[start] = null; // Start has no parent

        bool targetFound = false;

        while (queue.Count > 0)
        {
            NavCurve current = queue.Dequeue();

            if (current == target)
            {
                targetFound = true;
                break;
            }

            // Combine previous and next lines
            List<int> nextCurveIds = current.PreviousLines.Concat(current.NextLines).ToList();

            foreach (var id in nextCurveIds)
            {
                NavCurve nextCurve = Descriptor.NavCurves[id];

                // If we haven't visited this curve yet
                if (!cameFrom.ContainsKey(nextCurve))
                {
                    cameFrom[nextCurve] = current;
                    queue.Enqueue(nextCurve);
                }
            }
        }

        // If the target wasn't reachable, return an empty path
        if (!targetFound) return new List<NavCurve>();

        // Reconstruct the shortest path from target back to start
        List<NavCurve> shortestPath = new List<NavCurve>();
        NavCurve curr = target;
        
        while (curr != null)
        {
            shortestPath.Add(curr);
            curr = cameFrom[curr];
        }

        // Reverse it so it goes from start -> target
        shortestPath.Reverse();
        return shortestPath;
    }

    public (List<PrefabPath>, List<PrefabPath>) GetPathsFromNodeToNode(Node startNode, Node endNode, Vector3 startPosition)
    {
        ControlNode startControlNode = GetControlNodeForNode(startNode);
        ControlNode endControlNode = GetControlNodeForNode(endNode);

        Logger.Info($"Finding paths from C.N. {Descriptor.Nodes.IndexOf(startControlNode)} to C.N. {Descriptor.Nodes.IndexOf(endControlNode)}");

        // We extract the curve ids from Descriptor.ControlNode.Input/OutputLines
        // These match Prefab.Nodes in indices, so we can easily get the start and end using them
        List<int> startCurveIdsForward = GetCurveIdsForControlNode(startControlNode, Direction.Forward);
        List<int> startCurveIdsBackward = GetCurveIdsForControlNode(startControlNode, Direction.Backward);
        int bestCurve = GetClosestCurve(startPosition, startCurveIdsForward.Concat(startCurveIdsBackward).ToList());
        
        Direction dir = startCurveIdsForward.Contains(bestCurve) ? Direction.Forward : Direction.Backward;
        Direction other = dir == Direction.Forward ? Direction.Backward : Direction.Forward;
        List<int> startCurveIds = dir == Direction.Forward ? startCurveIdsForward : startCurveIdsBackward;
        List<int> endCurveIds = GetCurveIdsForControlNode(endControlNode, other);

        Logger.Info($"Start Curve IDs: {string.Join(", ", startCurveIds)}, End Curve IDs: {string.Join(", ", endCurveIds)}, Direction: {dir}");
        Logger.Info($"Descriptor curve count: {Descriptor.NavCurves.Count}");

        List<NavCurve> startCurves = startCurveIds.Select(id => Descriptor.NavCurves[id]).ToList();
        List<NavCurve> endCurves = endCurveIds.Select(id => Descriptor.NavCurves[id]).ToList();

        Logger.Info($"Start Curves: {startCurves.Count}, End Curves: {endCurves.Count}");

        // Then those have to be traversed until we find a path that connects them. Here we 
        // return them all since there can be multiple paths between the same nodes.
        List<PrefabPath> paths = new List<PrefabPath>();
        foreach (var startCurve in startCurves)
        {
            foreach (var endCurve in endCurves)
            {
                Logger.Info($"Finding path from curve {startCurve} to curve {endCurve}");
                List<NavCurve> path = TraverseCurveUntilTarget(startCurve, endCurve);
                Logger.Info($"Path found with {path.Count} curves");
                if (path.Count > 0 && path.Last() == endCurve)
                {
                    paths.Add(new PrefabPath(startNode, endNode, path, dir, rotationMatrix, prefabStart));
                }
            }
        }

        List<PrefabPath> bestPaths = paths.Where(p => p.Curves.First() == Descriptor.NavCurves[bestCurve]).ToList();
        List<PrefabPath> otherPaths = paths.Where(p => p.Curves.First() != Descriptor.NavCurves[bestCurve]).ToList();
        bestPaths = bestPaths.OrderBy(p => p.Length).ToList();
        otherPaths = otherPaths.OrderBy(p => p.Length).ToList();

        Logger.Info($"Total paths found: {paths.Count}, Best paths: {bestPaths.Count}, Other paths: {otherPaths.Count}");
        return (bestPaths, otherPaths);
    }

    public int GetClosestCurve(Vector3 position, List<int> curveIds)
    {
        int closestCurveId = -1;
        float closestDistanceSq = float.MaxValue;

        foreach (var curveId in curveIds)
        {
            NavCurve curve = Descriptor.NavCurves[curveId];
            Vector3 startPos = Vector3.Transform(curve.StartPosition + prefabStart, rotationMatrix);
            Vector3 endPos = Vector3.Transform(curve.EndPosition + prefabStart, rotationMatrix);

            float startDist = Vector3.DistanceSquared(position, startPos);
            float endDist = Vector3.DistanceSquared(position, endPos);
            float bestDist = Math.Min(startDist, endDist);

            if (bestDist < closestDistanceSq)
            {
                closestDistanceSq = bestDist;
                closestCurveId = curveId;
            }
        }

        return closestCurveId;
    }
}