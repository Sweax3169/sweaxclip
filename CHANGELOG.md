# Changelog

## 1.3.0 — 2026-10-31

A new Win + V panel, built for using rather than looking at.

- **The timeline.** The panel is now a list you can read at a glance: every clip shows its content, and the time it was copied runs down a rail on the left. Many clips are visible at once instead of one card at a time.
- **The open clip.** The selected clip opens up on a spring — text unfolds to seven lines, a picture grows into a real preview with the text read from it, code shows eight lines, a link shows its full address and the page it came from, and the source app appears.
- **Start and Search.** If Start or Windows Search is open when you press the shortcut, SweaxClip closes it (those surfaces are drawn above every app), opens above the taskbar, and pastes your pick into Search.
- Search marks the matching words in text, links, file names and the text read from images. The panel glides to the height of what it shows.
- Categories are icons; the open one names itself, and a line in the category's colour follows it. Each kind of clip has its own colour.
- Hover a clip for pin and delete; deleted rows fold away. Hold Alt to see 1–9 on the rail.
- One spring engine drives every movement, so the README animations are filmed from the app frame by frame.
- Dark mode is dark all the way: paper and folder cards in the library use dark materials, and near-black brand colours are lifted so links stay visible.
- New README, website and artwork.

## 1.2.0 — 2026-10-31

A design of its own. SweaxClip no longer imitates Windows; only the place the panel opens is the same.

- **The deck.** <kbd>Win</kbd> + <kbd>V</kbd> deals your history as a deck of cards where Windows opens its own panel. The newest clip is in front, older ones stack behind showing only their headers. Every card moves on springs: flipping through history, dealing a category, throwing the card at the text cursor when you paste, dropping it in the bin.
- **Every clip is an object.** Text is paper, a link carries its site's colour, code sits on slate with syntax colours, a colour is the colour itself, images are prints, screenshots sit in a bezel with crop marks, files come in a manila folder.
- Below the cursor the deck hangs from the search lid; above the cursor or the taskbar it rests on the bottom edge. With only a few cards the lid and hints stay attached to them.
- **The library is a desk.** Clips lie on it as their cards, grouped by day. Click one to pick it up into a lightbox with its full content, source, history and recognised text.
- Settings filed as manila dividers.
- New palette (warm desk, ink, vermilion), new drawn category icons and a new app icon: the deck itself.
- The panel's count shows the number of matches while searching.
- Apps without an icon get a stamped initial.

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
