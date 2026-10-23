using System;
using System.IO;

namespace OsuBeatmapEditor.Game.Library
{
    /// <summary>Checks images that came from a file chooser (map backgrounds). UI- and Android-free so it can be tested.</summary>
    public static class PickedImage
    {
        public const long MAX_BYTES = 30L * 1024 * 1024;

        /// <summary>The image type of a file's first bytes: ".jpg", ".png", or null when it is neither (extensions lie, bytes do not).</summary>
        public static string? SniffExtension(byte[] head, int length)
        {
            if (length >= 3 && head[0] == 0xFF && head[1] == 0xD8 && head[2] == 0xFF)
                return ".jpg";

            if (length >= 8 && head[0] == 0x89 && head[1] == 0x50 && head[2] == 0x4E && head[3] == 0x47
                && head[4] == 0x0D && head[5] == 0x0A && head[6] == 0x1A && head[7] == 0x0A)
                return ".png";

            return null;
        }

        /// <summary>A safe local file name for a picked image, with the extension matching the REAL image type.</summary>
        public static string FinalName(string? displayName, string? lastPathSegment, string realExtension)
        {
            string raw = !string.IsNullOrWhiteSpace(displayName) ? displayName! : lastPathSegment ?? string.Empty;

            int cut = raw.LastIndexOfAny(new[] { '/', '\\', ':' });
            if (cut >= 0)
                raw = raw.Substring(cut + 1);

            string stem = Path.GetFileNameWithoutExtension(LibraryActions.SafeFileName(raw));
            if (string.IsNullOrWhiteSpace(stem) || stem == "beatmap")
                stem = "background";

            return stem + realExtension;
        }

        /// <summary>User-facing problem with the file, or null when it is a usable .jpg / .png.</summary>
        public static string? Validate(string path)
        {
            try
            {
                var info = new FileInfo(path);
                if (!info.Exists)
                    return "The chosen file could not be found.";

                if (info.Length == 0)
                    return $"\"{info.Name}\" is empty.";

                if (info.Length > MAX_BYTES)
                    return $"\"{info.Name}\" is too large (limit {MAX_BYTES / 1024 / 1024} MB).";

                byte[] head = new byte[8];
                int read;
                using (var fs = info.OpenRead())
                    read = fs.Read(head, 0, head.Length);

                return SniffExtension(head, read) != null ? null : $"\"{info.Name}\" is not a .jpg or .png image.";
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                return "The chosen file could not be read: " + e.Message;
            }
        }
    }
}
