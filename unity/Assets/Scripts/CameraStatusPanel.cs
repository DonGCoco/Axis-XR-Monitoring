using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class CameraStatusPanel : MonoBehaviour
{
    private TMP_Text _titleText;
    private TMP_Text _statusText;
    private TMP_Text _detailsText;

    private Camera _mainCamera;
    private Transform _anchor;

    public static CameraStatusPanel Create(Transform anchor)
    {
        GameObject root = new GameObject(
            "CameraStatusPanel",
            typeof(RectTransform),
            typeof(Canvas),
            typeof(CanvasScaler));

        RectTransform rootRect = root.GetComponent<RectTransform>();
        rootRect.sizeDelta = new Vector2(440f, 300f);
        root.transform.localScale = Vector3.one * 0.00075f;

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
        bgImage.color = new Color(0.04f, 0.05f, 0.06f, 0.92f);

        CameraStatusPanel panel = root.AddComponent<CameraStatusPanel>();
        panel._titleText = CreateText(
            root.transform,
            "Title",
            new Vector2(20f, -18f),
            new Vector2(-20f, -78f),
            30f,
            FontStyles.Bold);

        panel._statusText = CreateText(
            root.transform,
            "Status",
            new Vector2(20f, -86f),
            new Vector2(-20f, -132f),
            25f,
            FontStyles.Bold);

        panel._detailsText = CreateText(
            root.transform,
            "Details",
            new Vector2(20f, -144f),
            new Vector2(-20f, -282f),
            22f,
            FontStyles.Normal);

        panel.SetAnchor(anchor);
        return panel;
    }

    private static TMP_Text CreateText(
        Transform parent,
        string objectName,
        Vector2 topLeft,
        Vector2 bottomRight,
        float fontSize,
        FontStyles style)
    {
        GameObject textObject = new GameObject(
            objectName,
            typeof(RectTransform),
            typeof(TextMeshProUGUI));

        textObject.transform.SetParent(parent, false);

        RectTransform rect = textObject.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.offsetMin = new Vector2(topLeft.x, bottomRight.y);
        rect.offsetMax = new Vector2(bottomRight.x, topLeft.y);

        TextMeshProUGUI text = textObject.GetComponent<TextMeshProUGUI>();
        text.fontSize = fontSize;
        text.fontStyle = style;
        text.color = Color.white;
        text.alignment = TextAlignmentOptions.TopLeft;
        text.enableWordWrapping = false;
        text.raycastTarget = false;

        if (TMP_Settings.defaultFontAsset != null)
        {
            text.font = TMP_Settings.defaultFontAsset;
        }

        return text;
    }

    private void Awake()
    {
        _mainCamera = Camera.main;
    }

    public void SetAnchor(Transform anchor)
    {
        _anchor = anchor;

        if (_anchor != null)
        {
            transform.position = _anchor.position + Vector3.up * 0.18f;
        }
    }

    private void LateUpdate()
    {
        if (_anchor != null)
        {
            transform.position = _anchor.position + Vector3.up * 0.18f;
        }

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
