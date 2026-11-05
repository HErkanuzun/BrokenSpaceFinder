# Changelog

All notable changes to Broken Space Finder. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

## [1.0] - 2026-11-05

First release.

### Added
- **Disk map**: scans a whole drive with `FindFirstFileEx` (a 500 GB SSD with 1.2 million files in about a minute), shows folders largest first with a coloured breakdown bar, lets you walk into folders (double-click / Backspace) and lists the 300 largest files on the drive. Shows how much space didn't appear in any folder (protected folders, System Restore).
- **Clean up**: temporary files, Windows Temp, crash dumps and error reports, Windows Update downloads, browser caches (Chrome, Edge, Brave, Opera, Firefox), Electron app caches, pip/uv/npm/NuGet/Yarn caches, old NeoStack versions, Unreal crash reports and logs, Recycle Bin, GPU shader caches, Unreal shared and project caches, hibernation file, NVIDIA recordings and old downloads. Each item has a risk label; safe ones come selected. Items whose program is running are skipped. "What will be deleted?" lists every path first.
- **App data**: size and last-change date of every AppData and home dot-folder; folders unchanged for 90+ days are flagged. Windows folders are shown but can't be deleted.
- **Programs**: installed programs with size and last use (UserAssist + Prefetch), sorting by size, last use or name, and "Uninstall…" through the program's own uninstaller.
- Protections against deleting Windows, Program Files, system areas, system files and main user folders. Permanent deletes ask you to type `SİL` / `DELETE`.
- Runs as a normal user; "Run as administrator" restarts the app elevated when needed.
- Turkish and English, dark theme, taskbar progress during scans, one running copy, installer and portable ZIP — same as the rest of the Broken family.
