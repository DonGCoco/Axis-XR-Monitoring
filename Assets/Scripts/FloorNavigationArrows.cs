using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.UI;

/// <summary>World-space route markers. Uses the project's retained UI material,
/// no textures, editor-created prefabs, colliders, or runtime shader lookup.</summary>
public sealed class FloorNavigationArrows : MonoBehaviour
{
    [Min(0.1f)] public float ArrowLength = 0.5f;
    [Min(0.1f)] public float ArrowWidth = 0.3f;
    [Min(0.3f)] public float Spacing = 0.8f;
    [Min(1f)] public float VisibleDistance = 5f;
    [Min(0f)] public float StartDistance = 0.7f;
    [Min(0.005f)] public float FloorOffset = 0.025f;
    public Color ArrowColor = new Color(0.12f, 0.6f, 1f, 0.85f);
    private readonly List<GameObject> _pool = new List<GameObject>();
    private Mesh _arrowMesh;
    private Mesh _ringMesh;
    private GameObject _destination;
    private Vector3[] _lastCorners;
    private bool _lastNearTarget;

    private void Awake()
    {
        // Arrow points along +Z on its local XZ ground plane.
        _arrowMesh = MakeMesh(new[] {
            new Vector3(-0.20f,0,-0.5f), new Vector3(0.20f,0,-0.5f),
            new Vector3(0.20f,0,0.02f), new Vector3(0.50f,0,0.02f),
            new Vector3(0,0,0.5f), new Vector3(-0.50f,0,0.02f),
            new Vector3(-0.20f,0,0.02f) },
            new[] {0,6,2, 0,2,1, 5,4,3});
        var vertices = new List<Vector3>();
        var triangles = new List<int>();
        for (int i = 0; i < 32; i++)
        {
            float a = i * Mathf.PI * 2f / 32;
            vertices.Add(new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * 0.5f);
            vertices.Add(new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * 0.40f);
            int v = i * 2, next = ((i + 1) % 32) * 2;
            triangles.AddRange(new[] { v, next, v + 1, next, next + 1, v + 1 });
        }
        _ringMesh = MakeMesh(vertices.ToArray(), triangles.ToArray());
    }

    private static Mesh MakeMesh(Vector3[] vertices, int[] triangles)
    {
        var mesh = new Mesh { name = "AxisFloorNavigation", vertices = vertices, triangles = triangles };
        var colors = new Color[vertices.Length];
        for (int i = 0; i < colors.Length; i++) colors[i] = Color.white;
        mesh.colors = colors;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    private GameObject MakeShape(string label, Mesh mesh, Color color)
    {
        var go = new GameObject(label, typeof(MeshFilter), typeof(MeshRenderer));
        go.transform.SetParent(transform, false);
        go.GetComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = go.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = Graphic.defaultGraphicMaterial;
        // UI/Default uses _Color. A property block avoids material allocations.
        var props = new MaterialPropertyBlock();
        props.SetColor("_Color", color);
        renderer.SetPropertyBlock(props);
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        return go;
    }

    private GameObject GetArrow(int index)
    {
        while (_pool.Count <= index)
        {
            var outline = MakeShape("FloorArrow", _arrowMesh, new Color(1,1,1,0.9f));
            var fill = MakeShape("Fill", _arrowMesh, ArrowColor);
            fill.transform.SetParent(outline.transform, false);
            fill.transform.localPosition = Vector3.up * 0.002f;
            fill.transform.localScale = new Vector3(0.78f, 1, 0.85f);
            _pool.Add(outline);
        }
        return _pool[index];
    }

    public void Hide()
    {
        _lastCorners = null;
        foreach (var arrow in _pool) arrow.SetActive(false);
        if (_destination) _destination.SetActive(false);
    }

    public void ShowRoute(Vector3[] corners, bool nearTarget)
    {
        if (ReferenceEquals(_lastCorners, corners) && _lastNearTarget == nearTarget) return;
        Hide();
        if (corners == null || corners.Length < 2) return;
        _lastCorners = corners;
        _lastNearTarget = nearTarget;
        float total = 0;
        for (int i = 1; i < corners.Length; i++) total += Vector3.Distance(corners[i - 1], corners[i]);
        int used = 0;
        // Anchor spacing to the destination so markers don't slide forward with each head movement.
        float spacing = Mathf.Max(0.3f, Spacing);
        float first = total % spacing;
        while (first < StartDistance) first += spacing;
        float max = Mathf.Min(total - 0.3f, VisibleDistance);
        for (float distance = first; !nearTarget && distance <= max && used < 32; distance += spacing)
        {
            float remaining = distance;
            for (int i = 1; i < corners.Length; i++)
            {
                Vector3 segment = corners[i] - corners[i - 1];
                float length = segment.magnitude;
                if (remaining > length) { remaining -= length; continue; }
                if (length < 0.001f) break;
                var point = corners[i - 1] + segment * (remaining / length);
                // Validate a tight local sample, never jump to a different floor or across a wall.
                if (!NavMesh.SamplePosition(point, out NavMeshHit hit, 0.12f, NavMesh.AllAreas)) break;
                Vector3 direction = segment; direction.y = 0;
                if (direction.sqrMagnitude < 0.001f) break;
                Quaternion rotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
                // Skip arrows whose footprint would leave the mapped walkable surface.
                bool fits = true;
                foreach (var vertex in _arrowMesh.vertices)
                {
                    var edge = hit.position + rotation * new Vector3(vertex.x * ArrowWidth, 0f, vertex.z * ArrowLength);
                    if (NavMesh.Raycast(hit.position, edge, out NavMeshHit blocked, NavMesh.AllAreas))
                    { fits = false; break; }
                }
                if (!fits) break;
                var arrow = GetArrow(used++);
                arrow.transform.position = hit.position + Vector3.up * FloorOffset;
                arrow.transform.rotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
                arrow.transform.localScale = new Vector3(ArrowWidth, 1, ArrowLength);
                arrow.SetActive(true);
                break;
            }
        }
        if (total <= VisibleDistance)
        {
            var end = corners[corners.Length - 1];
            if (NavMesh.SamplePosition(end, out NavMeshHit hit, 0.12f, NavMesh.AllAreas))
            {
                if (!_destination) _destination = MakeShape("CameraDestination", _ringMesh, ArrowColor);
                _destination.transform.position = hit.position + Vector3.up * FloorOffset;
                _destination.SetActive(true);
            }
        }
    }

    private void OnDestroy()
    {
        if (_arrowMesh) Destroy(_arrowMesh);
        if (_ringMesh) Destroy(_ringMesh);
        foreach (var arrow in _pool) if (arrow) Destroy(arrow);
        if (_destination) Destroy(_destination);
    }
}
