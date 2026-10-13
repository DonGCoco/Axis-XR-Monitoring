using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

public partial class AxisMonitoringUI
{
    private CameraSpatialAnchors _positions;
    private GameObject _spatialRoot;
    private GameObject _positionMarker;
    private Text _spatialText;
    private Text _directionText;
    private Text _locationStatus;
    private string _detailsCameraId;
    private CameraData _locateTarget;
    private CameraData _registerTarget;
    private Coroutine _placementCountdown;
    private OVRCameraRig _spatialRig;
    private bool _savingPosition;
    private string _scanNotice;
    private float _scanNoticeUntil;
    public int SpatialWorkflowVersion { get; private set; }

    private async void InitializeSpatialWorkflow()
    {
        _positions = gameObject.AddComponent<CameraSpatialAnchors>();
        _spatialRig = FindFirstObjectByType<OVRCameraRig>();
        string site = PlayerPrefs.GetString("AXIS_ANCHOR_SITE", "Home");
        await _positions.SelectSiteAsync(site == "Lab" ? "Lab" : "Home");
    }

    private void EndSpatialWorkflow()
    {
        if (_savingPosition) return;
        SpatialWorkflowVersion++;
        if (_placementCountdown != null) StopCoroutine(_placementCountdown);
        _placementCountdown = null;
        _locateTarget = null;
        _registerTarget = null;
        _scanNotice = null;
        if (_spatialRoot) Destroy(_spatialRoot);
        if (_positionMarker) Destroy(_positionMarker);
        _spatialRoot = null;
        _positionMarker = null;
        _spatialText = null;
        _directionText = null;
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
        string id = _locateTarget != null ? _locateTarget.cameraId : _registerTarget.cameraId;
        AddText(marker.transform, id, 18, FontStyle.Bold, TextPrimary,
            new Vector2(4f, -4f), new Vector2(-4f, -56f), TextAnchor.MiddleCenter);
        marker.SetActive(false);
        return marker;
    }

    private void BeginLocate(CameraData camera)
    {
        if (_savingPosition || _positions == null) return;
        EndSpatialWorkflow();
        _locateTarget = camera;
        BuildSpatialPanel("Locate " + camera.cameraId);
        _directionText = AddText(_spatialRoot.transform, "", 30, FontStyle.Bold, AccentColor,
            new Vector2(24f, -245f), new Vector2(-24f, -290f), TextAnchor.MiddleCenter);
        _positionMarker = CreatePositionMarker();
        CreateButton(_spatialRoot.transform, "Cancel", new Vector2(160f, 52f), new Vector2(0f, -310f),
            () => ShowDetails(camera), PanelSoftColor);
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
        BuildSpatialPanel("Register " + camera.cameraId + " | " + _positions.Site);
        _positionMarker = CreatePositionMarker();
        CreateButton(_spatialRoot.transform, "Save in 3 seconds", new Vector2(240f, 52f), new Vector2(-130f, -310f),
            () => { if (_placementCountdown == null) _placementCountdown = StartCoroutine(SavePlacement(camera)); }, AccentColor);
        CreateButton(_spatialRoot.transform, "Cancel", new Vector2(150f, 52f), new Vector2(170f, -310f),
            () => ShowDetails(camera), PanelSoftColor);
    }
    private bool TryGetPlacementPose(out Pose pose)
    {
        pose = default;
        if (!_spatialRig || !_spatialRig.rightHandAnchor ||
            !OVRInput.GetControllerPositionTracked(OVRInput.Controller.RTouch) ||
            !OVRInput.GetControllerOrientationTracked(OVRInput.Controller.RTouch)) return false;
        var hand = _spatialRig.rightHandAnchor;
        pose = new Pose(hand.position, hand.rotation);
        return true;
    }
    private IEnumerator SavePlacement(CameraData camera)
    {
        for (int seconds = 3; seconds > 0; seconds--)
        {
            _scanNotice = "Saving in " + seconds + "… Hold the marker at the camera.";
            _scanNoticeUntil = Time.unscaledTime + 1.1f;
            yield return new WaitForSecondsRealtime(1f);
        }
        _placementCountdown = null;
        if (!TryGetPlacementPose(out var pose))
        {
            _scanNotice = "Right controller is not tracked. Try again.";
            _scanNoticeUntil = Time.unscaledTime + 4f;
            yield break;
        }
        SavePosition(camera, pose);
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
            bool tracked = TryGetPlacementPose(out var pose);
            if (_positionMarker) { _positionMarker.SetActive(tracked); if (tracked) _positionMarker.transform.position = pose.position; }
            _spatialText.text = _savingPosition ? _positions.Status :
                $"Site: {_positions.Site}\nPlace the right controller's blue marker at the camera.\nSelect Save, then hold it there for 3 seconds.\n" +
                (tracked ? "Controller tracked" : "Pick up the right Quest controller");
        }
        else if (_locateTarget != null)
        {
            bool found = _positions.TryGetPosition(_locateTarget.cameraId, out var target);
            if (_positionMarker) { _positionMarker.SetActive(found); if (found) _positionMarker.transform.position = target; }
            if (!found)
            {
                _directionText.text = "Position unavailable";
                _spatialText.text = _positions.Busy ? _positions.Status :
                    (_positions.HasBinding(_locateTarget.cameraId) ? "Tracking / localization unavailable. Look around, or retry restore in Locations." :
                    "No position registered in " + _positions.Site + ". Register it first.");
            }
            else
            {
                Vector3 relative = _camera.transform.InverseTransformDirection(target - _camera.transform.position);
                float angle = Mathf.Atan2(relative.x, relative.z) * Mathf.Rad2Deg;
                float distance = Vector3.Distance(target, _camera.transform.position);
                string direction = Mathf.Abs(angle) < 15 ? "↑ Ahead" : Mathf.Abs(angle) > 150 ? "↓ Behind you" : angle > 0 ? "→ Turn right" : "← Turn left";
                _directionText.text = $"{direction}  |  {distance:0.0} m";
                _spatialText.text = $"{_locateTarget.cameraId} | {_positions.Site}\nStraight-line guidance\n" +
                    (distance < 0.8f ? "Near target. " : "Follow the blue marker. ") + "Scan its QR to confirm identity.";
            }
        }
        else _spatialText.text = $"Active site: {_positions.Site}\n{_positions.Status}\nSwitch to Lab and register again at school.";
        if (_scanNotice != null && Time.unscaledTime < _scanNoticeUntil) _spatialText.text += "\n" + _scanNotice;
    }
    private void OnDestroy() { if (_spatialRoot) Destroy(_spatialRoot); if (_positionMarker) Destroy(_positionMarker); }
}
