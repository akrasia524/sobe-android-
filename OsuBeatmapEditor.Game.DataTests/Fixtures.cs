using System.IO;
using System.Linq;
using OsuBeatmapEditor.Game.Beatmaps;

namespace OsuBeatmapEditor.Game.DataTests
{
    /// <summary>Shared .osu fixtures and helpers. The fixture deliberately contains every kind of data a save must not lose.</summary>
    public static class Fixtures
    {
        /// <summary>
        /// A v14 map with: unknown [General]/[Editor] keys, Unicode metadata, a video event, storyboard commands,
        /// a break, long-precision / kiai / inherited timing points, custom colours, and sliders of every curve type
        /// (including a red-anchor duplicate, a perfect curve with repeats + hitsample filename, fractional coordinates).
        /// </summary>
        public static string Full => """
osu file format v14

[General]
AudioFilename: audio.mp3
AudioLeadIn: 0
PreviewTime: 12345
Countdown: 0
SampleSet: Soft
StackLeniency: 0.7
Mode: 0
LetterboxInBreaks: 0
EpilepsyWarning: 1
WidescreenStoryboard: 1

[Editor]
Bookmarks: 1000,2000,3000
DistanceSpacing: 1.1
BeatDivisor: 4
GridSize: 8
TimelineZoom: 2.5

[Metadata]
Title:Test Song
TitleUnicode:テスト
Artist:Test Artist
ArtistUnicode:テストアーティスト
Creator:Mapper
Version:Insane
Source:Game
Tags:tag1 tag2
BeatmapID:123
BeatmapSetID:456

[Difficulty]
HPDrainRate:5
CircleSize:4
OverallDifficulty:8.5
ApproachRate:9.3
SliderMultiplier:1.8
SliderTickRate:2

[Events]
//Background and Video events
0,0,"bg.jpg",0,0
Video,-150,"video.mp4",0,0
//Break Periods
2,40000,45000
//Storyboard Layer 0 (Background)
Sprite,Background,Centre,"sb/star.png",320,240
 F,0,1000,2000,0,1
 M,0,1000,,100,100,200,200
//Storyboard Layer 1 (Fail)
//Storyboard Layer 2 (Pass)
//Storyboard Layer 3 (Foreground)
//Storyboard Layer 4 (Overlay)
//Storyboard Sound Samples
Sample,5000,0,"sb/boom.wav",80

[TimingPoints]
1000,333.333333333333,4,2,1,60,1,0
1000,-100,4,2,1,60,0,0
5000,-50,4,2,1,70,0,1
9000,-100,4,2,1,70,0,0
12000,285.714285714286,3,1,0,80,1,0

[Colours]
Combo1 : 255,0,0
Combo2 : 0,255,0
SliderBorder : 255,255,255

[HitObjects]
256,192,1000,5,0,0:0:0:0:
100,100,1500,6,2,B|200:100|300:200,1,150.5,2|0,1:2|0:0,0:0:0:0:
50,50,2000,2,0,B|100:50|100:50|150:100,1,200,0|0,0:0|0:0,0:0:0:0:
0,0,3000,2,4,P|120:20|200:80,3,200.000006,4|0|0|0,0:0|0:0|0:0|0:0,3:2:1:50:custom.wav
0,0,4000,2,0,L|100:0|100:100|0:100,2,300,0|0|0,0:0|0:0|0:0,0:0:0:0:
10,10,4500,2,0,C|20:5|30:10|40:5,1,60,0|0,0:0|0:0,0:0:0:0:
100.5,200.25,5000,2,0,B|150.5:250.25|200.75:200.5,1,120,0|0,0:0|0:0,0:0:0:0:
256,192,5500,1,8,0:0:0:0:
256,192,6000,12,0,8000,0:0:0:0:

[CustomSection]
Foo: bar
""".ReplaceLineEndings("\n") + "\n";

        /// <summary>The raw hit object lines of <see cref="Full"/> (in order).</summary>
        public static string[] HitObjectLines(string text)
        {
            var lines = text.Split('\n').Select(l => l.TrimEnd('\r')).ToList();
            int start = lines.IndexOf("[HitObjects]") + 1;
            return lines.Skip(start).TakeWhile(l => l.Length > 0).ToArray();
        }

        public static ParsedBeatmap Decode(string text) => OsuFileDecoder.Decode(new StringReader(text));

        /// <summary>
        /// The values the editor holds right after opening <paramref name="p"/>: it constrains difficulty values to its
        /// slider ranges and fills empty Unicode names from the plain ones (mirrors EditableBeatmap's constructor).
        /// </summary>
        public static BeatmapSaver.Edits EditsFor(ParsedBeatmap p) => new BeatmapSaver.Edits
        {
            Title = p.Title,
            TitleUnicode = string.IsNullOrEmpty(p.TitleUnicode) ? p.Title : p.TitleUnicode,
            Artist = p.Artist,
            ArtistUnicode = string.IsNullOrEmpty(p.ArtistUnicode) ? p.Artist : p.ArtistUnicode,
            Creator = p.Creator,
            Version = p.Version,
            Source = p.Source,
            Tags = p.Tags,
            Hp = OsuFilePatcher.Constrain(p.HpDrainRate, 0f, 10f, 0.1f),
            Cs = OsuFilePatcher.Constrain(p.CircleSize, 0f, 10f, 0.1f),
            Ar = OsuFilePatcher.Constrain(p.EffectiveApproachRate, 0f, 10f, 0.1f),
            Od = OsuFilePatcher.Constrain(p.OverallDifficulty, 0f, 10f, 0.1f),
            StackLeniency = OsuFilePatcher.Constrain(p.StackLeniency, 0f, 1f, 0.1f),
            SliderMultiplier = OsuFilePatcher.Constrain(p.SliderMultiplier, 0.4f, 3.6f, 0.1f),
            SliderTickRate = OsuFilePatcher.Constrain(p.SliderTickRate, 1f, 4f, 1f),
            ComboColours = p.ComboColours.ToList(),
            BackgroundFilename = string.Empty,
        };

        /// <summary>Patches <paramref name="original"/> after letting <paramref name="mutate"/> change the parsed map / edits.</summary>
        public static string Patch(string original, System.Action<ParsedBeatmap, BeatmapSaver.Edits>? mutate = null)
        {
            var parsed = Decode(original);
            var edits = EditsFor(parsed);
            mutate?.Invoke(parsed, edits);
            return OsuFilePatcher.Patch(original, parsed, edits);
        }
    }
}
