# Networking and security

## Internet connections without Hamachi (default host option)

When **Share over Internet (no Hamachi / no router setup)** is checked:

1. **Host computer**: `WebSocketTunnelHost` opens a local loopback WebSocket-to-TCP bridge to `syncplayServer.exe` and starts an ephemeral Cloudflare Quick Tunnel (`cloudflared.exe tunnel --url http://127.0.0.1:<bridgePort> --no-autoupdate`), producing an encrypted `wss://<random>.trycloudflare.com` URL without requiring router port forwarding, a static IP, or Hamachi.
2. **Joining friends**: No VPN client and no `cloudflared.exe` download are needed. When a friend enters or pastes the `wss://...` URL, `WebSocketTunnelClient` connects via `.NET`'s `ClientWebSocket` (using IPv4-first Cloudflare/Google DoH resolution so fresh tunnel subdomains resolve immediately even behind virtual adapters like Hamachi) and exposes a local loopback TCP bridge for `SyncplayConsole.exe`.
3. **Host privacy & security**: Traffic between the joining friend and Cloudflare edge is TLS-encrypted (`wss://`), and Cloudflare communicates with the host over an authenticated outbound `cloudflared` tunnel. Each session gets a new random subdomain and generated server password, and the tunnel closes automatically when the watch party stops.

> [!NOTE]
> The first time a host computer uses **Share over Internet**, Mostream TV downloads the official `cloudflared.exe` binary from Cloudflare's GitHub Releases into `%LOCALAPPDATA%\MoStreamTV\Runtime\cloudflared.exe`. You can uncheck **Share over Internet** at any time to run purely on your local network without downloading or starting the tunnel.

## Same LAN

Mostream TV displays a `LAN: <ip>:<port>` address alongside the tunnel URL (or under `Server:` when **Share over Internet** is unchecked), prioritizing physical Wi-Fi and Ethernet adapters over virtual VPN/tunnel adapters. Friends on the same routed network can connect directly using that IP and TCP port.

If a LAN connection fails, check:

1. Both computers are on the same LAN and client isolation is disabled.
2. The displayed address belongs to the active Wi-Fi or Ethernet adapter.
3. Windows Firewall permits inbound TCP traffic for the server (`syncplayServer.exe`).
4. No other program is already listening on the selected port.

## Direct Internet connections without the tunnel

If you uncheck **Share over Internet** and still want friends to join over the Internet without Hamachi, manual router port forwarding is required:

1. A router port-forward from the chosen external TCP port to the host's LAN address and port.
2. A stable LAN address for the host (for example a DHCP reservation).
3. A Windows Firewall inbound rule for the chosen TCP port.

Note that carrier-grade NAT (CGNAT) and double NAT prevent direct port forwarding; keeping **Share over Internet** enabled bypasses CGNAT cleanly. Direct plaintext TCP without TLS does not encrypt traffic across the public Internet.
