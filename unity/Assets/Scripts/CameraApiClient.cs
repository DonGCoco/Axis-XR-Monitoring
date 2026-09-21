using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;

public class CameraApiClient : MonoBehaviour
{
    [Tooltip("In the Unity Editor use http://127.0.0.1:5000. On Quest use your Mac's LAN IP, e.g. http://192.168.1.20:5000.")]
    [SerializeField] private string baseUrl = "http://127.0.0.1:5000";

    public string BaseUrl
    {
        get => baseUrl;
        set => baseUrl = value.TrimEnd('/');
    }

    public void GetCamera(
        string cameraId,
        Action<CameraData> onSuccess,
        Action<string> onError = null)
    {
        StartCoroutine(GetCameraCoroutine(cameraId, onSuccess, onError));
    }

    private IEnumerator GetCameraCoroutine(
        string cameraId,
        Action<CameraData> onSuccess,
        Action<string> onError)
    {
        if (string.IsNullOrWhiteSpace(cameraId))
        {
            onError?.Invoke("Camera ID is empty.");
            yield break;
        }

        string url = $"{baseUrl.TrimEnd('/')}/camera/{UnityWebRequest.EscapeURL(cameraId.Trim())}";

        using UnityWebRequest request = UnityWebRequest.Get(url);
        request.timeout = 5;

        yield return request.SendWebRequest();

        if (request.result != UnityWebRequest.Result.Success)
        {
            string message =
                $"Camera API request failed ({request.responseCode}): {request.error}\n{url}";
            Debug.LogError(message);
            onError?.Invoke(message);
            yield break;
        }

        CameraData data;

        try
        {
            data = JsonUtility.FromJson<CameraData>(request.downloadHandler.text);
        }
        catch (Exception exception)
        {
            string message = $"Could not parse camera JSON: {exception.Message}";
            Debug.LogError(message);
            onError?.Invoke(message);
            yield break;
        }

        if (data == null || string.IsNullOrWhiteSpace(data.cameraId))
        {
            string message = $"Camera API returned invalid JSON: {request.downloadHandler.text}";
            Debug.LogError(message);
            onError?.Invoke(message);
            yield break;
        }

        onSuccess?.Invoke(data);
    }
}
