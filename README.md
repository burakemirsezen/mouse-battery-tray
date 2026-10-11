# Mouse Battery Tray

A tiny Windows tray app that displays your Logitech mouse battery percentage beside the clock.

It reads the battery value from Logitech G HUB's local background agent. No account, administrator access, drivers, or configuration required.

## Use

1. Install Logitech G HUB and leave its background agent running. Its main window can stay closed.
2. Download `MouseBatteryTray.exe` from [Releases](https://github.com/burakemirsezen/mouse-battery-tray/releases/latest).
3. Put it somewhere permanent and double-click it. The tray icon shows the percentage.
4. If Windows puts the icon in the tray overflow, drag it beside the clock or enable it in taskbar settings.

Right-click the icon to **Refresh now**, toggle **Start with Windows**, or **Quit**. Double-click also refreshes. Startup is off by default; move the executable before enabling it. Disable and re-enable startup if you move it later.

Startup uses a shortcut in your Windows Startup folder and launches when you sign in. Existing startup registrations from older versions are migrated automatically. You can also enable it with `MouseBatteryTray.exe --startup on` or disable it with `--startup off`.

The rounded badge has a small battery level bar and adapts to light/dark taskbars. The digits turn amber at 20% and red at 10%. Green digits with a lightning bolt mean charging. Gray digits mean the last known reading while the mouse sleeps/disconnects or G HUB is unavailable; hover for its status. `?` appears only if no reading has been obtained yet. Right-click also shows when the app last checked.

## Compatibility

- Windows 10/11, x64. The release executable includes .NET; no runtime installation is needed.
- Tested on a **PRO X SUPERLIGHT** over a LIGHTSPEED receiver.
- Other G HUB mice exposing battery status should work but are not hardware-tested.
- If several battery mice are connected, the first active one reported by G HUB is displayed.
- A background worker checks every five seconds, automatically retries failed reads, and refreshes immediately after Windows resumes. Manual refresh wakes the same worker; requests never overlap.
- Keeps a clearly marked last known value during temporary failures and automatically restores the live value on recovery.
- Shows the value G HUB reports, including its cached value for a sleeping/inactive mouse (marked in the tooltip).
- Requires G HUB's local API. Logitech Options/Options+ and generic Bluetooth battery reporting are not supported.
- G HUB's API is undocumented and may change in a future version.

## Build

Install the .NET 9 SDK on Windows, then run:

```powershell
./build.ps1
```

The script runs the protocol tests and creates the standalone x64 executable.

Run `dist/MouseBatteryTray.exe`. For a one-shot diagnostic (without a tray icon):

```powershell
Start-Process ./dist/MouseBatteryTray.exe -ArgumentList '--check', './battery.check.json' -Wait
Get-Content ./battery.check.json
```

The diagnostic exit code is 0 for a battery reading, 2 if no reading is available, or 1 for a connection/protocol error.

## How it works

The app connects to `ws://127.0.0.1:9010` with WebSocket subprotocol `json`. It sends only `GET` requests:

```json
{"path":"/devices/list","verb":"GET","msgId":"unique-request-id"}
{"path":"/battery/<device-id>/state","verb":"GET","msgId":"unique-request-id"}
```

It selects a `MOUSE` from `payload.deviceInfos` with `capabilities.hasBatteryStatus`, then reads `payload.percentage` and `payload.charging`. It supports fragmented WebSocket messages and limits message size and request time. A single background worker owns the reads and posts updates to the tray's UI thread. Its in-memory last reading remains visible in gray through temporary failures. It never changes mouse settings. It makes no internet requests and stores no device identifiers or telemetry during normal use. The optional startup setting is a shortcut in the current user's Startup folder; it does not require administrator rights.

For developer troubleshooting, `--observe <file>` writes each applied UI update as JSON lines to the supplied file. This explicit diagnostic mode records the display name, percentage, status, and time, and is off by default.

The protocol was verified against the installed G HUB app and a live mouse. REA's ASAR analysis provided a partial graph; a focused trace did not resolve the battery route in that graph. Direct inspection of the packaged JavaScript found the endpoint and route, and a live read confirmed the response fields. No Logitech code or assets are distributed in this repository.

MIT licensed. Unofficial project, not affiliated with Logitech.
