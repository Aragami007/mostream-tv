# Architecture

## System shape

```text
MoStream TV Host UI
        |
        v
SyncplayHost module ---- SyncplayRuntime module
        |                         |
        v                         v
syncplayServer.exe         bundled portable runtime
        ^
        |
Syncplay clients <----> local media-player adapters
```

MoStream TV owns orchestration, not playback synchronization. The upstream Syncplay runtime remains unchanged and replaceable.

## Modules

### `SyncplayHost`

Its interface exposes start, stop, local-client launch, running status, and server-exit notification. Its implementation hides child-process arguments, environment variables, output capture, failure detection, and process-tree cleanup.

### `SyncplayRuntime`

Locates the bundled archive, extracts it once into the user's local application-data directory, and exposes the expected client/server executable paths.

### `LocalAddressProvider`

Selects a usable non-loopback IPv4 address for same-LAN connection instructions. It does not perform public-IP discovery.

### `MainForm`

Owns presentation and user interaction. It delegates server lifecycle and runtime preparation to the modules above.

## Trust and lifecycle

- The packaged Syncplay archive is retained with its upstream identity and notices.
- The server binds IPv4 on `0.0.0.0` so LAN peers can connect.
- Closing the form awaits termination of the owned server process tree.
- Syncplay and the media player are not terminated with the host window.
