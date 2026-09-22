using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class CameraStatusPanel : MonoBehaviour
{
    private TMP_Text _titleText;
    private TMP_Text _statusText;
    private TMP_Text _detailsText;
    private Camera _mainCamera;

    public static CameraStatusPanel CreateInFrontOfUser()
    {
        GameObject root = new GameObject(
            "CameraStatusPanel",
            typeof(RectTransform),
            typeof(Canvas),
            typeof(CanvasScaler));

        RectTransform rootRect = root.GetComponent<RectTransform>();
        rootRect.sizeDelta = new Vector2(520f, 340f);
        root.transform.localScale = Vector3.one * 0.001f;

        Canvas canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.sortingOrder = 100;

        GameObject background = new GameObject(
            "Background",
            typeof(RectTransform),
            typeof(Image));
        background.transform.SetParent(root.transform, false);

        RectTransform bgRect = background.GetComponent<RectTransform>();
        bgRect.anchorMin = Vector2.zero;
        bgRect.anchorMax = Vector2.one;
        bgRect.offsetMin = Vector2.zero;
        bgRect.offsetMax = Vector2.zero;

        Image bgImage = background.GetComponent<Image>();
        bgImage.color = new Color(0.03f, 0.04f, 0.05f, 0.96f);

        CameraStatusPanel panel = root.AddComponent<CameraStatusPanel>();

        panel._titleText = CreateText(root.transform, "Title", 30f, FontStyles.Bold);
        panel._statusText = CreateText(root.transform, "Status", 25f, FontStyles.Bold);
        panel._detailsText = CreateText(root.transform, "Details", 22f, FontStyles.Normal);

        RectTransform titleRect = panel._titleText.rectTransform;
        titleRect.anchorMin = new Vector2(0f, 1f);
        titleRect.anchorMax = new Vector2(1f, 1f);
        titleRect.pivot = new Vector2(0.5f, 1f);
        titleRect.offsetMin = new Vector2(24f, -90f);
        titleRect.offsetMax = new Vector2(-24f, -20f);

        RectTransform statusRect = panel._statusText.rectTransform;
        statusRect.anchorMin = new Vector2(0f, 1f);
        statusRect.anchorMax = new Vector2(1f, 1f);
        statusRect.pivot = new Vector2(0.5f, 1f);
        statusRect.offsetMin = new Vector2(24f, -145f);
        statusRect.offsetMax = new Vector2(-24f, -95f);

        RectTransform detailsRect = panel._detailsText.rectTransform;
        detailsRect.anchorMin = new Vector2(0f, 0f);
        detailsRect.anchorMax = new Vector2(1f, 1f);
        detailsRect.offsetMin = new Vector2(24f, 20f);
        detailsRect.offsetMax = new Vector2(-24f, -155f);

        panel.PlaceInFrontOfUser();
        Debug.Log("CAMERA PANEL CREATED in front of user.");

        return panel;
    }

    private static TMP_Text CreateText(
        Transform parent,
        string objectName,
        float fontSize,
        FontStyles style)
    {
        GameObject textObject = new GameObject(
            objectName,
            typeof(RectTransform),
            typeof(TextMeshProUGUI));

        textObject.transform.SetParent(parent, false);

        TextMeshProUGUI text = textObject.GetComponent<TextMeshProUGUI>();
        text.fontSize = fontSize;
        text.fontStyle = style;
        text.color = Color.white;
        text.alignment = TextAlignmentOptions.TopLeft;
        text.enableWordWrapping = false;
        text.raycastTarget = false;

        if (TMP_Settings.defaultFontAsset != null)
            text.font = TMP_Settings.defaultFontAsset;

        return text;
    }

    private void Awake()
    {
        _mainCamera = FindCamera();
    }

    private Camera FindCamera()
    {
        if (Camera.main != null)
            return Camera.main;

        return FindFirstObjectByType<Camera>();
    }

    public void PlaceInFrontOfUser()
    {
        _mainCamera = FindCamera();

        if (_mainCamera == null)
        {
            Debug.LogError("CAMERA PANEL: No camera found.");
            return;
        }

        Transform cameraTransform = _mainCamera.transform;

        transform.position =
            cameraTransform.position +
            cameraTransform.forward * 0.9f +
            cameraTransform.up * -0.05f;

        Vector3 towardCamera =
            cameraTransform.position - transform.position;

        transform.rotation =
            Quaternion.LookRotation(towardCamera.normalized, Vector3.up);

        Debug.Log(
            $"CAMERA PANEL POSITIONED | position={transform.position} | camera={cameraTransform.position}");
    }

    private void LateUpdate()
    {
        if (_mainCamera == null)
            _mainCamera = FindCamera();

        if (_mainCamera == null)
            return;

        Vector3 towardCamera =
            _mainCamera.transform.position - transform.position;

        if (towardCamera.sqrMagnitude > 0.001f)
        {
            transform.rotation =
                Quaternion.LookRotation(towardCamera.normalized, Vector3.up);
        }
    }

    public void ShowLoading(string cameraId)
    {
        SetText(cameraId, "LOADING", "Retrieving camera data...");
    }

    public void ShowCamera(CameraData camera)
    {
        string temperature = camera.temperatureAvailable
            ? $"{camera.temperature:0.0} °C"
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
        if (_titleText != null)
            _titleText.text = title;

        if (_statusText != null)
        {
            _statusText.text = $"● {status}";

            switch (status)
            {
                case "HEALTHY":
                    _statusText.color = new Color(0.25f, 0.9f, 0.4f);
                    break;
                case "WARNING":
                    _statusText.color = new Color(1f, 0.7f, 0.2f);
                    break;
                case "OFFLINE":
                case "ERROR":
                    _statusText.color = new Color(1f, 0.3f, 0.3f);
                    break;
                default:
                    _statusText.color = Color.white;
                    break;
            }
        }

        if (_detailsText != null)
            _detailsText.text = details;
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
