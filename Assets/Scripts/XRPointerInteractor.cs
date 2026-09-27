using System.Collections.Generic;
using UnityEngine;

public class XRPointerInteractor : MonoBehaviour
{
    [SerializeField] private float maxDistance = 5f;

    private OVRCameraRig _rig;
    private OVRHand _leftHand;
    private OVRHand _rightHand;
    private OVRSkeleton _leftSkeleton;
    private OVRSkeleton _rightSkeleton;
    private Transform _leftIndexDistal;
    private Transform _leftIndexTip;
    private Transform _rightIndexDistal;
    private Transform _rightIndexTip;

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

        ResolveIndexFingerBones(
            _leftHand,
            ref _leftSkeleton,
            ref _leftIndexDistal,
            ref _leftIndexTip);

        ResolveIndexFingerBones(
            _rightHand,
            ref _rightSkeleton,
            ref _rightIndexDistal,
            ref _rightIndexTip);
    }

    private void ResolveIndexFingerBones(
        OVRHand hand,
        ref OVRSkeleton skeleton,
        ref Transform indexDistal,
        ref Transform indexTip)
    {
        if (hand == null)
            return;

        if (skeleton == null)
        {
            skeleton = hand.GetComponent<OVRSkeleton>();

            if (skeleton == null)
                skeleton = hand.GetComponentInChildren<OVRSkeleton>(true);

            if (skeleton == null)
                skeleton = hand.GetComponentInParent<OVRSkeleton>();
        }

        if (skeleton == null || skeleton.Bones == null)
            return;

        // This project uses the OpenXR hand skeleton. Prefer the XR bone IDs,
        // then fall back to the legacy OVR hand IDs if needed.
        indexDistal =
            FindBoneTransform(
                skeleton,
                OVRSkeleton.BoneId.XRHand_IndexDistal);

        indexTip =
            FindBoneTransform(
                skeleton,
                OVRSkeleton.BoneId.XRHand_IndexTip);

        if (indexDistal == null)
        {
            indexDistal =
                FindBoneTransform(
                    skeleton,
                    OVRSkeleton.BoneId.Hand_Index3);
        }

        if (indexTip == null)
        {
            indexTip =
                FindBoneTransform(
                    skeleton,
                    OVRSkeleton.BoneId.Hand_IndexTip);
        }
    }

    private Transform FindBoneTransform(
        OVRSkeleton skeleton,
        OVRSkeleton.BoneId id)
    {
        if (skeleton == null || skeleton.Bones == null)
            return null;

        foreach (OVRBone bone in skeleton.Bones)
        {
            if (bone.Id == id)
                return bone.Transform;
        }

        return null;
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
            else
            {
                if (_leftIndexDistal == null ||
                    _leftIndexTip == null)
                {
                    ResolveIndexFingerBones(
                        _leftHand,
                        ref _leftSkeleton,
                        ref _leftIndexDistal,
                        ref _leftIndexTip);
                }

                if (_rightIndexDistal == null ||
                    _rightIndexTip == null)
                {
                    ResolveIndexFingerBones(
                        _rightHand,
                        ref _rightSkeleton,
                        ref _rightIndexDistal,
                        ref _rightIndexTip);
                }
            }

            RestoreBuildingBlockHandVisuals();
        }

        bool leftValid =
            IsHandRayValid(_leftHand);

        bool rightValid =
            IsHandRayValid(_rightHand);

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
                    _leftIndexTip,
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
                    _rightIndexTip,
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
        Transform indexTip,
        LineRenderer line,
        ref bool wasPinching,
        out bool pinchDown)
    {
        // Meta's system-defined pointer pose is the source of truth for aiming.
        // Do not derive the aim from individual finger bones: the system pose
        // already includes the filtering/aim model used for far-field UI.
        Transform pointerPose = hand.PointerPose;

        Ray systemRay =
            new Ray(
                pointerPose.position,
                pointerPose.forward);

        bool hitSomething =
            Physics.Raycast(
                systemRay,
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

            // Meta's system pointer pose lives near the wrist, while the
            // visible hand affordance is near the fingers. Keep hit-testing
            // on the system ray, but draw the line from the fingertip toward
            // the actual system target so the visual ray feels attached to
            // the hand instead of emerging from the wrist.
            Vector3 visualOrigin =
                indexTip != null
                    ? indexTip.position
                    : pointerPose.position;

            Vector3 target =
                hitSomething
                    ? hit.point
                    : pointerPose.position +
                      pointerPose.forward * maxDistance;

            line.SetPosition(0, visualOrigin);
            line.SetPosition(1, target);
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
