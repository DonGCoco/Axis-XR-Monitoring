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
                $"OS / Serial|{osAndSerial}"
            },
            34f);
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
