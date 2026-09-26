using System;
using System.Collections;
using System.Collections.Generic;
using Meta.XR.MRUtilityKit;
using UnityEngine;

public class QrCameraScanner : MonoBehaviour
{
    [SerializeField] private CameraApiClient apiClient;
    [SerializeField] private bool showStatusPanel = true;

    [Header("Repeat Scan")]
    [Tooltip("How long the QR must be out of tracking before the same code can trigger again.")]
    [SerializeField] private float rescanResetSeconds = 0.75f;

    [Tooltip("Minimum time between two triggers of the same QR code.")]
    [SerializeField] private float rescanCooldownSeconds = 1.5f;

    private MRUK _mruk;
    private bool _subscribed;
    private CameraStatusPanel _activePanel;
    private AxisMonitoringUI _monitoringUI;

    private readonly List<QrTrackState> _trackedQrs =
        new List<QrTrackState>();

    private class QrTrackState
    {
        public MRUKTrackable trackable;
        public string payload;
        public bool wasTracked;
        public bool armedForRescan;
        public float untrackedSince = -1f;
        public float lastTriggerTime = -999f;
    }

    private void Awake()
    {
        if (apiClient == null)
            apiClient = FindFirstObjectByType<CameraApiClient>();

        _monitoringUI =
            AxisMonitoringUI.EnsureExists(apiClient);
    }

    private void OnEnable()
    {
        StartCoroutine(WaitForMRUKAndSubscribe());
    }

    private IEnumerator WaitForMRUKAndSubscribe()
    {
        while (MRUK.Instance == null)
            yield return null;

        _mruk = MRUK.Instance;

        if (_mruk.SceneSettings == null)
        {
            Debug.LogError("QrCameraScanner: MRUK.SceneSettings is null.");
            yield break;
        }

        _mruk.SceneSettings.TrackableAdded.AddListener(OnTrackableAdded);
        _mruk.SceneSettings.TrackableRemoved.AddListener(OnTrackableRemoved);
        _subscribed = true;

        Debug.Log("QrCameraScanner ready. Waiting for CAM_* QR codes...");
    }

    private void OnDisable()
    {
        if (_subscribed &&
            _mruk != null &&
            _mruk.SceneSettings != null)
        {
            _mruk.SceneSettings.TrackableAdded.RemoveListener(OnTrackableAdded);
            _mruk.SceneSettings.TrackableRemoved.RemoveListener(OnTrackableRemoved);
        }

        _subscribed = false;
        _trackedQrs.Clear();
    }

    private void Update()
    {
        float now = Time.unscaledTime;

        for (int i = _trackedQrs.Count - 1; i >= 0; i--)
        {
            QrTrackState state = _trackedQrs[i];

            if (state.trackable == null)
            {
                _trackedQrs.RemoveAt(i);
                continue;
            }

            bool isTracked = state.trackable.IsTracked;

            if (!isTracked)
            {
                if (state.wasTracked)
                {
                    state.wasTracked = false;
                    state.untrackedSince = now;
                    state.armedForRescan = false;
                }

                if (!state.armedForRescan &&
                    state.untrackedSince >= 0f &&
                    now - state.untrackedSince >= rescanResetSeconds)
                {
                    state.armedForRescan = true;
                    Debug.Log($"QR RE-SCAN ARMED: {state.payload}");
                }

                continue;
            }

            if (!state.wasTracked)
            {
                state.wasTracked = true;

                if (state.armedForRescan &&
                    now - state.lastTriggerTime >= rescanCooldownSeconds)
                {
                    state.armedForRescan = false;
                    state.untrackedSince = -1f;
                    state.lastTriggerTime = now;

                    Debug.Log($"QR RE-SCANNED: {state.payload}");
                    HandleCameraPayload(state.payload);
                }
            }
        }
    }

    private void OnTrackableAdded(MRUKTrackable trackable)
    {
        if (!TryGetCameraPayload(trackable, out string payload))
            return;

        Debug.Log($"Axis camera QR detected: {payload}");

        QrTrackState existing =
            _trackedQrs.Find(state => state.trackable == trackable);

        if (existing == null)
        {
            _trackedQrs.Add(
                new QrTrackState
                {
                    trackable = trackable,
                    payload = payload,
                    wasTracked = trackable.IsTracked,
                    armedForRescan = false,
                    untrackedSince = -1f,
                    lastTriggerTime = Time.unscaledTime
                });
        }
        else
        {
            existing.payload = payload;
            existing.wasTracked = trackable.IsTracked;
            existing.armedForRescan = false;
            existing.untrackedSince = -1f;
            existing.lastTriggerTime = Time.unscaledTime;
        }

        HandleCameraPayload(payload);
    }

    private bool TryGetCameraPayload(
        MRUKTrackable trackable,
        out string payload)
    {
        payload = null;

        if (trackable == null)
            return false;

        if (trackable.TrackableType != OVRAnchor.TrackableType.QRCode)
            return false;

        string rawPayload = trackable.MarkerPayloadString;

        if (string.IsNullOrWhiteSpace(rawPayload))
        {
            Debug.LogWarning(
                "QR code detected, but it does not contain a text payload.");
            return false;
        }

        rawPayload = rawPayload.Trim();

        if (!rawPayload.StartsWith("CAM_"))
        {
            Debug.Log($"Ignoring non-camera QR payload: {rawPayload}");
            return false;
        }

        payload = rawPayload;
        return true;
    }

    private void HandleCameraPayload(string payload)
    {
        if (_monitoringUI == null)
            _monitoringUI = AxisMonitoringUI.EnsureExists(apiClient);

        // Keep the old panel only as a fallback if the new UX manager
        // could not be created.
        if (_monitoringUI == null && showStatusPanel)
        {
            try
            {
                if (_activePanel == null)
                    _activePanel = CameraStatusPanel.CreateInFrontOfUser();
                else
                    _activePanel.PlaceInFrontOfUser();

                if (_activePanel != null)
                    _activePanel.ShowLoading(payload);
            }
            catch (Exception ex)
            {
                Debug.LogError($"CAMERA PANEL FAILED: {ex}");
                _activePanel = null;
            }
        }

        if (apiClient == null)
            apiClient = FindFirstObjectByType<CameraApiClient>();

        if (apiClient == null)
        {
            string message =
                "QrCameraScanner: CameraApiClient not found in scene.";
            Debug.LogError(message);

            if (_monitoringUI != null)
                _monitoringUI.ShowFriendlyError(message);
            else if (_activePanel != null)
                _activePanel.ShowError(payload, message);

            return;
        }

        apiClient.GetCamera(
            payload,
            data =>
            {
                string temperature =
                    data.temperatureAvailable
                        ? $"{data.temperature:0.0} C"
                        : "N/A";

                Debug.Log(
                    $"CAMERA DATA OK | " +
                    $"ID={data.cameraId} | " +
                    $"Name={data.name} | " +
                    $"Status={data.status} | " +
                    $"Temperature={temperature} | " +
                    $"StorageHealthy={data.storageHealthy}");

                if (_monitoringUI != null)
                    _monitoringUI.ShowScannedCamera(data);
                else if (_activePanel != null)
                    _activePanel.ShowCamera(data);
            },
            error =>
            {
                Debug.LogError(
                    $"CAMERA DATA FAILED | {payload} | {error}");

                if (_monitoringUI != null)
                    _monitoringUI.ShowFriendlyError(error);
                else if (_activePanel != null)
                    _activePanel.ShowError(payload, error);
            });
    }

    private void OnTrackableRemoved(MRUKTrackable trackable)
    {
        if (trackable == null ||
            trackable.TrackableType != OVRAnchor.TrackableType.QRCode)
        {
            return;
        }

        _trackedQrs.RemoveAll(
            state => state.trackable == trackable);

        Debug.Log("Axis camera QR removed.");
    }
}
