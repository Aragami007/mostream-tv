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

The 0.2.0 verification performed on Windows checked:

- Release compilation and publish (`dist\MoStreamTV.Host`) with zero errors.
- Startup of `syncplayServer.exe` bound to localhost on a test TCP port.
- Startup of `SyncplayClientSession` via `SyncplayConsole.exe --no-gui` with `MOSTREAM_BRIDGE=1` and a real `vlc.exe` media player without launching any separate Syncplay GUI window.
- Adding files to the shared playlist, switching playlist items, toggling readiness, sending chat messages, and toggling playback state.
- Clean teardown of `vlc.exe`, `SyncplayConsole.exe`, and `syncplayServer.exe` on stop.
- Visual layout and control-bounds verification of `MainForm` and `WatchPartyPanel`.

## Manual acceptance test

1. Launch `MoStreamTV.Host.exe` (or `RUN_MOSTREAM_TV.bat`).
2. Confirm your media player is detected (or choose one) and optionally pick a movie file.
3. Click **Start watch party** and confirm no separate Syncplay window opens.
4. Confirm the status changes to running, join details appear, and your media player opens.
5. Add or change movie files from within MoStream TV (via **Choose movie...**, **Open movie...**, **Add file(s)...**, or drag-and-drop).
6. Connect a second computer on the same LAN using the displayed details and verify pause, unpause, seeking, readiness, chat, and playlist sync work in both directions.
7. Click **Stop watch party** or close MoStream TV and verify the session and private server stop cleanly.

Do not use a public production port for smoke testing.
