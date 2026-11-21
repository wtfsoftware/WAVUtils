## What's New in 1.2.0

### Timecode support (LTC / bext / iXML)

- **New: `tc` command** - decodes LTC (biphase) timecode from the first seconds of a WAV file's audio and stamps bext `TimeReference` + `OriginationTime` + iXML `TIMECODE` in place, so files from a free-running recorder join the timecode generator's timeline: `WAVUtils.exe tc track1.wav track2.wav`
- **New: `tc --from <source.wav>`** - decodes the timecode once from a source track and applies the same shift to sibling tracks (lav mics) that start with it but carry no LTC themselves
- **New: merge stamps Start TC automatically** - LTC decoded from the last input's audio is patched into the merged file's bext and iXML
- **Fixed: bext `TimeReference` was written at a non-spec offset (338)** - spec readers (DaVinci Resolve, Soundminer, libsndfile) saw timecode as 0. bext is now read/written at the correct AES3 / EBU Tech 3285 offsets (12-char `OriginationTime` at 330, `TimeReference` at 342); non-conforming recorder files (Zoom F6/H6: 8-char time, TR at 338) are detected and normalized

### Fixes

- **Fixed: split failed on every merged file** ("PMAL not found") - the reader hardcoded a plain fmt+data header and broke on merged files carrying bext/iXML/INFO before the data chunk; it now walks the real chunk list
- **Fixed: split wrote duplicate bext chunks** (typed + raw preserved) - raw chunks already covered by typed metadata are skipped
- **Fixed: merge launched once per selected file** - Explorer invokes file-class context menu verbs once per item; the first instance now collects the whole selection over a named pipe and performs a single merge
- **Fixed: merged WAV files had an invalid RIFF header and corrupt audio data** (fmt chunk written with 24-byte fields; the interleave loop rewrote the first frame and metadata overwrote the start of the audio), so output was silent noise that standard players could not open
- **Fixed: channel order no longer matches the selection** - merged tracks now follow the order the files were selected in Explorer
- **Fixed: process crash on merge/split errors** (progress window error path)
- **Fixed: process could hang after completing** without ever closing its progress window

## Download

- **Installer**: WAVUtils-Setup-1.2.0.exe (Windows x64)
- **Portable**: WAVUtils-1.2.0-Portable.zip (no installation required)

> Note: reinstalling re-registers the context menu command; machines with an older install need the new installer (or `Install.ps1`) re-run to pick up the single-instance fix.

## CLI Usage

```
WAVUtils.exe merge file1.wav file2.wav [...fileN.wav] [output.wav]
WAVUtils.exe split merged.wav
WAVUtils.exe tc [--from source.wav] file1.wav [...fileN.wav]
WAVUtils.exe check-update
```
