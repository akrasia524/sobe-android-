using System;
using System.Diagnostics;
using System.IO;

namespace OsuBeatmapEditor.Game.VideoExport
{
    /// <summary>
    /// Finds the ffmpeg executable used by the video exporter. Resolution order:
    /// <list type="number">
    /// <item>the <c>SOBE_FFMPEG</c> environment variable (an explicit full path), for dev overrides;</item>
    /// <item>a copy bundled next to the app (<c>&lt;basedir&gt;/ffmpeg/ffmpeg[.exe]</c>), the shipping path;</item>
    /// <item>the bare name <c>ffmpeg</c>, relying on it being on the system PATH.</item>
    /// </list>
    /// Until the per-platform binaries are bundled (Phase 4), install ffmpeg locally to test - e.g. <c>brew install ffmpeg</c>.
    /// </summary>
    public static class FfmpegLocator
    {
        private static string executableName => OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg";

        /// <summary>Best-guess path to ffmpeg. Never null; falls back to the bare name so the caller gets a clear launch error.</summary>
        public static string Resolve()
        {
            string? env = Environment.GetEnvironmentVariable("SOBE_FFMPEG");
            if (!string.IsNullOrWhiteSpace(env) && File.Exists(env))
                return env;

            string bundled = Path.Combine(AppContext.BaseDirectory, "ffmpeg", executableName);
            if (File.Exists(bundled))
                return bundled;

            return executableName;
        }

        /// <summary>Whether the given ffmpeg path actually runs (checks <c>ffmpeg -version</c> exits cleanly).</summary>
        public static bool IsAvailable(string ffmpegPath)
        {
            try
            {
                using var process = Process.Start(new ProcessStartInfo(ffmpegPath, "-version")
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                });

                if (process == null)
                    return false;

                process.WaitForExit(5000);
                return process.HasExited && process.ExitCode == 0;
            }
            catch
            {
                return false;
            }
        }
    }
}
