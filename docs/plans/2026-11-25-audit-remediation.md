# Audit remediation

Use this plan to verify the fixes for #5–#7 and #29–#74 in the
[master plan](MASTER.md). Build and test in a sandbox without running Setup or
changing production recordings or camera settings. Keep packaging separate
from promotion: verify the final binaries and source commit before publishing.
CRM extraction (#17) and camera configuration (#18) are outside this change.
For tooling, use a separate GitNexus index without modifying the persistent
store or runner (#9, #21, #48).

## Confirmed decisions

- Store the original bext and stamping provenance inside a versioned WAV chunk.
  Apply subsequent shifts to the original reference, not the current reference.
  Rewrite through a temporary file and retain a backup before replacing a WAV.
- Process audio in bounded blocks and automatically use RF64 above the RIFF limit.
- Add stable MSTest and a Windows CI gate with locked dependency restoration.
- Add explicit `merge --out` and `split --output-dir`; preserve unambiguous legacy
  output syntax. Add optional progress/cancellation parameters to Core.
- Keep Setup machine-wide and leave Explorer verbs and Tray autostart as
  separate per-account choices. Do not auto-run either executable from Setup.
- Keep the updater interactive. Compare numeric versions without treating
  trailing zeros as an upgrade, and leave destination selection to Inno Setup.
- Recover at startup without overwriting unreadable settings or opting into
  automatic update checks. Keep the write path strict.
- See the decision records for [WAV operations](../adr/0001-safe-wav-operations.md),
  [settings and updates](../adr/0002-desktop-settings-and-updates.md), and
  [release dependencies](../adr/0003-lock-windows-release-dependencies.md).

## Execution

1. Preserve the old foreign GitNexus store and build a current local index.
2. Install the permanent red regression cases before changing production code.
3. Correct BWF/INFO/iXML, strict RIFF/PMAL reads, lossless metadata and padding.
4. Implement safe streamed merge/split and RF64, testing samples and lengths.
5. Implement repeat-safe timecode, backup/rollback and independent LTC fixtures.
6. Connect progress/cancellation and CLI; verify rendered controls and an operation
   while it runs, including cancellation and released file handles.
7. Fix tray/autostart/upgrade mutex and packaging; run static and sandbox checks.
8. Update user/developer documents and the ledger; run the full canonical gate and
   refreshed graph review. Report unverified external integrations separately.

## Dependency evidence

NuGet's public version feeds were checked before selection: NAudio 3.3.0 is the
current stable audio library already used by the project; MSTest 4.5.3 and
Microsoft.NET.Test.Sdk 18.12.0 are stable test-only dependencies. MSTest is
Microsoft-maintained and MIT-licensed. NuGet auditing runs on all resolved
packages and must pass the canonical gate. Tests add no shipped runtime payload.

Release builds use the SDK pinned in [`build/global.json`](../../build/global.json)
and a separate lock for each published project's `win-x64` graph, including its
Core reference. The .NET SDK selects the runtime packs for self-contained builds.
Use the [Microsoft release metadata](https://builds.dotnet.microsoft.com/dotnet/release-metadata/10.0/releases.json)
to select a supported security patch when updating the SDK. A locked restore
doesn't reject a missing lock, so the release script checks each file first.

### Refresh the release lock files

When you intentionally update the SDK or dependencies, run the following from
the repository root. Review the three release lock diffs, then run the Windows
regression checks and build the release packages without a GitHub token.

```powershell
Push-Location -LiteralPath build
try {
    foreach ($project in @('WAVUtils.App', 'WAVUtils.Tray')) {
        dotnet restore "../$project/$project.csproj" -r win-x64 `
            -p:SelfContained=true -p:PublishSingleFile=true -p:PublishReadyToRun=true `
            -p:IncludeNativeLibrariesForSelfExtract=true --use-lock-file `
            --lock-file-path packages.win-x64.lock.json --force-evaluate -warnaserror
        if ($LASTEXITCODE -ne 0) { throw "Release restore failed for $project." }
    }
} finally { Pop-Location }
```

## Verification

The [Windows regression gate](../../README.md#build-and-test) on SDK 10.0.404
restored locked dependencies with NuGet audit enabled, built Release without
warnings or errors, and passed **185 tests** with no skips. Fixtures use
independently authored WAV/BWF bytes. The PowerShell 5.1 check compares parsed
text with source text; `actionlint` 1.7.12 passed on the workflow. These results
come from the local audit verification, separate from the release CI run.

The installer-profile and literal-path checks failed on the old scripts. The
shortcut, elevated launch, and log checks failed on the old installer and Tray.
The permanent gate now covers these cases without writing to the live registry.
Before fixing #68, #69, and #71, the launch stub saw a portable `/DIR`, two
equivalent version pairs reported updates, and the installer-payload check
found a duplicate `LICENSE` file entry.
Before fixing #73, six settings cases failed on a malformed document, a
non-object root, an unreadable user path, or incorrectly typed preferences.
Startup uses safe defaults without changing the file; the existing write
regression still proves that an invalid file cannot be overwritten.
Before fixing #74, publish did not use locked mode and did not stop at a missing
release lock. Both script checks now pass. A real publish with a verified change
to the NAudio version range in a sandbox returned `NU1004`, left the lock
unchanged, and produced neither ZIP nor Setup.

The following measurements come from the executable extracted from each ZIP:

| Portable build | Synthetic operation | Evidence |
| --- | --- | --- |
| 1.2.901 | CLI compatibility and metadata | Explicit and unambiguous legacy syntax, overwrite refusal, and upgrade mutex lifetime. Repeating `tc --from` did not change the target bytes. `LIST/adtl` survived exactly once; malformed padding returned 1 without creating an output. |
| 1.2.901 | 6 GiB RF64 round trip | `merge` took 15.807 s and `split` 17.627 s; peak working set was 77.3 MiB. `ffprobe`, ordered samples, the padded tail, and SHA-256 of both restored audio streams agreed with the fixture. |
| 1.2.901 | Desktop progress and cancellation | Progress changed at least every 1.032 s. `merge`, `split`, and `tc` returned 130, released inputs, and removed temporary output within 0.956 s, 0.315 s, and 0.679 s of cancellation, respectively. |
| 1.2.902 | Russian `tc` on RF64 over 4 GiB | All progress phases appeared in Russian. The operation took 15.444 s, with a maximum visible change interval of 1.044 s. `ffprobe` read `TimeReference` 354441900; the backup matched the original digest and the stamped audio matched the backup. |
| 1.2.902 | Cancel Russian `tc` during writing | The window returned to idle and the input unlocked after 0.119 s. The process returned 130, the input hash was unchanged, and no temporary file or backup remained. |
| 1.2.903 | Small RIFF/RF64 round trip | Both EXE versions and digests matched the publish files. Packaged scripts passed parsing and isolated registry scenarios. `ffprobe` read 44 merged frames and 44/31 restored frames; both sample hashes matched. The Inno build included both account scripts. |
| 1.2.904 | Updater and packaging fixes | Both EXEs reported 1.2.904.0. All seven ZIP entries matched the publish files by SHA-256; the Inno 7.1.1 compiler listed the same seven files, with one `LICENSE` install entry. Packaged scripts passed the three isolated checks. The RIFF/RF64 round trip preserved sample order, silence padding, and both restored audio hashes. |
| 1.2.905 | Package and CLI smoke test | Both EXEs reported 1.2.905.0. The seven ZIP digests matched the publish files, and Inno 7.1.1 listed the same payload. The packaged scripts passed their isolated checks. RIFF/RF64 samples and restored hashes matched. The explicit, default, and legacy CLI forms, overwrite refusal, and repeat `tc --from` also passed. |
| 1.2.905 | Corrupt settings recovery | With the same malformed executable-adjacent settings, 1.2.904 returned 1 from `--help`; 1.2.905 returned 0 and merged two WAV fixtures. Tray started without an error dialog or update request, and the settings bytes were unchanged. Explicit opt-in reached a local test proxy that did not forward the request. |
| 1.2.906 | Locked package and portable readback | Both EXEs reported 1.2.906.0. The seven ZIP entries matched the publish files by SHA-256, and Inno 7.1.1 listed the same payload with one `LICENSE`. The packaged scripts passed parsing and isolated registry checks. The extracted CLI started with a corrupt config and left it unchanged. `ffprobe`, sample order, silence padding, and both restored audio digests matched the fixtures. An overwrite attempt left its target unchanged, and repeating `tc --from` left the stamped file byte-identical. |

The 1.2.906 release build left all 61 source inputs byte-identical to the worktree,
including three development locks and three release locks. The restore and
publish manifests for both executables list .NET Core and Windows Desktop
`win-x64` runtime packs at 10.0.15.

Verify the latest sandbox artifacts against these SHA-256 digests:

| Artifact | SHA-256 |
| --- | --- |
| `WAVUtils-1.2.906-Portable.zip` | `BFCAB7C83F81961BCADAA3BDB968290ACDEDF63CEBD22E0B89190BF1EA8C0FB1` |
| `WAVUtils-Setup-1.2.906.exe` | `FD63212B29B18B19329C257291464714BAB515100482386F75B9EA7B45CC71E4` |

The changes after 1.2.901 did not alter the merge or split audio loops, so the
6 GiB round trip was not repeated. No speedup is claimed without a comparable
baseline.

## Remaining checks

- #55 and #67: Setup no longer writes Explorer or autostart settings. In
  disposable Windows, verify fresh install, upgrade, and uninstall with a
  standard user and a different administrator. Include both per-account
  scripts, sign-in, and the old uninstall log. Do not overwrite the old log
  without checking what other cleanup would be lost.
- #70: Verify when Setup no longer needs the downloaded EXE. The app cleans up
  on rejected elevation or launch failure, but not after a successful launch.
- Live installation, upgrade, uninstall, Authenticode, Resolve, and hardware
  interoperability are not covered by these runs. Do not infer these outcomes
  from a successful compiler run or from `ffprobe`.
- #9, #21, #48: leave the persistent GitNexus store and runner unchanged.
  In the temporary graph, require the indexed commit to match `HEAD` and
  `contentDrift` to be `current`. Run `git diff --check` and `detect_changes`
  with intent-to-add entries for untracked files in a separate Git index.
  Reject partial or truncated reports and check that `HEAD` and the original
  staging area are unchanged.
