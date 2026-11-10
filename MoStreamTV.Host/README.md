# MoStream TV

MoStream TV starts a private watch-party server on this Windows computer (in Host mode) or connects directly to a friend's server (in Join mode), integrating playback, chat, room/participant status, readiness, and shared playlist controls inside the single MoStream TV window.

## Run from source

```powershell
dotnet run --project .\MoStreamTV.Host\MoStreamTV.Host.csproj
```

For the prepared local build, double-click `RUN_MOSTREAM_TV.bat` in the workspace root or run `dist\MoStreamTV.Host\MoStreamTV.Host.exe` directly.

The project bundles the official Syncplay 1.7.6 portable package in `%LOCALAPPDATA%\MoStreamTV\Runtime\Syncplay-1.7.6` and automatically downloads the official `cloudflared.exe` helper once into `%LOCALAPPDATA%\MoStreamTV\Runtime` when hosting with **Share over Internet** enabled.

## Connecting friends

- Over the Internet (no Hamachi / no router setup): keep **Share over Internet** checked when hosting and send your friends the displayed `wss://...` invite link via **Copy join details**. Friends switch to **Join watch party**, click **Paste invite**, and connect using MoStream TV's built-in WebSocket client bridge—no Hamachi or extra download required on the friend's PC.
- Same Wi-Fi/LAN: friends can enter the displayed `LAN: <ip>:<port>` address and port directly.
- No media is uploaded by MoStream TV. Each viewer needs their own local copy or accessible stream.

## Licensing

MoStream TV Host is licensed under Apache License 2.0. The bundled Syncplay runtime remains licensed under Apache License 2.0, with its license and third-party notices in the `licenses` directory.
