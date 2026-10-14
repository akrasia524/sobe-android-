using System.Collections.Generic;
using System.Linq;
using OsuBeatmapEditor.Game.Beatmaps;

namespace OsuBeatmapEditor.Game.Library
{
    /// <summary>
    /// Presents the app-owned library to the existing song-select UI by converting <see cref="SetRecord"/>s into the
    /// <see cref="BeatmapSetModel"/>s it already understands. Background and audio files are addressed through
    /// <see cref="LibraryResourceStore"/> so cover images and the audio preview work too.
    /// </summary>
    public static class LibraryBeatmapSets
    {
        public static IReadOnlyList<BeatmapSetModel> ToModels(LibraryStore store) =>
            store.Sets.Select(set => toModel(store, set)).ToList();

        private static BeatmapSetModel toModel(LibraryStore store, SetRecord set)
        {
            var difficulties = set.Difficulties.Select(d => new BeatmapDifficultyModel
            {
                DifficultyName = d.Name,
                StarRating = d.StarRating,
                RulesetShortName = "osu",
                OsuFileHash = d.Sha256,
                LibraryFile = d.File,
                BackgroundFile = d.Background,
                AudioFile = d.Audio,
                PreviewTime = d.PreviewTime,
            }).ToList();

            // The background and audio files the list needs, addressed so LibraryResourceStore can open them (no disk access here).
            var files = new Dictionary<string, string>();
            foreach (var d in set.Difficulties)
            {
                foreach (string name in new[] { d.Background, d.Audio })
                {
                    if (!string.IsNullOrEmpty(name))
                        files[name.ToLowerInvariant()] = LibraryResourceStore.KeyFor(set.Id, name);
                }
            }

            return new BeatmapSetModel
            {
                OnlineID = set.OnlineSetId,
                Title = set.Title,
                Artist = set.Artist,
                Author = set.Creator,
                Difficulties = difficulties,
                Files = files,
                LibraryFilesDirectory = store.FilesDirectory(set.Id),
                Library = store,
                LibrarySetId = set.Id,
                SearchText = $"{set.Artist} {set.Title} {set.Creator} {string.Join(" ", difficulties.Select(d => d.DifficultyName))}".ToLowerInvariant(),
                DateAdded = set.DateAdded,
                DateModified = set.DateModified,
            };
        }
    }
}
