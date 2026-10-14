using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using osu.Framework.IO.Stores;

namespace OsuBeatmapEditor.Game.Library
{
    /// <summary>
    /// Lets the song list's existing texture and audio stores (built for osu!lazer's hash-addressed file store) read files from the
    /// app-owned library. A library file is addressed by a synthetic "hash" (<see cref="KeyFor"/>) that the list code already
    /// slices into the usual <c>a/ab/hash</c> path; this store turns that back into the real file. Read-only; unknown or unsafe
    /// names simply return nothing.
    /// </summary>
    public sealed class LibraryResourceStore : IResourceStore<byte[]>
    {
        private readonly LibraryStore library;

        public LibraryResourceStore(LibraryStore library) => this.library = library;

        /// <summary>The stand-in "hash" for a file of a library set (at least two characters, as the list code requires).</summary>
        public static string KeyFor(string setId, string file) => setId + "|" + file;

        /// <summary>Splits a store name (<c>x/xx/setId|file</c> or just <c>setId|file</c>) into its parts.</summary>
        public static bool TryParse(string name, out string setId, out string file)
        {
            setId = file = string.Empty;
            if (string.IsNullOrEmpty(name))
                return false;

            string rest = name;
            // The list code prefixes the key with its first one and two characters, e.g. "a/ab/" - drop those two segments.
            if (name.Length > 5 && name[1] == '/' && name[4] == '/' && name.IndexOf('|') > 4)
                rest = name.Substring(5);

            int bar = rest.IndexOf('|');
            if (bar <= 0 || bar == rest.Length - 1)
                return false;

            setId = rest.Substring(0, bar);
            file = rest.Substring(bar + 1);
            return true;
        }

        /// <summary>Full path of the file behind a store name, or null when it is unknown, missing or outside the set.</summary>
        public string? ResolvePath(string name)
        {
            if (!TryParse(name, out string setId, out string file) || library.Find(setId) is not { IsDeleted: false })
                return null;

            return SetFileResolver.ResolveInFolder(library.FilesDirectory(setId), file);
        }

        public byte[]? Get(string name)
        {
            string? path = ResolvePath(name);
            if (path == null)
                return null;

            try
            {
                return File.ReadAllBytes(path);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                return null;
            }
        }

        public Task<byte[]?> GetAsync(string name, CancellationToken cancellationToken = default) => Task.FromResult(Get(name));

        public Stream? GetStream(string name)
        {
            string? path = ResolvePath(name);
            if (path == null)
                return null;

            try
            {
                return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                return null;
            }
        }

        public IEnumerable<string> GetAvailableResources() => Array.Empty<string>();

        public void Dispose()
        {
        }
    }
}
