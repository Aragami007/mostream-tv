# Architecture

## System shape

```text
MoStream TV Window (MainForm + WatchPartyPanel)
        |                     |
        v                     v
  SyncplayHost        SyncplayClientSession (JSON stdio bridge)
        |                     |
        v                     v
syncplayServer.exe    SyncplayConsole.exe --no-gui (MOSTREAM_BRIDGE=1)
        ^                     |
        |                     v
   LAN/WAN peers      local media player (VLC, mpv, mpv.net, MPC-HC, MPC-BE)
```

MoStream TV provides a single unified Windows Forms UI for hosting and participating in a watch party without opening a separate Syncplay GUI window.

## Modules

### `SyncplayHost`

Manages the private server child process (`syncplayServer.exe`), arguments, password/salt environment variables, output capture, startup validation, and process-tree cleanup.

### `SyncplayRuntime`

Locates `vendor/Syncplay_1.7.6_Portable.zip`, extracts it into `%LOCALAPPDATA%\MoStreamTV\Runtime\Syncplay-1.7.6`, disables first-run GUI prompts/update checks in `syncplay.ini`, and installs the embedded `consoleUI.py` JSON stdio bridge into the extracted runtime's `lib/libsync.zip`.

### `SyncplayClientSession`

Starts `SyncplayConsole.exe --no-gui` with `MOSTREAM_BRIDGE=1` (no console window, no Qt GUI), exchanges JSON-line commands and events over UTF-8 stdin/stdout, and exposes strongly-typed events for chat, notifications, participants, shared playlists, and playback state.

### `LocalAddressProvider`

Selects a usable non-loopback IPv4 address for same-LAN connection instructions. It does not perform public-IP discovery.

### `MainForm` and `WatchPartyPanel`

Own native Windows Forms presentation and user interaction: watch-party setup, movie selection (before or during a session, including drag-and-drop), playback controls, activity/chat log, room & participant list, readiness/auto-play toggles, and shared playlist management.

## Trust and lifecycle

- The packaged Syncplay archive in `vendor/` remains unmodified with its upstream identity and notices preserved.
- The server binds IPv4 on `0.0.0.0` so LAN peers can connect.
- Stopping the watch party or closing `MainForm` gracefully stops both the client/player session and the server process tree.
