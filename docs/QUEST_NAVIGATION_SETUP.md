# Quest 3 QR Navigation Setup (MRUK + Runtime NavMesh)

This project now supports obstacle-avoiding navigation to wall-mounted `CAM_*` QR codes in `Assets/Scenes/SampleScene.unity`.

## 1) Quest Space Setup (required)

1. On Quest 3, complete **Space Setup / Room Scan** for the physical room.
2. Ensure walls/floor are detected and persisted on-device.
3. Enable app permissions required by MRUK scene understanding (when prompted on headset).

If no room exists, MRUK cannot provide room geometry and runtime NavMesh will fail.

## 2) Unity scene wiring (already included in SampleScene)

`CameraServices` now contains:

- `QrCameraScanner` (existing QR camera/status flow)
- `RoomNavMeshBootstrap` (waits for MRUK room data, builds runtime NavMesh)
- `QrRoutePlanner` (computes/updates path from headset to safe QR stand-off point)
- `QrRouteRenderer` is auto-added at runtime by `QrRoutePlanner` if not assigned

No XR origin/headset movement is performed by navigation scripts.

## 3) Build/run on Quest 3

1. Open project in Unity `6000.3.24f1`.
2. Open `Assets/Scenes/SampleScene.unity`.
3. Build Profile: `Android` / `Meta Quest`.
4. Ensure Quest is connected in developer mode.
5. **Build and Run**.

## 4) Runtime behavior

1. Wait for MRUK room load.
2. Scan one of the wall-mounted `CAM_*` QR codes.
3. Existing camera data/status UI still appears.
4. A floor route appears and updates as you physically walk.
5. Route hides when you arrive (horizontal distance threshold) or when target QR is removed.

## 5) Inspector tuning

Tune on `CameraServices`:

- `RoomNavMeshBootstrap`
  - `minRoomColliderCount`
  - `maxWaitForRoomSeconds`
  - `navMeshLayerMask`
- `QrRoutePlanner`
  - `standOffDistance`
  - `minWallClearance`
  - `targetSampleRadius`
  - `updateIntervalSeconds`
  - `updateMoveThreshold`
  - `arrivalDistance`

`QrRouteRenderer` (runtime-added) supports route width/color/floor offset/material.

## 6) Troubleshooting

- **No room data / no route**
  - Re-run Quest Space Setup room scan.
  - Confirm MRUK is active in scene (`MRUK` object) and logs show NavMesh build success.
- **Route missing after QR scan**
  - Confirm payload starts with `CAM_`.
  - Confirm NavMesh built (`RoomNavMeshBootstrap` log).
  - Increase `targetSampleRadius` and/or `extraDirectionalSamples`.
- **Route too close to walls**
  - Increase `minWallClearance` and `standOffDistance`.
- **Wrong side of wall QR**
  - Increase `extraDirectionalSamples` or reduce `directionSampleStepDegrees`.
