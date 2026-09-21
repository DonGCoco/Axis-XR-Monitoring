using TMPro;
using UnityEngine;

public class CameraStatusPanel : MonoBehaviour
{
    [SerializeField] private TMP_Text titleText;
    [SerializeField] private TMP_Text statusText;
    [SerializeField] private TMP_Text detailsText;

    private Camera _mainCamera;

    private void Awake()
    {
        _mainCamera = Camera.main;
    }

    private void LateUpdate()
    {
        if (_mainCamera == null)
        {
            _mainCamera = Camera.main;
            if (_mainCamera == null)
                return;
        }

        Vector3 direction = transform.position - _mainCamera.transform.position;

        if (direction.sqrMagnitude > 0.001f)
        {
            transform.rotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
        }
    }

    public void ShowLoading(string cameraId)
    {
        SetText(
            cameraId,
            "LOADING",
            "Retrieving camera data...");
    }

    public void ShowCamera(CameraData camera)
    {
        string temperature =
            camera.temperatureAvailable
                ? $"{camera.temperature:0.0} C"
                : "N/A";

        string storage = camera.storageHealthy ? "Healthy" : "Problem";

        SetText(
            $"{camera.name}\n{camera.cameraId}",
            camera.status,
            $"Model: {camera.model}\n" +
            $"OS: {camera.osVersion}\n" +
            $"Temperature: {temperature}\n" +
            $"Storage: {storage}\n" +
            $"Uptime: {FormatUptime(camera.uptime)}");
    }

    public void ShowError(string cameraId, string error)
    {
        SetText(cameraId, "ERROR", error);
    }

    private void SetText(string title, string status, string details)
    {
        if (titleText != null)
            titleText.text = title;

        if (statusText != null)
            statusText.text = status;

        if (detailsText != null)
            detailsText.text = details;
    }

    private static string FormatUptime(int seconds)
    {
        if (seconds < 0)
            return "N/A";

        int days = seconds / 86400;
        int hours = (seconds % 86400) / 3600;
        int minutes = (seconds % 3600) / 60;

        if (days > 0)
            return $"{days}d {hours}h {minutes}m";

        if (hours > 0)
            return $"{hours}h {minutes}m";

        return $"{minutes}m";
    }
}
