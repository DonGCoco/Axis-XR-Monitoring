# Camera locations — Quest 3 pilot

## What is included

The existing **SampleScene** is the app entry point. Camera details now offer **Locate**, **Register position**, and **Locations**. Existing Meta Interaction SDK buttons/rays handle selection; no custom ray, controller-button mapper or extra Input Actions asset is needed. Keep Active Input Handling set to **Input System Package (New)**.

CameraSpatialAnchors uses the installed Meta XR Core 205 APIs: SaveAnchorAsync, LoadUnboundAnchorsAsync, UnboundAnchor.LocalizeAsync / TryGetPose / BindTo, and EraseAnchorsAsync. It stores camera ID → UUID in separate local Home and Lab registries, not raw Unity positions. A restored anchor must be localized and actively tracked before guidance is shown. The overview's priority layout never supplies physical positions.

## Pull and build

1. Close Unity; preserve local work before switching. Do not discard local scene/package changes.
2. Fetch origin. The implementation branch is `feature/camera-spatial-anchors`.
   If a local branch with that name already exists, switch to it and merge `origin/feature/camera-spatial-anchors` after committing/stashing local changes. Do not force reset.
3. Open the project and **Assets/Scenes/SampleScene.unity**. Select only SampleScene in the active Android/Meta Quest build profile's Scene List (profile overrides may differ from the shared list).
4. Build And Run to Quest 3. Allow requested spatial-data permission. Anchor support and passthrough boundary suppression are configured in the committed project/scene.
5. The previously created SpatialAnchorTest and SpatialAnchorInput are not needed by this workflow. Do not add sample controller mapper blocks to SampleScene.

## Register at home

1. Open Overview → select CAM_001 → Register position. The active site defaults to **Home**.
2. Pick up the **right Quest controller**. A blue label follows its tracked position. Hand-ray/pinch UI continues to use the existing SDK; placement itself currently requires the right controller.
3. Select **Save in 3 seconds**, then put the blue marker at the physical test target and hold still. The sampled position is the controller location at the end of the countdown, not its position when the button was clicked. If the controller is not tracked, saving is refused.
4. Wait for creation and persistence. Success returns to details. Failed saves leave registration open with the error, and preserve a previously registered binding.
5. Choose Locate. Guidance shows left/right/ahead/behind, 3D straight-line distance and a marker at the target. This is not an obstacle-aware route.
6. Scan CAM_001: the existing API client obtains its data and opens details. Scan CAM_002 while targeting CAM_001: show mismatch and retain guidance without requesting CAM_002 details.

For this first pilot use a reachable tabletop camera/QR substitute. Placement is manual; scanning a QR does not register a position. Do not climb or infer a ceiling camera position from the controller pose. Remote placement for inaccessible cameras is not implemented.

## Persistence, recovery and school setup

- Exit the app completely, reopen in the same room, look around, then Locate CAM_001. If restoring fails, use Locations → Retry restore. No guidance is shown from stale/untracked poses.
- At school use Locations → Home / Lab to activate **Lab**, then register the laboratory positions. The same camera ID can have different Home and Lab bindings.
- Re-register position replaces only that camera in the active site. New anchor persistence and the ID binding are committed before erasing the old anchor.
- Locations → Clear site → Confirm clear erases only the active site's registered anchors. It does not call the sample's global erase-all function.
- Local storage is per headset/app installation. Uninstalling or clearing app data can remove the UUID registry. No multi-headset sharing is included.

## Acceptance checks (device required)

| Check | Expected result |
|---|---|
| First use without saved location | Position not registered; no direction/distance |
| Registration with right controller untracked | Save refused; retry available |
| Successful save and walk around | Target marker remains at registered location |
| Exit/relaunch and restore | Same physical target recovers, or explicit unavailable state |
| Tracking lost during Locate | Marker and direction/distance disappear |
| Wrong QR during Locate | Mismatch; target remains active; no wrong-device details |
| Correct QR during Locate | Identity confirmed; existing mock/API details open |
| Cancel while API request is pending | Late response cannot reopen target details |
| Re-register same ID | New location survives restart; old binding replaced |
| Home/Lab switch | Only active site's bindings can guide |
| Clear current site | Confirmation required; other site retained |
| Original onboarding, Overview, Help and pinch | Continue to work in SampleScene |

## Validation scope

Five Editor tests in `Assets/Tests/Editor` cover wrong-QR rejection, cancellation of pending responses, registration/save scan gating, mismatched responses and unavailable positions. Run them with Window → General → Test Runner → EditMode. They have not been executed in this container.

C# syntax parsing and a whitespace/diff check passed in the container; these are not a Unity compilation.

SDK API signatures were checked against the actual installed-version package source downloaded from Meta's registry. A container cannot validate Quest localization, permission prompts, physical tracking or Android/Unity build behavior. Run the device checklist before treating this branch as demo-ready.
