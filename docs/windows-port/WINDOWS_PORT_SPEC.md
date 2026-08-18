# Cloud POS Middleware — Windows Port Specification

> **Purpose of this document**: A complete, self-contained brief for building a **Windows system-tray application** that replicates — pixel-for-pixel where possible, behavior-for-behavior always — the existing Android app "Cloud POS Middleware" (this repository). An agent given only this document, the `assets/` folder next to it, and the `screenshots/` folder should be able to build the Windows app without reading the Android source.

---

## 1. What the app is

**Cloud POS Middleware** is a print bridge for the Cloud POS cloud point-of-sale (`cloudpos.lk`). It runs on a device at a store counter and:

1. Links the device to a Cloud POS **business → location (branch) → POS terminal (device)** via a 3-step setup wizard.
2. Lets staff **discover LAN thermal printers** (mDNS + subnet TCP scan on port 9100) or enter a printer IP manually.
3. Lets staff **assign printers** to (a) the terminal itself (full customer receipts) and (b) each **department** (kitchen/station tickets, "KOTs").
4. **Listens in the background** (Pusher WebSocket channel) for print jobs pushed by the Cloud POS backend, converts the job's receipt **HTML into ESC/POS**, and sends it over **raw TCP (port 9100)** to the right printer.
5. Persists its session so it **resumes listening automatically on relaunch**, and stays alive while the user works in other apps (on Android: a foreground service; **on Windows: the tray-resident app**).

**Windows target**: same UI/UX and functionality, delivered as a desktop app that lives in the **system tray** (notification area). Closing the window minimizes to tray; printing continues. Recommended stack: **Electron** (or Tauri) — anything with: raw TCP sockets, mDNS, WebSocket (pusher-js works in any JS runtime), local storage, tray APIs, and autostart. The rest of this spec is stack-agnostic.

---

## 2. Configuration

All config is build-time/env, no login or API keys needed (the tenant API is unauthenticated, addressed by business code):

| Setting | Production value | Purpose |
|---|---|---|
| `BASE_DOMAIN` | `cloudpos.lk` | Tenant API: `https://{businessCode}.{BASE_DOMAIN}` |
| `PUSHER_KEY` | `72e6aeaeb45fc01084ad` | Pusher app key |
| `PUSHER_CLUSTER` | `ap1` | Pusher cluster |
| `PUSHER_EVENT` | `LOCATION_COMMANDS` (prod); empty ⇒ bind all events and filter by payload `command` | Laravel broadcast event name |

If `PUSHER_KEY` is missing, the app must still run but show: *"Set PUSHER_KEY to enable the print listener."* and report listener state "failed".

---

## 3. Backend API contract (must be reproduced exactly)

Base URL per tenant: `https://{businessCode}.{BASE_DOMAIN}` where `businessCode` is lower-cased and trimmed (e.g. code `peck-cafe` → `https://peck-cafe.cloudpos.lk`). All requests send `Accept: application/json`. Non-2xx ⇒ show the error to the user (Android shows `Server {status}: {body}` in an alert). Network failure ⇒ error message `Cannot reach:\n{url}\n\n({message})`.

### 3.1 GET `/api/v1/locations`
Returns locations + their devices. Response shape (unwrap `json.data.locations ?? json.data ?? []`):

```json
{ "data": { "locations": [
  { "id": 1, "code": "APLOC1", "name": "Apparel", "city": "Colombo",
    "devices": [
      { "id": 1, "device_name": "Apparel Terminal", "serial_number": "SN123",
        "device_status": "active", "location_id": 1 }
    ] }
] } }
```

### 3.2 GET `/api/v1/locations/{locationId}/devices/{deviceId}/print-config`
Returns current printer assignment for the terminal and the location's departments. `json.data`:

```json
{ "device": { "id": 1, "name": "Apparel Terminal",
              "is_middleware_configured": true,
              "print_config": { "ip": "192.168.1.101", "port": 9100, "paper_size": "80mm" } },
  "departments": [
    { "id": 4, "name": "Kitchen", "is_middleware_configured": false, "print_config": null }
  ] }
```

`print_config` is `null` when unassigned. A **200 response drives the "Middleware Connected" badge** (see §6.4). This endpoint is **polled every 60 s** while the Middleware screen is active (silent refresh — no spinner).

### 3.3 PATCH `/api/v1/locations/{locationId}/devices/{deviceId}/print-config`
Saves the **full** state every time (device + all departments — not a delta). Body = same shape as the GET response's `data`. Rules the client follows when building the payload:

- **Assign printer to terminal**: `device.is_middleware_configured = true`, `device.print_config = {ip, port, paper_size:"80mm"}`; all departments echoed unchanged.
- **Assign printer to department N**: department N gets `is_middleware_configured: true` + the new `print_config`; device and other departments echoed unchanged.
- **Remove printer from a slot**: that slot gets `is_middleware_configured: false`, `print_config: null`; everything else echoed.
- **Clear all & reconfigure**: device **and every department** get `false`/`null` in one PATCH, then the agent stops and the wizard restarts at step 1.

Paper size is currently always sent as `"80mm"` (no UI to choose; keep it that way).

### 3.4 Test server
`server/` in the Android repo has an Express mock (`node server.js`, port 3000, business code `DEMO123`) with the same routes plus a `POST /:location_slug/middleware` that relays a job — useful for local development of the Windows app too. Note it serves plain HTTP on an IP, so give the Windows app a dev override that allows `http://` + host:port base URLs (the Android app hardcodes `https://{code}.{domain}`; improving this for dev is fine, production behavior must stay `https` subdomain).

---

## 4. Real-time print jobs (Pusher)

- Library: `pusher-js` (works in Node/Electron as-is). Options: `{ cluster, forceTLS: true }`.
- **Channel name**: `merchant.{businessCode}.location.{locationId}` — businessCode lower-cased/trimmed. Public channel, no auth.
- **Event binding**: if `PUSHER_EVENT` is set, bind both `LOCATION_COMMANDS` **and** `.LOCATION_COMMANDS` (Laravel dot-prefix variant). If not set, bind **all** events (`bind_global`), ignore events starting with `pusher:`, and filter by payload.
- **Connection states** surfaced to the UI: `disconnected | connecting | connected | failed | unavailable` (bind to pusher `connection` events `connected`, `disconnected`, `unavailable`, `failed`, `error`).
- Starting a listener always tears down any previous one first (unbind all, unsubscribe, disconnect).

### 4.1 Job payload

```json
{ "command": "PRINT_RECEIPT",
  "terminal_id": 1,
  "department_id": null,
  "html": "<html>…receipt markup…</html>" }
```

Payload may arrive as a JSON string or object; may be wrapped — unwrap in this order: if it has `command`, use as-is; else try `.data`, then `.message`, else use raw. HTML may be under `html` or `HTML`.

### 4.2 Job handling rules (exact order)

1. **Command filter**: handle only `PRINT`, `PRINT_RECEIPT`, `PRINT_KOT`, or anything starting with `PRINT_` (case-insensitive). Otherwise result: `Ignored command: {command}`.
2. **Terminal filter**: if the job has a `terminal_id` and it ≠ this device's selected id → skip with message `Skipped — terminal_id {x} ≠ this device {y}`. Jobs with `terminal_id: null` are handled by everyone on the channel.
3. **Empty HTML** → fail: `Print job has empty HTML`.
4. **Printer resolution**: if `department_id` is set, use that department's `print_config`; if that department has none, **fall back to the terminal's** printer; if neither → fail: `No printer configured. Set IP for terminal/department in middleware first.`
5. Convert HTML → ESC/POS (see §5) and send via the **per-printer queue** (§5.3). Success message: `Printed to {ip}:{port}`.
6. Every result (ok or not) updates the UI's "Last job:" line.

---

## 5. Printing pipeline

### 5.1 HTML → thermal text conversion
The Android app converts receipt HTML to the DantSu formatted-text dialect (`[L]`/`[C]`/`[R]` line prefixes) and lets the native lib render it to ESC/POS. **On Windows, port the same converter** (it's pure JS — `services/receiptFormat.ts`, no RN imports; it can be copied verbatim) and then render the formatted lines to raw ESC/POS bytes yourself (or use an npm ESC/POS lib). Conversion rules that must be preserved:

- Extract `<body>`, strip `<script>`/`<style>`; walk block tags in document order.
- `chars per line`: **48** for 80 mm paper, **32** for 58 mm. Effective print width: 72 mm for 80 mm rolls, 48 mm for 58 mm (centering is computed against this).
- Text inside elements whose `class` contains `header`/`footer`, inside `h1–h4`, or lines starting with "Powered by" → **centered**; all other text left-aligned, word-wrapped.
- `<hr>` and separator table rows (cells all `-_=.*` chars or empty) → full-width dash line (max 48 dashes, centered). Collapse consecutive dash lines.
- `<table>`: each row's **last cell right-aligned on the same line** as the joined left cells (single left column padded with spaces — never 50/50 columns). Long left text wraps; the right value sits on the last wrapped line if it fits, else on its own right-aligned line.
- `<img>` with an `http(s)` src: print centered logo image (max **2 distinct images** per receipt). Before printing, check each image URL is reachable (HEAD then GET, 4 s timeout) and drop unreachable ones. If the print fails **and** the payload contains a logo, retry once with all images stripped (bill must still print if logo rendering breaks).
- Strip the characters `< > [ ]` from all text (they are markup in the thermal dialect); collapse whitespace; decode HTML entities.
- Append **6 blank lines** at the end so the footer clears the cutter, then **auto-cut** and feed (~90 mm on Android; match visually).
- If the incoming content has no HTML tags at all, print it as plain left-aligned wrapped text.

### 5.2 Transport
Raw TCP socket to `{ip}:{port}` (default port 9100), 30 s timeout, printer DPI 203. On Windows use `net.Socket`.

### 5.3 Per-printer serial queue
Jobs targeting the **same `host:port` run strictly one after another** with a **400 ms gap** after each job (lets the cutter finish); different printers print in parallel. A failed job must not block subsequent jobs on that printer.

---

## 6. UI/UX — screens

The app is a **single window, wizard-style flow of 4 screens** with in-memory navigation (no URL routing): `Business code → Location → Device → Middleware`. Window sizing for Windows: portrait-ish content column (Android is a phone); recommend a fixed ~**420 × 760** window, content scrolls vertically, page padding 20 px horizontal / 10 px top / 30 px bottom. **Every screen shows the brand logo** (`assets/brand.png`, rendered ~180×60, contained, centered, ~40 px top margin, 20 px below).

### 6.0 Design tokens (light theme — the app is light-only)

```
bg        #f2f4f7   (page background)
card      #ffffff
border    #e5e7eb
primary   #0F62FE   (Cloud POS blue — also the brand/adaptive-icon color)
danger    #e74c3c
success   #22c55e   (a second green #2ecc71 is used for the "Configured" badge)
text      #111827
textMuted #6b7280
textDim   #9ca3af
```

Shared components:
- **Primary button**: primary bg, white bold 15 px text, radius 12, vertical padding 15, full width; shows an inline white spinner while busy; 50–60 % opacity when disabled. Danger variant = same with `danger` bg.
- **Back button**: outlined (1 px border color `border`), radius 12, muted 14 px text `‹ Back`.
- **Text input**: white card bg, 1 px `border`, radius 12, padding 16/14, 15 px text, muted placeholder.
- **List item (locations/devices)**: white card, radius 12, 1 px border, row layout, 16 px padding, 10 px gap between items; leading **40 px round avatar** in primary blue with white bold 13 px initials; title 16 px semibold, subtitle 13 px muted; trailing `›` chevron in muted.
- **Status badge (pill)**: white pill, radius 20, 12/6 padding, leading 8 px colored dot + 13 px semibold colored text.
- **Section heading**: 18 px bold text + 13 px muted description line under it.
- **Greeting pattern** (screens 1–3): centered 26 px bold **"Good Afternoon!"** + centered 14 px muted sub-line. (Static string in the Android app — keep it, or compute by time of day if desired; default: keep identical.)

See `screenshots/` for reference captures of every screen and key state (7 PNGs; provenance and caveats in `screenshots/README.md`).

### 6.1 Screen 1 — Business code
- Brand logo, greeting, sub-line: *"Please enter your business code to get started!"*
- Label `BUSINESS CODE` style: 12 px semibold muted, letter-spacing 0.5.
- One text input, placeholder *"Enter your business code here"*, **masked like a password**, no autocapitalize/autocorrect.
- `Continue` primary button → validates non-empty (alert *"Enter a business code"*), calls **GET /locations**, stores the code + locations, advances. Spinner replaces the label while loading. API error → alert dialog titled "Error" with the message.

### 6.2 Screen 2 — Select branch
- Greeting + sub-line *"Please select your branch to continue ›"*.
- List of locations: avatar = first 2 letters of `location.code` uppercased; title `name`; subtitle `city`. Click selects and advances.
- `‹ Back` returns to screen 1.
- Empty state: if a business has no locations the list is simply empty — add a gentle empty message on Windows (improvement, not in Android).

### 6.3 Screen 3 — Select device
- Same layout; sub-line *"Please select your device to continue ›"*.
- Items: avatar = first 2 letters of the **location** code; title `device_name`; subtitle `#{serial_number}`. Click selects and advances to Middleware.
- `‹ Back` to screen 2.

### 6.4 Screen 4 — Middleware (the main screen)
Top to bottom:

1. **Brand logo.**
2. **Connection badge** (pill): green dot + green *"Middleware Connected"* when the last print-config GET succeeded; red dot + red *"Connecting…"* otherwise. Driven by the initial load and the 60 s poll.
3. **Print-agent banner** (listener status): pill with dot colored by Pusher state — green=connected, blue/primary=connecting, red=otherwise — and text:
   - *"Starting background listener…"* (+ small spinner) while starting
   - *"Background listener ON (safe to use Chrome)"* when running & connected — for Windows reword to *"Background listener ON (safe to close this window)"*
   - *"Print listener connecting…"* / *"Print listener OFF"*
   Below the pill, two small muted 12 px lines when available: `Channel: merchant.{code}.location.{id}` and `Last job: {message}` (updates with every job result — this is the primary print feedback).
   (Android also shows an "Allow unrestricted battery" link here; Windows equivalent: a **"Start with Windows"** toggle/link.)
4. **Breadcrumb**: `{businessCode} › {locationName} › {deviceName}` — 13 px, muted, last item dark semibold.
5. While the first config load runs: a large centered spinner (primary color). Then:
6. **"Assigned printers" overview card** — heading *"Assigned printers"* + description *"Terminal and department printers used for print jobs."*. One card, hairline-divided rows: left = bold 14 px name + muted kind line (*"Terminal"* or *"Department · {id}"*); right = `ip:port` semibold + paper size muted, or muted *"Not set"*. If nothing at all assigned: *"No printers assigned yet."*
7. **Terminal section** — heading = device name, description *"Full receipt printer for orders done at this terminal."*, then a **ConfigCard** (below).
8. **Departments section** (only if any) — heading *"Departments"*, description *"Kitchen / station printers routed by department_id."*, then one ConfigCard per department.
9. **Scan bar**: card row with spinner (while scanning) or green dot, muted text — while scanning: *"Scanning mDNS + subnet (port 9100)…"*; idle: *"{n} printer(s) found on network"* + a primary **Re-scan** link.
10. **`Clear all & reconfigure`** danger button → confirm dialog: title "Reconfigure", body *"This clears every printer for this terminal and all departments, then restarts setup. To remove only one printer, use Remove printer on that card."*, buttons Cancel / **Clear all & restart** (destructive). On confirm: PATCH clearing everything, stop the agent, return to screen 1 (store reset).
11. **`‹ Back`** to screen 3.

#### ConfigCard (one per printable slot: "device" or "dept-{id}")
- Header row: title (15 px bold) + right-aligned pill badge: **"Configured"** (green text `#2ecc71` on ~12 % green bg) or **"Not set"** (muted on faint bg).
- If a printer is assigned: an inset "assigned" box (page-bg fill, radius 10, 1 px border): tiny muted label *"Assigned printer"*, `ip:port` 16 px bold, *"Paper: 80mm"* muted, and a small outlined **"Remove printer"** button (danger text, danger-tinted border) → confirm dialog *"Remove the assigned printer from {name}?"* Cancel/Remove(destructive) → PATCH with that slot nulled → reload. Otherwise the line *"No printer assigned yet."*
- Sub-label *"Select printer"* (or *"Change printer"* when one is assigned), then the **printer picker**:
  - While scanning with no results yet: small spinner + status text.
  - Found printers as a **horizontal chip row**: chip = name (12 px semibold) over `ip:port · source` (10 px), page-bg with border; **selected chip** = primary bg, white text. Selection is **per-card** (each slot has its own selection); a slot with an assigned printer pre-selects its IP.
  - If scan finished with none found: *"No printers found on network. Enter IP manually below or tap Re-scan."*
  - **Manual IP row**: label *"Manual IP (if scan fails)"*, IP input (placeholder `192.168.1.100`, flexible width) + port input (placeholder `9100`, ~72 px, centered text), and a **"Use this IP"** primary text-link → validates IPv4 (alert *"Invalid IP"* / *"Enter a valid IPv4 address (e.g. 192.168.1.100)"*), adds it to the shared printer list as `Manual {ip}` (source `manual`), selects it for this card, confirms with *"Printer {ip}:{port} is ready to configure."* Manual fields are also **per-card**.
- Footer: primary button **"Save printer"** / **"Update printer"** (spinner while saving; disabled while saving/clearing). No selection → alert *"Select a printer first"*. Success → alert *"Saved" / "Printer configured successfully"* then reload config.

Port used when saving = the discovered/entered port for that IP (fallback 9100).

### 6.5 Boot / resume screen
On launch, show a centered spinner on `bg` while checking for a persisted enabled session. If found: restore state (business code, location id+name, device id+name, saved print configs), **start the listener immediately**, and jump straight to the Middleware screen. Otherwise show screen 1.

---

## 7. Printer discovery

Runs automatically when the Middleware screen opens, and on **Re-scan**. Both methods run in parallel, streaming results into one deduplicated list (dedupe by IP; keep first-seen name/port; abortable when leaving the screen):

1. **mDNS/Bonjour**: browse `_pdl-datastream._tcp`, `_printer._tcp`, `_ipp._tcp` (Android also had a legacy `_http._tcp` helper — skip it) for ~6 s; each resolved service → `{name: service.name || ip, ip: first address, port: service.port || 9100, source: "zeroconf"}`. On Windows use e.g. `bonjour-service`.
2. **Subnet TCP probe**: get the machine's LAN IPv4, take its /24 (`a.b.c`), probe `a.b.c.1`–`a.b.c.254` on port **9100** with an **800 ms** connect timeout and **24-way concurrency**; open port → `{name: "Printer {ip}", ip, port: 9100, source: "subnet"}`. If the LAN IP can't be determined, skip silently (log a warning).

---

## 8. Background behavior — Android ↔ Windows mapping

| Android (current) | Windows (build this) |
|---|---|
| Foreground service + persistent notification "Cloud POS Middleware — Listening · {device}" (blue `#0F62FE` accent) | **Tray icon** (use `assets/Icon cloudpos.png`) with tooltip `Cloud POS Middleware — Listening · {device}`; tray menu: Open, listener status line (disabled item), Start with Windows (checkbox), Quit |
| Closing/backgrounding the app keeps the service alive | **Close button hides to tray** (app keeps running & printing); Quit only via tray menu. First hide → balloon/toast: "Still listening for print jobs" |
| Keep-awake (screen stays on) | Not needed; optionally `powerSaveBlocker` so sleep doesn't kill the socket — but reconnect handles resume anyway |
| On app-state → active, reconnect Pusher if disconnected/failed | On window focus/restore **and on OS resume-from-sleep + network-change events**, reconnect if state is `disconnected`/`failed` |
| "Allow unrestricted battery" deep-link | "Start with Windows" (auto-launch at login, start minimized to tray) |
| Session persisted in AsyncStorage keys `mwire.agent.session.v1` / `mwire.agent.configs.v1` | Same JSON blobs in local storage (electron-store or a JSON file in `%APPDATA%`) |

Persisted session shape:

```json
{ "businessCode": "peck-cafe", "locationId": 1, "locationName": "Apparel",
  "deviceId": 1, "deviceName": "Apparel Terminal", "enabled": true }
```

Persisted configs shape = `{ device, departments, selectedDeviceId }` (same objects as the print-config GET, plus the selected device id). Both are **saved every time the agent starts or configs refresh**, and cleared on stop/reconfigure. `enabled: false` or missing configs ⇒ do not auto-resume.

**Agent lifecycle rules** (port exactly):
- `startPrintAgent` is called after every successful print-config load. If the same business/location/device agent is already running, just refresh configs + persistence (don't reconnect). Otherwise: persist, start listener, start tray/background presence.
- The agent is **not stopped when leaving the Middleware screen** — only by "Clear all & reconfigure" (or Quit).
- On job results and connection changes, update the banner + tray tooltip live.

---

## 9. Suggested Windows project shape

Keep the same separation so logic stays testable:

```
src/
  main/            (Electron main: tray, window, autostart, TCP, mDNS, IPC)
  services/
    api.ts             ← port of services/api.ts (identical contract)
    pusherService.ts   ← port (pusher-js works unchanged; swap RN import for plain 'pusher-js')
    printJobHandler.ts ← port verbatim (pure logic)
    printQueue.ts      ← port verbatim (pure logic)
    receiptFormat.ts   ← copy verbatim (pure JS) + new escpos renderer for [L]/[C]/[R] + <img>
    printerDiscovery.ts← reimplement probes with net.Socket + bonjour-service
    agentStorage.ts    ← same keys/shapes on electron-store
    printAgent.ts      ← port; replace FGS with tray presence
  renderer/
    screens/ (BusinessCode, Location, Device, Middleware), store (zustand), styles (tokens above)
```

`services/receiptFormat.ts`, `printJobHandler.ts`, `printQueue.ts` from this repo are dependency-free TypeScript — copy them as-is and unit-test them (e.g. the sample HTML flow in `scripts/print-html-job.cjs`). `scripts/test-print.js` (plain Node) already proves the TCP/ESC-POS path from a desktop.

## 10. Acceptance checklist

- [ ] Wizard: business code (masked input) → branch list → device list → middleware; Back works at every step; state resets on "Clear all & reconfigure".
- [ ] Middleware screen matches §6.4: connected badge (60 s poll), listener banner with channel + last-job lines, breadcrumb, overview card, terminal ConfigCard, department ConfigCards, scan bar with Re-scan, danger reconfigure, Back.
- [ ] Discovery: mDNS + full /24 probe stream chips live; manual IPv4 entry with validation; per-card selection.
- [ ] Save/Update/Remove printer each send the full PATCH payload per §3.3 and reload; all confirm dialogs and alert texts match.
- [ ] Job handling passes §4.2 rule-by-rule (command filter, terminal filter, department routing with terminal fallback, queue serialization per printer with 400 ms gap, logo fallback retry).
- [ ] Receipt output visually matches Android output for the same HTML (48 cols/80 mm, 32 cols/58 mm, right-aligned amounts, centered headers/footers, dash separators, logo, 6 trailing blank lines + cut).
- [ ] Tray behavior: close-to-tray, tooltip status, Quit, autostart with Windows (minimized), auto-resume of a persisted session on launch, reconnect on resume/network change.
- [ ] Loading, empty, and error states everywhere per §6 (spinners in buttons, "No printers found…", alert dialogs); fully keyboard-operable (tab order, Enter submits business code).

---

*Source of truth: this spec was extracted from the Android repo `android-pos-middleware` @ commit `1f3f17c` (app v1.2.0). Reference screenshots: `screenshots/`. Brand assets: `assets/brand.png` (logo), `assets/Icon cloudpos.png` (icon/tray, brand blue `#0F62FE`).*
