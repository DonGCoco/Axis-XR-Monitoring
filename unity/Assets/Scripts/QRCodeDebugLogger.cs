using Meta.XR.MRUtilityKit;
using UnityEngine;

public class QRCodeDebugLogger : MonoBehaviour
{
    private MRUK _mruk;

    private void Awake()
    {
        _mruk = MRUK.Instance;

        if (_mruk == null)
        {
            Debug.LogError("QRCodeDebugLogger: MRUK.Instance not found in scene.");
        }
    }

    private void OnEnable()
    {
        if (_mruk == null)
            _mruk = MRUK.Instance;

        if (_mruk == null)
            return;

        _mruk.SceneSettings.TrackableAdded.AddListener(OnTrackableAdded);
        _mruk.SceneSettings.TrackableRemoved.AddListener(OnTrackableRemoved);

        Debug.Log("QRCodeDebugLogger enabled. Waiting for QR codes...");
    }

    private void OnDisable()
    {
        if (_mruk == null)
            return;

        _mruk.SceneSettings.TrackableAdded.RemoveListener(OnTrackableAdded);
        _mruk.SceneSettings.TrackableRemoved.RemoveListener(OnTrackableRemoved);
    }

    private void OnTrackableAdded(MRUKTrackable trackable)
    {
        if (trackable.TrackableType != OVRAnchor.TrackableType.QRCode)
            return;

        string payload = trackable.MarkerPayloadString ?? "<non-text payload>";
        Debug.Log($"QR DETECTED: {payload}");
    }

    private void OnTrackableRemoved(MRUKTrackable trackable)
    {
        if (trackable.TrackableType == OVRAnchor.TrackableType.QRCode)
        {
            Debug.Log("QR REMOVED");
        }
    }
}
