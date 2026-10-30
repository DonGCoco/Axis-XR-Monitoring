using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

// Optional on-demand camera preview. Uses the existing Windows Axis proxy;
// camera credentials never enter the Quest application.
public partial class AxisMonitoringUI
{
    private GameObject _liveViewRoot;
    private RawImage _liveViewImage;
    private AspectRatioFitter _liveViewAspect;
    private Text _liveViewStatus;
    private Texture2D _liveViewTexture;
    private Coroutine _liveViewRefresh;
    private int _liveViewVersion;

    private void ShowLiveView(CameraData camera)
    {
        if (camera == null || _apiClient == null || _camera == null)
        {
            ShowFriendlyError("Live View is not available.");
            return;
        }

        EndSpatialWorkflow();
        DestroyTransientUi();

        if (_hudRoot != null)
            _hudRoot.SetActive(false);

        _liveViewRoot = CreateHeadLockedPanel(
            "CameraLiveView",
            new Vector2(760f, 640f),
            new Vector3(0f, 0f, 0.96f));

        AddText(
            _liveViewRoot.transform,
            camera.name + " | Live View",
            28,
            FontStyle.Bold,
            TextPrimary,
            new Vector2(26f, -24f),
            new Vector2(-26f, -65f));

        AddText(
            _liveViewRoot.transform,
            camera.cameraId + "  ·  JPEG snapshot preview",
            17,
            FontStyle.Normal,
            TextSecondary,
            new Vector2(26f, -66f),
            new Vector2(-26f, -95f));

        GameObject frame = CreateRect(
            _liveViewRoot.transform,
            "SnapshotFrame",
            new Vector2(708f, 398f),
            new Vector2(0f, -108f),
            new Color(0.02f, 0.025f, 0.035f, 1f));

        GameObject imageObject = new GameObject(
            "LiveSnapshot",
            typeof(RectTransform),
            typeof(RawImage),
            typeof(AspectRatioFitter));
        imageObject.transform.SetParent(frame.transform, false);

        RectTransform imageRect = imageObject.GetComponent<RectTransform>();
        imageRect.anchorMin = Vector2.zero;
        imageRect.anchorMax = Vector2.one;
        imageRect.offsetMin = Vector2.zero;
        imageRect.offsetMax = Vector2.zero;

        _liveViewImage = imageObject.GetComponent<RawImage>();
        _liveViewImage.raycastTarget = false;
        _liveViewImage.enabled = false;

        _liveViewAspect = imageObject.GetComponent<AspectRatioFitter>();
        _liveViewAspect.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
        _liveViewAspect.aspectRatio = 16f / 9f;

        _liveViewStatus = AddText(
            _liveViewRoot.transform,
            "Connecting to the camera...",
            18,
            FontStyle.Normal,
            TextSecondary,
            new Vector2(26f, -520f),
            new Vector2(-26f, -550f));

        CreateButton(
            _liveViewRoot.transform,
            "Back to Details",
            new Vector2(220f, 58f),
            new Vector2(0f, -569f),
            () => ShowDetails(camera),
            AccentColor);

        int version = _liveViewVersion;
        _liveViewRefresh = StartCoroutine(
            RefreshLiveSnapshots(camera.cameraId, version));
    }

    private IEnumerator RefreshLiveSnapshots(string cameraId, int version)
    {
        if (cameraId != "CAM_001" && cameraId != "CAM_002")
        {
            if (_liveViewStatus != null)
                _liveViewStatus.text = "No snapshot endpoint configured for " + cameraId;
            yield break;
        }

        string url = _apiClient.BaseUrl.TrimEnd('/') +
                     "/camera/" + cameraId + "/snapshot";

        while (version == _liveViewVersion && _liveViewRoot != null)
        {
            using (UnityWebRequest request = UnityWebRequestTexture.GetTexture(url))
            {
                request.timeout = 10;
                request.SetRequestHeader("Cache-Control", "no-cache");
                yield return request.SendWebRequest();

                if (version != _liveViewVersion || _liveViewRoot == null)
                    yield break;

                if (request.result != UnityWebRequest.Result.Success)
                {
                    if (_liveViewStatus != null)
                        _liveViewStatus.text =
                            "Snapshot unavailable: " + request.error +
                            " (HTTP " + request.responseCode + ")";
                }
                else
                {
                    Texture2D incoming = DownloadHandlerTexture.GetContent(request);
                    if (incoming != null)
                    {
                        Texture2D old = _liveViewTexture;
                        _liveViewTexture = incoming;

                        if (_liveViewImage != null)
                        {
                            _liveViewImage.texture = incoming;
                            _liveViewImage.enabled = true;
                        }

                        if (_liveViewAspect != null && incoming.height > 0)
                            _liveViewAspect.aspectRatio =
                                (float)incoming.width / incoming.height;

                        if (old != null)
                            Destroy(old);

                        if (_liveViewStatus != null)
                            _liveViewStatus.text =
                                "Snapshot updated " + DateTime.Now.ToString("HH:mm:ss") +
                                "  ·  periodic JPEG preview";
                    }
                }
            }

            // The preview is opt-in and stops when the panel closes.
            // This is not full-motion RTSP video.
            yield return new WaitForSeconds(1f);
        }
    }

    private void StopLiveView()
    {
        _liveViewVersion++;

        if (_liveViewRefresh != null)
        {
            StopCoroutine(_liveViewRefresh);
            _liveViewRefresh = null;
        }

        if (_liveViewRoot != null)
            Destroy(_liveViewRoot);
        _liveViewRoot = null;
        _liveViewImage = null;
        _liveViewAspect = null;
        _liveViewStatus = null;

        if (_liveViewTexture != null)
            Destroy(_liveViewTexture);
        _liveViewTexture = null;
    }
}
