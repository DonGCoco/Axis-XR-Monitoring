using System.Collections.Generic;
using UnityEngine;

public static class QrNavigationMath
{
    public static float HorizontalDistance(Vector3 a, Vector3 b)
    {
        Vector3 delta = a - b;
        delta.y = 0f;
        return delta.magnitude;
    }

    public static Vector3 FlattenDirection(Vector3 direction)
    {
        direction.y = 0f;
        if (direction.sqrMagnitude <= 0.0001f)
            return Vector3.forward;

        return direction.normalized;
    }

    public static List<Vector3> BuildDirectionPriority(
        Vector3 qrForward,
        int radialSamples,
        float sampleStepDegrees)
    {
        Vector3 forward = FlattenDirection(qrForward);
        Vector3 right = Quaternion.Euler(0f, 90f, 0f) * forward;

        List<Vector3> directions = new List<Vector3>
        {
            forward,
            -forward,
            right,
            -right
        };

        int half = Mathf.Max(0, radialSamples / 2);
        for (int i = 1; i <= half; i++)
        {
            float angle = i * sampleStepDegrees;
            directions.Add(Quaternion.Euler(0f, angle, 0f) * forward);
            directions.Add(Quaternion.Euler(0f, -angle, 0f) * forward);
        }

        return directions;
    }
}
