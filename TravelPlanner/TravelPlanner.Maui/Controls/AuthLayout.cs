using Microsoft.Maui.Controls.Shapes;

namespace TravelPlanner.Maui.Controls;

/// <summary>
/// The template's home hero, used as the frame of the sign-in, sign-up, confirm-email and forgot-password pages:
/// navy background with the world-map pattern, a teal letter-spaced eyebrow, a big uppercase heading with a smaller
/// lowercase teal accent, a faded paragraph, the three scrolling photo columns and the animated star, planes and
/// compass. The page's form sits in a white rounded card where the template has its "View tours" button.
/// Wide windows (960px+) use the template's two columns; narrow ones stack text, form, footer and photos.
/// </summary>
[ContentProperty(nameof(Form))]
public sealed class AuthLayout : ContentView
{
    private const double WideBreakpoint = 960;
    private static readonly Color Navy = Color.FromArgb("#0F2454");
    private static readonly Color Teal = Color.FromArgb("#2095AE");

    public static readonly BindableProperty EyebrowProperty = BindableProperty.Create(
        nameof(Eyebrow), typeof(string), typeof(AuthLayout), "TravelPlanner",
        propertyChanged: (b, _, v) => ((AuthLayout)b).eyebrow.Text = (string?)v);
    public static readonly BindableProperty TitleProperty = BindableProperty.Create(
        nameof(Title), typeof(string), typeof(AuthLayout), "",
        propertyChanged: (b, _, _) => ((AuthLayout)b).UpdateTitle());
    public static readonly BindableProperty AccentProperty = BindableProperty.Create(
        nameof(Accent), typeof(string), typeof(AuthLayout), "",
        propertyChanged: (b, _, _) => ((AuthLayout)b).UpdateTitle());
    public static readonly BindableProperty SubtitleProperty = BindableProperty.Create(
        nameof(Subtitle), typeof(string), typeof(AuthLayout), "",
        propertyChanged: (b, _, v) => ((AuthLayout)b).UpdateSubtitle((string?)v));
    public static readonly BindableProperty FormProperty = BindableProperty.Create(
        nameof(Form), typeof(View), typeof(AuthLayout),
        propertyChanged: (b, _, v) => ((AuthLayout)b).card.Content = (View?)v);
    public static readonly BindableProperty FooterProperty = BindableProperty.Create(
        nameof(Footer), typeof(View), typeof(AuthLayout),
        propertyChanged: (b, _, v) => ((AuthLayout)b).footerHost.Content = (View?)v);

    /// <summary>Small uppercase teal line above the heading (the template's h6).</summary>
    public string Eyebrow { get => (string)GetValue(EyebrowProperty); set => SetValue(EyebrowProperty, value); }
    /// <summary>Big uppercase heading, e.g. "Discover the world".</summary>
    public string Title { get => (string)GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    /// <summary>Smaller lowercase teal words after the heading, e.g. "with our planner.".</summary>
    public string Accent { get => (string)GetValue(AccentProperty); set => SetValue(AccentProperty, value); }
    public string Subtitle { get => (string)GetValue(SubtitleProperty); set => SetValue(SubtitleProperty, value); }
    /// <summary>The form shown in the white card (the XAML content of this element).</summary>
    public View? Form { get => (View?)GetValue(FormProperty); set => SetValue(FormProperty, value); }
    /// <summary>Shown on the navy background under the card, e.g. the white arrow button to the other auth page.</summary>
    public View? Footer { get => (View?)GetValue(FooterProperty); set => SetValue(FooterProperty, value); }

    private readonly Grid root;
    private readonly ScrollView scroll;
    private readonly Grid page;
    private readonly VerticalStackLayout left;
    private readonly VerticalStackLayout text;
    private readonly Label eyebrow;
    private readonly Label title;
    private readonly Label subtitle;
    private readonly Border card;
    private readonly ContentView footerHost;
    private readonly PhotoMarquee marquee;
    private readonly HeroDecorations decorations;
    private bool? wide;
    private double titleSize = 80;
    private bool entered;

    public AuthLayout()
    {
        eyebrow = new Label
        {
            Text = Eyebrow,
            FontFamily = "BarlowCondensedMedium",
            FontSize = 15,
            CharacterSpacing = 6,
            TextTransform = TextTransform.Uppercase,
            TextColor = Teal
        };
        title = new Label { LineHeight = 1.0, Margin = new Thickness(0, 4, 0, 0) };
        SemanticProperties.SetHeadingLevel(title, SemanticHeadingLevel.Level1);
        subtitle = new Label
        {
            FontFamily = "BarlowSemiRegular",
            FontSize = 17,
            TextColor = Colors.White.WithAlpha(0.5f),
            MaximumWidthRequest = 480,
            HorizontalOptions = LayoutOptions.Start,
            IsVisible = false
        };
        text = new VerticalStackLayout { Spacing = 14, Children = { eyebrow, title, subtitle } };

        card = new Border
        {
            BackgroundColor = Colors.White,
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 30 },
            Padding = new Thickness(30),
            Shadow = new Shadow { Brush = new SolidColorBrush(Color.FromArgb("#06112B")), Opacity = 0.35f, Radius = 40, Offset = new Point(0, 18) }
        };
        footerHost = new ContentView();
        left = new VerticalStackLayout { Spacing = 26, VerticalOptions = LayoutOptions.Center, Children = { text, card, footerHost } };

        marquee = new PhotoMarquee();
        page = new Grid { MaximumWidthRequest = 1240, ColumnSpacing = 0, RowSpacing = 30 };
        scroll = new ScrollView { Content = page };
        decorations = new HeroDecorations();
        scroll.Scrolled += (_, e) => decorations.OnScrolled(e.ScrollY);

        root = new Grid { BackgroundColor = Navy };
        root.Add(new Image { Source = "tv_pattern_bg.png", Aspect = Aspect.AspectFill, InputTransparent = true });
        root.Add(decorations);
        root.Add(scroll);
        Content = root;

        UpdateTitle();
        root.SizeChanged += (_, _) => ApplyLayout();
        Loaded += (_, _) => PlayEntrance();
    }

    private void UpdateTitle()
    {
        var formatted = new FormattedString();
        formatted.Spans.Add(new Span
        {
            // Upper/lower case in code: span TextTransform isn't honoured on every platform.
            Text = Title.ToUpperInvariant(),
            FontFamily = "BarlowCondensedMedium",
            FontSize = titleSize,
            CharacterSpacing = -2,
            TextColor = Colors.White
        });
        if (!string.IsNullOrWhiteSpace(Accent))
        {
            formatted.Spans.Add(new Span
            {
                Text = " " + Accent.ToLowerInvariant(),
                FontFamily = "BarlowCondensedRegular",
                FontSize = Math.Round(titleSize / 2),
                TextColor = Teal
            });
        }
        title.FormattedText = formatted;
        SemanticProperties.SetDescription(title, $"{Title} {Accent}".Trim());
    }

    private void UpdateSubtitle(string? value)
    {
        subtitle.Text = value;
        subtitle.IsVisible = !string.IsNullOrWhiteSpace(value);
    }

    private void ApplyLayout()
    {
        var width = root.Width;
        var height = root.Height;
        if (width <= 0 || height <= 0) return;
        var isWide = width >= WideBreakpoint;
        var size = isWide ? 80 : width >= 560 ? 60 : 46;
        if (size != titleSize)
        {
            titleSize = size;
            UpdateTitle();
        }
        card.Padding = new Thickness(width >= 560 ? 30 : 22);
        left.MaximumWidthRequest = isWide ? double.PositiveInfinity : 560;
        decorations.Compact = !isWide;
        // Wide: the photos fill the window height next to the text, like the template's hero.
        marquee.HeightRequest = isWide ? Math.Max(560, height) : Math.Min(340, Math.Max(240, width * 0.75));
        marquee.ItemScale = isWide ? (height < 760 ? 0.8 : 1) : 0.55;
        if (wide == isWide) return;
        wide = isWide;

        page.Children.Clear();
        page.ColumnDefinitions.Clear();
        page.RowDefinitions.Clear();
        if (isWide)
        {
            page.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(5, GridUnitType.Star)));
            page.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(70)));
            page.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(6, GridUnitType.Star)));
            page.Padding = new Thickness(48, 0);
            left.Margin = new Thickness(0, 48);
            page.Add(left, 0, 0);
            page.Add(marquee, 2, 0);
        }
        else
        {
            page.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            page.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            page.Padding = new Thickness(width >= 560 ? 36 : 20, 36, width >= 560 ? 36 : 20, 0);
            left.Margin = new Thickness(0);
            page.Add(left, 0, 0);
            page.Add(marquee, 0, 1);
        }
    }

    // The template fades and lifts hero content in as the page opens.
    private async void PlayEntrance()
    {
        if (entered) return;
        entered = true;
        View[] parts = [text, card, footerHost];
        foreach (var part in parts)
        {
            part.Opacity = 0;
            part.TranslationY = 40;
        }
        marquee.Opacity = 0;
        _ = marquee.FadeTo(1, 1200, Easing.CubicOut);
        foreach (var part in parts)
        {
            _ = part.FadeTo(1, 700, Easing.CubicOut);
            _ = part.TranslateTo(0, 0, 700, Easing.CubicOut);
            await Task.Delay(140);
        }
    }
}
