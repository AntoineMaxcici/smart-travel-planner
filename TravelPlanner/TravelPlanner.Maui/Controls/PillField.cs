using Microsoft.Maui.Controls.Shapes;

namespace TravelPlanner.Maui.Controls;

/// <summary>
/// The template's form input: a 40px-radius pill with a light grey outline, a teal icon 25px from the left and the
/// text starting at 55px. The outline turns teal on hover and while the input inside has focus.
/// Put an Entry (or DatePicker/Picker) inside: <c>&lt;controls:PillField Icon="..."&gt;&lt;Entry /&gt;&lt;/controls:PillField&gt;</c>.
/// </summary>
[ContentProperty(nameof(Field))]
public sealed class PillField : ContentView
{
    private static readonly Color Outline = Color.FromArgb("#DEE2F0");
    private static readonly Color Active = Color.FromArgb("#2095AE");

    public static readonly BindableProperty FieldProperty = BindableProperty.Create(
        nameof(Field), typeof(View), typeof(PillField),
        propertyChanged: (b, oldValue, newValue) => ((PillField)b).OnFieldChanged((View?)oldValue, (View?)newValue));
    public static readonly BindableProperty IconProperty = BindableProperty.Create(
        nameof(Icon), typeof(string), typeof(PillField), TvIcons.Email,
        propertyChanged: (b, _, value) => ((PillField)b).icon.Data = TvIcons.Parse((string?)value ?? TvIcons.Email));

    public View? Field { get => (View?)GetValue(FieldProperty); set => SetValue(FieldProperty, value); }
    public string Icon { get => (string)GetValue(IconProperty); set => SetValue(IconProperty, value); }

    private readonly Border frame;
    private readonly Grid row;
    private readonly Microsoft.Maui.Controls.Shapes.Path icon;
    private bool hovering;
    private bool focused;

    public PillField()
    {
        icon = TvIcons.Create(TvIcons.Email, Active, 16, 1.4);
        icon.HorizontalOptions = LayoutOptions.Start;
        row = new Grid { ColumnDefinitions = { new ColumnDefinition(30), new ColumnDefinition(GridLength.Star) } };
        row.Add(icon, 0, 0);
        frame = new Border
        {
            BackgroundColor = Colors.Transparent,
            Stroke = new SolidColorBrush(Outline),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 28 },
            MinimumHeightRequest = 57,
            Padding = new Thickness(25, 0, 18, 0),
            Content = row
        };
        Content = frame;
        var pointer = new PointerGestureRecognizer();
        pointer.PointerEntered += (_, _) => { hovering = true; UpdateOutline(); };
        pointer.PointerExited += (_, _) => { hovering = false; UpdateOutline(); };
        GestureRecognizers.Add(pointer);
        // Tapping the pill outside the input still focuses it, like clicking a web input's padding.
        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) => Field?.Focus();
        frame.GestureRecognizers.Add(tap);
    }

    private void OnFieldChanged(View? oldField, View? newField)
    {
        if (oldField is not null)
        {
            row.Remove(oldField);
            oldField.Focused -= OnFocused;
            oldField.Unfocused -= OnUnfocused;
        }
        if (newField is null) return;
        newField.VerticalOptions = LayoutOptions.Center;
        newField.Focused += OnFocused;
        newField.Unfocused += OnUnfocused;
        row.Add(newField, 1, 0);
    }

    private void OnFocused(object? sender, FocusEventArgs e) { focused = true; UpdateOutline(); }
    private void OnUnfocused(object? sender, FocusEventArgs e) { focused = false; UpdateOutline(); }

    private void UpdateOutline() => frame.Stroke = new SolidColorBrush(hovering || focused ? Active : Outline);
}
