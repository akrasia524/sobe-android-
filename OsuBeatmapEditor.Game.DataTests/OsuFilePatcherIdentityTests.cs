using System.Linq;
using NUnit.Framework;
using OsuBeatmapEditor.Game.Beatmaps;

namespace OsuBeatmapEditor.Game.DataTests
{
    /// <summary>A save with no real edit must give back the original file byte-for-byte; a real edit must touch only its own line.</summary>
    [TestFixture]
    public class OsuFilePatcherIdentityTests
    {
        [Test]
        public void FixtureDecodesWhatTheOtherTestsAssume()
        {
            // If this fails the fixture or decoder changed and the identity tests below would be meaningless.
            var p = Fixtures.Decode(Fixtures.Full);
            Assert.That(p.HitObjects, Has.Count.EqualTo(9));
            Assert.That(p.TimingPointModels, Has.Count.EqualTo(5));
            Assert.That(p.BackgroundFilename, Is.EqualTo("bg.jpg"), "the video event must not be taken as the background");
            Assert.That(p.Breaks, Has.Count.EqualTo(1));
            Assert.That(p.ComboColours, Has.Count.EqualTo(2));
            Assert.That(p.Bookmarks, Is.EqualTo(new[] { 1000, 2000, 3000 }));
        }

        [Test]
        public void NoEditsReturnsOriginalUnchanged() =>
            Assert.That(Fixtures.Patch(Fixtures.Full), Is.EqualTo(Fixtures.Full));

        [Test]
        public void NoEditsKeepsCrlfFilesUnchanged()
        {
            string crlf = Fixtures.Full.Replace("\n", "\r\n");
            Assert.That(Fixtures.Patch(crlf), Is.EqualTo(crlf));
        }

        [Test]
        public void NoEditsKeepsMixedLineEndingsUnchanged()
        {
            // First half CRLF, second half LF: a real-world result of hand-editing.
            string[] lines = Fixtures.Full.Split('\n');
            string mixed = string.Join("\n", lines.Select((l, i) => i < lines.Length / 2 && i < lines.Length - 1 ? l + "\r" : l));
            Assert.That(Fixtures.Patch(mixed), Is.EqualTo(mixed));
        }

        [Test]
        public void NoEditsKeepsByteOrderMark()
        {
            string bom = "﻿" + Fixtures.Full;
            Assert.That(Fixtures.Patch(bom), Is.EqualTo(bom));
        }

        [Test]
        public void NoEditsKeepsVideoEventStoryboardUnknownKeysAndCustomSection()
        {
            string patched = Fixtures.Patch(Fixtures.Full);
            Assert.That(patched, Does.Contain("Video,-150,\"video.mp4\",0,0"));
            Assert.That(patched, Does.Contain("Sprite,Background,Centre,\"sb/star.png\",320,240"));
            Assert.That(patched, Does.Contain(" M,0,1000,,100,100,200,200"));
            Assert.That(patched, Does.Contain("Sample,5000,0,\"sb/boom.wav\",80"));
            Assert.That(patched, Does.Contain("EpilepsyWarning: 1"));
            Assert.That(patched, Does.Contain("TimelineZoom: 2.5"));
            Assert.That(patched, Does.Contain("1000,333.333333333333,4,2,1,60,1,0"));
            Assert.That(patched, Does.Contain("[CustomSection]\nFoo: bar"));
        }

        [Test]
        public void VerifyAcceptsAnUnchangedSave()
        {
            var p = Fixtures.Decode(Fixtures.Full);
            Assert.That(OsuFilePatcher.Verify(Fixtures.Patch(Fixtures.Full), p), Is.Null);
        }

        [Test]
        public void VerifyRejectsALostHitObject()
        {
            var p = Fixtures.Decode(Fixtures.Full);
            var lines = Fixtures.Full.Split('\n').ToList();
            lines.RemoveAt(lines.IndexOf(Fixtures.HitObjectLines(Fixtures.Full)[2])); // drop the red-anchor slider
            Assert.That(OsuFilePatcher.Verify(string.Join("\n", lines), p), Is.Not.Null);
        }

        [Test]
        public void ChangingTheTitleChangesOnlyTheTitleLine()
        {
            string patched = Fixtures.Patch(Fixtures.Full, (_, e) => e.Title = "New Title");
            assertOnlyLinesDiffer(Fixtures.Full, patched, "Title:Test Song", "Title:New Title");
        }

        [Test]
        public void ChangingApproachRateChangesOnlyThatLine()
        {
            string patched = Fixtures.Patch(Fixtures.Full, (_, e) => e.Ar = 7.5f);
            assertOnlyLinesDiffer(Fixtures.Full, patched, "ApproachRate:9.3", "ApproachRate:7.5");
        }

        [Test]
        public void MovingOneSliderChangesOnlyThatHitObjectLineAndKeepsAllItsControlPoints()
        {
            var p = Fixtures.Decode(Fixtures.Full);
            var edits = Fixtures.EditsFor(p);
            var h = p.HitObjects[1];
            p.HitObjects[1] = h with { RawLine = HitObjectLineEditor.ShiftPosition(h.RawLine, 8, -4) };

            string patched = OsuFilePatcher.Patch(Fixtures.Full, p, edits);

            string[] before = Fixtures.HitObjectLines(Fixtures.Full);
            string[] after = Fixtures.HitObjectLines(patched);
            Assert.That(after, Has.Length.EqualTo(before.Length));
            Assert.That(after[1], Is.EqualTo("108,96,1500,6,2,B|208:96|308:196,1,150.5,2|0,1:2|0:0,0:0:0:0:"),
                "head, middle AND end anchor must all move; edge sounds/sets stay");
            for (int i = 0; i < before.Length; i++)
                if (i != 1)
                    Assert.That(after[i], Is.EqualTo(before[i]), $"hit object {i} must be untouched");

            Assert.That(OsuFilePatcher.Verify(patched, p), Is.Null);
        }

        private static void assertOnlyLinesDiffer(string before, string after, string oldLine, string newLine)
        {
            string[] a = before.Split('\n');
            string[] b = after.Split('\n');
            Assert.That(b, Has.Length.EqualTo(a.Length), "no lines may be added or removed");

            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] == oldLine)
                    Assert.That(b[i], Is.EqualTo(newLine));
                else
                    Assert.That(b[i], Is.EqualTo(a[i]), $"line {i + 1} must be untouched");
            }
        }
    }
}
