using Hexa.NET.OpenGL;
using System.Numerics;
using ETS2LA.Game.Data;
using ETS2LA.Game.Utils;
using ETS2LA.Logging;
using TruckLib.ScsMap;
using TruckLib.Models.Ppd;
using ETS2LA.Game.PpdFiles;
using ETS2LA.Game.SiiFiles;
using TruckLib;

namespace ETS2LA.ML.Vision;

// This is basically just a container that tells OpenGL *what* to render. How it's rendered is determined
// by the shader we use. i.e. the included SolidColor shader.
public class RoadMesh : IDisposable
{
    private readonly GL _gl;
    public uint Vao { get; private set; }
    public uint Vbo { get; private set; }
    public int VertexCount { get; private set; }

    public RoadMesh(GL gl)
    {
        _gl = gl;
        Vao = _gl.GenVertexArray();
        Vbo = _gl.GenBuffer();

        _gl.BindVertexArray(Vao);
        _gl.BindBuffer(GLBufferTargetARB.ArrayBuffer, Vbo);

        unsafe
        {
            _gl.VertexAttribPointer(0, 3, GLVertexAttribPointerType.Float, false, 3 * sizeof(float), (void*)0);
        }
        _gl.EnableVertexAttribArray(0);

        _gl.BindVertexArray(0);
        _gl.BindBuffer(GLBufferTargetARB.ArrayBuffer, 0);
    }

    public unsafe void UpdateVertices(ReadOnlySpan<Vector3> vertices)
    {
        VertexCount = vertices.Length;
        if (VertexCount == 0) return;

        _gl.BindBuffer(GLBufferTargetARB.ArrayBuffer, Vbo);
        fixed (Vector3* ptr = vertices)
        {
            _gl.BufferData(
                GLBufferTargetARB.ArrayBuffer, 
                vertices.Length * sizeof(Vector3), 
                ptr, 
                GLBufferUsageARB.DynamicDraw
            );
        }
        _gl.BindBuffer(GLBufferTargetARB.ArrayBuffer, 0);
    }

    public void Draw()
    {
        if (VertexCount == 0) return;

        _gl.BindVertexArray(Vao);
        _gl.DrawArrays(GLPrimitiveType.Triangles, 0, VertexCount);
        _gl.BindVertexArray(0);
    }

    public void Dispose()
    {
        if (Vao != 0) _gl.DeleteVertexArray(Vao);
        if (Vbo != 0) _gl.DeleteBuffer(Vbo);
    }
}

// This util class will build road geometry from the nearby nodes.
public static class VisionRoadUtils
{
    // The real lane width is 4.5 meters, however I want to add
    // some padding to seperate them visually.
    private static float LaneWidth = 4.25f; // meters

    public static List<Vector3> BuildRoadGeometry(Node[] nearbyNodes)
    {
        List<Vector3> roadLineBuffer = new();
        Dictionary<ulong, Road> nearbyRoads = new();
        Dictionary<ulong, Prefab> nearbyPrefabs = new();

        foreach (var node in nearbyNodes)
        {
            if (node.BackwardItem is Road bRoad && !nearbyRoads.ContainsKey(bRoad.Uid))
                nearbyRoads.Add(bRoad.Uid, bRoad);

            if (node.ForwardItem is Road fRoad && !nearbyRoads.ContainsKey(fRoad.Uid))
                nearbyRoads.Add(fRoad.Uid, fRoad);

            if (node.BackwardItem is Prefab bPrefab && !nearbyPrefabs.ContainsKey(bPrefab.Uid))
                nearbyPrefabs.Add(bPrefab.Uid, bPrefab);

            if (node.ForwardItem is Prefab fPrefab && !nearbyPrefabs.ContainsKey(fPrefab.Uid))
                nearbyPrefabs.Add(fPrefab.Uid, fPrefab);
        }

        foreach (var road in nearbyRoads.Values)
        {
            try
            {                
                ParsedRoad parsedRoad = new ParsedRoad(road);
                float resolution = RoadUtils.GetRoadResolution(road);
                float length = road.Length;

                if (length <= 0) continue;
                float stepLength = 1 / length * resolution;

                ExtractLanes(roadLineBuffer, parsedRoad, Side.Left, stepLength);
                ExtractLanes(roadLineBuffer, parsedRoad, Side.Right, stepLength);
            } catch { }
        }

        foreach (var prefab in nearbyPrefabs.Values)
        {
            try
            {
                var ppd = PpdFileHandler.Current.GetPpdFile(prefab.Model.ToString());
                if (ppd is not PrefabDescriptor desc)
                    continue;

                ExtractPrefabCurves(roadLineBuffer, desc, prefab);                
            } catch { }
        }

        return roadLineBuffer;
    }

    private static void ExtractLanes(
        List<Vector3> roadBuffer,
        ParsedRoad parsedRoad,
        Side side,
        float stepLength)
    {
        int laneCount = parsedRoad.GetLaneCount(side);
        for (int laneIndex = 0; laneIndex < laneCount; laneIndex++)
        {
            List<Vector3> centers = new();

            float t = 0f;
            while (t < 1 + stepLength)
            {
                centers.Add(parsedRoad.InterpolateLane(t, side, laneIndex).Position);

                t += stepLength;
                if (t > 1 && t < 1 + stepLength)
                    t = 1;
            }

            if (centers.Count < 2)
                continue;

            CreatePathVertices(roadBuffer, centers, LaneWidth);
        }
    }

    private static void ExtractPrefabCurves(
        List<Vector3> roadBuffer,
        PrefabDescriptor desc,
        Prefab prefab)
    {
        int origin = prefab.Origin;

        Vector3 prefabStart =
            prefab.Nodes[0].Position - desc.Nodes[origin].Position;

        Vector3 prefabRotation =
            prefab.Nodes[0].Rotation.ToEuler() -
            MathEx.GetNodeRotation(desc.Nodes[origin].Direction).ToEuler();

        Matrix4x4 rotationMatrix =
            Matrix4x4.CreateRotationY(prefabRotation.Y, prefab.Nodes[0].Position);

        foreach (var curve in desc.NavCurves)
        {
            if (curve.Length <= 0)
                continue;

            List<Vector3> centers = new();

            const float resolution = 1f;
            float step = 1f / curve.Length / resolution;

            for (float t = 0; t < 1 + step; t += step)
            {
                if (t > 1 && t < 1 + step)
                    t = 1;

                Vector3 point = PrefabUtils.InterpolateNavCurve(curve, t);
                point = Vector3.Transform(point + prefabStart, rotationMatrix);

                centers.Add(point);
            }

            CreatePathVertices(roadBuffer, centers, LaneWidth);
        }
    }

    private static void CreatePathVertices(
        List<Vector3> vertices,
        List<Vector3> centers,
        float laneWidth)
    {
        if (centers.Count < 2)
            return;

        List<Vector3> left = new(centers.Count);
        List<Vector3> right = new(centers.Count);

        for (int i = 0; i < centers.Count; i++)
        {
            Vector3 tangent;

            if (i == 0)
                tangent = centers[1] - centers[0];
            else if (i == centers.Count - 1)
                tangent = centers[i] - centers[i - 1];
            else
                tangent = centers[i + 1] - centers[i - 1];

            tangent = Vector3.Normalize(tangent);

            Vector3 offset =
                Vector3.Normalize(Vector3.Cross(Vector3.UnitY, tangent))
                * (laneWidth * 0.5f);

            left.Add(centers[i] - offset);
            right.Add(centers[i] + offset);
        }

        for (int i = 0; i < centers.Count - 1; i++)
        {
            vertices.Add(left[i]);
            vertices.Add(right[i]);
            vertices.Add(left[i + 1]);

            vertices.Add(right[i]);
            vertices.Add(right[i + 1]);
            vertices.Add(left[i + 1]);
        }
    }
}