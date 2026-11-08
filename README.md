# WAVUtils

Multi-channel WAV merge and split utility for Windows.

## Features

- **Merge** — Combine multiple WAV files into a single multi-channel file. Shorter files are padded with silence.
- **Split** — Extract tracks from multi-channel WAV files using PMAL metadata chunks.
- **Typed Metadata** — Preserves bEXT, iXML, INFO, cue, smpl, and adtl chunks with full round-trip support.
- **7 Languages** — English, Russian, German, French, Spanish, Japanese, Chinese. Auto-detects OS language.
- **System Tray** — Optional background mode with balloon notifications.
- **Context Menu** — Right-click merge/split directly from Windows Explorer.
- **Portable** — Single executable, no installation required.

## Installation

### Installer
Run `WAVUtils-Setup-*.exe` (requires administrator). Installs to Program Files and adds context menu entries.

### Portable
Extract `WAVUtils-*-Portable.zip` to any folder and run `WAVUtils.exe`.

## Usage

### CLI

```bash
# Merge files
WAVUtils.exe merge file1.wav file2.wav [...fileN.wav] [output.wav]

# Split a multi-channel file
WAVUtils.exe split merged.wav
```

### Context Menu
Right-click WAV files in Explorer:
- **WAV Utils // Merge WAV** — Merge selected files
- **WAV Utils \\ Split WAV** — Split a multi-channel file

### Config

Create `config.json` next to `WAVUtils.exe`:

```json
{
  "language": "auto"
}
```

Supported values: `"auto"`, `"en"`, `"ru"`, `"de"`, `"fr"`, `"es"`, `"ja"`, `"zh"`.

## Requirements

- Windows 10/11 (64-bit)
- .NET 10.0 (included, no installation needed)

## Building

```bash
dotnet build
dotnet run --project WAVUtils.App
```

## License

MIT — Copyright (c) 2026 WTF Software
