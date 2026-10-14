using System;
using System.Collections;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.Networking;

public class CameraApiClient : MonoBehaviour
{
    [Header("Data Source")]
    [Tooltip("Use the real Axis camera directly through its official VAPIX APIs.")]
    [SerializeField] private bool useAxisVapix = false;

    [Tooltip("Keep this OFF for the normal online mock API. Ignored when Use Axis VAPIX is enabled.")]
    [SerializeField] private bool useEmbeddedMockData = false;

    [Tooltip("Online mock API base URL. Example: https://axis-xr-monitoring.onrender.com")]
    [SerializeField] private string baseUrl = "http://127.0.0.1:5000";

    [Header("Axis VAPIX")]
    [Tooltip("Base URL of the Axis device. The current lab camera uses a link-local address.")]
    [SerializeField] private string axisBaseUrl = "https://169.254.96.234";

    [Tooltip("QR/application ID mapped to this physical camera for the prototype.")]
    [SerializeField] private string axisCameraId = "CAM_001";

    [Tooltip("Optional friendly name. Leave empty to use the camera product name.")]
    [SerializeField] private string axisDisplayName = "";

    [SerializeField] private string axisUsername = "camera01";

    [Tooltip("Local prototype credential only. Do not commit a scene containing a real password.")]
    [SerializeField] private string axisPassword = "";

    [Tooltip("The lab camera currently uses its self-signed HTTPS certificate.")]
    [SerializeField] private bool allowSelfSignedCertificate = true;

    public string BaseUrl
    {
        get => baseUrl;
        set => baseUrl = value.TrimEnd('/');
    }

    public bool UseEmbeddedMockData
    {
        get => useEmbeddedMockData;
        set => useEmbeddedMockData = value;
    }

    public bool UseAxisVapix
    {
        get => useAxisVapix;
        set => useAxisVapix = value;
    }

    public void SetAxisCredentials(string username, string password)
    {
        axisUsername = username ?? "";
        axisPassword = password ?? "";
    }

    public void GetCamera(
        string cameraId,
        Action<CameraData> onSuccess,
        Action<string> onError = null)
    {
        if (string.IsNullOrWhiteSpace(cameraId))
        {
            onError?.Invoke("Camera ID is empty.");
            return;
        }

        string normalizedId = cameraId.Trim().ToUpperInvariant();

        if (useAxisVapix)
        {
            string configuredId =
                string.IsNullOrWhiteSpace(axisCameraId)
                    ? normalizedId
                    : axisCameraId.Trim().ToUpperInvariant();

            if (!string.Equals(
                    normalizedId,
                    configuredId,
                    StringComparison.OrdinalIgnoreCase))
            {
                onError?.Invoke(
                    $"No Axis device is configured for {normalizedId}. " +
                    $"This prototype currently maps {configuredId} to one physical camera.");
                return;
            }

            StartCoroutine(
                GetAxisCameraCoroutine(
                    configuredId,
                    onSuccess,
                    onError));
            return;
        }

        if (useEmbeddedMockData)
        {
            CameraData mockCamera = GetEmbeddedMockCamera(normalizedId);

            if (mockCamera == null)
            {
                string message = $"Embedded mock camera not found: {normalizedId}";
                Debug.LogError(message);
                onError?.Invoke(message);
                return;
            }

            mockCamera.serverTime = DateTime.UtcNow.ToString("o");
            Debug.Log($"USING EMBEDDED MOCK DATA | {normalizedId}");
            onSuccess?.Invoke(mockCamera);
            return;
        }

        StartCoroutine(GetCameraCoroutine(normalizedId, onSuccess, onError));
    }

    public void GetAllCameras(
        Action<CameraData[]> onSuccess,
        Action<string> onError = null)
    {
        if (useAxisVapix)
        {
            string configuredId =
                string.IsNullOrWhiteSpace(axisCameraId)
                    ? "CAM_001"
                    : axisCameraId.Trim().ToUpperInvariant();

            StartCoroutine(
                GetAxisCameraCoroutine(
                    configuredId,
                    camera => onSuccess?.Invoke(new[] { camera }),
                    onError));
            return;
        }

        if (useEmbeddedMockData)
        {
            CameraData[] cameras =
            {
                GetEmbeddedMockCamera("CAM_001"),
                GetEmbeddedMockCamera("CAM_002"),
                GetEmbeddedMockCamera("CAM_003")
            };

            string now = DateTime.UtcNow.ToString("o");
            foreach (CameraData camera in cameras)
            {
                if (camera != null)
                    camera.serverTime = now;
            }

            onSuccess?.Invoke(cameras);
            return;
        }

        StartCoroutine(GetAllCamerasCoroutine(onSuccess, onError));
    }

    private IEnumerator GetAxisCameraCoroutine(
        string cameraId,
        Action<CameraData> onSuccess,
        Action<string> onError)
    {
        if (string.IsNullOrWhiteSpace(axisBaseUrl))
        {
            onError?.Invoke("Axis VAPIX base URL is empty.");
            yield break;
        }

        if (string.IsNullOrWhiteSpace(axisUsername) ||
            string.IsNullOrWhiteSpace(axisPassword))
        {
            onError?.Invoke(
                "Axis VAPIX credentials are not configured. " +
                "Set the camera username and password locally in CameraApiClient.");
            yield break;
        }

        CameraData camera = new CameraData
        {
            cameraId = cameraId,
            name = string.IsNullOrWhiteSpace(axisDisplayName)
                ? cameraId
                : axisDisplayName.Trim(),
            online = false,
            uptime = -1,
            storageHealthy = true,
            status = "OFFLINE",
            serverTime = DateTime.UtcNow.ToString("o"),
            vapixTelemetry = true
        };

        // Basic Device Information is the required request. If this fails,
        // the camera is not considered reachable/authenticated.
        string basicJson = null;
        string basicError = null;

        yield return SendAxisJsonPost(
            "/axis-cgi/basicdeviceinfo.cgi",
            "{\"apiVersion\":\"1.3\",\"method\":\"getAllProperties\"}",
            result => basicJson = result,
            error => basicError = error);

        if (string.IsNullOrWhiteSpace(basicJson))
        {
            string message =
                "Axis Basic Device Information request failed: " +
                (basicError ?? "Unknown error.");
            Debug.LogError(message);
            onError?.Invoke(message);
            yield break;
        }

        try
        {
            BasicDeviceInfoResponse response =
                JsonUtility.FromJson<BasicDeviceInfoResponse>(basicJson);

            if (response?.data?.propertyList == null)
                throw new Exception("propertyList is missing.");

            BasicDeviceProperties p = response.data.propertyList;

            camera.online = true;
            camera.model =
                FirstNonEmpty(p.ProdFullName, p.ProdShortName, p.ProdNbr);
            camera.osVersion = p.Version;
            camera.serialNumber = p.SerialNumber;

            if (string.IsNullOrWhiteSpace(axisDisplayName))
            {
                camera.name =
                    FirstNonEmpty(p.ProdShortName, p.ProdNbr, cameraId);
            }
        }
        catch (Exception exception)
        {
            string message =
                $"Could not parse Axis Basic Device Information: {exception.Message}";
            Debug.LogError(message);
            onError?.Invoke(message);
            yield break;
        }

        // Temperature Control API.
        string temperatureText = null;
        yield return SendAxisGet(
            "/axis-cgi/temperaturecontrol.cgi?action=statusall",
            result => temperatureText = result,
            error => Debug.LogWarning(
                $"Axis temperature telemetry unavailable: {error}"));

        if (!string.IsNullOrWhiteSpace(temperatureText))
            ParseTemperatureStatus(temperatureText, camera);

        // Systemready API supplies uptime. systemready itself is kept as
        // telemetry but is not used as an application warning condition.
        string systemJson = null;
        yield return SendAxisJsonPost(
            "/axis-cgi/systemready.cgi",
            "{\"apiVersion\":\"1.4\",\"method\":\"systemready\",\"params\":{\"timeout\":0}}",
            result => systemJson = result,
            error => Debug.LogWarning(
                $"Axis system telemetry unavailable: {error}"));

        if (!string.IsNullOrWhiteSpace(systemJson))
        {
            try
            {
                SystemReadyResponse response =
                    JsonUtility.FromJson<SystemReadyResponse>(systemJson);

                if (response?.data != null)
                {
                    camera.uptime = response.data.uptime;
                    camera.systemReadyAvailable = true;
                    camera.systemReady =
                        string.Equals(
                            response.data.systemready,
                            "yes",
                            StringComparison.OrdinalIgnoreCase);
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    $"Could not parse Axis systemready response: {exception.Message}");
            }
        }

        // Edge Storage health API. For the current Q1728 this reports the
        // internal EMMC plus absent SD/network storage entries.
        string storageXml = null;
        yield return SendAxisGet(
            "/axis-cgi/disks/gethealth.cgi",
            result => storageXml = result,
            error => Debug.LogWarning(
                $"Axis storage health unavailable: {error}"));

        if (!string.IsNullOrWhiteSpace(storageXml))
            ParseStorageHealth(storageXml, camera);

        // Power Settings API. API Discovery on the Q1728 reports support for
        // power status and the device responds with API version 1.7.
        string powerJson = null;
        yield return SendAxisJsonPost(
            "/axis-cgi/power-settings.cgi",
            "{\"apiVersion\":\"1.7\",\"method\":\"getPowerStatus\"}",
            result => powerJson = result,
            error => Debug.LogWarning(
                $"Axis power telemetry unavailable: {error}"));

        if (!string.IsNullOrWhiteSpace(powerJson))
        {
            try
            {
                PowerStatusResponse response =
                    JsonUtility.FromJson<PowerStatusResponse>(powerJson);

                if (response?.data?.usage != null)
                {
                    camera.powerAvailable = true;
                    camera.currentPower = response.data.usage.currentPower;
                    camera.averagePower = response.data.usage.averagePower;
                    camera.maxPower = response.data.usage.maxPower;
                    camera.powerRequested = response.data.powerRequested;

                    camera.poeClass =
                        response.data.psePoeClass >= 0
                            ? response.data.psePoeClass
                            : response.data.lldpPoeClass;
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    $"Could not parse Axis power response: {exception.Message}");
            }
        }

        camera.status = DeriveStatus(camera);
        camera.serverTime = DateTime.UtcNow.ToString("o");

        Debug.Log(
            $"AXIS VAPIX OK | ID={camera.cameraId} | " +
            $"Model={camera.model} | OS={camera.osVersion} | " +
            $"Status={camera.status}");

        onSuccess?.Invoke(camera);
    }

    private IEnumerator SendAxisGet(
        string path,
        Action<string> onSuccess,
        Action<string> onError)
    {
        string url = BuildAxisUrl(path);

        using UnityWebRequest request = UnityWebRequest.Get(url);
        ConfigureAxisRequest(request);

        yield return request.SendWebRequest();

        HandleAxisResponse(request, url, onSuccess, onError);
    }

    private IEnumerator SendAxisJsonPost(
        string path,
        string jsonBody,
        Action<string> onSuccess,
        Action<string> onError)
    {
        string url = BuildAxisUrl(path);

        using UnityWebRequest request =
            new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST);

        request.uploadHandler =
            new UploadHandlerRaw(Encoding.UTF8.GetBytes(jsonBody));

        request.downloadHandler =
            new DownloadHandlerBuffer();

        request.SetRequestHeader(
            "Content-Type",
            "application/json");

        ConfigureAxisRequest(request);

        yield return request.SendWebRequest();

        HandleAxisResponse(request, url, onSuccess, onError);
    }

    private void ConfigureAxisRequest(UnityWebRequest request)
    {
        request.timeout = 8;

        string credentials =
            Convert.ToBase64String(
                Encoding.UTF8.GetBytes(
                    $"{axisUsername}:{axisPassword}"));

        request.SetRequestHeader(
            "Authorization",
            "Basic " + credentials);

        if (allowSelfSignedCertificate)
        {
            request.certificateHandler =
                new AxisSelfSignedCertificateHandler();

            request.disposeCertificateHandlerOnDispose = true;
        }
    }

    private static void HandleAxisResponse(
        UnityWebRequest request,
        string url,
        Action<string> onSuccess,
        Action<string> onError)
    {
        if (request.result != UnityWebRequest.Result.Success)
        {
            string message =
                $"Axis VAPIX request failed ({request.responseCode}): " +
                $"{request.error}\n{url}";

            onError?.Invoke(message);
            return;
        }

        onSuccess?.Invoke(request.downloadHandler.text);
    }

    private string BuildAxisUrl(string path)
    {
        string root = axisBaseUrl.Trim().TrimEnd('/');

        if (path.StartsWith("/"))
            return root + path;

        return root + "/" + path;
    }

    private static void ParseTemperatureStatus(
        string text,
        CameraData camera)
    {
        string[] lines =
            text.Split(
                new[] { '\r', '\n' },
                StringSplitOptions.RemoveEmptyEntries);

        float hottest = float.MinValue;
        bool anyTemperature = false;

        foreach (string rawLine in lines)
        {
            string line = rawLine.Trim();

            int equalsIndex = line.IndexOf('=');
            if (equalsIndex <= 0)
                continue;

            string key = line.Substring(0, equalsIndex);
            string value = line.Substring(equalsIndex + 1);

            if (!key.EndsWith(
                    ".Celsius",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!float.TryParse(
                    value,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out float celsius))
            {
                continue;
            }

            string sensorPrefix =
                key.Substring(
                    0,
                    key.Length - ".Celsius".Length);

            string sensorName =
                FindTemperatureSensorName(lines, sensorPrefix);

            switch (NormalizeSensorName(sensorName))
            {
                case "main":
                    camera.mainTemperatureAvailable = true;
                    camera.mainTemperature = celsius;
                    break;

                case "cpu":
                    camera.cpuTemperatureAvailable = true;
                    camera.cpuTemperature = celsius;
                    break;

                case "imagesensor":
                    camera.imageSensorTemperatureAvailable = true;
                    camera.imageSensorTemperature = celsius;
                    break;

                case "optics":
                    camera.opticsTemperatureAvailable = true;
                    camera.opticsTemperature = celsius;
                    break;
            }

            anyTemperature = true;
            hottest = Mathf.Max(hottest, celsius);
        }

        // Legacy field remains the hottest reported sensor so older panels
        // continue to display meaningful real telemetry.
        if (anyTemperature)
        {
            camera.temperatureAvailable = true;
            camera.temperature = hottest;
        }
    }

    private static string FindTemperatureSensorName(
        string[] lines,
        string sensorPrefix)
    {
        string target = sensorPrefix + ".Name=";

        foreach (string rawLine in lines)
        {
            string line = rawLine.Trim();

            if (line.StartsWith(
                    target,
                    StringComparison.OrdinalIgnoreCase))
            {
                return line.Substring(target.Length).Trim();
            }
        }

        return "";
    }

    private static string NormalizeSensorName(string sensorName)
    {
        if (string.IsNullOrWhiteSpace(sensorName))
            return "";

        return Regex.Replace(
                sensorName.Trim().ToLowerInvariant(),
                "[^a-z0-9]",
                "");
    }

    private static void ParseStorageHealth(
        string xml,
        CameraData camera)
    {
        MatchCollection entries =
            Regex.Matches(
                xml,
                @"<HealthStatus\b[^>]*>",
                RegexOptions.IgnoreCase);

        foreach (Match entry in entries)
        {
            string tag = entry.Value;

            Match diskIdMatch =
                Regex.Match(
                    tag,
                    "diskid=\"([^\"]+)\"",
                    RegexOptions.IgnoreCase);

            if (!diskIdMatch.Success ||
                !string.Equals(
                    diskIdMatch.Groups[1].Value,
                    "EMMC",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            camera.storageAvailable = true;

            Match wearMatch =
                Regex.Match(
                    tag,
                    "wear=\"(-?\\d+)\"",
                    RegexOptions.IgnoreCase);

            if (wearMatch.Success &&
                int.TryParse(
                    wearMatch.Groups[1].Value,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out int wear))
            {
                camera.emmcWear = wear;
            }

            Match healthMatch =
                Regex.Match(
                    tag,
                    "overallhealth=\"(-?\\d+)\"",
                    RegexOptions.IgnoreCase);

            if (healthMatch.Success &&
                int.TryParse(
                    healthMatch.Groups[1].Value,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out int health))
            {
                camera.emmcHealthAvailable = true;
                camera.storageHealthy = health == 1;
            }

            return;
        }
    }

    private static string DeriveStatus(CameraData camera)
    {
        if (camera == null || !camera.online)
            return "OFFLINE";

        if (camera.storageAvailable &&
            camera.emmcHealthAvailable &&
            !camera.storageHealthy)
        {
            return "WARNING";
        }

        // Do not invent temperature/power thresholds. Until Axis exposes a
        // device health condition for them, display the values as telemetry.
        return "HEALTHY";
    }

    private static string FirstNonEmpty(params string[] values)
    {
        foreach (string value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
                return value.Trim();
        }

        return "";
    }

    private static CameraData GetEmbeddedMockCamera(string cameraId)
    {
        switch (cameraId)
        {
            case "CAM_001":
                return new CameraData
                {
                    cameraId = "CAM_001",
                    name = "Entrance Camera",
                    online = true,
                    model = "AXIS Test Camera",
                    osVersion = "12.0",
                    uptime = 86400,
                    temperatureAvailable = true,
                    temperature = 43.2f,
                    storageHealthy = true,
                    status = "HEALTHY"
                };

            case "CAM_002":
                return new CameraData
                {
                    cameraId = "CAM_002",
                    name = "Hallway Camera",
                    online = true,
                    model = "AXIS Test Camera",
                    osVersion = "12.0",
                    uptime = 43200,
                    temperatureAvailable = true,
                    temperature = 81.4f,
                    storageHealthy = true,
                    status = "WARNING"
                };

            case "CAM_003":
                return new CameraData
                {
                    cameraId = "CAM_003",
                    name = "Lab Camera",
                    online = false,
                    model = "AXIS Test Camera",
                    osVersion = "12.0",
                    uptime = 0,
                    temperatureAvailable = false,
                    temperature = 0.0f,
                    storageHealthy = false,
                    status = "OFFLINE"
                };

            default:
                return null;
        }
    }

    private IEnumerator GetCameraCoroutine(
        string cameraId,
        Action<CameraData> onSuccess,
        Action<string> onError)
    {
        string url = $"{baseUrl.TrimEnd('/')}/camera/{UnityWebRequest.EscapeURL(cameraId)}";

        using UnityWebRequest request = UnityWebRequest.Get(url);
        request.timeout = 8;

        yield return request.SendWebRequest();

        if (request.result != UnityWebRequest.Result.Success)
        {
            string message =
                $"Camera API request failed ({request.responseCode}): {request.error}\n{url}";
            Debug.LogError(message);
            onError?.Invoke(message);
            yield break;
        }

        CameraData data;

        try
        {
            data = JsonUtility.FromJson<CameraData>(request.downloadHandler.text);
        }
        catch (Exception exception)
        {
            string message = $"Could not parse camera JSON: {exception.Message}";
            Debug.LogError(message);
            onError?.Invoke(message);
            yield break;
        }

        if (data == null || string.IsNullOrWhiteSpace(data.cameraId))
        {
            string message = $"Camera API returned invalid JSON: {request.downloadHandler.text}";
            Debug.LogError(message);
            onError?.Invoke(message);
            yield break;
        }

        onSuccess?.Invoke(data);
    }

    private IEnumerator GetAllCamerasCoroutine(
        Action<CameraData[]> onSuccess,
        Action<string> onError)
    {
        string url = $"{baseUrl.TrimEnd('/')}/cameras";

        using UnityWebRequest request = UnityWebRequest.Get(url);
        request.timeout = 10;

        yield return request.SendWebRequest();

        if (request.result != UnityWebRequest.Result.Success)
        {
            string message =
                $"Camera list request failed ({request.responseCode}): {request.error}\n{url}";
            Debug.LogError(message);
            onError?.Invoke(message);
            yield break;
        }

        try
        {
            string wrappedJson = "{\"items\":" + request.downloadHandler.text + "}";
            CameraDataList wrapper = JsonUtility.FromJson<CameraDataList>(wrappedJson);

            if (wrapper == null || wrapper.items == null)
            {
                onError?.Invoke("Camera list API returned invalid JSON.");
                yield break;
            }

            onSuccess?.Invoke(wrapper.items);
        }
        catch (Exception exception)
        {
            string message = $"Could not parse camera list JSON: {exception.Message}";
            Debug.LogError(message);
            onError?.Invoke(message);
        }
    }

    private sealed class AxisSelfSignedCertificateHandler : CertificateHandler
    {
        protected override bool ValidateCertificate(byte[] certificateData)
        {
            // Prototype-only: the lab Q1728 currently uses its device
            // self-signed certificate. Disable this when a trusted cert is used.
            return true;
        }
    }

    [Serializable]
    private class BasicDeviceInfoResponse
    {
        public BasicDeviceInfoData data;
    }

    [Serializable]
    private class BasicDeviceInfoData
    {
        public BasicDeviceProperties propertyList;
    }

    [Serializable]
    private class BasicDeviceProperties
    {
        public string ProdNbr;
        public string ProdFullName;
        public string ProdShortName;
        public string Version;
        public string SerialNumber;
    }

    [Serializable]
    private class SystemReadyResponse
    {
        public SystemReadyData data;
    }

    [Serializable]
    private class SystemReadyData
    {
        public string systemready;
        public int uptime;
    }

    [Serializable]
    private class PowerStatusResponse
    {
        public PowerStatusData data;
    }

    [Serializable]
    private class PowerStatusData
    {
        public PowerUsage usage;
        public int psePoeClass;
        public int lldpPoeClass;
        public float powerRequested;
        public int powerState;
    }

    [Serializable]
    private class PowerUsage
    {
        public float currentPower;
        public float averagePower;
        public float maxPower;
    }
}
