# Testing

## Automated build check

```powershell
dotnet build .\MostreamTV.Host\MostreamTV.Host.csproj -c Release
```

The GitHub Actions workflow runs this build on `windows-latest` and publishes the compiled directory as a workflow artifact.

To build the same friend-ready ZIP used for releases:

```powershell
.\scripts\Build-Release.ps1
```

## Local smoke test

The current Windows verification verified:

- Release compilation (`dotnet build -c Release`) for both `MostreamTV.Host` and `MostreamTV.Installer`, framework-dependent publish (`dist\MostreamTV.Host`), pruned untrimmed self-contained `win-x64` portable ZIP packaging (`MostreamTV.Host-0.6.2-windows-x64.zip`), and LZX-compressed Windows installer (`MostreamTV-Setup-0.6.2-windows-x64.exe`) with zero warnings or errors.
- Live UI Automation smoke testing against the installed self-contained `MostreamTV.Host.exe`, verifying multi-player auto-detection (`VLC media player` + `Windows Media Player` + synthetic multi-player paths) and persistent dropdown selection, watch-party startup (`Share over Internet`) with both `VLC` and synchronized **Windows Media Player** (`WmpPlayerForm` / `wmp.dll`), **Copy join details** (`Clipboard.SetText` without crashing), clipboard contents (`Server:` / `Room:` / `Password:`), clean shutdown (`Stop watch party` and window close with zero stray child processes), and orphaned process / TCP port-conflict protection.
- Clean silent install/uninstall cycle of `MostreamTV-Setup-0.6.2-windows-x64.exe` into an isolated directory, verifying installed `MostreamTV.Host.exe` launches immediately, Start Menu shortcut & uninstall registration are created cleanly, `Uninstall Mostream TV.exe` is ~66 KB, and `--uninstall --silent` removes all installed files cleanly.
- Live headless `syncplayServer.exe` + `SyncplayConsole.exe --no-gui` (`MOSTREAM_BRIDGE=1` with `consoleUI.py` and `vlc.exe`) connection and clean shutdown using the stripped Syncplay 1.7.6 runtime.
- UI layout and visual inspection across **Host** (idle and active) and **Join** modes in both **Light** and **Dark** themes, plus simulated 125% (`12.5pt`) font scaling, confirming full Dark styling on disabled `ListView`s, `ComboBox`, `NumericUpDown` spin buttons, and `CheckButton`, as well as centered, unclipped captions across all buttons and labels.
- End-to-end watch-party smoke test: started `SyncplayHost` + `WebSocketTunnelHost` (`wss://*.trycloudflare.com`) + Host `SyncplayClientSession` + Friend `SyncplayClientSession` connecting through a live Cloudflare Quick Tunnel with `vlc.exe`, verifying multi-user presence, separate **Chat** and **Activity log** event routing, and clean shutdown.

## Manual acceptance test

1. Host starts a watch party on Computer A with **Share over Internet** checked and clicks **Copy join details**.
2. Friend opens Mostream TV on Computer B, switches Mode to **Join watch party**, clicks **Paste invite** (or fills **Server address** with the `wss://...` link or `IP:port`, **Room name**, and **Password**), selects a local movie/player, and clicks **Join watch party**.
3. Confirm both viewers appear in **Room & participants**, messages appear in **Chat** while room/sync events appear in **Activity log**, and toggling the header theme icon cleanly switches between Light and Dark themes.
4. Click **Leave watch party** on Computer B and **Stop watch party** on Computer A to confirm clean teardown.

Do not use a public production port for smoke testing.
