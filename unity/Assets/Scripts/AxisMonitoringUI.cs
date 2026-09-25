using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class AxisMonitoringUI : MonoBehaviour
{
    private const string OnboardingKey = "AXIS_XR_ONBOARDING_SEEN_V1";

    private static readonly Color PanelColor =
        new Color(0.035f, 0.045f, 0.06f, 0.94f);

    private static readonly Color PanelSoftColor =
        new Color(0.055f, 0.07f, 0.09f, 0.92f);

    private static readonly Color TextPrimary =
        new Color(0.96f, 0.98f, 1f, 1f);

    private static readonly Color TextSecondary =
        new Color(0.67f, 0.72f, 0.78f, 1f);

    private static readonly Color HealthyColor =
        new Color(0.25f, 0.88f, 0.44f, 1f);

    private static readonly Color WarningColor =
        new Color(1f, 0.70f, 0.18f, 1f);

    private static readonly Color OfflineColor =
        new Color(1f, 0.28f, 0.30f, 1f);

    private static readonly Color AccentColor =
        new Color(0.26f, 0.65f, 1f, 1f);

    private CameraApiClient _apiClient;
    private Camera _camera;
    private bool _initialized;
    private int _onboardingStep;

    private GameObject _hudRoot;
    private GameObject _onboardingRoot;
    private GameObject _overviewRoot;
    private GameObject _previewRoot;
    private GameObject _detailsRoot;
    private GameObject _messageRoot;

    private Text _overviewButtonText;
    private readonly List<CardVisual> _cards = new List<CardVisual>();

    private class CardVisual
    {
        public CameraData data;
        public CanvasGroup group;
        public XRClickable clickable;
    }

    public static AxisMonitoringUI EnsureExists(CameraApiClient apiClient)
    {
        AxisMonitoringUI existing =
            FindFirstObjectByType<AxisMonitoringUI>();

        if (existing == null)
        {
            GameObject root = new GameObject("AxisMonitoringUI");
            existing = root.AddComponent<AxisMonitoringUI>();
        }

        existing.Initialize(apiClient);
        return existing;
    }

    public void Initialize(CameraApiClient apiClient)
    {
        if (apiClient != null)
            _apiClient = apiClient;

        if (_initialized)
            return;

        _initialized = true;
        StartCoroutine(Boot());
    }

    private IEnumerator Boot()
    {
        while (Camera.main == null)
            yield return null;

        _camera = Camera.main;

        if (FindFirstObjectByType<XRPointerInteractor>() == null)
        {
            GameObject pointerObject =
                new GameObject("XRPointerInteractor");
            XRPointerInteractor pointer =
                pointerObject.AddComponent<XRPointerInteractor>();
            pointer.Initialize();
        }

        BuildHud();

        if (PlayerPrefs.GetInt(OnboardingKey, 0) == 0)
            ShowOnboarding(0);
        else
            ShowIdle();
    }

    private Font GetFont()
    {
        return Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
    }

    private void BuildHud()
    {
        if (_hudRoot != null)
            Destroy(_hudRoot);

        _hudRoot = new GameObject("PersistentHUD");
        _hudRoot.transform.SetParent(_camera.transform, false);

        GameObject overviewButton = CreateButtonCanvas(
            "OverviewButton",
            _hudRoot.transform,
            new Vector3(-0.27f, -0.22f, 0.78f),
            new Vector2(230f, 68f),
            "Overview",
            () =>
            {
                if (_overviewRoot != null)
                    CloseOverview();
                else
                    OpenOverview();
            });

        _overviewButtonText =
            overviewButton.GetComponentInChildren<Text>();

        CreateButtonCanvas(
            "HelpButton",
            _hudRoot.transform,
            new Vector3(0.28f, -0.22f, 0.78f),
            new Vector2(86f, 68f),
            "?",
            () => ShowOnboarding(0));

        _hudRoot.SetActive(false);
    }

    private void ShowIdle()
    {
        DestroyTransientUi();
        if (_hudRoot != null)
            _hudRoot.SetActive(true);

        if (_overviewButtonText != null)
            _overviewButtonText.text = "Overview";
    }

    public void ShowOnboarding(int step)
    {
        _onboardingStep = Mathf.Clamp(step, 0, 2);
        DestroyTransientUi();

        if (_hudRoot != null)
            _hudRoot.SetActive(false);

        _onboardingRoot = CreateHeadLockedPanel(
            "Onboarding",
            new Vector2(700f, 430f),
            new Vector3(0f, 0f, 0.9f));

        switch (_onboardingStep)
        {
            case 0:
                AddText(
                    _onboardingRoot.transform,
                    "Axis XR Monitoring",
                    38,
                    FontStyle.Bold,
                    TextPrimary,
                    new Vector2(36f, -32f),
                    new Vector2(-36f, -95f));

                AddText(
                    _onboardingRoot.transform,
                    "Find camera problems without losing sight of the real environment.\n\nOpen the overview when you need it; keep it closed when you do not.",
                    24,
                    FontStyle.Normal,
                    TextSecondary,
                    new Vector2(36f, -120f),
                    new Vector2(-36f, -275f));
                break;

            case 1:
                AddText(
                    _onboardingRoot.transform,
                    "See what needs attention first",
                    34,
                    FontStyle.Bold,
                    TextPrimary,
                    new Vector2(36f, -32f),
                    new Vector2(-36f, -90f));

                AddStatusLegend(
                    _onboardingRoot.transform,
                    "● HEALTHY",
                    HealthyColor,
                    -125f);

                AddStatusLegend(
                    _onboardingRoot.transform,
                    "▲ WARNING",
                    WarningColor,
                    -185f);

                AddStatusLegend(
                    _onboardingRoot.transform,
                    "■ OFFLINE",
                    OfflineColor,
                    -245f);

                AddText(
                    _onboardingRoot.transform,
                    "Abnormal cameras are placed closer and made more prominent in the overview.",
                    21,
                    FontStyle.Normal,
                    TextSecondary,
                    new Vector2(270f, -125f),
                    new Vector2(-36f, -270f));
                break;

            default:
                AddText(
                    _onboardingRoot.transform,
                    "Inspect with one clear action",
                    34,
                    FontStyle.Bold,
                    TextPrimary,
                    new Vector2(36f, -32f),
                    new Vector2(-36f, -90f));

                AddText(
                    _onboardingRoot.transform,
                    "Open Overview  →  Select a camera  →  View Details\n\nYou can also scan a camera QR code to open that camera directly.",
                    24,
                    FontStyle.Normal,
                    TextSecondary,
                    new Vector2(36f, -125f),
                    new Vector2(-36f, -280f));
                break;
        }

        CreateButton(
            _onboardingRoot.transform,
            "Skip",
            new Vector2(170f, 62f),
            new Vector2(125f, -365f),
            FinishOnboarding,
            PanelSoftColor);

        string nextLabel =
            _onboardingStep == 2 ? "Start monitoring" : "Next";

        CreateButton(
            _onboardingRoot.transform,
            nextLabel,
            new Vector2(240f, 62f),
            new Vector2(540f, -365f),
            () =>
            {
                if (_onboardingStep >= 2)
                    FinishOnboarding();
                else
                    ShowOnboarding(_onboardingStep + 1);
            },
            AccentColor);
    }

    private void FinishOnboarding()
    {
        PlayerPrefs.SetInt(OnboardingKey, 1);
        PlayerPrefs.Save();
        ShowIdle();
    }

    public void OpenOverview()
    {
        if (_apiClient == null)
        {
            ShowFriendlyError("Camera data service is not available.");
            return;
        }

        DestroyTransientUi();

        if (_hudRoot != null)
            _hudRoot.SetActive(true);

        if (_overviewButtonText != null)
            _overviewButtonText.text = "Close";

        ShowMessage("Updating camera status...");

        _apiClient.GetAllCameras(
            cameras =>
            {
                HideMessage();
                BuildOverview(cameras);
            },
            error =>
            {
                Debug.LogError($"OVERVIEW LOAD FAILED | {error}");
                ShowOverviewError();
            });
    }

    private void BuildOverview(CameraData[] cameras)
    {
        if (cameras == null || cameras.Length == 0)
        {
            ShowOverviewError();
            return;
        }

        Array.Sort(cameras, CompareCameraPriority);

        _overviewRoot = new GameObject("SpatialOverview");
        _overviewRoot.transform.position = _camera.transform.position;
        _overviewRoot.transform.rotation = _camera.transform.rotation;

        _cards.Clear();

        CreateOverviewTitle(_overviewRoot.transform);

        Dictionary<string, int> counts =
            new Dictionary<string, int>();

        foreach (CameraData camera in cameras)
        {
            if (camera == null)
                continue;

            string status = NormalizeStatus(camera.status);

            if (!counts.ContainsKey(status))
                counts[status] = 0;

            int statusIndex = counts[status]++;
            Vector3 localPosition =
                GetOverviewPosition(status, statusIndex);

            CardVisual card =
                CreateCameraCard(
                    _overviewRoot.transform,
                    camera,
                    localPosition);

            _cards.Add(card);
        }
    }

    private static int CompareCameraPriority(
        CameraData a,
        CameraData b)
    {
        return StatusRank(a?.status).CompareTo(
            StatusRank(b?.status));
    }

    private static int StatusRank(string status)
    {
        switch (NormalizeStatus(status))
        {
            case "OFFLINE":
                return 0;
            case "WARNING":
                return 1;
            default:
                return 2;
        }
    }

    private static string NormalizeStatus(string status)
    {
        return string.IsNullOrWhiteSpace(status)
            ? "UNKNOWN"
            : status.Trim().ToUpperInvariant();
    }

    private Vector3 GetOverviewPosition(
        string status,
        int index)
    {
        float sideOffset =
            index == 0
                ? 0f
                : ((index % 2 == 1 ? 1f : -1f) *
                   (0.34f + 0.20f * ((index - 1) / 2)));

        switch (status)
        {
            case "OFFLINE":
                return new Vector3(
                    -0.22f + sideOffset,
                    0.07f,
                    0.76f);

            case "WARNING":
                return new Vector3(
                    0.20f + sideOffset,
                    0.04f,
                    0.88f);

            default:
                return new Vector3(
                    0.05f + sideOffset,
                    -0.16f,
                    1.12f);
        }
    }

    private void CreateOverviewTitle(Transform parent)
    {
        GameObject titleCanvas =
            CreateCanvas(
                "OverviewTitle",
                new Vector2(450f, 80f),
                parent,
                new Vector3(0f, 0.26f, 0.95f),
                PanelSoftColor);

        AddText(
            titleCanvas.transform,
            "Camera Overview",
            28,
            FontStyle.Bold,
            TextPrimary,
            new Vector2(22f, -16f),
            new Vector2(-22f, -58f));
    }

    private CardVisual CreateCameraCard(
        Transform parent,
        CameraData camera,
        Vector3 localPosition)
    {
        string status = NormalizeStatus(camera.status);
        Color statusColor = StatusColor(status);

        Vector2 size =
            status == "HEALTHY"
                ? new Vector2(290f, 126f)
                : new Vector2(330f, 145f);

        GameObject card =
            CreateCanvas(
                $"Card_{camera.cameraId}",
                size,
                parent,
                localPosition,
                PanelColor);

        CanvasGroup group =
            card.AddComponent<CanvasGroup>();

        Image background =
            card.transform.Find("Background")
                .GetComponent<Image>();

        string icon =
            status == "OFFLINE"
                ? "■"
                : status == "WARNING"
                    ? "▲"
                    : "●";

        AddText(
            card.transform,
            camera.cameraId,
            status == "HEALTHY" ? 25 : 28,
            FontStyle.Bold,
            TextPrimary,
            new Vector2(18f, -15f),
            new Vector2(-18f, -52f));

        AddText(
            card.transform,
            camera.name,
            19,
            FontStyle.Normal,
            TextSecondary,
            new Vector2(18f, -53f),
            new Vector2(-18f, -84f));

        AddText(
            card.transform,
            $"{icon} {status}",
            20,
            FontStyle.Bold,
            statusColor,
            new Vector2(18f, -88f),
            new Vector2(-18f, -120f));

        XRClickable clickable =
            AddClickable(
                card,
                background,
                () => SelectCamera(camera),
                PanelColor,
                new Color(
                    PanelColor.r + 0.08f,
                    PanelColor.g + 0.08f,
                    PanelColor.b + 0.10f,
                    PanelColor.a));

        return new CardVisual
        {
            data = camera,
            group = group,
            clickable = clickable
        };
    }

    private void SelectCamera(CameraData camera)
    {
        foreach (CardVisual card in _cards)
        {
            if (card.group != null)
            {
                card.group.alpha =
                    card.data.cameraId == camera.cameraId
                        ? 1f
                        : 0.28f;
            }
        }

        ShowQuickPreview(camera);
    }

    private void ShowQuickPreview(CameraData camera)
    {
        if (_previewRoot != null)
            Destroy(_previewRoot);

        _previewRoot = CreateHeadLockedPanel(
            "QuickPreview",
            new Vector2(520f, 270f),
            new Vector3(0f, -0.19f, 0.82f));

        string status = NormalizeStatus(camera.status);

        AddText(
            _previewRoot.transform,
            camera.cameraId,
            31,
            FontStyle.Bold,
            TextPrimary,
            new Vector2(26f, -22f),
            new Vector2(-26f, -65f));

        AddText(
            _previewRoot.transform,
            camera.name,
            22,
            FontStyle.Normal,
            TextSecondary,
            new Vector2(26f, -68f),
            new Vector2(-26f, -104f));

        AddText(
            _previewRoot.transform,
            StatusSymbol(status) + " " + status,
            23,
            FontStyle.Bold,
            StatusColor(status),
            new Vector2(26f, -112f),
            new Vector2(-26f, -150f));

        CreateButton(
            _previewRoot.transform,
            "View Details",
            new Vector2(220f, 60f),
            new Vector2(378f, -222f),
            () => ShowDetails(camera),
            AccentColor);

        CreateButton(
            _previewRoot.transform,
            "Back",
            new Vector2(120f, 60f),
            new Vector2(92f, -222f),
            ClearSelection,
            PanelSoftColor);
    }

    private void ClearSelection()
    {
        if (_previewRoot != null)
        {
            Destroy(_previewRoot);
            _previewRoot = null;
        }

        foreach (CardVisual card in _cards)
        {
            if (card.group != null)
                card.group.alpha = 1f;
        }
    }

    public void ShowScannedCamera(CameraData camera)
    {
        if (camera == null)
            return;

        StartCoroutine(ShowScanConfirmation(camera));
    }

    private IEnumerator ShowScanConfirmation(CameraData camera)
    {
        DestroyTransientUi();

        if (_hudRoot != null)
            _hudRoot.SetActive(false);

        ShowMessage($"✓ Camera identified\n{camera.cameraId}");
        yield return new WaitForSeconds(0.75f);
        HideMessage();
        ShowDetails(camera);
    }

    public void ShowDetails(CameraData camera)
    {
        if (camera == null)
            return;

        if (_overviewRoot != null)
        {
            Destroy(_overviewRoot);
            _overviewRoot = null;
            _cards.Clear();
        }

        if (_previewRoot != null)
        {
            Destroy(_previewRoot);
            _previewRoot = null;
        }

        if (_detailsRoot != null)
            Destroy(_detailsRoot);

        if (_hudRoot != null)
            _hudRoot.SetActive(false);

        _detailsRoot = CreateHeadLockedPanel(
            "CameraDetails",
            new Vector2(680f, 500f),
            new Vector3(0f, 0f, 0.92f));

        string status = NormalizeStatus(camera.status);

        AddText(
            _detailsRoot.transform,
            camera.name,
            32,
            FontStyle.Bold,
            TextPrimary,
            new Vector2(30f, -24f),
            new Vector2(-220f, -66f));

        AddText(
            _detailsRoot.transform,
            camera.cameraId,
            19,
            FontStyle.Normal,
            TextSecondary,
            new Vector2(30f, -68f),
            new Vector2(-220f, -100f));

        AddText(
            _detailsRoot.transform,
            StatusSymbol(status) + " " + status,
            22,
            FontStyle.Bold,
            StatusColor(status),
            new Vector2(430f, -32f),
            new Vector2(-30f, -72f));

        if (status == "OFFLINE")
            BuildOfflineDetails(camera);
        else if (status == "WARNING")
            BuildWarningDetails(camera);
        else
            BuildHealthyDetails(camera);

        CreateButton(
            _detailsRoot.transform,
            "Overview",
            new Vector2(170f, 58f),
            new Vector2(500f, -455f),
            OpenOverview,
            AccentColor);

        CreateButton(
            _detailsRoot.transform,
            "Close",
            new Vector2(120f, 58f),
            new Vector2(95f, -455f),
            ShowIdle,
            PanelSoftColor);
    }

    private void BuildHealthyDetails(CameraData camera)
    {
        AddSectionLabel("HEALTH", -128f);

        string temperature =
            camera.temperatureAvailable
                ? $"{camera.temperature:0.0} C"
                : "N/A";

        AddDetailRows(
            camera,
            -165f,
            new[]
            {
                $"Temperature|{temperature}",
                $"Storage|{(camera.storageHealthy ? "Healthy" : "Problem")}",
                $"Uptime|{FormatUptime(camera.uptime)}",
                $"Model|{camera.model}",
                $"OS|{camera.osVersion}",
                $"Updated|{FormatServerTime(camera.serverTime)}"
            });
    }

    private void BuildWarningDetails(CameraData camera)
    {
        AddSectionLabel("ISSUE", -128f);

        string issueTitle = "Attention required";
        string issueValue = "";

        if (camera.temperatureAvailable &&
            camera.temperature >= 70f)
        {
            issueTitle = "High temperature";
            issueValue = $"{camera.temperature:0.0} C";
        }
        else if (!camera.storageHealthy)
        {
            issueTitle = "Storage problem";
            issueValue = "Check device storage";
        }

        GameObject issueBox =
            CreateRect(
                _detailsRoot.transform,
                "IssueBox",
                new Vector2(620f, 92f),
                new Vector2(340f, -205f),
                new Color(0.22f, 0.14f, 0.035f, 0.96f));

        AddText(
            issueBox.transform,
            issueTitle,
            24,
            FontStyle.Bold,
            WarningColor,
            new Vector2(20f, -14f),
            new Vector2(-20f, -48f));

        AddText(
            issueBox.transform,
            issueValue,
            22,
            FontStyle.Normal,
            TextPrimary,
            new Vector2(20f, -51f),
            new Vector2(-20f, -82f));

        AddSectionLabel("OTHER HEALTH", -315f);

        AddDetailRows(
            camera,
            -345f,
            new[]
            {
                $"Storage|{(camera.storageHealthy ? "Healthy" : "Problem")}",
                $"Uptime|{FormatUptime(camera.uptime)}",
                $"Updated|{FormatServerTime(camera.serverTime)}"
            },
            31f);
    }

    private void BuildOfflineDetails(CameraData camera)
    {
        AddSectionLabel("STATUS", -140f);

        AddText(
            _detailsRoot.transform,
            "Camera unavailable",
            30,
            FontStyle.Bold,
            OfflineColor,
            new Vector2(30f, -180f),
            new Vector2(-30f, -225f));

        AddText(
            _detailsRoot.transform,
            "No current telemetry can be retrieved.\n\nLast update: " +
            FormatServerTime(camera.serverTime),
            22,
            FontStyle.Normal,
            TextSecondary,
            new Vector2(30f, -245f),
            new Vector2(-30f, -355f));
    }

    private void AddSectionLabel(string label, float top)
    {
        AddText(
            _detailsRoot.transform,
            label,
            17,
            FontStyle.Bold,
            TextSecondary,
            new Vector2(30f, top),
            new Vector2(-30f, top - 28f));
    }

    private void AddDetailRows(
        CameraData camera,
        float startTop,
        string[] rows,
        float rowHeight = 38f)
    {
        float top = startTop;

        foreach (string row in rows)
        {
            string[] parts = row.Split('|');
            if (parts.Length != 2)
                continue;

            AddText(
                _detailsRoot.transform,
                parts[0],
                20,
                FontStyle.Normal,
                TextSecondary,
                new Vector2(30f, top),
                new Vector2(-360f, top - 30f));

            AddText(
                _detailsRoot.transform,
                parts[1],
                20,
                FontStyle.Bold,
                TextPrimary,
                new Vector2(340f, top),
                new Vector2(-30f, top - 30f));

            top -= rowHeight;
        }
    }

    private void CloseOverview()
    {
        if (_overviewRoot != null)
        {
            Destroy(_overviewRoot);
            _overviewRoot = null;
        }

        if (_previewRoot != null)
        {
            Destroy(_previewRoot);
            _previewRoot = null;
        }

        _cards.Clear();

        if (_overviewButtonText != null)
            _overviewButtonText.text = "Overview";
    }

    private void ShowOverviewError()
    {
        HideMessage();

        _messageRoot = CreateHeadLockedPanel(
            "OverviewError",
            new Vector2(520f, 250f),
            new Vector3(0f, 0f, 0.88f));

        AddText(
            _messageRoot.transform,
            "Unable to retrieve camera status",
            27,
            FontStyle.Bold,
            TextPrimary,
            new Vector2(26f, -30f),
            new Vector2(-26f, -75f));

        AddText(
            _messageRoot.transform,
            "Check the Quest Wi-Fi connection and try again.",
            21,
            FontStyle.Normal,
            TextSecondary,
            new Vector2(26f, -92f),
            new Vector2(-26f, -145f));

        CreateButton(
            _messageRoot.transform,
            "Retry",
            new Vector2(160f, 58f),
            new Vector2(390f, -205f),
            OpenOverview,
            AccentColor);

        CreateButton(
            _messageRoot.transform,
            "Close",
            new Vector2(120f, 58f),
            new Vector2(90f, -205f),
            ShowIdle,
            PanelSoftColor);
    }

    public void ShowFriendlyError(string message)
    {
        Debug.LogError(message);
        DestroyTransientUi();

        if (_hudRoot != null)
            _hudRoot.SetActive(false);

        _messageRoot = CreateHeadLockedPanel(
            "FriendlyError",
            new Vector2(540f, 260f),
            new Vector3(0f, 0f, 0.9f));

        AddText(
            _messageRoot.transform,
            "Unable to retrieve camera data",
            28,
            FontStyle.Bold,
            TextPrimary,
            new Vector2(26f, -30f),
            new Vector2(-26f, -78f));

        AddText(
            _messageRoot.transform,
            "Check Wi-Fi and try again.",
            22,
            FontStyle.Normal,
            TextSecondary,
            new Vector2(26f, -100f),
            new Vector2(-26f, -145f));

        CreateButton(
            _messageRoot.transform,
            "Close",
            new Vector2(130f, 58f),
            new Vector2(270f, -215f),
            ShowIdle,
            PanelSoftColor);
    }

    private void ShowMessage(string text)
    {
        HideMessage();

        _messageRoot = CreateHeadLockedPanel(
            "SystemMessage",
            new Vector2(430f, 150f),
            new Vector3(0f, 0f, 0.82f));

        AddText(
            _messageRoot.transform,
            text,
            25,
            FontStyle.Bold,
            TextPrimary,
            new Vector2(24f, -34f),
            new Vector2(-24f, -115f),
            TextAnchor.MiddleCenter);
    }

    private void HideMessage()
    {
        if (_messageRoot != null)
        {
            Destroy(_messageRoot);
            _messageRoot = null;
        }
    }

    private void DestroyTransientUi()
    {
        if (_onboardingRoot != null)
        {
            Destroy(_onboardingRoot);
            _onboardingRoot = null;
        }

        if (_overviewRoot != null)
        {
            Destroy(_overviewRoot);
            _overviewRoot = null;
        }

        if (_previewRoot != null)
        {
            Destroy(_previewRoot);
            _previewRoot = null;
        }

        if (_detailsRoot != null)
        {
            Destroy(_detailsRoot);
            _detailsRoot = null;
        }

        HideMessage();
        _cards.Clear();
    }

    private GameObject CreateHeadLockedPanel(
        string name,
        Vector2 size,
        Vector3 localPosition)
    {
        return CreateCanvas(
            name,
            size,
            _camera.transform,
            localPosition,
            PanelColor);
    }

    private GameObject CreateCanvas(
        string name,
        Vector2 size,
        Transform parent,
        Vector3 localPosition,
        Color backgroundColor)
    {
        GameObject root = new GameObject(
            name,
            typeof(RectTransform),
            typeof(Canvas),
            typeof(CanvasScaler));

        root.transform.SetParent(parent, false);
        root.transform.localPosition = localPosition;
        root.transform.localRotation = Quaternion.identity;
        root.transform.localScale = Vector3.one * 0.001f;

        RectTransform rect = root.GetComponent<RectTransform>();
        rect.sizeDelta = size;

        Canvas canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.sortingOrder = 100;

        GameObject background = new GameObject(
            "Background",
            typeof(RectTransform),
            typeof(Image));

        background.transform.SetParent(root.transform, false);

        RectTransform bgRect =
            background.GetComponent<RectTransform>();

        bgRect.anchorMin = Vector2.zero;
        bgRect.anchorMax = Vector2.one;
        bgRect.offsetMin = Vector2.zero;
        bgRect.offsetMax = Vector2.zero;

        background.GetComponent<Image>().color =
            backgroundColor;

        return root;
    }

    private GameObject CreateRect(
        Transform parent,
        string name,
        Vector2 size,
        Vector2 anchoredPosition,
        Color color)
    {
        GameObject rectObject = new GameObject(
            name,
            typeof(RectTransform),
            typeof(Image));

        rectObject.transform.SetParent(parent, false);

        RectTransform rect =
            rectObject.GetComponent<RectTransform>();

        rect.anchorMin = new Vector2(0.5f, 1f);
        rect.anchorMax = new Vector2(0.5f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.sizeDelta = size;
        rect.anchoredPosition = anchoredPosition;

        rectObject.GetComponent<Image>().color = color;
        return rectObject;
    }

    private GameObject CreateButtonCanvas(
        string name,
        Transform parent,
        Vector3 localPosition,
        Vector2 size,
        string label,
        Action onClick)
    {
        GameObject root =
            CreateCanvas(
                name,
                size,
                parent,
                localPosition,
                PanelSoftColor);

        Image background =
            root.transform.Find("Background")
                .GetComponent<Image>();

        AddText(
            root.transform,
            label,
            22,
            FontStyle.Bold,
            TextPrimary,
            new Vector2(12f, -10f),
            new Vector2(-12f, -58f),
            TextAnchor.MiddleCenter);

        AddClickable(
            root,
            background,
            onClick,
            PanelSoftColor,
            new Color(0.12f, 0.20f, 0.28f, 0.98f));

        return root;
    }

    private GameObject CreateButton(
        Transform parent,
        string label,
        Vector2 size,
        Vector2 anchoredPosition,
        Action onClick,
        Color backgroundColor)
    {
        GameObject button = CreateRect(
            parent,
            $"Button_{label}",
            size,
            anchoredPosition,
            backgroundColor);

        AddText(
            button.transform,
            label,
            20,
            FontStyle.Bold,
            TextPrimary,
            new Vector2(10f, -8f),
            new Vector2(-10f, -52f),
            TextAnchor.MiddleCenter);

        AddClickable(
            button,
            button.GetComponent<Image>(),
            onClick,
            backgroundColor,
            new Color(
                Mathf.Min(1f, backgroundColor.r + 0.10f),
                Mathf.Min(1f, backgroundColor.g + 0.10f),
                Mathf.Min(1f, backgroundColor.b + 0.10f),
                backgroundColor.a));

        return button;
    }

    private XRClickable AddClickable(
        GameObject objectRoot,
        Image targetImage,
        Action onClick,
        Color normalColor,
        Color hoverColor)
    {
        RectTransform rect =
            objectRoot.GetComponent<RectTransform>();

        BoxCollider collider =
            objectRoot.AddComponent<BoxCollider>();

        collider.size =
            new Vector3(
                rect.rect.width,
                rect.rect.height,
                12f);

        collider.isTrigger = true;

        XRClickable clickable =
            objectRoot.AddComponent<XRClickable>();

        clickable.Initialize(
            targetImage,
            onClick,
            normalColor,
            hoverColor);

        return clickable;
    }

    private Text AddText(
        Transform parent,
        string textValue,
        int fontSize,
        FontStyle fontStyle,
        Color color,
        Vector2 topLeft,
        Vector2 bottomRight,
        TextAnchor alignment = TextAnchor.UpperLeft)
    {
        GameObject textObject = new GameObject(
            "Text",
            typeof(RectTransform),
            typeof(Text));

        textObject.transform.SetParent(parent, false);

        RectTransform rect =
            textObject.GetComponent<RectTransform>();

        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.offsetMin =
            new Vector2(topLeft.x, bottomRight.y);
        rect.offsetMax =
            new Vector2(bottomRight.x, topLeft.y);

        Text text = textObject.GetComponent<Text>();
        text.font = GetFont();
        text.fontSize = fontSize;
        text.fontStyle = fontStyle;
        text.color = color;
        text.alignment = alignment;
        text.horizontalOverflow =
            HorizontalWrapMode.Wrap;
        text.verticalOverflow =
            VerticalWrapMode.Overflow;
        text.raycastTarget = false;
        text.text = textValue;

        return text;
    }

    private void AddStatusLegend(
        Transform parent,
        string label,
        Color color,
        float top)
    {
        AddText(
            parent,
            label,
            23,
            FontStyle.Bold,
            color,
            new Vector2(40f, top),
            new Vector2(-430f, top - 40f));
    }

    private static string StatusSymbol(string status)
    {
        switch (NormalizeStatus(status))
        {
            case "OFFLINE":
                return "■";
            case "WARNING":
                return "▲";
            default:
                return "●";
        }
    }

    private static Color StatusColor(string status)
    {
        switch (NormalizeStatus(status))
        {
            case "OFFLINE":
                return OfflineColor;
            case "WARNING":
                return WarningColor;
            default:
                return HealthyColor;
        }
    }

    private static string FormatServerTime(string serverTime)
    {
        if (string.IsNullOrWhiteSpace(serverTime))
            return "N/A";

        if (DateTime.TryParse(
            serverTime,
            null,
            System.Globalization.DateTimeStyles.RoundtripKind,
            out DateTime parsed))
        {
            return parsed
                .ToLocalTime()
                .ToString("HH:mm:ss");
        }

        return serverTime;
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
