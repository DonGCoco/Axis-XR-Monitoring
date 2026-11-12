using System;
using System.Collections.Generic;

// No Unity dependencies: sampling and deduplication can be tested independently.
public sealed class CameraIncidentTracker
{
    public enum Observation { Unknown, Healthy, Abnormal }
    private sealed class Entry
    {
        public int bad;
        public int good;
        public bool notified;
        public Observation latest;
    }

    private readonly Dictionary<string, Entry> entries =
        new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
    private readonly int failureSamples;
    private readonly int recoverySamples;

    public CameraIncidentTracker(int failureSamples = 3, int recoverySamples = 3)
    {
        if (failureSamples < 1 || recoverySamples < 1)
            throw new ArgumentOutOfRangeException("Sample counts must be positive.");
        this.failureSamples = failureSamples;
        this.recoverySamples = recoverySamples;
    }

    public static Observation Classify(CameraData camera)
    {
        if (camera == null || string.IsNullOrWhiteSpace(camera.cameraId))
            return Observation.Unknown;
        string status = (camera.status ?? "").Trim().ToUpperInvariant();
        if (!camera.online || status == "OFFLINE")
            return Observation.Abnormal;
        if (status == "WARNING")
            return Observation.Abnormal;
        if (camera.issues != null)
            foreach (CameraHealthIssue issue in camera.issues)
                if (issue != null && (!string.IsNullOrWhiteSpace(issue.type) ||
                                      !string.IsNullOrWhiteSpace(issue.message)))
                    return Observation.Abnormal;
        if (camera.imageHealthExpected && !camera.imageHealthAvailable)
            return Observation.Unknown;
        return status == "HEALTHY" ? Observation.Healthy : Observation.Unknown;
    }

    public bool Observe(CameraData camera)
    {
        if (camera == null || string.IsNullOrWhiteSpace(camera.cameraId))
            return false;
        string id = camera.cameraId.Trim();
        if (!entries.TryGetValue(id, out Entry entry))
            entries[id] = entry = new Entry();
        entry.latest = Classify(camera);
        if (entry.latest == Observation.Unknown)
        {
            entry.bad = entry.good = 0;
            return false;
        }
        if (entry.latest == Observation.Healthy)
        {
            entry.bad = 0;
            entry.good = Math.Min(recoverySamples, entry.good + 1);
            if (entry.good >= recoverySamples)
                entry.notified = false;
            return false;
        }
        entry.good = 0;
        entry.bad = Math.Min(failureSamples, entry.bad + 1);
        return !entry.notified && entry.bad >= failureSamples;
    }

    public bool CanPresent(string id)
    {
        return entries.TryGetValue(id, out Entry entry) && !entry.notified &&
               entry.latest == Observation.Abnormal && entry.bad >= failureSamples;
    }

    public void MarkPresented(string id)
    {
        if (entries.TryGetValue(id, out Entry entry))
            entry.notified = true;
    }

    public void Gap(string id = null)
    {
        foreach (KeyValuePair<string, Entry> pair in entries)
        {
            if (id != null && !string.Equals(pair.Key, id, StringComparison.OrdinalIgnoreCase))
                continue;
            pair.Value.bad = pair.Value.good = 0;
            pair.Value.latest = Observation.Unknown;
        }
    }

    public string[] NotifiedIds()
    {
        var result = new List<string>();
        foreach (KeyValuePair<string, Entry> pair in entries)
            if (pair.Value.notified) result.Add(pair.Key);
        result.Sort(StringComparer.OrdinalIgnoreCase);
        return result.ToArray();
    }

    public void RestoreNotified(string[] ids)
    {
        if (ids == null) return;
        foreach (string id in ids)
            if (!string.IsNullOrWhiteSpace(id))
                entries[id.Trim()] = new Entry { notified = true };
    }
}
