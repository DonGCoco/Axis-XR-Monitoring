"""Multi-camera Axis VAPIX -> Unity JSON proxy.

Python standard library only.

Files next to this script:
  cameras.json   Camera IDs, IPs, usernames and model hints (safe for GitHub)
  secrets.json   Passwords only (keep local; do not commit)

Examples:
  python axis_proxy.py --check CAM_002
  python axis_proxy.py --check
  python axis_proxy.py --lan
"""

import argparse
import json
import re
import ssl
import sys
from concurrent.futures import ThreadPoolExecutor, as_completed
from datetime import datetime, timezone
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from urllib.parse import unquote, urlparse
from urllib.request import (
    HTTPBasicAuthHandler,
    HTTPDigestAuthHandler,
    HTTPPasswordMgrWithDefaultRealm,
    HTTPSHandler,
    Request,
    build_opener,
)
from xml.etree import ElementTree

BASE_DIR = Path(__file__).resolve().parent
DEFAULT_CONFIG = BASE_DIR / "cameras.json"
DEFAULT_SECRETS = BASE_DIR / "secrets.json"


def number(value, convert=float, default=0):
    try:
        return convert(value)
    except (ValueError, TypeError, OverflowError):
        return default


def utc_now():
    return datetime.now(timezone.utc).isoformat()


def load_json(path):
    with Path(path).open("r", encoding="utf-8") as handle:
        data = json.load(handle)
    if not isinstance(data, dict):
        raise ValueError(f"{path} must contain a JSON object")
    return data


def normalize_camera_id(value):
    return str(value or "").strip().upper()


def offline_camera(camera_id, cfg, reason=None):
    model_hint = str(cfg.get("modelHint") or "").strip()
    display_name = str(cfg.get("displayName") or model_hint or camera_id).strip()
    result = {
        "cameraId": camera_id,
        "name": display_name,
        "online": False,
        "model": model_hint,
        "osVersion": "",
        "serialNumber": "",
        "uptime": -1,
        "temperatureAvailable": False,
        "temperature": 0.0,
        "storageHealthy": True,
        "status": "OFFLINE",
        "serverTime": utc_now(),
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
    }
    if reason:
        result["proxyError"] = reason
    return result


class AxisClient:
    def __init__(self, camera_id, cfg, password):
        self.camera_id = camera_id
        self.cfg = cfg
        self.root = str(cfg.get("url") or "").strip().rstrip("/")
        self.username = str(cfg.get("username") or "").strip()
        self.password = password or ""

    def validate(self):
        if not self.root.startswith("https://"):
            raise ValueError(f"{self.camera_id}: camera url must start with https://")
        if not self.username:
            raise ValueError(f"{self.camera_id}: username is missing")
        if not self.password:
            raise ValueError(f"{self.camera_id}: password is missing in secrets.json")

    def request(self, path, body=None, timeout=3.0):
        self.validate()
        ctx = ssl._create_unverified_context()
        passwords = HTTPPasswordMgrWithDefaultRealm()
        passwords.add_password(None, self.root, self.username, self.password)
        passwords.add_password(None, self.root + "/", self.username, self.password)
        opener = build_opener(
            HTTPSHandler(context=ctx),
            HTTPDigestAuthHandler(passwords),
            HTTPBasicAuthHandler(passwords),
        )
        headers = {"Accept": "application/json, text/plain, */*"}
        data = None
        if body is not None:
            headers["Content-Type"] = "application/json"
            data = json.dumps(body).encode("utf-8")
        req = Request(self.root + path, data=data, headers=headers)
        with opener.open(req, timeout=timeout) as response:
            return response.read().decode("utf-8", errors="replace")

    def safe_request(self, path, body=None):
        try:
            return self.request(path, body, timeout=2.5)
        except Exception as exc:
            print(
                f"[{self.camera_id} optional unavailable] {path}: "
                f"{type(exc).__name__}: {exc}",
                file=sys.stderr,
            )
            return None

    def camera_data(self):
        basic = json.loads(
            self.request(
                "/axis-cgi/basicdeviceinfo.cgi",
                {"apiVersion": "1.3", "method": "getAllProperties"},
                timeout=4.0,
            )
        )
        props = basic.get("data", {}).get("propertyList")
        if not isinstance(props, dict):
            raise RuntimeError(
                f"Missing propertyList in basicdeviceinfo response: {str(basic)[:450]}"
            )

        model = (
            props.get("ProdFullName")
            or props.get("ProdShortName")
            or props.get("ProdNbr")
            or str(self.cfg.get("modelHint") or "")
        )
        name = (
            str(self.cfg.get("displayName") or "").strip()
            or props.get("ProdShortName")
            or props.get("ProdNbr")
            or self.camera_id
        )
        result = offline_camera(self.camera_id, self.cfg)
        result.update(
            {
                "name": name,
                "online": True,
                "model": model,
                "osVersion": props.get("Version") or "",
                "serialNumber": props.get("SerialNumber") or "",
                "status": "HEALTHY",
                "serverTime": utc_now(),
            }
        )

        tasks = {
            "temp": ("/axis-cgi/temperaturecontrol.cgi?action=statusall", None),
            "system": (
                "/axis-cgi/systemready.cgi",
                {
                    "apiVersion": "1.4",
                    "method": "systemready",
                    "params": {"timeout": 0},
                },
            ),
            "storage": ("/axis-cgi/disks/gethealth.cgi", None),
            "power": (
                "/axis-cgi/power-settings.cgi",
                {"apiVersion": "1.7", "method": "getPowerStatus"},
            ),
        }
        with ThreadPoolExecutor(max_workers=4) as pool:
            futures = {
                key: pool.submit(self.safe_request, *args)
                for key, args in tasks.items()
            }
            values = {key: future.result() for key, future in futures.items()}

        self.parse_temperature(values["temp"], result)
        self.parse_system(values["system"], result)
        self.parse_storage(values["storage"], result)
        self.parse_power(values["power"], result)
        result["serverTime"] = utc_now()
        return result

    @staticmethod
    def parse_temperature(raw, result):
        if not raw:
            return
        fields = {}
        for line in raw.splitlines():
            if "=" in line:
                key, val = line.strip().split("=", 1)
                fields[key] = val.strip()
        temps = []
        for key, val in fields.items():
            if not key.lower().endswith(".celsius"):
                continue
            try:
                deg = float(val)
            except ValueError:
                continue
            temps.append(deg)
            prefix = key[:-len(".Celsius")]
            sensor = re.sub(
                r"[^a-z0-9]", "", fields.get(prefix + ".Name", "").lower()
            )
            mappings = {
                "main": ("mainTemperatureAvailable", "mainTemperature"),
                "cpu": ("cpuTemperatureAvailable", "cpuTemperature"),
                "imagesensor": (
                    "imageSensorTemperatureAvailable",
                    "imageSensorTemperature",
                ),
                "optics": ("opticsTemperatureAvailable", "opticsTemperature"),
                "lens": ("opticsTemperatureAvailable", "opticsTemperature"),
            }
            if sensor in mappings:
                available, field = mappings[sensor]
                result[available] = True
                result[field] = deg
        if temps:
            result["temperatureAvailable"] = True
            result["temperature"] = max(temps)

    @staticmethod
    def parse_system(raw, result):
        if not raw:
            return
        try:
            data = json.loads(raw).get("data", {})
            if "uptime" in data:
                result["uptime"] = number(data.get("uptime"), int, -1)
                result["systemReadyAvailable"] = True
                result["systemReady"] = (
                    str(data.get("systemready", "")).lower() == "yes"
                )
        except (ValueError, AttributeError, TypeError):
            pass

    @staticmethod
    def parse_storage(raw, result):
        if not raw:
            return
        try:
            root = ElementTree.fromstring(raw)
            for element in root.iter():
                tag = element.tag.split("}")[-1].lower()
                if (
                    tag != "healthstatus"
                    or element.attrib.get("diskid", "").upper() != "EMMC"
                ):
                    continue
                result["storageAvailable"] = True
                if "wear" in element.attrib:
                    result["emmcWear"] = number(element.attrib["wear"], int)
                    result["emmcWearAvailable"] = True
                if "overallhealth" in element.attrib:
                    result["emmcHealthAvailable"] = True
                    result["storageHealthy"] = (
                        number(element.attrib["overallhealth"], int, -1) == 1
                    )
                    if not result["storageHealthy"]:
                        result["status"] = "WARNING"
                break
        except ElementTree.ParseError:
            pass

    @staticmethod
    def parse_power(raw, result):
        if not raw:
            return
        try:
            data = json.loads(raw).get("data", {})
            usage = data.get("usage")
            if not isinstance(usage, dict):
                return
            result["powerAvailable"] = True
            for key in ("currentPower", "averagePower", "maxPower"):
                result[key] = number(usage.get(key))
            result["powerRequested"] = number(data.get("powerRequested"))
            poe_class = number(data.get("psePoeClass"), int, -1)
            result["poeClass"] = (
                poe_class
                if poe_class >= 0
                else number(data.get("lldpPoeClass"), int, -1)
            )
        except (ValueError, AttributeError, TypeError):
            pass


class CameraRegistry:
    def __init__(self, config_path, secrets_path):
        self.config_path = Path(config_path)
        self.secrets_path = Path(secrets_path)
        self.cameras = load_json(self.config_path)
        self.secrets = load_json(self.secrets_path)

    def enabled_ids(self):
        return [
            normalize_camera_id(camera_id)
            for camera_id, cfg in self.cameras.items()
            if isinstance(cfg, dict) and bool(cfg.get("enabled", False))
        ]

    def config_for(self, camera_id):
        camera_id = normalize_camera_id(camera_id)
        cfg = self.cameras.get(camera_id)
        if not isinstance(cfg, dict) or not bool(cfg.get("enabled", False)):
            return None
        return cfg

    def client_for(self, camera_id):
        camera_id = normalize_camera_id(camera_id)
        cfg = self.config_for(camera_id)
        if cfg is None:
            return None
        secret_cfg = self.secrets.get(camera_id, {})
        password = secret_cfg.get("password", "") if isinstance(secret_cfg, dict) else ""
        return AxisClient(camera_id, cfg, password)

    def fetch_one(self, camera_id):
        camera_id = normalize_camera_id(camera_id)
        cfg = self.config_for(camera_id)
        if cfg is None:
            raise KeyError(camera_id)
        client = self.client_for(camera_id)
        try:
            return client.camera_data()
        except Exception as exc:
            message = f"{type(exc).__name__}: {exc}"
            print(f"[{camera_id} offline] {message}", file=sys.stderr)
            return offline_camera(camera_id, cfg, message)

    def fetch_all(self):
        camera_ids = self.enabled_ids()
        if not camera_ids:
            return []
        results = {}
        with ThreadPoolExecutor(max_workers=max(1, len(camera_ids))) as pool:
            futures = {pool.submit(self.fetch_one, cid): cid for cid in camera_ids}
            for future in as_completed(futures):
                cid = futures[future]
                results[cid] = future.result()
        return [results[cid] for cid in camera_ids]


class Handler(BaseHTTPRequestHandler):
    registry = None

    def log_message(self, fmt, *args):
        print(f"[http] {self.address_string()} - {fmt % args}")

    def respond(self, code, payload):
        blob = json.dumps(payload, ensure_ascii=False).encode("utf-8")
        self.send_response(code)
        self.send_header("Content-Type", "application/json; charset=utf-8")
        self.send_header("Cache-Control", "no-store")
        self.send_header("Content-Length", str(len(blob)))
        self.end_headers()
        self.wfile.write(blob)

    def do_GET(self):
        path = unquote(urlparse(self.path).path).rstrip("/")
        if not path:
            path = "/"

        if path == "/health":
            self.respond(
                200,
                {
                    "status": "running",
                    "configuredCameras": self.registry.enabled_ids(),
                },
            )
            return

        if path == "/cameras":
            self.respond(200, self.registry.fetch_all())
            return

        if path.startswith("/camera/"):
            camera_id = normalize_camera_id(path[len("/camera/") :])
            if self.registry.config_for(camera_id) is None:
                self.respond(404, {"error": f"Camera not configured: {camera_id}"})
                return
            self.respond(200, self.registry.fetch_one(camera_id))
            return

        self.respond(404, {"error": "Unknown camera endpoint"})


def main():
    parser = argparse.ArgumentParser(
        description="Multi-camera Axis VAPIX proxy matching Unity /cameras API"
    )
    parser.add_argument("--config", default=str(DEFAULT_CONFIG))
    parser.add_argument("--secrets", default=str(DEFAULT_SECRETS))
    parser.add_argument("--port", type=int, default=5000)
    parser.add_argument(
        "--lan",
        action="store_true",
        help="Allow Quest connection from LAN / Windows Mobile Hotspot",
    )
    parser.add_argument(
        "--check",
        nargs="?",
        const="ALL",
        metavar="CAM_ID",
        help="Fetch all enabled cameras, or one camera such as CAM_002, then exit",
    )
    args = parser.parse_args()

    try:
        registry = CameraRegistry(args.config, args.secrets)
    except Exception as exc:
        print(f"Configuration error: {type(exc).__name__}: {exc}", file=sys.stderr)
        sys.exit(2)

    if args.check is not None:
        if args.check == "ALL":
            payload = registry.fetch_all()
        else:
            camera_id = normalize_camera_id(args.check)
            if registry.config_for(camera_id) is None:
                print(f"Camera not configured or disabled: {camera_id}", file=sys.stderr)
                sys.exit(2)
            payload = registry.fetch_one(camera_id)
        print(json.dumps(payload, indent=2, ensure_ascii=False))
        return

    Handler.registry = registry
    host = "0.0.0.0" if args.lan else "127.0.0.1"
    server = ThreadingHTTPServer((host, args.port), Handler)
    print(f"API Proxy running on http://{host}:{args.port}")
    print(f"Configured cameras: {', '.join(registry.enabled_ids()) or '(none)'}")
    print("Endpoints: /health, /cameras, /camera/CAM_001, /camera/CAM_002, ...")
    print("Ctrl+C to stop.")
    try:
        server.serve_forever()
    except KeyboardInterrupt:
        print("\nShutting down.")
    finally:
        server.server_close()


if __name__ == "__main__":
    main()
