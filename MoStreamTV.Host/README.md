# MoStream TV

MoStream TV starts a private watch-party server on this Windows computer (in Host mode) or connects directly to a friend's server (in Join mode), integrating playback, chat, room/participant status, readiness, and shared playlist controls inside the single MoStream TV window.

## Run from source

```powershell
dotnet run --project .\MoStreamTV.Host\MoStreamTV.Host.csproj
```

For the prepared local build, double-click `RUN_MOSTREAM_TV.bat` in the workspace root or run `dist\MoStreamTV.Host\MoStreamTV.Host.exe` directly.

The project bundles the official Syncplay 1.7.6 portable package and prepares it in `%LOCALAPPDATA%\MoStreamTV\Runtime\Syncplay-1.7.6`.

## Connecting friends

- Same Wi-Fi/LAN: friends select **Join watch party**, enter the displayed `IP:port`, room name, and password (or click **Paste invite**).
- Over the Internet: forward the displayed TCP port in the router to the host computer and allow the application through Windows Firewall. Friends use the host's public IP address and port.
- No media is uploaded by MoStream TV. Each viewer needs their own local copy or accessible stream.

## Licensing

MoStream TV Host is licensed under Apache License 2.0. The bundled Syncplay runtime remains licensed under Apache License 2.0, with its license and third-party notices in the `licenses` directory.
