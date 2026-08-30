#if WINDOWS
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Color = System.Windows.Media.Color;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using Orientation = System.Windows.Controls.Orientation;
using VerticalAlignment = System.Windows.VerticalAlignment;

namespace Nvp;

internal sealed class NoraUpdateAvailableWindow : Window
{
    private readonly NoraUpdateInfo _update;
    private readonly TextBlock _status = new();
    private readonly NoraFxButton _updateButton;
    private readonly NoraFxButton _laterButton;
    private bool _dontShowAgain;

    public NoraUpdateAvailableWindow(NoraUpdateInfo update)
    {
        _update = update;
        Title = "NORA VPN update";
        Icon = NoraWpfWindow.LoadBrandWindowIcon();
        Width = 540;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = false;
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
                Kind = NoraIconKind.Refresh,
                Width = 22,
                Height = 22,
                Stroke = NoraWpfTheme.OrangeBrush,
                Weight = 2
            }
        });
        var heading = new StackPanel { Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        heading.Children.Add(new TextBlock
        {
            Text = "A new NORA VPN version is available",
            FontSize = 19,
            FontWeight = FontWeights.Bold,
            Foreground = NoraWpfTheme.TextBrush,
            TextWrapping = TextWrapping.Wrap
        });
        heading.Children.Add(new TextBlock
        {
            Text = $"{NoraUpdateService.CurrentVersion}  →  {update.Version}",
            FontSize = 12.5,
            Foreground = NoraWpfTheme.OrangeBrush,
            Margin = new Thickness(0, 4, 0, 0)
        });
        Grid.SetColumn(heading, 1);
        header.Children.Add(heading);
        root.Children.Add(header);

        root.Children.Add(new TextBlock
        {
            Text = "You can keep using NORA normally. The direct Setup download opens only after you choose Update; Setup will preserve subscriptions, servers and settings.",
            FontSize = 13,
            LineHeight = 19,
            Foreground = NoraWpfTheme.Brush(Color.FromRgb(211, 218, 229)),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 17, 0, 14)
        });

        _status.Text = "The update check ran in the background and did not delay startup. The download opens in your browser.";
        _status.FontSize = 11.5;
        _status.Foreground = NoraWpfTheme.DimBrush;
        _status.TextWrapping = TextWrapping.Wrap;
        _status.Margin = new Thickness(0, 8, 0, 13);
        root.Children.Add(_status);

        var toggleMark = new TextBlock
        {
            Text = "✓",
            FontSize = 14,
            FontWeight = FontWeights.Bold,
            Foreground = NoraWpfTheme.BgBrush,
            Visibility = Visibility.Collapsed,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        var toggleBox = new Border
        {
            Width = 21,
            Height = 21,
            CornerRadius = new CornerRadius(6),
            BorderThickness = new Thickness(1),
            BorderBrush = NoraWpfTheme.Brush(Color.FromArgb(150, 146, 157, 176)),
            Background = NoraWpfTheme.Brush(Color.FromRgb(12, 16, 23)),
            Child = toggleMark,
            VerticalAlignment = VerticalAlignment.Center
        };
        var toggleContent = new StackPanel { Orientation = Orientation.Horizontal };
        toggleContent.Children.Add(toggleBox);
        toggleContent.Children.Add(new TextBlock
        {
            Text = $"Don't show version {update.Version} again",
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = NoraWpfTheme.TextBrush,
            Margin = new Thickness(10, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        });
        var toggle = new NoraFxButton(
            Color.FromArgb(128, 12, 16, 23),
            Color.FromArgb(220, 25, 31, 41),
            13,
            accent: false,
            stroke: NoraWpfTheme.Brush(Color.FromArgb(95, 146, 157, 176)))
        {
            Height = 45,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(13, 0, 13, 0),
            Content = toggleContent
        };
        toggle.Click += (_, _) =>
        {
            var nextValue = !_dontShowAgain;
            try
            {
                NoraUiPreferences.SetSkippedUpdateVersion(nextValue ? update.Version : "");
            }
            catch
            {
                _status.Text = "The preference could not be saved. Check access to the NORA data folder.";
                _status.Foreground = NoraWpfTheme.RedBrush;
                return;
            }
            _dontShowAgain = nextValue;
            toggleMark.Visibility = _dontShowAgain ? Visibility.Visible : Visibility.Collapsed;
            toggleBox.Background = _dontShowAgain ? NoraWpfTheme.OrangeBrush : NoraWpfTheme.Brush(Color.FromRgb(12, 16, 23));
            toggleBox.BorderBrush = _dontShowAgain ? NoraWpfTheme.OrangeBrush : NoraWpfTheme.Brush(Color.FromArgb(150, 146, 157, 176));
        };
        root.Children.Add(toggle);

        var actions = new Grid { Margin = new Thickness(0, 15, 0, 0) };
        actions.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        actions.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _laterButton = new NoraFxButton(NoraWpfTheme.Card2, Color.FromRgb(31, 38, 49), 14, false, NoraWpfTheme.StrokeBrush)
        {
            Width = 112,
            Height = 42,
            Content = new TextBlock { Text = "Later", FontSize = 13, FontWeight = FontWeights.Bold, Foreground = NoraWpfTheme.TextBrush }
        };
        _laterButton.Click += (_, _) => Close();
        actions.Children.Add(_laterButton);
        _updateButton = new NoraFxButton(NoraWpfTheme.Orange, NoraWpfTheme.Orange2, 14, accent: true)
        {
            Width = 176,
            Height = 42,
            Content = new TextBlock { Text = "Download update", FontSize = 13, FontWeight = FontWeights.Bold, Foreground = NoraWpfTheme.BgBrush }
        };
        _updateButton.Click += (_, _) =>
        {
            try
            {
                Process.Start(new ProcessStartInfo(_update.SetupDownload.ToString())
                {
                    UseShellExecute = true
                });
                Close();
            }
            catch (Exception exception)
            {
                _status.Text = "The Setup download could not be opened: " + exception.GetBaseException().Message;
                _status.Foreground = NoraWpfTheme.RedBrush;
            }
        };
        Grid.SetColumn(_updateButton, 1);
        actions.Children.Add(_updateButton);
        root.Children.Add(actions);

    }
}
#endif
