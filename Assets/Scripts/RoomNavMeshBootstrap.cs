using System;
using System.Collections;
using Meta.XR.MRUtilityKit;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;

[DisallowMultipleComponent]
public class RoomNavMeshBootstrap : MonoBehaviour
{
    [Header("Room Readiness")]
    [Tooltip("Maximum seconds to wait for MRUK room colliders before forcing a NavMesh build.")]
    [SerializeField] private float maxWaitForRoomSeconds = 12f;

    [Tooltip("Minimum MRUK child collider count used as a heuristic that room geometry is loaded.")]
    [SerializeField] private int minRoomColliderCount = 8;

    [Header("NavMesh Build")]
    [Tooltip("Layer mask used by NavMeshSurface when collecting room colliders/meshes.")]
    [SerializeField] private LayerMask navMeshLayerMask = ~0;

    [Tooltip("If true, trackable updates can trigger a delayed NavMesh rebuild.")]
    [SerializeField] private bool rebuildOnTrackableChanges = true;

    [Tooltip("Delay used to coalesce frequent trackable updates before rebuilding NavMesh.")]
    [SerializeField] private float rebuildDebounceSeconds = 1.25f;

    private MRUK _mruk;
    private NavMeshSurface _surface;
    private Coroutine _bootstrapRoutine;
    private Coroutine _rebuildRoutine;
    private bool _subscribedToTrackables;

    public bool IsNavMeshReady { get; private set; }

    public event Action NavMeshBuilt;

    private void Awake()
    {
        _surface = GetComponent<NavMeshSurface>();
        if (_surface == null)
            _surface = gameObject.AddComponent<NavMeshSurface>();

        ConfigureSurface();
    }

    private void OnEnable()
    {
        _bootstrapRoutine = StartCoroutine(BootstrapRoutine());
    }

    private IEnumerator BootstrapRoutine()
    {
        while (MRUK.Instance == null)
            yield return null;

        _mruk = MRUK.Instance;

        if (_mruk.SceneSettings == null)
        {
            Debug.LogError(
                "RoomNavMeshBootstrap: MRUK.SceneSettings is null. " +
                "Enable MRUK scene setup in SampleScene.");
            yield break;
        }

        if (rebuildOnTrackableChanges)
        {
            _mruk.SceneSettings.TrackableAdded.AddListener(OnTrackableChanged);
            _mruk.SceneSettings.TrackableRemoved.AddListener(OnTrackableChanged);
            _subscribedToTrackables = true;
        }

        float startedAt = Time.unscaledTime;
        while (Time.unscaledTime - startedAt < maxWaitForRoomSeconds)
        {
            if (HasLikelyRoomGeometry())
                break;

            yield return null;
        }

        if (!HasLikelyRoomGeometry())
        {
            Debug.LogWarning(
                "RoomNavMeshBootstrap: Timed out waiting for MRUK room colliders. " +
                "Attempting NavMesh build anyway. Ensure Quest Space Setup room scan is complete.");
        }

        BuildRuntimeNavMesh();
    }

    private void ConfigureSurface()
    {
        if (_surface == null)
            return;

        _surface.collectObjects = CollectObjects.All;
        _surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
        _surface.layerMask = navMeshLayerMask;
    }

    private bool HasLikelyRoomGeometry()
    {
        if (_mruk == null)
            return false;

        Collider[] colliders = _mruk.GetComponentsInChildren<Collider>(true);
        return colliders != null && colliders.Length >= minRoomColliderCount;
    }

    private void OnTrackableChanged(MRUKTrackable _)
    {
        if (!rebuildOnTrackableChanges || !enabled || !gameObject.activeInHierarchy)
            return;

        if (_rebuildRoutine != null)
            StopCoroutine(_rebuildRoutine);

        _rebuildRoutine = StartCoroutine(RebuildAfterDelay());
    }

    private IEnumerator RebuildAfterDelay()
    {
        yield return new WaitForSecondsRealtime(rebuildDebounceSeconds);
        BuildRuntimeNavMesh();
    }

    public void BuildRuntimeNavMesh()
    {
        if (_surface == null)
        {
            Debug.LogError(
                "RoomNavMeshBootstrap: NavMeshSurface missing. " +
                "Attach NavMeshSurface to the same GameObject.");
            IsNavMeshReady = false;
            return;
        }

        ConfigureSurface();
        _surface.BuildNavMesh();

        IsNavMeshReady = NavMesh.CalculateTriangulation().vertices.Length > 0;
        if (!IsNavMeshReady)
        {
            Debug.LogWarning(
                "RoomNavMeshBootstrap: NavMesh build produced no polygons. " +
                "Check MRUK room data, colliders, and layer mask.");
            return;
        }

        Debug.Log("RoomNavMeshBootstrap: Runtime NavMesh built from MRUK room geometry.");
        NavMeshBuilt?.Invoke();
    }

    private void OnDisable()
    {
        if (_bootstrapRoutine != null)
        {
            StopCoroutine(_bootstrapRoutine);
            _bootstrapRoutine = null;
        }

        if (_rebuildRoutine != null)
        {
            StopCoroutine(_rebuildRoutine);
            _rebuildRoutine = null;
        }

        if (_subscribedToTrackables &&
            _mruk != null &&
            _mruk.SceneSettings != null)
        {
            _mruk.SceneSettings.TrackableAdded.RemoveListener(OnTrackableChanged);
            _mruk.SceneSettings.TrackableRemoved.RemoveListener(OnTrackableChanged);
        }

        _subscribedToTrackables = false;
    }
}
