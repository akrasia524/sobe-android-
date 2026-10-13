using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using osu.Framework.Android;
using OsuBeatmapEditor.Game;
using OsuBeatmapEditor.Game.Library;

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
        protected override void OnCreate(Bundle? savedInstanceState)
        {
            // Must be installed before the game thread starts so early crashes are shown on screen.
            CrashReporter.Install(this);
            NativeLibraries.Preload(this);
            base.OnCreate(savedInstanceState);
            LibraryActions.Publisher = publishToDownloads;
        }

        /// <summary>
        /// Copies an exported .osz into the public Downloads/sobe folder through MediaStore (Android 10+, no storage
        /// permission needed). The system never overwrites: a name clash gets a "(1)" suffix. Returns where it went, or null.
        /// </summary>
        private string? publishToDownloads(string path)
        {
            if (Build.VERSION.SdkInt < BuildVersionCodes.Q)
                return null; // older phones: the game keeps the file in its own export folder instead

            global::Android.Net.Uri? uri = null;

            try
            {
                var values = new ContentValues();
                values.Put("_display_name", System.IO.Path.GetFileName(path));
                values.Put("mime_type", "application/octet-stream");
                values.Put("relative_path", "Download/sobe");

                uri = ContentResolver!.Insert(global::Android.Net.Uri.Parse("content://media/external/downloads")!, values);
                if (uri == null)
                    return null;

                using (var input = System.IO.File.OpenRead(path))
                using (var output = ContentResolver.OpenOutputStream(uri)!)
                    input.CopyTo(output);

                return "your Downloads/sobe folder";
            }
            catch (System.Exception)
            {
                if (uri != null)
                {
                    try { ContentResolver!.Delete(uri, null, null); }
                    catch (System.Exception) { }
                }

                return null;
            }
        }

        protected override osu.Framework.Game CreateGame() => new OsuBeatmapEditorGame();
    }
}
