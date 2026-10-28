using System.Collections.Generic;
using Meta.XR.MRUtilityKit;
using UnityEngine;
using UnityEngine.AI;

[DisallowMultipleComponent]
public class QrRoutePlanner : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Runtime NavMesh bootstrapper that builds room NavMesh from MRUK geometry.")]
    [SerializeField] private RoomNavMeshBootstrap navMeshBootstrap;

    [Tooltip("Route renderer used for floor path visualization.")]
    [SerializeField] private QrRouteRenderer routeRenderer;

    [Tooltip("Optional headset transform override. Defaults to MainCamera transform.")]
    [SerializeField] private Transform headsetTransform;

    [Header("Destination (Wall QR)")]
    [Tooltip("Desired horizontal stand-off distance in front of the QR code.")]
    [SerializeField] private float standOffDistance = 0.9f;

    [Tooltip("Minimum distance from NavMesh edges/walls for target safety.")]
    [SerializeField] private float minWallClearance = 0.28f;

    [Tooltip("Radius used when sampling a walkable NavMesh point.")]
    [SerializeField] private float targetSampleRadius = 0.8f;

    [Tooltip("Additional directional samples (in pairs) around the QR forward vector.")]
    [SerializeField] private int extraDirectionalSamples = 8;

    [Tooltip("Degrees between sampled fallback directions.")]
    [SerializeField] private float directionSampleStepDegrees = 30f;

    [Tooltip("Height used for downward floor probe around the QR.")]
    [SerializeField] private float floorProbeHeight = 1.6f;

    [Tooltip("Maximum distance for downward floor probe.")]
    [SerializeField] private float floorProbeDistance = 3.5f;

    [Header("Path Recalculation")]
    [Tooltip("Minimum seconds between path recalculations.")]
    [SerializeField] private float updateIntervalSeconds = 0.35f;

    [Tooltip("Minimum headset movement (meters) before recalculating the route.")]
    [SerializeField] private float updateMoveThreshold = 0.2f;

    [Tooltip("Sample radius used to project headset position onto NavMesh.")]
    [SerializeField] private float headsetSampleRadius = 0.7f;

    [Tooltip("Arrival threshold measured on horizontal distance.")]
    [SerializeField] private float arrivalDistance = 0.6f;

    private readonly NavMeshPath _path = new NavMeshPath();

    private MRUKTrackable _activeTrackable;
    private string _activePayload;
    private bool _hasTarget;
    private bool _forceRecalculate;
    private float _lastRouteTime = -999f;
    private float _lastNoDestinationWarnTime = -999f;
    private Vector3 _lastHeadsetNavPos;

    private QrCameraScanner _scanner;

    private void Awake()
    {
        if (navMeshBootstrap == null)
            navMeshBootstrap = FindFirstObjectByType<RoomNavMeshBootstrap>();

        if (routeRenderer == null)
            routeRenderer = GetComponent<QrRouteRenderer>();

        if (routeRenderer == null)
            routeRenderer = gameObject.AddComponent<QrRouteRenderer>();
    }

    private void OnEnable()
    {
        _scanner = FindFirstObjectByType<QrCameraScanner>();
        if (_scanner != null)
        {
            _scanner.CameraQrSelected += OnCameraQrSelected;
            _scanner.CameraQrRemoved += OnCameraQrRemoved;
        }
        else
        {
            Debug.LogWarning(
                "QrRoutePlanner: QrCameraScanner not found. " +
                "Route planning will stay idle.");
        }
    }

    private void OnDisable()
    {
        if (_scanner != null)
        {
            _scanner.CameraQrSelected -= OnCameraQrSelected;
            _scanner.CameraQrRemoved -= OnCameraQrRemoved;
        }

        if (routeRenderer != null)
            routeRenderer.Hide();
    }

    private void Update()
    {
        if (!_hasTarget)
            return;

        if (_activeTrackable == null)
        {
            ClearTargetInternal();
            return;
        }

        Transform head = ResolveHeadsetTransform();
        if (head == null)
            return;

        if (navMeshBootstrap == null || !navMeshBootstrap.IsNavMeshReady)
        {
            if (routeRenderer != null)
                routeRenderer.Hide();
            return;
        }

        if (!NavMesh.SamplePosition(
                head.position,
                out NavMeshHit headHit,
                headsetSampleRadius,
                NavMesh.AllAreas))
        {
            return;
        }

        bool movedEnough =
            QrNavigationMath.HorizontalDistance(_lastHeadsetNavPos, headHit.position) >= updateMoveThreshold;
        bool timedOut = Time.unscaledTime - _lastRouteTime >= updateIntervalSeconds;
        if (!_forceRecalculate && !movedEnough && !timedOut)
            return;

        _lastHeadsetNavPos = headHit.position;
        _lastRouteTime = Time.unscaledTime;
        _forceRecalculate = false;

        if (!TryResolveTargetFloorPoint(headHit.position, _activeTrackable.transform, out Vector3 floorTarget))
        {
            if (routeRenderer != null)
                routeRenderer.Hide();

            if (Time.unscaledTime - _lastNoDestinationWarnTime > 3f)
            {
                _lastNoDestinationWarnTime = Time.unscaledTime;
                Debug.LogWarning(
                    $"QrRoutePlanner: Could not find a safe walkable destination for {_activePayload}.");
            }
            return;
        }

        if (QrNavigationMath.HorizontalDistance(headHit.position, floorTarget) <= arrivalDistance)
        {
            if (routeRenderer != null)
                routeRenderer.Hide();
            return;
        }

        bool pathFound = NavMesh.CalculatePath(headHit.position, floorTarget, NavMesh.AllAreas, _path);
        if (!pathFound || _path.status != NavMeshPathStatus.PathComplete || _path.corners == null || _path.corners.Length < 2)
        {
            if (routeRenderer != null)
                routeRenderer.Hide();
            return;
        }

        routeRenderer.RenderPath(_path.corners);
    }

    private Transform ResolveHeadsetTransform()
    {
        if (headsetTransform != null)
            return headsetTransform;

        if (Camera.main != null)
            return Camera.main.transform;

        Camera[] cameras = FindObjectsByType<Camera>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < cameras.Length; i++)
        {
            if (cameras[i] != null && cameras[i].enabled)
                return cameras[i].transform;
        }

        return null;
    }

    private void OnCameraQrSelected(string payload, MRUKTrackable trackable)
    {
        if (trackable == null)
            return;

        _hasTarget = true;
        _activePayload = payload;
        _activeTrackable = trackable;
        _forceRecalculate = true;
    }

    private void OnCameraQrRemoved(MRUKTrackable trackable)
    {
        if (trackable == null)
            return;

        if (_activeTrackable == trackable)
            ClearTargetInternal();
    }

    private void ClearTargetInternal()
    {
        _hasTarget = false;
        _activePayload = null;
        _activeTrackable = null;
        _forceRecalculate = false;

        if (routeRenderer != null)
            routeRenderer.Hide();
    }

    private bool TryResolveTargetFloorPoint(
        Vector3 headsetNavPos,
        Transform qrTransform,
        out Vector3 floorTarget)
    {
        floorTarget = Vector3.zero;

        if (qrTransform == null)
            return false;

        Vector3 qrPos = qrTransform.position;
        List<Vector3> directions = QrNavigationMath.BuildDirectionPriority(
            qrTransform.forward,
            extraDirectionalSamples,
            directionSampleStepDegrees);

        float bestScore = float.MaxValue;
        bool found = false;
        Vector3 bestTarget = Vector3.zero;
        NavMeshPath candidatePath = new NavMeshPath();

        for (int i = 0; i < directions.Count; i++)
        {
            Vector3 dir = QrNavigationMath.FlattenDirection(directions[i]);
            Vector3 standOffPoint = qrPos + dir * standOffDistance;
            Vector3 floorProbeStart = standOffPoint + Vector3.up * floorProbeHeight;
            Vector3 sampleOrigin = standOffPoint;

            if (Physics.Raycast(
                    floorProbeStart,
                    Vector3.down,
                    out RaycastHit floorHit,
                    floorProbeDistance,
                    Physics.DefaultRaycastLayers,
                    QueryTriggerInteraction.Ignore))
            {
                sampleOrigin = floorHit.point;
            }

            if (!NavMesh.SamplePosition(
                    sampleOrigin,
                    out NavMeshHit navHit,
                    targetSampleRadius,
                    NavMesh.AllAreas))
            {
                continue;
            }

            Vector3 candidate = navHit.position;
            if (!IsSafeDestination(candidate, qrPos))
                continue;

            if (!NavMesh.CalculatePath(headsetNavPos, candidate, NavMesh.AllAreas, candidatePath) ||
                candidatePath.status != NavMeshPathStatus.PathComplete)
            {
                continue;
            }

            float score = EstimatePathLength(candidatePath);
            if (score < bestScore)
            {
                bestScore = score;
                bestTarget = candidate;
                found = true;
            }
        }

        if (!found)
            return false;

        floorTarget = bestTarget;
        return true;
    }

    private bool IsSafeDestination(Vector3 candidate, Vector3 qrPos)
    {
        if (QrNavigationMath.HorizontalDistance(candidate, qrPos) < minWallClearance)
            return false;

        if (NavMesh.FindClosestEdge(candidate, out NavMeshHit edgeHit, NavMesh.AllAreas))
        {
            if (edgeHit.distance < minWallClearance)
                return false;
        }

        return true;
    }

    private float EstimatePathLength(NavMeshPath path)
    {
        if (path == null || path.corners == null || path.corners.Length < 2)
            return float.MaxValue;

        float length = 0f;
        for (int i = 1; i < path.corners.Length; i++)
            length += Vector3.Distance(path.corners[i - 1], path.corners[i]);

        return length;
    }
}
