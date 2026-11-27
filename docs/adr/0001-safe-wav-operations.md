# Safe WAV operations and repeatable timecode

Status: accepted by the user.

This decision is for maintainers changing merge, split, and timecode stamping. It
covers audit findings #29–#44 in [the master plan](../plans/MASTER.md), not migration
of existing recordings or camera timecode configuration.

## Decision

- Validate the complete RIFF/RF64 envelope, format, track layout, and output names
  before creating outputs. Reject existing destinations; never overwrite originals
  during merge or split. Write temporary files beside their final destinations and
  publish only completed files with non-overwriting moves.
- Stream frame-aligned blocks with bounded audio buffers. Read RF64 and write RF64
  automatically when the finalized RIFF size exceeds the unsigned 32-bit limit.
  Treat RF64 `ds64` values as replacements only for `0xFFFFFFFF`. Decode LTC
  from the validated audio range. Use NAudio to write WAV; compensate for missing
  padding after odd `fmt ` extensions and truncated RF64 `fact` sample counts.
- Preserve original metadata bytes in PMAL. Use typed fields as a compatibility
  fallback for older PMAL records without the corresponding raw chunk. Add optional
  original format bytes so valid bits and channel-layout metadata survive splitting.
- Write standard BWF fields: eight-byte OriginationTime at body offset 330,
  TimeReference at 338, Version at 346, and UMID at 348. Update iXML using XML APIs
  with DTD and external entity processing disabled. Preserve unrelated metadata.
- Store the original bext, original file's SHA-256, and stamping parameters in a
  versioned `wtcp` WAV chunk. For stamped targets and donors, verify the original
  backup and compare its original bext before trusting that provenance.
  Resolve subsequent donor shifts against this original reference. A stamped donor
  uses its stored original reference too. A donor without an original reference
  cannot establish a recorder-clock shift and is rejected for sibling stamping.
- Rewrite `tc` through a temporary copy. Retain the first original as a `.bak` file
  and refuse to replace an unrelated backup. Keep the displaced WAV until its
  hash confirms no concurrent change; roll back the replacement on mismatch or
  cancellation during verification. Keep audio bytes unchanged. Do not guess or
  silently migrate the previously shipped, shifted bext field layout.
- Extend Core commands with optional progress and cancellation parameters. Keep
  compatible existing calls. Add `merge --out` and `split --output-dir`; preserve
  legacy positional output only when it is unambiguous.
- Pin stable test packages, keep lockfiles, and run a Windows regression gate with
  dependency auditing and compiler warnings treated as errors.

## Alternatives

An explicit size limit is simpler than RF64 but excludes long multichannel
recordings; RF64 was selected. In-place timecode patching avoids a file copy but
cannot retain provenance safely or guarantee recovery after interruption. A
sidecar would be easier to edit but can become detached from a WAV. An out-of-day
guard cannot detect repeated valid shifts; stored provenance can.

## Consequences

Timecode stamping needs temporary free space and retains a backup. A temporary
file and its destination must share a filesystem. Publishing multiple split files
is not a filesystem-wide transaction: an interrupted finalization can leave some
completed tracks, but never truncated tracks or overwritten pre-existing files.
Timecode stamping validates the batch first, then replaces one WAV at a time; it
does not roll back earlier successful targets if a later target fails.
Cancellation is cooperative and checked between bounded blocks; synchronous
filesystem calls on a disconnected network share can still delay cancellation.

Older PMAL without format bytes is supported using its track channel counts and
the merged encoding. Existing invalid bext recordings require a separate,
explicit backup-based migration; this work does not touch them.

## References

- [EBU Tech 3285: Broadcast Wave Format](https://tech.ebu.ch/docs/tech/tech3285.pdf)
- [EBU Tech 3306: RF64](https://tech.ebu.ch/docs/tech/tech3306.pdf)
