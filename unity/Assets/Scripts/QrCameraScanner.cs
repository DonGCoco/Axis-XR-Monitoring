using Meta.XR.MRUtilityKit;
using UnityEngine;

public class QrCameraScanner : MonoBehaviour
{
    [SerializeField] private MRUK mruk;
    [SerializeField] private CameraApiClient apiClient;
    [SerializeField] private CameraStatusPanel statusPanelPrefab;

    private void Awake()
    {
        if (mruk == null)
        {
            mruk = FindFirstObjectByType<MRUK>();
        }

        if (apiClient == null)
        {
            apiClient = FindFirstObjectByType<CameraApiClient>();
        }
    }

    private void OnEnable()
    {
        if (mruk == null)
        {
            Debug.LogError("QrCameraScanner: MRUK component not found.");
            return;
        }

        mruk.SceneSettings.TrackableAdded.AddListener(OnTrackableAdded);
        mruk.SceneSettings.TrackableRemoved.AddListener(OnTrackableRemoved);
    }

    private void OnDisable()
    {
        if (mruk == null)
            return;

        mruk.SceneSettings.TrackableAdded.RemoveListener(OnTrackableAdded);
        mruk.SceneSettings.TrackableRemoved.RemoveListener(OnTrackableRemoved);
    }

    private void OnTrackableAdded(MRUKTrackable trackable)
    {
        if (trackable.TrackableType != OVRAnchor.TrackableType.QRCode)
            return;

        string payload = trackable.MarkerPayloadString;

        if (string.IsNullOrWhiteSpace(payload))
        {
            Debug.LogWarning("QR code detected, but it does not contain a UTF-8 string payload.");
            return;
        }

        payload = payload.Trim();

        if (!payload.StartsWith("CAM_"))
        {
            Debug.Log($"Ignoring QR payload that is not a camera ID: {payload}");
            return;
        }

        Debug.Log($"Axis camera QR detected: {payload}");

        if (apiClient == null)
        {
            Debug.LogError("QrCameraScanner: CameraApiClient is not assigned.");
            return;
        }

        CameraStatusPanel panel = null;

        if (statusPanelPrefab != null)
        {
            panel = Instantiate(statusPanelPrefab, trackable.transform);
            panel.transform.localPosition = new Vector3(0f, -0.15f, 0f);
            panel.transform.localRotation = Quaternion.identity;
            panel.ShowLoading(payload);
        }

        apiClient.GetCamera(
            payload,
            data =>
            {
                Debug.Log(
                    $"Camera loaded from QR: {data.cameraId} | " +
                    $"{data.name} | {data.status}");

                if (panel != null)
                    panel.ShowCamera(data);
            },
            error =>
            {
                Debug.LogError(error);

                if (panel != null)
                    panel.ShowError(payload, error);
            });
    }

    private void OnTrackableRemoved(MRUKTrackable trackable)
    {
        if (trackable.TrackableType == OVRAnchor.TrackableType.QRCode)
        {
            Debug.Log("QR code trackable removed.");
        }
    }
}
