#if WINDOWS
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Color = System.Windows.Media.Color;

namespace Nvp;

internal sealed class NoraDeepLinkConfirmWindow : Window
{
    public NoraDeepLinkConfirmWindow(NoraDeepLinkRequest request)
    {
        var subscription = request.Kind == NoraDeepLinkKind.Subscription;
        Title = "NORA VPN import";
        Icon = NoraWpfWindow.LoadBrandWindowIcon();
        Width = 500;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = NoraWpfTheme.BgBrush;
        Foreground = NoraWpfTheme.TextBrush;
        FontFamily = NoraWpfTheme.UiFont;

        var card = new Border
        {
            Margin = new Thickness(18),
            Padding = new Thickness(22),
            CornerRadius = new CornerRadius(20),
            Background = NoraWpfTheme.Brush(Color.FromArgb(244, 17, 22, 30)),
            BorderBrush = NoraWpfTheme.Brush(Color.FromArgb(105, 255, 156, 38)),
            BorderThickness = new Thickness(1)
        };
        Content = card;
        var root = new StackPanel();
        card.Child = root;

        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.Children.Add(new Border
        {
            Width = 46,
            Height = 46,
            CornerRadius = new CornerRadius(15),
            Background = NoraWpfTheme.Brush(Color.FromArgb(36, 255, 156, 38)),
            BorderBrush = NoraWpfTheme.Brush(Color.FromArgb(100, 255, 156, 38)),
            BorderThickness = new Thickness(1),
            Child = new NoraIcon
            {
                Kind = NoraIconKind.Import,
                Width = 22,
                Height = 22,
                Stroke = NoraWpfTheme.OrangeBrush,
                Weight = 2
            }
        });
        var heading = new StackPanel { Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        heading.Children.Add(new TextBlock
        {
            Text = subscription ? "Import this subscription?" : "Import this VPN profile?",
            FontSize = 20,
            FontWeight = FontWeights.Bold,
            Foreground = NoraWpfTheme.TextBrush,
            TextWrapping = TextWrapping.Wrap
        });
        heading.Children.Add(new TextBlock
        {
            Text = "NORA opened an import link from another application.",
            FontSize = 12.5,
            Foreground = NoraWpfTheme.MutedBrush,
            Margin = new Thickness(0, 4, 0, 0),
            TextWrapping = TextWrapping.Wrap
        });
        Grid.SetColumn(heading, 1);
        header.Children.Add(heading);
        root.Children.Add(header);

        var source = subscription
            ? new Uri(request.Payload).IdnHost
            : "Profile contents are hidden for your safety";
        var sourceCard = new Border
        {
            CornerRadius = new CornerRadius(13),
            Padding = new Thickness(14, 11, 14, 11),
            Margin = new Thickness(0, 17, 0, 13),
            Background = NoraWpfTheme.Brush(Color.FromArgb(190, 10, 14, 20)),
            BorderBrush = NoraWpfTheme.StrokeBrush,
            BorderThickness = new Thickness(1),
            Child = new TextBlock
            {
                Text = subscription ? "Provider: " + source : source,
                FontSize = 12.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = NoraWpfTheme.Brush(Color.FromRgb(215, 222, 233)),
                TextWrapping = TextWrapping.Wrap
            }
        };
        root.Children.Add(sourceCard);

        root.Children.Add(new TextBlock
        {
            Text = subscription
                ? "NORA will contact this provider, save the returned servers and select the first available entry. Review the provider name before continuing."
                : "NORA will validate and save the profile, then select it for the next connection.",
            FontSize = 13,
            LineHeight = 19,
            Foreground = NoraWpfTheme.Brush(Color.FromRgb(211, 218, 229)),
            TextWrapping = TextWrapping.Wrap
        });
        root.Children.Add(new TextBlock
        {
            Text = "The import does not start, stop or reconnect the VPN.",
            FontSize = 11.5,
            Foreground = NoraWpfTheme.DimBrush,
            Margin = new Thickness(0, 8, 0, 0),
            TextWrapping = TextWrapping.Wrap
        });

        var actions = new Grid { Margin = new Thickness(0, 17, 0, 0) };
        actions.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        actions.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var cancel = new NoraFxButton(NoraWpfTheme.Card2, Color.FromRgb(31, 38, 49), 14, false, NoraWpfTheme.StrokeBrush)
        {
            Width = 112,
            Height = 42,
            IsCancel = true,
            Content = new TextBlock { Text = "Cancel", FontSize = 13, FontWeight = FontWeights.Bold, Foreground = NoraWpfTheme.TextBrush }
        };
        cancel.Click += (_, _) => DialogResult = false;
        actions.Children.Add(cancel);
        var import = new NoraFxButton(NoraWpfTheme.Orange, NoraWpfTheme.Orange2, 14, accent: true)
        {
            Width = 154,
            Height = 42,
            IsDefault = true,
            Content = new TextBlock { Text = "Import", FontSize = 13, FontWeight = FontWeights.Bold, Foreground = NoraWpfTheme.BgBrush }
        };
        import.Click += (_, _) => DialogResult = true;
        Grid.SetColumn(import, 1);
        actions.Children.Add(import);
        root.Children.Add(actions);
    }
}
#endif
