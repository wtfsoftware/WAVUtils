# Lock Windows release dependencies

The portable ZIP and Setup bundle the .NET runtime. The framework-dependent
development build and the self-contained `win-x64` build resolve different
dependency graphs. The same lock files cannot protect both: the release script
used to rewrite the development locks during publish.

## Decision

Use `packages.win-x64.lock.json` in App, Core, and Tray for packaging. Check
that all three files exist before calling `dotnet`: NuGet can create a missing
file even in locked mode. Reject changes to the dependency graphs or package
hashes during publish rather than rewriting the files.

Pin the exact stable .NET SDK for packaging in `build/global.json`. The SDK
selects the runtime packs; the NuGet locks alone do not pin the SDK or its
runtime. Windows CI runs the package build without uploading artifacts.

## Alternatives and consequences

One lock per project would require the development gate to use the release's
runtime and publish properties. A repository-root SDK pin would also force
the exact release SDK for development builds. A release-only pin preserves the
normal .NET 10 development gate but makes a packaging SDK upgrade deliberate:
update the pin, [refresh the release locks](../plans/2026-11-25-audit-remediation.md#refresh-the-release-lock-files),
review the diff, and rerun the build and package checks. This controls build
inputs; it does not guarantee byte-identical ZIP or Setup files across builds.
