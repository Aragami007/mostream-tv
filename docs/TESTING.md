# Testing

## Automated build check

```powershell
dotnet build .\MoStreamTV.Host\MoStreamTV.Host.csproj -c Release
```

The GitHub Actions workflow runs this build on `windows-latest` and publishes the compiled directory as a workflow artifact.

To build the same friend-ready ZIP used for releases:

```powershell
.\scripts\Build-Release.ps1
```

## Local smoke test

The 0.4.0 verification performed on Windows verified:

- Release compilation (`dotnet build -c Release`) and ZIP packaging (`Build-Release.ps1`) with zero warnings or errors.
- Automatic one-time `cloudflared.exe` download to `%LOCALAPPDATA%\MoStreamTV\Runtime\cloudflared.exe` (`CloudflaredRuntime.EnsureAvailableAsync`) and IPv4 LAN prioritization (`LocalAddressProvider`).
- End-to-end zero-Hamachi watch-party test: started `SyncplayHost` + `WebSocketTunnelHost` (`wss://*.trycloudflare.com`) + Host `SyncplayClientSession` + Friend `SyncplayClientSession` connecting through the Cloudflare Quick Tunnel with `vlc.exe`, verifying multi-user presence, bidirectional chat (including early chat arrival before media player init finishes), and clean shutdown.
- Visual layout and control-bounds verification of `MainForm` in both Host and Join modes.

## Manual acceptance test

1. Host starts a watch party on Computer A with **Share over Internet (no Hamachi / no router setup)** checked and clicks **Copy join details**.
2. Friend opens MoStream TV on Computer B, switches Mode to **Join watch party**, clicks **Paste invite** (or fills **Server address** with the `wss://...` link or `IP:port`, **Room name**, and **Password**), selects a local movie/player, and clicks **Join watch party**.
3. Confirm both viewers appear in **Room & participants**, chat works in both directions, and play/pause/seeking/playlist switches stay synchronized without Hamachi.
4. Click **Leave watch party** on Computer B and **Stop watch party** on Computer A to confirm clean teardown.

Do not use a public production port for smoke testing.
