# Follow-up list (not implemented yet)

## UI-1: "New beatmap" pop-up has no way to choose an audio file on Android
- Finding: `UI/NewBeatmapOverlay.cs` picks audio with the framework's in-game `BasicFileSelector`, rooted at the user
  profile folder (desktop-style file browser squeezed into the right column). On Android that folder is the app's private
  area and the browser is not usable on a phone, so "Create" never enables (it needs an audio path).
- Plan: on mobile, replace that column with a "Choose audio file" button using `host.CreateSystemFileSelector(AudioExtensions)`
  (osu!framework copies the picked content URI to a temp file; `Selected` gives the path -> existing `setAudio(path)`).
  Copy the audio into the library straight away (the temp copy is purged on next launch). Keep `BasicFileSelector` on desktop.
- Needs a phone test.

## UI-2: an Android file picker appears before the main menu on every start
- Finding: nothing in the app code or manifest opens a system picker at start-up (no `CreateSystemFileSelector`, no intent
  handling, no `intent-filter` other than the launcher). The cause is not yet identified.
- Next step: ask for a screenshot of the picker (its title / buttons / folder shown) - that separates "storage permission
  prompt", "framework-level picker" and "osu!lazer data-folder lookup" - then fix. Do not guess-fix.

## Data layer note for the wiring stage (B2.3)
- `File.ReadAllText` strips a leading UTF-8 byte-order mark, so a save through it would lose the BOM. Use
  `LibraryStore.ReadFileText` (keeps it; tested).

## Phone-tested status (Infinix Hot 50, screenshots from the user, 2026-10-13)
- WORKS on device: Import button -> Android file picker -> pick an .osz -> toast "Imported <artist> - <title>" -> the set
  is listed in the song list (title, mapper, difficulty name). Needs build 8a2e25d or newer.
- NOT yet device-tested: opening the set in the editor, cover images, audio preview, delete, export, New Beatmap.

## New issues seen in the screenshot (UI-3, UI-4, UI-5)
- UI-3 [FIXED, VERIFIED on the phone in landscape and portrait, build 316ba7a]: the search box overlaps the first carousel card (the card's title is half hidden behind it). The carousel's top
  padding is a fixed 120 in `SongSelectScreen` but the mobile top bar (80) + search row (60) are taller. Use MobileMetrics.
- UI-4: the star rating shows 0.00 for library sets (no star-rating computation is wired to the library yet).
- UI-5 [FIXED, VERIFIED on the phone: shows "1 difficulty" / "10 difficulties"]: "1 difficulties" - singular/plural wording; the left info panel stays empty until a map is selected.

## Still unresolved
- UI-1 (New Beatmap audio picker), UI-2 (picker at every start - needs a screenshot of it), editor open/save, delete/export,
  cover images/preview audio for library sets, baseline tags not pushed.

## Fix log: search-box overlap (UI-3) and "1 difficulty" (UI-5)
- Carousel top inset is now derived: max(120, top bar + gap + search row + gap) from the same `TopBar.HeightPx` /
  `MobileMetrics.SearchRowHeight` constants the search row uses, so it follows the device sizing instead of a fixed 120.
  Desktop keeps 120 (unchanged). The search grid height now matches the search box height (it was 40 vs 60 on phones).
- "1 difficulty" / "N difficulties" in the set header.
- Verification: phone screenshots (landscape + portrait, 2026-10-13): the first card sits fully below the search box in both.

## New observations from the portrait/landscape screenshots (not fixed)
- UI-6: NOT AN APP ISSUE. The ">_" floating button is the user's own Termux overlay, not part of this app.
- UI-7: portrait still uses the landscape layout (list in the right half, large empty area on the left/bottom, small text).
  A real portrait layout is part of the mobile UI pass (Stage C).
- Info panel (selected map) works for library sets: shows artist, title, difficulty, mapper. Star rating is still 0.00 (UI-4).
