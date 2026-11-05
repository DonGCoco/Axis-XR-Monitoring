# Windows lab setup for Axis XR

Use this on the borrowed Windows laptop. Unity does not need to be installed on this machine.

## Target topology

- Axis cameras -> PoE switch -> Windows Ethernet
- Windows Wi-Fi -> any upstream network if needed
- Windows Mobile Hotspot -> Quest 3
- Axis proxy -> TCP 5000

The Quest build on `feature/spatial-vapix-integration` is configured for:

`http://192.168.137.1:5000`

## One-time setup

1. Connect the PoE switch to the Windows laptop by Ethernet.
2. Confirm the two cameras are powered.
3. Turn on **Settings > Network & internet > Mobile hotspot**.
4. Share the Windows Wi-Fi connection over Wi-Fi.
5. Connect Quest 3 to the Windows hotspot.
6. Open **PowerShell as Administrator** in this folder and run:

   `powershell -ExecutionPolicy Bypass -File .\setup_windows.ps1`

7. Run:

   `start_proxy_windows.bat`

8. Enter the two Axis camera passwords when prompted. Passwords are not stored.
9. On Windows, open:

   `http://127.0.0.1:5000/cameras`

10. In the Quest browser, open:

   `http://192.168.137.1:5000/cameras`

If both show CAM_001 and CAM_002, the network path is ready.

## Expected application flow

Overview -> real camera status -> Details -> Locate -> MRUK mapped navigation -> QR confirmation.

Details -> Live View -> on-demand JPEG preview (about one snapshot per second).

Do not use Windows ICS scripts, Hosted Network commands, Bluetooth PAN, or the old D-Link adapter on this laptop.

## JPEG snapshot preview

The same running proxy now supports:

- `http://127.0.0.1:5000/camera/CAM_001/snapshot`
- `http://127.0.0.1:5000/camera/CAM_002/snapshot`

Test one of these in the Windows browser first. A successful response displays an actual JPEG image rather than JSON.
The Quest uses `http://192.168.137.1:5000/camera/CAM_001/snapshot` (or CAM_002).
The proxy authenticates to the Axis devices; **never embed Axis passwords in Unity or commit them**.
Only the local lab hotspot should have access to this unauthenticated demo proxy. Do not expose port 5000 to public networks; shut down the proxy after testing.

After updating this repository, restart the Windows proxy process. Updating Git alone does not reload the running Python server.

## Real health issues (native temperature events)

Install the event transport dependency before running the existing launcher:

```powershell
python -m pip install -r requirements.txt
python axis_proxy_multi.py --lan
```

The proxy discovers supported native temperature topics using GetEventInstances,
then opens an authenticated WSS event subscription using a short-lived session token.
No camera alarm thresholds or configuration are modified. Requires the camera's
WebSocket event API and account permission; unsupported/failed/initial event state
is explicitly reported as unavailable rather than assumed healthy. No numerical
70 C threshold is used for real telemetry. Reconnect invalidates previous states.

`issues` reports simultaneous native temperature alarms, eMMC health problems,
system-not-ready observations and unavailable telemetry. WARNING can mean missing
monitoring data, not a confirmed hardware fault. System not ready may mean startup;
this phase does not implement startup grace periods or failure/recovery debouncing.
Connection failures still map immediately to OFFLINE. No root-cause claims.
Unity uses paged issue cards with the existing XR buttons; old mock data keeps its
legacy UI. Install the matching Unity build and proxy together.

Validation:

```powershell
python -m unittest -v test_axis_health.py
python axis_proxy_multi.py --check
```

On hardware, inspect `/cameras`: `temperatureEventStatus` should be `connected`;
`temperatureAlarmAvailable` becomes true only after a recognized native state.
If false, temperature alarm coverage is unverified even if numerical readings work.
Check eMMC availability and systemReady separately. Confirm Attention and all issue
pages in Quest, including when multiple issues are present. Do not heat the device
for a test; validate alarm transitions with test fixtures, clearly marked simulated.
Unplug/reconnect a camera to verify OFFLINE/recovery. Disconnect the event stream
and verify alarm monitoring becomes unavailable. Check snapshot/Locate regressions.

Automated tests use synthetic event packets and mocked telemetry, not hardware.
Unity compilation and Quest interaction require the project's Unity environment.

References:
- https://developer.axis.com/vapix/network-video/event-data-streaming/
- https://developer.axis.com/vapix/network-video/event-streaming-over-websocket/
- https://developer.axis.com/vapix/network-video/event-and-action-services/
