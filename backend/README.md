# Axis API Proxy

One Python proxy serves all Axis cameras to Unity.

Current mapping:
- CAM_001 -> AXIS Q1728 -> https://169.254.96.234 -> camera01
- CAM_002 -> AXIS Q1809-LE -> https://169.254.52.210 -> camera02
- CAM_003 -> reserved / disabled

Create `secrets.json` beside the script by copying `secrets.json.example` and filling each local camera password. `secrets.json` is ignored by Git.

Run on the lab Windows PC:

```cmd
python axis_proxy.py --check CAM_002
python axis_proxy.py --lan
```

Unity uses one base URL, e.g. `http://192.168.137.1:5000`.

Endpoints:
- `GET /health`
- `GET /cameras`
- `GET /camera/CAM_001`
- `GET /camera/CAM_002`

If a configured camera is physically unavailable, the proxy returns that camera as `OFFLINE` rather than failing the whole `/cameras` response.
