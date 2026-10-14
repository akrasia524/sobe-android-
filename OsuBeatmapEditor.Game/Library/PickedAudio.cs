using System;
using System.IO;
using System.Linq;

namespace OsuBeatmapEditor.Game.Library
{
    /// <summary>What the platform's file chooser gave back: a local file, an error, or (both null) a cancel.</summary>
    public sealed record PickResult(string? Path, string? Error)
    {
        public bool Cancelled => Path == null && Error == null;
    }

    /// <summary>
    /// Set by the Android host: opens a chooser (system picker plus any installed file manager) for one audio file, copies the
    /// choice into app storage and calls back (on any thread) with the result. Null where no such picker exists (desktop).
    /// </summary>
    public static class PlatformPicker
    {
        public static Action<Action<PickResult>>? PickAudio { get; set; }
    }

    /// <summary>Checks and names audio files that came from a file chooser. UI- and Android-free so it can be tested.</summary>
    public static class PickedAudio
    {
        public static readonly string[] Extensions = { ".mp3", ".ogg", ".wav" };

        public const long MAX_BYTES = 300L * 1024 * 1024;

        /// <summary>
        /// A usable local file name for a picked item. Uses the provider's display name (falling back to the last path
        /// segment); when that has no audio extension, derives one from the MIME type. Path parts are always stripped.
        /// </summary>
        public static string ResolveFileName(string? displayName, string? mimeType, string? lastPathSegment)
        {
            string raw = !string.IsNullOrWhiteSpace(displayName) ? displayName! : lastPathSegment ?? string.Empty;

            int cut = raw.LastIndexOfAny(new[] { '/', '\\', ':' });
            if (cut >= 0)
                raw = raw.Substring(cut + 1);

            string name = LibraryActions.SafeFileName(raw);
            if (name == "beatmap" && string.IsNullOrWhiteSpace(raw))
                name = "audio";

            string ext = Path.GetExtension(name).ToLowerInvariant();
            if (Extensions.Contains(ext))
                return name;

            string? fromMime = ExtensionForMime(mimeType);
            if (fromMime == null)
                return name;

            return (ext.Length > 0 ? name.Substring(0, name.Length - ext.Length) : name) + fromMime;
        }

        public static string? ExtensionForMime(string? mime)
        {
            switch ((mime ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "audio/mpeg":
                case "audio/mp3":
                case "audio/mpeg3":
                case "audio/x-mpeg":
                case "audio/x-mp3":
                    return ".mp3";

                case "audio/ogg":
                case "application/ogg":
                case "audio/vorbis":
                case "audio/x-vorbis":
                case "audio/x-ogg":
                    return ".ogg";

                case "audio/wav":
                case "audio/x-wav":
                case "audio/wave":
                case "audio/vnd.wave":
                    return ".wav";

                default:
                    return null;
            }
        }

        /// <summary>Returns a user-facing problem with the file, or null when it looks like a usable mp3 / ogg / wav.</summary>
        public static string? Validate(string path)
        {
            try
            {
                var info = new FileInfo(path);
                if (!info.Exists)
                    return "The chosen file could not be found.";

                string ext = info.Extension.ToLowerInvariant();
                if (!Extensions.Contains(ext))
                    return $"\"{info.Name}\" is not a supported audio file. Choose an mp3, ogg or wav file.";

                if (info.Length == 0)
                    return $"\"{info.Name}\" is empty.";

                if (info.Length > MAX_BYTES)
                    return $"\"{info.Name}\" is too large (limit {MAX_BYTES / 1024 / 1024} MB).";

                byte[] head = new byte[12];
                int read;
                using (var fs = info.OpenRead())
                    read = fs.Read(head, 0, head.Length);

                bool ogg = read >= 4 && head[0] == 'O' && head[1] == 'g' && head[2] == 'g' && head[3] == 'S';
                bool riff = read >= 4 && head[0] == 'R' && head[1] == 'I' && head[2] == 'F' && head[3] == 'F';
                bool mp4 = read >= 8 && head[4] == 'f' && head[5] == 't' && head[6] == 'y' && head[7] == 'p';

                bool ok = ext switch
                {
                    ".ogg" => ogg,
                    ".wav" => riff,
                    _ => !ogg && !riff && !mp4, // mp3 has no single signature; just reject other known containers
                };

                return ok ? null : $"\"{info.Name}\" does not look like a real {ext.TrimStart('.')} file.";
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                return "The chosen file could not be read: " + e.Message;
            }
        }
    }
}
