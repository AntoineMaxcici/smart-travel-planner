using System.Windows.Input;
using Microsoft.Maui.Controls.Shapes;

namespace TravelPlanner.Maui.Controls;

public enum ArrowButtonVariant
{
    /// <summary>The template's <c>butn-arrow</c>: teal pill, white text, white circle with a navy arrow.</summary>
    Teal,
    /// <summary>The template's <c>butn-arrow2</c>: white pill, navy text, teal circle with a white arrow (for navy backgrounds).</summary>
    White
}

/// <summary>
/// The Tourvex "arrow" button: a 55px pill with the label and a 38px circle holding an arrow. On hover the arrow
/// slides down out of the circle while a second arrow slides in from the top (0.35s), exactly like the template.
/// Taps play the same slide. While <see cref="IsBusy"/> is true the circle shows a spinner instead of the arrow.
/// </summary>
public sealed class ArrowButton : ContentView
{
    private const double Circle = 38;
    private const uint SlideMs = 350;

    public static readonly BindableProperty TextProperty = BindableProperty.Create(
        nameof(Text), typeof(string), typeof(ArrowButton), "",
        propertyChanged: (b, _, value) => ((ArrowButton)b).OnTextChanged((string?)value ?? ""));
    public static readonly BindableProperty CommandProperty = BindableProperty.Create(
        nameof(Command), typeof(ICommand), typeof(ArrowButton),
        propertyChanged: (b, oldValue, newValue) => ((ArrowButton)b).OnCommandChanged((ICommand?)oldValue, (ICommand?)newValue));
    public static readonly BindableProperty CommandParameterProperty = BindableProperty.Create(
        nameof(CommandParameter), typeof(object), typeof(ArrowButton),
        propertyChanged: (b, _, _) => ((ArrowButton)b).UpdateEnabled());
    public static readonly BindableProperty VariantProperty = BindableProperty.Create(
        nameof(Variant), typeof(ArrowButtonVariant), typeof(ArrowButton), ArrowButtonVariant.Teal,
        propertyChanged: (b, _, _) => ((ArrowButton)b).ApplyVariant());
    public static readonly BindableProperty IsBusyProperty = BindableProperty.Create(
        nameof(IsBusy), typeof(bool), typeof(ArrowButton), false,
        propertyChanged: (b, _, value) => ((ArrowButton)b).OnBusyChanged((bool)value));
    public static readonly BindableProperty GlowProperty = BindableProperty.Create(
        nameof(Glow), typeof(bool), typeof(ArrowButton), false,
        propertyChanged: (b, _, value) => ((ArrowButton)b).pill.Shadow = (bool)value
            ? new Shadow { Brush = new SolidColorBrush(Color.FromArgb("#2095AE")), Opacity = 0.4f, Radius = 18, Offset = new Point(0, 8) }
            : null);
    public static readonly BindableProperty IconProperty = BindableProperty.Create(
        nameof(Icon), typeof(string), typeof(ArrowButton), TvIcons.ArrowRight,
        propertyChanged: (b, _, value) => ((ArrowButton)b).OnIconChanged((string?)value ?? TvIcons.ArrowRight));

    public string Text { get => (string)GetValue(TextProperty); set => SetValue(TextProperty, value); }
    public ICommand? Command { get => (ICommand?)GetValue(CommandProperty); set => SetValue(CommandProperty, value); }
    public object? CommandParameter { get => GetValue(CommandParameterProperty); set => SetValue(CommandParameterProperty, value); }
    public ArrowButtonVariant Variant { get => (ArrowButtonVariant)GetValue(VariantProperty); set => SetValue(VariantProperty, value); }
    public bool IsBusy { get => (bool)GetValue(IsBusyProperty); set => SetValue(IsBusyProperty, value); }
    /// <summary>Soft teal shadow under the pill, for floating buttons.</summary>
    public bool Glow { get => (bool)GetValue(GlowProperty); set => SetValue(GlowProperty, value); }
    /// <summary>The icon in the circle; an arrow by default (see <see cref="TvIcons"/>).</summary>
    public string Icon { get => (string)GetValue(IconProperty); set => SetValue(IconProperty, value); }

    private readonly Border pill;
    private readonly Label label;
    private readonly Border circle;
    private readonly Microsoft.Maui.Controls.Shapes.Path incoming;
    private readonly Microsoft.Maui.Controls.Shapes.Path outgoing;
    private readonly ActivityIndicator spinner;
    private bool hovering;

    public ArrowButton()
    {
        label = new Label
        {
            FontFamily = "BarlowSemiRegular",
            FontSize = 17,
            LineBreakMode = LineBreakMode.NoWrap,
            VerticalOptions = LayoutOptions.Center,
            VerticalTextAlignment = TextAlignment.Center
        };
        // Two arrows in a clipped circle: "incoming" waits above (-100%), "outgoing" is visible (0).
        incoming = TvIcons.Create(TvIcons.ArrowRight, Colors.White, 13, 1.6);
        outgoing = TvIcons.Create(TvIcons.ArrowRight, Colors.White, 13, 1.6);
        incoming.TranslationY = -Circle;
        spinner = new ActivityIndicator
        {
            IsRunning = false, IsVisible = false, WidthRequest = 20, HeightRequest = 20,
            HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center
        };
        circle = new Border
        {
            WidthRequest = Circle,
            HeightRequest = Circle,
            StrokeThickness = 0,
            StrokeShape = new Ellipse(),
            VerticalOptions = LayoutOptions.Center,
            Content = new Grid { WidthRequest = Circle, HeightRequest = Circle, IsClippedToBounds = true, Children = { incoming, outgoing, spinner } }
        };
        var row = new Grid
        {
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) },
            ColumnSpacing = 15
        };
        row.Add(label, 0, 0);
        row.Add(circle, 1, 0);
        pill = new Border
        {
            HeightRequest = 55,
            Padding = new Thickness(25, 0, 9, 0),
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 27.5 },
            Content = row
        };
        Content = pill;
        HorizontalOptions = LayoutOptions.Start;

        var tap = new TapGestureRecognizer();
        tap.Tapped += OnTapped;
        GestureRecognizers.Add(tap);
        var pointer = new PointerGestureRecognizer();
        pointer.PointerEntered += (_, _) => { hovering = true; Slide(true); };
        pointer.PointerExited += (_, _) => { hovering = false; Slide(false); };
        pointer.PointerPressed += (_, _) => { if (IsEnabled) _ = pill.ScaleTo(0.97, 90); };
        pointer.PointerReleased += (_, _) => _ = pill.ScaleTo(1, 120);
        GestureRecognizers.Add(pointer);

        ApplyVariant();
        UpdateEnabled();
    }

    private void OnTextChanged(string text)
    {
        label.Text = text;
        SemanticProperties.SetDescription(this, text);
    }

    private void OnIconChanged(string data)
    {
        incoming.Data = TvIcons.Parse(data);
        outgoing.Data = TvIcons.Parse(data);
    }

    private void ApplyVariant()
    {
        var teal = Variant == ArrowButtonVariant.Teal;
        var tealColor = Color.FromArgb("#2095AE");
        var navy = Color.FromArgb("#0F2454");
        pill.BackgroundColor = teal ? tealColor : Colors.White;
        label.TextColor = teal ? Colors.White : navy;
        circle.BackgroundColor = teal ? Colors.White : tealColor;
        var arrow = new SolidColorBrush(teal ? navy : Colors.White);
        incoming.Stroke = arrow;
        outgoing.Stroke = arrow;
        spinner.Color = teal ? tealColor : Colors.White;
    }

    private void OnCommandChanged(ICommand? oldCommand, ICommand? newCommand)
    {
        if (oldCommand is not null) oldCommand.CanExecuteChanged -= OnCanExecuteChanged;
        if (newCommand is not null) newCommand.CanExecuteChanged += OnCanExecuteChanged;
        UpdateEnabled();
    }

    private void OnCanExecuteChanged(object? sender, EventArgs e) => UpdateEnabled();

    private void UpdateEnabled()
    {
        var enabled = !IsBusy && (Command?.CanExecute(CommandParameter) ?? true);
        IsEnabled = enabled;
        // The template has no disabled style; fade the button like a disabled form button.
        Opacity = enabled || IsBusy ? 1 : 0.55;
    }

    private void OnBusyChanged(bool busy)
    {
        spinner.IsVisible = busy;
        spinner.IsRunning = busy;
        incoming.IsVisible = !busy;
        outgoing.IsVisible = !busy;
        UpdateEnabled();
    }

    private void Slide(bool over)
    {
        if (IsBusy) return;
        incoming.CancelAnimations();
        outgoing.CancelAnimations();
        _ = incoming.TranslateTo(0, over ? 0 : -Circle, SlideMs, Easing.CubicInOut);
        _ = outgoing.TranslateTo(0, over ? Circle : 0, SlideMs, Easing.CubicInOut);
    }

    private async void OnTapped(object? sender, TappedEventArgs e)
    {
        var command = Command;
        if (!IsEnabled || command is null || !command.CanExecute(CommandParameter)) return;
        command.Execute(CommandParameter);
        if (hovering) return;
        // Touch has no hover, so play the slide once on tap.
        Slide(true);
        await Task.Delay((int)SlideMs + 60);
        if (!hovering) Slide(false);
    }
}
