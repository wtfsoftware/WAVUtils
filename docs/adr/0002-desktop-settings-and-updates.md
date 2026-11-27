# Desktop settings and verified updates

Status: accepted by the user.

This decision is for maintainers fixing the Tray application and updater. It does
not authorize installing a package or modifying existing user settings during
verification.

## Decision

- Store settings in the user's `LocalAppData/WAVUtils/config.json`. Read the old
  executable-adjacent `config.json` only as a compatibility fallback. Preserve
  language, update preferences, and unknown fields when changing one preference.
  Write through a temporary file under a named mutex and replace the settings file.
- If startup can't read the settings file, use the system language and skip
  background update checks. Don't fall back to older preferences or use startup
  defaults when saving over an unreadable file. Read the two preferences
  independently if just one has the wrong type.
- Accept an update installer only from this repository's official GitHub release,
  with a version-matching filename, a bounded positive size, and a SHA-256 digest
  supplied by the GitHub release API. Verify downloaded length, SHA-256, and the PE
  envelope before offering to launch it.
- Treat missing numeric version components as zero when comparing versions.
  Use the original release version and tag when matching the asset filename and URL.
- Prompt before downloading the installer. Keep the download window responsive
  and close streams and remove incomplete files on failure or cancellation.
  Prompt again after verification. Release the application's upgrade mutex and
  recheck the file's digest and executable header before requesting elevation.
- Leave destination selection to Inno Setup. It reuses a matching installation's
  registered directory as the default; passing Tray's directory would override
  that behavior and could install into a portable folder.
- If automatic download or verification fails, offer manual installation from
  the official release page. Never launch an unverified download. A user-initiated
  cancellation, including declining elevation, is not an installation failure.
- Keep the machine-wide Setup separate from the Explorer menu and Tray
  autostart. Manage each optional feature explicitly with the existing user
  scripts, without elevation, whether using Setup or the portable package.
  Setup does not run either executable; interactive users can start Tray from
  the installed shortcuts.

## Alternatives and limits

Executable-adjacent writes fail for standard users in Program Files. Keeping
auto-installation without asset verification is unsafe. Manual-only installation
is simpler; the user selected verified automatic download with manual fallback.

Writing the optional settings to `HKCU` from Setup can use another account
when Windows asks for administrator credentials. Machine-wide Explorer verbs
and autostart would instead change other accounts without their approval. The
scripts preserve a per-account choice without relying on Setup to recover the
original user's credentials. They do not migrate previous settings; Inno Setup
can still replay legacy entries when uninstalling after an in-place upgrade.

A release SHA-256 verifies the asset supplied by GitHub; it does not establish an
independent publisher identity or replace Authenticode signing. No signing key is
available in this project. Live installation, UAC, and upgrade replacement still
require a separate disposable Windows environment.
