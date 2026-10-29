using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.Textures;
using osu.Framework.Graphics.Colour;
using OsuBeatmapEditor.Game.Beatmaps;
using osuTK.Graphics;

namespace OsuBeatmapEditor.Game.Screens.SongSelect
{
    /// <summary>
    /// Builds the dimmed background image shown behind a carousel card, fading from a transparent left
    /// edge (where the text sits) to the visible image on the right - matching osu!lazer's panels.
    /// </summary>
    public static class CarouselBackground
    {
        public static Drawable? TryCreate(LargeTextureStore? textures, BeatmapSetModel set, string backgroundFile)
        {
            if (textures == null || backgroundFile.Length == 0
                || !set.Files.TryGetValue(backgroundFile.ToLowerInvariant(), out string? hash) || hash.Length < 2)
                return null;

            // The image is decoded off the update thread AFTER the card is already on screen (text first, picture fades in).
            // Decoding it inside the card's own load made every card wait for its full-size JPEG, so the list filled in
            // one card at a time.
            return new CarouselBackgroundImage(textures, $"{hash[..1]}/{hash[..2]}/{hash}");
        }
    }

    /// <summary>
    /// The dimmed picture behind a card. Starts invisible and empty, decodes its texture on a background thread (at most two
    /// at a time, so a fast scroll can't flood a phone's CPU), skips the decode if the card has already gone, then fades in.
    /// </summary>
    public partial class CarouselBackgroundImage : Container
    {
        // Two at a time: enough to keep up, few enough to leave the update/draw threads their CPU.
        private static readonly System.Threading.SemaphoreSlim decode_slots = new System.Threading.SemaphoreSlim(2);

        private readonly LargeTextureStore textures;
        private readonly string path;
        private readonly System.Threading.CancellationTokenSource cancel = new System.Threading.CancellationTokenSource();

        public CarouselBackgroundImage(LargeTextureStore textures, string path)
        {
            this.textures = textures;
            this.path = path;
            RelativeSizeAxes = Axes.Both;
            Alpha = 0;
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            var token = cancel.Token;

            System.Threading.Tasks.Task.Run(async () =>
            {
                try
                {
                    await decode_slots.WaitAsync(token).ConfigureAwait(false);

                    osu.Framework.Graphics.Textures.Texture? texture;

                    try
                    {
                        if (token.IsCancellationRequested)
                            return;

                        texture = await textures.GetAsync(path, token).ConfigureAwait(false);
                    }
                    finally
                    {
                        decode_slots.Release();
                    }

                    if (texture == null || token.IsCancellationRequested)
                        return;

                    Schedule(() =>
                    {
                        if (token.IsCancellationRequested)
                            return;

                        Children = new Drawable[]
                        {
                            new Sprite
                            {
                                RelativeSizeAxes = Axes.Both,
                                Anchor = Anchor.Centre,
                                Origin = Anchor.Centre,
                                FillMode = FillMode.Fill,
                                Texture = texture,
                            },
                            // Left-to-right dim so the title/subtitle stays readable over the image.
                            new Box
                            {
                                RelativeSizeAxes = Axes.Both,
                                Colour = ColourInfo.GradientHorizontal(
                                    new Color4(0f, 0f, 0f, 0.85f),
                                    new Color4(0f, 0f, 0f, 0.35f)),
                            },
                        };
                        this.FadeTo(1, 250, Easing.OutQuint);
                    });
                }
                catch
                {
                    // Cancelled or a bad image: the card just keeps its plain background.
                }
            });
        }

        protected override void Dispose(bool isDisposing)
        {
            cancel.Cancel();
            base.Dispose(isDisposing);
        }
    }
}
