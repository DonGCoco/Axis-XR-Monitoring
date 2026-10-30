using System;
using System.Collections;
using System.Collections.Generic;
using Meta.XR.MRUtilityKit;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using System.Threading.Tasks;

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

    [Header("Scan Feedback")]
    [SerializeField] private bool showWalkableFloorOverlay = true;
    [SerializeField] private bool showObstacleVolumeOverlays = true;

    private MRUK _mruk;
    private NavMeshSurface _surface;
    private Coroutine _bootstrapRoutine;
    private Coroutine _rebuildRoutine;
    private bool _subscribedToTrackables;
    private SceneNavigation _sceneNavigation;
    private bool _isLoadingRoom;
    private float _nextScanRequestTime;
    private Coroutine _scanCooldownRoutine;
    private GameObject _walkableOverlay;
    private Material _walkableMaterial;
    private Material _obstacleMaterial;
    private readonly List<GameObject> _obstacleOverlays = new List<GameObject>();

    public bool IsNavMeshReady { get; private set; }

    public event Action NavMeshBuilt;
    public event Action<string> RoomStatusChanged;

    private void Awake()
    {
        _surface = GetComponent<NavMeshSurface>();
        if (_surface == null)
            _surface = gameObject.AddComponent<NavMeshSurface>();

        ConfigureSurface();
        _sceneNavigation = GetComponent<SceneNavigation>();
        if (_sceneNavigation == null) _sceneNavigation = gameObject.AddComponent<SceneNavigation>();
        _sceneNavigation.UseSceneData = true;
        _sceneNavigation.BuildOnSceneLoaded = MRUK.RoomFilter.AllRooms;
        _sceneNavigation.NavigableSurfaces = MRUKAnchor.SceneLabels.FLOOR;
        _sceneNavigation.SceneObstacles = MRUKAnchor.SceneLabels.WALL_FACE |
            MRUKAnchor.SceneLabels.INVISIBLE_WALL_FACE | MRUKAnchor.SceneLabels.TABLE |
            MRUKAnchor.SceneLabels.COUCH | MRUKAnchor.SceneLabels.BED |
            MRUKAnchor.SceneLabels.STORAGE |
            MRUKAnchor.SceneLabels.SCREEN | MRUKAnchor.SceneLabels.PLANT |
            MRUKAnchor.SceneLabels.LAMP | MRUKAnchor.SceneLabels.OTHER;
        _sceneNavigation.AgentRadius = 0.32f;
        _sceneNavigation.AgentHeight = 1.7f;
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

    public async void RequestRoomScan()
    {
        if (_isLoadingRoom)
        {
            RoomStatusChanged?.Invoke("正在读取房间数据，请稍候…");
            return;
        }
        float now = Time.realtimeSinceStartup;
        if (now < _nextScanRequestTime)
        {
            int remainingSeconds = Mathf.CeilToInt(_nextScanRequestTime - now);
            RoomStatusChanged?.Invoke($"Quest 正在限制空间扫描请求，请等待 {remainingSeconds} 秒再试");
            StartScanCooldownFeedback();
            return;
        }

        _isLoadingRoom = true;
        _nextScanRequestTime = now + 15f;
        StartScanCooldownFeedback();
        IsNavMeshReady = false;
        RoomStatusChanged?.Invoke("请在 Quest 空间设置中扫描房间；返回后会更新导航网格…");
        string previousScene = null;
        try
        {
            float waitStarted = Time.realtimeSinceStartup;
            while (MRUK.Instance == null && Time.realtimeSinceStartup - waitStarted < 10f)
                await Task.Yield();
            if (MRUK.Instance == null)
            {
                RoomStatusChanged?.Invoke("MRUK 尚未启动，请稍后重试");
                return;
            }

            // Keep a serialized fallback so a failed device refresh cannot leave
            // the app without the last known room map.
            if (MRUK.Instance.Rooms != null && MRUK.Instance.Rooms.Count > 0)
            {
                previousScene = MRUK.Instance.SaveSceneToJsonString(true, null);
            }

            Debug.Log("RoomNavMeshBootstrap: Requesting Quest Space Setup for a fresh room capture.");
            bool setupCompleted = await OVRScene.RequestSpaceSetup();
            if (!setupCompleted)
            {
                _nextScanRequestTime = Time.realtimeSinceStartup + 30f;
                StartScanCooldownFeedback();
                Debug.LogWarning("RoomNavMeshBootstrap: Quest Space Setup request failed or was rate-limited. Wait before retrying.");
                RoomStatusChanged?.Invoke("Quest 未启动空间设置；正在重读设备中已保存的房间地图…");
            }

            if (MRUK.Instance == null)
            {
                RoomStatusChanged?.Invoke("Quest 空间设置已结束，但 MRUK 未运行；请重启应用");
                return;
            }

            RoomStatusChanged?.Invoke("正在读取 Quest 最新房间数据…");
            var result = await MRUK.Instance.LoadSceneFromDevice(false, true);
            Debug.Log($"RoomNavMeshBootstrap: Room scan/load result: {result}");
            if (MRUK.Instance.Rooms != null && MRUK.Instance.Rooms.Count > 0)
            {
                BuildRuntimeNavMesh();
                string scanMessage = setupCompleted
                    ? "已读取 Quest 房间地图"
                    : "系统扫描未启动，已重读 Quest 保存的房间地图（若地图未更新，请在系统 Space Setup 中重新采集）";
                RoomStatusChanged?.Invoke($"{scanMessage}：{MRUK.Instance.Rooms.Count} 个房间 · 导航网格{(IsNavMeshReady ? "就绪" : "未生成")}");
            }
            else
            {
                Debug.LogWarning($"RoomNavMeshBootstrap: Device refresh returned {result} with no room data.");
                if (previousScene != null)
                {
                    await RestorePreviousRoom(previousScene);
                    RoomStatusChanged?.Invoke(IsNavMeshReady
                        ? $"读取新地图失败（{result}），已保留上次房间地图 · 导航就绪"
                        : $"读取新地图失败（{result}），旧导航网格恢复失败");
                }
                else
                {
                    string status = result == MRUK.LoadDeviceResult.NoScenePermission
                        ? "扫描未获权限，请允许 Scene 权限后重试"
                        : result == MRUK.LoadDeviceResult.NoRoomsFound
                            ? "未找到房间，请完成 Quest Space Setup 扫描"
                            : $"房间加载失败：{result}，请重试";
                    RoomStatusChanged?.Invoke(status);
                }
            }
        }
        catch (System.Exception ex)
        {
            if (previousScene != null && MRUK.Instance != null)
            {
                try
                {
                    await RestorePreviousRoom(previousScene);
                    RoomStatusChanged?.Invoke(IsNavMeshReady
                        ? "房间刷新失败，已保留上次地图 · 导航就绪"
                        : "房间刷新失败，旧导航网格恢复失败");
                }
                catch (System.Exception restoreException)
                {
                    RoomStatusChanged?.Invoke("房间刷新与旧地图恢复均失败，请重启应用");
                    Debug.LogError($"RoomNavMeshBootstrap: Room restore failed: {restoreException.Message}");
                }
            }
            else
            {
                RoomStatusChanged?.Invoke("房间扫描失败：请检查 Scene 权限和 Space Setup");
            }
            Debug.LogError($"RoomNavMeshBootstrap: Room scan failed: {ex.Message}");
        }
        finally { _isLoadingRoom = false; }
    }

    private void StartScanCooldownFeedback()
    {
        if (_scanCooldownRoutine != null)
            StopCoroutine(_scanCooldownRoutine);
        _scanCooldownRoutine = StartCoroutine(ScanCooldownFeedbackRoutine());
    }

    private IEnumerator ScanCooldownFeedbackRoutine()
    {
        int lastSeconds = -1;
        while (Time.realtimeSinceStartup < _nextScanRequestTime)
        {
            int remainingSeconds = Mathf.CeilToInt(_nextScanRequestTime - Time.realtimeSinceStartup);
            if (remainingSeconds != lastSeconds && !_isLoadingRoom)
            {
                RoomStatusChanged?.Invoke($"Quest 正在限制空间扫描请求，请等待 {remainingSeconds} 秒再试");
                lastSeconds = remainingSeconds;
            }
            yield return new WaitForSecondsRealtime(0.25f);
        }

        _scanCooldownRoutine = null;
        if (!_isLoadingRoom)
            RoomStatusChanged?.Invoke("冷却结束，可以重新扫描房间；Quest 未弹出扫描界面时，请先在系统 Space Setup 中更新房间");
    }

    private async Task RestorePreviousRoom(string previousScene)
    {
        if (MRUK.Instance == null || string.IsNullOrEmpty(previousScene))
            return;

        // A failed device capture can leave MRUK discovery active. Keep any
        // still-loaded live room intact instead of clearing it during discovery.
        if (MRUK.Instance.Rooms != null && MRUK.Instance.Rooms.Count > 0)
        {
            BuildRuntimeNavMesh();
            return;
        }

        MRUK.LoadDeviceResult restoreResult = await MRUK.Instance.LoadSceneFromJsonString(previousScene, true);
        if (restoreResult != MRUK.LoadDeviceResult.Success)
            Debug.LogError($"RoomNavMeshBootstrap: Failed to restore previous room snapshot: {restoreResult}.");
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

        if (_sceneNavigation != null && MRUK.Instance != null && MRUK.Instance.Rooms != null && MRUK.Instance.Rooms.Count > 0)
        {
            _sceneNavigation.BuildSceneNavMesh();
        }
        else
        {
            ConfigureSurface();
            _surface.BuildNavMesh();
        }

        IsNavMeshReady = NavMesh.CalculateTriangulation().vertices.Length > 0;
        if (!IsNavMeshReady)
        {
            Debug.LogWarning(
                "RoomNavMeshBootstrap: NavMesh build produced no polygons. " +
                "Check MRUK room data, colliders, and layer mask.");
            return;
        }

        Debug.Log("RoomNavMeshBootstrap: Runtime NavMesh built from MRUK room geometry.");
        CreateScanVisualization();
        RoomStatusChanged?.Invoke($"已加载 {(MRUK.Instance != null && MRUK.Instance.Rooms != null ? MRUK.Instance.Rooms.Count : 0)} 个房间 · 导航就绪");
        NavMeshBuilt?.Invoke();
    }

    private void CreateScanVisualization()
    {
        ClearScanVisualization();
        if (showWalkableFloorOverlay)
        {
            NavMeshTriangulation triangulation = NavMesh.CalculateTriangulation();
            if (triangulation.vertices != null && triangulation.vertices.Length >= 3 &&
                triangulation.indices != null && triangulation.indices.Length >= 3)
            {
                Mesh mesh = new Mesh { name = "ScannedWalkableAreas" };
                if (triangulation.vertices.Length > 65535)
                    mesh.indexFormat = IndexFormat.UInt32;
                Vector3[] liftedVertices = triangulation.vertices;
                for (int i = 0; i < liftedVertices.Length; i++)
                    liftedVertices[i].y += 0.025f;
                mesh.vertices = liftedVertices;
                mesh.triangles = triangulation.indices;
                mesh.RecalculateNormals();

                _walkableOverlay = new GameObject("ScanFeedback_WalkableArea");
                _walkableOverlay.AddComponent<MeshFilter>().sharedMesh = mesh;
                // The NavMesh overlay is also the most reliable target surface
                // for controller selection. Quest scene anchors do not always
                // provide Unity colliders, so without this collider the aim ray
                // can never hit the floor even though the NavMesh is ready.
                MeshCollider walkableCollider = _walkableOverlay.AddComponent<MeshCollider>();
                walkableCollider.sharedMesh = mesh;
                walkableCollider.convex = false;
                MeshRenderer renderer = _walkableOverlay.AddComponent<MeshRenderer>();
                _walkableMaterial = CreateFeedbackMaterial(new Color(0.12f, 0.95f, 0.42f, 0.24f));
                renderer.sharedMaterial = _walkableMaterial;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }
        }

        if (!showObstacleVolumeOverlays || _sceneNavigation == null)
            return;

        _obstacleMaterial = CreateFeedbackMaterial(new Color(1f, 0.22f, 0.12f, 0.26f));
        foreach (KeyValuePair<MRUKAnchor, GameObject> entry in _sceneNavigation.Obstacles)
        {
            if (entry.Key == null || entry.Value == null)
                continue;

            NavMeshObstacle obstacle = entry.Value.GetComponent<NavMeshObstacle>();
            if (obstacle == null)
                continue;

            GameObject volume = GameObject.CreatePrimitive(PrimitiveType.Cube);
            volume.name = "ScanFeedback_Obstacle_" + entry.Key.Label;
            volume.transform.SetParent(entry.Value.transform, false);
            volume.transform.localPosition = obstacle.center;
            volume.transform.localRotation = Quaternion.identity;
            volume.transform.localScale = obstacle.size;
            Collider collider = volume.GetComponent<Collider>();
            if (collider != null)
                Destroy(collider);
            Renderer volumeRenderer = volume.GetComponent<Renderer>();
            volumeRenderer.sharedMaterial = _obstacleMaterial;
            volumeRenderer.shadowCastingMode = ShadowCastingMode.Off;
            volumeRenderer.receiveShadows = false;
            _obstacleOverlays.Add(volume);
        }

        Debug.Log($"RoomNavMeshBootstrap: scan visualization created: {triangulationCount} walkable mesh vertices, {_obstacleOverlays.Count} obstacle volumes.");
    }

    private int triangulationCount => _walkableOverlay != null
        ? _walkableOverlay.GetComponent<MeshFilter>().sharedMesh.vertexCount
        : 0;

    private Material CreateFeedbackMaterial(Color color)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null)
            shader = Shader.Find("Sprites/Default");
        if (shader == null)
            return null;

        Material material = new Material(shader);
        material.color = color;
        material.renderQueue = (int)RenderQueue.Transparent;
        if (material.HasProperty("_BaseColor"))
            material.SetColor("_BaseColor", color);
        if (material.HasProperty("_Surface"))
            material.SetFloat("_Surface", 1f);
        if (material.HasProperty("_ZWrite"))
            material.SetFloat("_ZWrite", 0f);
        if (material.HasProperty("_SrcBlend"))
            material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        if (material.HasProperty("_DstBlend"))
            material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        material.SetOverrideTag("RenderType", "Transparent");
        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        return material;
    }

    private void ClearScanVisualization()
    {
        if (_walkableOverlay != null)
        {
            MeshFilter filter = _walkableOverlay.GetComponent<MeshFilter>();
            if (filter != null && filter.sharedMesh != null)
                Destroy(filter.sharedMesh);
            Destroy(_walkableOverlay);
            _walkableOverlay = null;
        }

        for (int i = 0; i < _obstacleOverlays.Count; i++)
        {
            if (_obstacleOverlays[i] != null)
                Destroy(_obstacleOverlays[i]);
        }
        _obstacleOverlays.Clear();

        if (_walkableMaterial != null) Destroy(_walkableMaterial);
        if (_obstacleMaterial != null) Destroy(_obstacleMaterial);
        _walkableMaterial = null;
        _obstacleMaterial = null;
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

    private void OnDestroy()
    {
        ClearScanVisualization();
    }
}
