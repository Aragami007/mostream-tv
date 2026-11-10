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

The 0.4.1 verification performed on Windows verified:

- Release compilation (`dotnet build -c Release`), publish (`dist\MoStreamTV.Host`), and ZIP packaging (`Build-Release.ps1`) with zero warnings or errors.
- Two-client VLC seek-stability verification on a 2.5-hour `.mp4` over simulated 150 ms tunnel latency (`+20s` seek, `34:22` jump, rapid `+10s` x3 jumps, `12:00` backward jump, and remote `45:00` jump while playing, plus mixed patched/unpatched friend interoperability), confirming zero unexpected rewinds or bounce-backs.
- End-to-end zero-Hamachi watch-party test: started `SyncplayHost` + `WebSocketTunnelHost` (`wss://*.trycloudflare.com`) + Host `SyncplayClientSession` + Friend `SyncplayClientSession` connecting through a live Cloudflare Quick Tunnel with `vlc.exe`, verifying multi-user presence, bidirectional chat, and clean shutdown.
- Visual layout and control-bounds verification of `MainForm` in both Host and Join modes.

## Manual acceptance test

1. Host starts a watch party on Computer A with **Share over Internet (no Hamachi / no router setup)** checked and clicks **Copy join details**.
2. Friend opens MoStream TV on Computer B, switches Mode to **Join watch party**, clicks **Paste invite** (or fills **Server address** with the `wss://...` link or `IP:port`, **Room name**, and **Password**), selects a local movie/player, and clicks **Join watch party**.
3. Confirm both viewers appear in **Room & participants**, chat works in both directions, and play/pause/seeking/playlist switches stay synchronized without Hamachi.
4. Click **Leave watch party** on Computer B and **Stop watch party** on Computer A to confirm clean teardown.

Do not use a public production port for smoke testing.
