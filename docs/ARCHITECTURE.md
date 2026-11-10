# Architecture

## System shape

```text
MoStream TV Window (MainForm + WatchPartyPanel)
        |                     |
        | (Host mode only)    | (Host & Join modes)
        v                     v
  SyncplayHost        SyncplayClientSession (JSON stdio bridge)
        |                     |
        v                     v
syncplayServer.exe    SyncplayConsole.exe --no-gui (MOSTREAM_BRIDGE=1)
        ^                     |
        |                     v
   LAN/WAN peers      local media player (VLC, mpv, mpv.net, MPC-HC, MPC-BE)
```

MoStream TV provides a single unified Windows Forms UI for both hosting a private watch party and joining a friend's watch party without opening a separate Syncplay GUI window.

## Modules

### `SyncplayHost`

Used in **Host watch party** mode to manage the private server child process (`syncplayServer.exe`), arguments, password/salt environment variables, output capture, startup validation, and process-tree cleanup.

### `SyncplayRuntime`

Locates `vendor/Syncplay_1.7.6_Portable.zip`, extracts it into `%LOCALAPPDATA%\MoStreamTV\Runtime\Syncplay-1.7.6`, clears stale stored server passwords and disables GUI prompts/update checks in `syncplay.ini`, and installs the embedded `consoleUI.py` JSON stdio bridge into the extracted runtime's `lib/libsync.zip`.

### `SyncplayClientSession`

Starts `SyncplayConsole.exe --no-gui` with `MOSTREAM_BRIDGE=1` pointing either to `127.0.0.1:<port>` (Host mode) or a friend's `<server>:<port>` (Join mode), exchanges JSON-line commands and events over UTF-8 stdin/stdout, and exposes strongly-typed events for chat, notifications, participants, shared playlists, and playback state.

### `LocalAddressProvider`

Selects a usable non-loopback IPv4 address for same-LAN connection instructions. It does not perform public-IP discovery.

### `MainForm` and `WatchPartyPanel`

Own native Windows Forms presentation and user interaction: Host/Join mode switching, friend server entry and clipboard invite parsing (`Paste invite`), movie selection (before or during a session, including drag-and-drop), playback controls, activity/chat log, room & participant list, readiness/auto-play toggles, and shared playlist management.
