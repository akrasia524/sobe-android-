using System.Collections.Generic;
using System.Linq;
using OsuBeatmapEditor.Game.Beatmaps;

namespace OsuBeatmapEditor.Game.Library
{
    /// <summary>
    /// Presents the app-owned library to the existing song-select UI by converting <see cref="SetRecord"/>s into the
    /// <see cref="BeatmapSetModel"/>s it already understands. Covers listing data only: star rating is 0 and the
    /// lazer-style hashed file maps are empty, so cover images, audio previews and opening in the editor are
    /// separate, later stages.
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

            return new BeatmapSetModel
            {
                OnlineID = set.OnlineSetId,
                Title = set.Title,
                Artist = set.Artist,
                Author = set.Creator,
                Difficulties = difficulties,
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
