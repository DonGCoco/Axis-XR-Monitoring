using UnityEngine;

/// <summary>
/// Legacy compatibility component.
///
/// Hand-ray interaction is now owned by the Meta Interaction SDK rig in the
/// scene (HandRayInteractor + HandPointerPose + SDK ray visual). This class is
/// intentionally a no-op so an old serialized/runtime reference cannot start a
/// second custom ray system or disable the SDK rig.
/// </summary>
public class XRPointerInteractor : MonoBehaviour
{
    public void Initialize()
    {
        enabled = false;
    }
}
