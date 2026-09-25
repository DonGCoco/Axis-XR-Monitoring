using UnityEngine;

public class XRPointerInteractor : MonoBehaviour
{
    [SerializeField] private float maxDistance = 4f;

    private Transform _rayOrigin;
    private XRClickable _hovered;
    private LineRenderer _line;

    public void Initialize()
    {
        OVRCameraRig rig = FindFirstObjectByType<OVRCameraRig>();

        if (rig != null && rig.rightControllerAnchor != null)
            _rayOrigin = rig.rightControllerAnchor;

        if (_rayOrigin == null && Camera.main != null)
            _rayOrigin = Camera.main.transform;

        CreateLine();
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
            material.color = new Color(0.75f, 0.9f, 1f, 0.75f);
            _line.material = material;
        }

        _line.enabled = false;
    }

    private void Update()
    {
        if (_rayOrigin == null)
        {
            Initialize();
            if (_rayOrigin == null)
                return;
        }

        Ray ray = new Ray(_rayOrigin.position, _rayOrigin.forward);
        bool hitSomething = Physics.Raycast(ray, out RaycastHit hit, maxDistance);

        XRClickable nextHovered = null;

        if (hitSomething)
            nextHovered = hit.collider.GetComponent<XRClickable>();

        if (_hovered != nextHovered)
        {
            if (_hovered != null)
                _hovered.SetHovered(false);

            _hovered = nextHovered;

            if (_hovered != null)
                _hovered.SetHovered(true);
        }

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

            _line.enabled = _hovered != null || triggerTouched;

            if (_line.enabled)
            {
                float distance = hitSomething ? hit.distance : maxDistance;
                _line.SetPosition(0, _rayOrigin.position);
                _line.SetPosition(
                    1,
                    _rayOrigin.position + _rayOrigin.forward * distance);
            }
        }
    }
}
