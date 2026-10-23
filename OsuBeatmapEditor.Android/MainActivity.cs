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
        // Landscape only (either way up, following the sensor): the editor is designed for a wide screen and portrait is not supported.
        ScreenOrientation = ScreenOrientation.SensorLandscape)]
    public class MainActivity : AndroidGameActivity
    {
        protected override void OnCreate(Bundle? savedInstanceState)
        {
            // Must be installed before the game thread starts so early crashes are shown on screen.
            CrashReporter.Install(this);
            NativeLibraries.Preload(this);
            lockLandscape();
            base.OnCreate(savedInstanceState);
            lockLandscape();
            LibraryActions.Publisher = publishToDownloads;
            PlatformPicker.PickAudio = startAudioPick;
            PlatformPicker.PickMap = startMapPick;
            PlatformPicker.PickImage = startImagePick;
        }

        // Landscape only, enforced in code as well as by the [Activity] attribute: some phones/ROMs let the surface
        // rotate to portrait when the game window is (re)created, so the request is re-applied on every lifecycle step.
        private void lockLandscape()
        {
            if (RequestedOrientation != ScreenOrientation.SensorLandscape)
                RequestedOrientation = ScreenOrientation.SensorLandscape;
        }

        protected override void OnResume()
        {
            lockLandscape();
            base.OnResume();
        }

        public override void OnConfigurationChanged(global::Android.Content.Res.Configuration newConfig)
        {
            lockLandscape();
            base.OnConfigurationChanged(newConfig);
        }

        public override void OnWindowFocusChanged(bool hasFocus)
        {
            if (hasFocus)
                lockLandscape();
            base.OnWindowFocusChanged(hasFocus);
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

        private const int audio_pick_request = 0x50AD;
        private System.Action<PickResult>? pendingAudioPick;

        /// <summary>
        /// Opens Android's chooser for one audio file. GET_CONTENT inside a chooser lists the system picker and every installed
        /// file manager that supports it (the plain system picker alone makes searching hard).
        /// </summary>
        private void startAudioPick(System.Action<PickResult> done)
        {
            pendingAudioPick = done;

            try
            {
                var intent = new Intent(Intent.ActionGetContent);
                intent.AddCategory(Intent.CategoryOpenable);
                intent.SetType("*/*");
                intent.PutExtra(Intent.ExtraMimeTypes, new[] { "audio/*", "application/ogg" });
                intent.PutExtra(Intent.ExtraAllowMultiple, false);

                StartActivityForResult(Intent.CreateChooser(intent, "Choose audio file"), audio_pick_request);
            }
            catch (System.Exception e)
            {
                pendingAudioPick = null;
                done(new PickResult(null, "Could not open a file chooser: " + e.Message));
            }
        }

        private const int image_pick_request = 0x50AF;
        private System.Action<PickResult>? pendingImagePick;

        /// <summary>Opens the same chooser for one background image (.jpg / .png).</summary>
        private void startImagePick(System.Action<PickResult> done)
        {
            pendingImagePick = done;

            try
            {
                var intent = new Intent(Intent.ActionGetContent);
                intent.AddCategory(Intent.CategoryOpenable);
                intent.SetType("*/*");
                intent.PutExtra(Intent.ExtraMimeTypes, new[] { "image/jpeg", "image/png" });
                intent.PutExtra(Intent.ExtraAllowMultiple, false);

                StartActivityForResult(Intent.CreateChooser(intent, "Choose a background image"), image_pick_request);
            }
            catch (System.Exception e)
            {
                pendingImagePick = null;
                done(new PickResult(null, "Could not open a file chooser: " + e.Message));
            }
        }

        /// <summary>Copies the chosen image into the app cache (never touching the original); the extension follows the real image type.</summary>
        private PickResult copyPickedImage(global::Android.Net.Uri uri)
        {
            string? dir = null;

            try
            {
                string? displayName = null;
                using (var cursor = ContentResolver!.Query(uri, new[] { "_display_name" }, null, null, null))
                {
                    if (cursor != null && cursor.MoveToFirst())
                        displayName = cursor.GetString(0);
                }

                dir = System.IO.Path.Combine(CacheDir!.AbsolutePath, "image-pick", System.Guid.NewGuid().ToString("N"));
                System.IO.Directory.CreateDirectory(dir);
                string temp = System.IO.Path.Combine(dir, "picked.bin");

                using (var input = ContentResolver.OpenInputStream(uri))
                {
                    if (input == null)
                        return new PickResult(null, "The chosen app did not provide the file.");

                    using var output = System.IO.File.Create(temp);
                    byte[] buffer = new byte[81920];
                    long total = 0;
                    int n;

                    while ((n = input.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        total += n;
                        if (total > PickedImage.MAX_BYTES)
                            throw new System.IO.IOException($"the file is larger than {PickedImage.MAX_BYTES / 1024 / 1024} MB");

                        output.Write(buffer, 0, n);
                    }
                }

                byte[] head = new byte[8];
                int read;
                using (var fs = System.IO.File.OpenRead(temp))
                    read = fs.Read(head, 0, head.Length);

                string? ext = PickedImage.SniffExtension(head, read);
                if (ext == null)
                {
                    tryDeleteDir(dir);
                    return new PickResult(null, "That file is not a .jpg or .png image.");
                }

                string target = System.IO.Path.Combine(dir, PickedImage.FinalName(displayName, uri.LastPathSegment, ext));
                System.IO.File.Move(temp, target);
                return new PickResult(target, null);
            }
            catch (System.Exception e)
            {
                tryDeleteDir(dir);
                return new PickResult(null, "Could not read the chosen file: " + e.Message);
            }
        }

        private const int map_pick_request = 0x50AE;
        private System.Action<PickResult>? pendingMapPick;

        /// <summary>
        /// Opens the same chooser for a beatmap file (.osz / .zip / .osu). No MIME filter: file managers report .osz as
        /// octet-stream, so a filter would hide it. The import itself recognises the file by its contents.
        /// </summary>
        private void startMapPick(System.Action<PickResult> done)
        {
            pendingMapPick = done;

            try
            {
                var intent = new Intent(Intent.ActionGetContent);
                intent.AddCategory(Intent.CategoryOpenable);
                intent.SetType("*/*");
                intent.PutExtra(Intent.ExtraAllowMultiple, false);

                StartActivityForResult(Intent.CreateChooser(intent, "Choose a beatmap (.osz / .zip)"), map_pick_request);
            }
            catch (System.Exception e)
            {
                pendingMapPick = null;
                done(new PickResult(null, "Could not open a file chooser: " + e.Message));
            }
        }

        private const long max_map_bytes = 1024L * 1024 * 1024;

        /// <summary>Copies the chosen beatmap file into the app cache (keeping its name) so the import can read it as a normal file.</summary>
        private PickResult copyPickedMap(global::Android.Net.Uri uri)
        {
            string? dir = null;

            try
            {
                string? displayName = null;
                using (var cursor = ContentResolver!.Query(uri, new[] { "_display_name" }, null, null, null))
                {
                    if (cursor != null && cursor.MoveToFirst())
                        displayName = cursor.GetString(0);
                }

                string raw = !string.IsNullOrWhiteSpace(displayName) ? displayName! : uri.LastPathSegment ?? "beatmap";
                int cut = raw.LastIndexOfAny(new[] { '/', '\\', ':' });
                if (cut >= 0)
                    raw = raw.Substring(cut + 1);

                string name = LibraryActions.SafeFileName(raw);

                dir = System.IO.Path.Combine(CacheDir!.AbsolutePath, "map-pick", System.Guid.NewGuid().ToString("N"));
                System.IO.Directory.CreateDirectory(dir);
                string target = System.IO.Path.Combine(dir, name);

                using (var input = ContentResolver.OpenInputStream(uri))
                {
                    if (input == null)
                        return new PickResult(null, "The chosen app did not provide the file.");

                    using var output = System.IO.File.Create(target);
                    byte[] buffer = new byte[81920];
                    long total = 0;
                    int n;

                    while ((n = input.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        total += n;
                        if (total > max_map_bytes)
                            throw new System.IO.IOException("the file is larger than 1 GB");

                        output.Write(buffer, 0, n);
                    }
                }

                return new PickResult(target, null);
            }
            catch (System.Exception e)
            {
                tryDeleteDir(dir);
                return new PickResult(null, "Could not read the chosen file: " + e.Message);
            }
        }

        protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
        {
            if (requestCode == map_pick_request)
            {
                var mapDone = pendingMapPick;
                pendingMapPick = null;
                if (mapDone == null)
                    return;

                var mapUri = data?.Data ?? data?.ClipData?.GetItemAt(0)?.Uri;
                if (resultCode != Result.Ok || mapUri == null)
                {
                    mapDone(new PickResult(null, null)); // cancelled
                    return;
                }

                System.Threading.Tasks.Task.Run(() => mapDone(copyPickedMap(mapUri)));
                return;
            }

            if (requestCode == image_pick_request)
            {
                var imageDone = pendingImagePick;
                pendingImagePick = null;
                if (imageDone == null)
                    return;

                var imageUri = data?.Data ?? data?.ClipData?.GetItemAt(0)?.Uri;
                if (resultCode != Result.Ok || imageUri == null)
                {
                    imageDone(new PickResult(null, null)); // cancelled
                    return;
                }

                System.Threading.Tasks.Task.Run(() => imageDone(copyPickedImage(imageUri)));
                return;
            }

            if (requestCode != audio_pick_request)
            {
                base.OnActivityResult(requestCode, resultCode, data);
                return;
            }

            var done = pendingAudioPick;
            pendingAudioPick = null;
            if (done == null)
                return;

            var uri = data?.Data ?? data?.ClipData?.GetItemAt(0)?.Uri;
            if (resultCode != Result.Ok || uri == null)
            {
                done(new PickResult(null, null)); // cancelled
                return;
            }

            // Copy while the temporary read permission is still valid; do it off the UI thread (files can be large).
            System.Threading.Tasks.Task.Run(() => done(copyPickedAudio(uri)));
        }

        /// <summary>Copies the chosen document into the app cache under a safe name. Never touches the original.</summary>
        private PickResult copyPickedAudio(global::Android.Net.Uri uri)
        {
            string? dir = null;

            try
            {
                string? displayName = null;
                using (var cursor = ContentResolver!.Query(uri, new[] { "_display_name" }, null, null, null))
                {
                    if (cursor != null && cursor.MoveToFirst())
                        displayName = cursor.GetString(0);
                }

                string name = PickedAudio.ResolveFileName(displayName, ContentResolver.GetType(uri), uri.LastPathSegment);

                dir = System.IO.Path.Combine(CacheDir!.AbsolutePath, "audio-pick", System.Guid.NewGuid().ToString("N"));
                System.IO.Directory.CreateDirectory(dir);
                string target = System.IO.Path.Combine(dir, name);

                using (var input = ContentResolver.OpenInputStream(uri))
                {
                    if (input == null)
                        return new PickResult(null, "The chosen app did not provide the file.");

                    using var output = System.IO.File.Create(target);
                    byte[] buffer = new byte[81920];
                    long total = 0;
                    int n;

                    while ((n = input.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        total += n;
                        if (total > PickedAudio.MAX_BYTES)
                            throw new System.IO.IOException($"the file is larger than {PickedAudio.MAX_BYTES / 1024 / 1024} MB");

                        output.Write(buffer, 0, n);
                    }
                }

                string? problem = PickedAudio.Validate(target);
                if (problem != null)
                {
                    tryDeleteDir(dir);
                    return new PickResult(null, problem);
                }

                return new PickResult(target, null);
            }
            catch (System.Exception e)
            {
                tryDeleteDir(dir);
                return new PickResult(null, "Could not read the chosen file: " + e.Message);
            }
        }

        private static void tryDeleteDir(string? dir)
        {
            if (dir == null)
                return;

            try { System.IO.Directory.Delete(dir, true); }
            catch (System.Exception) { }
        }

        protected override osu.Framework.Game CreateGame() => new OsuBeatmapEditorGame();
    }
}
