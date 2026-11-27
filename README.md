# WAVUtils

WAVUtils merges and splits multichannel WAV files and stamps audio timecode
(LTC) into Broadcast Wave metadata on Windows.

## Installation

- **Installer:** Close WAVUtils and WAVUtils Tray, then run
  `WAVUtils-Setup-*.exe` and approve Windows elevation. Setup installs both
  applications for the computer. Open **WAVUtils** from the Start menu (or from
  the optional desktop shortcut) to start Tray. Use `WAVUtils.exe` for WAV commands.
- **Portable:** Extract `WAVUtils-*-Portable.zip`. Use `WAVUtils.exe` for WAV
  operations and optionally start `WAVUtils.Tray.exe` for update notifications.

Both packages bundle the .NET runtime for x64 Windows.

### Configure Explorer and Tray for your account

Open Windows PowerShell without administrator rights in the installed or
extracted WAVUtils folder. Choose only the features you want to change:

| Feature | Enable | Disable |
| --- | --- | --- |
| Explorer context menu | `.\Install.ps1` | `.\Uninstall.ps1` |
| Start Tray at Windows sign-in | `.\Install.ps1 -AutoStart` | `.\Uninstall.ps1 -AutoStart` |

If the default `Restricted` policy blocks scripts on a computer you manage,
start a session with `powershell.exe -NoProfile -ExecutionPolicy RemoteSigned`
and run the command there. Review any downloaded script before
[unblocking that file](https://learn.microsoft.com/en-us/powershell/module/microsoft.powershell.core/about/about_execution_policies?view=powershell-5.1#manage-signed-and-unsigned-scripts).
This doesn't change your account or computer policy. On a managed device,
follow your organization's policy.

Check the context menu by right-clicking a WAV file in Explorer. If you enabled
autostart, Tray appears in the notification area the next time you sign in.
Disable the features from your account before removing or moving the files. If
you move WAVUtils or choose a different copy, enable the features from that
folder to update the paths. Settings created by older Setup versions stay in
the Windows account where they were created; Setup doesn't migrate them. After
an in-place upgrade, the uninstaller can still replay older registry cleanup
entries for the account that runs it.

## Merge and split WAV files

To merge two tracks and restore them to a separate folder, run:

```powershell
.\WAVUtils.exe merge "track 1.wav" "track 2.wav" --out "merged.wav"
.\WAVUtils.exe split "merged.wav" --output-dir "restored"
```

- Merge keeps input channel order and pads shorter tracks with silence. Inputs
  need matching sample rates, encodings, bit depths, and valid bits. Supported
  encodings are PCM (8, 16, 24, or 32 bits) and IEEE float (32 or 64 bits),
  including their WAVE_FORMAT_EXTENSIBLE forms. Output switches to RF64 when
  it exceeds the RIFF size limit.
- Without `--out`, merge puts a timestamped WAV in the first input's folder.
  The old positional output syntax still works if there are at least two
  existing input files followed by a path that does not exist. Use `--out`
  to avoid ambiguity.
- Split needs a merge file with WAVUtils' `pmal` track metadata; it is not a
  general-purpose channel extractor. Split restores original track lengths,
  formats, and metadata. Without `--output-dir`, it writes next to the merged
  WAV. Use an empty destination folder if the original track files are there.
- Neither command overwrites existing files. Cancelling split can leave
  completed tracks if publication has already started. Both commands keep
  their result or error visible until you close the progress window.

If you added the Explorer entries, select WAV files and right-click
**Объединить WAV** (Merge WAV), or right-click a WAVUtils merge file and select
**Разъединить WAV** (Split WAV).

## Stamp audio timecode

To stamp a WAV from its own linear timecode (LTC) or apply a donor's clock shift,
run one of the following commands:

```powershell
.\WAVUtils.exe tc "ltc track.wav"
.\WAVUtils.exe tc --from "ltc track.wav" "lav 1.wav" "lav 2.wav"
```

`tc` updates standard `bext` and iXML time fields without changing the audio.
For `--from`, use a clean LTC track and targets from the same recorder clock.
The donor and targets need the same sample rate and an original `bext` clock.
Merge also checks the last input for LTC and stamps the merged output if it
finds a valid signal.

Keep the first `track.wav.bak` beside the stamped WAV. The operation uses
temporary disk space, preserves that backup, and checks it before using stored
timecode provenance from a stamped target or donor. WAVUtils
does not migrate legacy files written with the incorrect `bext` layout. Use
verified originals or backups when working with those recordings.

For scripts, exit codes are `0` for success, `1` for processing failure, `2`
for invalid command-line arguments, and `130` for cancellation. To wait for
the Windows application and read its exit code in PowerShell, use
`Start-Process -Wait -PassThru`.

## Settings and updates

Settings are saved in `%LOCALAPPDATA%\WAVUtils\config.json`. If this file
does not exist, WAVUtils can read `config.json` next to the executable. It
does not write to that fallback file.

For example, use this configuration to select the system language and enable
the update check at startup:

```json
{
  "language": "auto",
  "checkUpdates": true
}
```

`language` accepts `auto`, `en`, `ru`, `de`, `fr`, `es`, `ja`, and `zh`.
`checkUpdates` controls the Tray check at startup and can also be toggled
from its menu.

If WAVUtils can't read the settings file, it leaves the file unchanged, uses
the system language when supported, and skips automatic update checks. Fix the
JSON and restart WAVUtils to restore your preferences. If only `checkUpdates`
is invalid, Tray skips the automatic check without changing your language.

After you approve an update download, Tray checks the installer against the
size and SHA-256 from the official GitHub release and checks its executable
header. It asks again before closing Tray and launching Setup. If automatic
download, verification, or launch fails, use the
[official release page](https://github.com/wtfsoftware/WAVUtils/releases/latest)
to install manually. Cancelling a download or declining Windows elevation
doesn't start Setup or trigger the manual-install prompt.

Setup uses the previous installation folder when it finds a matching install.
It doesn't default to the folder where you ran Tray. To stay on the portable
edition, download and extract the newer portable ZIP instead of running Setup.

## Build and test

Install the .NET 10 SDK on Windows, then run the regression checks:

```powershell
dotnet restore WAVUtils.slnx --locked-mode -warnaserror
dotnet build WAVUtils.slnx -c Release --no-restore --nologo -v q -warnaserror
dotnet test WAVUtils.slnx -c Release --no-build --no-restore --nologo
```

See [Architecture](ARCHITECTURE.md) for the WAV and desktop processing flows.

### Build release packages

Use the exact .NET 10 SDK version in [`build/global.json`](build/global.json).
Run `./build-release.ps1 -Version X.Y.Z` in PowerShell, replacing `X.Y.Z` with
the package version. The script puts a `win-x64` portable ZIP in `dist/`. If
Inno Setup 7 is installed, it also builds Setup without running it. Leave the
GitHub token unset to keep the packages local, as Windows CI does.

Release builds reject dependency changes against the `packages.win-x64.lock.json`
files and leave the development `packages.lock.json` files alone. For an
intentional dependency or SDK upgrade, regenerate and review the release locks
before building the packages. See the
[release lock refresh procedure](docs/plans/2026-11-25-audit-remediation.md#refresh-the-release-lock-files).

## License

MIT — Copyright (c) 2026 WTF Software
