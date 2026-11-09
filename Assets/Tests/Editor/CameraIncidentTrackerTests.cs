using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

// Existing Editor test asmdef cannot directly reference Assembly-CSharp.
public class CameraIncidentTrackerTests
{
    private Type trackerType;
    private Type cameraType;
    private object tracker;

    [SetUp] public void Setup()
    {
        trackerType = Type.GetType("CameraIncidentTracker, Assembly-CSharp", true);
        cameraType = Type.GetType("CameraData, Assembly-CSharp", true);
        tracker = Activator.CreateInstance(trackerType, new object[] { 2, 2 });
    }

    private object Camera(string status, string id = "CAM_001", bool expected = true, bool available = true)
    {
        object camera = Activator.CreateInstance(cameraType);
        Set(camera, "cameraId", id);
        Set(camera, "online", status != "OFFLINE");
        Set(camera, "status", status);
        Set(camera, "imageHealthExpected", expected);
        Set(camera, "imageHealthAvailable", available);
        return camera;
    }
    private void Set(object camera, string name, object value) => cameraType.GetField(name).SetValue(camera, value);
    private object Call(string name, params object[] args) => trackerType.GetMethod(name).Invoke(tracker, args);
    private bool Sample(object camera) => (bool)Call("Observe", camera);
    private void Presented(string id = "CAM_001") => Call("MarkPresented", id);

    [Test] public void PersistentIncidentOnlyPresentsOnce()
    {
        object bad = Camera("WARNING");
        Assert.That(Sample(bad), Is.False);
        Assert.That(Sample(bad), Is.True);
        Presented();
        Assert.That(Sample(Camera("OFFLINE")), Is.False);
        Assert.That(Sample(bad), Is.False);
    }

    [Test] public void RecoveryMustBeStableAndThenNewIncidentPresents()
    {
        object bad = Camera("WARNING"), good = Camera("HEALTHY");
        Sample(bad); Sample(bad); Presented();
        Sample(good); Sample(bad);
        Assert.That(Sample(bad), Is.False);
        Sample(good); Sample(good);
        Assert.That(Sample(bad), Is.False);
        Assert.That(Sample(bad), Is.True);
    }

    [Test] public void MissingEventStreamDoesNotRearmOrConfirmAnIncident()
    {
        object bad = Camera("WARNING");
        Sample(bad); Sample(bad); Presented();
        Sample(Camera("HEALTHY", available: false));
        Sample(Camera("HEALTHY", available: false));
        Assert.That(Sample(bad), Is.False);
        Assert.That(Sample(bad), Is.False);
    }

    [Test] public void TransportGapBreaksRecoveryStreak()
    {
        object bad = Camera("WARNING");
        Sample(bad); Sample(bad); Presented();
        Sample(Camera("HEALTHY")); Call("Gap", new object[] { null });
        Sample(Camera("HEALTHY"));
        Assert.That(Sample(bad), Is.False);
        Assert.That(Sample(bad), Is.False);
    }

    [Test] public void PerCameraIncidentsAndPersistedDeduplication()
    {
        object first = Camera("WARNING"), second = Camera("WARNING", "CAM_003");
        Sample(first); Sample(first); Presented();
        Sample(second); Assert.That(Sample(second), Is.True);
        string[] ids = (string[])Call("NotifiedIds");
        tracker = Activator.CreateInstance(trackerType, new object[] { 2, 2 });
        Call("RestoreNotified", new object[] { ids });
        Assert.That(Sample(first), Is.False);
        Assert.That(Sample(first), Is.False);
        Sample(second); Assert.That(Sample(second), Is.True);
    }

    [Test] public void RealProxyJsonParsesIssueReasonAndUnknownAvailability()
    {
        const string json = "{\"cameraId\":\"CAM_001\",\"online\":true,\"status\":\"WARNING\"," +
            "\"imageHealthExpected\":true,\"imageHealthAvailable\":true," +
            "\"issues\":[{\"type\":\"image_blocked\",\"message\":\"Image blocked\"}]}";
        object camera = JsonUtility.FromJson(json, cameraType);
        var issues = (Array)cameraType.GetField("issues").GetValue(camera);
        Assert.That(issues.Length, Is.EqualTo(1));
        Assert.That(issues.GetValue(0).GetType().GetField("type").GetValue(issues.GetValue(0)), Is.EqualTo("image_blocked"));
        Assert.That(trackerType.GetMethod("Classify").Invoke(null, new[] { camera }).ToString(), Is.EqualTo("Abnormal"));
    }
}
