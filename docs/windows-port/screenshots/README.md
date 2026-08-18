# Reference screenshots for the Windows port

Captured from the app's **own React Native code rendered via Expo web** (react-native-web) at a
420×760 viewport (@2x), driven through the real wizard flow against a mock Cloud POS backend
(`DEMO123` business, Restaurant location, Kitchen/Bar departments). Layout, colors, spacing, and
copy are the real thing; only two caveats:

- Fonts are the browser defaults, not Android Roboto — treat typography sizes/weights in the spec
  (`../WINDOWS_PORT_SPEC.md` §6) as authoritative, these images as layout reference.
- Printer discovery results (EPSON/Star chips) were injected data — browsers can't open raw TCP or
  mDNS — but the chip UI, selection states, and save flows are the real components and real PATCH
  round-trips. The Pusher listener in shots 05–07 is genuinely connected to the production cluster.

## Included

| File | State |
|---|---|
| `01-business-code.png` | Screen 1 — business code entry (idle) |
| `02-location-list.png` | Screen 2 — branch list (avatar initials, chevrons, Back) |
| `03-device-list.png` | Screen 3 — device list |
| `04-middleware-loading.png` | Screen 4 — initial config load (spinner, red "Connecting…" badge, listener OFF) |
| `05-middleware-unconfigured.png` | Screen 4 — nothing assigned; overview rows "Not set", discovery chips, manual IP rows |
| `06-middleware-configured.png` | Screen 4 — terminal + Kitchen configured: green badges, assigned boxes, selected chip, "Update printer", listener ON |
| `07-config-card-manual-ip.png` | Terminal ConfigCard with a manual IP typed (focused input) |

## Not capturable in this setup (describe-only, see spec)

- `08` / `09` — the "Remove printer" and "Clear all & reconfigure" **confirm dialogs**: native OS
  alert dialogs (Material on Android). On Windows use native/OS-styled message boxes with the exact
  title/body/button texts from spec §6.4.
- `10` — the Android **foreground-service notification**; its Windows equivalent is the tray
  icon/tooltip described in spec §8.

To re-capture from a real Android device instead: `adb exec-out screencap -p > file.png` per state.
