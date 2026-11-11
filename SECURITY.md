# Security policy

## Supported versions

Only the latest release of Mostream TV receives security fixes. Please update to the [latest release](https://github.com/Aragami007/mostream-tv/releases/latest) before reporting an issue.

| Version | Supported |
|---|---|
| 0.6.x (latest) | ✅ Yes |
| < 0.6 | ❌ No |

## Current security posture

Mostream TV is an early-stage, unsigned development build intended for private watch parties between people who trust each other. Please read the points below before using it.

### What Mostream TV does to protect you

- **No media is uploaded.** Mostream TV only exchanges synchronization commands (play, pause, seek, readiness), playlist entries, and chat. Movie files never leave your computer.
- **Encrypted Internet connections.** With **Share over Internet** enabled, friends connect to the host through a TLS-encrypted `wss://<random>.trycloudflare.com` Cloudflare Quick Tunnel. Cloudflare reaches the host through an authenticated outbound `cloudflared` connection, so no router ports are opened.
- **Per-session links and passwords.** Each watch party gets a new random tunnel subdomain and a generated room password. The tunnel closes automatically when the watch party stops or the app closes.
- **Local-only server binding behind the tunnel.** The tunnel forwards to a loopback (`127.0.0.1`) WebSocket bridge in front of the private Syncplay server.
- **Clean process lifecycle.** The server, tunnel, sync engine, and media player run as child processes scoped to a Windows Job Object and are terminated when Mostream TV exits.
- **Official upstream binaries.** `cloudflared.exe` is downloaded only from Cloudflare's official GitHub Releases, and the bundled Syncplay 1.7.6 runtime comes from the official Syncplay release.

### Known limitations

- **Unsigned build.** Release binaries are not code-signed, so Windows SmartScreen may warn about them. If you prefer not to run unsigned binaries, build from the reviewed source.
- **Anyone with the invite can join.** The `wss://` link, room name, and password together grant access to the room. Share join details only with people you trust, and start a new party (which creates a new link and password) if they leak.
- **Cloudflare sees tunnel traffic.** Quick Tunnel traffic terminates at Cloudflare's edge. It contains sync commands and chat, not media, but you should not send sensitive information in chat.
- **LAN and manual port-forwarding connections are not encrypted.** Direct `IP:port` connections (with **Share over Internet** turned off) use plaintext TCP. Use them only on trusted networks; for Internet use, keep the tunnel enabled.
- **Password visibility to local processes.** The server password is passed to the Syncplay server through a child-process environment variable, and to the local Syncplay client through its command-line interface. Other sufficiently privileged processes on the same computer may be able to read it.
- **No rate limiting or automatic updates.** Mostream TV does not currently rate-limit password attempts or update itself. Check the Releases page for new versions.
- **Firewall and router changes are manual.** Mostream TV does not configure Windows Firewall or router port forwarding for you. Windows may ask to allow network access the first time a server starts.

See [Networking and security](docs/NETWORKING.md) for more detail on how connections work.

## Reporting a vulnerability

Please **do not** open a public issue for security problems.

1. Use GitHub's private vulnerability reporting: go to the repository's **Security** tab → **Report a vulnerability**.
2. Include:
   - the affected Mostream TV version and Windows version,
   - whether the connection used the Internet tunnel, LAN, or manual port forwarding,
   - step-by-step reproduction instructions,
   - the impact you observed, and
   - any proposed mitigation.
3. **Never** include real passwords, invite links, public IP addresses, or working proof-of-concept exploit details in public places.

We will acknowledge your report as soon as possible and keep you informed while a fix is prepared.
