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

The 0.1.0 verification performed on Windows checked:

- Release compilation with zero warnings and zero errors.
- SHA-256 equality between the bundled archive and the inspected official Syncplay 1.7.6 portable package.
- Startup of `syncplayServer.exe` bound to localhost on a test TCP port.
- A successful TCP connection to that listening port.
- Termination of the exact test server process.
- Visual inspection of the host form and correction of clipped controls.

## Manual acceptance test

1. Start a watch party.
2. Confirm the status changes to running and join details appear.
3. Confirm the local Syncplay client opens with localhost, room, username, and password populated.
4. Connect a second computer on the same LAN using the displayed details.
5. Verify pause, unpause, and seeking are replicated in both directions.
6. Close MoStream TV and verify the friend disconnects because the private server stopped.

Do not use a public production port for smoke testing.
