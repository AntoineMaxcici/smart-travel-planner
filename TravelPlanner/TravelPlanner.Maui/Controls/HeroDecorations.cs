using Microsoft.Maui.Layouts;

namespace TravelPlanner.Maui.Controls;

/// <summary>
/// The template hero's moving decorations, at the template's positions:
/// <list type="bullet">
/// <item>star (top 15%, left 10%, 40px): <c>durustar1</c>, scale 0.2 → 1 → 0.2 every 3s;</item>
/// <item>plane flying down (top 40%, right edge, 240px): slides in from the right (<c>duru-slide-right</c>);</item>
/// <item>plane flying up (bottom, left 40%, 180px): <c>durustar3</c>, rises 20px while growing 0.8 → 1 every 6s;</item>
/// <item>compass (top 35%, left 45%, 74x80): turns; the template turns it on scroll, so call <see cref="OnScrolled"/>.</item>
/// </list>
/// The layer ignores touches. <see cref="Compact"/> hides the large plane and the compass for narrow screens.
/// </summary>
public sealed class HeroDecorations : ContentView
{
    public static readonly BindableProperty CompactProperty = BindableProperty.Create(
        nameof(Compact), typeof(bool), typeof(HeroDecorations), false,
        propertyChanged: (b, _, _) => ((HeroDecorations)b).ApplyCompact());

    /// <summary>Narrow layout: smaller star and rising plane, no large plane or compass.</summary>
    public bool Compact { get => (bool)GetValue(CompactProperty); set => SetValue(CompactProperty, value); }

    private readonly Image star;
    private readonly Image flightDown;
    private readonly Image flightUp;
    private readonly Image compass;
    private bool running;
    private double scrollTurn;
    private double idleTurn;

    public HeroDecorations()
    {
        InputTransparent = true;
        star = Decoration("tv_star.png");
        flightDown = Decoration("tv_flight_down.png");
        flightUp = Decoration("tv_flight_up.png");
        compass = Decoration("tv_compass.png");
        var canvas = new AbsoluteLayout { InputTransparent = true, Children = { compass, flightUp, flightDown, star } };
        Content = canvas;
        ApplyCompact();
        Loaded += (_, _) => Start();
        Unloaded += (_, _) => Stop();
    }

    private static Image Decoration(string file) => new()
    {
        Source = file,
        Aspect = Aspect.AspectFit,
        InputTransparent = true
    };

    private static void Place(View view, double x, double y, double width, double height)
    {
        AbsoluteLayout.SetLayoutBounds(view, new Rect(x, y, width, height));
        AbsoluteLayout.SetLayoutFlags(view, AbsoluteLayoutFlags.PositionProportional);
    }

    private void ApplyCompact()
    {
        var compact = Compact;
        // Proportional x/y in AbsoluteLayout place the item's own edge proportionally, which matches the
        // template's percentage offsets closely enough for decorations.
        Place(star, 0.1, 0.15, compact ? 28 : 40, compact ? 34 : 49);
        Place(flightDown, 1, 0.4, 240, 211);
        Place(flightUp, compact ? 0.92 : 0.4, 1, compact ? 120 : 180, compact ? 92 : 138);
        Place(compass, 0.45, 0.35, 74, 80);
        flightDown.IsVisible = !compact;
        compass.IsVisible = !compact;
    }

    /// <summary>Turns the compass with the page's scroll position, like the template's <c>duru-rotate-on-scroll</c>.</summary>
    public void OnScrolled(double scrollY)
    {
        scrollTurn = -scrollY * 0.4;
        compass.Rotation = idleTurn + scrollTurn;
    }

    private void Start()
    {
        if (running) return;
        running = true;

        // durustar1: scale 0.2 -> 1 -> 0.2, 3s, ease-in-out, forever.
        var twinkle = new Animation();
        twinkle.Add(0, 0.5, new Animation(v => star.Scale = v, 0.2, 1, Easing.SinInOut));
        twinkle.Add(0.5, 1, new Animation(v => star.Scale = v, 1, 0.2, Easing.SinInOut));
        twinkle.Commit(this, "TvStar", 16, 3000, repeat: () => running);

        // durustar3: translateY 0 -> -20 and scale 0.8 -> 1, then back, 6s, forever.
        var bob = new Animation();
        bob.Add(0, 0.5, new Animation(v => { flightUp.TranslationY = -20 * v; flightUp.Scale = 0.8 + 0.2 * v; }, 0, 1, Easing.SinInOut));
        bob.Add(0.5, 1, new Animation(v => { flightUp.TranslationY = -20 * v; flightUp.Scale = 0.8 + 0.2 * v; }, 1, 0, Easing.SinInOut));
        bob.Commit(this, "TvFlightUp", 16, 6000, repeat: () => running);

        // duru-slide-right: the plane glides in from the right once.
        flightDown.TranslationX = 100;
        flightDown.Opacity = 0;
        new Animation(v => { flightDown.TranslationX = 100 * (1 - v); flightDown.Opacity = v; }, 0, 1, Easing.CubicOut)
            .Commit(this, "TvFlightDown", 16, 1400);

        // The app pages barely scroll, so the compass also turns slowly on its own (360 -> 0 like the template).
        new Animation(v => { idleTurn = v; compass.Rotation = idleTurn + scrollTurn; }, 360, 0, Easing.Linear)
            .Commit(this, "TvCompass", 16, 40_000, repeat: () => running);
    }

    private void Stop()
    {
        running = false;
        foreach (var name in new[] { "TvStar", "TvFlightUp", "TvFlightDown", "TvCompass" }) this.AbortAnimation(name);
    }
}
