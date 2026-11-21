using Microsoft.Maui.Controls.Shapes;

namespace TravelPlanner.Maui.Controls;

/// <summary>
/// Compact version of the template hero for the signed-in pages: navy with the world-map pattern, rounded bottom
/// corners, the twinkling star and the rising plane. Put the header text inside it.
/// </summary>
[ContentProperty(nameof(Body))]
public sealed class PageHero : ContentView
{
    public static readonly BindableProperty BodyProperty = BindableProperty.Create(
        nameof(Body), typeof(View), typeof(PageHero),
        propertyChanged: (b, _, v) => ((PageHero)b).host.Content = (View?)v);

    public View? Body { get => (View?)GetValue(BodyProperty); set => SetValue(BodyProperty, value); }

    private readonly ContentView host;

    public PageHero()
    {
        host = new ContentView { Padding = new Thickness(24, 26, 24, 30) };
        var layers = new Grid { IsClippedToBounds = true };
        // The pattern must not size the header: left alone, the image asks for its full picture height and the
        // header fills the window. It starts tiny and follows the height of the text instead.
        var pattern = new Image { Source = "tv_pattern_bg.png", Aspect = Aspect.AspectFill, InputTransparent = true, HeightRequest = 1 };
        host.SizeChanged += (_, _) => { if (host.Height > 0) pattern.HeightRequest = host.Height; };
        layers.Add(pattern);
        layers.Add(new HeroDecorations { Compact = true });
        layers.Add(host);
        Content = new Border
        {
            BackgroundColor = Color.FromArgb("#0F2454"),
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(0, 0, 32, 32) },
            Content = layers
        };
    }
}
