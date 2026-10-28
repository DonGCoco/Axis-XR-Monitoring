using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class QrRouteRenderer : MonoBehaviour
{
    [Header("Line Style")]
    [Tooltip("Optional line material. If empty, a lightweight default is created at runtime.")]
    [SerializeField] private Material routeMaterial;

    [Tooltip("Route color in passthrough mode.")]
    [SerializeField] private Color routeColor = new Color(0.08f, 0.8f, 1f, 0.95f);

    [Tooltip("Route width in world meters.")]
    [SerializeField] private float routeWidth = 0.035f;

    [Tooltip("Vertical offset to avoid z-fighting with the floor.")]
    [SerializeField] private float floorOffset = 0.03f;

    private LineRenderer _line;
    private Material _runtimeMaterial;

    private void Awake()
    {
        CreateLineIfNeeded();
        Hide();
    }

    private void CreateLineIfNeeded()
    {
        if (_line != null)
            return;

        GameObject lineObject = new GameObject("QrRouteLine");
        lineObject.transform.SetParent(transform, false);
        _line = lineObject.AddComponent<LineRenderer>();
        _line.useWorldSpace = true;
        _line.alignment = LineAlignment.View;
        _line.numCornerVertices = 2;
        _line.numCapVertices = 2;
        _line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _line.receiveShadows = false;
        _line.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        _line.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        _line.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
        _line.enabled = false;

        if (routeMaterial != null)
        {
            _line.material = routeMaterial;
        }
        else
        {
            Shader shader = Shader.Find("Sprites/Default");
            _runtimeMaterial = shader != null ? new Material(shader) : null;
            if (_runtimeMaterial != null)
                _line.material = _runtimeMaterial;
        }
    }

    public void RenderPath(IReadOnlyList<Vector3> corners)
    {
        if (corners == null || corners.Count < 2)
        {
            Hide();
            return;
        }

        CreateLineIfNeeded();

        _line.startColor = routeColor;
        _line.endColor = routeColor;
        _line.startWidth = routeWidth;
        _line.endWidth = routeWidth;
        _line.positionCount = corners.Count;

        for (int i = 0; i < corners.Count; i++)
        {
            Vector3 point = corners[i];
            point.y += floorOffset;
            _line.SetPosition(i, point);
        }

        _line.enabled = true;
    }

    public void Hide()
    {
        if (_line == null)
            return;

        _line.positionCount = 0;
        _line.enabled = false;
    }

    private void OnDestroy()
    {
        if (_runtimeMaterial != null)
            Destroy(_runtimeMaterial);
    }
}
