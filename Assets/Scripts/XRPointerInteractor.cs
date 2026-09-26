using UnityEngine;

public class XRPointerInteractor : MonoBehaviour
{
    [SerializeField] private float maxDistance = 5f;

    private Transform _rayOrigin;
    private OVRHand _rightHand;
    private XRClickable _hovered;
    private LineRenderer _line;
    private bool _usingHandRay;
    private bool _wasPinching;

    public void Initialize()
    {
        ResolveInputSource();

        if (_line == null)
            CreateLine();
    }

    private void ResolveInputSource()
    {
        // Prefer the Meta Interaction SDK HandRayInteractor that is configured
        // under Hands/RightHand/HandInteractorsRight in the scene.
        GameObject[] allObjects = FindObjectsByType<GameObject>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);

        foreach (GameObject candidate in allObjects)
        {
            if (candidate.name != "HandRayInteractor")
                continue;

            Transform pointerPose = FindChildRecursive(
                candidate.transform,
                "PointerPose");

            if (pointerPose != null)
            {
                _rayOrigin = pointerPose;
                _usingHandRay = true;
                break;
            }
        }

        if (_usingHandRay)
        {
            OVRHand[] hands = FindObjectsByType<OVRHand>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);

            foreach (OVRHand hand in hands)
            {
                // The Core SDK 205 OVRHand API does not expose HandType
                // publicly. The right-hand Building Block lives under
                // RightHandAnchor, so use the hierarchy to identify it.
                if (hand.transform.IsChildOf(
                        FindFirstObjectByType<OVRCameraRig>()
                            .rightHandAnchor))
                {
                    _rightHand = hand;
                    break;
                }
            }

            return;
        }

        // Controller fallback keeps the existing desktop/device workflow usable.
        OVRCameraRig rig = FindFirstObjectByType<OVRCameraRig>();

        if (rig != null && rig.rightControllerAnchor != null)
            _rayOrigin = rig.rightControllerAnchor;

        if (_rayOrigin == null && Camera.main != null)
            _rayOrigin = Camera.main.transform;
    }

    private Transform FindChildRecursive(
        Transform root,
        string childName)
    {
        foreach (Transform child in root)
        {
            if (child.name == childName)
                return child;

            Transform result =
                FindChildRecursive(child, childName);

            if (result != null)
                return result;
        }

        return null;
    }

    private void CreateLine()
    {
        _line = gameObject.AddComponent<LineRenderer>();
        _line.positionCount = 2;
        _line.startWidth = 0.0025f;
        _line.endWidth = 0.0015f;
        _line.useWorldSpace = true;

        Shader shader = Shader.Find("Sprites/Default");
        if (shader != null)
        {
            Material material = new Material(shader);
            material.color =
                new Color(0.75f, 0.9f, 1f, 0.75f);
            _line.material = material;
        }

        _line.enabled = false;
    }

    private void Update()
    {
        if (_rayOrigin == null)
        {
            ResolveInputSource();

            if (_rayOrigin == null)
                return;
        }

        Ray ray =
            new Ray(_rayOrigin.position, _rayOrigin.forward);

        bool hitSomething =
            Physics.Raycast(
                ray,
                out RaycastHit hit,
                maxDistance);

        XRClickable nextHovered = null;

        if (hitSomething)
            nextHovered =
                hit.collider.GetComponent<XRClickable>();

        if (_hovered != nextHovered)
        {
            if (_hovered != null)
                _hovered.SetHovered(false);

            _hovered = nextHovered;

            if (_hovered != null)
                _hovered.SetHovered(true);
        }

        bool selectDown;

        if (_usingHandRay && _rightHand != null)
        {
            bool pinching =
                _rightHand.GetFingerIsPinching(
                    OVRHand.HandFinger.Index);

            selectDown = pinching && !_wasPinching;
            _wasPinching = pinching;
        }
        else
        {
            selectDown =
                OVRInput.GetDown(
                    OVRInput.Button.PrimaryIndexTrigger,
                    OVRInput.Controller.RTouch);
        }

        if (selectDown && _hovered != null)
            _hovered.InvokeClick();

        // Meta's HandRayInteractor renders its own ray. Keep the old line
        // only for the controller fallback.
        if (_line != null)
        {
            if (_usingHandRay)
            {
                _line.enabled = false;
            }
            else
            {
                bool triggerTouched =
                    OVRInput.Get(
                        OVRInput.Button.PrimaryIndexTrigger,
                        OVRInput.Controller.RTouch);

                _line.enabled =
                    _hovered != null || triggerTouched;

                if (_line.enabled)
                {
                    float distance =
                        hitSomething
                            ? hit.distance
                            : maxDistance;

                    _line.SetPosition(
                        0,
                        _rayOrigin.position);

                    _line.SetPosition(
                        1,
                        _rayOrigin.position
                        + _rayOrigin.forward * distance);
                }
            }
        }
    }
}
