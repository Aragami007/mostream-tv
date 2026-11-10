# MoStream TV Host

MoStream TV starts a private watch-party server on this Windows computer, integrates playback, chat, room/participant status, readiness, and shared playlist controls directly into the MoStream TV window, displays connection details for friends, and shuts down the session cleanly when stopped or closed.

## Run from source

```powershell
dotnet run --project .\MoStreamTV.Host\MoStreamTV.Host.csproj
```

For the prepared local build, double-click `RUN_MOSTREAM_TV.bat` in the workspace root or run `dist\MoStreamTV.Host\MoStreamTV.Host.exe` directly.

The project bundles the official Syncplay 1.7.6 portable package and prepares it in `%LOCALAPPDATA%\MoStreamTV\Runtime\Syncplay-1.7.6`.

## Connecting friends

- Same Wi-Fi/LAN: friends enter the displayed `IP:port`, room name, and password.
- Over the Internet: forward the displayed TCP port in the router to the host computer and allow the application through Windows Firewall. Friends use the host's public IP address and port.
- No media is uploaded by MoStream TV. Each viewer needs their own local copy or accessible stream.

## Licensing

MoStream TV Host is licensed under Apache License 2.0. The bundled Syncplay runtime remains licensed under Apache License 2.0, with its license and third-party notices in the `licenses` directory.
