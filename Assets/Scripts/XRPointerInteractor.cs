using System.Collections.Generic;
using UnityEngine;

public class XRPointerInteractor : MonoBehaviour
{
    [SerializeField] private float maxDistance = 5f;

    private OVRCameraRig _rig;
    private OVRHand _leftHand;
    private OVRHand _rightHand;

    private XRClickable _leftHovered;
    private XRClickable _rightHovered;
    private XRClickable _controllerHovered;

    private LineRenderer _leftLine;
    private LineRenderer _rightLine;
    private LineRenderer _controllerLine;

    private bool _leftWasPinching;
    private bool _rightWasPinching;
    private float _nextResolveTime;

    public void Initialize()
    {
        DisableComprehensiveRig();
        ResolveRigAndHands();
        RestoreBuildingBlockHandVisuals();

        if (_leftLine == null)
            _leftLine = CreateLine("LeftHandRay");

        if (_rightLine == null)
            _rightLine = CreateLine("RightHandRay");

        if (_controllerLine == null)
            _controllerLine = CreateLine("ControllerRay");
    }

    private void DisableComprehensiveRig()
    {
        GameObject[] allObjects = FindObjectsByType<GameObject>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);

        foreach (GameObject obj in allObjects)
        {
            if (obj.name == "OVRComprehensiveInteractionRig" ||
                obj.name == "OVRInteraction")
            {
                obj.SetActive(false);
            }
        }
    }

    private void ResolveRigAndHands()
    {
        _rig = FindFirstObjectByType<OVRCameraRig>();

        if (_rig == null)
            return;

        if (_rig.leftHandAnchor != null)
        {
            _leftHand =
                _rig.leftHandAnchor.GetComponentInChildren<OVRHand>(true);
        }

        if (_rig.rightHandAnchor != null)
        {
            _rightHand =
                _rig.rightHandAnchor.GetComponentInChildren<OVRHand>(true);
        }
    }

    private void RestoreBuildingBlockHandVisuals()
    {
        if (_rig == null)
            return;

        RestoreHandVisuals(_rig.leftHandAnchor);
        RestoreHandVisuals(_rig.rightHandAnchor);
    }

    private void RestoreHandVisuals(Transform handAnchor)
    {
        if (handAnchor == null)
            return;

        foreach (Transform child in
                 handAnchor.GetComponentsInChildren<Transform>(true))
        {
            if (child.name.StartsWith("[BuildingBlock] Hand Tracking"))
                child.gameObject.SetActive(true);
        }

        foreach (Behaviour behaviour in
                 handAnchor.GetComponentsInChildren<Behaviour>(true))
        {
            string typeName = behaviour.GetType().Name;

            if (typeName == "OVRHand" ||
                typeName == "OVRSkeleton" ||
                typeName == "OVRMesh" ||
                typeName == "OVRMeshRenderer" ||
                typeName == "OVRSkeletonRenderer")
            {
                behaviour.enabled = true;
            }
        }

        foreach (Renderer renderer in
                 handAnchor.GetComponentsInChildren<Renderer>(true))
        {
            renderer.enabled = true;
        }
    }

    private LineRenderer CreateLine(string name)
    {
        GameObject lineObject = new GameObject(name);
        lineObject.transform.SetParent(transform, false);

        LineRenderer line =
            lineObject.AddComponent<LineRenderer>();

        line.positionCount = 2;
        line.startWidth = 0.0035f;
        line.endWidth = 0.0015f;
        line.useWorldSpace = true;

        Shader shader = Shader.Find("Sprites/Default");

        if (shader != null)
        {
            Material material = new Material(shader);
            material.color =
                new Color(0.75f, 0.9f, 1f, 0.9f);
            line.material = material;
        }

        line.enabled = false;
        return line;
    }

    private void Update()
    {
        if (Time.unscaledTime >= _nextResolveTime)
        {
            _nextResolveTime = Time.unscaledTime + 0.5f;
            DisableComprehensiveRig();

            if (_rig == null ||
                _leftHand == null ||
                _rightHand == null)
            {
                ResolveRigAndHands();
            }

            RestoreBuildingBlockHandVisuals();
        }

        bool leftValid = IsHandRayValid(_leftHand);
        bool rightValid = IsHandRayValid(_rightHand);

        XRClickable newLeftHovered = null;
        XRClickable newRightHovered = null;
        XRClickable newControllerHovered = null;

        bool leftPinchDown = false;
        bool rightPinchDown = false;

        if (leftValid)
        {
            newLeftHovered =
                UpdateHandRay(
                    _leftHand,
                    _leftLine,
                    ref _leftWasPinching,
                    out leftPinchDown);
        }
        else
        {
            _leftWasPinching = false;

            if (_leftLine != null)
                _leftLine.enabled = false;
        }

        if (rightValid)
        {
            newRightHovered =
                UpdateHandRay(
                    _rightHand,
                    _rightLine,
                    ref _rightWasPinching,
                    out rightPinchDown);
        }
        else
        {
            _rightWasPinching = false;

            if (_rightLine != null)
                _rightLine.enabled = false;
        }

        if (!leftValid && !rightValid)
        {
            newControllerHovered =
                UpdateControllerFallback();
        }
        else if (_controllerLine != null)
        {
            _controllerLine.enabled = false;
        }

        UpdateHoverStates(
            newLeftHovered,
            newRightHovered,
            newControllerHovered);

        if (leftPinchDown && _leftHovered != null)
            _leftHovered.InvokeClick();

        if (rightPinchDown && _rightHovered != null)
        {
            if (!leftPinchDown ||
                _rightHovered != _leftHovered)
            {
                _rightHovered.InvokeClick();
            }
        }
    }

    private bool IsHandRayValid(OVRHand hand)
    {
        return hand != null &&
               hand.isActiveAndEnabled &&
               hand.IsTracked &&
               hand.IsPointerPoseValid &&
               hand.PointerPose != null;
    }

    private XRClickable UpdateHandRay(
        OVRHand hand,
        LineRenderer line,
        ref bool wasPinching,
        out bool pinchDown)
    {
        Transform pointerPose = hand.PointerPose;

        Ray ray =
            new Ray(
                pointerPose.position,
                pointerPose.forward);

        bool hitSomething =
            Physics.Raycast(
                ray,
                out RaycastHit hit,
                maxDistance);

        XRClickable hovered = null;

        if (hitSomething)
        {
            hovered =
                hit.collider.GetComponent<XRClickable>();
        }

        bool pinching =
            hand.GetFingerIsPinching(
                OVRHand.HandFinger.Index);

        pinchDown =
            pinching && !wasPinching;

        wasPinching = pinching;

        if (line != null)
        {
            line.enabled = true;

            float distance =
                hitSomething
                    ? hit.distance
                    : maxDistance;

            line.SetPosition(
                0,
                pointerPose.position);

            line.SetPosition(
                1,
                pointerPose.position
                + pointerPose.forward * distance);
        }

        return hovered;
    }

    private XRClickable UpdateControllerFallback()
    {
        if (_rig == null ||
            _rig.rightControllerAnchor == null)
        {
            if (_controllerLine != null)
                _controllerLine.enabled = false;

            return null;
        }

        Transform origin =
            _rig.rightControllerAnchor;

        Ray ray =
            new Ray(
                origin.position,
                origin.forward);

        bool hitSomething =
            Physics.Raycast(
                ray,
                out RaycastHit hit,
                maxDistance);

        XRClickable hovered = null;

        if (hitSomething)
        {
            hovered =
                hit.collider.GetComponent<XRClickable>();
        }

        bool triggerDown =
            OVRInput.GetDown(
                OVRInput.Button.PrimaryIndexTrigger,
                OVRInput.Controller.RTouch);

        if (triggerDown && hovered != null)
            hovered.InvokeClick();

        if (_controllerLine != null)
        {
            bool triggerTouched =
                OVRInput.Get(
                    OVRInput.Button.PrimaryIndexTrigger,
                    OVRInput.Controller.RTouch);

            _controllerLine.enabled =
                hovered != null || triggerTouched;

            if (_controllerLine.enabled)
            {
                float distance =
                    hitSomething
                        ? hit.distance
                        : maxDistance;

                _controllerLine.SetPosition(
                    0,
                    origin.position);

                _controllerLine.SetPosition(
                    1,
                    origin.position
                    + origin.forward * distance);
            }
        }

        return hovered;
    }

    private void UpdateHoverStates(
        XRClickable newLeft,
        XRClickable newRight,
        XRClickable newController)
    {
        HashSet<XRClickable> previous =
            new HashSet<XRClickable>();

        HashSet<XRClickable> current =
            new HashSet<XRClickable>();

        if (_leftHovered != null)
            previous.Add(_leftHovered);

        if (_rightHovered != null)
            previous.Add(_rightHovered);

        if (_controllerHovered != null)
            previous.Add(_controllerHovered);

        if (newLeft != null)
            current.Add(newLeft);

        if (newRight != null)
            current.Add(newRight);

        if (newController != null)
            current.Add(newController);

        foreach (XRClickable clickable in previous)
        {
            if (!current.Contains(clickable))
                clickable.SetHovered(false);
        }

        foreach (XRClickable clickable in current)
        {
            if (!previous.Contains(clickable))
                clickable.SetHovered(true);
        }

        _leftHovered = newLeft;
        _rightHovered = newRight;
        _controllerHovered = newController;
    }
}
