<!-- gitnexus:start -->
# GitNexus — Code Intelligence

This project is indexed by GitNexus as **WAVUtils** (212 symbols, 507 relationships, 17 execution flows).

> Index stale? Run `node .gitnexus/run.cjs analyze --index-only` from the project root — it auto-selects an available runner. No `.gitnexus/run.cjs` yet? Bootstrap with `npx`, `bunx`, or `pnpm dlx` — e.g. `bunx gitnexus@latest analyze` (npm 11 npx crash; #1939).

## Always Do

- **MUST run impact before editing.** Use `impact({target: "symbolName", direction: "upstream"})` or `node .gitnexus/run.cjs impact "symbolName" --direction upstream --repo .`; report callers, processes, and risk. Never substitute grep for graph analysis.
- **MUST analyze graph changes before committing.** Use `detect_changes({scope: "all"})` (MCP) or `node .gitnexus/run.cjs detect-changes --scope all --repo .` (CLI fallback). `partial: true` or `truncated: true` is not a clean check — a zero means unseen, not unaffected; re-run it. For regression review: `detect_changes({scope: "compare", base_ref: "main"})` or `node .gitnexus/run.cjs detect-changes --scope compare --base-ref "main" --repo .`.
- MUST warn on HIGH/CRITICAL `risk` pre-edit; never use `riskSharedAxes` to waive a HIGH/CRITICAL `risk` warning. Compare File/symbol: MCP File omits axes; Graph-RAG expands File.
- **MUST treat `risk: UNKNOWN` as unresolved, not as low.** An empty caller set is not evidence the symbol is unused — it can also mean the callers are not resolvable by the index (plain-object property access, dynamic dispatch, cross-language calls). `impact` pairs `UNKNOWN` with a `riskNote` saying so. Confirm with a text search before treating the symbol as safe to change or delete; do not proceed on the strength of a zero.
- **MUST use `query({search_query: "concept"})` for concepts/flows, `context({name: "symbolName"})` for a named symbol, or `impact` for blast radius, on read-only callers, dependencies, imports, or execution flow.** Graph first; text search only for empty/`UNKNOWN`/literals.
- For security review, `explain({target: "fileOrSymbol"})` lists taint findings (source→sink flows; needs `analyze --pdg`).

## Never Do

- NEVER edit a function, class, or method before MCP/CLI impact analysis.
- NEVER ignore HIGH or CRITICAL risk warnings from impact analysis, and never read `UNKNOWN` as an all-clear — it means the walk could not answer, which is the one verdict that requires confirming by other means.
- NEVER rename symbols with find-and-replace — use `rename` which understands the call graph.
- NEVER commit before MCP/CLI graph change analysis.

## Resources

| Resource | Use for |
| --- | --- |
| `gitnexus://repo/WAVUtils/context` | Codebase overview, check index freshness |
| `gitnexus://repo/WAVUtils/clusters` | All functional areas |
| `gitnexus://repo/WAVUtils/processes` | All execution flows |
| `gitnexus://repo/WAVUtils/process/{name}` | Step-by-step execution trace |

## CLI

| Task | Read this skill file |
| --- | --- |
| Understand architecture / "How does X work?" | `.claude/skills/gitnexus-exploring/SKILL.md` |
| Blast radius / "What breaks if I change X?" | `.claude/skills/gitnexus-impact-analysis/SKILL.md` |
| Trace bugs / "Why is X failing?" | `.claude/skills/gitnexus-debugging/SKILL.md` |
| Rename / extract / split / refactor | `.claude/skills/gitnexus-refactoring/SKILL.md` |
| Tools, resources, schema reference | `.claude/skills/gitnexus-guide/SKILL.md` |
| Index, status, clean, wiki CLI commands | `.claude/skills/gitnexus-cli/SKILL.md` |

<!-- gitnexus:end -->

# WAVUtils developer notes

## Build and test

Run the [Windows regression gate](README.md#build-and-test) after changes. The
solution includes `WAVUtils.Tests`; no test requires running Setup or touching
production recordings. See [architecture](ARCHITECTURE.md) for the App, Core,
and Tray boundaries and [the master plan](docs/plans/MASTER.md) for finding IDs.

- In WinForms layout tests, request a scaled width beyond the native tracking
  limit and assert that Windows capped it. Testing a fixed scale on a wide
  monitor alone can miss controls clipped after the cap.

## WAV verification

- Verify WAV headers and timecode with independently authored bytes and
  `ffprobe`. A Parse/Serialize round trip can agree on the same wrong layout.
- Use standard Broadcast Wave offsets from [EBU Tech 3285](https://tech.ebu.ch/docs/tech/tech3285.pdf),
  pp. 9–11: eight-byte OriginationTime at body offset 330, TimeReference at
  338, Version at 346, and UMID at 348. Do not silently migrate legacy stamps.
- Validate raw FourCC bytes before ASCII decoding: .NET's replacement `?`
  can otherwise turn an invalid byte into a valid chunk identifier.

## Windows scripts

- Keep scripts used by Windows PowerShell 5.1 ASCII or save non-ASCII scripts
  as UTF-8 with BOM. Check parser text as well as parse errors; both a corrupted
  string and its intended replacement can be syntactically valid.
- Use `-LiteralPath` for exact paths containing `*` or brackets. For a literal
  registry key, use `Registry.SetValue` to create or update a value without
  clearing other values with `New-Item -Force`.

## Settings

- Keep startup defaults out of the settings write path. Re-read the stored
  preferences strictly before saving so recovery never overwrites an unreadable
  configuration.

## Packaging

- Check that each `win-x64` release lock exists before publishing: NuGet can
  generate a missing lock even in locked mode. Keep release locks separate
  from development locks.
