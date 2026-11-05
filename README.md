# Axis XR Camera Monitoring

A Unity mixed-reality prototype developed with Axis Communications for the Lund University Augmented Reality course.

The application runs on Meta Quest 3 and connects camera status and telemetry to physical Axis cameras. Users can browse devices in an overview, inspect a selected camera, follow location guidance, or identify a camera directly through its attached QR code.

**Group 5:** Dong Jieru, Xu Haobo, Li Wenao.

## Current version

The integrated implementation is on **[feature/spatial-vapix-integration](https://github.com/DonGCoco/Axis-XR-Monitoring/tree/feature/spatial-vapix-integration)**. The latest application changes on this branch were committed on **30 October 2026**. The default `main` branch contains an earlier version.

The current prototype has been demonstrated in the laboratory with two real Axis cameras. It includes real telemetry, spatial registration, mapped navigation and on-demand image previews. Broader reliability and user evaluation remain planned.

## Implemented workflow

### Browse and inspect

- Switch between spatial and list overviews.
- Prioritise offline and warning devices ahead of healthy devices.
- Filter and page through the list view.
- Select a camera, inspect its preview and open its details.
- View device identity and available VAPIX telemetry.
- Open the selected camera's Live View preview.

The spatial overview uses status-based display positions. These positions are separate from the cameras' registered physical locations.

### Identify a physical camera

QR payloads such as `CAM_001` identify devices. Looking at a supported QR code allows the application to retrieve the associated camera information and open its details without first selecting it in the overview.

The current integration uses MRUK QR trackables. During registration, the tracked QR pose supplies the physical location for a Meta Spatial Anchor.

### Register and locate

- Register camera positions using QR codes and persistent Meta Spatial Anchors.
- Store camera ID-to-anchor UUID bindings locally on the headset.
- Maintain separate Home and Lab location registries.
- Restore, re-register or clear locations through the location settings.
- Build a walkable navigation map from Quest room data using MRUK SceneNavigation and Unity NavMesh.
- Guide the walk with floor arrows placed along the complete mapped route.
- Show the target and remaining distance in a compact lower-left control bar.
- Confirm the target camera through QR identification. A mismatched QR does not replace the active navigation target.

Navigation requires a usable room map and a localized, tracked camera anchor. Map loading, route availability and recovery have explicit UI states. Laboratory recordings show the navigation workflow in use; reliable operation across other layouts and repeated sessions still requires testing.

### Floor guidance

Locate draws the route on the floor instead of a floating next-point marker.

- `0.5 m × 0.3 m` blue arrows with a white edge, spaced `0.8 m`, covering roughly the next `5 m` of the route.
- Arrows follow every NavMesh corner, so turns match the mapped route rather than a straight line to the camera.
- Each arrow is checked against the NavMesh at its centre and around its outline. An arrow that would leave the walkable surface is not drawn, and no straight-line fallback is generated.
- A ground ring marks the destination once the remaining route is short.
- The lower-left control bar shows the target and remaining distance, with `Cancel` and a collapsed `Info` panel containing diagnostics and `Reload map`.
- Guidance hides when localization, the room map or a complete route is unavailable. Cancelling, switching target or deleting saved positions clears the arrows.
- Arrows refresh when the route is recalculated, not every frame, and are reused from a small object pool.

Arrow geometry uses the project's retained UI material. The navigation UI adds no textures, prefabs, shaders or scene objects, and it leaves the Boundary, anchor and QR identity code untouched.

The saved room map is not live obstacle avoidance. Arrows describe the route through mapped geometry, so unmapped furniture, people and moved objects are not avoided.

### Interaction

The prototype includes onboarding, Help, Meta Interaction SDK ray-based selection and interaction feedback.

## Real camera integration

The laboratory proxy currently maps two devices:

| Application ID | Camera |
| --- | --- |
| `CAM_001` | AXIS Q1728 |
| `CAM_002` | AXIS Q1809-LE |

The proxy authenticates to the cameras, reads VAPIX responses and provides a common HTTP interface to Quest.

| Data group | Fields implemented |
| --- | --- |
| Identity | Model, OS version, serial number |
| Runtime | Uptime and system-ready response |
| Temperature | Main, CPU, image sensor and optics, where returned |
| Storage | Internal eMMC health and wear |
| Power | Current, average and peak power, requested power and PoE class |

Availability depends on the device and API response. Temperature readings are not CPU utilisation. CPU, memory and DLPU utilisation are not retrieved by the current proxy.

### Status interpretation

The current proxy uses a limited status rule:

- `OFFLINE`: the basic device-information request fails. This can reflect a connection or authentication problem and does not establish a hardware fault.
- `WARNING`: an available eMMC health response reports an unhealthy condition.
- `HEALTHY`: neither of the above conditions applies.

No temperature or power thresholds are invented, and `systemReady = no` alone does not trigger a warning.

`HEALTHY` means no warning was derived from these rules. It is not a comprehensive diagnosis: optional telemetry requests can fail without changing this status. More complete handling of unknown or incomplete health information remains an evaluation and development item.

### Live View

Live View is an opt-in, periodically refreshed JPEG preview, not full-motion RTSP video.

The proxy requests a 640 × 360 JPEG. While the panel is open, Quest requests another snapshot after each completed request and a one-second delay. Closing the panel stops refreshes and releases the image texture.

Camera credentials remain in the proxy process rather than being sent to the headset.

## Laboratory setup

### Hardware and network

- Meta Quest 3.
- Axis cameras connected through a PoE switch.
- Windows computer connected to the switch by Ethernet.
- Quest connected to the Windows Mobile Hotspot.
- Python proxy listening on TCP port 5000.

The current Quest proxy URL is `http://192.168.137.1:5000`.

### Start the real-camera proxy

From `backend/axis-proxy` on Windows:

1. Connect and power the cameras.
2. Enable Windows Mobile Hotspot and connect Quest to it.
3. Follow the one-time network setup in [the proxy guide](backend/axis-proxy/README.md).
4. Run `start_proxy_windows.bat` and enter the camera credentials when prompted.
5. Check `http://127.0.0.1:5000/cameras` on Windows.
6. Check `http://192.168.137.1:5000/cameras` in the Quest browser.
7. Open the Quest application and inspect the overview and camera details.

Restart the Python process after updating proxy code.

| Endpoint | Purpose |
| --- | --- |
| `GET /health` | Proxy availability |
| `GET /cameras` | Read configured devices |
| `GET /camera/CAM_001` | Read one device; also supports CAM_002 |
| `GET /camera/CAM_001/snapshot` | Fetch a JPEG preview; also supports CAM_002 |

The prototype proxy has no client authentication. Use it only on the private laboratory network or hotspot. Do not commit camera passwords.

### Unity and Quest

Use the integrated branch and open `Assets/Scenes/SampleScene.unity` in the Unity project.

The project uses Unity 6.3 LTS, OpenXR, Meta XR and MRUK. Resolve the versions specified by the project's package files rather than substituting versions from older setup notes.

Build for Android/Quest. Allow the requested spatial permissions, prepare the Quest room map and register camera locations in the appropriate site before testing Locate.

See [spatial registration and recovery](docs/SPATIAL_LOCATIONS.md) and [QR setup](docs/QUEST_QR_SETUP.md) for further setup context. Some documents describe earlier implementation stages; the current navigation service is `CameraNavigationPath.cs` in the integrated branch.

### Floor guidance verification

This branch was checked as far as the development machine allowed. Nothing below was validated on a headset.

| Check | Result |
| --- | --- |
| Unity compile | Pass. 0 errors, 0 warnings. Built with Unity `6000.3.12f1`; the project targets `6000.3.24f1`. |
| EditMode tests | Pass. 5/5, including the QR identity checks. |
| Android/Quest build | Pass. IL2CPP, ARM64, `targetSdk 34`, 81.8 MB APK, 0 errors. |
| Arrow material, orientation, turn direction | Rendered offscreen against a generated NavMesh: arrows appear as blue fills with white edges, lie flat, and turn with the route. |
| Control bar and diagnostics layout | Rendered at Quest 3 resolution and field of view: the bar sits lower-left, and both panels, the two buttons and `Reload map` fit without clipping. |
| On-device walk, hand-ray interaction, multi-room and floor-height behaviour | Not run. No headset was attached, so `adb` reported no devices. |

Still unverified on hardware: arrow visibility against real floor textures in passthrough, comfort of the head-locked lower-left bar while walking, text legibility of the `15`–`18` pt labels at `1 m`, depth sorting between the white edge and the blue fill at grazing angles, and whether the NavMesh surface matches the physical floor closely enough for the arrows to sit on it rather than float.

## Repository structure

```text
Assets/
  Scenes/SampleScene.unity
  Scripts/
    AxisMonitoringUI.cs             # Overview, selection, details and onboarding
    AxisMonitoringUI.Spatial.cs     # Registration and Locate UI
    AxisMonitoringUI.Vapix.cs       # Real telemetry presentation
    AxisMonitoringUI.LiveView.cs    # JPEG preview lifecycle
    CameraApiClient.cs             # Data access and VAPIX parsing
    CameraData.cs                  # Device data and availability fields
    CameraSpatialAnchors.cs        # Persistent physical-location bindings
    CameraNavigationPath.cs        # MRUK/NavMesh route service
    FloorNavigationArrows.cs       # Ground arrow and destination-ring guidance
    QrCameraScanner.cs             # QR identification and registration input
backend/
  axis-proxy/                      # Real two-camera Windows proxy and launchers
  axis-mock/                       # Earlier development/test service
docs/
  QUEST_QR_SETUP.md
  SPATIAL_LOCATIONS.md
Packages/
ProjectSettings/
```

## Mock development mode

The earlier mock service remains available for controlled interface testing:

```bash
cd backend/axis-mock
python3 -m venv venv
source venv/bin/activate
pip install -r requirements.txt
python app.py
```

It provides three synthetic cases: CAM_001 healthy, CAM_002 warning and CAM_003 offline. These are development scenarios, not observations of three connected laboratory devices. They are separate from the current two-camera real proxy.

## Next evaluation

The next stage is to assess the integrated workflow through a pilot and task-based user evaluation:

- Find a specified physical camera.
- Identify a nearby camera and inspect its status.
- Locate a reading that needs attention.

Planned measures include task success, completion time, wrong-device selections, assistance and perceived difficulty. A suitable two-dimensional comparison is still to be finalised.

Repeated-session anchor recovery, navigation under different room-map conditions, input reliability and interpretation of incomplete telemetry need further validation. The project does not yet establish that XR is faster than existing tools or that displayed metrics explain the cause of a fault.
