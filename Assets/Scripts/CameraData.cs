using System;

[Serializable]
public class CameraData
{
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
