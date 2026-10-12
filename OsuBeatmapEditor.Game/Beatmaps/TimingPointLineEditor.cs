using System.Globalization;

namespace OsuBeatmapEditor.Game.Beatmaps
{
    /// <summary>
    /// Emits a <c>[TimingPoints]</c> line in the osu! stable format
    /// (<c>time,beatLength,meter,sampleSet,sampleIndex,volume,uninherited,effects</c>) from a
    /// <see cref="TimingPointModel"/>. The stable model covers the full standard line, so we re-emit it
    /// in full; osu!lazer's importer then translates these red/green lines into its own control points.
    /// </summary>
    public static class TimingPointLineEditor
    {
        public static string Encode(TimingPointModel tp)
        {
            string time = tp.Time.ToString("0.###", CultureInfo.InvariantCulture);
            string beatLength = tp.BeatLength.ToString("0.###########", CultureInfo.InvariantCulture);
            return string.Join(',',
                time,
                beatLength,
                tp.Meter.ToString(CultureInfo.InvariantCulture),
                tp.SampleSet.ToString(CultureInfo.InvariantCulture),
                tp.SampleIndex.ToString(CultureInfo.InvariantCulture),
                tp.Volume.ToString(CultureInfo.InvariantCulture),
                tp.Uninherited ? "1" : "0",
                tp.Effects.ToString(CultureInfo.InvariantCulture));
        }

        private static readonly CultureInfo inv = CultureInfo.InvariantCulture;

        /// <summary>Whether <paramref name="line"/> is a timing record the decoder accepts (time and beatLength parse).</summary>
        public static bool IsRecord(string line)
        {
            string[] parts = line.Split(',');
            return parts.Length >= 2
                   && double.TryParse(parts[0], NumberStyles.Float, inv, out _)
                   && double.TryParse(parts[1], NumberStyles.Float, inv, out _);
        }

        /// <summary>Parses a timing line exactly like <see cref="OsuFileDecoder"/> does (same defaults), or null if it is not a record.</summary>
        public static TimingPointModel? ParseLine(string line)
        {
            string[] parts = line.Split(',');
            if (parts.Length < 2
                || !double.TryParse(parts[0], NumberStyles.Float, inv, out double time)
                || !double.TryParse(parts[1], NumberStyles.Float, inv, out double beatLength))
                return null;

            bool uninherited = parts.Length >= 7 ? parts[6].Trim() == "1" : beatLength > 0;
            int effects = parts.Length >= 8 && int.TryParse(parts[7].Trim(), NumberStyles.Integer, inv, out int ef) ? ef : 0;
            int meter = parts.Length >= 3 && int.TryParse(parts[2].Trim(), NumberStyles.Integer, inv, out int m) && m > 0 ? m : 4;
            int sampleSet = parts.Length >= 4 && int.TryParse(parts[3].Trim(), NumberStyles.Integer, inv, out int ss) ? ss : 0;
            int sampleIndex = parts.Length >= 5 && int.TryParse(parts[4].Trim(), NumberStyles.Integer, inv, out int si) ? si : 0;
            int volume = parts.Length >= 6 && int.TryParse(parts[5].Trim(), NumberStyles.Integer, inv, out int vol) ? vol : 100;

            return new TimingPointModel(0, time, beatLength, meter, sampleSet, sampleIndex, volume, uninherited, effects, line);
        }

        /// <summary>Whether two timing points carry identical data (ignoring Id and RawLine).</summary>
        public static bool SameFields(TimingPointModel a, TimingPointModel b) =>
            a.Time == b.Time && a.BeatLength == b.BeatLength && a.Meter == b.Meter && a.SampleSet == b.SampleSet
            && a.SampleIndex == b.SampleIndex && a.Volume == b.Volume && a.Uninherited == b.Uninherited && a.Effects == b.Effects;

        /// <summary>
        /// The line to write for a timing point: its original text when it has not been edited (so its exact number
        /// formatting survives), otherwise a freshly encoded line.
        /// </summary>
        public static string EncodeOrOriginal(TimingPointModel tp)
        {
            if (!string.IsNullOrEmpty(tp.RawLine) && ParseLine(tp.RawLine) is { } parsed && SameFields(parsed, tp))
                return tp.RawLine;

            return Encode(tp);
        }

        /// <summary>Beat length (ms/beat) for a given BPM, for building uninherited lines.</summary>
        public static double BeatLengthFromBpm(double bpm) => bpm > 0 ? 60000.0 / bpm : 500;

        /// <summary>Encoded beat-length field for an inherited line carrying the given SV multiplier.</summary>
        public static double BeatLengthFromSv(double sv) => -100.0 / (sv <= 0 ? 1 : sv);

        /// <summary>Returns <paramref name="effects"/> with the kiai bit (bit 0) set or cleared.</summary>
        public static int WithKiai(int effects, bool kiai) => kiai ? effects | 1 : effects & ~1;
    }
}
