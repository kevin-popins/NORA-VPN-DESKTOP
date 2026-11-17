#if WINDOWS
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using TextBox = System.Windows.Controls.TextBox;
using Color = System.Windows.Media.Color;

namespace Nvp;

// Shared by the Add-page input and the owned-window presentation proof. Raising
// these routed notifications styles the brush; this helper never moves focus.
internal static class NoraInputFocus
{
    internal static void Attach(UIElement input, SolidColorBrush border)
    {
        input.GotKeyboardFocus += (_, _) => border.BeginAnimation(SolidColorBrush.ColorProperty,
            ColorTo(Color.FromArgb(190, 255, 156, 38), 180));
        input.LostKeyboardFocus += (_, _) => border.BeginAnimation(SolidColorBrush.ColorProperty,
            ColorTo(NoraWpfTheme.Stroke, 260));
    }

    private static ColorAnimation ColorTo(Color target, double milliseconds)
        => new(target, TimeSpan.FromMilliseconds(milliseconds))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
}
#endif
