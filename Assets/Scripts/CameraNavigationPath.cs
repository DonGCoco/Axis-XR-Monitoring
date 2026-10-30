using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Lightweight waypoint navigation for the Axis XR prototype.
/// It replaces MRUK room scanning/NavMesh with a small graph calibrated once
/// in the lab. Waypoints are stored in a stable horizontal room frame defined
/// by the persistent positions of CAM_001 and CAM_002. QR/anchor rotation is
/// deliberately ignored so orientation drift cannot rotate the whole route.
/// </summary>
public sealed class CameraNavigationPath : MonoBehaviour
{
    public enum NavigationState
    {
        Idle,
        LoadingScene,
        BuildingNavMesh,
        Ready,
        SceneSetupRequired,
        Failed
    }

    public struct Route
    {
        public Vector3 nextWaypoint;
        public float totalDistance;
        public int cornerCount;
    }

    [Serializable]
    private class WaypointNode
    {
        public int id;
        public Vector3 localPosition;
    }

    [Serializable]
    private class WaypointEdge
    {
        public int a;
        public int b;
    }

    [Serializable]
    private class WaypointGraph
    {
        public List<WaypointNode> nodes = new List<WaypointNode>();
        public List<WaypointEdge> edges = new List<WaypointEdge>();
    }

    private const string ReferenceCameraA = "CAM_001";
    private const string ReferenceCameraB = "CAM_002";
    private const string GraphKeyPrefix = "AXIS_WAYPOINT_GRAPH_V2_";
    private const float RevisitMergeRadius = 0.55f;
    private const float SkipNearStartRadius = 0.65f;

    private CameraSpatialAnchors _positions;
    private WaypointGraph _graph = new WaypointGraph();
    private WaypointGraph _calibrationGraph;
    private int _lastCalibrationNode = -1;
    private string _loadedSite;

    public NavigationState State { get; private set; } = NavigationState.Idle;
    public string Status { get; private set; } = "Waypoint route not loaded";
    public bool IsCalibrating => _calibrationGraph != null;
    public int CalibrationPointCount =>
        _calibrationGraph != null && _calibrationGraph.nodes != null
            ? _calibrationGraph.nodes.Count
            : 0;

    public void Initialize(CameraSpatialAnchors positions)
    {
        _positions = positions;
        RefreshGraphForCurrentSite();
        EnsureReady();
    }

    public void EnsureReady()
    {
        if (_positions == null)
            _positions = FindFirstObjectByType<CameraSpatialAnchors>();

        if (_positions == null)
        {
            State = NavigationState.Failed;
            Status = "Camera spatial anchors are unavailable.";
            return;
        }

        if (string.IsNullOrWhiteSpace(_positions.Site))
        {
            State = NavigationState.LoadingScene;
            Status = "Waiting for the active location site...";
            return;
        }

        RefreshGraphForCurrentSite();

        if (_graph == null ||
            _graph.nodes == null ||
            _graph.edges == null ||
            _graph.nodes.Count < 2 ||
            _graph.edges.Count < 1)
        {
            State = NavigationState.SceneSetupRequired;
            Status = "Waypoint route is not calibrated for " + _positions.Site + ".";
            return;
        }

        if (!TryGetReferenceFrame(out _, out _, out _))
        {
            State = NavigationState.LoadingScene;
            Status = "Restore/register CAM_001 and CAM_002 before using the route.";
            return;
        }

        State = NavigationState.Ready;
        Status = "Waypoint navigation ready";
    }

    public bool TryGetRoute(
        Vector3 headsetPosition,
        Vector3 cameraAnchorPosition,
        out Route route)
    {
        route = default;
        EnsureReady();

        if (State != NavigationState.Ready)
            return false;

        if (!TryGetReferenceFrame(out Vector3 origin, out Vector3 xAxis, out Vector3 zAxis))
        {
            State = NavigationState.LoadingScene;
            Status = "CAM_001 / CAM_002 reference positions are not currently tracked.";
            return false;
        }

        int count = _graph.nodes.Count;
        var worldPositions = new Vector3[count];

        for (int i = 0; i < count; i++)
            worldPositions[i] = LocalToWorld(origin, xAxis, zAxis, _graph.nodes[i].localPosition);

        int startNode = FindNearestNode(headsetPosition, worldPositions);
        int endNode = FindNearestNode(cameraAnchorPosition, worldPositions);

        if (startNode < 0 || endNode < 0)
        {
            Status = "Could not connect to the waypoint route.";
            return false;
        }

        List<int> nodePath = FindShortestPath(startNode, endNode, worldPositions);
        if (nodePath == null || nodePath.Count == 0)
        {
            Status = "No waypoint route connects this area to the camera.";
            return false;
        }

        float totalDistance = HorizontalDistance(headsetPosition, worldPositions[startNode]);

        for (int i = 1; i < nodePath.Count; i++)
        {
            totalDistance += HorizontalDistance(
                worldPositions[nodePath[i - 1]],
                worldPositions[nodePath[i]]);
        }

        totalDistance += HorizontalDistance(worldPositions[endNode], cameraAnchorPosition);

        float startDistance = HorizontalDistance(headsetPosition, worldPositions[startNode]);
        Vector3 nextWaypoint;

        if (startDistance > SkipNearStartRadius)
            nextWaypoint = worldPositions[startNode];
        else if (nodePath.Count > 1)
            nextWaypoint = worldPositions[nodePath[1]];
        else
            nextWaypoint = cameraAnchorPosition;

        route = new Route
        {
            nextWaypoint = nextWaypoint,
            totalDistance = totalDistance,
            cornerCount = nodePath.Count + 2
        };

        State = NavigationState.Ready;
        Status = "Waypoint navigation ready";
        return true;
    }

    public bool BeginCalibration()
    {
        if (_positions == null)
            _positions = FindFirstObjectByType<CameraSpatialAnchors>();

        if (_positions == null ||
            !TryGetReferenceFrame(out _, out _, out _))
        {
            State = NavigationState.LoadingScene;
            Status = "Restore or register CAM_001 and CAM_002 before route setup.";
            return false;
        }

        _calibrationGraph = new WaypointGraph();
        _lastCalibrationNode = -1;
        Status = "Route setup started. Add points at doors, turns, junctions, and near each camera.";
        return true;
    }

    public bool RecordCalibrationPoint(Vector3 worldPosition)
    {
        if (_calibrationGraph == null)
        {
            Status = "Start route setup first.";
            return false;
        }

        if (_positions == null ||
            !TryGetReferenceFrame(out Vector3 origin, out Vector3 xAxis, out Vector3 zAxis))
        {
            Status = "CAM_001 / CAM_002 reference tracking was lost.";
            return false;
        }

        Vector3 local = WorldToLocal(origin, xAxis, zAxis, worldPosition);
        int currentNode = FindExistingCalibrationNode(local);

        if (currentNode < 0)
        {
            currentNode = _calibrationGraph.nodes.Count;
            _calibrationGraph.nodes.Add(
                new WaypointNode
                {
                    id = currentNode,
                    localPosition = local
                });
        }

        if (_lastCalibrationNode >= 0 &&
            _lastCalibrationNode != currentNode &&
            !HasEdge(_calibrationGraph, _lastCalibrationNode, currentNode))
        {
            _calibrationGraph.edges.Add(
                new WaypointEdge
                {
                    a = _lastCalibrationNode,
                    b = currentNode
                });
        }

        _lastCalibrationNode = currentNode;

        Status =
            "Waypoint " + (currentNode + 1) + " recorded · " +
            _calibrationGraph.nodes.Count + " points · " +
            _calibrationGraph.edges.Count + " connections";

        return true;
    }

    public bool FinishCalibration()
    {
        if (_calibrationGraph == null)
        {
            Status = "No route setup is active.";
            return false;
        }

        if (_calibrationGraph.nodes.Count < 2 ||
            _calibrationGraph.edges.Count < 1)
        {
            Status = "Record at least two connected route points.";
            return false;
        }

        if (_positions == null || string.IsNullOrWhiteSpace(_positions.Site))
        {
            Status = "No active location site.";
            return false;
        }

        PlayerPrefs.SetString(
            GraphKeyPrefix + _positions.Site,
            JsonUtility.ToJson(_calibrationGraph));
        PlayerPrefs.Save();

        _graph = _calibrationGraph;
        _calibrationGraph = null;
        _lastCalibrationNode = -1;
        _loadedSite = _positions.Site;

        EnsureReady();

        if (State == NavigationState.Ready)
            Status = "Waypoint route saved for " + _positions.Site + ": " + _graph.nodes.Count + " points.";

        return State == NavigationState.Ready;
    }

    public void CancelCalibration()
    {
        _calibrationGraph = null;
        _lastCalibrationNode = -1;
        EnsureReady();
    }

    public void ClearCalibration()
    {
        if (_positions == null || string.IsNullOrWhiteSpace(_positions.Site))
            return;

        PlayerPrefs.DeleteKey(GraphKeyPrefix + _positions.Site);
        PlayerPrefs.Save();

        _graph = new WaypointGraph();
        _calibrationGraph = null;
        _lastCalibrationNode = -1;
        _loadedSite = _positions.Site;

        State = NavigationState.SceneSetupRequired;
        Status = "Waypoint route cleared for " + _positions.Site + ".";
    }

    private void RefreshGraphForCurrentSite()
    {
        if (_positions == null || string.IsNullOrWhiteSpace(_positions.Site))
            return;

        if (_loadedSite == _positions.Site && _graph != null)
            return;

        _loadedSite = _positions.Site;
        string json = PlayerPrefs.GetString(GraphKeyPrefix + _positions.Site, "");

        if (string.IsNullOrWhiteSpace(json))
        {
            _graph = new WaypointGraph();
            return;
        }

        try
        {
            _graph = JsonUtility.FromJson<WaypointGraph>(json) ?? new WaypointGraph();
            if (_graph.nodes == null) _graph.nodes = new List<WaypointNode>();
            if (_graph.edges == null) _graph.edges = new List<WaypointEdge>();
        }
        catch (Exception ex)
        {
            Debug.LogException(ex);
            _graph = new WaypointGraph();
            State = NavigationState.Failed;
            Status = "Saved waypoint route could not be loaded.";
        }
    }

    private int FindExistingCalibrationNode(Vector3 localPosition)
    {
        if (_calibrationGraph == null || _calibrationGraph.nodes == null)
            return -1;

        for (int i = 0; i < _calibrationGraph.nodes.Count; i++)
        {
            if (HorizontalDistance(
                    _calibrationGraph.nodes[i].localPosition,
                    localPosition) <= RevisitMergeRadius)
            {
                return i;
            }
        }

        return -1;
    }

    private static bool HasEdge(WaypointGraph graph, int a, int b)
    {
        if (graph == null || graph.edges == null)
            return false;

        for (int i = 0; i < graph.edges.Count; i++)
        {
            WaypointEdge edge = graph.edges[i];
            if ((edge.a == a && edge.b == b) ||
                (edge.a == b && edge.b == a))
                return true;
        }

        return false;
    }

    private static int FindNearestNode(Vector3 worldPosition, Vector3[] nodes)
    {
        int best = -1;
        float bestDistance = float.MaxValue;

        for (int i = 0; i < nodes.Length; i++)
        {
            float distance = HorizontalDistance(worldPosition, nodes[i]);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = i;
            }
        }

        return best;
    }

    private List<int> FindShortestPath(
        int start,
        int goal,
        Vector3[] worldPositions)
    {
        int count = _graph.nodes.Count;
        var distances = new float[count];
        var previous = new int[count];
        var visited = new bool[count];

        for (int i = 0; i < count; i++)
        {
            distances[i] = float.PositiveInfinity;
            previous[i] = -1;
        }

        distances[start] = 0f;

        for (int step = 0; step < count; step++)
        {
            int current = -1;
            float best = float.PositiveInfinity;

            for (int i = 0; i < count; i++)
            {
                if (!visited[i] && distances[i] < best)
                {
                    best = distances[i];
                    current = i;
                }
            }

            if (current < 0 || current == goal)
                break;

            visited[current] = true;

            for (int i = 0; i < _graph.edges.Count; i++)
            {
                WaypointEdge edge = _graph.edges[i];
                int neighbor = -1;

                if (edge.a == current) neighbor = edge.b;
                else if (edge.b == current) neighbor = edge.a;

                if (neighbor < 0 || neighbor >= count || visited[neighbor])
                    continue;

                float candidate =
                    distances[current] +
                    HorizontalDistance(
                        worldPositions[current],
                        worldPositions[neighbor]);

                if (candidate < distances[neighbor])
                {
                    distances[neighbor] = candidate;
                    previous[neighbor] = current;
                }
            }
        }

        if (float.IsPositiveInfinity(distances[goal]))
            return null;

        var reversed = new List<int>();
        int cursor = goal;

        while (cursor >= 0)
        {
            reversed.Add(cursor);
            if (cursor == start) break;
            cursor = previous[cursor];
        }

        if (reversed[reversed.Count - 1] != start)
            return null;

        reversed.Reverse();
        return reversed;
    }

    private bool TryGetReferenceFrame(
        out Vector3 origin,
        out Vector3 xAxis,
        out Vector3 zAxis)
    {
        origin = default;
        xAxis = default;
        zAxis = default;

        if (_positions == null ||
            !_positions.TryGetPosition(ReferenceCameraA, out Vector3 a) ||
            !_positions.TryGetPosition(ReferenceCameraB, out Vector3 b))
        {
            return false;
        }

        Vector3 horizontal = b - a;
        horizontal.y = 0f;

        if (horizontal.sqrMagnitude < 0.25f)
            return false;

        origin = a;
        xAxis = horizontal.normalized;
        zAxis = Vector3.Cross(xAxis, Vector3.up).normalized;
        return true;
    }

    private static Vector3 LocalToWorld(
        Vector3 origin,
        Vector3 xAxis,
        Vector3 zAxis,
        Vector3 local)
    {
        return origin +
               xAxis * local.x +
               Vector3.up * local.y +
               zAxis * local.z;
    }

    private static Vector3 WorldToLocal(
        Vector3 origin,
        Vector3 xAxis,
        Vector3 zAxis,
        Vector3 world)
    {
        Vector3 offset = world - origin;
        return new Vector3(
            Vector3.Dot(offset, xAxis),
            Vector3.Dot(offset, Vector3.up),
            Vector3.Dot(offset, zAxis));
    }

    private static float HorizontalDistance(Vector3 a, Vector3 b)
    {
        float dx = a.x - b.x;
        float dz = a.z - b.z;
        return Mathf.Sqrt(dx * dx + dz * dz);
    }
}
