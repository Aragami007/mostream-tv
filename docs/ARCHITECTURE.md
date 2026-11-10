# Architecture

## System shape

```text
Mostream TV Window (MainForm + WatchPartyPanel)
        |                             |
        | (Host mode)                 | (Host & Join modes)
        v                             v
  SyncplayHost                 SyncplayClientSession (JSON stdio bridge)
        |                             |            \ (when joining wss://)
        |   (Internet toggle)         |             v
        +--> WebSocketTunnelHost      |       WebSocketTunnelClient
        |      (`cloudflared` + WS)   |       (local TCP <-> ClientWebSocket)
        v                             v
syncplayServer.exe            SyncplayConsole.exe --no-gui (MOSTREAM_BRIDGE=1)
        ^                             |
        |                             v
  wss:// tunnel & LAN peers   local media player (VLC, mpv, mpv.net, MPC-HC, MPC-BE)
```

Mostream TV provides a single unified Windows Forms UI for hosting a private watch party (over the Internet without Hamachi or on a local network) and joining a friend's watch party without opening a separate Syncplay GUI window.

## Modules

### `SyncplayHost`

Used in **Host watch party** mode to manage the private server child process (`syncplayServer.exe`), arguments, password/salt environment variables, output capture, startup validation, and process-tree cleanup.

### `CloudflaredRuntime`, `WebSocketTunnelHost`, and `WebSocketTunnelClient`

Implement the built-in zero-Hamachi Internet tunnel:

- `CloudflaredRuntime` locates or performs a one-time download of the official `cloudflared.exe` binary into `%LOCALAPPDATA%\MoStreamTV\Runtime\cloudflared.exe` when the host enables **Share over Internet**.
- `WebSocketTunnelHost` opens an in-process HTTP/1.1 WebSocket upgrade bridge (`TcpListener` + `WebSocket.CreateFromStream`) forwarding traffic to `127.0.0.1:<port>` behind an ephemeral `cloudflared` Quick Tunnel (`wss://<subdomain>.trycloudflare.com`).
- `WebSocketTunnelClient` lets joining friends connect to any `wss://`, `ws://`, `https://`, or `*.trycloudflare.com` invite link using only .NET's built-in `ClientWebSocket` over an IPv4-first DoH-aware `SocketsHttpHandler`, exposing a local `127.0.0.1:<ephemeralPort>` TCP endpoint for `SyncplayConsole.exe`.

### `SyncplayRuntime`

Locates `vendor/Syncplay_1.7.6_Portable.zip`, extracts it into `%LOCALAPPDATA%\MoStreamTV\Runtime\Syncplay-1.7.6`, clears stale stored server passwords and disables GUI prompts/update checks in `syncplay.ini`, and installs the embedded `consoleUI.py` JSON stdio bridge into the extracted runtime's `lib/libsync.zip`.

### `SyncplayClientSession`

Starts `SyncplayConsole.exe --no-gui` with `MOSTREAM_BRIDGE=1` pointing to `127.0.0.1:<port>` (Host mode), a local `WebSocketTunnelClient` bridge (`wss://` Join mode), or a friend's `<server>:<port>` (LAN/direct Join mode), exchanges JSON-line commands and events over UTF-8 stdin/stdout, and exposes strongly-typed events for chat, notifications, participants, shared playlists, and playback state.

### `LocalAddressProvider`

Selects a usable private IPv4 address for same-LAN connection instructions, prioritizing physical Wi-Fi/Ethernet adapters (`192.168.*`, `10.*`, `172.16-31.*`) above virtual VPN/tunnel interfaces such as Hamachi or Hyper-V.

### `MainForm`, `WatchPartyPanel`, and `AppTheme`

Own native Windows Forms presentation, Light/Dark theming, and user interaction: Host/Join mode switching, friend server entry and clipboard invite parsing (`Paste invite`), movie selection (before or during a session, including drag-and-drop), separated **Chat** and **Activity log** panels, room & participant list, readiness/auto-play toggles, and shared playlist management.
