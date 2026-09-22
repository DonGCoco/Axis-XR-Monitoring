using System.Collections;
using Meta.XR.MRUtilityKit;
using UnityEngine;

public class QRCodeDebugLogger : MonoBehaviour
{
    private MRUK _mruk;
    private bool _subscribed;

    private void OnEnable()
    {
        Debug.Log("QRCodeDebugLogger component enabled.");
        StartCoroutine(WaitForMRUKAndSubscribe());
    }

    private IEnumerator WaitForMRUKAndSubscribe()
    {
        while (MRUK.Instance == null)
        {
            Debug.Log("QRCodeDebugLogger: waiting for MRUK.Instance...");
            yield return null;
        }

        _mruk = MRUK.Instance;

        if (_mruk.SceneSettings == null)
        {
            Debug.LogError("QRCodeDebugLogger: MRUK.SceneSettings is null.");
            yield break;
        }

        _mruk.SceneSettings.TrackableAdded.AddListener(OnTrackableAdded);
        _mruk.SceneSettings.TrackableRemoved.AddListener(OnTrackableRemoved);
        _subscribed = true;

        Debug.Log("QRCodeDebugLogger subscribed. Waiting for QR codes...");
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
        Debug.Log($"Trackable added: {trackable.TrackableType}");

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
