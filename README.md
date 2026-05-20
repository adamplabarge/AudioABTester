# AudioABTester

AudioABTester is a Windows 11 desktop app for critical A/B listening. It loads two audio files, decodes both into a shared PCM format, starts them on the same timeline, and switches between A and B by changing gain only. Playback does not restart during comparison.

## Stack

- C#
- .NET 8
- WPF
- MVVM
- NAudio
- WASAPI shared output

## Architecture

- `AudioEngine` owns a single `WasapiOut` instance and a single composite `ISampleProvider`.
- `AudioTrack` caches decoded PCM samples in the output device's mix format so both tracks share the same sample rate and channel layout.
- A single frame cursor is advanced by the output callback, which keeps both tracks sample-aligned over time.
- A/B switching changes gain only; it never stops, seeks, or recreates playback.
- `VolumeMatchService` is a placeholder for future RMS/LUFS loudness matching.

## Supported Formats

- `.wav`
- `.mp3`
- `.flac`
- `.aiff`

`AudioFileReader` relies on Windows decoding support for formats such as FLAC.

## Controls

- `Load A`, `Load B`
- `Start`, `Pause`, `Stop`
- `Listen A`, `Listen B`
- Slider scrubber

Keyboard shortcuts:

- `Space`: Play/Pause
- `X`: Toggle A/B
- `Left Arrow`: Rewind 5 seconds
- `Right Arrow`: Forward 5 seconds

## Build

Install the .NET 8 SDK, then run:

```bash
dotnet restore
dotnet build
```

## Publish

Release publishes are configured for self-contained, single-file `win-x64` output.

```bash
dotnet publish -c Release
```

The executable will be produced under `AudioABTester/bin/Release/net8.0-windows/win-x64/publish/`.

## Windows Installer (EXE)

Use Inno Setup to create a traditional Windows installer that your friend can run.

1. Install Inno Setup (6.x):
	- https://jrsoftware.org/isinfo.php
2. Publish the app to a stable folder:

```bash
dotnet publish AudioABTester/AudioABTester.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o installer/publish
```

3. Open `installer/AudioABTester.iss` in Inno Setup Compiler.
4. Build the installer from the Inno Setup UI (Build -> Compile).
5. The output installer will be created in `installer/output/`.

What the installer does:

- Installs to Program Files
- Adds Start Menu and optional Desktop shortcuts
- Supports uninstall from Apps & Features
- Includes all published files required to run

Tip: Keep version numbers in `installer/AudioABTester.iss` updated for each release.

## GitHub Cloud Build (Actions)

Yes. This repository now includes a GitHub Actions workflow that builds in the cloud on Windows, publishes the app, compiles the Inno Setup installer, and uploads both as artifacts.

Workflow file:

- `.github/workflows/build-installer.yml`

How to run it:

1. Push your changes to GitHub.
2. In GitHub, open Actions -> Build Windows Installer.
3. Click Run workflow (manual trigger), or push a tag like `v1.0.0`.
4. After it finishes, download artifacts:
	- `AudioABTester-installer` (setup EXE)
	- `AudioABTester-publish` (published app files)

The workflow uses `windows-latest`, installs .NET 8 SDK, installs Inno Setup via Chocolatey, and compiles `installer/AudioABTester.iss`.

## Notes

- The current design is ready for blind A/B/X workflows because the transport and source-selection logic are already separated.
- Waveform rendering, loudness matching, markers, and playlist support can be layered on top of the existing engine and view-model boundaries.
- This repository was generated without local `dotnet` validation because the current environment does not have the .NET SDK installed.

## Open Source

- License: MIT (see `LICENSE`)
- Contributions: see `CONTRIBUTING.md`
- Community expectations: see `CODE_OF_CONDUCT.md`