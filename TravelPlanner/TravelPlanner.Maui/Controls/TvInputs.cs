using Microsoft.Maui.Handlers;

namespace TravelPlanner.Maui.Controls;

/// <summary>
/// Removes the platform's own underline (Android), border (Windows) and rounded box (iOS/Mac) from entries and date
/// pickers, so the template's pill or card around them is the only outline.
/// </summary>
public static class TvInputs
{
    public static void RemoveNativeBorders()
    {
        EntryHandler.Mapper.AppendToMapping("TvBorderless", (handler, _) =>
        {
#if ANDROID
            handler.PlatformView.BackgroundTintList = Android.Content.Res.ColorStateList.ValueOf(Android.Graphics.Color.Transparent);
#elif WINDOWS
            Flatten(handler.PlatformView);
#elif IOS || MACCATALYST
            handler.PlatformView.BorderStyle = UIKit.UITextBorderStyle.None;
#endif
        });
        DatePickerHandler.Mapper.AppendToMapping("TvBorderless", (handler, _) =>
        {
#if ANDROID
            handler.PlatformView.BackgroundTintList = Android.Content.Res.ColorStateList.ValueOf(Android.Graphics.Color.Transparent);
#elif WINDOWS
            handler.PlatformView.BorderThickness = new Microsoft.UI.Xaml.Thickness(0);
            handler.PlatformView.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);
#endif
        });
        PickerHandler.Mapper.AppendToMapping("TvBorderless", (handler, _) =>
        {
#if ANDROID
            handler.PlatformView.BackgroundTintList = Android.Content.Res.ColorStateList.ValueOf(Android.Graphics.Color.Transparent);
#elif WINDOWS
            handler.PlatformView.BorderThickness = new Microsoft.UI.Xaml.Thickness(0);
            handler.PlatformView.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);
#endif
        });
    }

#if WINDOWS
    private static void Flatten(Microsoft.UI.Xaml.Controls.TextBox box)
    {
        box.BorderThickness = new Microsoft.UI.Xaml.Thickness(0);
        box.Background = Clear();
        // WinUI repaints the box and its accent underline on hover and focus; keep those transparent too.
        // This is cosmetic, and WinUI refuses resource changes on some boxes (e.g. MAUI's password box once it is
        // shown again), so each key is set at most once and a refusal never stops the app.
        SetOnce(box, "TextControlBackgroundPointerOver", Clear());
        SetOnce(box, "TextControlBackgroundFocused", Clear());
        SetOnce(box, "TextControlBorderBrushPointerOver", Clear());
        SetOnce(box, "TextControlBorderBrushFocused", Clear());
        SetOnce(box, "TextControlBorderThemeThicknessFocused", new Microsoft.UI.Xaml.Thickness(0));
    }

    private static Microsoft.UI.Xaml.Media.SolidColorBrush Clear() => new(Microsoft.UI.Colors.Transparent);

    private static void SetOnce(Microsoft.UI.Xaml.FrameworkElement element, string key, object value)
    {
        try
        {
            if (!element.Resources.ContainsKey(key)) element.Resources.Add(key, value);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"TvInputs: could not set {key}: {ex.Message}");
        }
    }
#endif
}
