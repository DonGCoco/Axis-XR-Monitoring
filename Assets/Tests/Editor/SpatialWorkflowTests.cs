using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

// Reflection allows the test assembly to exercise the existing Assembly-CSharp app
// without restructuring the project's production assemblies or SDK dependencies.
public class SpatialWorkflowTests
{
    private GameObject root;
    private Component ui;
    private Type uiType;
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

    [SetUp] public void SetUp()
    {
        uiType = Type.GetType("AxisMonitoringUI, Assembly-CSharp", true);
        root = new GameObject("SpatialWorkflowTest");
        ui = root.AddComponent(uiType);
    }
    [TearDown] public void TearDown() { UnityEngine.Object.DestroyImmediate(root); }
    private object Camera(string id)
    {
        var type = Type.GetType("CameraData, Assembly-CSharp", true);
        var camera = Activator.CreateInstance(type);
        type.GetField("cameraId").SetValue(camera, id);
        return camera;
    }
    private void Set(string field, object value) => uiType.GetField(field, PrivateInstance).SetValue(ui, value);
    private object Call(string method, params object[] args) => uiType.GetMethod(method).Invoke(ui, args);

    [Test] public void WrongQrCannotReplaceNavigationTarget()
    {
        var target = Camera("CAM_001");
        Set("_locateTarget", target);
        Assert.That(Call("AcceptCameraScan", "CAM_002"), Is.False);
        Assert.That(uiType.GetField("_locateTarget", PrivateInstance).GetValue(ui), Is.SameAs(target));
        Assert.That(Call("AcceptCameraScan", "CAM_001"), Is.True);
    }
    [Test] public void CancelInvalidatesPendingScanResponse()
    {
        Set("_locateTarget", Camera("CAM_001"));
        int version = (int)uiType.GetProperty("SpatialWorkflowVersion").GetValue(ui);
        Assert.That(Call("IsScanResponseCurrent", "CAM_001", version), Is.True);
        uiType.GetMethod("EndSpatialWorkflow", PrivateInstance).Invoke(ui, null);
        Assert.That(Call("IsScanResponseCurrent", "CAM_001", version), Is.False);
    }
    [Test] public void RegistrationAndSavingBlockQrNavigation()
    {
        Set("_registerTarget", Camera("CAM_001"));
        Assert.That(Call("AcceptCameraScan", "CAM_001"), Is.False);
        Set("_registerTarget", null);
        Set("_savingPosition", true);
        Assert.That(Call("AcceptCameraScan", "CAM_001"), Is.False);
    }
    [Test] public void ResponseForAnotherCameraIsRejectedDuringLocate()
    {
        Set("_locateTarget", Camera("CAM_001"));
        int version = (int)uiType.GetProperty("SpatialWorkflowVersion").GetValue(ui);
        Assert.That(Call("IsScanResponseCurrent", "CAM_002", version), Is.False);
    }
    [Test] public void UnregisteredCameraHasNoGuidancePosition()
    {
        var type = Type.GetType("CameraSpatialAnchors, Assembly-CSharp", true);
        var positions = root.AddComponent(type);
        object[] args = { "CAM_001", Vector3.zero };
        Assert.That(type.GetMethod("TryGetPosition").Invoke(positions, args), Is.False);
        Assert.That(type.GetMethod("HasBinding").Invoke(positions, new object[] { "CAM_001" }), Is.False);
    }
}
