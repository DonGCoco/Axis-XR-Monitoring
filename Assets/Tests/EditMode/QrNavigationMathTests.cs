using NUnit.Framework;
using UnityEngine;

public class QrNavigationMathTests
{
    [Test]
    public void HorizontalDistance_IgnoresVerticalDifference()
    {
        Vector3 a = new Vector3(1f, 0f, 1f);
        Vector3 b = new Vector3(4f, 3f, 5f);

        float distance = QrNavigationMath.HorizontalDistance(a, b);

        Assert.That(distance, Is.EqualTo(5f).Within(0.001f));
    }

    [Test]
    public void BuildDirectionPriority_StartsWithForwardThenBack()
    {
        var dirs = QrNavigationMath.BuildDirectionPriority(
            Vector3.forward,
            radialSamples: 4,
            sampleStepDegrees: 30f);

        Assert.That(dirs.Count, Is.GreaterThanOrEqualTo(4));
        Assert.That(Vector3.Dot(dirs[0], Vector3.forward), Is.GreaterThan(0.99f));
        Assert.That(Vector3.Dot(dirs[1], Vector3.back), Is.GreaterThan(0.99f));
    }
}
