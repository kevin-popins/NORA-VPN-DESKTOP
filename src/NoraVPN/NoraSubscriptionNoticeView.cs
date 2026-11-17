#if WINDOWS
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Color = System.Windows.Media.Color;

namespace Nvp;

internal static class NoraSubscriptionNoticeView
{
    internal static string Signature(NoraSubscriptionMetadataState state)
        => HasNotice(state) ? string.Join('\u001f', state.Access, state.Availability, state.Message, state.CachedOnly.ToString()) : "";
    internal static bool HasNotice(NoraSubscriptionMetadataState state)
        => state.CachedOnly ||
           state.Access is "expired" or "quota_exhausted" or "disabled" or "pending" ||
           state.Availability is "degraded" or "node_maintenance" or "outage" or "no_nodes";

    internal static string Heading(NoraSubscriptionMetadataState state) => state.Access switch
    {
        "expired" => "Subscription expired",
        "quota_exhausted" => "Traffic allowance used",
        "disabled" => "Subscription unavailable",
        "pending" => "Subscription not active yet",
        _ => state.Availability switch
        {
            "node_maintenance" => "Servers under maintenance",
            "outage" => "VPN temporarily unavailable",
            "no_nodes" => "No servers available",
            "degraded" => "Some servers are unavailable",
            _ => state.CachedOnly ? "Refresh required" : "Provider notice"
        }
    };

    internal static FrameworkElement? Create(NoraSubscriptionMetadataState state)
    {
        if (!HasNotice(state)) return null;
        var text = new StackPanel();
        text.Children.Add(new TextBlock
        {
            Text = Heading(state), FontSize = 13, FontWeight = FontWeights.SemiBold,
            Foreground = NoraWpfTheme.OrangeBrush, TextWrapping = TextWrapping.Wrap
        });
        var message = string.IsNullOrWhiteSpace(state.Message)
            ? state.CachedOnly ? "Connection settings are saved. Refresh this subscription before starting a new connection." : "Check with your provider for the latest service information."
            : NoraProtectedDisplay.Redact(state.Message);
        text.Children.Add(new TextBlock
        {
            Text = message, FontSize = 12, Foreground = NoraWpfTheme.TextBrush,
            Margin = new Thickness(0, 4, 0, 0), TextWrapping = TextWrapping.Wrap
        });
        return new Border
        {
            Margin = new Thickness(0, 10, 0, 2), Padding = new Thickness(12, 10, 12, 10),
            CornerRadius = new CornerRadius(12),
            Background = NoraWpfTheme.Brush(Color.FromArgb(30, 255, 156, 38)),
            BorderBrush = NoraWpfTheme.Brush(Color.FromArgb(95, 255, 156, 38)), BorderThickness = new Thickness(1), Child = text
        };
    }
}
#endif
