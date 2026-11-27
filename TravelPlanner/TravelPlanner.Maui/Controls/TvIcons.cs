using Microsoft.Maui.Controls.Shapes;

namespace TravelPlanner.Maui.Controls;

/// <summary>
/// Outline icons drawn as 16x16 vector paths, in the spirit of the template's light icon set (whose font licence
/// can't be shipped in the app). Use with a stroked Path, e.g. <c>Data="{x:Static controls:TvIcons.Email}"</c>.
/// </summary>
public static class TvIcons
{
    public const string ArrowRight = "M1,8 L15,8 M9.5,2.5 L15,8 L9.5,13.5";
    public const string Person = "M1,8 A7,7 0 1 1 15,8 A7,7 0 1 1 1,8 Z M5.2,9.6 Q8,12.6 10.8,9.6 M5.8,5.8 L5.8,6.4 M10.2,5.8 L10.2,6.4";
    public const string Email = "M1.5,3.5 L14.5,3.5 L14.5,12.5 L1.5,12.5 Z M1.5,4 L8,9 L14.5,4";
    public const string Lock = "M3.5,7.5 L12.5,7.5 L12.5,14.5 L3.5,14.5 Z M5.5,7.5 L5.5,5 A2.5,2.5 0 0 1 10.5,5 L10.5,7.5 M8,10.2 L8,11.8";
    public const string Code = "M6,1.5 L4.5,14.5 M11.5,1.5 L10,14.5 M2,5.5 L14.5,5.5 M1.5,10.5 L14,10.5";
    public const string Calendar = "M2,3.5 L14,3.5 L14,14 L2,14 Z M2,6.5 L14,6.5 M5,1.5 L5,5 M11,1.5 L11,5";
    public const string Pin = "M8,15 C8,15 3,9.8 3,6.2 A5,5 0 0 1 13,6.2 C13,9.8 8,15 8,15 Z M6.4,6.2 A1.6,1.6 0 1 1 9.6,6.2 A1.6,1.6 0 1 1 6.4,6.2 Z";
    public const string Plus = "M8,2 L8,14 M2,8 L14,8";
    public const string Wallet = "M1.5,4.5 L14.5,4.5 L14.5,13.5 L1.5,13.5 Z M1.5,4.5 L11.5,1.5 L11.5,4.5 M11,9 L11.6,9";
    public const string Trash = "M2.5,4 L13.5,4 M6,4 L6,2 L10,2 L10,4 M3.8,4 L4.6,14.5 L11.4,14.5 L12.2,4";
    public const string SignOut = "M6,2 L2,2 L2,14 L6,14 M10.5,4.5 L14,8 L10.5,11.5 M14,8 L5.5,8";

    private static readonly PathGeometryConverter Converter = new();

    /// <summary>Parses one of the path strings above for use in code.</summary>
    public static Geometry Parse(string data) => (Geometry)Converter.ConvertFromInvariantString(data)!;

    /// <summary>A stroked icon path, sized like the template's 12-16px icons.</summary>
    public static Microsoft.Maui.Controls.Shapes.Path Create(string data, Color color, double size = 16, double thickness = 1.5) => new()
    {
        Data = Parse(data),
        Stroke = new SolidColorBrush(color),
        StrokeThickness = thickness,
        StrokeLineCap = PenLineCap.Round,
        StrokeLineJoin = PenLineJoin.Round,
        Aspect = Stretch.Uniform,
        WidthRequest = size,
        HeightRequest = size,
        HorizontalOptions = LayoutOptions.Center,
        VerticalOptions = LayoutOptions.Center,
        InputTransparent = true
    };
}
