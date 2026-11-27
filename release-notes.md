## What's new in 1.3.0

### Before you upgrade

Files stamped with the incorrect `bext` layout in earlier versions are not
migrated automatically. To stamp them again, start from verified originals or
backups. Keep the first `.bak` beside each file stamped by this version.

Setup no longer changes the Explorer menu or Tray autostart. If you use either
feature, [configure it for your Windows account](https://github.com/wtfsoftware/WAVUtils/blob/v1.3.0/README.md#configure-explorer-and-tray-for-your-account).
Old settings aren't migrated. If you use WAVUtils without installing it, upgrade
by extracting the portable ZIP instead of running Setup.

### Added

- Use `merge --out` to name the output and `split --output-dir` to choose a
  restore folder. Unambiguous positional merge output still works.
- Merge and split stream WAV data, including RF64 files larger than the RIFF
  limit. The progress window shows the work and offers cancellation.

### Fixed

- Merge and split preserve channel order and restore original track lengths and
  metadata. Neither command overwrites an existing output.
- Broadcast Wave timecode uses the standard `bext` layout. Repeating the same
  `tc --from` no longer applies the time shift twice.
- Unreadable settings don't prevent startup or trigger automatic update checks.
  Tray verifies the installer against GitHub's SHA-256 digest and asks before
  downloading it and again before launching it.

### Download

- `WAVUtils-1.3.0-Portable.zip`: Extract and run on x64 Windows without installing.
- `WAVUtils-Setup-1.3.0.exe`: Install for all accounts; administrator rights are required.

Both packages include the .NET runtime. See the
[WAVUtils usage guide](https://github.com/wtfsoftware/WAVUtils/blob/v1.3.0/README.md)
and the [changes since v1.2.0](https://github.com/wtfsoftware/WAVUtils/compare/v1.2.0...v1.3.0).
