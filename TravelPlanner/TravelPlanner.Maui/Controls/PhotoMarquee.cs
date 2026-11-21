using Microsoft.Maui.Controls.Shapes;

namespace TravelPlanner.Maui.Controls;

/// <summary>
/// The template hero's three endlessly scrolling photo columns (<c>.slide-vertical</c>): widths 30% / 40% / 30%,
/// photos 300px tall with a 50px radius (the middle column 400px with a 100px radius), 15px apart, each column
/// moving up by one full set of photos every 50 seconds and starting again seamlessly. Hovering pauses it, and a
/// navy fade covers the top and bottom 80px. <see cref="ItemScale"/> shrinks everything for small screens.
/// </summary>
public sealed class PhotoMarquee : ContentView
{
    private const uint LoopMs = 50_000;
    private static readonly string[][] Photos =
    [
        ["tv_marquee_a1.jpg", "tv_marquee_a2.jpg", "tv_marquee_a3.jpg", "tv_marquee_a4.jpg"],
        ["tv_marquee_da.jpg", "tv_marquee_dc.jpg", "tv_marquee_de.jpg", "tv_marquee_df.jpg"],
        ["tv_marquee_b1.jpg", "tv_marquee_b2.jpg", "tv_marquee_b3.jpg", "tv_marquee_b4.jpg"]
    ];
    private static readonly double[] BaseHeights = [300, 400, 300];
    private static readonly double[] BaseRadii = [50, 100, 50];

    public static readonly BindableProperty ItemScaleProperty = BindableProperty.Create(
        nameof(ItemScale), typeof(double), typeof(PhotoMarquee), 1.0,
        propertyChanged: (b, _, _) => ((PhotoMarquee)b).Rebuild());
    public static readonly BindableProperty FadeColorProperty = BindableProperty.Create(
        nameof(FadeColor), typeof(Color), typeof(PhotoMarquee), Color.FromArgb("#0F2454"),
        propertyChanged: (b, _, _) => ((PhotoMarquee)b).UpdateFade());

    public double ItemScale { get => (double)GetValue(ItemScaleProperty); set => SetValue(ItemScaleProperty, value); }
    public Color FadeColor { get => (Color)GetValue(FadeColorProperty); set => SetValue(FadeColorProperty, value); }

    private readonly VerticalStackLayout[] columns = new VerticalStackLayout[3];
    private readonly double[] loopHeights = new double[3];
    private readonly Grid fade;
    private readonly AbsoluteLayout canvas;
    private double lastWidth = -1;
    private bool running;
    private bool paused;

    public PhotoMarquee()
    {
        // Absolute positions give every column its full height (two photo sets); only this viewport clips.
        canvas = new AbsoluteLayout { IsClippedToBounds = true };
        for (var i = 0; i < 3; i++)
        {
            columns[i] = new VerticalStackLayout { Spacing = 0 };
            canvas.Add(columns[i]);
        }
        fade = new Grid { InputTransparent = true };
        canvas.Add(fade);
        AbsoluteLayout.SetLayoutBounds(fade, new Rect(0, 0, 1, 1));
        AbsoluteLayout.SetLayoutFlags(fade, Microsoft.Maui.Layouts.AbsoluteLayoutFlags.All);
        Content = canvas;
        IsClippedToBounds = true;
        SemanticProperties.SetDescription(this, "Travel photos");

        Rebuild();
        SizeChanged += (_, _) => { UpdateFade(); PlaceColumns(); };
        Loaded += (_, _) => Start();
        Unloaded += (_, _) => Stop();
        var pointer = new PointerGestureRecognizer();
        pointer.PointerEntered += (_, _) => Pause();
        pointer.PointerExited += (_, _) => Resume();
        GestureRecognizers.Add(pointer);
    }

    private void Rebuild()
    {
        var scale = Math.Clamp(ItemScale, 0.3, 1.5);
        for (var i = 0; i < 3; i++)
        {
            var height = BaseHeights[i] * scale;
            var margin = 15 * scale;
            columns[i].Children.Clear();
            // Two identical sets: moving up by exactly one set and jumping back is invisible.
            for (var copy = 0; copy < 2; copy++)
            {
                foreach (var photo in Photos[i])
                {
                    columns[i].Children.Add(new Border
                    {
                        HeightRequest = height,
                        Margin = new Thickness(0, margin),
                        StrokeThickness = 0,
                        StrokeShape = new RoundRectangle { CornerRadius = BaseRadii[i] * scale },
                        BackgroundColor = Color.FromArgb("#1B3566"),
                        Content = new Image { Source = photo, Aspect = Aspect.AspectFill, InputTransparent = true }
                    });
                }
            }
            loopHeights[i] = Photos[i].Length * (height + 2 * margin);
        }
        lastWidth = -1;
        PlaceColumns();
        if (running)
        {
            Stop();
            Start();
        }
    }

    // Widths 30% / 40% / 30% with 20px gaps, like the template's st1/st2/st3 columns.
    private void PlaceColumns()
    {
        var width = Width;
        if (width <= 0 || Math.Abs(width - lastWidth) < 0.5) return;
        lastWidth = width;
        var gap = 20 * Math.Clamp(ItemScale, 0.3, 1.5);
        var usable = Math.Max(0, width - 2 * gap);
        double[] widths = [usable * 0.3, usable * 0.4, usable * 0.3];
        var x = 0.0;
        for (var i = 0; i < 3; i++)
        {
            AbsoluteLayout.SetLayoutBounds(columns[i], new Rect(x, 0, widths[i], 2 * loopHeights[i]));
            x += widths[i] + gap;
        }
    }

    private void UpdateFade()
    {
        var height = Height > 0 ? Height : 600;
        var edge = Math.Clamp(80 / height, 0.02, 0.45);
        var clear = FadeColor.WithAlpha(0);
        var brush = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        brush.GradientStops.Add(new GradientStop(FadeColor, 0));
        brush.GradientStops.Add(new GradientStop(clear, (float)edge));
        brush.GradientStops.Add(new GradientStop(clear, (float)(1 - edge)));
        brush.GradientStops.Add(new GradientStop(FadeColor, 1));
        fade.Background = brush;
    }

    private static string AnimationName(int column) => $"TvMarquee{column}";

    private void Start()
    {
        running = true;
        paused = false;
        for (var i = 0; i < 3; i++) Run(i, 0);
    }

    private void Stop()
    {
        running = false;
        for (var i = 0; i < 3; i++) this.AbortAnimation(AnimationName(i));
    }

    private void Pause()
    {
        if (!running || paused) return;
        paused = true;
        for (var i = 0; i < 3; i++) this.AbortAnimation(AnimationName(i));
    }

    private void Resume()
    {
        if (!running || !paused) return;
        paused = false;
        for (var i = 0; i < 3; i++) Run(i, columns[i].TranslationY);
    }

    // Moves one column from `from` (0 or a paused position) to minus one full set, then starts again from 0.
    private void Run(int column, double from)
    {
        var loop = loopHeights[column];
        if (loop <= 0) return;
        var remaining = Math.Clamp((loop + from) / loop, 0.001, 1);
        var target = columns[column];
        var animation = new Animation(value => target.TranslationY = value, from, -loop, Easing.Linear);
        animation.Commit(this, AnimationName(column), 16, (uint)(LoopMs * remaining), finished: (_, cancelled) =>
        {
            if (cancelled || !running || paused) return;
            target.TranslationY = 0;
            Run(column, 0);
        });
    }
}
