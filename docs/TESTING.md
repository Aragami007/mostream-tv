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

The 0.5.1 verification performed on Windows verified:

- Release compilation (`dotnet build -c Release`), publish (`dist\MoStreamTV.Host`), and ZIP packaging (`Build-Release.ps1`) with zero warnings or errors.
- UI layout and visual inspection across **Host** (idle and active) and **Join** modes in both **Light** and **Dark** themes, plus simulated 125% (`12.5pt`) font scaling, confirming full Dark styling on disabled `ListView`s, `ComboBox`, `NumericUpDown` spin buttons, and `CheckButton`, as well as centered, unclipped captions across all buttons and labels.
- End-to-end watch-party smoke test: started `SyncplayHost` + `WebSocketTunnelHost` (`wss://*.trycloudflare.com`) + Host `SyncplayClientSession` + Friend `SyncplayClientSession` connecting through a live Cloudflare Quick Tunnel with `vlc.exe`, verifying multi-user presence, separate **Chat** and **Activity log** event routing, and clean shutdown.

## Manual acceptance test

1. Host starts a watch party on Computer A with **Share over Internet** checked and clicks **Copy join details**.
2. Friend opens MoStream TV on Computer B, switches Mode to **Join watch party**, clicks **Paste invite** (or fills **Server address** with the `wss://...` link or `IP:port`, **Room name**, and **Password**), selects a local movie/player, and clicks **Join watch party**.
3. Confirm both viewers appear in **Room & participants**, messages appear in **Chat** while room/sync events appear in **Activity log**, and toggling the header theme icon cleanly switches between Light and Dark themes.
4. Click **Leave watch party** on Computer B and **Stop watch party** on Computer A to confirm clean teardown.

Do not use a public production port for smoke testing.
