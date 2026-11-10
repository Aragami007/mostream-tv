# MoStream TV

MoStream TV is a small Windows host application for private, self-hosted watch parties. It starts a Syncplay-compatible server on your computer, opens your local Syncplay client, shows the connection details to share with friends, and stops the server when the host window closes.

It does **not** upload or stream the movie. Every participant plays their own local copy or accessible stream while Syncplay coordinates pause, seek, and playback position.

## Download and test

1. Download this repository using **Code → Download ZIP** and extract it.
2. Run `dist\MoStreamTV.Host\MoStreamTV.Host.exe`, or double-click `RUN_MOSTREAM_TV.bat`.
3. Choose a room name, display name, password, and TCP port.
4. Select **Start watch party**.
5. Send your friends the displayed server address, room, and password.

The first launch extracts the bundled Syncplay 1.7.6 runtime to:

```text
%LOCALAPPDATA%\MoStreamTV\Runtime\Syncplay-1.7.6
```

MoStream TV requires the .NET 8 Desktop Runtime on Windows. Windows 10 and Windows 11 are the initial supported platforms.

This is an unsigned development build, so Windows may identify it as an unrecognized application. If your friend is not comfortable running an unsigned binary, they should build it from the reviewed source instead.

## Network requirements

### Friends on the same Wi-Fi or LAN

They can connect directly to the LAN address displayed by MoStream TV. Windows may ask you to allow network access the first time the server starts.

### Friends over the Internet

You must forward the selected **TCP** port in your router to the host computer and allow that port through the host firewall. Friends then use your public IP address and port.

The current MVP does not automate router configuration, firewall rules, dynamic DNS, or TLS. It is suitable for trusted LAN testing but is not yet an Internet-hardened deployment. See [Networking and security](docs/NETWORKING.md) before exposing it publicly.

## What happens when the app closes?

The private server is a child process owned by MoStream TV. Closing the host window terminates that server process tree. The media player and local Syncplay client are intentionally left open.

## Build from source

Requirements:

- Windows 10 or 11
- .NET 8 SDK or newer
- `vendor/Syncplay_1.7.6_Portable.zip` from the official Syncplay release

```powershell
dotnet build .\MoStreamTV.Host\MoStreamTV.Host.csproj -c Release
dotnet publish .\MoStreamTV.Host\MoStreamTV.Host.csproj -c Release -o .\dist\MoStreamTV.Host
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

MoStream TV is an independent host/launcher built around the unmodified Syncplay runtime. Syncplay is licensed under Apache License 2.0. Its license and third-party notices are preserved in `MoStreamTV.Host/licenses/`.

MoStream TV source code is also distributed under Apache License 2.0. See [LICENSE](LICENSE) and [NOTICE](NOTICE).

Syncplay project: <https://github.com/Syncplay/syncplay>
