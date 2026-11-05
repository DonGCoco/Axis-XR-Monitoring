using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

public partial class AxisMonitoringUI
{
    private CameraSpatialAnchors _positions;
    private CameraNavigationPath _navigationPath;
    private FloorNavigationArrows _floorArrows;
    private GameObject _locateDiagnostics;
    private Text _locateSummary;
    private GameObject _spatialRoot;
    private GameObject _positionMarker;
    private Text _positionMarkerLabel;
    private Text _spatialText;
    private Text _directionText;
    private Text _locationStatus;
    private string _detailsCameraId;
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
        if (_floorArrows) _floorArrows.Hide();
        if (_locateDiagnostics) Destroy(_locateDiagnostics);
        _locateDiagnostics = null;
        _locateSummary = null;
        _locateTarget = null;
        _registerTarget = null;
        _scanNotice = null;
        if (_spatialRoot) Destroy(_spatialRoot);
        if (_positionMarker) Destroy(_positionMarker);
        _spatialRoot = null;
        _positionMarker = null;
        _positionMarkerLabel = null;
        _spatialText = null;
        _directionText = null;
        _routeAvailable = false;
        _routeRefreshAt = 0f;
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

    private GameObject CreatePositionMarker()
    {
        // Reuse the app's UI shader instead of relying on a runtime-only shader that may be stripped.
        var marker = CreateCanvas("CameraPositionMarker", new Vector2(110f, 60f), null, Vector3.zero, AccentColor);
        _positionMarkerLabel = AddText(marker.transform, "NEXT", 18, FontStyle.Bold, TextPrimary,
            new Vector2(4f, -4f), new Vector2(-4f, -56f), TextAnchor.MiddleCenter);
        marker.SetActive(false);
        return marker;
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
        BuildLocateControls(camera);
    }

    private void BuildLocateControls(CameraData camera)
    {
        DestroyTransientUi();
        if (_hudRoot) _hudRoot.SetActive(false);
        if (!_floorArrows) _floorArrows = gameObject.AddComponent<FloorNavigationArrows>();
        _floorArrows.Hide();
        // Compact lower-left controls: central passthrough stays clear.
        _spatialRoot = CreateHeadLockedPanel("LocateControls", new Vector2(400f, 140f),
            new Vector3(-0.36f, -0.36f, 1.0f));
        _directionText = AddText(_spatialRoot.transform, "Preparing route", 18,
            FontStyle.Bold, AccentColor, new Vector2(14f,-8f), new Vector2(-14f,-43f));
        _locateSummary = AddText(_spatialRoot.transform, camera.cameraId, 15,
            FontStyle.Normal, TextSecondary, new Vector2(14f,-45f), new Vector2(-14f,-78f));
        CreateButton(_spatialRoot.transform, "Cancel", new Vector2(145f,44f),
            new Vector2(-92f,-88f), () => ShowDetails(camera), PanelSoftColor);
        CreateButton(_spatialRoot.transform, "Info", new Vector2(145f,44f),
            new Vector2(92f,-88f), () => ToggleLocateDiagnostics(), PanelSoftColor);
        _locateDiagnostics = CreateCanvas("LocateDiagnostics", new Vector2(400f, 260f),
            _spatialRoot.transform, Vector3.zero, PanelColor);
        // CreateCanvas scales UI from mm to metres; nested canvas inherits parent scale.
        _locateDiagnostics.transform.localScale = Vector3.one;
        _locateDiagnostics.transform.localPosition = new Vector3(0f,210f,0f);
        _spatialText = AddText(_locateDiagnostics.transform, "", 17, FontStyle.Normal,
            TextSecondary, new Vector2(14f,-12f), new Vector2(-14f,-184f));
        CreateButton(_locateDiagnostics.transform, "Reload map", new Vector2(170f,44f),
            new Vector2(0f,-202f), () =>
            {
                _routeAvailable = false;
                _routeRefreshAt = 0f;
                if (_floorArrows) _floorArrows.Hide();
                _navigationPath?.ReloadScene();
            }, AccentColor);
        _locateDiagnostics.SetActive(false);
    }

    private void ToggleLocateDiagnostics()
    {
        if (_locateDiagnostics) _locateDiagnostics.SetActive(!_locateDiagnostics.activeSelf);
    }

    // Called before API lookup. Wrong QR leaves guidance active and never opens its details.
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

        _scanNotice = "QR confirmed. Saving spatial anchor at the tracked QR position…";
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
        if (_locationStatus && _positions != null && _detailsCameraId != null)
        {
            string state = _positions.Busy ? "Restoring / saving" :
                _positions.TryGetPosition(_detailsCameraId, out _) ? "Position ready" :
                _positions.HasBinding(_detailsCameraId) ? "Position saved; tracking unavailable" : "Position not registered";
            _locationStatus.text = _positions.Site + " | " + state;
        }
        if (!_spatialRoot || _positions == null) return;
        if (_positionMarker && _camera) _positionMarker.transform.rotation = _camera.transform.rotation;
        if (_registerTarget != null)
        {
            _spatialText.text = _savingPosition
                ? _positions.Status
                : $"Site: {_positions.Site}\nScan the QR attached beside {_registerTarget.cameraId}.\n" +
                  "The Meta QR trackable pose will be saved as this camera's spatial anchor.";
        }
        else if (_locateTarget != null)
        {
            bool found = _positions.TryGetPosition(_locateTarget.cameraId, out var target);

            if (!found)
            {
                if (_floorArrows) _floorArrows.Hide();
                _routeAvailable = false;
                if (_positionMarker) _positionMarker.SetActive(false);
                _directionText.text = "Position unavailable";
                _spatialText.text = _positions.Busy ? _positions.Status :
                    (_positions.HasBinding(_locateTarget.cameraId) ? "Tracking / localization unavailable. Look around, or retry restore in Locations." :
                    "No position registered in " + _positions.Site + ". Register it first.");
            }
            else
            {
                if (_navigationPath == null)
                {
                    if (_floorArrows) _floorArrows.Hide();
                    _routeAvailable = false;
                    if (_positionMarker) _positionMarker.SetActive(false);
                    _directionText.text = "Navigation unavailable";
                    _spatialText.text = "MRUK SceneNavigation service is unavailable.";
                }
                else
                {
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
                        if (_floorArrows) _floorArrows.Hide();
                        if (_positionMarker) _positionMarker.SetActive(false);
                        _directionText.text =
                            _navigationPath.State == CameraNavigationPath.NavigationState.SceneSetupRequired
                                ? "Space Setup needed"
                                : _navigationPath.State == CameraNavigationPath.NavigationState.Failed
                                    ? "Map load failed"
                                    : _navigationPath.State == CameraNavigationPath.NavigationState.Ready
                                        ? "No mapped route"
                                        : "Preparing route";
                        _spatialText.text =
                            $"{_locateTarget.cameraId} | {_positions.Site}\n" +
                            _navigationPath.Status;
                    }
                    else
                    {
                        bool nearTarget = _route.totalDistance < 0.8f;
                        if (_floorArrows) _floorArrows.ShowRoute(_route.corners, nearTarget);
                        _directionText.text = nearTarget
                            ? "Near target · scan its QR"
                            : $"{_locateTarget.cameraId} · {_route.totalDistance:0.0} m";

                        _spatialText.text =
                            $"{_locateTarget.cameraId} | {_positions.Site}\n" +
                            "MRUK SceneNavigation route\n" +
                            (nearTarget
                                ? "Near target. Scan its QR to confirm identity."
                                : "Follow the floor arrows. Route uses the saved room map; check your surroundings.");
                    }
                }
            }
        }
        else _spatialText.text = $"Active site: {_positions.Site}\n{_positions.Status}\nSwitch to Lab and register again at school.";
        if (_scanNotice != null && Time.unscaledTime < _scanNoticeUntil) _spatialText.text += "\n" + _scanNotice;
        if (_locateTarget != null && _locateSummary)
        {
            _locateSummary.text = _scanNotice != null && Time.unscaledTime < _scanNoticeUntil
                ? _scanNotice : _routeAvailable ? "Follow floor arrows · Info for map options" : "Guidance paused · open Info";
        }
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
        if (_positionMarker) Destroy(_positionMarker);
        if (_floorArrows) Destroy(_floorArrows);
    }
}
