# Axis XR Monitoring

XR prototype for identifying physical Axis cameras with QR codes and viewing device health / telemetry in Meta Quest 3.

## Current MVP

1. Scan a QR code attached to a camera.
2. Read the camera ID, for example `CAM_001`.
3. Resolve the camera ID to camera data.
4. Retrieve camera status from a mock API during development.
5. Display camera status in XR.
6. Replace mock data with live Axis VAPIX data when the cameras are available.

## Repository structure

```text
Axis-XR-Monitoring/
├── backend/
│   └── axis-mock/
│       ├── app.py
│       └── requirements.txt
├── unity/
│   └── Assets/
│       └── Scripts/
│           ├── CameraData.cs
│           └── CameraApiClient.cs
└── README.md
```

## Mock backend

The development backend exposes:

- `GET /cameras` — all cameras
- `GET /camera/CAM_001` — one camera

Development-only camera states:

- `CAM_001` — HEALTHY
- `CAM_002` — WARNING
- `CAM_003` — OFFLINE

Run locally:

```bash
cd backend/axis-mock
python3 -m venv venv
source venv/bin/activate
pip install -r requirements.txt
python app.py
```

Then open:

```text
http://127.0.0.1:5000/cameras
```

## Unity / Quest direction

Target stack:

- Unity 6.3 LTS
- Universal 3D / URP
- Meta Quest 3
- Unity OpenXR
- Meta XR Core SDK
- Meta MR Utility Kit (MRUK)
- MRUK QR Code Detection

The Meta XR packages are intentionally not committed yet. Install them in Unity when the Unity project is created so Package Manager can resolve the correct package versions.

## Important

The current warning thresholds and telemetry are mock development data only. They are not official Axis thresholds. Live telemetry and health rules will be updated after the real Axis camera model and VAPIX capabilities are known.
