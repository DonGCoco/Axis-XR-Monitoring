using System.Collections;
using System.Reflection;
using Meta.XR.MRUtilityKit;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Thin app layer over Meta MRUK SceneNavigation + Unity NavMesh.
/// Camera positions remain OVRSpatialAnchors; this service only turns the
/// Quest room model into a walkable route so Locate does not point through walls.
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

    private SceneNavigation _sceneNavigation;
    private readonly NavMeshPath _path = new NavMeshPath();
    private bool _initializationStarted;
    private bool _building;

    // Space Setup may be completed after the first launch.
    // A manual retry avoids restarting the app just to load the new scene model.

    public NavigationState State { get; private set; } = NavigationState.Idle;
    public string Status { get; private set; } = "Navigation map not loaded";

    public void EnsureReady()
    {
        if (_initializationStarted || State == NavigationState.Ready)
            return;

        _initializationStarted = true;
        StartCoroutine(InitializeWhenMrukIsReady());
    }

    public void ReloadScene()
    {
        if (_building || State == NavigationState.LoadingScene)
            return;

        if (!_initializationStarted)
        {
            EnsureReady();
            return;
        }

        if (MRUK.Instance == null)
        {
            State = NavigationState.LoadingScene;
            Status = "MRUK is not ready yet.";
            return;
        }

        State = NavigationState.LoadingScene;
        LoadSceneFromDevice();
    }

    private IEnumerator InitializeWhenMrukIsReady()
    {
        Status = "Waiting for Meta MRUK...";
        while (MRUK.Instance == null)
            yield return null;

        ConfigureSceneNavigation();

        MRUK.Instance.RegisterSceneLoadedCallback(OnSceneLoaded);

        if (MRUK.Instance.Rooms != null && MRUK.Instance.Rooms.Count > 0)
        {
            BuildNavMesh();
            yield break;
        }

        LoadSceneFromDevice();
    }

    private void ConfigureSceneNavigation()
    {
        _sceneNavigation = GetComponent<SceneNavigation>();
        if (_sceneNavigation == null)
            _sceneNavigation = gameObject.AddComponent<SceneNavigation>();

        // Match Meta's SceneNavigation sample pattern, but build explicitly
        // after the Quest scene model is available.
        _sceneNavigation.BuildOnSceneLoaded = MRUK.RoomFilter.None;
        _sceneNavigation.UseSceneData = true;
        _sceneNavigation.CustomAgent = false;
        _sceneNavigation.NavigableSurfaces = MRUKAnchor.SceneLabels.FLOOR;
        _sceneNavigation.SceneObstacles =
            MRUKAnchor.SceneLabels.CEILING |
            MRUKAnchor.SceneLabels.WALL_FACE |
            MRUKAnchor.SceneLabels.TABLE |
            MRUKAnchor.SceneLabels.COUCH |
            MRUKAnchor.SceneLabels.DOOR_FRAME |
            MRUKAnchor.SceneLabels.WINDOW_FRAME |
            MRUKAnchor.SceneLabels.OTHER |
            MRUKAnchor.SceneLabels.STORAGE |
            MRUKAnchor.SceneLabels.BED |
            MRUKAnchor.SceneLabels.SCREEN |
            MRUKAnchor.SceneLabels.LAMP |
            MRUKAnchor.SceneLabels.PLANT |
            MRUKAnchor.SceneLabels.WALL_ART |
            MRUKAnchor.SceneLabels.INVISIBLE_WALL_FACE;
        _sceneNavigation.Layers = 1 << 0;

        // SDK 205 serializes this field in Meta's official SceneNavigation
        // prefab but does not expose it as a documented public property.
        // Enable it so a NavMesh can connect mapped rooms through doorways.
        FieldInfo connectRoomsField =
            typeof(SceneNavigation).GetField(
                "connectRoomsInNavMesh",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        connectRoomsField?.SetValue(_sceneNavigation, true);

        _sceneNavigation.OnNavMeshInitialized.RemoveListener(OnNavMeshInitialized);
        _sceneNavigation.OnNavMeshInitialized.AddListener(OnNavMeshInitialized);
    }

    private async void LoadSceneFromDevice()
    {
        if (!this || MRUK.Instance == null)
            return;

        State = NavigationState.LoadingScene;
        Status = "Loading the Quest room map for wall-aware navigation...";

        MRUK.LoadDeviceResult result;
        try
        {
            // If the lab has not been mapped yet, Meta opens Space Setup.
            // The scene model is needed only for pathfinding, not for camera anchors.
            result = await MRUK.Instance.LoadSceneFromDevice(
                requestSceneCaptureIfNoDataFound: true,
                removeMissingRooms: true,
                sceneModel: MRUK.SceneModel.V1);
        }
        catch (System.Exception ex)
        {
            Debug.LogException(ex);
            State = NavigationState.Failed;
            Status = "Could not load the Quest room map.";
            return;
        }

        if (!this)
            return;

        if (result == MRUK.LoadDeviceResult.Success)
        {
            BuildNavMesh();
            return;
        }

        if (result == MRUK.LoadDeviceResult.NoRoomsFound ||
            result == MRUK.LoadDeviceResult.NoScenePermission)
        {
            State = NavigationState.SceneSetupRequired;
            Status = "Space Setup is required once for wall-aware navigation.";
            return;
        }

        State = NavigationState.Failed;
        Status = "Room map load failed: " + result;
    }

    private void OnSceneLoaded()
    {
        if (!this || MRUK.Instance == null ||
            MRUK.Instance.Rooms == null || MRUK.Instance.Rooms.Count == 0)
        {
            return;
        }

        BuildNavMesh();
    }

    private void BuildNavMesh()
    {
        if (_sceneNavigation == null || _building || State == NavigationState.Ready)
            return;

        _building = true;
        State = NavigationState.BuildingNavMesh;
        Status = "Building a walkable route from the mapped rooms...";

        try
        {
            _sceneNavigation.BuildSceneNavMesh();
        }
        catch (System.Exception ex)
        {
            Debug.LogException(ex);
            _building = false;
            State = NavigationState.Failed;
            Status = "Could not build the walkable route map.";
        }
    }

    private void OnNavMeshInitialized()
    {
        _building = false;
        State = NavigationState.Ready;
        int rooms = MRUK.Instance != null && MRUK.Instance.Rooms != null
            ? MRUK.Instance.Rooms.Count
            : 0;
        Status = "NavMesh ready | mapped rooms: " + rooms;
    }

    public bool TryGetRoute(Vector3 headsetPosition, Vector3 cameraAnchorPosition, out Route route)
    {
        route = default;

        if (State != NavigationState.Ready)
            return false;

        if (!NavMesh.SamplePosition(
                headsetPosition,
                out NavMeshHit startHit,
                3.0f,
                NavMesh.AllAreas))
        {
            Status = "Move onto a mapped walkable area.";
            return false;
        }

        // Camera anchors are normally above the floor, so project the target
        // to the nearest walkable point below/near the physical camera.
        if (!NavMesh.SamplePosition(
                cameraAnchorPosition,
                out NavMeshHit endHit,
                4.0f,
                NavMesh.AllAreas))
        {
            Status = "The camera is outside the mapped walkable area.";
            return false;
        }

        if (!NavMesh.CalculatePath(
                startHit.position,
                endHit.position,
                NavMesh.AllAreas,
                _path) ||
            _path.status != NavMeshPathStatus.PathComplete ||
            _path.corners == null ||
            _path.corners.Length < 2)
        {
            Status = "No complete route. Verify mapped floors / doorway connections in Space Setup.";
            return false;
        }

        float totalDistance = 0f;
        for (int i = 1; i < _path.corners.Length; i++)
            totalDistance += Vector3.Distance(_path.corners[i - 1], _path.corners[i]);

        Vector3 next = _path.corners[1];
        for (int i = 1; i < _path.corners.Length - 1; i++)
        {
            Vector3 flatDelta = _path.corners[i] - startHit.position;
            flatDelta.y = 0f;
            if (flatDelta.magnitude > 0.65f)
            {
                next = _path.corners[i];
                break;
            }

            next = _path.corners[i + 1];
        }

        route = new Route
        {
            nextWaypoint = next,
            totalDistance = totalDistance,
            cornerCount = _path.corners.Length
        };

        Status = "Wall-aware navigation ready";
        return true;
    }

    private void OnDestroy()
    {
        if (_sceneNavigation != null)
            _sceneNavigation.OnNavMeshInitialized.RemoveListener(OnNavMeshInitialized);
    }
}
