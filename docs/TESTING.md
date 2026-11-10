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

The 0.3.0 verification performed on Windows checked:

- Release compilation, publish (`dist\MoStreamTV.Host`), and release archive build (`Build-Release.ps1`) with zero errors.
- Host + Join two-client end-to-end test: started `SyncplayHost` + Host `SyncplayClientSession` (`WatchPartyMode.Host`) + Friend `SyncplayClientSession` (`WatchPartyMode.Join`) connecting with `vlc.exe`, verifying multi-user room presence, bidirectional chat, and clean shutdown of all processes.
- Wrong-password rejection test in `WatchPartyMode.Join`, verifying immediate failure reporting without hanging.
- Visual layout and control-bounds verification of `MainForm` in both Host and Join modes.

## Manual acceptance test

1. Host starts a watch party on Computer A and clicks **Copy join details**.
2. Friend opens MoStream TV on Computer B, switches Mode to **Join watch party**, clicks **Paste invite** (or fills **Server address**, **TCP port**, **Room name**, and **Password**), selects a local movie/player, and clicks **Join watch party**.
3. Confirm both viewers appear in **Room & participants**, chat works in both directions, and play/pause/seeking/playlist switches stay synchronized.
4. Click **Leave watch party** on Computer B and **Stop watch party** on Computer A to confirm clean teardown.

Do not use a public production port for smoke testing.
