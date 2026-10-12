using System;
using System.IO;
using System.Linq;
using System.Text;
using Android.Content;
using Android.Util;

namespace OsuBeatmapEditor.Android
{
    /// <summary>
    /// Loads the BASS audio libraries explicitly and records what happened. The game's P/Invokes use the bare
    /// name "bass"; pre-loading puts libbass.so into the process so that lookup succeeds, and the recorded report
    /// (included in crash screens) shows the real dlopen error if it does not.
    /// </summary>
    internal static class NativeLibraries
    {
        public static string Report { get; private set; } = "(native library preload did not run)";

        public static void Preload(Context context)
        {
            var sb = new StringBuilder();

            try
            {
                string? dir = context.ApplicationInfo?.NativeLibraryDir;
                sb.AppendLine("nativeLibraryDir: " + dir);

                if (dir != null && Directory.Exists(dir))
                {
                    foreach (string file in Directory.GetFiles(dir).OrderBy(f => f))
                        sb.AppendLine($"  {Path.GetFileName(file)} ({new FileInfo(file).Length} bytes)");
                }
                else
                {
                    sb.AppendLine("  (directory missing)");
                }
            }
            catch (Exception ex)
            {
                sb.AppendLine("could not list native libraries: " + ex.Message);
            }

            // Order matters: bass_fx and bassmix depend on bass.
            foreach (string name in new[] { "bass", "bass_fx", "bassmix" })
            {
                try
                {
                    Java.Lang.JavaSystem.LoadLibrary(name);
                    sb.AppendLine($"LoadLibrary({name}): ok");
                }
                catch (Exception ex)
                {
                    sb.AppendLine($"LoadLibrary({name}) FAILED: {ex.Message}");
                }
            }

            Report = sb.ToString();
            Log.Info("sobe", Report);
        }
    }
}
