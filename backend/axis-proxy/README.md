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
