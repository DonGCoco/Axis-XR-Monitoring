using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

public partial class AxisMonitoringUI
{
    // Locate uses a slim direction bar plus a collapsible drawer.
    // Register and Locations keep the original full-size panel.
    private const float LocateBarWidth = 460f;
    private const float LocateBarHeight = 100f;
    private const float LocateDrawerHeight = 230f;
    private const float LocateNoticeHeight = 44f;
    private const float LocateBarDistance = 0.72f;
    private const float LocateBarDrop = -0.135f;
    private const float LocateNearDistance = 0.8f;

    private static readonly Color LocateArrowColor =
        new Color(0.26f, 0.65f, 1f, 0.85f);

    private CameraSpatialAnchors _positions;
    private CameraNavigationPath _navigationPath;

    private GameObject _spatialRoot;
    private Text _spatialText;
    private Text _locationStatus;
    private string _detailsCameraId;

    private GameObject _locateBarRoot;
    private GameObject _locateDrawerRoot;
    private GameObject _locateNoticeRoot;
    private GameObject _routeArrow;
    private GameObject _locateTargetTag;
    private Text _locateArrowGlyph;
    private Text _locateDistance;
    private Text _locateSubtitle;
    private Text _locateNotice;
    private Text _locateTagLabel;
    private Text _drawerStatus;
    private Text _drawerToggleLabel;
    private bool _drawerOpen;

    private CameraData _locateTarget;
    private CameraData _registerTarget;
    private bool _savingPosition;
    private string _scanNotice;
    private float _scanNoticeUntil;
    private float _routeRefreshAt;
    private bool _routeAvailable;
    private CameraNavigationPath.Route _route;
    public int SpatialWorkflowVersion { get; private set; }

    private async void InitializeSpatialWorkflow()
    {
        _positions = gameObject.AddComponent<CameraSpatialAnchors>();
        _navigationPath = gameObject.AddComponent<CameraNavigationPath>();

        // This is a passthrough-first MR app. Keep Meta's official Boundary API
        // suppression active for the whole app session so users can walk between
        // rooms before, during, and after camera registration.
        SetSpatialBoundaryless(true);

        string site = PlayerPrefs.GetString("AXIS_ANCHOR_SITE", "Home");
        await _positions.SelectSiteAsync(site == "Lab" ? "Lab" : "Home");
    }

    private void EndSpatialWorkflow()
    {
        if (_savingPosition) return;
        SpatialWorkflowVersion++;
        _locateTarget = null;
        _registerTarget = null;
        _scanNotice = null;

        if (_spatialRoot) Destroy(_spatialRoot);
        _spatialRoot = null;
        _spatialText = null;

        DestroyLocateHud();

        _routeAvailable = false;
        _routeRefreshAt = 0f;
    }

    // Only the Locate objects are torn down here so Register / Locations
    // can keep using the shared full-size panel.
    private void DestroyLocateHud()
    {
        if (_locateBarRoot) Destroy(_locateBarRoot);
        if (_locateDrawerRoot) Destroy(_locateDrawerRoot);
        if (_locateNoticeRoot) Destroy(_locateNoticeRoot);
        if (_routeArrow) Destroy(_routeArrow);
        if (_locateTargetTag) Destroy(_locateTargetTag);

        _locateBarRoot = null;
        _locateDrawerRoot = null;
        _locateNoticeRoot = null;
        _routeArrow = null;
        _locateTargetTag = null;

        _locateArrowGlyph = null;
        _locateDistance = null;
        _locateSubtitle = null;
        _locateNotice = null;
        _locateTagLabel = null;
        _drawerStatus = null;
        _drawerToggleLabel = null;
        _drawerOpen = false;
    }

    private void BuildSpatialPanel(string title)
    {
        DestroyTransientUi();
        if (_hudRoot) _hudRoot.SetActive(false);
        _spatialRoot = CreateHeadLockedPanel("CameraLocations", new Vector2(620f, 380f), new Vector3(0f, -0.08f, 0.9f));
        AddText(_spatialRoot.transform, title, 28, FontStyle.Bold, TextPrimary,
            new Vector2(24f, -20f), new Vector2(-24f, -60f));
        _spatialText = AddText(_spatialRoot.transform, "", 21, FontStyle.Normal, TextSecondary,
            new Vector2(24f, -75f), new Vector2(-24f, -250f));
    }

    private void BeginLocate(CameraData camera)
    {
        if (_savingPosition || _positions == null) return;
        EndSpatialWorkflow();
        _locateTarget = camera;
        _routeAvailable = false;
        _routeRefreshAt = 0f;
        _navigationPath?.EnsureReady();
        SetSpatialBoundaryless(true);
        BuildLocateHud(camera);
    }

    private void BuildLocateHud(CameraData camera)
    {
        DestroyTransientUi();
        if (_hudRoot) _hudRoot.SetActive(false);

        // Direction bar. Deliberately small and dropped below the eye line so
        // the walkable view stays clear while the user moves to the camera.
        _locateBarRoot = CreateCanvas(
            "LocateBar",
            new Vector2(LocateBarWidth, LocateBarHeight),
            _camera.transform,
            new Vector3(0f, LocateBarDrop, LocateBarDistance),
            PanelColor);

        CreateRect(
            _locateBarRoot.transform,
            "AccentEdge",
            new Vector2(LocateBarWidth, 3f),
            new Vector2(0f, -LocateBarHeight + 3f),
            AccentColor);

        _locateArrowGlyph = AddText(
            _locateBarRoot.transform,
            "\u2191",
            46,
            FontStyle.Bold,
            AccentColor,
            new Vector2(12f, -12f),
            new Vector2(88f, -88f),
            TextAnchor.MiddleCenter);

        _locateDistance = AddText(
            _locateBarRoot.transform,
            "\u2014",
            34,
            FontStyle.Bold,
            TextPrimary,
            new Vector2(96f, -10f),
            new Vector2(310f, -56f));

        _locateSubtitle = AddText(
            _locateBarRoot.transform,
            camera.cameraId,
            15,
            FontStyle.Normal,
            TextSecondary,
            new Vector2(96f, -56f),
            new Vector2(336f, -82f));

        GameObject detailsButton = CreateButton(
            _locateBarRoot.transform,
            "Details",
            new Vector2(88f, 38f),
            new Vector2(154f, -31f),
            ToggleLocateDrawer,
            PanelSoftColor);
        _drawerToggleLabel = detailsButton.GetComponentInChildren<Text>();

        // Transient scan feedback sits just above the bar, where the user is
        // already looking, instead of competing for space inside the bar.
        _locateNoticeRoot = CreateCanvas(
            "LocateNotice",
            new Vector2(LocateBarWidth, LocateNoticeHeight),
            _camera.transform,
            new Vector3(
                0f,
                LocateBarDrop + (LocateBarHeight * 0.0005f) + 0.004f + (LocateNoticeHeight * 0.0005f),
                LocateBarDistance),
            PanelSoftColor);

        _locateNotice = AddText(
            _locateNoticeRoot.transform,
            "",
            17,
            FontStyle.Normal,
            WarningColor,
            new Vector2(14f, -2f),
            new Vector2(-14f, -LocateNoticeHeight + 2f),
            TextAnchor.MiddleCenter);
        _locateNoticeRoot.SetActive(false);

        BuildLocateDrawer(camera);

        BuildWorldGuides();
    }

    private void BuildLocateDrawer(CameraData camera)
    {
        _drawerOpen = false;

        _locateDrawerRoot = CreateCanvas(
            "LocateDrawer",
            new Vector2(LocateBarWidth, LocateDrawerHeight),
            _camera.transform,
            new Vector3(
                0f,
                LocateBarDrop - (LocateBarHeight * 0.0005f) - 0.006f - (LocateDrawerHeight * 0.0005f),
                LocateBarDistance),
            PanelSoftColor);

        AddText(
            _locateDrawerRoot.transform,
            camera.name,
            20,
            FontStyle.Bold,
            TextPrimary,
            new Vector2(20f, -12f),
            new Vector2(-20f, -42f));

        string model = string.IsNullOrWhiteSpace(camera.model) ? camera.cameraId : camera.model;
        AddText(
            _locateDrawerRoot.transform,
            camera.cameraId + "  \u00b7  " + model + "  \u00b7  " + _positions.Site,
            14,
            FontStyle.Normal,
            TextSecondary,
            new Vector2(20f, -42f),
            new Vector2(-20f, -66f));

        _drawerStatus = AddText(
            _locateDrawerRoot.transform,
            "",
            15,
            FontStyle.Normal,
            TextSecondary,
            new Vector2(20f, -72f),
            new Vector2(-20f, -160f));

        CreateButton(
            _locateDrawerRoot.transform,
            "Reload map",
            new Vector2(150f, 44f),
            new Vector2(-85f, -172f),
            () =>
            {
                _routeAvailable = false;
                _routeRefreshAt = 0f;
                _navigationPath?.ReloadScene();
            },
            AccentColor);

        CreateButton(
            _locateDrawerRoot.transform,
            "Cancel",
            new Vector2(120f, 44f),
            new Vector2(85f, -172f),
            () => ShowDetails(camera),
            PanelSoftColor);

        _locateDrawerRoot.SetActive(false);
    }

    private void ToggleLocateDrawer()
    {
        _drawerOpen = !_drawerOpen;
        if (_locateDrawerRoot != null)
            _locateDrawerRoot.SetActive(_drawerOpen);
        if (_drawerToggleLabel != null)
            _drawerToggleLabel.text = _drawerOpen ? "Hide" : "Details";
    }

    private void BuildWorldGuides()
    {
        // Floor arrow: three plain UI quads on a canvas laid flat on the ground.
        // Reusing the UI material avoids shipping an extra shader with the build.
        _routeArrow = new GameObject("RouteArrow");
        GameObject arrowCanvas = CreateCanvas(
            "ArrowCanvas",
            new Vector2(100f, 100f),
            _routeArrow.transform,
            Vector3.zero,
            new Color(0f, 0f, 0f, 0f));
        arrowCanvas.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        arrowCanvas.transform.localScale = Vector3.one * 0.004f;

        CreateArrowBar(arrowCanvas.transform, new Vector2(16f, 56f), new Vector2(0f, -14f), 0f);
        CreateArrowBar(arrowCanvas.transform, new Vector2(16f, 40f), new Vector2(-13f, 20f), 45f);
        CreateArrowBar(arrowCanvas.transform, new Vector2(16f, 40f), new Vector2(13f, 20f), -45f);
        _routeArrow.SetActive(false);

        // Near-target tag replaces the arrow once the user is close enough
        // that a floor arrow would sit under their feet.
        _locateTargetTag = CreateCanvas(
            "LocateTargetTag",
            new Vector2(150f, 48f),
            null,
            Vector3.zero,
            AccentColor);

        AddText(
            _locateTargetTag.transform,
            "",
            20,
            FontStyle.Bold,
            TextPrimary,
            new Vector2(4f, -4f),
            new Vector2(-4f, -44f),
            TextAnchor.MiddleCenter);

        _locateTagLabel = _locateTargetTag.GetComponentInChildren<Text>();
        _locateTargetTag.SetActive(false);
    }

    private void CreateArrowBar(Transform parent, Vector2 size, Vector2 center, float angle)
    {
        GameObject bar = CreateRect(parent, "ArrowBar", size, Vector2.zero, LocateArrowColor);
        RectTransform rect = bar.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = center;
        rect.localEulerAngles = new Vector3(0f, 0f, angle);
    }

    // QrCameraScanner calls this before the API lookup so a wrong QR never
    // replaces the active navigation target.
    public bool AcceptCameraScan(string id)
    {
        if (_registerTarget != null || _savingPosition) return false;
        if (_locateTarget == null) return true;
        if (id == _locateTarget.cameraId) return true;
        _scanNotice = "Wrong camera: " + id + ". Find " + _locateTarget.cameraId;
        _scanNoticeUntil = Time.unscaledTime + 4f;
        return false;
    }

    public void ShowCameraScanError(string message)
    {
        if (_locateTarget != null)
        {
            _scanNotice = message + " Scan the target QR again to retry.";
            _scanNoticeUntil = Time.unscaledTime + 8f;
        }
        else ShowFriendlyError(message);
    }

    public bool IsScanResponseCurrent(string id, int version)
    {
        return version == SpatialWorkflowVersion && (_locateTarget == null || _locateTarget.cameraId == id)
            && _registerTarget == null && !_savingPosition;
    }

    private void BeginRegistration(CameraData camera)
    {
        if (_savingPosition || _positions == null || _positions.Busy) return;
        EndSpatialWorkflow();
        _registerTarget = camera;
        SetSpatialBoundaryless(true);
        BuildSpatialPanel("Register " + camera.cameraId + " | " + _positions.Site);
        _spatialText.text =
            "Scan the QR attached beside " + camera.cameraId + ".\n" +
            "Its tracked QR pose will become the saved physical camera position.\n" +
            "No controller placement is required.";
        CreateButton(_spatialRoot.transform, "Cancel", new Vector2(160f, 52f), new Vector2(0f, -310f),
            () => ShowDetails(camera), PanelSoftColor);
    }

    // QrCameraScanner calls this with the Meta MRUK trackable pose.
    // Return true when the QR was consumed by registration mode.
    public bool TryRegisterCameraFromQr(string id, Pose qrPose)
    {
        if (_registerTarget == null)
            return false;

        if (_savingPosition)
            return true;

        if (id != _registerTarget.cameraId)
        {
            _scanNotice = "Wrong camera: " + id + ". Scan " + _registerTarget.cameraId + " to register its position.";
            _scanNoticeUntil = Time.unscaledTime + 5f;
            return true;
        }

        _scanNotice = "QR confirmed. Saving spatial anchor at the tracked QR position\u2026";
        _scanNoticeUntil = Time.unscaledTime + 8f;
        SavePosition(_registerTarget, qrPose);
        return true;
    }

    private async void SavePosition(CameraData camera, Pose pose)
    {
        _savingPosition = true;
        bool saved = false;
        try
        {
            saved = await _positions.RegisterAsync(camera.cameraId, pose);
            if (!this) return;
            _scanNotice = _positions.Status;
            _scanNoticeUntil = Time.unscaledTime + 8f;
        }
        catch (Exception ex) { Debug.LogException(ex); }
        finally { _savingPosition = false; }
        if (this && saved) ShowDetails(camera);
    }

    private void ShowLocationSettings(CameraData camera, bool confirmClear = false)
    {
        if (_savingPosition || _positions == null) return;
        EndSpatialWorkflow();
        BuildSpatialPanel("Locations");
        if (confirmClear)
        {
            _scanNotice = "Delete all saved camera positions in " + _positions.Site + "?";
            _scanNoticeUntil = Time.unscaledTime + 3600f;
            CreateButton(_spatialRoot.transform, "Confirm clear", new Vector2(220f, 52f), new Vector2(-140f, -310f),
                ClearCurrentSite, OfflineColor);
        }
        else
        {
            CreateButton(_spatialRoot.transform, "Home / Lab", new Vector2(170f, 52f), new Vector2(-200f, -250f), SwitchLocationSite, AccentColor);
            CreateButton(_spatialRoot.transform, "Retry restore", new Vector2(170f, 52f), new Vector2(0f, -250f), RestoreLocations, PanelSoftColor);
            CreateButton(_spatialRoot.transform, "Clear site", new Vector2(170f, 52f), new Vector2(200f, -250f),
                () => { if (!_positions.Busy) ShowLocationSettings(camera, true); }, PanelSoftColor);
        }
        CreateButton(_spatialRoot.transform, "Back", new Vector2(150f, 52f), new Vector2(180f, -310f),
            () => ShowDetails(camera), PanelSoftColor);
    }

    private async void SwitchLocationSite() { if (!_positions.Busy) await _positions.SelectSiteAsync(_positions.Site == "Home" ? "Lab" : "Home"); }
    private async void RestoreLocations() { if (!_positions.Busy) await _positions.ReloadAsync(); }
    private async void ClearCurrentSite()
    {
        if (_positions.Busy) return;
        _scanNotice = null;
        await _positions.ClearSiteAsync();
    }

    private void Update()
    {
        if (_locationStatus != null && _positions != null && _detailsCameraId != null)
        {
            string state = _positions.Busy ? "Restoring / saving" :
                _positions.TryGetPosition(_detailsCameraId, out _) ? "Position ready" :
                _positions.HasBinding(_detailsCameraId) ? "Position saved; tracking unavailable" : "Position not registered";
            _locationStatus.text = _positions.Site + " | " + state;
        }

        if (_positions == null) return;

        UpdateScanNotice();

        if (_registerTarget != null)
        {
            if (_spatialText != null)
            {
                _spatialText.text = _savingPosition
                    ? _positions.Status
                    : $"Site: {_positions.Site}\nScan the QR attached beside {_registerTarget.cameraId}.\n" +
                      "The Meta QR trackable pose will be saved as this camera's spatial anchor.";
                AppendScanNoticeToPanel();
            }
            return;
        }

        if (_locateTarget != null)
        {
            UpdateLocateHud();
            return;
        }

        if (_spatialText != null)
        {
            _spatialText.text = $"Active site: {_positions.Site}\n{_positions.Status}\nSwitch to Lab and register again.";
            AppendScanNoticeToPanel();
        }
    }

    private void AppendScanNoticeToPanel()
    {
        if (_scanNotice != null && Time.unscaledTime < _scanNoticeUntil)
            _spatialText.text += "\n" + _scanNotice;
    }

    private void UpdateScanNotice()
    {
        if (_locateNoticeRoot == null) return;

        bool visible = _scanNotice != null && Time.unscaledTime < _scanNoticeUntil;
        if (_locateNoticeRoot.activeSelf != visible)
            _locateNoticeRoot.SetActive(visible);

        if (!visible || _locateNotice == null) return;

        _locateNotice.text = _scanNotice;
        _locateNotice.color =
            _scanNotice.StartsWith("Wrong camera", StringComparison.Ordinal) ? WarningColor :
            _scanNotice.IndexOf("confirmed", StringComparison.OrdinalIgnoreCase) >= 0 ? HealthyColor :
            TextPrimary;
    }

    private void UpdateLocateHud()
    {
        bool found = _positions.TryGetPosition(_locateTarget.cameraId, out var target);

        if (!found)
        {
            HideWorldGuides();
            SetBar("\u00b7", "\u2014", "Position unavailable");
            if (_drawerStatus != null)
            {
                _drawerStatus.text = _positions.Busy ? _positions.Status :
                    (_positions.HasBinding(_locateTarget.cameraId)
                        ? "Tracking / localization unavailable. Look around, or retry restore in Locations."
                        : "No position registered in " + _positions.Site + ". Register it first.");
            }
            return;
        }

        if (_navigationPath == null)
        {
            HideWorldGuides();
            SetBar("\u00b7", "\u2014", "Navigation unavailable");
            if (_drawerStatus != null)
                _drawerStatus.text = "MRUK SceneNavigation service is unavailable.";
            return;
        }

        _navigationPath.EnsureReady();

        if (Time.unscaledTime >= _routeRefreshAt)
        {
            _routeRefreshAt = Time.unscaledTime + 0.25f;
            _routeAvailable = _navigationPath.TryGetRoute(
                _camera.transform.position,
                target,
                out _route);
        }

        if (!_routeAvailable)
        {
            HideWorldGuides();

            string stateLabel =
                _navigationPath.State == CameraNavigationPath.NavigationState.SceneSetupRequired ? "Space Setup needed" :
                _navigationPath.State == CameraNavigationPath.NavigationState.Failed ? "Map load failed" :
                _navigationPath.State == CameraNavigationPath.NavigationState.Ready ? "No mapped route" :
                "Preparing route";

            SetBar("\u00b7", "\u2014", stateLabel);
            if (_drawerStatus != null)
            {
                _drawerStatus.text =
                    _locateTarget.cameraId + " | " + _positions.Site + "\n" +
                    _navigationPath.Status;
            }
            return;
        }

        bool nearTarget = _route.totalDistance < LocateNearDistance;
        Vector3 guidePoint = nearTarget ? target : _route.nextWaypoint + Vector3.up * 0.02f;

        Vector3 relative =
            _camera.transform.InverseTransformDirection(
                guidePoint - _camera.transform.position);
        relative.y = 0f;

        float angle = Mathf.Atan2(relative.x, relative.z) * Mathf.Rad2Deg;

        string glyph =
            Mathf.Abs(angle) < 15 ? "\u2191" :
            Mathf.Abs(angle) > 150 ? "\u2193" :
            angle > 0 ? "\u2192" : "\u2190";

        SetBar(
            glyph,
            _route.totalDistance.ToString("0.0") + " m",
            nearTarget ? "Near target \u00b7 scan QR" : _locateTarget.cameraId + " \u00b7 " + _positions.Site);

        if (nearTarget)
        {
            ShowTargetTag(target);
        }
        else
        {
            ShowRouteArrow(guidePoint);
        }

        if (_drawerStatus != null)
        {
            _drawerStatus.text =
                _locateTarget.cameraId + " | " + _positions.Site + "\n" +
                (nearTarget
                    ? "Near target. Scan its QR to confirm identity."
                    : "Follow the floor arrow. The route avoids mapped walls.");
        }
    }

    private void SetBar(string glyph, string distance, string subtitle)
    {
        if (_locateArrowGlyph != null) _locateArrowGlyph.text = glyph;
        if (_locateDistance != null) _locateDistance.text = distance;
        if (_locateSubtitle != null) _locateSubtitle.text = subtitle;
    }

    private void ShowRouteArrow(Vector3 guidePoint)
    {
        if (_locateTargetTag != null && _locateTargetTag.activeSelf)
            _locateTargetTag.SetActive(false);

        if (_routeArrow == null) return;

        if (!_routeArrow.activeSelf)
            _routeArrow.SetActive(true);

        _routeArrow.transform.position = guidePoint;

        Vector3 flat = guidePoint - _camera.transform.position;
        flat.y = 0f;
        if (flat.sqrMagnitude > 0.0001f)
            _routeArrow.transform.rotation = Quaternion.LookRotation(flat.normalized, Vector3.up);
    }

    private void ShowTargetTag(Vector3 target)
    {
        if (_routeArrow != null && _routeArrow.activeSelf)
            _routeArrow.SetActive(false);

        if (_locateTargetTag == null) return;

        if (!_locateTargetTag.activeSelf)
            _locateTargetTag.SetActive(true);

        _locateTargetTag.transform.position = target + Vector3.up * 1.15f;
        _locateTargetTag.transform.rotation = _camera.transform.rotation;

        if (_locateTagLabel != null)
            _locateTagLabel.text = _locateTarget.cameraId;
    }

    private void HideWorldGuides()
    {
        if (_routeArrow != null && _routeArrow.activeSelf)
            _routeArrow.SetActive(false);
        if (_locateTargetTag != null && _locateTargetTag.activeSelf)
            _locateTargetTag.SetActive(false);
    }

    private void SetSpatialBoundaryless(bool suppressed)
    {
        // Keep Meta's official Boundary API suppression active for this
        // passthrough-first MR app so users can walk between mapped rooms.
        if (OVRManager.instance != null)
            OVRManager.instance.shouldBoundaryVisibilityBeSuppressed = suppressed;
    }

    private void OnDestroy()
    {
        SetSpatialBoundaryless(false);
        if (_spatialRoot) Destroy(_spatialRoot);
        DestroyLocateHud();
    }
}
