# Follow-up list (not implemented yet)

## UI-1: New Beatmap audio picker on Android - phone-tested (d34ef09); follow-up fixes CI verified in 57f793f, NOT yet phone-tested
- d34ef09 phone result (user, 2026-10-14): chooser lists every installed file manager, picking works, .jpg/.txt show a clear 'not supported'
  message, Create works. Three problems were reported and are fixed in 57f793f:
  1. Artist/Title were guessed from the file name ('Title - Vocaloid' became Artist/Title wrongly). Now the file's own tags are used first
     (ID3v2.2/2.3/2.4, ID3v1, Vorbis and Opus comments; `AudioTags`); only a tag-less file falls back to the 'Artist - Title' file-name rule.
     A later pick replaces text we filled in ourselves, never text the user typed. Tags stored in a legacy code page (e.g. Shift-JIS in an
     ID3 'Latin-1' field) cannot be detected and may show as odd characters - the user can edit them.
  2. After Create the old metadata and 'Beatmap created.' stayed. 3. After Cancel the metadata and 'Audio selected' stayed.
     Both: the form is now reset (Artist, Title, Creator blank; Difficulty 'Normal'; BPM 120; audio, message and staged copy cleared)
     whenever the dialog closes (Cancel, click outside, or after Create) and again when it opens.
- Also: copying the picked file and reading its tags now run off the UI thread ('Reading audio file...' is shown meanwhile).
- Tests: CI 165/165 (10 new AudioTagsTests: ID3v2.2/2.3/2.4, UTF-16/UTF-8, NUL-separated values, ID3v1, Vorbis, Opus, multi-page cover-art tags,
  garbage / truncated files never throw). The form reset itself is UI code and needs the phone.

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

## B2.3c status (editor + library)
- B2.3c-1 DONE (CI only, commit b7864df): the editor finds a library set's .osu, audio, background and hitsound samples
  through `SetFileResolver` (library folder by real name, case-insensitive, traversal-safe; osu!lazer sets unchanged).
- NOT done: SAVING a library set. The editor's save still goes to `BeatmapRealmWriter` / `BeatmapSaver` (lazer), which on a
  phone reports an error and writes nothing (no data is lost, but edits cannot be kept yet). Next: B2.3c-2 = save through
  `OsuFilePatcher` + `LibraryStore.SaveDifficulty` (rolling backup), reading the original with `LibraryStore.ReadFileText`.
- B2.3c-1 VERIFIED ON THE PHONE (user screenshot + report, 2026-10-13): a library difficulty opens in the editor with its
  background image, timeline, hit objects and sliders; audio plays and the map runs when Play is pressed; the editor shows
  a computed star rating (5.59) and about 54 FPS (single screenshot, not a performance measurement).
- Observed in the editor screenshot (not fixed, Stage C): tool/buttons and text are small for touch; layout is the desktop one.

## B2.3c-2 (saving library maps) - CI verified AND phone-tested
- Commit 8ae2f51: the editor's Save on a library map goes through `LibrarySaver`: patch the ORIGINAL text with
  `OsuFilePatcher` -> `Verify` (hit objects + timing must be intact) -> `LibraryStore.SaveDifficulty` (atomic replace + rolling
  backup, newest 10 kept). No real change = no write and no backup. osu!lazer maps still use the old lazer save path.
- Tests (9, in LibrarySaverTests): no-op save writes nothing and makes no backup; a combined edit (title, bookmark, moved
  slider, timing volume) changes exactly those four lines and keeps video/storyboard/unknown keys/[CustomSection]/long
  precision/hitsamples; backup holds the previous text; survives reopening; set-back-to-original gives the exact original file;
  BOM + CRLF kept; dropped background refused clearly; a corrupting save is refused by the check; non-library set refused.
- CI: 122/122 tests pass, Android build succeeds.
- PHONE-VERIFIED (user screenshots + report, 2026-10-13): added the tag "test" in Song setup -> back -> "save changes?" prompt
  -> Save -> toast "Beatmap saved" -> reopened the map -> the tag was still there. Removing the tag and saving again also worked.
  (Backup files and the exact file text were not inspected on the phone; those are covered by the automated tests.)
- UI-8 (non-Latin Artist/Title showing "???"): FIXED, CI verified AND phone-verified (screenshot: Artist れをる and Title ストリーミングハート now draw correctly in Song setup). Finding: the app shipped no fonts of
  its own, so the framework's default font (Latin/Cyrillic/Greek) had no CJK/Hangul glyphs and drew "?". This is display only: tests
  (UnrelatedEditKeepsUnicodeMetadataByteForByte, EditingUnicodeMetadataSavesAndReopensExactly) prove Japanese/Korean/Chinese/emoji
  metadata is saved and re-read exactly, and an unrelated edit leaves those lines byte-identical. Fix: bundled osu!'s Noto bitmap
  fonts (Basic, Bopomofo, CJK-Basic, CJK-Compatibility, Hangul; ~17 MB, licence file included) in OsuBeatmapEditor.Resources/Fonts/Noto and
  registered in OsuBeatmapEditorGameBase. Not bundled: Thai. Costs about 17 MB of APK size. Caveat: if the field was ALREADY literally
  "???" in the file (earlier tool damage), the fix cannot recover it. 
- Known limits: changing/dropping a background image on a library map is refused ("not supported yet"); the set-level title
  shown in the list is not updated when a difficulty's Title is edited (the difficulty name is); the review layer / collab /
  video export paths for library maps are untested.
- No UI to browse/restore backups yet (RestoreBackup exists in LibraryStore and is tested).

## B2.3d (delete / restore / permanent delete / export for library sets) - CI verified AND phone-tested
- Code in 6fa31ca. Tests CI: 138/138 (14 new in LibraryActionsTests). Android build succeeded (verified on 675278b, same code).
- Phone-tested by the user (2026-10-14): Library button, Export .osz to Downloads/sobe, Move to trash, Restore and Delete forever (with
  confirm) all behaved as designed.
- What exists: LibraryActions (delete = move to trash, restore, purge only for a trashed set, export .osz without set.json/backups, never
  overwrites, numbered names); a mobile 'Library' button opening LibraryManageOverlay; the right-click 'Delete set' / 'Export .osz' now work
  for library sets; Android copies the exported .osz to Downloads/sobe via MediaStore (Android 10+; older phones keep it in the app's exports folder).
- Limits: deleting a single difficulty from a library map is not supported yet (shows a message); no backup browser yet; the exported
  .osz has not been opened in osu!droid by us (the archive contents are test-verified, a real osu!droid import is still worth trying).

## UI-4 (star rating 0.00 in the song list for library maps) - CI verified (cb64a5a), NOT yet phone-tested
- Finding: the rating algorithm already existed (StarRatingCalculator, used live by the editor), but library import/save never called it, so
  every library difficulty stayed 0.00. UI-2 (startup picker) was reported fixed by the user earlier; no action needed.
- Fix: LibraryStore now computes the no-mod rating when a difficulty is imported, saved, or restored from a backup, and stores it in set.json
  (a rating failure gives 0 and never blocks an import/save). `BackfillStarRatings()` rates existing sets that have none; the song list runs it in
  the background at start and refreshes when anything changed. The list, the info panel and the difficulty colour all read the stored value.
- Tests: CI 171/171 (6 new in LibraryStarRatingTests: import, zero-object maps, save recalculates, backfill + persistence, backfill touches no
  files, restore recalculates). Android build succeeded.
- Phone test: open the app; sets imported earlier should show a non-zero rating within a few seconds (list refreshes by itself); the value on
  the card should match the star chip shown inside the editor for the same difficulty (small rounding differences are OK). Edit a map, save, go back:
  the rating should update. Note: this is the algorithm version pinned in StarRatingCalculator, which can differ slightly from the newest osu!.

## Cover images + audio preview for library sets - CI verified (cbdd35e), NOT yet phone-tested
- Finding: the song list builds its texture and audio stores only from osu!lazer's hash-addressed file store (null on Android), and looks
  files up through `set.Files` (name -> hash). Library sets had empty `Files`, so no card/hero background and no preview.
- Fix (small, no UI rewrite): `LibraryResourceStore` (read-only IResourceStore<byte[]>) serves library files for the same `a/ab/<key>` style
  lookups; `LibraryBeatmapSets` fills `Files` with a key per difficulty background/audio (no disk access while listing); `SongSelectScreen`
  builds the full-size texture store, the 640px card store and the track store from it on phones. Desktop/lazer path is the same code as before.
  Safety: names are parsed and resolved with the existing traversal-safe `SetFileResolver.ResolveInFolder`; trashed sets serve nothing.
- Preview point: uses the map's PreviewTime, else 40% into the track (as before).
- Tests: CI 178/178 (7 new in LibraryResourceStoreTests). The real image/audio decoding and playback are NOT covered by tests (phone needed).
- Phone test: open the song list. Cards with a background should show their image, the selected map's background should fill the screen,
  and music should start from the preview point when you select a map (Ctrl+Space-style pause button in the playback card toggles it).
  Check a map WITHOUT a background (no image, no crash), a big image, and a map with a mixed-case file name. Report memory/lag with many sets.
- Known gap: the editor's own audio/background loading for library sets was already working (B2.3c). Changing a library map's background is still refused.

## BUG-1: crash when tapping between maps 2-3 times quickly (preview audio) - fix CI verified (c2c1e1a), NOT yet phone-tested
- Phone report (2026-10-15): NullReferenceException inside osu.Framework `TrackBass.seekInternal`, reached from `PreviewTrack.StartPreviewAt`
  (SongSelectScreen), after the library audio preview was added. The cause inside the framework's audio thread is not visible from our code
  and could not be reproduced here (no device); the likely trigger is several preview tracks being created and disposed back-to-back while
  the previous one is still loading.
- Fix (two layers, no behaviour change when things work): 1) preview loading is debounced - the track for a map is only loaded once the
  selection has stayed put for 250 ms, so quick taps no longer start a file read + decode per tap (also saves CPU/battery/RAM);
  2) every audio call in `PreviewTrack` (seek/start/stop/pause/loop/volume) is now guarded: on any failure the preview is dropped and logged
  to the runtime log instead of crashing the app. Update() uses try/catch (no per-frame allocation).
- Tests: CI 178/178 and Android build succeeded, but this is audio/UI behaviour that no unit test covers - the phone is the only real check.
- Phone test: tap quickly between maps (try 10+ taps, also scrolling and tapping), with and without waiting; music should follow the final
  selection after a short pause and the app must not crash. If it still crashes, send the new crash text (the stack trace shows which call).

## UI-7 replaced: landscape-only + bigger touch targets (user decision 2026-10-15: portrait is dropped for the Android port)
- Orientation: `MainActivity` now uses `ScreenOrientation.SensorLandscape` (landscape either way up, never portrait). Portrait layout work is cancelled;
  no portrait code paths remain to maintain. (Before: FullUser = any orientation.)
- Finding: only the song-select screen and a few overlays have phone sizing (`MobileMetrics`); the editor (EditorScreen, ~6000+ lines) and most
  panels use fixed desktop sizes designed for a 1366x768 layout. On the Infinix (720 px short side, ~70 mm) 768 layout units = 70 mm, so a 40-unit button
  is only ~3.6 mm (comfortable touch targets are ~7-9 mm).
- Step 1 (715350d, CI verified 178/178 + Android build, NOT phone-tested): one global lever - `MobileMetrics.TargetDrawSize` = 1024x576 on phones
  (desktop stays 1366x768). Everything scales ~1.33x larger (scale 0.94 -> 1.25 on a 1612x720 screen), including the editor. The already phone-tuned
  `MobileMetrics` values are multiplied by 0.75 so song select's controls keep their tested physical size. Fixed dialogs (<= 560 units tall) still fit 576.
- Risk to check on the phone: panels designed for 768 units of height now have 576, so editor panels/timeline may overlap or clip. Tuning is one line
  (`TargetDrawSize`): larger value = smaller controls and more room.
- Next steps (incremental, need screenshots first): (a) screenshots of song select + editor (landscape) to see overlaps/clipping and which controls
  are still too small; (b) enlarge specific editor controls (tool buttons, timeline/seek, snap/zoom, playback, popovers) with `MobileMetrics.Pick`;
  (c) per-screen spacing/arrangement fixes. Keep editing logic untouched.

## Editor landscape UI (A1-A8) - status

All on branch `android-port`; each increment CI-verified (tests + APK) and phone-verified by the user unless noted.

- A1 field fit `560a295` (phone-verified) - A2 tool buttons `cb367e0` - A3 top-left controls `108e8e3` - A4 bottom-left controls `3879e5e`
- A5 time + percentage `52f51bb` - A6 snap divisor box + BPM/SV/star `dc9a4b3`, `0e4ed5d`
- A7 modifier column HD/HR/DT/EZ/AU `2f60482`, follow-ups (divisor flush under top bar, Auto popup + key overlay positions, bigger "...") `61f391d`
- A8 play/pause above the speed row (25/50/75/100) `d7f233e`, play button size/position + drawn play/pause shapes `28c06d9` (phone-verified)
- Final pass done: tests + APK green on `28c06d9`; every A1-A8 increment phone-verified by the user. Field resize only changes the uniform fit scale (`PlayfieldFit`); circle diameter/CS code untouched.
- Open items: osu!-style cursor trail (see reminder below); the ghost-number tweaks `0b865e5`/`36ddf6d` were not the real fix (the live-renumber preview was, `a9126ae`) but are harmless; layout values are tuned to the 1024x576 mobile target at the Infinix Hot 50 aspect ratio, other aspect ratios not checked.
- Also fixed on the way: combo-number "skip" while Circle/Slider armed (live renumber disabled on phones) `a9126ae`; Android Back exits the editor in one press `5ca5de4`; Humanize tuning panel scroll/bounce `1c840ae`.

## Auto cursor long trail - second attempt (awaiting phone test)

Second attempt (single batched drawable, ring buffer of <=640 points, even spacing, wall-clock fade; no child drawables or masking) is in the commits after `ec32a33`; CI tests+APK green. Awaiting phone result for lag/smoothness. The first attempt (`4f568b8`, reverted `12c69a5`) lagged. Look reference: osu!droid uses a tiny soft-dot sprite repeated along the path (own implementation, nothing copied). Frame time could not be measured in CI.
