using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Android.App;
using Android.Content;
using Android.OS;
using Android.Runtime;
using Android.Util;

namespace OsuBeatmapEditor.Android
{
    /// <summary>
    /// Shows a fatal crash on screen (in a separate process, so it survives the crash) with a Share button.
    /// Needed because the person testing has no PC/logcat. Cannot catch native (SIGSEGV) crashes.
    /// </summary>
    internal static class CrashReporter
    {
        private static int installed;
        private static int reported;

        public static void Install(Context context)
        {
            if (Interlocked.Exchange(ref installed, 1) == 1)
                return;

            var app = context.ApplicationContext ?? context;

            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
                report(app, "AppDomain.UnhandledException", e.ExceptionObject?.ToString() ?? "unknown");

            AndroidEnvironment.UnhandledExceptionRaiser += (_, e) =>
                report(app, "AndroidEnvironment.UnhandledExceptionRaiser", e.Exception?.ToString() ?? "unknown");

            TaskScheduler.UnobservedTaskException += (_, e) =>
                appendLog(app, "UnobservedTaskException (non-fatal): " + e.Exception);

            var previous = Java.Lang.Thread.DefaultUncaughtExceptionHandler;
            Java.Lang.Thread.DefaultUncaughtExceptionHandler = new JavaHandler(app, previous);
        }

        private sealed class JavaHandler : Java.Lang.Object, Java.Lang.Thread.IUncaughtExceptionHandler
        {
            private readonly Context app;
            private readonly Java.Lang.Thread.IUncaughtExceptionHandler? previous;

            public JavaHandler(Context app, Java.Lang.Thread.IUncaughtExceptionHandler? previous)
            {
                this.app = app;
                this.previous = previous;
            }

            public void UncaughtException(Java.Lang.Thread? t, Java.Lang.Throwable? e)
            {
                report(app, "Java uncaught exception on thread " + t?.Name, e == null ? "unknown" : Log.GetStackTraceString(e));
                previous?.UncaughtException(t, e);
            }
        }

        private static void appendLog(Context app, string text)
        {
            try
            {
                File.AppendAllText(Path.Combine(app.FilesDir!.AbsolutePath, "crash-log.txt"), text + "\n");
            }
            catch
            {
            }
        }

        private static void report(Context app, string source, string detail)
        {
            // Several handlers can fire for one crash; only show it once.
            if (Interlocked.Exchange(ref reported, 1) == 1)
                return;

            try
            {
                var sb = new StringBuilder();
                string abis = Build.SupportedAbis == null ? "?" : string.Join(",", Build.SupportedAbis);
                sb.AppendLine($"sobe Android debug | Android {Build.VERSION.Release} (API {(int)Build.VERSION.SdkInt}) | {Build.Manufacturer} {Build.Model} | {abis}");
                sb.AppendLine("Source: " + source);
                sb.AppendLine();
                sb.AppendLine(detail);

                string? tail = readFrameworkLogTail(app);
                if (tail != null)
                {
                    sb.AppendLine();
                    sb.AppendLine("---- last lines of the game's runtime.log ----");
                    sb.AppendLine(tail);
                }

                string text = sb.ToString();
                if (text.Length > 60000)
                    text = text[..60000] + "\n...(truncated)";

                appendLog(app, text);

                var intent = new Intent(app, typeof(CrashActivity));
                intent.AddFlags(ActivityFlags.NewTask | ActivityFlags.ClearTask);
                intent.PutExtra(CrashActivity.EXTRA_TEXT, text);
                app.StartActivity(intent);

                // Give the crash screen's process a moment to start before this process dies.
                Thread.Sleep(1500);
            }
            catch
            {
                // Nothing more we can do; never throw from a crash handler.
            }
        }

        private static string? readFrameworkLogTail(Context app)
        {
            try
            {
                string? dir = app.GetExternalFilesDir(string.Empty)?.AbsolutePath;
                if (dir == null)
                    return null;

                string path = Path.Combine(dir, "logs", "runtime.log");
                if (!File.Exists(path))
                    return "(no runtime.log was written - the game crashed before the framework started)";

                var lines = File.ReadAllLines(path);
                return string.Join("\n", lines.Skip(Math.Max(0, lines.Length - 60)));
            }
            catch (Exception ex)
            {
                return "(could not read runtime.log: " + ex.Message + ")";
            }
        }
    }
}
