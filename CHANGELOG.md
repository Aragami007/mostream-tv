# Changelog

All notable changes to MoStream TV are documented here.

## 0.4.0 - 2026-11-11

### Added

- Built-in Internet watch-party tunnel (`Share over Internet (no Hamachi / no router setup)`) powered by a pure-.NET WebSocket-to-TCP bridge and Cloudflare Quick Tunnels (`wss://*.trycloudflare.com`), allowing friends to watch together over the Internet without Hamachi or router port forwarding.
- One-time automatic `cloudflared.exe` setup in `%LOCALAPPDATA%\MoStreamTV\Runtime` on host computers (friends joining via `wss://...` links require zero extra downloads).
- IPv4-first DoH resolution and retry logic in the join tunnel client (`WebSocketTunnelClient`), plus improved `LocalAddressProvider` scoring that prioritizes real Wi-Fi/Ethernet LAN adapters over virtual VPN/Hamachi adapters.

### Fixed

- Guarded incoming chat and OSD callbacks in `consoleUI.py` before media player initialization finishes, preventing `'NoneType' object has no attribute 'chatOSDSupported'` disconnects when chat messages arrive immediately after joining.

## 0.3.0 - 2026-11-11

### Added

- **Join watch party** mode inside MoStream TV with fields for friend's server address (supports `host` or `host:port`), TCP port, room name, display name, and optional password.
- **Paste invite** helper to auto-fill server, port, room, and password from a friend's copied join details.
- Immediate error reporting and cleanup when joining with an incorrect password or unreachable server.

## 0.2.0 - 2026-11-10

### Changed

- Integrated and rebranded the watch-party client directly inside the single MoStream TV Windows Forms window using the native UI style.
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
