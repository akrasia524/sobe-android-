# Android port (work in progress)

Branch: `android-port`. The original desktop/iOS code is unchanged apart from small mobile guards.

## Status
- [x] Android host project (`OsuBeatmapEditor.Android`, `net8.0-android`, ARM64).
- [x] GitHub Actions workflow that builds a debug APK (`.github/workflows/android-debug.yml`).
- [ ] App-owned beatmap library (replace the osu!lazer Realm dependency).
- [ ] Round-trip tests for `.osu` / `.osz` data preservation.
- [ ] Touch-friendly UI.
- [ ] Measured performance work.

A successful CI build only proves the code compiles and packages. It does not prove the app runs correctly on a phone.

## Why .NET 8
The pinned osu!framework release (2026.527.0) targets .NET 8. Only unreleased framework master targets .NET 10.

## Getting the APK
Actions tab -> "Android debug APK" -> Run workflow, then download `sobe-debug.apk` from the
`android-debug-latest` pre-release on the Releases page and install it (allow "install unknown apps").
