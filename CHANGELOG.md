# Changelog

All notable changes to Mostream TV are documented here.

## 0.5.2 - 2026-11-11

### Changed

- Updated branding, window/dialog titles, assembly product metadata, embedded runtime messages, documentation, and project/output filenames to **Mostream TV** (`MostreamTV.Host`).

## 0.5.1 - 2026-11-11

### Fixed

- Completed Dark theme coverage across all controls in both idle and active states (`ThemedListView` for **Room & participants** and **Shared playlist**, `ThemedComboBox` for room selection and dropdown button, `ThemedNumericUpDown` for **TCP port** and **Min** spin buttons/text, `ThemedCheckButton` for **I'm ready to watch!**, and read-only locked inputs in `_chatInput` and `MainForm` when inactive/running).
- Fixed text warping, bottom clipping, and horizontal truncation on `Choose movie...`, `Choose player...`, and other compact buttons at 125%+ Windows DPI by custom-painting centered single-line button captions in Dark Mode (`AppTheme.DrawButtonFace`), scaling control/button sizes from actual font metrics (`AppTheme.Scale`, `RecommendedInputHeight`, `MeasureButtonWidth`), switching setup label columns and rows to `AutoSize`, and aligning readiness row controls cleanly on a single centerline.

## 0.5.0 - 2026-11-11

### Changed

- Removed the inline playback transport bar (`Play`, `Pause`, `Seek`, `Undo`, `Open movie`, `Open URL`) and instructional status banner from the **Watch party** panel.
- Separated **Chat** and **Activity log** into dedicated stacked panels so viewer conversation and sync/room events remain easy to follow independently.
- Removed unnecessary "no Hamachi needed" / "no router setup" promotional wording from the application UI.
- Applied custom Mostream TV branding (`Assets\mostream.ico` and `Assets\mostream-32.png` / `Assets\mostream-16.png`) across the window icon, header bar, dialogs, and compiled executable.
- Added Light and Dark themes (`AppTheme`) persisted across launches, toggled via a subtle icon-only button in the top-right header bar.

## 0.4.1 - 2026-11-11

### Fixed

- Fixed forward seeks reverting to the pre-seek timestamp (`Rewinded due to time difference`) when watching MP4 files in VLC over an Internet tunnel:
  - Updated `consoleUI.py` so incoming remote seeks update Syncplay's cached player position immediately instead of echoing the stale pre-seek position alongside the `ignoringOnTheFly` server acknowledgment while VLC is still seeking asynchronously.
  - Filtered transient stale pre-seek `position` replies from VLC's Lua interface until VLC finishes jumping near the commanded target (or times out).
  - Suppressed spurious non-seek desync rewinds/slowdowns for a brief settle window immediately after the local viewer initiates a seek, while preserving incoming remote seeks and post-settle drift correction.

## 0.4.0 - 2026-11-11

### Added

- Built-in Internet watch-party tunnel (`Share over Internet (no Hamachi / no router setup)`) powered by a pure-.NET WebSocket-to-TCP bridge and Cloudflare Quick Tunnels (`wss://*.trycloudflare.com`), allowing friends to watch together over the Internet without Hamachi or router port forwarding.
- One-time automatic `cloudflared.exe` setup in `%LOCALAPPDATA%\MoStreamTV\Runtime` on host computers (friends joining via `wss://...` links require zero extra downloads).
- IPv4-first DoH resolution and retry logic in the join tunnel client (`WebSocketTunnelClient`), plus improved `LocalAddressProvider` scoring that prioritizes real Wi-Fi/Ethernet LAN adapters over virtual VPN/Hamachi adapters.

### Fixed

- Guarded incoming chat and OSD callbacks in `consoleUI.py` before media player initialization finishes, preventing `'NoneType' object has no attribute 'chatOSDSupported'` disconnects when chat messages arrive immediately after joining.

## 0.3.0 - 2026-11-11

### Added

- **Join watch party** mode inside Mostream TV with fields for friend's server address (supports `host` or `host:port`), TCP port, room name, display name, and optional password.
- **Paste invite** helper to auto-fill server, port, room, and password from a friend's copied join details.
- Immediate error reporting and cleanup when joining with an incorrect password or unreachable server.

## 0.2.0 - 2026-11-10

### Changed

- Integrated and rebranded the watch-party client directly inside the single Mostream TV Windows Forms window using the native UI style.
- Removed the separate Syncplay GUI launch so no standalone Syncplay window opens.
- Added in-app movie selection, stream URL opening, playback controls (play, pause, seek, undo seek), activity/chat feed, room/participant list, readiness and auto-play controls, shared playlist management, and drag-and-drop file support.

## 0.1.0 - 2026-11-10

### Added

- Windows host UI for configuring a private room, username, password, and TCP port.
- Lifecycle-managed Syncplay server that stops when the host application closes.
- Automatic launch of the local Syncplay client using the private server.
- LAN connection details with one-click clipboard copying.
- Bundled and license-preserved Syncplay 1.7.6 portable runtime.
- Windows build workflow and project documentation.
