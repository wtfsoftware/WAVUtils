WAVUtils
Multi-channel WAV merge and split utility with LTC timecode support

INSTALLATION
  Close WAVUtils and WAVUtils Tray before installing or upgrading.
  Setup requires administrator permission and installs both applications.
  After Setup closes, open WAVUtils from the Start menu or optional desktop
  shortcut to start Tray. Use WAVUtils.exe for WAV commands.

YOUR WINDOWS ACCOUNT
  To enable the Explorer menu, open Windows PowerShell without administrator
  rights in the WAVUtils installation folder and run .\Install.ps1.
  To start Tray at sign-in, run .\Install.ps1 -AutoStart instead.
  To disable a feature, run .\Uninstall.ps1 with the same options before
  you remove or move the application files. If Windows PowerShell blocks
  the scripts, see Configure Explorer and Tray for your account in README.md.

CONTEXT MENU
  If enabled, use the merge action on WAV files selected in Explorer.
  Use the split action on a file merged by WAVUtils to restore its tracks.
  Merge and split do not overwrite existing files. If split reports that
  the original tracks exist, use an empty folder with split --output-dir.

TIMECODE AND UPDATES
  The tc command changes metadata in the selected WAV. Keep its .bak file.
  Tray checks the official release and asks before downloading or starting
  setup. You can also install manually from the official release page.

SUPPORT
  GitHub: https://github.com/wtfsoftware/WAVUtils
  Issues: https://github.com/wtfsoftware/WAVUtils/issues
