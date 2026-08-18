# Cloud POS Middleware (Windows)

Windows port of the Android **Cloud POS Middleware** — a system-tray print bridge for
the Cloud POS cloud point-of-sale (`cloudpos.lk`). It links a counter PC to a
business → branch → POS terminal, discovers LAN thermal printers (mDNS + subnet scan
on port 9100), assigns them to the terminal and departments, and listens on a Pusher
channel for print jobs, converting receipt HTML to ESC/POS and sending it over raw
TCP (port 9100).

Spec: `docs/windows-port/WINDOWS_PORT_SPEC.md` (behavior contract, screens, assets).
Releasing: see `RELEASE.md` (push to `production` ⇒ auto-versioned GitHub Release).

## Projects

| Project | Target | Purpose |
|---|---|---|
| `MiddlewareApp` | `net10.0-windows` (WPF) | Tray app + 4-screen wizard UI |
| `MiddlewareApp.Core` | `net10.0` | All logic: API client, Pusher listener, HTML→ESC/POS, print queue, discovery, agent/session persistence — cross-platform & unit-testable |
| `MiddlewareApp.Core.Tests` | `net10.0` (xunit) | Tests for the receipt formatter, job rules, and print queue |

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- Windows to **run** the app. The solution also **builds** on macOS/Linux
  (`EnableWindowsTargeting`), and the Core tests run anywhere.

## Build, test, run

```
dotnet build MiddlewareApp.sln
dotnet test MiddlewareApp.Core.Tests
dotnet run --project MiddlewareApp.csproj      # Windows only
```

Start hidden in the tray (used by "Start with Windows"): `MiddlewareApp.exe --minimized`

## Configuration

Build-time constants live in `MiddlewareApp.Core/AppConfig.cs`
(`BASE_DOMAIN`, `PUSHER_KEY`, `PUSHER_CLUSTER`, `PUSHER_EVENT`).

Dev override: set `MIDDLEWARE_DEV_BASE_URL` (e.g. `http://192.168.1.50:3000`) to point
all API calls at the Express mock server from the Android repo instead of
`https://{businessCode}.cloudpos.lk`.

## Behavior notes

- Closing the window hides to the tray; printing continues. Quit via the tray menu.
- The session persists in `%APPDATA%\CloudPOSMiddleware`; on relaunch the app resumes
  listening and jumps straight to the Middleware screen.
- "Clear all & reconfigure" clears every printer server-side, stops the listener,
  wipes the stored session, and restarts the wizard.
