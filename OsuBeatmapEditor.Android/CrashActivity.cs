using Android.App;
using Android.Content;
using Android.OS;
using Android.Views;
using Android.Widget;

namespace OsuBeatmapEditor.Android
{
    /// <summary>Plain Android screen (no game engine) that displays a crash report. Runs in its own process.</summary>
    [Activity(Label = "sobe crash report", Process = ":crash", Exported = false, ExcludeFromRecents = true)]
    public class CrashActivity : Activity
    {
        public const string EXTRA_TEXT = "crash_text";

        protected override void OnCreate(Bundle? savedInstanceState)
        {
            base.OnCreate(savedInstanceState);

            string text = Intent?.GetStringExtra(EXTRA_TEXT) ?? "(no details were passed)";

            var layout = new LinearLayout(this) { Orientation = Orientation.Vertical };
            layout.SetPadding(32, 64, 32, 32);

            var title = new TextView(this) { Text = "sobe crashed. Please send a screenshot of this screen, or tap Share." };
            title.TextSize = 16;

            var share = new Button(this) { Text = "Share report" };
            share.Click += (_, _) =>
            {
                var send = new Intent(Intent.ActionSend);
                send.SetType("text/plain");
                send.PutExtra(Intent.ExtraText, text);
                StartActivity(Intent.CreateChooser(send, "Share crash report"));
            };

            var body = new TextView(this) { Text = text };
            body.TextSize = 11;
            body.SetTextIsSelectable(true);

            var scroll = new ScrollView(this);
            scroll.AddView(body);

            layout.AddView(title);
            layout.AddView(share);
            layout.AddView(scroll, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, 0, 1f));
            SetContentView(layout);
        }
    }
}
