using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

public partial class AxisMonitoringUI
{
    [Serializable]
    private sealed class SavedHealthIncidents { public string[] ids; }

    private CameraIncidentTracker _healthIncidents;
    private Coroutine _healthPoll;
    private Coroutine _healthDisplay;
    private int _healthGeneration;
    private string _healthStateKey;
    private string _healthPersisted;
    private GameObject _healthToast;
    private string _healthToastId;
    private float _healthToastUntil;
    private readonly Dictionary<string, CameraData> _healthLatest =
        new Dictionary<string, CameraData>(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, CameraData> _healthPending =
        new Dictionary<string, CameraData>(StringComparer.OrdinalIgnoreCase);

    private void OnEnable()
    {
        if (_camera != null && _initialized) StartHealthAlerts();
    }

    private void OnDisable() { StopHealthAlerts(); }

    private void OnApplicationPause(bool paused)
    {
        if (paused) StopHealthAlerts();
        else if (_camera != null && isActiveAndEnabled) StartHealthAlerts();
    }

    private void StartHealthAlerts()
    {
        if (_healthPoll != null || _apiClient == null) return;
        string key = "AXIS_HEALTH_INCIDENTS_V1_" +
            Convert.ToBase64String(Encoding.UTF8.GetBytes(_apiClient.BaseUrl ?? ""));
        if (_healthIncidents == null || _healthStateKey != key)
        {
            _healthStateKey = key;
            _healthIncidents = new CameraIncidentTracker(3, 3);
            _healthPersisted = PlayerPrefs.GetString(key, "");
            if (!string.IsNullOrEmpty(_healthPersisted))
            {
                try
                {
                    var saved = JsonUtility.FromJson<SavedHealthIncidents>(_healthPersisted);
                    _healthIncidents.RestoreNotified(saved?.ids);
                }
                catch (Exception) { Debug.LogWarning("[AxisHealth] Could not read saved incident state."); }
            }
        }
        _healthGeneration++;
        _healthPoll = StartCoroutine(PollHealthAlerts(_healthGeneration));
        _healthDisplay = StartCoroutine(DisplayHealthAlerts());
    }

    private void StopHealthAlerts()
    {
        _healthGeneration++;
        if (_healthPoll != null) StopCoroutine(_healthPoll);
        if (_healthDisplay != null) StopCoroutine(_healthDisplay);
        _healthPoll = _healthDisplay = null;
        _healthIncidents?.Gap();
        _healthPending.Clear();
        _healthLatest.Clear();
        CloseHealthToast();
    }

    private IEnumerator PollHealthAlerts(int generation)
    {
        yield return new WaitForSecondsRealtime(2f);
        while (generation == _healthGeneration && _apiClient != null)
        {
            bool finished = false;
            float deadline = Time.realtimeSinceStartup + 75f;
            _apiClient.GetAllCameras(
                cameras =>
                {
                    if (generation != _healthGeneration || finished) return;
                    finished = true;
                    if (ValidHealthSnapshot(cameras)) ApplyHealthSnapshot(cameras);
                    else
                    {
                        _healthIncidents.Gap();
                        _healthLatest.Clear();
                        CloseHealthToast();
                    }
                },
                error =>
                {
                    if (generation != _healthGeneration || finished) return;
                    finished = true;
                    _healthIncidents.Gap();
                    _healthLatest.Clear();
                    CloseHealthToast();
                    Debug.LogWarning("[AxisHealth] Proxy unavailable; recovery not assumed.");
                });
            while (!finished && generation == _healthGeneration && Time.realtimeSinceStartup < deadline)
                yield return null;
            if (!finished)
            {
                finished = true; // ignore a late response to this sample
                _healthIncidents.Gap();
                _healthLatest.Clear();
                CloseHealthToast();
            }
            yield return new WaitForSecondsRealtime(15f);
        }
    }

    private static bool ValidHealthSnapshot(CameraData[] cameras)
    {
        if (cameras == null || cameras.Length == 0) return false;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (CameraData camera in cameras)
            if (camera == null || string.IsNullOrWhiteSpace(camera.cameraId) ||
                !seen.Add(camera.cameraId.Trim())) return false;
        return true;
    }

    private void ApplyHealthSnapshot(CameraData[] cameras)
    {
        bool changed = _healthLatest.Count != cameras.Length;
        var present = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (CameraData camera in cameras)
        {
            string id = camera.cameraId.Trim();
            present.Add(id);
            if (!_healthLatest.TryGetValue(id, out CameraData old) ||
                old.status != camera.status || HealthIssueText(old) != HealthIssueText(camera))
                changed = true;
        }
        foreach (string previous in _healthLatest.Keys)
            if (!present.Contains(previous))
            {
                _healthIncidents.Gap(previous);
                _healthPending.Remove(previous);
            }
        _healthLatest.Clear();
        foreach (CameraData camera in cameras)
        {
            string id = camera.cameraId.Trim();
            _healthLatest[id] = camera;
            if (_healthIncidents.Observe(camera)) _healthPending[id] = camera;
            if (CameraIncidentTracker.Classify(camera) == CameraIncidentTracker.Observation.Healthy)
                _healthPending.Remove(id);
        }
        SaveHealthIncidents();
        // Preserve navigation/registration. Rebuild only an already open overview,
        // and only when status/reason changed, rather than moving UI every poll.
        if (changed && _overviewRoot != null && !_savingPosition && _registerTarget == null)
        {
            _overviewCameras = (CameraData[])cameras.Clone();
            Array.Sort(_overviewCameras, CompareCameraPriority);
            RebuildCurrentOverview();
        }
        if (_healthToastId != null && (!_healthLatest.TryGetValue(_healthToastId, out CameraData shown) ||
            CameraIncidentTracker.Classify(shown) != CameraIncidentTracker.Observation.Abnormal))
            CloseHealthToast();
    }

    private void SaveHealthIncidents()
    {
        string value = JsonUtility.ToJson(new SavedHealthIncidents { ids = _healthIncidents.NotifiedIds() });
        if (value == _healthPersisted) return;
        PlayerPrefs.SetString(_healthStateKey, value);
        PlayerPrefs.Save();
        _healthPersisted = value;
    }

    private IEnumerator DisplayHealthAlerts()
    {
        while (true)
        {
            if (_healthToast != null && Time.realtimeSinceStartup >= _healthToastUntil)
                CloseHealthToast();
            if (_healthToast == null && _onboardingRoot == null && !_savingPosition && _registerTarget == null)
            {
                string next = null;
                foreach (string id in _healthPending.Keys)
                    if (_healthIncidents.CanPresent(id) && _healthLatest.ContainsKey(id))
                    { next = id; break; }
                if (next != null)
                {
                    _healthPending.Remove(next);
                    ShowHealthToast(_healthLatest[next]);
                    _healthIncidents.MarkPresented(next);
                    SaveHealthIncidents();
                }
            }
            yield return new WaitForSecondsRealtime(0.25f);
        }
    }

    private void ShowHealthToast(CameraData camera)
    {
        CloseHealthToast();
        _healthToastId = camera.cameraId.Trim();
        _healthToastUntil = Time.realtimeSinceStartup + 10f;
        _healthToast = CreateHeadLockedPanel("CameraHealthAlert", new Vector2(460f, 190f),
                                            new Vector3(0.29f, 0.19f, 1.05f));
        _healthToast.GetComponent<Canvas>().sortingOrder = 110;
        CreateRect(_healthToast.transform, "AlertAccent", new Vector2(460f, 4f), Vector2.zero,
                   camera.online ? WarningColor : OfflineColor);
        AddText(_healthToast.transform, "CAMERA ALERT · " + PlainHealthText(camera.cameraId), 18,
            FontStyle.Bold, WarningColor, new Vector2(18f, -14f), new Vector2(-18f, -40f));
        AddText(_healthToast.transform, PlainHealthText(camera.name), 19, FontStyle.Bold, TextPrimary,
            new Vector2(18f, -43f), new Vector2(-18f, -70f));
        AddText(_healthToast.transform, HealthIssueText(camera), 18, FontStyle.Normal, TextPrimary,
            new Vector2(18f, -74f), new Vector2(-18f, -125f));
        string id = _healthToastId;
        CreateButton(_healthToast.transform, "Inspect", new Vector2(180f, 44f), new Vector2(-110f, -135f),
            () =>
            {
                CloseHealthToast();
                if (_healthLatest.TryGetValue(id, out CameraData latest)) ShowDetails(latest);
            }, AccentColor);
        CreateButton(_healthToast.transform, "Dismiss", new Vector2(180f, 44f), new Vector2(110f, -135f),
                     CloseHealthToast, PanelSoftColor);
        Debug.Log("[AxisHealth] First incident shown: " + camera.cameraId + " | " + HealthIssueText(camera));
    }

    private void CloseHealthToast()
    {
        if (_healthToast != null) Destroy(_healthToast);
        _healthToast = null;
        _healthToastId = null;
    }

    private static string PlainHealthText(string value)
    {
        value = (value ?? "").Replace('<', ' ').Replace('>', ' ');
        return value.Length <= 220 ? value : value.Substring(0, 217) + "...";
    }

    private static string HealthIssueText(CameraData camera)
    {
        if (camera == null) return "Status unavailable";
        if (!camera.online || NormalizeStatus(camera.status) == "OFFLINE") return "Camera offline";
        var labels = new List<string>();
        if (camera.issues != null)
            foreach (CameraIssue issue in camera.issues)
            {
                if (issue == null) continue;
                string label;
                switch (issue.type)
                {
                    case "image_blocked": label = "Image blocked"; break;
                    case "image_blurred": label = "Image blurred"; break;
                    case "image_redirected": label = "View redirected"; break;
                    case "image_underexposed": label = "Image too dark"; break;
                    case "image_scene_unsuitable": label = "Scene unsuitable for analysis"; break;
                    default: label = PlainHealthText(issue.message ?? issue.type); break;
                }
                if (!string.IsNullOrWhiteSpace(label) && !labels.Contains(label)) labels.Add(label);
            }
        return labels.Count > 0 ? PlainHealthText(string.Join("; ", labels)) :
            (camera.imageHealthExpected && !camera.imageHealthAvailable ? "Image status unavailable" : "Attention required");
    }

    private void BuildUnknownHealthDetails(CameraData camera)
    {
        AddSectionLabel("STATUS UNAVAILABLE", -128f);
        AddText(_detailsRoot.transform,
            "Waiting for current health data.\nAn interrupted image event stream does not mean the image is healthy.",
            23, FontStyle.Normal, TextSecondary, new Vector2(30f, -180f), new Vector2(-30f, -310f));
    }
}
