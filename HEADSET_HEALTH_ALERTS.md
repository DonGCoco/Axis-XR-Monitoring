# Headset camera-health alerts

The Windows proxy now publishes native Image Health Analytics issues for CAM_001 and CAM_003. This client change parses those issues, polls the camera list while the app is active, shows a small ten-second alert, and displays specific issue reasons in Overview and Details.

Behavior:
- Three consecutive abnormal samples confirm an incident; three healthy samples rearm it. Sampling waits 15 seconds after each completed request, in addition to the camera's own validation period.
- One alert per camera incident. Changing from blocked to blurred/offline does not produce another alert until stable recovery. Dismiss and automatic expiry do not rearm it.
- Inspect opens that camera's Details. Alerts are queued during onboarding and QR position registration; no scan/navigation source files are changed.
- Missing cameras, failed requests, unknown status, and unavailable required image-event streams never count as recovery. UNKNOWN uses a neutral indicator rather than green.
- Presented incident IDs are saved in local PlayerPrefs by proxy URL. If a camera recovers and fails again entirely while the app is stopped, that unobserved recovery cannot rearm the previous incident.
- Alerts require the app to be running. There is no operating-system notification when the app is closed. Independent email alerts remain on Windows.

The API client shares simultaneous list requests and caches them for four seconds to reduce repeated camera traffic. Proxy-list timeout is 60 seconds, and individual-camera timeout is 45 seconds, allowing the existing sequential telemetry reads to complete. This does not repair a disconnected network.

## Apply to the working Mac checkout

The user's working QR implementation has two locally restored source files. Do not switch branches or restore the entire project for this change. Fetch the feature branch and restore only the eight script/meta paths below, preserving local scene, package settings, APK identity and the working QR/spatial files:

```bash
git fetch origin
git restore --source=origin/feature/headset-health-alerts -- Assets/Scripts/CameraData.cs Assets/Scripts/CameraApiClient.cs Assets/Scripts/AxisMonitoringUI.cs Assets/Scripts/AxisMonitoringUI.Vapix.cs Assets/Scripts/AxisMonitoringUI.Alerts.cs Assets/Scripts/AxisMonitoringUI.Alerts.cs.meta Assets/Scripts/CameraIncidentTracker.cs Assets/Scripts/CameraIncidentTracker.cs.meta
```

Keep the existing `SampleScene`, application ID `com.dongjieru.axisxrmonitoring.floortest`, and CameraApiClient Base URL `http://192.168.1.100:5000`. Keep `Use Axis VAPIX` and embedded mock mode disabled for the lab proxy. Rebuild `Builds/AxisFloorNavigation.apk`, then install the newly built APK on the connected Quest. Do not install the old APK before rebuilding.

Windows proxy stays running with:

```powershell
py axis_proxy_multi.py --lan --image-health CAM_001 CAM_003
```

## Headset acceptance

1. App opens with three cameras; QR registration still works.
2. Leave the app in its idle view (Overview need not be open). Cover CAM_001 long enough for native validation and three proxy samples.
3. A small upper-right alert identifies CAM_001 and `Image blocked`. Inspect opens Details with that reason. Overview reports WARNING.
4. Leave it covered: no repeat after ten-second expiry, Dismiss, reopening Overview, or further issue-type changes.
5. Remove cover. Wait for all active native issues to clear and three healthy polls, then cover again: one new alert.
6. Repeat for CAM_003. Both cameras' simultaneous incidents must each display once.
7. Disconnect event streaming or the proxy: UNKNOWN/network errors must not become green or rearm an unresolved incident.

## Validation

`CameraIncidentTrackerTests` is an Editor test class using the existing reflection-based test assembly. Run in Unity Test Runner after importing its `.cs` and `.meta` paths from the feature branch. Tests cover persistent incidents, recovery hysteresis, event-stream gaps, per-camera state, persisted deduplication, and native issue JSON parsing. Full Unity compilation and on-device UX still require the Mac/Quest environment.
