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
