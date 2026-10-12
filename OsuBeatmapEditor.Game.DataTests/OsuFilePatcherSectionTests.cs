using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using osu.Framework.Graphics;
using OsuBeatmapEditor.Game.Beatmaps;

namespace OsuBeatmapEditor.Game.DataTests
{
    /// <summary>Real edits to timing points, background, bookmarks and colours, plus unparseable lines and a missing ApproachRate.</summary>
    [TestFixture]
    public class OsuFilePatcherSectionTests
    {
        // ---- helpers -------------------------------------------------------------------------------------------

        /// <summary>Non-blank lines of a [Section] (header excluded).</summary>
        private static List<string> body(string text, string name)
        {
            var result = new List<string>();
            bool inside = false;

            foreach (string line in text.Split('\n').Select(l => l.TrimEnd('\r')))
            {
                if (line.StartsWith('['))
                {
                    inside = line == $"[{name}]";
                    continue;
                }

                if (inside && line.Length > 0)
                    result.Add(line);
            }

            return result;
        }

        /// <summary>The whole file with the given section's body removed, to prove nothing OUTSIDE it changed.</summary>
        private static string withoutBody(string text, string name)
        {
            var lines = text.Split('\n').ToList();
            int header = lines.FindIndex(l => l.TrimEnd('\r') == $"[{name}]");
            int end = header + 1;
            while (end < lines.Count && !lines[end].StartsWith('['))
                end++;
            lines.RemoveRange(header + 1, end - header - 1);
            return string.Join("\n", lines);
        }

        private static string[] timingLines(string text) => body(text, "TimingPoints").ToArray();

        // ---- timing points -------------------------------------------------------------------------------------

        [Test]
        public void EditingOneTimingPointRewritesOnlyThatLineAndKeepsTheOthersByteForByte()
        {
            string[] before = timingLines(Fixtures.Full);

            string patched = Fixtures.Patch(Fixtures.Full, (p, _) =>
            {
                int i = p.TimingPointModels.FindIndex(t => t.Time == 5000);
                p.TimingPointModels[i] = p.TimingPointModels[i] with { Volume = 90 };
            });

            string[] after = timingLines(patched);
            Assert.That(after, Has.Length.EqualTo(5));

            for (int i = 0; i < before.Length; i++)
            {
                if (i == 2)
                    continue;
                Assert.That(after[i], Is.EqualTo(before[i]), $"timing line {i} must stay verbatim (e.g. the 15-digit beat length)");
            }

            var edited = TimingPointLineEditor.ParseLine(after[2])!.Value;
            Assert.That(edited.Time, Is.EqualTo(5000));
            Assert.That(edited.Volume, Is.EqualTo(90));
            Assert.That(edited.BeatLength, Is.EqualTo(-50));
            Assert.That(edited.Kiai, Is.True, "kiai flag of the edited point must survive");

            Assert.That(withoutBody(patched, "TimingPoints"), Is.EqualTo(withoutBody(Fixtures.Full, "TimingPoints")), "nothing outside [TimingPoints] may change");
        }

        [Test]
        public void AddingATimingPointInsertsItInTimeOrder()
        {
            string patched = Fixtures.Patch(Fixtures.Full, (p, _) =>
                p.TimingPointModels.Add(new TimingPointModel(99, 7000, -75, 4, 2, 1, 65, false, 0)));

            string[] after = timingLines(patched);
            Assert.That(after, Has.Length.EqualTo(6));

            var times = after.Select(l => TimingPointLineEditor.ParseLine(l)!.Value.Time).ToArray();
            Assert.That(times, Is.EqualTo(new[] { 1000d, 1000d, 5000d, 7000d, 9000d, 12000d }));

            var added = TimingPointLineEditor.ParseLine(after[3])!.Value;
            Assert.That(added.BeatLength, Is.EqualTo(-75));
            Assert.That(added.Volume, Is.EqualTo(65));
            Assert.That(added.Uninherited, Is.False);

            // the red line still sorts before the green line at the same time
            Assert.That(TimingPointLineEditor.ParseLine(after[0])!.Value.Uninherited, Is.True);
            Assert.That(after[0], Is.EqualTo("1000,333.333333333333,4,2,1,60,1,0"));
        }

        [Test]
        public void DeletingATimingPointRemovesOnlyItsLine()
        {
            string[] before = timingLines(Fixtures.Full);

            string patched = Fixtures.Patch(Fixtures.Full, (p, _) => p.TimingPointModels.RemoveAll(t => t.Time == 9000));

            Assert.That(timingLines(patched), Is.EqualTo(before.Where(l => !l.StartsWith("9000,")).ToArray()));
            Assert.That(withoutBody(patched, "TimingPoints"), Is.EqualTo(withoutBody(Fixtures.Full, "TimingPoints")));
        }

        // ---- background ----------------------------------------------------------------------------------------

        [Test]
        public void ReplacingTheBackgroundChangesOnlyTheBackgroundEventAndKeepsTheVideo()
        {
            string patched = Fixtures.Patch(Fixtures.Full, (_, e) => e.BackgroundFilename = "new bg.png");

            string expected = Fixtures.Full.Replace("0,0,\"bg.jpg\",0,0", "0,0,\"new bg.png\",0,0");
            Assert.That(patched, Is.EqualTo(expected));
            Assert.That(patched, Does.Contain("Video,-150,\"video.mp4\",0,0"));
            Assert.That(Fixtures.Decode(patched).BackgroundFilename, Is.EqualTo("new bg.png"));
        }

        [Test]
        public void SettingTheSameBackgroundNameChangesNothing() =>
            Assert.That(Fixtures.Patch(Fixtures.Full, (_, e) => e.BackgroundFilename = "bg.jpg"), Is.EqualTo(Fixtures.Full));

        [Test]
        public void AddingABackgroundToAMapWithoutOneKeepsTheVideoAndStoryboard()
        {
            string noBackground = Fixtures.Full.Replace("0,0,\"bg.jpg\",0,0\n", "");
            Assert.That(Fixtures.Decode(noBackground).BackgroundFilename, Is.Empty, "fixture sanity");

            string patched = Fixtures.Patch(noBackground, (_, e) => e.BackgroundFilename = "new.png");

            Assert.That(Fixtures.Decode(patched).BackgroundFilename, Is.EqualTo("new.png"));
            var events = body(patched, "Events");
            Assert.That(events, Does.Contain("Video,-150,\"video.mp4\",0,0"));
            Assert.That(events, Does.Contain("Sprite,Background,Centre,\"sb/star.png\",320,240"));
            Assert.That(events, Does.Contain("2,40000,45000"));
        }

        // ---- bookmarks -----------------------------------------------------------------------------------------

        [Test]
        public void AddingABookmarkChangesOnlyTheBookmarksLine()
        {
            string patched = Fixtures.Patch(Fixtures.Full, (p, _) => p.Bookmarks.Add(4000));
            Assert.That(patched, Is.EqualTo(Fixtures.Full.Replace("Bookmarks: 1000,2000,3000", "Bookmarks: 1000,2000,3000,4000")));
        }

        [Test]
        public void RemovingOneBookmarkChangesOnlyTheBookmarksLine()
        {
            string patched = Fixtures.Patch(Fixtures.Full, (p, _) => p.Bookmarks.Remove(2000));
            Assert.That(patched, Is.EqualTo(Fixtures.Full.Replace("Bookmarks: 1000,2000,3000", "Bookmarks: 1000,3000")));
        }

        [Test]
        public void RemovingAllBookmarksDropsTheLineButKeepsTheOtherEditorKeys()
        {
            string patched = Fixtures.Patch(Fixtures.Full, (p, _) => p.Bookmarks.Clear());

            var editor = body(patched, "Editor");
            Assert.That(editor, Is.EqualTo(new[] { "DistanceSpacing: 1.1", "BeatDivisor: 4", "GridSize: 8", "TimelineZoom: 2.5" }));
            Assert.That(Fixtures.Decode(patched).Bookmarks, Is.Empty);
        }

        // ---- colours -------------------------------------------------------------------------------------------

        [Test]
        public void ChangingComboColoursReplacesOnlyComboLinesAndKeepsSliderBorder()
        {
            string patched = Fixtures.Patch(Fixtures.Full, (_, e) =>
                e.ComboColours = new List<Colour4> { new Colour4(255, 0, 0, 255), new Colour4(1, 2, 3, 255), new Colour4(0, 0, 255, 255) });

            Assert.That(body(patched, "Colours"), Is.EqualTo(new[]
            {
                "Combo1 : 255,0,0",
                "Combo2 : 1,2,3",
                "Combo3 : 0,0,255",
                "SliderBorder : 255,255,255",
            }));
            Assert.That(withoutBody(patched, "Colours"), Is.EqualTo(withoutBody(Fixtures.Full, "Colours")));
            Assert.That(Fixtures.Decode(patched).ComboColours, Has.Count.EqualTo(3));
        }

        [Test]
        public void RemovingAllComboColoursKeepsSliderBorder()
        {
            string patched = Fixtures.Patch(Fixtures.Full, (_, e) => e.ComboColours = new List<Colour4>());

            Assert.That(body(patched, "Colours"), Is.EqualTo(new[] { "SliderBorder : 255,255,255" }));
            Assert.That(Fixtures.Decode(patched).ComboColours, Is.Empty);
        }

        // ---- unparseable lines ---------------------------------------------------------------------------------

        [Test]
        public void UnparseableHitObjectAndTimingLinesSurviveARealEdit()
        {
            const string badHit = "this,is,not,a,hitobject";
            const string badTiming = "not a timing point";

            string text = Fixtures.Full
                .Replace("[HitObjects]\n", "[HitObjects]\n" + badHit + "\n")
                .Replace("[TimingPoints]\n", "[TimingPoints]\n" + badTiming + "\n");

            var parsed = Fixtures.Decode(text);
            Assert.That(parsed.HitObjects, Has.Count.EqualTo(9), "fixture sanity: the bad line is not a hit object");
            Assert.That(parsed.TimingPointModels, Has.Count.EqualTo(5), "fixture sanity: the bad line is not a timing point");

            // Make real hit-object AND timing edits so both sections are rebuilt.
            var edits = Fixtures.EditsFor(parsed);
            var h = parsed.HitObjects[0];
            parsed.HitObjects[0] = h with { RawLine = HitObjectLineEditor.ShiftPosition(h.RawLine, 1, 1) };
            parsed.TimingPointModels[1] = parsed.TimingPointModels[1] with { Volume = 10 };

            string patched = OsuFilePatcher.Patch(text, parsed, edits);

            Assert.That(body(patched, "HitObjects"), Does.Contain(badHit));
            Assert.That(body(patched, "TimingPoints"), Does.Contain(badTiming));
            Assert.That(body(patched, "HitObjects")[0], Is.EqualTo("257,193,1000,5,0,0:0:0:0:"), "the real edit was applied");
            Assert.That(Fixtures.Decode(patched).HitObjects, Has.Count.EqualTo(9));
            Assert.That(OsuFilePatcher.Verify(patched, parsed), Is.Null);
        }

        // ---- missing ApproachRate ------------------------------------------------------------------------------

        private static string withoutApproachRate => Fixtures.Full.Replace("ApproachRate:9.3\n", "");

        [Test]
        public void AMapWithoutApproachRateIsNotGivenOneWhenNothingChanged()
        {
            Assert.That(Fixtures.Decode(withoutApproachRate).ApproachRate, Is.LessThan(0), "fixture sanity");
            Assert.That(Fixtures.Patch(withoutApproachRate), Is.EqualTo(withoutApproachRate));
        }

        [Test]
        public void ChangingApproachRateOnAMapWithoutOneInsertsTheLineAtTheEndOfDifficulty()
        {
            string patched = Fixtures.Patch(withoutApproachRate, (_, e) => e.Ar = 7f);

            Assert.That(body(patched, "Difficulty").Select(l => l.Replace(" ", "")), Is.EqualTo(new[]
            {
                "HPDrainRate:5", "CircleSize:4", "OverallDifficulty:8.5", "SliderMultiplier:1.8", "SliderTickRate:2", "ApproachRate:7",
            }));

            var reread = Fixtures.Decode(patched);
            Assert.That(reread.ApproachRate, Is.EqualTo(7f));
            Assert.That(reread.EffectiveApproachRate, Is.EqualTo(7f));
            Assert.That(withoutBody(patched, "Difficulty"), Is.EqualTo(withoutBody(withoutApproachRate, "Difficulty")));
        }
    }
}
