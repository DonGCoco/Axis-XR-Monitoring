using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

// Meta SDK owns persistence/localization; only UUID bindings, never world poses, are stored.
public sealed class CameraSpatialAnchors : MonoBehaviour
{
    [Serializable] private class Binding { public string cameraId; public string uuid; }
    [Serializable] private class Registry { public List<Binding> items = new List<Binding>(); }
    private readonly Dictionary<string, OVRSpatialAnchor> anchors = new Dictionary<string, OVRSpatialAnchor>();
    private Registry registry = new Registry();
    public string Site { get; private set; }
    public bool Busy { get; private set; }
    public string Status { get; private set; } = "Positions not loaded";
    private string Key => "AXIS_CAMERA_ANCHORS_V1_" + Site;
    public bool HasBinding(string id) => registry.items.Exists(b => b.cameraId == id);
    public bool TryGetPosition(string id, out Vector3 position)
    {
        position = default;
        if (Busy || !anchors.TryGetValue(id, out var anchor) || !anchor || !anchor.Localized || !anchor.IsTracked) return false;
        position = anchor.transform.position;
        return true;
    }
    public async Task SelectSiteAsync(string site)
    {
        if (Busy) return;
        if (site != "Home" && site != "Lab") throw new ArgumentException("Unknown site");
        Busy = true;
        try
        {
            foreach (var anchor in anchors.Values) if (anchor) Destroy(anchor.gameObject);
            anchors.Clear();
            Site = site;
            PlayerPrefs.SetString("AXIS_ANCHOR_SITE", site);
            PlayerPrefs.Save();
            registry = new Registry();
            registry = JsonUtility.FromJson<Registry>(PlayerPrefs.GetString(Key, "{}")) ?? new Registry();
            if (registry.items == null) registry.items = new List<Binding>();
            await LoadAsync();
        }
        catch (Exception ex) { Debug.LogException(ex); Status = "Position load failed; retry"; }
        finally { Busy = false; }
    }
    public async Task ReloadAsync()
    {
        if (Busy) return;
        Busy = true;
        try { await LoadAsync(); }
        catch (Exception ex) { Debug.LogException(ex); Status = "Position load failed; retry"; }
        finally { Busy = false; }
    }
    private async Task LoadAsync()
    {
        Status = "Restoring positions. Look around the room.";
        var ids = new List<Guid>();
        foreach (var binding in registry.items)
        {
            if (anchors.TryGetValue(binding.cameraId, out var existing) && existing) continue;
            anchors.Remove(binding.cameraId);
            if (Guid.TryParse(binding.uuid, out var uuid)) ids.Add(uuid);
        }
        if (ids.Count > 0)
        {
            var unbound = new List<OVRSpatialAnchor.UnboundAnchor>();
            var result = await OVRSpatialAnchor.LoadUnboundAnchorsAsync(ids, unbound);
            if (!result.Success) { Status = "Position restore failed: " + result.Status; return; }
            foreach (var candidate in unbound)
            {
                if (!this) return;
                if (!candidate.Localized && !await candidate.LocalizeAsync(10)) continue;
                if (!this) return;
                if (!candidate.TryGetPose(out var pose)) continue;
                var binding = registry.items.Find(b => b.uuid == candidate.Uuid.ToString());
                if (binding == null || anchors.ContainsKey(binding.cameraId)) continue;
                var go = new GameObject("CameraAnchor_" + binding.cameraId);
                go.transform.SetPositionAndRotation(pose.position, pose.rotation);
                var anchor = go.AddComponent<OVRSpatialAnchor>();
                candidate.BindTo(anchor);
                anchors[binding.cameraId] = anchor;
            }
        }
        Status = $"{Site}: {anchors.Count}/{registry.items.Count} positions restored";
    }
    public async Task<bool> RegisterAsync(string id, Pose pose)
    {
        if (Busy || string.IsNullOrEmpty(Site) || string.IsNullOrWhiteSpace(id)) return false;
        Busy = true;
        OVRSpatialAnchor created = null;
        bool committed = false;
        try
        {
            Status = "Creating and saving " + id;
            var go = new GameObject("CameraAnchor_" + id);
            go.transform.SetPositionAndRotation(pose.position, pose.rotation);
            created = go.AddComponent<OVRSpatialAnchor>();
            float deadline = Time.realtimeSinceStartup + 15f;
            while (created && (!created.Created || !created.Localized) && Time.realtimeSinceStartup < deadline) await Task.Yield();
            if (!created || !created.Created || !created.Localized) { Status = "Creation failed; look around and retry"; return false; }
            var saved = await created.SaveAnchorAsync();
            if (!saved.Success) { Status = "Anchor save failed: " + saved.Status; return false; }
            if (!this) return false;
            var previous = registry.items.Find(b => b.cameraId == id);
            var replacement = new Binding { cameraId = id, uuid = created.Uuid.ToString() };
            if (previous != null) registry.items.Remove(previous);
            registry.items.Add(replacement);
            try { Persist(); }
            catch
            {
                registry.items.Remove(replacement);
                if (previous != null) registry.items.Add(previous);
                throw;
            }
            anchors.TryGetValue(id, out var old);
            anchors[id] = created;
            committed = true;
            Status = $"{id} position saved in {Site}";
            // Preserve the previous binding until the replacement is saved durably.
            if (previous != null && Guid.TryParse(previous.uuid, out var oldUuid))
            {
                var erased = await OVRSpatialAnchor.EraseAnchorsAsync(null, new[] { oldUuid });
                if (!erased.Success) Debug.LogWarning("Old anchor cleanup failed: " + erased.Status);
            }
            if (old) Destroy(old.gameObject);
            return true;
        }
        catch (Exception ex) { Debug.LogException(ex); Status = "Position save failed; retry"; return committed; }
        finally
        {
            if (!committed && created)
            {
                if (created.Created)
                {
                    try { await created.EraseAnchorAsync(); } catch (Exception ex) { Debug.LogException(ex); }
                }
                Destroy(created.gameObject);
            }
            Busy = false;
        }
    }
    public async Task ClearSiteAsync()
    {
        if (Busy || string.IsNullOrEmpty(Site)) return;
        Busy = true;
        try
        {
            var ids = new List<Guid>();
            foreach (var binding in registry.items) if (Guid.TryParse(binding.uuid, out var id)) ids.Add(id);
            if (ids.Count > 0)
            {
                var erased = await OVRSpatialAnchor.EraseAnchorsAsync(null, ids);
                if (!erased.Success) { Status = "Clear failed: " + erased.Status; return; }
            }
            registry.items.Clear();
            Persist();
            foreach (var anchor in anchors.Values) if (anchor) Destroy(anchor.gameObject);
            anchors.Clear();
            Status = Site + " positions cleared";
        }
        catch (Exception ex) { Debug.LogException(ex); Status = "Clear failed; retry"; }
        finally { Busy = false; }
    }
    private void Persist() { PlayerPrefs.SetString(Key, JsonUtility.ToJson(registry)); PlayerPrefs.Save(); }
    private void OnDestroy() { foreach (var anchor in anchors.Values) if (anchor) Destroy(anchor.gameObject); }
}
