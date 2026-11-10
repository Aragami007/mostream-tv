# Changelog

All notable changes to MoStream TV are documented here.

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
