using UnityEngine;

public partial class AxisMonitoringUI
{
    private void BuildVapixHealthyDetails(CameraData camera)
    {
        AddSectionLabel("LIVE VAPIX TELEMETRY", -128f);

        string mainCpu =
            $"{FormatAxisTemperature(camera.mainTemperatureAvailable, camera.mainTemperature)}" +
            $" · {FormatAxisTemperature(camera.cpuTemperatureAvailable, camera.cpuTemperature)}";

        string sensorOptics =
            $"{FormatAxisTemperature(camera.imageSensorTemperatureAvailable, camera.imageSensorTemperature)}" +
            $" · {FormatAxisTemperature(camera.opticsTemperatureAvailable, camera.opticsTemperature)}";

        string storage = "N/A";
        if (camera.storageAvailable)
        {
            storage =
                camera.emmcHealthAvailable
                    ? (camera.storageHealthy ? "Healthy" : "Problem")
                    : "Detected";

            if (camera.emmcWearAvailable)
                storage += $" · wear {camera.emmcWear}%";
        }

        string power = "N/A";
        string powerPeak = "N/A";

        if (camera.powerAvailable)
        {
            power =
                $"{camera.currentPower:0.00} W now · {camera.averagePower:0.00} W avg";

            powerPeak =
                $"{camera.maxPower:0.00} W max · {camera.powerRequested:0.0} W req · PoE {camera.poeClass}";
        }

        string osAndSerial =
            string.IsNullOrWhiteSpace(camera.serialNumber)
                ? camera.osVersion
                : $"{camera.osVersion} · {camera.serialNumber}";

        AddDetailRows(
            camera,
            -158f,
            new[]
            {
                $"Main / CPU|{mainCpu}",
                $"Sensor / Optics|{sensorOptics}",
                $"eMMC|{storage}",
                $"Power|{power}",
                $"Peak / PoE|{powerPeak}",
                $"Uptime|{FormatUptime(camera.uptime)}",
                $"OS / Serial|{osAndSerial}",
                $"Image health|{(camera.imageHealthExpected ? (camera.imageHealthAvailable ? "Normal" : "Unavailable") : "Not monitored")}"
            },
            34f);
    }

    private void BuildRealHealthDetails(CameraData camera)
    {
        AddSectionLabel("REPORTED ISSUES", -128f);
        GameObject viewport = CreateRect(_detailsRoot.transform, "HealthIssues",
            new UnityEngine.Vector2(620f, 210f), new UnityEngine.Vector2(0f, -162f), PanelSoftColor);
        int page = 0;
        int count = camera.issues == null ? 0 : camera.issues.Length;
        int pages = UnityEngine.Mathf.Max(1, (count + 1) / 2);
        System.Action render = null;
        render = () =>
        {
            for (int child = viewport.transform.childCount - 1; child >= 0; child--)
            {
                var old = viewport.transform.GetChild(child).gameObject;
                old.SetActive(false);
                Destroy(old);
            }
            float top = -6f;
            for (int i = page * 2; i < UnityEngine.Mathf.Min(count, page * 2 + 2); i++)
            {
                var issue = camera.issues[i];
                if (issue == null) continue;
                AddText(viewport.transform, issue.message, 20, UnityEngine.FontStyle.Bold, WarningColor,
                    new UnityEngine.Vector2(12f, top), new UnityEngine.Vector2(-12f, top - 35f));
                AddText(viewport.transform, issue.source + " · " + issue.evidence, 14,
                    UnityEngine.FontStyle.Normal, TextSecondary,
                    new UnityEngine.Vector2(12f, top - 36f), new UnityEngine.Vector2(-12f, top - 66f));
                top -= 72f;
            }
            if (pages > 1)
            {
                CreateButton(viewport.transform, "Previous", new UnityEngine.Vector2(120f, 42f),
                    new UnityEngine.Vector2(-210f, -163f), () => { page = (page + pages - 1) % pages; render(); }, PanelColor);
                CreateButton(viewport.transform, "Next", new UnityEngine.Vector2(120f, 42f),
                    new UnityEngine.Vector2(210f, -163f), () => { page = (page + 1) % pages; render(); }, PanelColor);
                AddText(viewport.transform, $"{page + 1}/{pages}", 16, UnityEngine.FontStyle.Bold, TextSecondary,
                    new UnityEngine.Vector2(280f, -173f), new UnityEngine.Vector2(-260f, -200f));
            }
        };
        render();
        AddText(_detailsRoot.transform,
            $"Temperature: {FormatAxisTemperature(camera.temperatureAvailable, camera.temperature)} · eMMC: " +
            (camera.emmcHealthAvailable ? (camera.storageHealthy ? "Healthy" : "Problem") : "N/A") +
            $"\nUpdated: {FormatServerTime(camera.serverTime)}",
            17, UnityEngine.FontStyle.Normal, TextSecondary,
            new UnityEngine.Vector2(30f, -382f), new UnityEngine.Vector2(-30f, -432f));
    }

    private static string FormatAxisTemperature(
        bool available,
        float value)
    {
        return available
            ? $"{value:0.0} C"
            : "N/A";
    }

}

