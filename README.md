# Mostream TV

Mostream TV is a Windows desktop application for private, self-hosted watch parties. Everything happens inside one Mostream TV window—both for hosting a party and joining a friend's party.

It does **not** upload or stream the movie file over the server. Every participant plays their own local copy or accessible stream while Mostream TV coordinates pause, seek, and playback position.

## Host or join a watch party

1. **Quickest install (recommended for new users):** Download **`MostreamTV-Setup-...-windows-x64.exe`** from the latest GitHub Release, run it, click **Install**, and Mostream TV launches ready to use immediately—both the `.NET 8` desktop runtime and the Syncplay watch-party runtime are bundled inside.
   - Or download **`MostreamTV.Host-...-windows-x64.zip`**, extract it anywhere, and run `MostreamTV.Host.exe` without installing.
   - If you cloned or downloaded the source repository instead, install the [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/en-us/download/dotnet/8.0) and run `dist\MostreamTV.Host\MostreamTV.Host.exe` (or double-click `RUN_MOSTREAM_TV.bat`).
2. **To host a watch party:**
   - Keep **Mode** on **Host watch party** and keep **Share over Internet** checked (or uncheck it for same-Wi-Fi/LAN-only hosting).
   - Choose a room name, display name, password, TCP port, media player (VLC, mpv, mpv.net, MPC-HC, or MPC-BE), and optionally a movie file.
   - Click **Start watch party** and share the generated `wss://...` invite details (and optional `LAN:` IP) with friends using **Copy join details**.
3. **To join a friend's watch party:**
   - Select **Join watch party** under **Mode**.
   - Click **Paste invite** after copying your friend's invite details (or enter the `wss://...` link or `IP:port`, **Room name**, **Your name**, and **Password**).
   - Click **Join watch party**.
4. Chat, view room activity, toggle readiness, and manage the shared playlist directly inside Mostream TV at any time (via **Choose movie...**, **Add file(s)...**, **Add URL...**, or drag-and-drop), or switch between Light and Dark themes from the top-right header icon.

Windows 10 and Windows 11 (64-bit) are supported. On host computers using the built-in Internet tunnel for the first time, Mostream TV also downloads the official `cloudflared.exe` helper once into:

```text
%LOCALAPPDATA%\MoStreamTV\Runtime
```

This is an unsigned development build, so Windows may identify it as an unrecognized application. If your friend is not comfortable running an unsigned binary, they should build it from the reviewed source instead.

## Network requirements

### Friends over the Internet

With **Share over Internet** enabled, Mostream TV opens a temporary encrypted `wss://*.trycloudflare.com` WebSocket tunnel bridged cleanly to your private watch-party server. Friends select **Join watch party**, paste the invite link, and connect directly—even behind CGNAT or double-NAT routers.

### Friends on the same Wi-Fi or LAN

They can connect directly to the LAN address displayed by Mostream TV. Windows may ask you to allow network access the first time the server starts. Uncheck **Share over Internet** if you only want local LAN access (or use manual TCP port forwarding without the tunnel). See [Networking and security](docs/NETWORKING.md) for details.

## What happens when the app closes?

The private server (when hosting), headless watch-party engine, and launched media player session are managed by Mostream TV and shut down cleanly when you stop/leave the watch party or close the Mostream TV window.

## Build from source

Requirements:

- Windows 10 or 11
- .NET 8 SDK or newer
- `vendor/Syncplay_1.7.6_Portable.zip` from the official Syncplay release

```powershell
dotnet build .\MostreamTV.Host\MostreamTV.Host.csproj -c Release
dotnet publish .\MostreamTV.Host\MostreamTV.Host.csproj -c Release -o .\dist\MostreamTV.Host
```

Create a friend-ready ZIP containing the runtime and licenses:

```powershell
.\scripts\Build-Release.ps1
```

## Project documentation

- [Architecture](docs/ARCHITECTURE.md)
- [Networking and security](docs/NETWORKING.md)
- [Testing](docs/TESTING.md)
- [Contributing](CONTRIBUTING.md)
- [Security policy](SECURITY.md)
- [Changelog](CHANGELOG.md)

## Upstream and licensing

Mostream TV bundles the Syncplay 1.7.6 runtime for underlying synchronization and media-player control. Syncplay is licensed under Apache License 2.0. Its license and third-party notices are preserved in `MostreamTV.Host/licenses/`.

Mostream TV source code is also distributed under Apache License 2.0. See [LICENSE](LICENSE) and [NOTICE](NOTICE).

Made by Mohit and Kausthuba

Syncplay project: <https://github.com/Syncplay/syncplay>
