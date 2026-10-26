using NUnit.Framework;
using OsuBeatmapEditor.Game.Beatmaps;

namespace OsuBeatmapEditor.Game.DataTests
{
    [TestFixture]
    public class NewBeatmapTimingTests
    {
        private static string timingLine(NewBeatmapRequest request)
        {
            string text = BeatmapArchiveWriter.BuildOsuText(request, "audio.mp3", uninheritedTimingOnly: false);
            int start = text.IndexOf("[TimingPoints]\n", System.StringComparison.Ordinal) + "[TimingPoints]\n".Length;
            return text.Substring(start, text.IndexOf('\n', start) - start);
        }

        [Test]
        public void DefaultsKeepTheOldBehaviour()
        {
            Assert.That(timingLine(new NewBeatmapRequest { Bpm = 120 }), Is.EqualTo("0,500,4,1,0,100,1,0"));
        }

        [Test]
        public void OffsetAndMeterAreWritten()
        {
            Assert.That(timingLine(new NewBeatmapRequest { Bpm = 150, Offset = 1234, Meter = 3 }), Is.EqualTo("1234,400,3,1,0,100,1,0"));
        }

        [Test]
        public void NegativeOffsetIsAllowed()
        {
            Assert.That(timingLine(new NewBeatmapRequest { Bpm = 120, Offset = -80 }), Does.StartWith("-80,"));
        }

        [Test]
        public void MeterIsClamped()
        {
            Assert.That(timingLine(new NewBeatmapRequest { Bpm = 120, Meter = 0 }).Split(',')[2], Is.EqualTo("1"));
            Assert.That(timingLine(new NewBeatmapRequest { Bpm = 120, Meter = 99 }).Split(',')[2], Is.EqualTo("16"));
        }

        [Test]
        public void FractionalOffsetRoundsToWholeMilliseconds()
        {
            Assert.That(timingLine(new NewBeatmapRequest { Bpm = 120, Offset = 100.6 }), Does.StartWith("101,"));
        }
    }
}
