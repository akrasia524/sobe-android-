using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace OsuBeatmapEditor.Game.Beatmaps
{
    /// <summary>
    /// Produces the text of an edited <c>.osu</c> by patching the ORIGINAL text, never by regenerating it.
    ///
    /// Guarantees (all covered by tests in OsuBeatmapEditor.Game.DataTests):
    /// <list type="bullet">
    /// <item>A save with no real edits returns the original text byte-for-byte (line endings, comments, unknown
    /// keys, unknown sections, storyboard/video events, spacing all intact).</item>
    /// <item>Only the pieces that actually changed are rewritten; everything else stays verbatim.</item>
    /// <item>Hit-object / timing-point records the decoder could not understand are kept, not dropped.</item>
    /// </list>
    /// Pure and I/O free so it can be unit-tested without a device.
    /// </summary>
    public static class OsuFilePatcher
    {
        private static readonly CultureInfo inv = CultureInfo.InvariantCulture;

        // Canonical section order of a .osu file; used to decide where to insert a missing section.
        private static readonly string[] section_order = { "General", "Editor", "Metadata", "Difficulty", "Events", "TimingPoints", "Colours", "HitObjects" };

        /// <summary>
        /// Returns <paramref name="original"/> with <paramref name="current"/>'s hit objects / timing / bookmarks and
        /// <paramref name="edits"/>' metadata, difficulty and colours applied (only where they differ from the original).
        /// </summary>
        public static string Patch(string original, ParsedBeatmap current, BeatmapSaver.Edits edits)
        {
            var orig = OsuFileDecoder.Decode(new StringReader(original));
            var doc = new Doc(original);

            patchGeneral(doc, orig, edits);
            patchMetadata(doc, orig, edits);
            patchDifficulty(doc, orig, edits);
            patchBookmarks(doc, orig, current);
            patchBackground(doc, orig, edits);
            patchColours(doc, orig, edits);
            patchTimingPoints(doc, orig, current);
            patchHitObjects(doc, orig, current);

            return doc.ToString();
        }

        /// <summary>
        /// Re-reads <paramref name="patched"/> and checks that every hit object line and timing point survived exactly.
        /// Returns null when it matches <paramref name="expected"/>, otherwise a description of the first mismatch.
        /// Run this BEFORE replacing a file on disk.
        /// </summary>
        public static string? Verify(string patched, ParsedBeatmap expected)
        {
            ParsedBeatmap reread;
            try
            {
                reread = OsuFileDecoder.Decode(new StringReader(patched));
            }
            catch (Exception ex)
            {
                return $"The saved text could not be read back: {ex.Message}";
            }

            var want = expected.HitObjects.Select(o => o.RawLine).Where(l => !string.IsNullOrEmpty(l)).ToList();
            var got = reread.HitObjects.Select(o => o.RawLine).ToList();
            if (want.Count != got.Count)
                return $"Hit object count mismatch (expected {want.Count}, would save {got.Count}).";

            for (int i = 0; i < want.Count; i++)
            {
                if (want[i] != got[i])
                    return $"Hit object {i + 1} would be changed on save.";
            }

            var wantTiming = sortedTiming(expected.TimingPointModels);
            var gotTiming = sortedTiming(reread.TimingPointModels);
            if (wantTiming.Count != gotTiming.Count)
                return $"Timing point count mismatch (expected {wantTiming.Count}, would save {gotTiming.Count}).";

            for (int i = 0; i < wantTiming.Count; i++)
            {
                var a = wantTiming[i];
                var b = gotTiming[i];
                if (Math.Abs(a.Time - b.Time) > 0.0011 || Math.Abs(a.BeatLength - b.BeatLength) > 1e-9
                    || a.Meter != b.Meter || a.SampleSet != b.SampleSet || a.SampleIndex != b.SampleIndex
                    || a.Volume != b.Volume || a.Uninherited != b.Uninherited || a.Effects != b.Effects)
                    return $"Timing point {i + 1} would be changed on save.";
            }

            return null;
        }

        // ---------------------------------------------------------------- key/value sections

        private static void patchGeneral(Doc doc, ParsedBeatmap orig, BeatmapSaver.Edits e)
        {
            // Limits mirror EditableBeatmap's bindables (kept in sync deliberately: the editor clamps/rounds the file's
            // values when it opens it, so an unedited value must be compared AFTER the same constraint).
            if (!sameNumber(Constrain(orig.StackLeniency, 0f, 1f, 0.1f), e.StackLeniency))
                setKey(doc, "General", "StackLeniency", fmt(e.StackLeniency), true);
        }

        private static void patchMetadata(Doc doc, ParsedBeatmap orig, BeatmapSaver.Edits e)
        {
            changeText(doc, "Title", orig.Title, orig.Title, e.Title);
            // The editor shows Title/Artist in the Unicode fields when the file has none; that is not an edit.
            changeText(doc, "TitleUnicode", orig.TitleUnicode, string.IsNullOrEmpty(orig.TitleUnicode) ? orig.Title : orig.TitleUnicode, e.TitleUnicode);
            changeText(doc, "Artist", orig.Artist, orig.Artist, e.Artist);
            changeText(doc, "ArtistUnicode", orig.ArtistUnicode, string.IsNullOrEmpty(orig.ArtistUnicode) ? orig.Artist : orig.ArtistUnicode, e.ArtistUnicode);
            changeText(doc, "Creator", orig.Creator, orig.Creator, e.Creator);
            changeText(doc, "Version", orig.Version, orig.Version, e.Version);
            changeText(doc, "Source", orig.Source, orig.Source, e.Source);
            changeText(doc, "Tags", orig.Tags, orig.Tags, e.Tags);
        }

        private static void changeText(Doc doc, string key, string fileValue, string shownValue, string? edited)
        {
            // Single-line fields: a newline would corrupt the file.
            string value = (edited ?? string.Empty).Replace("\r", " ").Replace("\n", " ").Trim();
            if (value == shownValue || value == fileValue)
                return;

            setKey(doc, "Metadata", key, value, false);
        }

        private static void patchDifficulty(Doc doc, ParsedBeatmap orig, BeatmapSaver.Edits e)
        {
            changeNumber(doc, "HPDrainRate", Constrain(orig.HpDrainRate, 0f, 10f, 0.1f), e.Hp);
            changeNumber(doc, "CircleSize", Constrain(orig.CircleSize, 0f, 10f, 0.1f), e.Cs);
            changeNumber(doc, "OverallDifficulty", Constrain(orig.OverallDifficulty, 0f, 10f, 0.1f), e.Od);

            // When the file has no ApproachRate the game uses OverallDifficulty, so the "unchanged" AR is the
            // (possibly edited) OD; if the user changed OD alone, AR must now be written explicitly to keep its value.
            float arBaseline = orig.ApproachRate >= 0 ? Constrain(orig.ApproachRate, 0f, 10f, 0.1f) : e.Od;
            changeNumber(doc, "ApproachRate", arBaseline, e.Ar);

            changeNumber(doc, "SliderMultiplier", Constrain(orig.SliderMultiplier, 0.4f, 3.6f, 0.1f), e.SliderMultiplier);
            changeNumber(doc, "SliderTickRate", Constrain(orig.SliderTickRate, 1f, 4f, 1f), e.SliderTickRate);
        }

        private static void changeNumber(Doc doc, string key, float unchangedValue, float edited)
        {
            if (sameNumber(unchangedValue, edited))
                return;

            setKey(doc, "Difficulty", key, fmt(edited), false);
        }

        /// <summary>The editor's bindable constraint: clamp to [min,max] then round to the precision step.</summary>
        public static float Constrain(float value, float min, float max, float step)
        {
            float clamped = Math.Clamp(value, min, max);
            return (float)(Math.Round(clamped / step) * step);
        }

        private static bool sameNumber(float a, float b) => Math.Abs(a - b) < 0.0005f;

        private static string fmt(float value) => value.ToString("0.######", inv);

        /// <summary>Replaces <paramref name="key"/>'s value in place (keeping its separator style), or appends it to the section.</summary>
        private static void setKey(Doc doc, string section, string key, string value, bool spaceAfterColon)
        {
            var s = doc.EnsureSection(section);

            for (int i = s.Start; i < s.End; i++)
            {
                string line = doc.Lines[i];
                int colon = line.IndexOf(':');
                if (colon < 0 || line[..colon].Trim() != key)
                    continue;

                string cr = line.EndsWith('\r') ? "\r" : string.Empty;
                int end = colon + 1;
                while (end < line.Length && (line[end] == ' ' || line[end] == '\t'))
                    end++;

                doc.Lines[i] = line[..end] + value + cr;
                return;
            }

            int at = s.End;
            while (at > s.Start && doc.Lines[at - 1].Trim().Length == 0)
                at--;

            doc.Lines.Insert(at, doc.Gen(spaceAfterColon ? $"{key}: {value}" : $"{key}:{value}"));
        }

        // ---------------------------------------------------------------- bookmarks / background / colours

        private static void patchBookmarks(Doc doc, ParsedBeatmap orig, ParsedBeatmap current)
        {
            if (current.Bookmarks.SequenceEqual(orig.Bookmarks))
                return;

            string? line = current.Bookmarks.Count > 0 ? "Bookmarks: " + string.Join(",", current.Bookmarks.Select(b => b.ToString(inv))) : null;

            if (doc.TryFindSection("Editor", out var s))
            {
                for (int i = s.Start; i < s.End; i++)
                {
                    string t = doc.Lines[i];
                    int colon = t.IndexOf(':');
                    if (colon < 0 || t[..colon].Trim() != "Bookmarks")
                        continue;

                    if (line == null)
                        doc.Lines.RemoveAt(i);
                    else
                        doc.Lines[i] = doc.Gen(line);
                    return;
                }
            }

            if (line != null)
            {
                var created = doc.EnsureSection("Editor");
                doc.Lines.Insert(created.Start, doc.Gen(line));
            }
        }

        private static void patchBackground(Doc doc, ParsedBeatmap orig, BeatmapSaver.Edits e)
        {
            string name = e.BackgroundFilename;
            if (string.IsNullOrEmpty(name) || name == orig.BackgroundFilename)
                return;

            string newLine = $"0,0,\"{name}\",0,0";

            if (doc.TryFindSection("Events", out var s))
            {
                for (int i = s.Start; i < s.End; i++)
                {
                    if (!isBackgroundEventLine(doc.Lines[i].Trim()))
                        continue;

                    doc.Lines[i] = doc.Gen(newLine);
                    return;
                }
            }

            var created = doc.EnsureSection("Events");
            doc.Lines.Insert(created.Start, doc.Gen(newLine));
        }

        /// <summary>Whether an [Events] line is the background image event (type 0 with a quoted filename).</summary>
        private static bool isBackgroundEventLine(string trimmed)
        {
            string[] parts = trimmed.Split(',');
            return parts.Length >= 3 && parts[0].Trim() == "0" && parts[2].Contains('"');
        }

        private static void patchColours(Doc doc, ParsedBeatmap orig, BeatmapSaver.Edits e)
        {
            var newColours = e.ComboColours;
            var oldColours = orig.ComboColours;

            bool same = newColours.Count == oldColours.Count;
            for (int i = 0; same && i < newColours.Count; i++)
            {
                same = toByte(newColours[i].R) == toByte(oldColours[i].R)
                       && toByte(newColours[i].G) == toByte(oldColours[i].G)
                       && toByte(newColours[i].B) == toByte(oldColours[i].B);
            }

            if (same)
                return;

            var lines = newColours.Select((c, i) => doc.Gen($"Combo{i + 1} : {toByte(c.R)},{toByte(c.G)},{toByte(c.B)}")).ToList();

            if (doc.TryFindSection("Colours", out var s))
            {
                // Replace only the ComboN lines; SliderBorder / SliderTrackOverride / unknown keys stay verbatim.
                for (int i = s.End - 1; i >= s.Start; i--)
                {
                    int colon = doc.Lines[i].IndexOf(':');
                    if (colon > 0 && doc.Lines[i][..colon].Trim().StartsWith("Combo", StringComparison.OrdinalIgnoreCase))
                        doc.Lines.RemoveAt(i);
                }

                doc.Lines.InsertRange(s.Start, lines);
            }
            else if (lines.Count > 0)
            {
                var created = doc.EnsureSection("Colours");
                doc.Lines.InsertRange(created.Start, lines);
            }
        }

        private static int toByte(float component) => (int)Math.Round(Math.Clamp(component, 0f, 1f) * 255f);

        // ---------------------------------------------------------------- timing points / hit objects

        private static List<TimingPointModel> sortedTiming(IEnumerable<TimingPointModel> points) =>
            points.OrderBy(t => t.Time).ThenBy(t => t.Uninherited ? 0 : 1).ToList();

        private static void patchTimingPoints(Doc doc, ParsedBeatmap orig, ParsedBeatmap current)
        {
            var before = sortedTiming(orig.TimingPointModels);
            var after = sortedTiming(current.TimingPointModels);

            bool same = before.Count == after.Count;
            for (int i = 0; same && i < before.Count; i++)
                same = TimingPointLineEditor.SameFields(before[i], after[i]);

            if (same)
                return;

            var lines = after.Select(TimingPointLineEditor.EncodeOrOriginal).ToList();
            replaceBody(doc, "TimingPoints", lines, TimingPointLineEditor.IsRecord);
        }

        private static void patchHitObjects(Doc doc, ParsedBeatmap orig, ParsedBeatmap current)
        {
            var before = orig.HitObjects.Select(o => o.RawLine).Where(l => !string.IsNullOrEmpty(l)).ToList();
            var after = current.HitObjects.Select(o => o.RawLine).Where(l => !string.IsNullOrEmpty(l)).ToList();

            if (before.SequenceEqual(after))
                return;

            replaceBody(doc, "HitObjects", after, OsuFileDecoder.IsValidHitObjectLine);
        }

        /// <summary>
        /// Replaces a record section's body with <paramref name="records"/>. Comments and blank lines are dropped
        /// (they carry no data), but any other line the decoder did not recognise as a record is KEPT so malformed
        /// or future-format lines are never silently lost.
        /// </summary>
        private static void replaceBody(Doc doc, string name, IReadOnlyList<string> records, Func<string, bool> isRecord)
        {
            if (!doc.TryFindSection(name, out var s))
            {
                if (records.Count == 0)
                    return;

                s = doc.EnsureSection(name);
            }

            var kept = new List<string>();
            for (int i = s.Start; i < s.End; i++)
            {
                string t = doc.Lines[i].Trim();
                if (t.Length == 0 || t.StartsWith("//", StringComparison.Ordinal))
                    continue;

                if (!isRecord(t))
                    kept.Add(doc.Lines[i]);
            }

            int trailing = 0;
            while (s.End - 1 - trailing >= s.Start && doc.Lines[s.End - 1 - trailing].Trim().Length == 0)
                trailing++;

            var trailingBlanks = doc.Lines.GetRange(s.End - trailing, trailing);

            doc.Lines.RemoveRange(s.Start, s.End - s.Start);

            var replacement = new List<string>(records.Count + kept.Count + trailing);
            foreach (string record in records)
                replacement.Add(doc.Gen(record));
            replacement.AddRange(kept);
            replacement.AddRange(trailingBlanks);

            doc.Lines.InsertRange(s.Start, replacement);
        }

        // ---------------------------------------------------------------- line document

        private readonly record struct Section(string Name, int Header, int Start, int End);

        /// <summary>
        /// The file as a list of lines such that <c>string.Join("\n", Lines)</c> is exactly the original text
        /// (a trailing '\r' stays on the line it belongs to, so mixed line endings survive untouched).
        /// </summary>
        private sealed class Doc
        {
            public readonly List<string> Lines;
            private readonly bool crlf;

            public Doc(string text)
            {
                Lines = new List<string>(text.Split('\n'));

                int crlfCount = 0, lfCount = 0;
                for (int i = 0; i < Lines.Count - 1; i++)
                {
                    if (Lines[i].EndsWith('\r'))
                        crlfCount++;
                    else
                        lfCount++;
                }

                crlf = crlfCount > lfCount;
            }

            /// <summary>A generated line, using the file's dominant line ending.</summary>
            public string Gen(string line) => crlf ? line + "\r" : line;

            public override string ToString() => string.Join("\n", Lines);

            public bool TryFindSection(string name, out Section section)
            {
                int header = -1;

                for (int i = 0; i < Lines.Count; i++)
                {
                    if (!tryHeader(Lines[i], out string found))
                        continue;

                    if (header >= 0)
                    {
                        section = new Section(name, header, header + 1, i);
                        return true;
                    }

                    if (found == name)
                        header = i;
                }

                if (header >= 0)
                {
                    section = new Section(name, header, header + 1, Lines.Count);
                    return true;
                }

                section = default;
                return false;
            }

            /// <summary>Returns the section, creating it in canonical position if the file does not have it.</summary>
            public Section EnsureSection(string name)
            {
                if (TryFindSection(name, out var existing))
                    return existing;

                int rank = Array.IndexOf(section_order, name);
                int insertAt = -1;

                for (int i = 0; i < Lines.Count; i++)
                {
                    if (tryHeader(Lines[i], out string found) && Array.IndexOf(section_order, found) > rank)
                    {
                        insertAt = i;
                        break;
                    }
                }

                if (insertAt >= 0)
                {
                    Lines.Insert(insertAt, Gen(string.Empty));
                    Lines.Insert(insertAt, Gen("[" + name + "]"));
                }
                else
                {
                    // Append, keeping the final-newline marker (the empty last element) last.
                    int at = Lines.Count;
                    if (at > 0 && Lines[at - 1].Trim().Length == 0)
                        at--;

                    Lines.Insert(at, Gen("[" + name + "]"));
                    Lines.Insert(at, Gen(string.Empty));
                }

                TryFindSection(name, out var created);
                return created;
            }

            private static bool tryHeader(string line, out string name)
            {
                string t = line.Trim();
                if (t.Length >= 2 && t[0] == '[' && t[^1] == ']')
                {
                    name = t[1..^1];
                    return true;
                }

                name = string.Empty;
                return false;
            }
        }
    }
}
