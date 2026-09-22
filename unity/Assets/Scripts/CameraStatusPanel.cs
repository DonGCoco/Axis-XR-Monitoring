using UnityEngine;
using UnityEngine.UI;

public class CameraStatusPanel : MonoBehaviour
{
    private Text _titleText;
    private Text _statusText;
    private Text _detailsText;

    public static CameraStatusPanel CreateInFrontOfUser()
    {
        Debug.Log("CAMERA PANEL STEP 1: creating root");

        GameObject root = new GameObject("CameraStatusPanel", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        RectTransform rootRect = root.GetComponent<RectTransform>();
        rootRect.sizeDelta = new Vector2(520f, 340f);

        Canvas canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.sortingOrder = 100;

        Camera camera = FindCamera();
        if (camera == null)
        {
            Debug.LogError("CAMERA PANEL FAILED: no camera found.");
            Destroy(root);
            return null;
        }

        root.transform.SetParent(camera.transform, false);
        root.transform.localPosition = new Vector3(0f, -0.05f, 0.9f);
        root.transform.localRotation = Quaternion.identity;
        root.transform.localScale = Vector3.one * 0.001f;

        Debug.Log("CAMERA PANEL STEP 2: creating background");

        GameObject background = new GameObject("Background", typeof(RectTransform), typeof(Image));
        background.transform.SetParent(root.transform, false);

        RectTransform bgRect = background.GetComponent<RectTransform>();
        bgRect.anchorMin = Vector2.zero;
        bgRect.anchorMax = Vector2.one;
        bgRect.offsetMin = Vector2.zero;
        bgRect.offsetMax = Vector2.zero;

        Image bgImage = background.GetComponent<Image>();
        bgImage.color = new Color(0.03f, 0.04f, 0.05f, 0.96f);

        Debug.Log("CAMERA PANEL STEP 3: creating text");

        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        CameraStatusPanel panel = root.AddComponent<CameraStatusPanel>();
        panel._titleText = CreateText(root.transform, "Title", font, 30, FontStyle.Bold);
        panel._statusText = CreateText(root.transform, "Status", font, 25, FontStyle.Bold);
        panel._detailsText = CreateText(root.transform, "Details", font, 22, FontStyle.Normal);

        SetRect(panel._titleText.rectTransform, 24f, -20f, -24f, -90f);
        SetRect(panel._statusText.rectTransform, 24f, -95f, -24f, -145f);

        RectTransform detailsRect = panel._detailsText.rectTransform;
        detailsRect.anchorMin = new Vector2(0f, 0f);
        detailsRect.anchorMax = new Vector2(1f, 1f);
        detailsRect.offsetMin = new Vector2(24f, 20f);
        detailsRect.offsetMax = new Vector2(-24f, -155f);

        Debug.Log("CAMERA PANEL CREATED in front of user.");
        return panel;
    }

    private static Camera FindCamera()
    {
        if (Camera.main != null)
            return Camera.main;

        return FindFirstObjectByType<Camera>();
    }

    private static Text CreateText(
        Transform parent,
        string objectName,
        Font font,
        int fontSize,
        FontStyle fontStyle)
    {
        GameObject textObject = new GameObject(objectName, typeof(RectTransform), typeof(Text));
        textObject.transform.SetParent(parent, false);

        Text text = textObject.GetComponent<Text>();
        text.font = font;
        text.fontSize = fontSize;
        text.fontStyle = fontStyle;
        text.color = Color.white;
        text.alignment = TextAnchor.UpperLeft;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.raycastTarget = false;

        return text;
    }

    private static void SetRect(
        RectTransform rect,
        float left,
        float top,
        float right,
        float bottom)
    {
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.offsetMin = new Vector2(left, bottom);
        rect.offsetMax = new Vector2(right, top);
    }

    public void PlaceInFrontOfUser()
    {
        Camera camera = FindCamera();

        if (camera == null)
        {
            Debug.LogError("CAMERA PANEL: no camera found while repositioning.");
            return;
        }

        transform.SetParent(camera.transform, false);
        transform.localPosition = new Vector3(0f, -0.05f, 0.9f);
        transform.localRotation = Quaternion.identity;
        transform.localScale = Vector3.one * 0.001f;

        Debug.Log("CAMERA PANEL POSITIONED in front of user.");
    }

    public void ShowLoading(string cameraId)
    {
        SetText(cameraId, "LOADING", "Retrieving camera data...");
    }

    public void ShowCamera(CameraData camera)
    {
        string temperature = camera.temperatureAvailable
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
        if (_titleText != null)
            _titleText.text = title;

        if (_statusText != null)
        {
            _statusText.text = "● " + status;

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
