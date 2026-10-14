# VoicePTT 2.1

VoicePTT is a free Windows microphone and push-to-talk utility. It is written in C# and WPF, and ships as a self-contained executable with an Inno Setup installer.

## Features

- Voice activity automatically holds a configurable PTT key while you speak.
- Tap-to-talk starts with a hotkey and stays open until silence.
- Hold-to-talk maps any configured hotkey to the PTT output key.
- Separate activation and release levels, release delay, live microphone meter.
- Global hotkey recording, including Scroll Lock and key combinations.
- Microphone selection, tray menu, optional launch at sign-in, local settings.
- No account, remote audio processing, trial, or paid tier.
- Animated terminal startup, scanline treatment, and glitch accents.

## Build

Install the .NET 10 SDK and publish from the repository root:

```powershell
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish
```

The project uses NAudio 2.4.0 for Windows audio capture. Install Inno Setup 6, then compile `VoicePTT.iss` to create `dist/VoicePTT-Setup-2.1.0.exe`. The installer includes the quick start guide and third party notices from `docs/`.

Set the matching PTT key in your game or chat app. Scroll Lock works as an input hotkey, including in key combinations.

Settings are stored in `%APPDATA%\VoicePTT\settings.json`; settings from the earlier Python release are read on first launch. Windows may show a SmartScreen warning because this build is unsigned.

## License

VoicePTT is released under the MIT license. NAudio is distributed under its own license; see `docs/VoicePTT-Third-Party-Notices.txt`.
