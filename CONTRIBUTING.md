# Contributing

Thanks for helping improve MoStream TV.

## Development setup

1. Install the .NET 8 SDK or newer on Windows.
2. Confirm `vendor/Syncplay_1.7.6_Portable.zip` is present.
3. Run `dotnet build .\MoStreamTV.Host\MoStreamTV.Host.csproj -c Release`.
4. Run the application from `MoStreamTV.Host\bin\Release\net8.0-windows\`.

## Pull requests

- Keep the server lifecycle behind `SyncplayHost` rather than placing process logic in the form.
- Do not commit secrets, personal IP addresses, router screenshots, or local configuration files.
- Preserve Syncplay's license and third-party notices when updating its bundled runtime.
- Document user-visible changes in `CHANGELOG.md`.
- Include build and relevant smoke-test results in the pull request description.

## Reporting issues

Include the MoStream TV version, Windows version, whether the friend is on LAN or the Internet, and the exact error message. Redact public IP addresses and passwords.
