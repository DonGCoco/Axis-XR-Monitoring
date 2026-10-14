using System;

[Serializable]
public class CameraData
{
    // Existing application fields. Keep these for mock compatibility and
    // for the overview/status UI.
    public string cameraId;
    public string name;
    public bool online;
    public string model;
    public string osVersion;
    public int uptime;
    public bool temperatureAvailable;
    public float temperature;
    public bool storageHealthy;
    public string status;
    public string serverTime;

    // Real Axis/VAPIX telemetry.
    public bool vapixTelemetry;
    public string serialNumber;

    public bool systemReadyAvailable;
    public bool systemReady;

    public bool mainTemperatureAvailable;
    public float mainTemperature;

    public bool cpuTemperatureAvailable;
    public float cpuTemperature;

    public bool imageSensorTemperatureAvailable;
    public float imageSensorTemperature;

    public bool opticsTemperatureAvailable;
    public float opticsTemperature;

    public bool storageAvailable;
    public bool emmcHealthAvailable;
    public bool emmcWearAvailable;
    public int emmcWear;

    public bool powerAvailable;
    public float currentPower;
    public float averagePower;
    public float maxPower;
    public float powerRequested;
    public int poeClass;
}

[Serializable]
public class CameraDataList
{
    public CameraData[] items;
}

[Serializable]
public class CameraError
{
    public string error;
}
