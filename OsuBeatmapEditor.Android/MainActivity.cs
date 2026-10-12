using Android.App;
using Android.Content.PM;
using osu.Framework.Android;
using OsuBeatmapEditor.Game;

namespace OsuBeatmapEditor.Android
{
    /// <summary>The single activity hosting the osu!framework game surface.</summary>
    [Activity(
        Label = "sobe",
        ConfigurationChanges = DEFAULT_CONFIG_CHANGES,
        Exported = true,
        LaunchMode = DEFAULT_LAUNCH_MODE,
        MainLauncher = true,
        // Follow the device sensor so the editor works in both portrait and landscape.
        ScreenOrientation = ScreenOrientation.FullUser)]
    public class MainActivity : AndroidGameActivity
    {
        protected override osu.Framework.Game CreateGame() => new OsuBeatmapEditorGame();
    }
}
