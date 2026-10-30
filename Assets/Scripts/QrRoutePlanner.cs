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
    [SerializeField] private float minWallClearance = 0.10f;

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
    [SerializeField] private float headsetSampleRadius = 2.25f;

    [Tooltip("Arrival threshold measured on horizontal distance.")]
    [SerializeField] private float arrivalDistance = 0.6f;

    [Header("Spatial Destination Test Mode")]
    [SerializeField] private float selectionRayLength = 12f;
    [SerializeField] private float selectionSampleRadius = 1.25f;

    private NavMeshPath _path;

    private MRUKTrackable _activeTrackable;
    private string _activePayload;
    private Vector3 _manualTarget;
    private bool _isManualTarget;
    private bool _isSelectingTarget;
    private bool _hasTarget;
    private bool _forceRecalculate;
    private float _lastRouteTime = -999f;
    private float _lastNoDestinationWarnTime = -999f;
    private Vector3 _lastHeadsetNavPos;

    private QrCameraScanner _scanner;
    private Transform _trackingSpace;
    private LineRenderer _selectionRay;
    private GameObject _selectionMarker;

    public event System.Action<string> RouteStatusChanged;
    public bool IsSelectingTarget => _isSelectingTarget;

    private void Awake()
    {
        _path = new NavMeshPath();

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
        if (_isSelectingTarget)
            UpdateSpatialSelection();

        if (!_hasTarget)
            return;

        if (!_isManualTarget && _activeTrackable == null)
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

        if (!TrySampleHeadsetPosition(head.position, out NavMeshHit headHit))
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

        Vector3 floorTarget;
        bool hasFloorTarget = _isManualTarget
            ? TryResolveManualTarget(headHit.position, out floorTarget)
            : TryResolveTargetFloorPoint(headHit.position, _activeTrackable.transform, out floorTarget);

        if (!hasFloorTarget)
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
        _isManualTarget = false;
        _activePayload = payload;
        _activeTrackable = trackable;
        _forceRecalculate = true;
        RouteStatusChanged?.Invoke($"已选择二维码 {payload}，正在计算避障路线…");
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
        _isManualTarget = false;
        _forceRecalculate = false;

        if (routeRenderer != null)
            routeRenderer.Hide();
    }

    public bool BeginSpatialTargetSelection()
    {
        if (navMeshBootstrap == null)
            navMeshBootstrap = FindFirstObjectByType<RoomNavMeshBootstrap>();

        if (navMeshBootstrap == null || !navMeshBootstrap.IsNavMeshReady)
        {
            RouteStatusChanged?.Invoke("请先扫描房间，等待导航网格就绪");
            return false;
        }

        _isSelectingTarget = true;
        CreateSelectionVisuals();
        Debug.Log("QrRoutePlanner: spatial target selection started. Aim the right controller at a floor and press its trigger.");
        RouteStatusChanged?.Invoke("指向地面目标点，按右手柄扳机确认");
        return true;
    }

    public void CancelSpatialTargetSelection()
    {
        _isSelectingTarget = false;
        if (_selectionRay != null) _selectionRay.enabled = false;
        if (_selectionMarker != null) _selectionMarker.SetActive(false);
        RouteStatusChanged?.Invoke(_hasTarget ? "目标已设置；正在显示避障路线" : "已取消目标选择");
    }

    private void UpdateSpatialSelection()
    {
        if (navMeshBootstrap == null || !navMeshBootstrap.IsNavMeshReady)
        {
            RouteStatusChanged?.Invoke("导航网格未就绪，请重新扫描房间");
            CancelSpatialTargetSelection();
            return;
        }

        if (!TryGetSelectionRay(out Ray ray))
            return;

        Vector3 rayEnd = ray.origin + ray.direction * selectionRayLength;
        bool hitSurface = TryRaycastRoomSurface(ray, out Vector3 hitPoint);

        if (_selectionRay != null)
        {
            _selectionRay.enabled = true;
            _selectionRay.SetPosition(0, ray.origin);
            _selectionRay.SetPosition(1, hitSurface ? hitPoint : rayEnd);
        }

        Vector3 walkablePoint = Vector3.zero;
        bool validTarget = hitSurface && TryProjectWalkableTarget(hitPoint, out walkablePoint);
        if (_selectionMarker != null)
        {
            _selectionMarker.SetActive(validTarget);
            if (validTarget) _selectionMarker.transform.position = walkablePoint + Vector3.up * 0.035f;
        }

        if (OVRInput.GetDown(OVRInput.Button.PrimaryIndexTrigger, OVRInput.Controller.RTouch))
        {
            Debug.Log($"QrRoutePlanner: target trigger pressed; roomHit={hitSurface}, walkable={validTarget}, point={(hitSurface ? hitPoint.ToString() : "none")}");
            if (!hitSurface)
            {
                RouteStatusChanged?.Invoke("射线没有碰到房间表面；请对准可见地面再按扳机");
                return;
            }

            if (!validTarget)
            {
                RouteStatusChanged?.Invoke("此处不可通行或离障碍太近，请选一处空旷地面");
                return;
            }

            Vector3 start = ResolveHeadsetTransform() != null
                ? ResolveHeadsetTransform().position
                : walkablePoint;
            if (!TrySampleHeadsetPosition(start, out NavMeshHit startHit))
            {
                Debug.LogWarning($"QrRoutePlanner: headset position {start} could not be projected to NavMesh within {GetHeadsetSampleRadius():0.00}m.");
                RouteStatusChanged?.Invoke("当前位置未落在导航区域；请站在绿色可走区域内再选目标");
                return;
            }

            bool pathFound = NavMesh.CalculatePath(startHit.position, walkablePoint, NavMesh.AllAreas, _path);
            if (!pathFound || _path.status != NavMeshPathStatus.PathComplete)
            {
                Debug.LogWarning($"QrRoutePlanner: no complete route from {startHit.position} to {walkablePoint}; path status={_path.status}.");
                RouteStatusChanged?.Invoke("目标与当前位置之间没有连续可行走路线，请选附近绿色区域内的地面");
                return;
            }

            _manualTarget = walkablePoint;
            _isManualTarget = true;
            _activeTrackable = null;
            _activePayload = "空间目标";
            _hasTarget = true;
            _forceRecalculate = true;
            _lastRouteTime = -999f;
            CancelSpatialTargetSelection();
            Debug.Log($"QrRoutePlanner: spatial target set at {walkablePoint}; route path has {(_path.corners != null ? _path.corners.Length : 0)} corners.");
            RouteStatusChanged?.Invoke("目标已设置，路线已避开扫描到的障碍物");
        }
    }

    private bool TryGetSelectionRay(out Ray ray)
    {
        OVRCameraRig rig = FindFirstObjectByType<OVRCameraRig>();
        if (rig != null && rig.trackingSpace != null &&
            OVRInput.GetControllerPositionValid(OVRInput.Controller.RTouch))
        {
            Vector3 localPosition = OVRInput.GetLocalControllerPosition(OVRInput.Controller.RTouch);
            Quaternion localRotation = OVRInput.GetLocalControllerRotation(OVRInput.Controller.RTouch);
            Vector3 origin = rig.trackingSpace.TransformPoint(localPosition);
            Vector3 direction = rig.trackingSpace.TransformDirection(localRotation * Vector3.forward);
            ray = new Ray(origin, direction.normalized);
            return true;
        }

        // Editor/head-gaze fallback makes the target-selection flow inspectable
        // without a tracked Touch controller.
        Camera camera = Camera.main;
        if (camera != null)
        {
            ray = new Ray(camera.transform.position, camera.transform.forward);
            return true;
        }

        ray = default;
        return false;
    }

    private bool TryProjectWalkableTarget(Vector3 point, out Vector3 walkablePoint)
    {
        walkablePoint = Vector3.zero;
        if (!NavMesh.SamplePosition(point, out NavMeshHit navHit, selectionSampleRadius, NavMesh.AllAreas))
            return false;

        if (Vector3.Distance(point, navHit.position) > selectionSampleRadius * 0.8f)
            return false;

        if (NavMesh.FindClosestEdge(navHit.position, out NavMeshHit edgeHit, NavMesh.AllAreas) &&
            edgeHit.distance < minWallClearance)
            return false;

        walkablePoint = navHit.position;
        return true;
    }

    private float GetHeadsetSampleRadius()
    {
        // The headset camera sits well above the floor NavMesh, so sampling it
        // with a sub-meter radius misses the walkable floor on Quest.
        return Mathf.Max(headsetSampleRadius, 2.25f);
    }

    private bool TrySampleHeadsetPosition(Vector3 headsetPosition, out NavMeshHit hit)
    {
        return NavMesh.SamplePosition(
            headsetPosition,
            out hit,
            GetHeadsetSampleRadius(),
            NavMesh.AllAreas);
    }

    private bool TryRaycastRoomSurface(Ray ray, out Vector3 hitPoint)
    {
        hitPoint = Vector3.zero;
        if (Physics.Raycast(
                ray,
                out RaycastHit physicsHit,
                selectionRayLength,
                Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore))
        {
            hitPoint = physicsHit.point;
            return true;
        }

        // MRUK floor planes do not always expose a Unity Collider. Intersect the
        // controller ray with saved room floor anchors as a reliable fallback.
        MRUK mruk = MRUK.Instance;
        if (mruk == null || mruk.Rooms == null)
            return false;

        float nearestDistance = float.MaxValue;
        bool foundFloor = false;
        for (int roomIndex = 0; roomIndex < mruk.Rooms.Count; roomIndex++)
        {
            MRUKRoom room = mruk.Rooms[roomIndex];
            if (room == null || room.FloorAnchors == null)
                continue;

            for (int floorIndex = 0; floorIndex < room.FloorAnchors.Count; floorIndex++)
            {
                MRUKAnchor floor = room.FloorAnchors[floorIndex];
                if (floor == null || !floor.PlaneRect.HasValue)
                    continue;

                Plane floorPlane = new Plane(floor.transform.forward, floor.transform.position);
                if (!floorPlane.Raycast(ray, out float distance) ||
                    distance < 0f || distance > selectionRayLength || distance >= nearestDistance)
                    continue;

                Vector3 candidate = ray.GetPoint(distance);
                Vector3 localCandidate = floor.transform.InverseTransformPoint(candidate);
                bool insideBoundary = floor.PlaneBoundary2D != null && floor.PlaneBoundary2D.Count > 2
                    ? floor.IsPositionInBoundary(new Vector2(localCandidate.x, localCandidate.y))
                    : floor.PlaneRect.Value.Contains(new Vector2(localCandidate.x, localCandidate.y));
                if (!insideBoundary)
                    continue;

                nearestDistance = distance;
                hitPoint = candidate;
                foundFloor = true;
            }
        }

        return foundFloor;
    }

    private bool TryResolveManualTarget(Vector3 headsetNavPos, out Vector3 floorTarget)
    {
        floorTarget = _manualTarget;
        return NavMesh.SamplePosition(_manualTarget, out NavMeshHit hit, selectionSampleRadius, NavMesh.AllAreas) &&
               NavMesh.CalculatePath(headsetNavPos, hit.position, NavMesh.AllAreas, _path) &&
               _path.status == NavMeshPathStatus.PathComplete;
    }

    private void CreateSelectionVisuals()
    {
        if (_selectionRay == null)
        {
            GameObject rayObject = new GameObject("SpatialTargetSelectionRay");
            _selectionRay = rayObject.AddComponent<LineRenderer>();
            _selectionRay.useWorldSpace = true;
            _selectionRay.positionCount = 2;
            _selectionRay.startWidth = 0.008f;
            _selectionRay.endWidth = 0.008f;
            _selectionRay.startColor = new Color(0.2f, 0.8f, 1f, 0.8f);
            _selectionRay.endColor = new Color(0.2f, 0.8f, 1f, 0.8f);
            Shader shader = Shader.Find("Sprites/Default");
            if (shader != null) _selectionRay.material = new Material(shader);
        }

        if (_selectionMarker == null)
        {
            _selectionMarker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            _selectionMarker.name = "SpatialTargetMarker";
            _selectionMarker.transform.localScale = Vector3.one * 0.12f;
            Collider collider = _selectionMarker.GetComponent<Collider>();
            if (collider != null) Destroy(collider);
            Renderer markerRenderer = _selectionMarker.GetComponent<Renderer>();
            Shader shader = Shader.Find("Sprites/Default");
            if (markerRenderer != null && shader != null)
            {
                Material material = new Material(shader);
                material.color = new Color(0.1f, 1f, 0.35f, 0.95f);
                markerRenderer.material = material;
            }
        }

        _selectionRay.enabled = true;
        _selectionMarker.SetActive(false);
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
