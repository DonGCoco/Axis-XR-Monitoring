"""Native temperature event ingestion and explicit health issues. No numeric alarm threshold."""
import json
import threading
import time
from datetime import datetime, timezone
from urllib.parse import quote
from xml.etree import ElementTree as ET


def now():
    return datetime.now(timezone.utc).isoformat()


def issue(kind, message, source="telemetry", evidence=""):
    return dict(type=kind, message=message, source=source, evidence=evidence)


def temperature_topics(xml):
    root = ET.fromstring(xml)
    if any(e.tag.endswith("Fault") for e in root.iter()):
        raise ValueError("Event discovery SOAP fault")
    topics = []
    def walk(e, path):
        local = e.tag.split("}")[-1]
        if local == "Device":
            path = ["tns1:Device"]
        elif path:
            path = path + ["tnsaxis:" + local]
        if path and any(k.split("}")[-1] == "topic" and v in ("true", "1") for k,v in e.attrib.items()):
            simple = "/".join(x.split(":")[-1] for x in path)
            if simple in ("Device/Status/Temperature/Above", "Device/Status/Temperature/Below", "Device/Status/Temperature/Above_or_below", "Device/HardwareFailure/TemperatureCritical"):
                topics.append("/".join(path))
        for child in e:
            walk(child, path)
    walk(root, [])
    return topics


class NativeTemperature:
    def __init__(self):
        self.lock = threading.Lock()
        self.state = {}

    def reset(self, camera_id, status):
        with self.lock:
            self.state[camera_id] = dict(status=status, values={})

    def notify(self, camera_id, packet):
        if packet.get("method") != "events:notify":
            return
        n = packet.get("params", {}).get("notification", {})
        topic = "/".join(x.split(":")[-1] for x in n.get("topic", "").split("/"))
        if topic not in ("Device/Status/Temperature/Above", "Device/Status/Temperature/Below", "Device/Status/Temperature/Above_or_below", "Device/HardwareFailure/TemperatureCritical"):
            return
        msg = n.get("message", {})
        data = msg.get("data", {})
        value = data.get("sensor_level", data.get("temperature_critical"))
        if str(value).lower() not in ("true", "false", "1", "0"):
            return
        key = topic + json.dumps(msg.get("source", {}), sort_keys=True)
        with self.lock:
            entry = self.state.setdefault(camera_id, dict(status="connected", values={}))
            entry["values"][key] = dict(active=str(value).lower() in ("true", "1"), topic=topic, observedAt=now())

    def apply(self, camera_id, camera):
        with self.lock:
            entry = self.state.get(camera_id, dict(status="starting", values={}))
            values = list(entry["values"].values())
            status = entry["status"]
        camera["temperatureEventStatus"] = status
        camera["temperatureAlarmAvailable"] = status == "connected" and bool(values)
        camera["temperatureAlarmActive"] = any(v["active"] for v in values)
        for v in values:
            if v["active"]:
                label = "Below operating temperature" if v["topic"].endswith("/Below") else "Temperature outside operating range"
                camera["issues"].append(issue("temperature", label, "camera_event", v["topic"]))
        if not camera["temperatureAlarmAvailable"]:
            camera["issues"].append(issue("temperature_monitoring_unavailable", "Temperature alarm state unavailable", "camera_event", status))

    def run(self, camera_id, base_url, request, stop):
        try:
            import websocket
        except ImportError:
            self.reset(camera_id, "dependency_missing")
            return
        while not stop.is_set():
            self.reset(camera_id, "discovering")
            ws = None
            try:
                body = '<s:Envelope xmlns:s="http://www.w3.org/2003/05/soap-envelope"><s:Body><GetEventInstances xmlns="http://www.axis.com/vapix/ws/event1"/></s:Body></s:Envelope>'
                xml = request(camera_id, "/vapix/services", method="POST", body=body, content_type='application/soap+xml; action="http://www.axis.com/vapix/ws/event1/GetEventInstances"; charset=utf-8')
                topics = temperature_topics(xml)
                if not topics:
                    self.reset(camera_id, "unsupported")
                    stop.wait(60)
                    continue
                token = request(camera_id, "/axis-cgi/wssession.cgi").strip()
                # Session tokens are never logged or returned to Quest.
                url = base_url.replace("https://", "wss://", 1) + "/vapix/ws-data-stream?sources=events&wssession=" + quote(token, safe="")
                import ssl
                ws = websocket.create_connection(url, timeout=10, sslopt={"cert_reqs": ssl.CERT_NONE}, http_proxy_host=None)
                ws.send(json.dumps(dict(apiVersion="1.0", method="events:configure", params=dict(eventFilterList=[dict(topicFilter=t) for t in topics]))))
                configured = False
                last_ping = time.monotonic()
                while not stop.is_set():
                    try:
                        packet = json.loads(ws.recv())
                    except websocket.WebSocketTimeoutException:
                        if time.monotonic() - last_ping > 30:
                            ws.ping()
                            last_ping = time.monotonic()
                        continue
                    if packet.get("error"):
                        raise ValueError("Event subscription rejected")
                    if packet.get("method") == "events:configure":
                        configured = True
                        with self.lock:
                            self.state[camera_id]["status"] = "connected"
                    elif packet.get("method") == "events:notify":
                        self.notify(camera_id, packet)
            except Exception:
                # Invalidate old values: a disconnected event stream is not a healthy device.
                self.reset(camera_id, "unavailable")
            finally:
                if ws is not None:
                    ws.close()
            stop.wait(5)


def finalize(camera, native=None):
    issues = camera.setdefault("issues", [])
    if not camera.get("online"):
        issues.append(issue("connection", "Camera unavailable; cause undetermined", "proxy"))
        camera["status"] = "OFFLINE"
        return camera
    for available, kind, message in (
        ("temperatureAvailable", "temperature_data_unavailable", "Temperature data unavailable"),
        ("systemReadyAvailable", "system_data_unavailable", "System readiness unavailable"),
        ("emmcHealthAvailable", "storage_data_unavailable", "eMMC health unavailable"),
        ("powerAvailable", "power_data_unavailable", "Power data unavailable"),
    ):
        if not camera.get(available):
            issues.append(issue(kind, message))
    if camera.get("emmcHealthAvailable") and not camera.get("storageHealthy"):
        issues.append(issue("storage", "eMMC reports unhealthy storage"))
    if camera.get("systemReadyAvailable") and not camera.get("systemReady"):
        issues.append(issue("system_not_ready", "System not ready; startup may be in progress"))
    if native:
        native.apply(camera["cameraId"], camera)
    camera["status"] = "WARNING" if issues else "HEALTHY"
    camera["healthIssuesAvailable"] = True
    return camera
