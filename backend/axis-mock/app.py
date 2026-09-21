from flask import Flask, jsonify

app = Flask(__name__)

# Development-only mock data.
# Thresholds here are NOT official Axis thresholds.
cameras = {
    "CAM_001": {
        "cameraId": "CAM_001",
        "name": "Entrance Camera",
        "online": True,
        "model": "AXIS Test Camera",
        "osVersion": "12.0",
        "uptime": 86400,
        "temperature": 43.2,
        "storageHealthy": True,
    },
    "CAM_002": {
        "cameraId": "CAM_002",
        "name": "Hallway Camera",
        "online": True,
        "model": "AXIS Test Camera",
        "osVersion": "12.0",
        "uptime": 43200,
        "temperature": 81.4,
        "storageHealthy": True,
    },
    "CAM_003": {
        "cameraId": "CAM_003",
        "name": "Lab Camera",
        "online": False,
        "model": "AXIS Test Camera",
        "osVersion": "12.0",
        "uptime": 0,
        "temperature": None,
        "storageHealthy": False,
    },
}


def get_camera_status(camera):
    """Return a development-only health state for mock data."""
    if not camera["online"]:
        return "OFFLINE"

    if camera["temperature"] is not None and camera["temperature"] >= 70:
        return "WARNING"

    if not camera["storageHealthy"]:
        return "WARNING"

    return "HEALTHY"


def camera_with_status(camera):
    result = camera.copy()
    result["status"] = get_camera_status(camera)
    return result


@app.get("/health")
def health():
    return jsonify({"status": "ok"})


@app.get("/camera/<camera_id>")
def get_camera(camera_id):
    camera = cameras.get(camera_id.upper())

    if camera is None:
        return jsonify({"error": "Camera not found"}), 404

    return jsonify(camera_with_status(camera))


@app.get("/cameras")
def get_all_cameras():
    return jsonify([camera_with_status(camera) for camera in cameras.values()])


if __name__ == "__main__":
    # 0.0.0.0 allows a Quest on the same LAN to reach this Mac.
    app.run(host="0.0.0.0", port=5000, debug=True)
