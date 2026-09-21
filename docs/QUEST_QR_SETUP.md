# Quest 3 + QR setup

This repository already contains the mock backend and the Unity-side camera data / QR scanning scripts. The Unity project itself should be created locally with Unity 6.3 LTS.

## 1. Create the Unity project

Create a Universal 3D project with Unity 6.3 LTS and place it inside this repository, for example:

```text
Axis-XR-Monitoring/
├── backend/
├── unity/
└── AxisXRMonitoring/
```

The scripts currently under `unity/Assets/Scripts/` can then be copied into the Unity project's `Assets/Scripts/` folder.

## 2. XR provider

Use Unity OpenXR for Android / Meta Quest.

Meta currently recommends OpenXR for new Quest projects.

## 3. Install Meta packages

Install:

- Meta XR Core SDK
- Meta XR MR Utility Kit (MRUK), version 83 or newer

## 4. Scene setup

Use Meta Building Blocks to add:

- Camera Rig
- Passthrough Layer

On the Camera Rig / OVRManager:

- Scene Support: Required
- Anchor Support: Enabled
- Request Scene / Spatial Data permission

Add an `MRUK` component to the scene.

In MRUK:

- Scene Settings
- Tracker Configuration
- Enable QR Code Tracking

## 5. App objects

Create one GameObject called `CameraServices`.

Add:

- `CameraApiClient`
- `QrCameraScanner`

For Editor testing, the API base URL can remain:

```text
http://127.0.0.1:5000
```

For a Quest build, replace that with the Mac's LAN address, for example:

```text
http://192.168.1.20:5000
```

The Quest and Mac must be on the same network.

## 6. QR payload

For the first test, generate a QR code containing exactly:

```text
CAM_001
```

The scanner intentionally ignores payloads that do not begin with `CAM_`.

## 7. Mock backend

Run:

```bash
cd backend/axis-mock
python3 -m venv venv
source venv/bin/activate
pip install -r requirements.txt
python app.py
```

The backend listens on port 5000.

## 8. First validation target

The first end-to-end milestone is:

```text
Quest detects CAM_001 QR
        ↓
MRUK returns the QR payload
        ↓
QrCameraScanner calls CameraApiClient
        ↓
Mock backend returns camera data
        ↓
Unity logs HEALTHY
```

The world-space status panel can be wired after this console path is confirmed.
