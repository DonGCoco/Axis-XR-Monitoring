using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;

public class CameraApiClient : MonoBehaviour
{
    [Header("Data Source")]
    [Tooltip("Keep this OFF for the real demo so Quest fetches data from the online mock API over Wi-Fi.")]
    [SerializeField] private bool useEmbeddedMockData = false;

    [Tooltip("Online mock API base URL. Example: https://your-service.onrender.com")]
    [SerializeField] private string baseUrl = "http://127.0.0.1:5000";

    public string BaseUrl
    {
        get => baseUrl;
        set => baseUrl = value.TrimEnd('/');
    }

    public bool UseEmbeddedMockData
    {
        get => useEmbeddedMockData;
        set => useEmbeddedMockData = value;
    }

    public void GetCamera(
        string cameraId,
        Action<CameraData> onSuccess,
        Action<string> onError = null)
    {
        if (string.IsNullOrWhiteSpace(cameraId))
        {
            onError?.Invoke("Camera ID is empty.");
            return;
        }

        string normalizedId = cameraId.Trim().ToUpperInvariant();

        if (useEmbeddedMockData)
        {
            CameraData mockCamera = GetEmbeddedMockCamera(normalizedId);

            if (mockCamera == null)
            {
                string message = $"Embedded mock camera not found: {normalizedId}";
                Debug.LogError(message);
                onError?.Invoke(message);
                return;
            }

            mockCamera.serverTime = DateTime.UtcNow.ToString("o");
            Debug.Log($"USING EMBEDDED MOCK DATA | {normalizedId}");
            onSuccess?.Invoke(mockCamera);
            return;
        }

        StartCoroutine(GetCameraCoroutine(normalizedId, onSuccess, onError));
    }

    private static CameraData GetEmbeddedMockCamera(string cameraId)
    {
        switch (cameraId)
        {
            case "CAM_001":
                return new CameraData
                {
                    cameraId = "CAM_001",
                    name = "Entrance Camera",
                    online = true,
                    model = "AXIS Test Camera",
                    osVersion = "12.0",
                    uptime = 86400,
                    temperatureAvailable = true,
                    temperature = 43.2f,
                    storageHealthy = true,
                    status = "HEALTHY"
                };

            case "CAM_002":
                return new CameraData
                {
                    cameraId = "CAM_002",
                    name = "Hallway Camera",
                    online = true,
                    model = "AXIS Test Camera",
                    osVersion = "12.0",
                    uptime = 43200,
                    temperatureAvailable = true,
                    temperature = 81.4f,
                    storageHealthy = true,
                    status = "WARNING"
                };

            case "CAM_003":
                return new CameraData
                {
                    cameraId = "CAM_003",
                    name = "Lab Camera",
                    online = false,
                    model = "AXIS Test Camera",
                    osVersion = "12.0",
                    uptime = 0,
                    temperatureAvailable = false,
                    temperature = 0.0f,
                    storageHealthy = false,
                    status = "OFFLINE"
                };

            default:
                return null;
        }
    }

    private IEnumerator GetCameraCoroutine(
        string cameraId,
        Action<CameraData> onSuccess,
        Action<string> onError)
    {
        string url = $"{baseUrl.TrimEnd('/')}/camera/{UnityWebRequest.EscapeURL(cameraId)}";

        using UnityWebRequest request = UnityWebRequest.Get(url);
        request.timeout = 8;

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
