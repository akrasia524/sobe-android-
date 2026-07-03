using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace OsuBeatmapEditor.Game.VideoExport
{
    /// <summary>
    /// Opens the operating system's native "choose folder" dialog and returns the picked path (or null if the user
    /// cancelled / no picker is available). osu!framework has no directory selector, so we shell out to each OS's
    /// standard tool. macOS uses AppleScript (rock-solid); Windows/Linux are best-effort.
    /// </summary>
    public static class NativeFolderPicker
    {
        public static Task<string?> PickAsync(string prompt)
        {
            if (OperatingSystem.IsMacOS())
                return runAsync("osascript", new[] { "-e", $"POSIX path of (choose folder with prompt \"{escape(prompt)}\")" });

            if (OperatingSystem.IsWindows())
            {
                string script =
                    "Add-Type -AssemblyName System.Windows.Forms; " +
                    "$d = New-Object System.Windows.Forms.FolderBrowserDialog; " +
                    $"$d.Description = '{escape(prompt)}'; " +
                    "if ($d.ShowDialog() -eq 'OK') { [Console]::Out.Write($d.SelectedPath) }";
                return runAsync("powershell", new[] { "-NoProfile", "-STA", "-Command", script });
            }

            if (OperatingSystem.IsLinux())
                return runAsync("zenity", new[] { "--file-selection", "--directory", $"--title={prompt}" });

            return Task.FromResult<string?>(null);
        }

        private static async Task<string?> runAsync(string exe, string[] args)
        {
            try
            {
                var info = new ProcessStartInfo(exe)
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                foreach (string a in args)
                    info.ArgumentList.Add(a);

                using var process = Process.Start(info);
                if (process == null)
                    return null;

                string output = await process.StandardOutput.ReadToEndAsync().ConfigureAwait(false);
                await process.WaitForExitAsync().ConfigureAwait(false);

                // A cancelled dialog exits non-zero (macOS) or prints nothing; treat both as "no selection".
                if (process.ExitCode != 0)
                    return null;

                string path = output.Trim();
                return string.IsNullOrEmpty(path) ? null : path;
            }
            catch
            {
                // Tool missing (e.g. no zenity) or any launch failure: no selection.
                return null;
            }
        }

        private static string escape(string s) => s.Replace("\"", "\\\"").Replace("'", "");
    }
}
