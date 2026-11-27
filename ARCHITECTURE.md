# WAVUtils architecture

If you maintain WAVUtils, use this overview to trace WAV processing, the
Windows desktop entry points, and the boundaries that protect existing files.
It does not describe migration of previously stamped recordings. For command
examples, see the [README](README.md).

## Solution layout

The .NET 10 solution contains these projects:

| Project | Responsibility |
| --- | --- |
| `WAVUtils.Core` | Validates RIFF/RF64, processes samples and metadata, stores settings, and verifies update downloads. Uses NAudio for WAV output. |
| `WAVUtils.App` | Parses commands, collects Explorer merge inputs, and runs operations in a WinForms progress window. |
| `WAVUtils.Tray` | Manages update preferences, prompts for download and Setup, and offers a manual fallback. |
| `WAVUtils.Tests` | Exercises independently authored WAV fixtures, Windows controls, and release scripts without running Setup. |

## WAV operations

`WavMetadataChunk.ReadFile` validates the container and records the audio offset
and length. In RF64, `ds64` replaces a 32-bit value only if that field is
`0xFFFFFFFF`. Core processes audio in frame-aligned blocks; `Rf64WaveFileWriter`
writes RIFF or RF64 according to the finalized size. Each operation checks
cancellation between blocks, and each writer finalizes and flushes its temporary
file before publication.

- `MergeCommand` interleaves inputs in argument order and pads shorter inputs
  with the format's silence value. It uses `bext` from the last input, falling
  back to the first. The first input supplies the remaining top-level source
  metadata. Merge checks the last input for linear timecode (LTC), channel by
  channel, and stamps `bext` and iXML when it finds a valid signal.
- `SplitCommand` validates the `pmal` track table and all destination names.
  It reads the merged audio once and restores the original length, format bytes,
  and metadata of each track. For older PMAL records, it can derive the format
  from the merged file and use typed metadata when raw chunks are absent.
- `TcCommand` updates `bext` and iXML without changing the audio bytes. It
  subtracts the donor's original recorder reference from its decoded timecode
  and adds that offset to each target's original reference. Each stamped file
  stores its original `bext` (if present), the original file's SHA-256, and
  stamping parameters in `wtcp`. The command verifies the retained `.bak`
  before using the provenance and keeps a rollback copy until it verifies
  the replacement.

Merge and split do not overwrite existing destinations. Split publishes files
individually, and timecode stamping replaces targets individually. If a batch
stops after publication begins, completed outputs or earlier stamps can remain.
See [the WAV operations decision](docs/adr/0001-safe-wav-operations.md) for
format compatibility, backups, and cancellation limits.

## Desktop settings and updates

App and Tray share Core's localized resources and read settings from
`%LOCALAPPDATA%\WAVUtils\config.json`. An executable-adjacent configuration is a
read-only fallback. When writing a preference, Tray preserves the other keys.

Setup installs files and shortcuts for the computer. The separate PowerShell
scripts change Explorer verbs and Tray autostart only for the account running them.

Tray's startup check does not download an installer without approval. Tray
accepts only the version-matching installer from the official release. It
checks the length, SHA-256, and Portable Executable (PE) header, and asks again
before starting Setup. After Tray releases its upgrade mutex, it rechecks
the digest and header before launch. Download or launch failure offers the
official release page; cancellation and declining elevation do not. See
[the desktop settings and updates decision](docs/adr/0002-desktop-settings-and-updates.md)
for the trust and installation limits.

## Verification

Run the [Windows build and test gate](README.md#build-and-test). For header or
timecode changes, use independently authored fixtures and inspect them with
`ffprobe`; a parser and its serializer can agree on the same incorrect layout.
See [the master plan](docs/plans/MASTER.md) for the results and external checks
that remain open.
