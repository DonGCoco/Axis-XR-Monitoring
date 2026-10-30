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

Overview -> real camera status -> Details -> Locate -> waypoint guidance -> QR confirmation.

Do not use Windows ICS scripts, Hosted Network commands, Bluetooth PAN, or the old D-Link adapter on this laptop.
