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

    // The Register panel shows this so a silent scan failure is visible on the
    // headset instead of looking like "nothing happened".
    public static QrCameraScanner Instance { get; private set; }
    public string ScannerStatus { get; private set; } = "QR scanner: starting";
    public int TrackedQrCount => _trackedQrs.Count;

    private readonly List<MRUKTrackable> _seedTrackables =
        new List<MRUKTrackable>();
    private float _nextTrackablePoll;
    private int _lastPolledTotal = -1;
    private readonly List<OVRAnchor> _markerAnchors = new List<OVRAnchor>();
    private bool _markerFetchBusy;
    private float _nextMarkerFetch;
    private int _lastMarkerCount = -1;

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
        Instance = this;

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
        ScannerStatus = "QR scanner: waiting for MRUK";

        while (MRUK.Instance == null)
            yield return null;

        _mruk = MRUK.Instance;

        if (_mruk.SceneSettings == null)
        {
            ScannerStatus = "QR scanner: MRUK settings missing";
            Debug.LogError("QrCameraScanner: MRUK.SceneSettings is null.");
            yield break;
        }

        _mruk.SceneSettings.TrackableAdded.AddListener(OnTrackableAdded);
        _mruk.SceneSettings.TrackableRemoved.AddListener(OnTrackableRemoved);
        _subscribed = true;

        // MRUK only raises TrackableAdded for codes that appear after the
        // listener is attached. A QR that is already in view when the app
        // starts would therefore never reach the registration flow, so pull
        // the existing trackables once and feed them through the same path.
        int seeded = 0;
        _mruk.GetTrackables(_seedTrackables);

        foreach (MRUKTrackable trackable in _seedTrackables)
        {
            if (trackable == null)
                continue;

            seeded++;
            OnTrackableAdded(trackable);
        }

        _seedTrackables.Clear();

        ScannerStatus = "QR scanner: listening (seeded " + seeded + ")";
        Debug.Log("QrCameraScanner ready. Waiting for CAM_* QR codes... seeded=" + seeded);
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

        PollTrackables(now);

        if (_monitoringUI != null && _monitoringUI.IsRegistering)
            FetchAnchoredMarkers(now);

        for (int i = _trackedQrs.Count - 1; i >= 0; i--)
        {
            QrTrackState state = _trackedQrs[i];

            if (state.trackable == null)
            {
                _trackedQrs.RemoveAt(i);
                continue;
            }

            bool isTracked = state.trackable.IsTracked;

            // If registration mode was opened while this QR was already in
            // view, consume its current MRUK pose immediately instead of
            // requiring the user to look away and trigger TrackableAdded again.
            if (isTracked)
            {
                if (_monitoringUI == null)
                    _monitoringUI = AxisMonitoringUI.EnsureExists(apiClient);

                if (_monitoringUI != null)
                {
                    Pose qrPose =
                        new Pose(
                            state.trackable.transform.position,
                            state.trackable.transform.rotation);

                    if (_monitoringUI.TryRegisterCameraFromQr(
                        state.payload,
                        qrPose))
                    {
                        state.wasTracked = true;
                        continue;
                    }
                }
            }

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
                    HandleCameraPayload(state.payload, state.trackable);
                }
            }
        }
    }

    // MRUK does not reliably raise TrackableAdded for markers the system is
    // already tracking, for example a QR that was in view before the listener
    // attached. Polling the trackable list keeps registration working.
    private void PollTrackables(float now)
    {
        if (!_subscribed || _mruk == null)
            return;

        if (now < _nextTrackablePoll)
            return;

        _nextTrackablePoll = now + 0.5f;

        _mruk.GetTrackables(_seedTrackables);

        int total = _seedTrackables.Count;

        if (total != _lastPolledTotal)
        {
            _lastPolledTotal = total;
            Debug.Log($"QR POLL total={total}");
        }

        int added = 0;

        foreach (MRUKTrackable trackable in _seedTrackables)
        {
            if (trackable == null)
                continue;

            if (_trackedQrs.Exists(state => state.trackable == trackable))
                continue;

            added++;
            OnTrackableAdded(trackable);
        }

        _seedTrackables.Clear();

        if (added > 0)
            Debug.Log($"QR POLL: {added} new trackable(s)");
    }

    // Markers that the system has already anchored are never reported through
    // MRUK's TrackableAdded callback, which is why registration could not see a
    // QR that was registered in an earlier session. Query the anchored markers
    // directly and reuse them as registration poses.
    private void FetchAnchoredMarkers(float now)
    {
        if (_markerFetchBusy || now < _nextMarkerFetch)
            return;

        _nextMarkerFetch = now + 1.5f;
        FetchAnchoredMarkersAsync();
    }

    private async void FetchAnchoredMarkersAsync()
    {
        _markerFetchBusy = true;

        try
        {
            var options = new OVRAnchor.FetchOptions
            {
                SingleComponentType = typeof(OVRMarkerPayload)
            };

            var result = await OVRAnchor.FetchAnchorsAsync(_markerAnchors, options);

            if (!result.Success)
            {
                ScannerStatus = "Marker query failed: " + result.Status;
                return;
            }

            if (_markerAnchors.Count != _lastMarkerCount)
            {
                _lastMarkerCount = _markerAnchors.Count;
                Debug.Log($"ANCHOR MARKERS: {_lastMarkerCount}");
            }

            foreach (OVRAnchor anchor in _markerAnchors)
            {
                if (!anchor.TryGetComponent<OVRMarkerPayload>(out OVRMarkerPayload marker))
                    continue;

                string payload = marker.AsString();

                if (string.IsNullOrWhiteSpace(payload))
                    continue;

                payload = payload.Trim();

                if (!payload.StartsWith("CAM_"))
                    continue;

                if (!anchor.TryGetComponent<OVRLocatable>(out OVRLocatable locatable))
                    continue;

                if (!locatable.TryGetSceneAnchorPose(out OVRLocatable.TrackingSpacePose spacePose))
                    continue;

                if (!spacePose.IsPositionTracked)
                    continue;

                Vector3 position = spacePose.Position ?? Vector3.zero;
                Quaternion rotation = spacePose.Rotation ?? Quaternion.identity;

                Debug.Log($"ANCHOR MARKER MATCH: {payload}");

                if (_monitoringUI != null &&
                    _monitoringUI.TryRegisterCameraFromQr(payload, new Pose(position, rotation)))
                {
                    ScannerStatus = "Anchor marker used: " + payload;
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogException(ex);
        }
        finally
        {
            _markerFetchBusy = false;
        }
    }

    private void OnTrackableAdded(MRUKTrackable trackable)
    {
        if (!TryGetCameraPayload(trackable, out string payload))
            return;

        Debug.Log($"Axis camera QR detected: {payload}");
        ScannerStatus = "QR detected: " + payload;

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

        HandleCameraPayload(payload, trackable);
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

    private void HandleCameraPayload(string payload, MRUKTrackable trackable = null)
    {
        if (_monitoringUI == null)
            _monitoringUI = AxisMonitoringUI.EnsureExists(apiClient);

        // Registration uses the official MRUK QR trackable pose. MRUK keeps
        // this Transform updated while the marker is tracked.
        if (_monitoringUI != null &&
            trackable != null &&
            trackable.IsTracked)
        {
            Pose qrPose =
                new Pose(
                    trackable.transform.position,
                    trackable.transform.rotation);

            if (_monitoringUI.TryRegisterCameraFromQr(payload, qrPose))
                return;
        }

        // A navigation target must be confirmed before requesting/showing another device.
        if (_monitoringUI != null && !_monitoringUI.AcceptCameraScan(payload))
            return;

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
                _monitoringUI.ShowCameraScanError(message);
            else if (_activePanel != null)
                _activePanel.ShowError(payload, message);

            return;
        }

        int scanVersion = _monitoringUI != null ? _monitoringUI.SpatialWorkflowVersion : 0;
        apiClient.GetCamera(
            payload,
            data =>
            {
                if (_monitoringUI != null && !_monitoringUI.IsScanResponseCurrent(payload, scanVersion)) return;
                if (data == null || data.cameraId != payload)
                {
                    if (_monitoringUI != null) _monitoringUI.ShowCameraScanError("Camera API identity does not match scanned QR.");
                    return;
                }
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
                if (_monitoringUI != null && !_monitoringUI.IsScanResponseCurrent(payload, scanVersion)) return;
                Debug.LogError(
                    $"CAMERA DATA FAILED | {payload} | {error}");

                if (_monitoringUI != null)
                    _monitoringUI.ShowCameraScanError(error);
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
