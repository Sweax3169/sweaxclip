# Changelog

## 1.1.0 — 2026-10-31

A redesign that makes SweaxClip look and behave like part of Windows 11.

- New interface built on the Windows 11 design language: Segoe UI, Fluent controls, Mica in the library and Acrylic in the quick panel.
- Follows the Windows light/dark setting and accent colour, and switches live when they change.
- The quick panel opens under the text cursor in Chrome, Edge and other apps that expose the cursor to accessibility tools; without a cursor it opens above the taskbar. It slides in like a Windows flyout.
- Category tabs in the quick panel, like the Windows emoji and clipboard panel. Hold Alt to show 1–9 shortcuts.
- Library laid out like Windows Settings: navigation pane, content layer, details pane and settings cards.
- New app icon.
- Bundled fonts removed; the app uses the system fonts.

## 1.0.0 — 2026-10-31

First release.

- Quick panel on <kbd>Win</kbd> + <kbd>V</kbd>, opened at the text cursor, pastes into the previous app.
- Unlimited, permanent archive: SQLite (WAL, synchronous) + images stored once by SHA-256.
- Categories: Everything, Text, Links, Images, Screenshots, Files, Code, Colors, Pinned, Trash.
- Full-text search with a trigram index; Turkish letters and accents folded.
- Offline OCR with the Windows engine — screenshots are searchable by the words in them.
- Screenshots folder watched; existing screenshots imported on first run.
- Library window: timeline or contact sheet, inspector with full content and history.
- Rich text (HTML / RTF) and copied files are pasted back as they were copied.
- Respects password-manager privacy flags; ignore list; pause from the tray.
- Daily database snapshots (7 days + 1 per month), `.zip` export, portable mode.
- English and Turkish.
