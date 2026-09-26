using UnityEngine;

public class XRPointerInteractor : MonoBehaviour
{
    [SerializeField] private float maxDistance = 5f;

    private OVRCameraRig _rig;
    private OVRHand _rightHand;
    private XRClickable _hovered;
    private LineRenderer _line;
    private bool _wasPinching;
    private float _nextResolveTime;

    public void Initialize()
    {
        DisableComprehensiveRig();
        ResolveRigAndHands();
        RestoreBuildingBlockHandVisuals();

        if (_line == null)
            CreateLine();
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

        // The Comprehensive Rig wizard disables the old Hand Tracking
        // Building Block visuals to avoid duplicate hands. We intentionally
        // use the original Core SDK hand blocks for this project, so restore
        // those objects and their renderer/data components explicitly.
        foreach (Transform child in handAnchor.GetComponentsInChildren<Transform>(true))
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

    private void CreateLine()
    {
        _line = gameObject.AddComponent<LineRenderer>();
        _line.positionCount = 2;
        _line.startWidth = 0.0035f;
        _line.endWidth = 0.0015f;
        _line.useWorldSpace = true;

        Shader shader = Shader.Find("Sprites/Default");

        if (shader != null)
        {
            Material material = new Material(shader);
            material.color =
                new Color(0.75f, 0.9f, 1f, 0.9f);
            _line.material = material;
        }

        _line.enabled = false;
    }

    private void Update()
    {
        // Hands can become available a few frames after app startup.
        if (Time.unscaledTime >= _nextResolveTime)
        {
            _nextResolveTime = Time.unscaledTime + 0.5f;
            DisableComprehensiveRig();

            if (_rig == null || _rightHand == null)
                ResolveRigAndHands();

            RestoreBuildingBlockHandVisuals();
        }

        bool handRayValid =
            _rightHand != null &&
            _rightHand.isActiveAndEnabled &&
            _rightHand.IsTracked &&
            _rightHand.IsPointerPoseValid &&
            _rightHand.PointerPose != null;

        if (handRayValid)
        {
            UpdateHandRay();
            return;
        }

        UpdateControllerFallback();
    }

    private void UpdateHandRay()
    {
        Transform pointerPose = _rightHand.PointerPose;

        Ray ray =
            new Ray(
                pointerPose.position,
                pointerPose.forward);

        bool hitSomething =
            Physics.Raycast(
                ray,
                out RaycastHit hit,
                maxDistance);

        XRClickable nextHovered = null;

        if (hitSomething)
        {
            nextHovered =
                hit.collider.GetComponent<XRClickable>();
        }

        UpdateHover(nextHovered);

        bool pinching =
            _rightHand.GetFingerIsPinching(
                OVRHand.HandFinger.Index);

        bool pinchDown =
            pinching && !_wasPinching;

        _wasPinching = pinching;

        if (pinchDown && _hovered != null)
            _hovered.InvokeClick();

        if (_line != null)
        {
            _line.enabled = true;

            float distance =
                hitSomething
                    ? hit.distance
                    : maxDistance;

            _line.SetPosition(
                0,
                pointerPose.position);

            _line.SetPosition(
                1,
                pointerPose.position
                + pointerPose.forward * distance);
        }
    }

    private void UpdateControllerFallback()
    {
        _wasPinching = false;

        if (_rig == null ||
            _rig.rightControllerAnchor == null)
        {
            ClearHover();

            if (_line != null)
                _line.enabled = false;

            return;
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

        XRClickable nextHovered = null;

        if (hitSomething)
        {
            nextHovered =
                hit.collider.GetComponent<XRClickable>();
        }

        UpdateHover(nextHovered);

        bool triggerDown =
            OVRInput.GetDown(
                OVRInput.Button.PrimaryIndexTrigger,
                OVRInput.Controller.RTouch);

        if (triggerDown && _hovered != null)
            _hovered.InvokeClick();

        if (_line != null)
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
                    origin.position);

                _line.SetPosition(
                    1,
                    origin.position
                    + origin.forward * distance);
            }
        }
    }

    private void UpdateHover(XRClickable nextHovered)
    {
        if (_hovered == nextHovered)
            return;

        if (_hovered != null)
            _hovered.SetHovered(false);

        _hovered = nextHovered;

        if (_hovered != null)
            _hovered.SetHovered(true);
    }

    private void ClearHover()
    {
        if (_hovered != null)
        {
            _hovered.SetHovered(false);
            _hovered = null;
        }
    }
}
