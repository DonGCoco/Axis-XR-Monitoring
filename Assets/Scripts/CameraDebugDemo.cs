using UnityEngine;

public class CameraDebugDemo : MonoBehaviour
{
    [SerializeField] private CameraApiClient apiClient;
    [SerializeField] private string testCameraId = "CAM_001";
    [SerializeField] private bool requestOnStart = true;

    private void Start()
    {
        if (requestOnStart)
        {
            RequestTestCamera();
        }
    }

    [ContextMenu("Request Test Camera")]
    public void RequestTestCamera()
    {
        if (apiClient == null)
        {
            Debug.LogError("CameraDebugDemo: CameraApiClient is not assigned.");
            return;
        }

        apiClient.GetCamera(
            testCameraId,
            camera =>
            {
                Debug.Log(
                    $"Camera loaded: {camera.cameraId} | " +
                    $"{camera.name} | {camera.status} | " +
                    $"Temp: {camera.temperature} C");
            },
            error => Debug.LogError(error));
    }
}
