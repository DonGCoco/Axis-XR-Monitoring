using System;
using System.Collections;
using Meta.XR.MRUtilityKit;
using UnityEngine;

public class QrCameraScanner : MonoBehaviour
{
    [SerializeField] private CameraApiClient apiClient;
    [SerializeField] private bool showStatusPanel = true;

    private MRUK _mruk;
    private bool _subscribed;
    private CameraStatusPanel _activePanel;

    private void Awake()
    {
        if (apiClient == null)
            apiClient = FindFirstObjectByType<CameraApiClient>();
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
        if (!_subscribed || _mruk == null || _mruk.SceneSettings == null)
            return;

        _mruk.SceneSettings.TrackableAdded.RemoveListener(OnTrackableAdded);
        _mruk.SceneSettings.TrackableRemoved.RemoveListener(OnTrackableRemoved);
        _subscribed = false;
    }

    private void OnTrackableAdded(MRUKTrackable trackable)
    {
        if (trackable == null)
            return;

        if (trackable.TrackableType != OVRAnchor.TrackableType.QRCode)
            return;

        string payload = trackable.MarkerPayloadString;

        if (string.IsNullOrWhiteSpace(payload))
        {
            Debug.LogWarning("QR code detected, but it does not contain a text payload.");
            return;
        }

        payload = payload.Trim();

        if (!payload.StartsWith("CAM_"))
        {
            Debug.Log($"Ignoring non-camera QR payload: {payload}");
            return;
        }

        Debug.Log($"Axis camera QR detected: {payload}");

        if (showStatusPanel)
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
            string message = "QrCameraScanner: CameraApiClient not found in scene.";
            Debug.LogError(message);

            if (_activePanel != null)
                _activePanel.ShowError(payload, message);

            return;
        }

        apiClient.GetCamera(
            payload,
            data =>
            {
                string temperature = data.temperatureAvailable
                    ? $"{data.temperature:0.0} C"
                    : "N/A";

                Debug.Log(
                    $"CAMERA DATA OK | " +
                    $"ID={data.cameraId} | " +
                    $"Name={data.name} | " +
                    $"Status={data.status} | " +
                    $"Temperature={temperature} | " +
                    $"StorageHealthy={data.storageHealthy}");

                if (_activePanel != null)
                    _activePanel.ShowCamera(data);
            },
            error =>
            {
                Debug.LogError($"CAMERA DATA FAILED | {payload} | {error}");

                if (_activePanel != null)
                    _activePanel.ShowError(payload, error);
            });
    }

    private void OnTrackableRemoved(MRUKTrackable trackable)
    {
        if (trackable != null &&
            trackable.TrackableType == OVRAnchor.TrackableType.QRCode)
        {
            Debug.Log("Axis camera QR removed.");
        }
    }
}
