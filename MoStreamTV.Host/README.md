# MoStream TV Host

MoStream TV Host starts a private Syncplay server on this Windows computer, opens the local Syncplay client, displays the connection details for friends, and stops the server when the host window closes.

## Run from source

```powershell
dotnet run --project .\MoStreamTV.Host\MoStreamTV.Host.csproj
```

For the prepared local build, double-click `RUN_MOSTREAM_TV.bat` in the workspace root or run `dist\MoStreamTV.Host\MoStreamTV.Host.exe` directly.

The project bundles the official Syncplay 1.7.6 portable package. The first launch extracts it to `%LOCALAPPDATA%\MoStreamTV\Runtime\Syncplay-1.7.6`.

## Connecting friends

- Same Wi-Fi/LAN: friends enter the displayed `IP:port`, room name, and password in Syncplay.
- Over the Internet: forward the displayed TCP port in the router to the host computer and allow the application through Windows Firewall. Friends use the host's public IP address and port.
- No media is uploaded by MoStream TV or Syncplay. Each viewer needs their own local copy or accessible stream.

The host process is owned by MoStream TV and is terminated when the application closes. The media player and local Syncplay client are intentionally left open.

## Licensing

MoStream TV Host is an independent launcher. The bundled Syncplay runtime remains licensed under Apache License 2.0, with its license and third-party notices in the `licenses` directory.
