import argparse
import getpass
import json
import re
import ssl
import urllib.request
from datetime import datetime, timezone
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

CAMERAS = {
    "CAM_001": {
        "name": "AXIS Q1728",
        "base_url": "https://169.254.96.234",
        "default_username": "camera01",
    },
    "CAM_002": {
        "name": "AXIS Q1809-LE",
        "base_url": "https://169.254.52.210",
        "default_username": "camera02",
    },
}

_credentials = {}
_ssl_context = ssl._create_unverified_context()


def now_iso():
    return datetime.now(timezone.utc).isoformat()


def auth_opener(base_url, username, password):
    pm = urllib.request.HTTPPasswordMgrWithDefaultRealm()
    pm.add_password(None, base_url, username, password)
    digest = urllib.request.HTTPDigestAuthHandler(pm)
    basic = urllib.request.HTTPBasicAuthHandler(pm)
    https = urllib.request.HTTPSHandler(context=_ssl_context)
    return urllib.request.build_opener(digest, basic, https)


def request_text(camera_id, path, method="GET", body=None, content_type=None, timeout=6):
    cfg = CAMERAS[camera_id]
    username, password = _credentials[camera_id]
    url = cfg["base_url"].rstrip("/") + "/" + path.lstrip("/")
    data = None
    headers = {}
    if body is not None:
        data = body.encode("utf-8")
        headers["Content-Type"] = content_type or "application/json"
    req = urllib.request.Request(url, data=data, headers=headers, method=method)
    opener = auth_opener(cfg["base_url"], username, password)
    with opener.open(req, timeout=timeout) as response:
        return response.read().decode("utf-8", errors="replace")


def json_post(camera_id, path, payload):
    return request_text(
        camera_id,
        path,
        method="POST",
        body=json.dumps(payload),
        content_type="application/json",
    )


def first_nonempty(*values):
    for value in values:
        if value is not None and str(value).strip():
            return str(value).strip()
    return ""


def parse_temp(text, camera):
    lines = [line.strip() for line in text.replace("\r", "\n").split("\n") if line.strip()]
    names, values = {}, {}
    for line in lines:
        if "=" not in line:
            continue
        key, value = line.split("=", 1)
        if key.endswith(".Name"):
            names[key[:-5]] = value.strip()
        elif key.endswith(".Celsius"):
            try:
                values[key[:-8]] = float(value.strip())
            except ValueError:
                pass

    hottest = None
    for prefix, value in values.items():
        sensor_name = re.sub(r"[^a-z0-9]", "", names.get(prefix, "").lower())
        hottest = value if hottest is None else max(hottest, value)
        if sensor_name == "main":
            camera["mainTemperatureAvailable"] = True
            camera["mainTemperature"] = value
        elif sensor_name == "cpu":
            camera["cpuTemperatureAvailable"] = True
            camera["cpuTemperature"] = value
        elif sensor_name == "imagesensor":
            camera["imageSensorTemperatureAvailable"] = True
            camera["imageSensorTemperature"] = value
        elif sensor_name in ("optics", "lens"):
            camera["opticsTemperatureAvailable"] = True
            camera["opticsTemperature"] = value

    if hottest is not None:
        camera["temperatureAvailable"] = True
        camera["temperature"] = hottest


def parse_storage(xml, camera):
    match = re.search(r'<HealthStatus\b[^>]*diskid="EMMC"[^>]*>', xml, re.I)
    if not match:
        return
    tag = match.group(0)
    camera["storageAvailable"] = True
    wear = re.search(r'wear="(-?\d+)"', tag, re.I)
    if wear:
        camera["emmcWearAvailable"] = True
        camera["emmcWear"] = int(wear.group(1))
    health = re.search(r'overallhealth="(-?\d+)"', tag, re.I)
    if health:
        camera["emmcHealthAvailable"] = True
        camera["storageHealthy"] = int(health.group(1)) == 1


def offline_camera(camera_id, error):
    cfg = CAMERAS[camera_id]
    return {
        "cameraId": camera_id,
        "name": cfg["name"],
        "online": False,
        "model": cfg["name"],
        "osVersion": "",
        "serialNumber": "",
        "uptime": -1,
        "temperatureAvailable": False,
        "temperature": 0.0,
        "storageHealthy": False,
        "status": "OFFLINE",
        "serverTime": now_iso(),
        "vapixTelemetry": True,
        "systemReadyAvailable": False,
        "systemReady": False,
        "mainTemperatureAvailable": False,
        "mainTemperature": 0.0,
        "cpuTemperatureAvailable": False,
        "cpuTemperature": 0.0,
        "imageSensorTemperatureAvailable": False,
        "imageSensorTemperature": 0.0,
        "opticsTemperatureAvailable": False,
        "opticsTemperature": 0.0,
        "storageAvailable": False,
        "emmcHealthAvailable": False,
        "emmcWearAvailable": False,
        "emmcWear": 0,
        "powerAvailable": False,
        "currentPower": 0.0,
        "averagePower": 0.0,
        "maxPower": 0.0,
        "powerRequested": 0.0,
        "poeClass": -1,
        "error": error,
    }


def read_camera(camera_id):
    cfg = CAMERAS[camera_id]
    camera = offline_camera(camera_id, "")
    camera["storageHealthy"] = True

    try:
        basic = json.loads(json_post(
            camera_id,
            "/axis-cgi/basicdeviceinfo.cgi",
            {"apiVersion": "1.3", "method": "getAllProperties"},
        ))
        props = basic.get("data", {}).get("propertyList", {})
        camera["online"] = True
        camera["model"] = first_nonempty(
            props.get("ProdFullName"), props.get("ProdShortName"),
            props.get("ProdNbr"), cfg["name"]
        )
        camera["name"] = first_nonempty(
            props.get("ProdShortName"), props.get("ProdNbr"), cfg["name"]
        )
        camera["osVersion"] = props.get("Version", "")
        camera["serialNumber"] = props.get("SerialNumber", "")
    except Exception as exc:
        return offline_camera(camera_id, f"{type(exc).__name__}: {exc}")

    try:
        parse_temp(
            request_text(camera_id, "/axis-cgi/temperaturecontrol.cgi?action=statusall"),
            camera,
        )
    except Exception:
        pass

    try:
        system = json.loads(json_post(
            camera_id,
            "/axis-cgi/systemready.cgi",
            {"apiVersion": "1.4", "method": "systemready", "params": {"timeout": 0}},
        ))
        data = system.get("data", {})
        camera["systemReadyAvailable"] = True
        camera["systemReady"] = str(data.get("systemready", "")).lower() == "yes"
        camera["uptime"] = int(data.get("uptime", -1))
    except Exception:
        pass

    try:
        parse_storage(
            request_text(camera_id, "/axis-cgi/disks/gethealth.cgi"),
            camera,
        )
    except Exception:
        pass

    try:
        power = json.loads(json_post(
            camera_id,
            "/axis-cgi/power-settings.cgi",
            {"apiVersion": "1.7", "method": "getPowerStatus"},
        ))
        data = power.get("data", {})
        usage = data.get("usage") or {}
        if usage:
            camera["powerAvailable"] = True
            camera["currentPower"] = float(usage.get("currentPower", 0.0))
            camera["averagePower"] = float(usage.get("averagePower", 0.0))
            camera["maxPower"] = float(usage.get("maxPower", 0.0))
            camera["powerRequested"] = float(data.get("powerRequested", 0.0))
            pse = int(data.get("psePoeClass", -1))
            lldp = int(data.get("lldpPoeClass", -1))
            camera["poeClass"] = pse if pse >= 0 else lldp
    except Exception:
        pass

    camera["status"] = (
        "WARNING"
        if camera["storageAvailable"]
        and camera["emmcHealthAvailable"]
        and not camera["storageHealthy"]
        else "HEALTHY"
    )
    camera["serverTime"] = now_iso()
    camera.pop("error", None)
    return camera


def prompt_credentials():
    print("Enter Axis credentials. Passwords are not stored or printed.")
    for camera_id, cfg in CAMERAS.items():
        default = cfg["default_username"]
        username = input(f"{camera_id} username [{default}]: ").strip() or default
        password = getpass.getpass(f"{camera_id} password (not displayed): ")
        _credentials[camera_id] = (username, password)


class Handler(BaseHTTPRequestHandler):
    def send_json(self, obj, status=200):
        body = json.dumps(obj, ensure_ascii=False).encode("utf-8")
        self.send_response(status)
        self.send_header("Content-Type", "application/json; charset=utf-8")
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)

    def do_GET(self):
        path = self.path.split("?", 1)[0].rstrip("/") or "/"
        if path == "/health":
            self.send_json({"status": "running", "cameras": list(CAMERAS)})
            return
        if path == "/cameras":
            self.send_json([read_camera(camera_id) for camera_id in CAMERAS])
            return
        match = re.fullmatch(r"/camera/([^/]+)", path, re.I)
        if match:
            camera_id = match.group(1).upper()
            if camera_id not in CAMERAS:
                self.send_json({"error": "unknown camera"}, 404)
                return
            self.send_json(read_camera(camera_id))
            return
        self.send_json({"error": "unknown camera endpoint"}, 404)

    def log_message(self, fmt, *args):
        print(f"[HTTP] {self.address_string()} - {fmt % args}")


def main():
    parser = argparse.ArgumentParser(description="Axis XR multi-camera VAPIX proxy")
    parser.add_argument("--check", action="store_true")
    parser.add_argument("--lan", action="store_true")
    parser.add_argument("--port", type=int, default=5000)
    args = parser.parse_args()

    prompt_credentials()

    if args.check:
        print(json.dumps(
            [read_camera(camera_id) for camera_id in CAMERAS],
            indent=2,
            ensure_ascii=False,
        ))
        return

    host = "0.0.0.0" if args.lan else "127.0.0.1"
    print(f"API Proxy running on http://{host}:{args.port}")
    print("Endpoints: /health, /cameras, /camera/CAM_001, /camera/CAM_002")
    ThreadingHTTPServer((host, args.port), Handler).serve_forever()


if __name__ == "__main__":
    main()
