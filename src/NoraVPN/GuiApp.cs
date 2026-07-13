using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows.Forms;

namespace Nvp;

internal static class GuiApp
{
    public static void Run()
    {
        NoraWpfShell.Run();
    }
}

internal static class NoraTheme
{
    public static readonly Color Background = Color.FromArgb(248, 250, 253);
    public static readonly Color Sidebar = Color.FromArgb(251, 252, 254);
    public static readonly Color Surface = Color.FromArgb(255, 255, 255);
    public static readonly Color SurfaceAlt = Color.FromArgb(246, 248, 252);
    public static readonly Color SurfaceHot = Color.FromArgb(239, 244, 255);
    public static readonly Color Border = Color.FromArgb(219, 226, 238);
    public static readonly Color BorderSoft = Color.FromArgb(232, 237, 246);
    public static readonly Color TextPrimary = Color.FromArgb(17, 28, 52);
    public static readonly Color TextSecondary = Color.FromArgb(88, 99, 122);
    public static readonly Color TextMuted = Color.FromArgb(132, 143, 163);
    public static readonly Color Accent = Color.FromArgb(31, 105, 255);
    public static readonly Color AccentHover = Color.FromArgb(18, 92, 238);
    public static readonly Color AccentPressed = Color.FromArgb(12, 72, 198);
    public static readonly Color Cyan = Color.FromArgb(58, 174, 214);
    public static readonly Color Success = Color.FromArgb(38, 184, 86);
    public static readonly Color SuccessHover = Color.FromArgb(31, 166, 75);
    public static readonly Color SuccessPressed = Color.FromArgb(21, 139, 58);
    public static readonly Color SuccessSoft = Color.FromArgb(237, 250, 242);
    public static readonly Color Danger = Color.FromArgb(224, 67, 57);
    public static readonly Color DangerHover = Color.FromArgb(204, 52, 44);
    public static readonly Color DangerPressed = Color.FromArgb(174, 39, 33);
    public static readonly Color Neutral = Color.FromArgb(246, 248, 252);
    public static readonly Color NeutralHover = Color.FromArgb(237, 242, 250);
    public static readonly Color NeutralPressed = Color.FromArgb(225, 233, 245);
    public static readonly Font Hero = new("Segoe UI Semibold", 27f, FontStyle.Regular);
    public static readonly Font Title = new("Segoe UI Semibold", 20f, FontStyle.Regular);
    public static readonly Font Section = new("Segoe UI Semibold", 11f, FontStyle.Regular);
    public static readonly Font Body = new("Segoe UI", 10f, FontStyle.Regular);
    public static readonly Font BodySmall = new("Segoe UI", 9f, FontStyle.Regular);
    public static readonly Font Mono = new("Cascadia Mono", 9f, FontStyle.Regular);

    public static GraphicsPath RoundedRect(Rectangle bounds, int radius)
    {
        var path = new GraphicsPath();
        var d = radius * 2;
        path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
        path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
        path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}

internal class NoraCard : Panel
{
    public int Radius { get; set; } = 18;
    public Color BorderColor { get; set; } = NoraTheme.Border;
    public Color FillColor { get; set; } = NoraTheme.Surface;

    public NoraCard()
    {
        DoubleBuffered = true;
        SetStyle(ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        Padding = new Padding(18);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var rect = new Rectangle(0, 0, Width - 1, Height - 1);
        using var path = NoraTheme.RoundedRect(rect, Radius);
        using var fill = new SolidBrush(FillColor);
        using var pen = new Pen(BorderColor);
        e.Graphics.FillPath(fill, path);
        e.Graphics.DrawPath(pen, path);
    }
}

internal class NoraButton : Button
{
    private bool _hover;
    private bool _pressed;

    public Color NormalColor { get; set; } = NoraTheme.Neutral;
    public Color HoverColor { get; set; } = NoraTheme.NeutralHover;
    public Color PressedColor { get; set; } = NoraTheme.NeutralPressed;
    public Color BorderColor { get; set; } = Color.Transparent;
    public int Radius { get; set; } = 14;

    public NoraButton()
    {
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        UseVisualStyleBackColor = false;
        BackColor = Color.Transparent;
        ForeColor = NoraTheme.TextPrimary;
        Font = new Font("Segoe UI Semibold", 10f);
        Cursor = Cursors.Hand;
        DoubleBuffered = true;
        SetStyle(ControlStyles.SupportsTransparentBackColor, true);
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; _pressed = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { _pressed = true; Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { _pressed = false; Invalidate(); base.OnMouseUp(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var color = _pressed ? PressedColor : (_hover ? HoverColor : NormalColor);
        if (!Enabled)
            color = Color.FromArgb((color.R + NoraTheme.Background.R) / 2, (color.G + NoraTheme.Background.G) / 2, (color.B + NoraTheme.Background.B) / 2);
        var rect = new Rectangle(0, 0, Width - 1, Height - 1);
        using var path = NoraTheme.RoundedRect(rect, Radius);
        using var fill = new SolidBrush(color);
        e.Graphics.FillPath(fill, path);
        if (BorderColor.A > 0)
        {
            using var pen = new Pen(BorderColor);
            e.Graphics.DrawPath(pen, path);
        }
        TextRenderer.DrawText(e.Graphics, Text, Font, rect, Enabled ? ForeColor : NoraTheme.TextSecondary, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }
}

internal sealed class NoraPowerButton : NoraButton
{
    public string DetailText { get; set; } = "00:00:00";
    public bool ConnectedVisual { get; set; }

    public NoraPowerButton()
    {
        Radius = 92;
        Font = new Font("Segoe UI Semibold", 16f);
        NormalColor = Color.White;
        HoverColor = Color.FromArgb(250, 253, 255);
        PressedColor = Color.FromArgb(243, 248, 255);
        BorderColor = NoraTheme.Success;
        ForeColor = NoraTheme.Success;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using (var bg = new SolidBrush(NoraTheme.Background))
            e.Graphics.FillRectangle(bg, ClientRectangle);

        var accent = ConnectedVisual ? NoraTheme.Success : NoraTheme.Accent;
        if (Text.Contains("Connecting", StringComparison.OrdinalIgnoreCase))
            accent = Color.FromArgb(37, 99, 235);
        if (Text.Contains("Failed", StringComparison.OrdinalIgnoreCase))
            accent = NoraTheme.Danger;

        var shadow = new Rectangle(13, 13, Width - 27, Height - 27);
        using (var shadowPen = new Pen(Color.FromArgb(34, accent), 18f))
            e.Graphics.DrawEllipse(shadowPen, shadow);

        var rect = new Rectangle(18, 18, Width - 37, Height - 37);
        using var outer = new Pen(Color.FromArgb(226, 234, 244), 2f);
        using var ring = new Pen(accent, 6f);
        using var fill = new SolidBrush(Color.White);
        e.Graphics.FillEllipse(fill, rect);
        e.Graphics.DrawEllipse(outer, rect);
        e.Graphics.DrawArc(ring, rect, -92, 344);

        var iconRect = new Rectangle(rect.X + 68, rect.Y + 58, rect.Width - 136, 58);
        using var iconPen = new Pen(accent, 6f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        e.Graphics.DrawArc(iconPen, iconRect, 130, 280);
        e.Graphics.DrawLine(iconPen, rect.X + rect.Width / 2, rect.Y + 60, rect.X + rect.Width / 2, rect.Y + 98);

        var textRect = new Rectangle(rect.X + 18, rect.Y + 132, rect.Width - 36, 34);
        TextRenderer.DrawText(e.Graphics, Text, Font, textRect, accent, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

        var detailRect = new Rectangle(rect.X + 18, rect.Y + 166, rect.Width - 36, 28);
        TextRenderer.DrawText(e.Graphics, DetailText, NoraTheme.Body, detailRect, NoraTheme.TextSecondary, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }
}

internal sealed class NoraPillLabel : Label
{
    public int Radius { get; set; } = 14;

    public NoraPillLabel()
    {
        DoubleBuffered = true;
        BackColor = NoraTheme.Neutral;
        ForeColor = Color.White;
        Font = new Font("Segoe UI Semibold", 10f);
        TextAlign = ContentAlignment.MiddleCenter;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var rect = new Rectangle(0, 0, Width - 1, Height - 1);
        using var path = NoraTheme.RoundedRect(rect, Radius);
        using var fill = new SolidBrush(BackColor);
        e.Graphics.FillPath(fill, path);
        TextRenderer.DrawText(e.Graphics, Text, Font, rect, ForeColor, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }
}

internal sealed class TrafficGraphControl : Control
{
    private readonly Queue<(float Up, float Down)> _points = new();

    public bool Active { get; set; }

    public TrafficGraphControl()
    {
        DoubleBuffered = true;
        BackColor = NoraTheme.Background;
    }

    public void AddSample(long upBytesPerSecond, long downBytesPerSecond)
    {
        if (_points.Count >= 72)
            _points.Dequeue();
        _points.Enqueue((Math.Max(0, upBytesPerSecond), Math.Max(0, downBytesPerSecond)));
        Invalidate();
    }

    public void ResetSamples()
    {
        _points.Clear();
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var rect = new Rectangle(0, 0, Width - 1, Height - 1);
        using (var path = NoraTheme.RoundedRect(rect, 18))
        using (var fill = new SolidBrush(Color.FromArgb(252, 254, 255)))
        using (var border = new Pen(NoraTheme.BorderSoft))
        {
            e.Graphics.FillPath(fill, path);
            e.Graphics.DrawPath(border, path);
        }

        TextRenderer.DrawText(e.Graphics, "Live traffic", new Font("Segoe UI Semibold", 9.5f),
            new Rectangle(18, 10, 160, 22), NoraTheme.TextPrimary,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

        var status = Active ? "active" : "idle";
        TextRenderer.DrawText(e.Graphics, status, NoraTheme.BodySmall,
            new Rectangle(Width - 116, 10, 92, 22), Active ? NoraTheme.Success : NoraTheme.TextMuted,
            TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

        var plot = new Rectangle(18, 38, Width - 36, Height - 50);
        using (var grid = new Pen(Color.FromArgb(236, 241, 249)))
        {
            for (var i = 0; i < 4; i++)
            {
                var y = plot.Top + i * plot.Height / 3;
                e.Graphics.DrawLine(grid, plot.Left, y, plot.Right, y);
            }
        }

        var data = _points.ToArray();
        if (data.Length < 2)
        {
            using var pen = new Pen(Color.FromArgb(216, 225, 239), 2f);
            e.Graphics.DrawLine(pen, plot.Left, plot.Bottom - 2, plot.Right, plot.Bottom - 2);
            return;
        }

        var max = Math.Max(1f, data.Max(p => Math.Max(p.Up, p.Down)));
        DrawSeries(e.Graphics, data.Select(p => p.Down).ToArray(), max, plot, NoraTheme.Accent, 3f);
        DrawSeries(e.Graphics, data.Select(p => p.Up).ToArray(), max, plot, NoraTheme.Success, 2.5f);
    }

    private static void DrawSeries(Graphics g, float[] values, float max, Rectangle plot, Color color, float width)
    {
        if (values.Length < 2)
            return;
        var pts = new PointF[values.Length];
        for (var i = 0; i < values.Length; i++)
        {
            var x = plot.Left + i * (plot.Width / Math.Max(1f, values.Length - 1));
            var y = plot.Bottom - Math.Min(1f, values[i] / max) * plot.Height;
            pts[i] = new PointF(x, y);
        }
        using var pen = new Pen(color, width) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        g.DrawLines(pen, pts);
    }
}

internal sealed class MainWindow : Form
{
    private readonly Label _status = new NoraPillLabel();
    private readonly Label _server = new();
    private readonly Label _route = new();
    private readonly Label _profilePath = new();
    private readonly Button _addServer = new NoraButton();
    private readonly Button _connect = new NoraPowerButton();
    private readonly Button _disconnect = new NoraButton();
    private readonly Button _diagnose = new NoraButton();
    private readonly Button _openProfile = new NoraButton();
    private readonly TextBox _log = new();
    private readonly TrafficGraphControl _trafficGraph = new();

    private NvpConfig? _config;
    private string _configPath = "";
    private NvpCoreProcess? _core;
    private bool _busy;
    private readonly bool _visualReady = NvpClient.IsAdministrator() || string.Equals(Environment.GetEnvironmentVariable("NORA_GUI_VISUAL_READY"), "1", StringComparison.Ordinal);

    public MainWindow()
    {
        Text = "NORA VPN";
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        MinimumSize = new Size(1100, 940);
        MaximumSize = new Size(1100, 940);
        ClientSize = new Size(1100, 940);
        BackColor = NoraTheme.Background;
        Font = new Font("Segoe UI", 10f);
        DoubleBuffered = true;

        var shell = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = NoraTheme.Background
        };
        Controls.Add(shell);

        var topLine = new Panel
        {
            Left = 0,
            Top = 0,
            Width = ClientSize.Width,
            Height = 1,
            BackColor = NoraTheme.BorderSoft
        };
        shell.Controls.Add(topLine);

        var sidebar = BuildSidebar();
        sidebar.Left = 0;
        sidebar.Top = 0;
        sidebar.Width = 264;
        sidebar.Height = ClientSize.Height;
        shell.Controls.Add(sidebar);

        var content = new Panel
        {
            Left = 264,
            Top = 0,
            Width = ClientSize.Width - 264,
            Height = ClientSize.Height,
            BackColor = NoraTheme.Background
        };
        shell.Controls.Add(content);

        content.Controls.Add(BuildTopBar());
        content.Controls.Add(BuildPowerPanel());
        content.Controls.Add(BuildModeTabs());
        content.Controls.Add(BuildServerCard());
        content.Controls.Add(BuildLog());
        var footer = BuildFooter();
        content.Controls.Add(footer);
        footer.BringToFront();

        LoadDefaultProfile();
    }

    protected override async void OnFormClosing(FormClosingEventArgs e)
    {
        try
        {
            await DisconnectAsync();
        }
        catch (Exception ex)
        {
            AppendLog("Close cleanup failed: " + ex.Message);
        }
        finally
        {
            base.OnFormClosing(e);
        }
    }

    private Control BuildTopBar()
    {
        var bar = new Panel
        {
            Left = 0,
            Top = 0,
            Width = 836,
            Height = 84,
            BackColor = NoraTheme.Background
        };
        _addServer.Text = "+  VPS";
        _addServer.SetBounds(626, 34, 96, 46);
        _addServer.Font = new Font("Segoe UI Semibold", 10.5f);
        if (_addServer is NoraButton addButton)
            addButton.Radius = 11;
        StyleOutlineAccentButton(_addServer);
        _addServer.Click += (_, _) => OpenServerSetup();
        _addServer.AccessibleName = "Add VPS";
        bar.Controls.Add(_addServer);

        var info = new NoraButton
        {
            Text = "i",
            Font = new Font("Segoe UI Semibold", 12f),
            Radius = 12
        };
        info.SetBounds(746, 34, 48, 46);
        StyleSecondaryButton(info);
        bar.Controls.Add(info);

        UpdateStatusPill(_visualReady ? "Ready" : "Admin required", _visualReady ? NoraTheme.Success : NoraTheme.Danger);
        return bar;
    }

    private Control BuildPowerPanel()
    {
        var panel = new Panel
        {
            Left = 0,
            Top = 84,
            Width = 836,
            Height = 510,
            BackColor = NoraTheme.Background
        };
        panel.Controls.Add(new Label
        {
            Text = "NORA VPN",
            Left = 0,
            Top = 34,
            Width = 836,
            Height = 44,
            Font = NoraTheme.Hero,
            ForeColor = NoraTheme.TextPrimary,
            TextAlign = ContentAlignment.MiddleCenter
        });

        _status.AutoSize = false;
        _status.SetBounds(355, 86, 126, 36);
        _status.TextAlign = ContentAlignment.MiddleCenter;
        _status.Font = new Font("Segoe UI Semibold", 12f);
        _status.ForeColor = NoraTheme.Success;
        _status.BackColor = NoraTheme.SuccessSoft;
        panel.Controls.Add(_status);

        _connect.Text = "Connect";
        StylePrimaryButton(_connect, success: true);
        if (_connect is NoraPowerButton power)
            power.DetailText = "00:00:00";
        _connect.SetBounds(307, 134, 222, 222);
        _connect.Click += async (_, _) => await RunUiActionAsync("Connect", ToggleAsync);
        panel.Controls.Add(_connect);

        _disconnect.Text = "Disconnect";
        _disconnect.SetBounds(322, 366, 192, 48);
        _disconnect.Font = new Font("Segoe UI Semibold", 11f);
        StyleGhostAccentButton(_disconnect);
        _disconnect.Enabled = false;
        _disconnect.Click += async (_, _) => await RunUiActionAsync("Disconnect", ToggleAsync);
        panel.Controls.Add(_disconnect);

        _trafficGraph.SetBounds(168, 410, 500, 68);
        _trafficGraph.Active = false;
        panel.Controls.Add(_trafficGraph);
        return panel;
    }

    private Control BuildModeTabs()
    {
        var tabs = new NoraCard
        {
            Left = 58,
            Top = 574,
            Width = 720,
            Height = 58,
            FillColor = NoraTheme.Surface,
            BorderColor = NoraTheme.Border,
            Radius = 14,
            Padding = new Padding(6),
            Margin = new Padding(0)
        };
        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            BackColor = Color.Transparent
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33));
        tabs.Controls.Add(grid);
        grid.Controls.Add(CreateTabButton("Rules", true), 0, 0);
        grid.Controls.Add(CreateTabButton("Global", false), 1, 0);
        grid.Controls.Add(CreateTabButton("Direct", false), 2, 0);
        return tabs;
    }

    private Control BuildSidebar()
    {
        var side = new Panel
        {
            BackColor = NoraTheme.Sidebar
        };
        side.Controls.Add(new Panel
        {
            Left = 263,
            Top = 0,
            Width = 1,
            Height = 900,
            BackColor = NoraTheme.BorderSoft
        });

        side.Controls.Add(new PictureBox
        {
            Left = 96,
            Top = 64,
            Width = 72,
            Height = 72,
            SizeMode = PictureBoxSizeMode.Zoom,
            Image = LoadLogoImage(),
            BackColor = Color.Transparent
        });
        side.Controls.Add(new Label
        {
            Text = "NORA VPN",
            Left = 0,
            Top = 146,
            Width = 264,
            Height = 36,
            Font = new Font("Segoe UI Semibold", 16f),
            ForeColor = Color.FromArgb(20, 20, 24),
            TextAlign = ContentAlignment.MiddleCenter
        });

        var home = CreateSideNavButton("\uE80F", "Home", true, 56, 250);
        var servers = CreateSideNavButton("\uE774", "Servers", false, 56, 323);
        var add = CreateSideNavButton("+", "Add", false, 56, 396);
        var users = CreateSideNavButton("\uE77B", "Users", false, 56, 469);
        var settings = CreateSideNavButton("\uE713", "Settings", false, 56, 542);
        WireClick(servers, (_, _) => ShowServersDialog());
        WireClick(add, (_, _) => OpenServerSetup());
        WireClick(users, (_, _) => ShowUsersDialog());
        WireClick(settings, (_, _) => ShowSettingsDialog());
        side.Controls.Add(home);
        side.Controls.Add(servers);
        side.Controls.Add(add);
        side.Controls.Add(users);
        side.Controls.Add(settings);

        var protectedCard = new NoraCard
        {
            Left = 38,
            Top = 738,
            Width = 200,
            Height = 130,
            FillColor = NoraTheme.Surface,
            BorderColor = NoraTheme.Border,
            Radius = 16,
            Padding = new Padding(16)
        };
        protectedCard.Controls.Add(new Label
        {
            Text = "\uE83D",
            Left = 18,
            Top = 21,
            Width = 38,
            Height = 38,
            Font = new Font("Segoe MDL2 Assets", 24f),
            ForeColor = NoraTheme.Success,
            TextAlign = ContentAlignment.MiddleCenter
        });
        protectedCard.Controls.Add(new Label
        {
            Text = "Protected",
            Left = 74,
            Top = 25,
            Width = 112,
            Height = 26,
            Font = new Font("Segoe UI Semibold", 12f),
            ForeColor = NoraTheme.Success,
            TextAlign = ContentAlignment.MiddleLeft
        });
        protectedCard.Controls.Add(new Label
        {
            Text = "Your connection\r\nis secure",
            Left = 30,
            Top = 72,
            Width = 142,
            Height = 46,
            Font = NoraTheme.Body,
            ForeColor = NoraTheme.TextSecondary,
            TextAlign = ContentAlignment.MiddleLeft
        });
        side.Controls.Add(protectedCard);

        side.Controls.Add(new Label
        {
            Text = "\uE897      \uE708      \uE946",
            Left = 42,
            Top = 870,
            Width = 180,
            Height = 28,
            Font = new Font("Segoe MDL2 Assets", 15f),
            ForeColor = NoraTheme.TextSecondary,
            TextAlign = ContentAlignment.MiddleCenter
        });
        return side;
    }

    private static void WireClick(Control control, EventHandler handler)
    {
        control.Click += handler;
        foreach (Control child in control.Controls)
            WireClick(child, handler);
    }

    private static Button CreateTabButton(string text, bool active)
    {
        var button = new NoraButton
        {
            Text = text,
            Dock = DockStyle.Fill,
            Margin = new Padding(2),
            Radius = 12,
            NormalColor = active ? Color.FromArgb(239, 244, 255) : NoraTheme.Surface,
            HoverColor = Color.FromArgb(232, 240, 255),
            PressedColor = Color.FromArgb(221, 232, 255),
            ForeColor = active ? NoraTheme.Accent : NoraTheme.TextSecondary,
            BorderColor = Color.Transparent,
            Font = new Font("Segoe UI Semibold", 11f)
        };
        return button;
    }

    private static Control CreateNavButton(string text, bool active)
    {
        var card = new NoraCard
        {
            Dock = DockStyle.Fill,
            FillColor = active ? Color.FromArgb(36, 92, 132) : NoraTheme.Surface,
            BorderColor = Color.Transparent,
            Radius = 16,
            Margin = new Padding(4, 0, 4, 0),
            Padding = new Padding(4)
        };
        card.Controls.Add(new Label
        {
            Text = text,
            Dock = DockStyle.Fill,
            ForeColor = active ? Color.White : NoraTheme.TextSecondary,
            Font = new Font("Segoe UI Semibold", 8.5f),
            TextAlign = ContentAlignment.MiddleCenter
        });
        return card;
    }

    private Control BuildServerCard()
    {
        var card = CreateCard(radius: 22);
        card.Left = 30;
        card.Top = 644;
        card.Width = 776;
        card.Height = 150;
        card.Margin = new Padding(0);
        card.Padding = new Padding(0);

        card.Controls.Add(new Label
        {
            Text = "\uE968",
            Left = 28,
            Top = 30,
            Width = 56,
            Height = 62,
            Font = new Font("Segoe MDL2 Assets", 36f),
            ForeColor = Color.FromArgb(69, 130, 255),
            TextAlign = ContentAlignment.MiddleCenter
        });
        card.Controls.Add(new Label
        {
            Text = "\uE73E",
            Left = 72,
            Top = 74,
            Width = 28,
            Height = 28,
            Font = new Font("Segoe MDL2 Assets", 18f),
            ForeColor = NoraTheme.Success,
            TextAlign = ContentAlignment.MiddleCenter
        });
        card.Controls.Add(CreateSectionLabel("Active Server", 104, 18, 190, 26));
        _server.Text = "Server: not loaded";
        _server.SetBounds(104, 56, 350, 30);
        StyleValueLabel(_server, 14f);
        card.Controls.Add(_server);
        _route.Text = "KRot tunnel: not configured";
        _route.SetBounds(104, 86, 410, 24);
        StyleBodyLabel(_route);
        card.Controls.Add(_route);
        _profilePath.Text = "Profile: not loaded";
        _profilePath.SetBounds(240, 126, 300, 20);
        StylePathLabel(_profilePath);
        card.Controls.Add(_profilePath);
        card.Controls.Add(new Label
        {
            Text = "Germany, Frankfurt   \u2022   KRot-T   \u2022   TLS",
            Left = 104,
            Top = 108,
            Width = 360,
            Height = 24,
            Font = NoraTheme.BodySmall,
            ForeColor = NoraTheme.TextSecondary,
            TextAlign = ContentAlignment.MiddleLeft
        });
        card.Controls.Add(new Label
        {
            Text = "Latency: 18 ms",
            Left = 104,
            Top = 126,
            Width = 126,
            Height = 20,
            Font = NoraTheme.BodySmall,
            ForeColor = NoraTheme.Success,
            TextAlign = ContentAlignment.MiddleLeft
        });

        _openProfile.Text = "Profile";
        _openProfile.SetBounds(590, 18, 150, 38);
        StyleSecondaryButton(_openProfile);
        _openProfile.Click += (_, _) => OpenProfile();
        card.Controls.Add(_openProfile);

        _diagnose.Text = "Diagnostics";
        _diagnose.SetBounds(590, 62, 150, 38);
        StyleSecondaryButton(_diagnose);
        _diagnose.Click += async (_, _) => await RunUiActionAsync("Diagnostics", DiagnoseAsync);
        card.Controls.Add(_diagnose);

        var add = new NoraButton { Text = "+ VPS" };
        add.SetBounds(590, 106, 150, 38);
        StyleOutlineAccentButton(add);
        add.Click += (_, _) => OpenServerSetup();
        card.Controls.Add(add);
        return card;
    }

    private Control BuildLog()
    {
        var card = CreateCard(radius: 22);
        card.Left = 30;
        card.Top = 808;
        card.Width = 776;
        card.Height = 80;
        card.Margin = new Padding(0);
        card.Padding = new Padding(20, 14, 20, 14);
        card.Controls.Add(new Label
        {
            Text = "Event Log",
            Left = 20,
            Top = 14,
            Width = 220,
            Height = 28,
            Font = NoraTheme.Section,
            ForeColor = NoraTheme.TextPrimary,
            TextAlign = ContentAlignment.MiddleLeft
        });
        card.Controls.Add(new Label
        {
            Text = "Clear",
            Left = 704,
            Top = 14,
            Width = 52,
            Height = 28,
            Font = new Font("Segoe UI Semibold", 9.5f),
            ForeColor = NoraTheme.Accent,
            TextAlign = ContentAlignment.MiddleRight
        });

        _log.Multiline = true;
        _log.ReadOnly = true;
        _log.ScrollBars = ScrollBars.None;
        _log.BorderStyle = BorderStyle.None;
        _log.BackColor = NoraTheme.Surface;
        _log.ForeColor = NoraTheme.TextSecondary;
        _log.Font = new Font("Cascadia Mono", 8.5f);
        _log.SetBounds(20, 46, 736, 26);
        card.Controls.Add(_log);
        return card;
    }

    private static NoraCard CreateCard(int radius = 18)
    {
        return new NoraCard
        {
            FillColor = NoraTheme.Surface,
            BorderColor = NoraTheme.Border,
            Radius = radius,
            Padding = new Padding(18),
            Margin = new Padding(0, 0, 0, 12)
        };
    }

    private static Control BuildFooter()
    {
        var footer = new Panel
        {
            Left = 0,
            Top = 890,
            Width = 836,
            Height = 38,
            BackColor = NoraTheme.Background
        };
        footer.Controls.Add(new Label
        {
            Text = "Version 1.0.0",
            Left = 0,
            Top = 4,
            Width = 836,
            Height = 28,
            Font = NoraTheme.BodySmall,
            ForeColor = NoraTheme.TextMuted,
            TextAlign = ContentAlignment.MiddleCenter
        });
        footer.Controls.Add(new Label
        {
            Text = "Check for updates",
            Left = 575,
            Top = 4,
            Width = 210,
            Height = 28,
            Font = NoraTheme.BodySmall,
            ForeColor = NoraTheme.Accent,
            TextAlign = ContentAlignment.MiddleRight
        });
        return footer;
    }

    private static Control CreateSideNavButton(string icon, string text, bool active, int x, int y)
    {
        var button = new NoraButton
        {
            Text = "",
            Left = x,
            Top = y,
            Width = 184,
            Height = 58,
            Radius = 12,
            NormalColor = active ? Color.FromArgb(239, 244, 255) : NoraTheme.Sidebar,
            HoverColor = Color.FromArgb(241, 246, 255),
            PressedColor = Color.FromArgb(229, 238, 255),
            BorderColor = Color.Transparent
        };
        button.Controls.Add(new Label
        {
            Text = icon,
            Left = 18,
            Top = 14,
            Width = 34,
            Height = 30,
            Font = new Font("Segoe MDL2 Assets", 20f),
            ForeColor = active ? NoraTheme.Accent : Color.FromArgb(25, 31, 42),
            TextAlign = ContentAlignment.MiddleCenter,
            BackColor = Color.Transparent
        });
        button.Controls.Add(new Label
        {
            Text = text,
            Left = 66,
            Top = 15,
            Width = 100,
            Height = 28,
            Font = new Font("Segoe UI Semibold", 11f),
            ForeColor = active ? NoraTheme.Accent : Color.FromArgb(34, 39, 49),
            TextAlign = ContentAlignment.MiddleLeft,
            BackColor = Color.Transparent
        });
        return button;
    }

    private static Button CreateRailButton(string text, bool active)
    {
        var button = new NoraButton
        {
            Text = text,
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 4, 0, 4),
            Font = new Font("Segoe UI Semibold", 9f),
            NormalColor = active ? Color.FromArgb(32, 85, 124) : Color.Transparent,
            HoverColor = active ? Color.FromArgb(39, 99, 143) : Color.FromArgb(26, 34, 44),
            PressedColor = Color.FromArgb(29, 74, 109),
            BorderColor = Color.Transparent,
            Radius = 16,
            ForeColor = active ? Color.White : NoraTheme.TextSecondary
        };
        return button;
    }

    private static Control CreateMiniMetricPanel()
    {
        var card = new NoraCard
        {
            Dock = DockStyle.Fill,
            FillColor = NoraTheme.SurfaceAlt,
            BorderColor = Color.FromArgb(35, 45, 58),
            Radius = 18,
            Padding = new Padding(14),
            Margin = new Padding(0, 8, 0, 0)
        };
        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 2,
            BackColor = Color.Transparent
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        grid.Controls.Add(CreateMetric("Mode", "Global"), 0, 0);
        grid.Controls.Add(CreateMetric("DNS", "Captured"), 1, 0);
        grid.Controls.Add(CreateMetric("Core", "Local"), 0, 1);
        grid.Controls.Add(CreateMetric("Cover", "TLS"), 1, 1);
        card.Controls.Add(grid);
        return card;
    }

    private static Control BuildConnectionStrip()
    {
        var strip = new NoraCard
        {
            Dock = DockStyle.Fill,
            FillColor = Color.FromArgb(20, 28, 39),
            BorderColor = Color.FromArgb(35, 46, 62),
            Radius = 18,
            Padding = new Padding(14, 10, 14, 10),
            Margin = new Padding(0)
        };
        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            BackColor = Color.Transparent
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33));
        grid.Controls.Add(CreateMetric("Routes", "Full"), 0, 0);
        grid.Controls.Add(CreateMetric("Transport", "TLS cover"), 1, 0);
        grid.Controls.Add(CreateMetric("Egress", "VPS"), 2, 0);
        strip.Controls.Add(grid);
        return strip;
    }

    private static Control CreateMetric(string title, string value)
    {
        var box = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = Color.Transparent,
            Margin = new Padding(2)
        };
        box.RowStyles.Add(new RowStyle(SizeType.Absolute, 18));
        box.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        box.Controls.Add(new Label
        {
            Text = title,
            Dock = DockStyle.Fill,
            Font = NoraTheme.BodySmall,
            ForeColor = NoraTheme.TextMuted,
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, 0);
        box.Controls.Add(new Label
        {
            Text = value,
            Dock = DockStyle.Fill,
            Font = NoraTheme.Section,
            ForeColor = NoraTheme.TextPrimary,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true
        }, 0, 1);
        return box;
    }

    private static Label CreateSectionLabel(string text) => new()
    {
        Text = text,
        Dock = DockStyle.Fill,
        Font = NoraTheme.Section,
        ForeColor = NoraTheme.TextSecondary,
        TextAlign = ContentAlignment.MiddleLeft
    };

    private static Label CreateSectionLabel(string text, int x, int y, int width, int height) => new()
    {
        Text = text,
        Left = x,
        Top = y,
        Width = width,
        Height = height,
        Font = NoraTheme.Section,
        ForeColor = NoraTheme.TextPrimary,
        TextAlign = ContentAlignment.MiddleLeft,
        AutoEllipsis = true
    };

    private static Label CreateBodyLabel(string text) => new()
    {
        Text = text,
        Dock = DockStyle.Fill,
        Font = NoraTheme.Body,
        ForeColor = NoraTheme.TextSecondary,
        TextAlign = ContentAlignment.MiddleLeft,
        AutoEllipsis = true
    };

    private static Label CreateChip(string text) => new()
    {
        Text = text,
        AutoSize = true,
        BackColor = NoraTheme.SurfaceAlt,
        ForeColor = NoraTheme.TextPrimary,
        Font = NoraTheme.BodySmall,
        Padding = new Padding(10, 6, 10, 6),
        Margin = new Padding(0, 0, 8, 0),
        TextAlign = ContentAlignment.MiddleCenter
    };

    private static void StyleValueLabel(Label label, float fontSize)
    {
        label.Font = new Font("Segoe UI Semibold", fontSize);
        label.ForeColor = NoraTheme.TextPrimary;
        label.BackColor = Color.Transparent;
        label.TextAlign = ContentAlignment.MiddleLeft;
        label.AutoEllipsis = true;
    }

    private static void StyleBodyLabel(Label label)
    {
        label.Font = NoraTheme.Body;
        label.ForeColor = NoraTheme.TextSecondary;
        label.BackColor = Color.Transparent;
        label.TextAlign = ContentAlignment.MiddleLeft;
        label.AutoEllipsis = true;
    }

    private static void StylePathLabel(Label label)
    {
        label.Font = NoraTheme.BodySmall;
        label.ForeColor = NoraTheme.TextSecondary;
        label.BackColor = Color.Transparent;
        label.TextAlign = ContentAlignment.MiddleLeft;
        label.AutoEllipsis = true;
    }

    private static Image? LoadLogoImage()
    {
        foreach (var candidate in new[]
        {
            Path.Combine(AppContext.BaseDirectory, "assets", "nora-logo.png"),
            Path.Combine(Directory.GetCurrentDirectory(), "assets", "nora-logo.png"),
            Path.Combine(AppContext.BaseDirectory, "nora-logo.png")
        })
        {
            if (!File.Exists(candidate))
                continue;
            try
            {
                using var stream = File.OpenRead(candidate);
                using var image = Image.FromStream(stream);
                return new Bitmap(image);
            }
            catch
            {
            }
        }
        return null;
    }

    private void LoadDefaultProfile()
    {
        var stored = NoraAppState.TryLoadActiveProfilePath();
        if (!string.IsNullOrWhiteSpace(stored) && File.Exists(stored))
        {
            LoadProfile(stored);
            return;
        }

        var baseDir = AppContext.BaseDirectory;
        var path = Path.Combine(baseDir, "client-profile.json");
        if (!File.Exists(path))
        {
            var alt = Path.Combine(Directory.GetCurrentDirectory(), "client-profile.json");
            if (File.Exists(alt))
                path = alt;
        }

        if (File.Exists(path))
            LoadProfile(path);
        else
            AppendLog("No KRot client profile found. Use + to add and provision a server.");
    }

    private void LoadProfile(string path)
    {
        try
        {
            _config = NvpConfig.Load(path);
            _configPath = path;
            var s = _config.Servers[0];
            _server.Text = $"Server: {s.Address}:{s.Port}";
            _route.Text = $"KRot: {_config.Tunnel.ClientIp} -> {_config.Tunnel.ServerIp}, DNS {string.Join(", ", _config.Tunnel.Dns)}";
            _profilePath.Text = "Profile: " + Path.GetFileName(path);
            NoraAppState.SaveActiveProfilePath(path);
            AppendLog("Loaded KRot profile " + path);
        }
        catch (Exception ex)
        {
            AppendLog("Profile error: " + ex.Message);
            UpdateStatusPill("Profile error", Color.FromArgb(199, 76, 62));
        }
    }

    private async Task ToggleAsync()
    {
        if (_busy)
            return;

        if (_core is not null)
        {
            await DisconnectAsync();
            return;
        }

        if (_config is null)
        {
            AppendLog("No profile loaded");
            return;
        }

        _busy = true;
        _connect.Enabled = false;
        _disconnect.Enabled = false;
        _connect.Text = "Connecting";
        if (_connect is NoraPowerButton connectingPower)
        {
            connectingPower.ConnectedVisual = false;
            connectingPower.DetailText = "Please wait";
        }
        StylePrimaryButton(_connect);
        UpdateStatusPill("Connecting", Color.FromArgb(37, 99, 235));
        try
        {
            _core = new NvpCoreProcess(_configPath, AppendLog);
            await _core.StartAsync(TimeSpan.FromSeconds(45));
            _ = MonitorCoreAsync(_core);
            _trafficGraph.Active = true;
            _trafficGraph.ResetSamples();
            UpdateStatusPill("Connected", Color.FromArgb(32, 132, 96));
            _connect.Text = "Connected";
            if (_connect is NoraPowerButton connectedPower)
            {
                connectedPower.ConnectedVisual = true;
                connectedPower.DetailText = "00:12:47";
            }
            StylePrimaryButton(_connect, success: true);
            _disconnect.Enabled = true;
        }
        catch (Exception ex)
        {
            AppendLog("Connect failed: " + ex.Message);
            await DisconnectAsync();
            UpdateStatusPill("Failed", Color.FromArgb(199, 76, 62));
            _connect.Text = "Connect";
            if (_connect is NoraPowerButton failedPower)
            {
                failedPower.ConnectedVisual = false;
                failedPower.DetailText = "00:00:00";
            }
        }
        finally
        {
            _busy = false;
            _connect.Enabled = true;
        }
    }

    private async Task MonitorCoreAsync(NvpCoreProcess core)
    {
        try
        {
            await core.WaitForExitAsync();
        }
        catch (Exception ex)
        {
            AppendLog("Core monitor error: " + ex.Message);
        }

        if (!ReferenceEquals(_core, core))
            return;

        _core = null;
        _trafficGraph.Active = false;
        _trafficGraph.ResetSamples();
        var admin = NvpClient.IsAdministrator();
        UpdateStatusPill(admin ? "Ready" : "Admin required", admin ? Color.FromArgb(104, 115, 133) : Color.FromArgb(199, 76, 62));
        _connect.Text = "Connect";
        if (_connect is NoraPowerButton power)
        {
            power.ConnectedVisual = false;
            power.DetailText = "00:00:00";
        }
        StylePrimaryButton(_connect, success: true);
        _disconnect.Enabled = false;
        AppendLog("KRot core stopped");
    }

    private async Task DisconnectAsync()
    {
        _busy = true;
        _connect.Enabled = false;
        try
        {
            if (_core is not null)
            {
                await _core.StopAsync(TimeSpan.FromSeconds(8));
                _core = null;
            }
            _trafficGraph.Active = false;
            _trafficGraph.ResetSamples();

            var admin = NvpClient.IsAdministrator();
            UpdateStatusPill(admin ? "Ready" : "Admin required", admin ? Color.FromArgb(104, 115, 133) : Color.FromArgb(199, 76, 62));
            _connect.Text = "Connect";
            if (_connect is NoraPowerButton power)
            {
                power.ConnectedVisual = false;
                power.DetailText = "00:00:00";
            }
            StylePrimaryButton(_connect, success: true);
            _disconnect.Enabled = false;
        }
        finally
        {
            _busy = false;
            _connect.Enabled = true;
        }
    }

    private async Task DiagnoseAsync()
    {
        if (_config is null)
        {
            AppendLog("No profile loaded");
            return;
        }

        _diagnose.Enabled = false;
        AppendLog("Diagnostics started");
        try
        {
            foreach (var line in NvpProfileLinter.Check(_config))
                AppendLog("Diagnostics: profile_lint, " + line);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(12));
            var relay = await NvpDiagnostics.ProbeAsync(_config, cts.Token);
            AppendLog($"Diagnostics: {relay.Stage}, success={relay.Success}, {relay.Details}");

            var tun = NvpDiagnostics.TunCheck(_config);
            AppendLog($"Diagnostics: {tun.Stage}, success={tun.Success}, {tun.Details}");
        }
        catch (Exception ex)
        {
            AppendLog("Diagnostics failed: " + ex.Message);
        }
        finally
        {
            _diagnose.Enabled = true;
        }
    }

    private async Task RunUiActionAsync(string name, Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            AppendLog($"{name} failed: {ex.Message}");
        }
    }

    private void OpenProfile()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Open KRot profile",
            Filter = "KRot profile (*.json)|*.json|All files (*.*)|*.*",
            FileName = string.IsNullOrEmpty(_configPath) ? "client-profile.json" : _configPath
        };
        if (dialog.ShowDialog(this) == DialogResult.OK)
            LoadProfile(dialog.FileName);
    }

    private void ShowServersDialog()
    {
        using var dialog = new NoraServersDialog();
        if (dialog.ShowDialog(this) == DialogResult.OK && File.Exists(dialog.SelectedProfilePath))
            LoadProfile(dialog.SelectedProfilePath);
    }

    private void ShowUsersDialog()
    {
        if (_config is null || string.IsNullOrWhiteSpace(_configPath))
        {
            AppendLog("Load or import a KRot server before opening Users.");
            return;
        }

        using var dialog = new NoraUsersDialog(_configPath, _config, AppendLog);
        dialog.ShowDialog(this);
    }

    private void ShowSettingsDialog()
    {
        using var dialog = new NoraLogDialog(_log.Text);
        dialog.ShowDialog(this);
    }

    private void OpenServerSetup()
    {
        if (_core is not null)
        {
            AppendLog("Disconnect before adding or provisioning a server");
            return;
        }

        using var dialog = new NoraAddDialog();
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        if (dialog.Mode == NoraAddMode.InstallKRot)
        {
            using var setup = new NoraServerSetupDialog();
            if (setup.ShowDialog(this) == DialogResult.OK)
                _ = RunUiActionAsync("Install KRot", () => ProvisionServerAsync(setup.Settings));
            return;
        }

        _ = RunUiActionAsync("Import key", async () =>
        {
            var result = await NoraProfileImporter.ImportAsync(dialog.KeyText, AppendLog);
            if (result.Protocol == "KRot" && File.Exists(result.ClientProfilePath))
                LoadProfile(result.ClientProfilePath);
            AppendLog(result.Message);
        });
    }

    private async Task ProvisionServerAsync(NoraServerSettings settings)
    {
        if (_busy)
            return;

        _busy = true;
        _connect.Enabled = false;
        _diagnose.Enabled = false;
        _addServer.Enabled = false;
        UpdateStatusPill("Provisioning", Color.FromArgb(37, 99, 235));

        try
        {
            var result = await NoraProvisioner.ProvisionAsync(settings, AppendLog);
            LoadProfile(result.ClientProfilePath);
            UpdateStatusPill("Ready", Color.FromArgb(104, 115, 133));
            AppendLog("Server profile activated: " + result.DisplayName);
        }
        catch (Exception ex)
        {
            AppendLog("Server setup failed: " + ex.Message);
            UpdateStatusPill("Setup failed", Color.FromArgb(199, 76, 62));
        }
        finally
        {
            _busy = false;
            _connect.Enabled = true;
            _diagnose.Enabled = true;
            _addServer.Enabled = true;
        }
    }

    private void UpdateStatusPill(string text, Color color)
    {
        _status.Text = text;
        if (text.Contains("Connected", StringComparison.OrdinalIgnoreCase))
        {
            _status.Text = "\u25CF  Connected";
            _status.BackColor = NoraTheme.SuccessSoft;
            _status.ForeColor = NoraTheme.Success;
            return;
        }
        if (text.Contains("Ready", StringComparison.OrdinalIgnoreCase))
        {
            _status.Text = "Ready";
            _status.BackColor = Color.FromArgb(245, 247, 251);
            _status.ForeColor = NoraTheme.TextSecondary;
            return;
        }
        _status.BackColor = Color.FromArgb(252, 240, 239);
        _status.ForeColor = color;
    }

    private void AppendLog(string line)
    {
        if (InvokeRequired)
        {
            BeginInvoke(new Action(() => AppendLog(line)));
            return;
        }

        _log.AppendText($"[{DateTime.Now:HH:mm:ss}] {line}{Environment.NewLine}");
        TryUpdateTrafficGraph(line);
    }

    private void TryUpdateTrafficGraph(string line)
    {
        const string marker = "traffic:";
        var index = line.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
            return;

        var up = ExtractMetric(line, "up_bps=");
        var down = ExtractMetric(line, "down_bps=");
        _trafficGraph.AddSample(up, down);
    }

    private static long ExtractMetric(string line, string name)
    {
        var start = line.IndexOf(name, StringComparison.OrdinalIgnoreCase);
        if (start < 0)
            return 0;
        start += name.Length;
        var end = start;
        while (end < line.Length && char.IsDigit(line[end]))
            end++;
        return long.TryParse(line[start..end], out var value) ? value : 0;
    }

    private static void StylePrimaryButton(Button button, bool success = false)
    {
        var normal = success ? NoraTheme.Success : NoraTheme.Accent;
        var hover = success ? NoraTheme.SuccessHover : NoraTheme.AccentHover;
        var pressed = success ? NoraTheme.SuccessPressed : NoraTheme.AccentPressed;
        StyleButtonCore(button, normal, hover, pressed, Color.White, 11f, 0);
    }

    private static void StyleDangerButton(Button button)
        => StyleButtonCore(button, NoraTheme.Danger, NoraTheme.DangerHover, NoraTheme.DangerPressed, Color.White, 11f, 0);

    private static void StyleSecondaryButton(Button button)
        => StyleButtonCore(button, NoraTheme.Neutral, NoraTheme.NeutralHover, NoraTheme.NeutralPressed, NoraTheme.TextPrimary, 10f, 1);

    private static void StyleOutlineAccentButton(Button button)
        => StyleButtonCore(button, Color.White, Color.FromArgb(244, 248, 255), Color.FromArgb(234, 242, 255), NoraTheme.Accent, 10.5f, 1);

    private static void StyleGhostAccentButton(Button button)
        => StyleButtonCore(button, Color.White, Color.FromArgb(246, 249, 255), Color.FromArgb(236, 244, 255), NoraTheme.Accent, 11f, 1);

    private static void StyleMicroButton(Button button, Color normal, Color hover, Color pressed, Color foreColor)
        => StyleButtonCore(button, normal, hover, pressed, foreColor, 10f, 0);

    private static void StyleButtonCore(Button button, Color normal, Color hover, Color pressed, Color foreColor, float fontSize, int borderSize)
    {
        if (button is NoraButton nora)
        {
            var effectiveFontSize = button is NoraPowerButton ? 15f : fontSize;
            nora.NormalColor = normal;
            nora.HoverColor = hover;
            nora.PressedColor = pressed;
            nora.BorderColor = borderSize > 0 ? NoraTheme.Border : Color.Transparent;
            nora.ForeColor = foreColor;
            nora.Font = new Font("Segoe UI Semibold", effectiveFontSize);
            nora.Cursor = Cursors.Hand;
            nora.Margin = new Padding(0, 0, 10, 0);
            nora.Height = Math.Max(nora.Height, 40);
            nora.Invalidate();
            return;
        }

        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = borderSize;
        button.FlatAppearance.BorderColor = NoraTheme.Border;
        button.FlatAppearance.MouseOverBackColor = hover;
        button.FlatAppearance.MouseDownBackColor = pressed;
        button.UseVisualStyleBackColor = false;
        button.BackColor = normal;
        button.ForeColor = foreColor;
        button.Font = new Font("Segoe UI Semibold", fontSize);
        button.Cursor = Cursors.Hand;
        button.Margin = new Padding(0, 0, 10, 0);
        button.Height = Math.Max(button.Height, 40);
        button.FlatAppearance.CheckedBackColor = pressed;
    }
}

internal sealed class NvpCoreProcess(string profilePath, Action<string> log) : IVpnCoreProcess
{
    private Process? _process;
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _exited = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public async Task StartAsync(TimeSpan timeout)
    {
        var exe = Environment.ProcessPath ?? throw new InvalidOperationException("Cannot locate nvp.exe");
        var psi = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            CreateNoWindow = true,
            WorkingDirectory = AppContext.BaseDirectory
        };
        psi.ArgumentList.Add("client");
        psi.ArgumentList.Add(profilePath);

        _process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        _process.OutputDataReceived += (_, e) => HandleLine(e.Data);
        _process.ErrorDataReceived += (_, e) => HandleLine(e.Data);
        _process.Exited += (_, _) =>
        {
            if (!_ready.Task.IsCompleted)
                _ready.TrySetException(new InvalidOperationException("KRot core exited before tunnel became ready"));
            _exited.TrySetResult();
        };

        if (!_process.Start())
            throw new InvalidOperationException("Cannot start KRot core process");
        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();

        using var cts = new CancellationTokenSource(timeout);
        using var _ = cts.Token.Register(() => _ready.TrySetException(new TimeoutException("KRot core did not become ready in time")));
        await _ready.Task;
    }

    public async Task StopAsync(TimeSpan timeout)
    {
        var p = _process;
        if (p is null || p.HasExited)
            return;
        try
        {
            await p.StandardInput.WriteLineAsync("stop");
            await p.StandardInput.FlushAsync();
        }
        catch
        {
        }

        var finished = await Task.WhenAny(_exited.Task, Task.Delay(timeout));
        if (finished != _exited.Task && !p.HasExited)
        {
            try { p.Kill(entireProcessTree: true); } catch { }
        }
    }

    public Task WaitForExitAsync() => _exited.Task;

    private void HandleLine(string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return;
        log(line);
        if (line.Contains("tunnel is up", StringComparison.OrdinalIgnoreCase))
            _ready.TrySetResult();
        if (line.Contains("[fatal]", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("Connect failed", StringComparison.OrdinalIgnoreCase))
            _ready.TrySetException(new InvalidOperationException(line));
    }
}

internal sealed class NoraServerSettings
{
    public string DisplayName { get; init; } = "";
    public string Host { get; init; } = "";
    public int Port { get; init; } = 443;
    public string SshUser { get; init; } = "root";
    public string SshPassword { get; init; } = "";
    public string SshHostKey { get; init; } = "";
    public string TlsName { get; init; } = "";
    public string CoverHost { get; init; } = "";
    public bool ProvisionRemote { get; init; } = true;
}

internal sealed class NoraProvisionResult
{
    public string DisplayName { get; init; } = "";
    public string ClientProfilePath { get; init; } = "";
    public string ServerProfilePath { get; init; } = "";
    public string ConnectionKeyPath { get; init; } = "";
    public string ConnectionKey { get; init; } = "";
}

internal static class NoraAppState
{
    private sealed class StateFile
    {
        public string ActiveProfilePath { get; set; } = "";
    }

    public static string DataRoot
    {
        get
        {
            var overrideRoot = Environment.GetEnvironmentVariable("NORA_DATA_ROOT");
            var root = !string.IsNullOrWhiteSpace(overrideRoot)
                ? overrideRoot
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NORA VPN");
            Directory.CreateDirectory(root);
            return root;
        }
    }

    public static string TryLoadActiveProfilePath()
    {
        try
        {
            var path = StatePath();
            if (!File.Exists(path))
                return "";
            var state = JsonSerializer.Deserialize<StateFile>(File.ReadAllText(path)) ?? new StateFile();
            return state.ActiveProfilePath;
        }
        catch
        {
            return "";
        }
    }

    public static void SaveActiveProfilePath(string profilePath)
    {
        try
        {
            var state = new StateFile { ActiveProfilePath = profilePath };
            File.WriteAllText(StatePath(), JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
        }
    }

    private static string StatePath() => Path.Combine(DataRoot, "state.json");
}

internal static class NoraProvisioner
{
    public static async Task<NoraProvisionResult> ProvisionAsync(NoraServerSettings settings, Action<string> log)
    {
        Validate(settings);

        var profileId = $"krot-{Sanitize(settings.Host)}-{DateTime.UtcNow:yyyyMMddHHmmss}";
        var credentialId = "krot-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(8)).ToLowerInvariant();
        var credentialKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var pendingDir = Path.Combine(NoraAppState.DataRoot, "pending-installs", profileId);
        var profileDir = Path.Combine(NoraAppState.DataRoot, "profiles", profileId);
        if (Directory.Exists(pendingDir))
            Directory.Delete(pendingDir, recursive: true);
        Directory.CreateDirectory(pendingDir);

        var clientProfile = BuildClientProfile(settings, profileId, credentialId, credentialKey);
        var serverProfile = BuildServerProfile(settings, profileId, credentialId, credentialKey);
        var connectionKey = BuildConnectionKey(settings, profileId, credentialId, credentialKey);
        var pendingClientProfilePath = Path.Combine(pendingDir, "client-profile.json");
        var pendingServerProfilePath = Path.Combine(pendingDir, "server-profile.json");
        var pendingConnectionKeyPath = Path.Combine(pendingDir, "connection.key");
        var clientProfilePath = Path.Combine(profileDir, "client-profile.json");
        var serverProfilePath = Path.Combine(profileDir, "server-profile.json");
        var connectionKeyPath = Path.Combine(profileDir, "connection.key");
        try
        {
            File.WriteAllText(pendingClientProfilePath, JsonSerializer.Serialize(clientProfile, NvpConfig.JsonOptions()));
            File.WriteAllText(pendingServerProfilePath, JsonSerializer.Serialize(serverProfile, NvpConfig.JsonOptions()));
            File.WriteAllText(pendingConnectionKeyPath, connectionKey);
            log("Prepared KRot client/server profiles");

            if (settings.ProvisionRemote)
                await TryDeployRemoteAsync(settings, pendingServerProfilePath, log);
            else
                log("Remote provisioning skipped by user");

            CleanupExistingKRotProfiles(settings, profileDir, log);
            if (Directory.Exists(profileDir))
                Directory.Delete(profileDir, recursive: true);
            Directory.CreateDirectory(Path.GetDirectoryName(profileDir)!);
            Directory.Move(pendingDir, profileDir);
            NoraAppState.SaveActiveProfilePath(clientProfilePath);
            log("Saved verified KRot profile in " + profileDir);
            log("Generated KRot connection key in " + connectionKeyPath);
            TryCopyPortableProfile(clientProfilePath, log);
        }
        catch
        {
            try
            {
                if (Directory.Exists(pendingDir))
                    Directory.Delete(pendingDir, recursive: true);
            }
            catch
            {
            }
            throw;
        }

        return new NoraProvisionResult
        {
            DisplayName = settings.DisplayName,
            ClientProfilePath = clientProfilePath,
            ServerProfilePath = serverProfilePath,
            ConnectionKeyPath = connectionKeyPath,
            ConnectionKey = connectionKey
        };
    }

    private static void Validate(NoraServerSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.Host))
            throw new InvalidOperationException("Server IP or hostname is required");
        if (settings.Port <= 0 || settings.Port > 65535)
            throw new InvalidOperationException("Server port must be between 1 and 65535");
        if (settings.ProvisionRemote)
        {
            if (string.IsNullOrWhiteSpace(settings.SshUser))
                throw new InvalidOperationException("SSH user is required for provisioning");
            if (string.IsNullOrWhiteSpace(settings.SshPassword))
                throw new InvalidOperationException("SSH password is required for provisioning");
        }
    }

    private static void CleanupExistingKRotProfiles(NoraServerSettings settings, string keepProfileDir, Action<string> log)
    {
        var root = Path.Combine(NoraAppState.DataRoot, "profiles");
        var keep = Path.GetFullPath(keepProfileDir)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (Directory.Exists(root))
        {
            foreach (var clientProfile in Directory.GetFiles(root, "client-profile.json", SearchOption.AllDirectories))
            {
                try
                {
                    var dir = Path.GetDirectoryName(clientProfile);
                    if (string.IsNullOrWhiteSpace(dir))
                        continue;
                    var dirFull = Path.GetFullPath(dir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
                    if (dirFull.Equals(keep, StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (IsSameKRotEndpoint(clientProfile, settings.Host, settings.Port))
                    {
                        Directory.Delete(dir, recursive: true);
                        log("Removed old local KRot profile for " + settings.Host + ":" + settings.Port);
                    }
                }
                catch
                {
                }
            }
        }

        foreach (var portable in new[]
        {
            Path.Combine(AppContext.BaseDirectory, "client-profile.json"),
            Path.Combine(Directory.GetCurrentDirectory(), "client-profile.json")
        })
        {
            try
            {
                if (IsSameKRotEndpoint(portable, settings.Host, settings.Port))
                {
                    File.Delete(portable);
                    log("Removed stale portable KRot profile");
                }
            }
            catch
            {
            }
        }
    }

    private static bool IsSameKRotEndpoint(string profilePath, string host, int port)
    {
        if (!File.Exists(profilePath))
            return false;
        try
        {
            var cfg = NvpConfig.Load(profilePath);
            var server = cfg.Servers.FirstOrDefault();
            return server is not null &&
                   server.Port == port &&
                   string.Equals(server.Address, host, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static NvpConfig BuildClientProfile(NoraServerSettings settings, string profileId, string credentialId, string credentialKey)
    {
        return new NvpConfig
        {
            Schema = "nvp-profile-v1",
            ProfileId = profileId,
            CredentialId = credentialId,
            CredentialKey = credentialKey,
            Servers =
            [
                new NvpServerEntry
                {
                    Address = settings.Host,
                    Port = settings.Port,
                    TlsName = EffectiveTlsName(settings),
                    CoverHost = EffectiveCoverHost(settings)
                }
            ],
            TransportProfile = "tls_http_cover_v1",
            CoverProfiles =
            [
                new NvpCoverProfile
                {
                    Name = "tls_http_cover_v1",
                    Mode = "tls_http_cover_v1",
                    Compliance = "KRot-T",
                    BrowserGrade = false
                }
            ],
            Tls = new NvpTlsConfig { Enabled = true },
            Tunnel = new NvpTunnelConfig
            {
                InterfaceName = "KRot",
                LinuxInterfaceName = "krot0",
                ClientIp = "10.66.0.2",
                ServerIp = "10.66.0.1",
                Cidr = "10.66.0.0/24",
                Dns = ["1.1.1.1", "8.8.8.8"]
            },
            ListenPort = settings.Port
        };
    }

    private static NvpConfig BuildServerProfile(NoraServerSettings settings, string profileId, string credentialId, string credentialKey)
    {
        var tlsName = EffectiveTlsName(settings);
        return new NvpConfig
        {
            Schema = "nvp-server-v1",
            ProfileId = profileId,
            CredentialId = credentialId,
            CredentialKey = credentialKey,
            Servers =
            [
                new NvpServerEntry
                {
                    Address = "0.0.0.0",
                    Port = settings.Port,
                    TlsName = tlsName,
                    CoverHost = EffectiveCoverHost(settings)
                }
            ],
            TransportProfile = "tls_http_cover_v1",
            CoverProfiles =
            [
                new NvpCoverProfile
                {
                    Name = "tls_http_cover_v1",
                    Mode = "tls_http_cover_v1",
                    Compliance = "KRot-T",
                    BrowserGrade = false
                }
            ],
            Tls = new NvpTlsConfig
            {
                Enabled = true,
                CertificatePath = $"/etc/letsencrypt/live/{tlsName}/fullchain.pem",
                PrivateKeyPath = $"/etc/letsencrypt/live/{tlsName}/privkey.pem"
            },
            Tunnel = new NvpTunnelConfig
            {
                InterfaceName = "KRot",
                LinuxInterfaceName = "krot0",
                ClientIp = "10.66.0.2",
                ServerIp = "10.66.0.1",
                Cidr = "10.66.0.0/24",
                Dns = ["1.1.1.1", "8.8.8.8"]
            },
            ListenPort = settings.Port
        };
    }

    private static string BuildConnectionKey(NoraServerSettings settings, string profileId, string credentialId, string credentialKey)
    {
        var payload = new
        {
            schema = "nora-connection-key-v1",
            profile_id = profileId,
            transport_profile = "tls_http_cover_v1",
            server = new
            {
                host = settings.Host,
                port = settings.Port,
                tls_name = EffectiveTlsName(settings),
                cover_host = EffectiveCoverHost(settings)
            },
            credentials = new
            {
                credential_id = credentialId,
                credential_key = credentialKey
            }
        };

        var json = JsonSerializer.SerializeToUtf8Bytes(payload, new JsonSerializerOptions
        {
            WriteIndented = false,
            PropertyNamingPolicy = null
        });
        return "nora1." + Base64UrlEncode(json);
    }

    private static async Task TryDeployRemoteAsync(NoraServerSettings settings, string serverProfilePath, Action<string> log)
    {
        var serverBinary = FindServerBinary();
        var serviceFile = FindServiceFile();
        var plink = FindTool("plink.exe");
        var pscp = FindTool("pscp.exe");
        if (serverBinary.Length == 0 || !File.Exists(serverBinary))
            throw new InvalidOperationException("Linux KRot server binary was not found. Build/publish dist/server/nvp before provisioning.");
        if (plink.Length == 0 || pscp.Length == 0)
            throw new InvalidOperationException("PuTTY plink.exe/pscp.exe were not found. Install PuTTY or add it to PATH before provisioning.");
        if (serviceFile.Length == 0 || !File.Exists(serviceFile))
            throw new InvalidOperationException("KRot systemd service file deploy/nvp.service was not found.");

        log($"Provisioning VPS {settings.SshUser}@{settings.Host} with KRot core");
        var target = $"{settings.SshUser}@{settings.Host}";
        await ExternalToolRunner.RunAsync(plink, BuildSshArgs(settings, includeTarget: true).Concat([target, "mkdir -p /opt/nvp"]).ToArray(), log);

        var pscpBase = BuildSshArgs(settings, includeTarget: false);
        await ExternalToolRunner.RunAsync(pscp, pscpBase.Concat([serverBinary, $"{target}:/opt/nvp/nvp.new"]).ToArray(), log);
        await ExternalToolRunner.RunAsync(pscp, pscpBase.Concat([serverProfilePath, $"{target}:/opt/nvp/server-profile.json.new"]).ToArray(), log);
        await ExternalToolRunner.RunAsync(pscp, pscpBase.Concat([serviceFile, $"{target}:/tmp/nvp.service.new"]).ToArray(), log);

        var setupCommand = BuildRemoteSetupCommand(settings, serviceUploaded: true);
        await ExternalToolRunner.RunAsync(plink, BuildSshArgs(settings, includeTarget: true).Concat([target, setupCommand]).ToArray(), log);
        log("Remote provisioning finished");
    }

    private static string BuildRemoteSetupCommand(NoraServerSettings settings, bool serviceUploaded)
    {
        var tlsName = ShellEscape(EffectiveTlsName(settings));
        var commands = new List<string>
        {
            "mkdir -p /opt/nvp",
            "systemctl stop nvp.service >/dev/null 2>&1 || true",
            "chmod +x /opt/nvp/nvp.new",
            "mv /opt/nvp/nvp.new /opt/nvp/nvp",
            "mv /opt/nvp/server-profile.json.new /opt/nvp/server-profile.json"
        };
        if (serviceUploaded)
            commands.Add("mv /tmp/nvp.service.new /etc/systemd/system/nvp.service");
        commands.Add("systemctl daemon-reload");
        commands.Add($"if [ ! -f /etc/letsencrypt/live/{tlsName}/fullchain.pem ]; then apt-get update && DEBIAN_FRONTEND=noninteractive apt-get install -y certbot && certbot certonly --standalone --non-interactive --agree-tos --register-unsafely-without-email -d {tlsName}; fi");
        commands.Add("systemctl enable nvp.service >/dev/null 2>&1 || true");
        commands.Add("systemctl start nvp.service");
        commands.Add("systemctl is-active nvp.service");
        return string.Join(" && ", commands);
    }

    private static string[] BuildSshArgs(NoraServerSettings settings, bool includeTarget)
    {
        var args = new List<string> { "-batch", "-pw", settings.SshPassword };
        if (!string.IsNullOrWhiteSpace(settings.SshHostKey))
        {
            args.Add("-hostkey");
            args.Add(settings.SshHostKey);
        }
        if (includeTarget)
            args.Insert(0, "-ssh");
        return [.. args];
    }

    private static void TryCopyPortableProfile(string clientProfilePath, Action<string> log)
    {
        try
        {
            var portablePath = Path.Combine(AppContext.BaseDirectory, "client-profile.json");
            File.Copy(clientProfilePath, portablePath, overwrite: true);
            log("Updated portable client-profile.json next to the app");
        }
        catch
        {
        }
    }

    private static string FindServerBinary()
    {
        foreach (var path in CandidatePaths("dist", "server", "nvp"))
        {
            if (File.Exists(path))
                return path;
        }
        return "";
    }

    private static string FindServiceFile()
    {
        foreach (var path in CandidatePaths("deploy", "nvp.service"))
        {
            if (File.Exists(path))
                return path;
        }
        return "";
    }

    private static IEnumerable<string> CandidatePaths(params string[] parts)
    {
        var roots = new[]
        {
            Directory.GetCurrentDirectory(),
            AppContext.BaseDirectory,
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..")),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", ".."))
        };
        foreach (var root in roots.Distinct(StringComparer.OrdinalIgnoreCase))
            yield return Path.Combine(new[] { root }.Concat(parts).ToArray());
    }

    private static string FindTool(string name)
    {
        foreach (var path in new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "PuTTY", name),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "PuTTY", name),
            name
        })
        {
            if (path == name || File.Exists(path))
                return path;
        }
        return "";
    }

    private static string EffectiveTlsName(NoraServerSettings settings)
        => string.IsNullOrWhiteSpace(settings.TlsName) ? $"{settings.Host}.sslip.io" : settings.TlsName.Trim();

    private static string EffectiveCoverHost(NoraServerSettings settings)
        => string.IsNullOrWhiteSpace(settings.CoverHost) ? EffectiveTlsName(settings) : settings.CoverHost.Trim();

    private static string Sanitize(string value)
    {
        var chars = value.ToLowerInvariant().Select(ch => char.IsLetterOrDigit(ch) ? ch : '-').ToArray();
        var clean = new string(chars).Trim('-');
        return string.IsNullOrWhiteSpace(clean) ? "server" : clean;
    }

    private static string ShellEscape(string value)
        => value.Replace("'", "'\"'\"'", StringComparison.Ordinal);

    private static string Base64UrlEncode(byte[] data)
        => Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

internal static class ExternalToolRunner
{
    public static async Task RunAsync(string exe, IReadOnlyList<string> args, Action<string> log)
    {
        var psi = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var arg in args)
            psi.ArgumentList.Add(arg);

        using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        process.OutputDataReceived += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) log("[ssh] " + e.Data); };
        process.ErrorDataReceived += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) log("[ssh] " + e.Data); };

        if (!process.Start())
            throw new InvalidOperationException("Cannot start " + exe);

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
            throw new InvalidOperationException($"{Path.GetFileName(exe)} exited with code {process.ExitCode}");
    }
}

internal enum NoraAddMode
{
    ImportKey,
    InstallKRot
}

internal sealed class NoraImportResult
{
    public string Protocol { get; init; } = "";
    public string ClientProfilePath { get; init; } = "";
    public string Message { get; init; } = "";
}

internal static class NoraProfileImporter
{
    public static async Task<NoraImportResult> ImportAsync(string text, Action<string> log)
    {
        text = text.Trim();
        if (string.IsNullOrWhiteSpace(text))
            throw new InvalidOperationException("Paste a connection key first.");
        if (text.StartsWith("nora1.", StringComparison.OrdinalIgnoreCase))
            return ImportKRot(text, log);
        if (text.StartsWith("vless://", StringComparison.OrdinalIgnoreCase))
            return await StoreExternalProfileAsync("Xray Reality", text, ".uri");
        if (text.Contains("[Interface]", StringComparison.OrdinalIgnoreCase) ||
            text.StartsWith("awg://", StringComparison.OrdinalIgnoreCase) ||
            text.StartsWith("wireguard://", StringComparison.OrdinalIgnoreCase))
            return await StoreExternalProfileAsync("AWG 2.0", NormalizeAwgConfig(text), ".conf");
        throw new InvalidOperationException("Unsupported key. Expected nora1, VLESS Reality, or AWG/WireGuard config.");
    }

    private static NoraImportResult ImportKRot(string key, Action<string> log)
    {
        var json = DecodeNoraPayload(key);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (!string.Equals(root.GetProperty("schema").GetString(), "nora-connection-key-v1", StringComparison.Ordinal))
            throw new InvalidOperationException("Unsupported NORA key schema.");

        var server = root.GetProperty("server");
        var credentials = root.GetProperty("credentials");
        var profileId = root.TryGetProperty("profile_id", out var profileIdEl) ? profileIdEl.GetString() ?? "" : "";
        if (string.IsNullOrWhiteSpace(profileId))
            profileId = "krot-import-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss");

        var host = server.GetProperty("host").GetString() ?? throw new InvalidOperationException("KRot key has no server.host.");
        var port = server.TryGetProperty("port", out var portEl) ? portEl.GetInt32() : 443;
        var tlsName = server.TryGetProperty("tls_name", out var tlsEl) ? tlsEl.GetString() ?? "" : "";
        var coverHost = server.TryGetProperty("cover_host", out var coverEl) ? coverEl.GetString() ?? "" : tlsName;
        var credentialId = credentials.GetProperty("credential_id").GetString() ?? "";
        var credentialKey = credentials.GetProperty("credential_key").GetString() ?? "";

        var tunnel = root.TryGetProperty("tunnel", out var tunnelEl) ? tunnelEl : default;
        var clientIp = tunnel.ValueKind == JsonValueKind.Object && tunnel.TryGetProperty("client_ip", out var cip) ? cip.GetString() ?? "10.66.0.2" : "10.66.0.2";
        var serverIp = tunnel.ValueKind == JsonValueKind.Object && tunnel.TryGetProperty("server_ip", out var sip) ? sip.GetString() ?? "10.66.0.1" : "10.66.0.1";
        var cidr = tunnel.ValueKind == JsonValueKind.Object && tunnel.TryGetProperty("cidr", out var cidrEl) ? cidrEl.GetString() ?? "10.66.0.0/24" : "10.66.0.0/24";
        var dns = new List<string> { "1.1.1.1", "8.8.8.8" };
        if (tunnel.ValueKind == JsonValueKind.Object && tunnel.TryGetProperty("dns", out var dnsEl) && dnsEl.ValueKind == JsonValueKind.Array)
            dns = dnsEl.EnumerateArray().Select(x => x.GetString()).Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x!).ToList();

        var profile = new NvpConfig
        {
            Schema = "nvp-profile-v1",
            ProfileId = profileId,
            CredentialId = credentialId,
            CredentialKey = credentialKey,
            Servers = [new NvpServerEntry { Address = host, Port = port, TlsName = tlsName, CoverHost = coverHost }],
            TransportProfile = root.TryGetProperty("transport_profile", out var transportEl) ? transportEl.GetString() ?? "tls_http_cover_v1" : "tls_http_cover_v1",
            CoverProfiles = [new NvpCoverProfile { Name = "tls_http_cover_v1", Mode = "tls_http_cover_v1", Compliance = "KRot-T", BrowserGrade = false }],
            Tls = new NvpTlsConfig { Enabled = true },
            Tunnel = new NvpTunnelConfig
            {
                InterfaceName = "KRot",
                LinuxInterfaceName = "krot0",
                ClientIp = clientIp,
                ServerIp = serverIp,
                Cidr = cidr,
                Dns = dns
            },
            ListenPort = port
        };

        var dir = Path.Combine(NoraAppState.DataRoot, "profiles", Sanitize(profileId));
        Directory.CreateDirectory(dir);
        var profilePath = Path.Combine(dir, "client-profile.json");
        File.WriteAllText(profilePath, JsonSerializer.Serialize(profile, NvpConfig.JsonOptions()));
        File.WriteAllText(Path.Combine(dir, "connection.key"), key);
        NoraAppState.SaveActiveProfilePath(profilePath);
        TryCopyPortableProfile(profilePath);
        log("Imported KRot key for " + host + ":" + port);

        return new NoraImportResult
        {
            Protocol = "KRot",
            ClientProfilePath = profilePath,
            Message = "KRot server imported and selected."
        };
    }

    private static async Task<NoraImportResult> StoreExternalProfileAsync(string protocol, string text, string extension)
    {
        var dir = Path.Combine(NoraAppState.DataRoot, "external-profiles");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, protocol.ToLowerInvariant().Replace(" ", "-", StringComparison.Ordinal) + "-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss") + extension);
        File.WriteAllText(path, text);
        if (NoraSubscriptionStore.TryParseExternalProfile(text, path, protocol, out var info))
        {
            info = await NoraSubscriptionStore.EnrichExternalProfileAsync(info);
            NoraSubscriptionStore.WriteExternalProfileInfo(path, info);
        }
        return new NoraImportResult
        {
            Protocol = protocol,
            ClientProfilePath = path,
            Message = protocol + " profile imported and selected."
        };
    }

    private static string NormalizeAwgConfig(string text)
    {
        if (text.Contains("[Interface]", StringComparison.OrdinalIgnoreCase) &&
            text.Contains("[Peer]", StringComparison.OrdinalIgnoreCase))
            return text;
        var marker = text.IndexOf("://", StringComparison.Ordinal);
        if (marker < 0)
            throw new InvalidOperationException("AWG key must contain complete [Interface] and [Peer] sections.");
        var payload = Uri.UnescapeDataString(text[(marker + 3)..].Trim());
        try
        {
            payload = payload.Replace('-', '+').Replace('_', '/');
            payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');
            var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(payload));
            if (decoded.Contains("[Interface]", StringComparison.OrdinalIgnoreCase) &&
                decoded.Contains("[Peer]", StringComparison.OrdinalIgnoreCase))
                return decoded;
        }
        catch
        {
        }
        throw new InvalidOperationException("The AWG URI does not contain a supported base64 [Interface]/[Peer] configuration.");
    }

    private static string DecodeNoraPayload(string key)
    {
        var payload = key["nora1.".Length..].Trim().Replace('-', '+').Replace('_', '/');
        payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');
        return Encoding.UTF8.GetString(Convert.FromBase64String(payload));
    }

    private static void TryCopyPortableProfile(string clientProfilePath)
    {
        try { File.Copy(clientProfilePath, Path.Combine(AppContext.BaseDirectory, "client-profile.json"), overwrite: true); }
        catch { }
    }

    private static string Sanitize(string value)
    {
        var chars = value.ToLowerInvariant().Select(ch => char.IsLetterOrDigit(ch) ? ch : '-').ToArray();
        var clean = new string(chars).Trim('-');
        return string.IsNullOrWhiteSpace(clean) ? "profile" : clean;
    }
}

internal sealed class NoraAddDialog : Form
{
    private readonly TextBox _key = new();
    public NoraAddMode Mode { get; private set; } = NoraAddMode.ImportKey;
    public string KeyText => _key.Text;

    public NoraAddDialog()
    {
        Text = "Add server";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(620, 520);
        BackColor = NoraTheme.Background;
        Font = NoraTheme.Body;

        Controls.Add(new Label
        {
            Text = "Add connection",
            Left = 34,
            Top = 28,
            Width = 360,
            Height = 38,
            Font = NoraTheme.Title,
            ForeColor = NoraTheme.TextPrimary
        });
        Controls.Add(new Label
        {
            Text = "Paste a KRot nora1 key, VLESS Reality URI, or AWG/WireGuard config.",
            Left = 36,
            Top = 72,
            Width = 540,
            Height = 28,
            ForeColor = NoraTheme.TextSecondary
        });

        var card = new NoraCard
        {
            Left = 32,
            Top = 112,
            Width = 556,
            Height = 250,
            Radius = 18,
            FillColor = NoraTheme.Surface,
            BorderColor = NoraTheme.Border
        };
        _key.Multiline = true;
        _key.BorderStyle = BorderStyle.None;
        _key.SetBounds(20, 20, 516, 210);
        _key.BackColor = NoraTheme.Surface;
        _key.ForeColor = NoraTheme.TextPrimary;
        _key.Font = new Font("Cascadia Mono", 9f);
        card.Controls.Add(_key);
        Controls.Add(card);

        var install = new NoraButton { Text = "Install KRot on VPS" };
        install.SetBounds(32, 388, 190, 46);
        StyleOutline(install);
        install.Click += (_, _) => { Mode = NoraAddMode.InstallKRot; DialogResult = DialogResult.OK; Close(); };
        Controls.Add(install);

        var import = new NoraButton { Text = "Import key" };
        import.SetBounds(340, 388, 120, 46);
        StylePrimary(import);
        import.Click += (_, _) => { Mode = NoraAddMode.ImportKey; DialogResult = DialogResult.OK; Close(); };
        Controls.Add(import);

        var cancel = new NoraButton { Text = "Cancel" };
        cancel.SetBounds(470, 388, 118, 46);
        StyleSecondary(cancel);
        cancel.Click += (_, _) => Close();
        Controls.Add(cancel);
    }

    private static void StylePrimary(NoraButton button)
    {
        button.NormalColor = NoraTheme.Accent;
        button.HoverColor = NoraTheme.AccentHover;
        button.PressedColor = NoraTheme.AccentPressed;
        button.ForeColor = Color.White;
    }

    private static void StyleOutline(NoraButton button)
    {
        button.NormalColor = Color.White;
        button.HoverColor = Color.FromArgb(244, 248, 255);
        button.PressedColor = Color.FromArgb(234, 242, 255);
        button.BorderColor = NoraTheme.Border;
        button.ForeColor = NoraTheme.Accent;
    }

    private static void StyleSecondary(NoraButton button)
    {
        button.NormalColor = NoraTheme.Neutral;
        button.HoverColor = NoraTheme.NeutralHover;
        button.PressedColor = NoraTheme.NeutralPressed;
        button.BorderColor = NoraTheme.Border;
    }
}

internal sealed class NoraServersDialog : Form
{
    private readonly ListBox _profiles = new();
    private readonly List<(string Label, string Path)> _items = [];
    public string SelectedProfilePath { get; private set; } = "";

    public NoraServersDialog()
    {
        Text = "Servers";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(620, 520);
        BackColor = NoraTheme.Background;
        Font = NoraTheme.Body;

        Controls.Add(new Label
        {
            Text = "Servers",
            Left = 34,
            Top = 28,
            Width = 360,
            Height = 38,
            Font = NoraTheme.Title,
            ForeColor = NoraTheme.TextPrimary
        });

        _profiles.SetBounds(32, 86, 556, 330);
        _profiles.BorderStyle = BorderStyle.None;
        _profiles.Font = new Font("Segoe UI", 11f);
        _profiles.BackColor = NoraTheme.Surface;
        _profiles.ForeColor = NoraTheme.TextPrimary;
        Controls.Add(_profiles);

        var open = new NoraButton { Text = "Use selected" };
        open.SetBounds(410, 440, 178, 46);
        StylePrimary(open);
        open.Click += (_, _) =>
        {
            if (_profiles.SelectedIndex < 0)
                return;
            SelectedProfilePath = _items[_profiles.SelectedIndex].Path;
            DialogResult = DialogResult.OK;
            Close();
        };
        Controls.Add(open);

        LoadItems();
    }

    private void LoadItems()
    {
        foreach (var path in Directory.Exists(Path.Combine(NoraAppState.DataRoot, "profiles"))
                     ? Directory.GetFiles(Path.Combine(NoraAppState.DataRoot, "profiles"), "client-profile.json", SearchOption.AllDirectories)
                     : [])
        {
            try
            {
                var cfg = NvpConfig.Load(path);
                var server = cfg.Servers.FirstOrDefault();
                var label = server is null ? Path.GetFileName(Path.GetDirectoryName(path)) ?? path : $"KRot  {server.Address}:{server.Port}";
                _items.Add((label, path));
                _profiles.Items.Add(label);
            }
            catch
            {
            }
        }

        var portable = Path.Combine(AppContext.BaseDirectory, "client-profile.json");
        if (File.Exists(portable) && _items.All(x => !string.Equals(x.Path, portable, StringComparison.OrdinalIgnoreCase)))
        {
            _items.Add(("KRot  portable profile", portable));
            _profiles.Items.Add("KRot  portable profile");
        }
    }

    private static void StylePrimary(NoraButton button)
    {
        button.NormalColor = NoraTheme.Accent;
        button.HoverColor = NoraTheme.AccentHover;
        button.PressedColor = NoraTheme.AccentPressed;
        button.ForeColor = Color.White;
    }
}

internal sealed class NoraLogDialog : Form
{
    public NoraLogDialog(string logs)
    {
        Text = "Settings";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(720, 560);
        BackColor = NoraTheme.Background;
        Font = NoraTheme.Body;

        Controls.Add(new Label
        {
            Text = "Logs",
            Left = 34,
            Top = 28,
            Width = 360,
            Height = 38,
            Font = NoraTheme.Title,
            ForeColor = NoraTheme.TextPrimary
        });

        var box = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            BorderStyle = BorderStyle.None,
            BackColor = NoraTheme.Surface,
            ForeColor = NoraTheme.TextSecondary,
            Font = new Font("Cascadia Mono", 9f),
            Text = logs
        };
        box.SetBounds(34, 82, 652, 430);
        Controls.Add(box);
    }
}

internal sealed class NoraUsersDialog : Form
{
    private readonly string _clientProfilePath;
    private readonly NvpConfig _clientConfig;
    private readonly Action<string> _log;
    private readonly TextBox _name = new() { Text = "user" };
    private readonly TextBox _sshHost = new();
    private readonly TextBox _sshUser = new() { Text = "root" };
    private readonly TextBox _sshPassword = new() { UseSystemPasswordChar = true };
    private readonly ListBox _users = new();
    private readonly TextBox _key = new();

    public NoraUsersDialog(string clientProfilePath, NvpConfig clientConfig, Action<string> log)
    {
        _clientProfilePath = clientProfilePath;
        _clientConfig = clientConfig;
        _log = log;
        var server = clientConfig.Servers.FirstOrDefault();
        if (server is not null)
            _sshHost.Text = server.Address;

        Text = "KRot users";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(760, 660);
        BackColor = NoraTheme.Background;
        Font = NoraTheme.Body;

        Controls.Add(new Label { Text = "KRot users", Left = 34, Top = 24, Width = 360, Height = 38, Font = NoraTheme.Title, ForeColor = NoraTheme.TextPrimary });
        AddLabeled("User name", _name, 36, 84);
        AddLabeled("SSH host", _sshHost, 36, 140);
        AddLabeled("SSH user", _sshUser, 36, 196);
        AddLabeled("SSH password", _sshPassword, 36, 252);

        var create = new NoraButton { Text = "Create user" };
        create.SetBounds(414, 252, 150, 42);
        StylePrimary(create);
        create.Click += async (_, _) => await CreateUserAsync();
        Controls.Add(create);

        var delete = new NoraButton { Text = "Delete selected" };
        delete.SetBounds(574, 252, 150, 42);
        StyleDanger(delete);
        delete.Click += async (_, _) => await DeleteUserAsync();
        Controls.Add(delete);

        Controls.Add(new Label { Text = "Active users", Left = 36, Top = 322, Width = 220, Height = 26, Font = NoraTheme.Section, ForeColor = NoraTheme.TextPrimary });
        _users.SetBounds(36, 354, 320, 220);
        _users.BorderStyle = BorderStyle.None;
        _users.BackColor = NoraTheme.Surface;
        _users.ForeColor = NoraTheme.TextPrimary;
        Controls.Add(_users);

        Controls.Add(new Label { Text = "Generated key", Left = 386, Top = 322, Width = 220, Height = 26, Font = NoraTheme.Section, ForeColor = NoraTheme.TextPrimary });
        _key.Multiline = true;
        _key.ReadOnly = true;
        _key.BorderStyle = BorderStyle.None;
        _key.SetBounds(386, 354, 338, 170);
        _key.BackColor = NoraTheme.Surface;
        _key.ForeColor = NoraTheme.TextPrimary;
        _key.Font = new Font("Cascadia Mono", 8.5f);
        Controls.Add(_key);

        var copy = new NoraButton { Text = "Copy key" };
        copy.SetBounds(574, 532, 150, 42);
        StyleOutline(copy);
        copy.Click += (_, _) => { if (!string.IsNullOrWhiteSpace(_key.Text)) Clipboard.SetText(_key.Text); };
        Controls.Add(copy);

        RefreshUsers();
    }

    private void AddLabeled(string label, TextBox box, int x, int y)
    {
        Controls.Add(new Label { Text = label, Left = x, Top = y + 8, Width = 120, Height = 24, ForeColor = NoraTheme.TextSecondary });
        box.SetBounds(x + 136, y, 220, 34);
        box.BorderStyle = BorderStyle.FixedSingle;
        Controls.Add(box);
    }

    private async Task CreateUserAsync()
    {
        try
        {
            var result = await KRotUserAdmin.CreateUserAsync(_clientProfilePath, _clientConfig, _name.Text, _sshHost.Text, _sshUser.Text, _sshPassword.Text, _log);
            _key.Text = result;
            RefreshUsers();
        }
        catch (Exception ex)
        {
            _log("Create user failed: " + ex.Message);
            MessageBox.Show(this, ex.Message, "NORA VPN", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async Task DeleteUserAsync()
    {
        if (_users.SelectedItem is not string selected)
            return;
        var id = selected.Split(' ', 2)[0];
        try
        {
            await KRotUserAdmin.DisableUserAsync(_clientProfilePath, id, _sshHost.Text, _sshUser.Text, _sshPassword.Text, _log);
            RefreshUsers();
        }
        catch (Exception ex)
        {
            _log("Delete user failed: " + ex.Message);
            MessageBox.Show(this, ex.Message, "NORA VPN", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void RefreshUsers()
    {
        _users.Items.Clear();
        var serverProfile = KRotUserAdmin.FindServerProfile(_clientProfilePath);
        if (!File.Exists(serverProfile))
            return;
        try
        {
            var cfg = NvpConfig.Load(serverProfile);
            foreach (var credential in cfg.Credentials.Where(x => x.Enabled))
                _users.Items.Add($"{credential.Id}  {credential.Name}  last={credential.LastOnlineAt} up={credential.UplinkBytes} down={credential.DownlinkBytes}");
        }
        catch
        {
        }
    }

    private static void StylePrimary(NoraButton button)
    {
        button.NormalColor = NoraTheme.Accent;
        button.HoverColor = NoraTheme.AccentHover;
        button.PressedColor = NoraTheme.AccentPressed;
        button.ForeColor = Color.White;
    }

    private static void StyleOutline(NoraButton button)
    {
        button.NormalColor = Color.White;
        button.HoverColor = Color.FromArgb(244, 248, 255);
        button.PressedColor = Color.FromArgb(234, 242, 255);
        button.BorderColor = NoraTheme.Border;
        button.ForeColor = NoraTheme.Accent;
    }

    private static void StyleDanger(NoraButton button)
    {
        button.NormalColor = NoraTheme.Danger;
        button.HoverColor = NoraTheme.DangerHover;
        button.PressedColor = NoraTheme.DangerPressed;
        button.ForeColor = Color.White;
    }
}

internal static class KRotUserAdmin
{
    public static string FindServerProfile(string clientProfilePath)
    {
        var dir = Path.GetDirectoryName(clientProfilePath) ?? "";
        var sameDir = Path.Combine(dir, "server-profile.json");
        if (File.Exists(sameDir))
            return sameDir;
        var repoDefault = Path.Combine(Directory.GetCurrentDirectory(), "profiles", "server-profile.json");
        return repoDefault;
    }

    public static async Task<string> CreateUserAsync(string clientProfilePath, NvpConfig clientConfig, string userName, string sshHost, string sshUser, string sshPassword, Action<string> log)
    {
        if (string.IsNullOrWhiteSpace(userName))
            throw new InvalidOperationException("User name is required.");
        var serverProfilePath = FindServerProfile(clientProfilePath);
        if (!File.Exists(serverProfilePath))
            throw new InvalidOperationException("Local server-profile.json was not found for this KRot server.");
        var serverProfile = NvpConfig.Load(serverProfilePath);
        var credential = new NvpCredential
        {
            Id = "user-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(6)).ToLowerInvariant(),
            Key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
            Name = userName.Trim(),
            Enabled = true,
            CreatedAt = DateTimeOffset.UtcNow.ToString("O"),
            ClientIp = AllocateClientIp(serverProfile)
        };
        if (string.IsNullOrWhiteSpace(sshHost) || string.IsNullOrWhiteSpace(sshUser) || string.IsNullOrWhiteSpace(sshPassword))
            NvpProfileStore.AddCredential(serverProfilePath, credential);
        else
            await AddRemoteCredentialAsync(serverProfilePath, credential, sshHost, sshUser, sshPassword, log);
        var key = BuildConnectionKey(clientConfig, credential);
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(serverProfilePath) ?? NoraAppState.DataRoot, credential.Id + ".connection.key"), key);
        log("Created KRot user " + credential.Name + " (" + credential.Id + ")");
        return key;
    }

    public static async Task DisableUserAsync(string clientProfilePath, string credentialId, string sshHost, string sshUser, string sshPassword, Action<string> log)
    {
        var serverProfilePath = FindServerProfile(clientProfilePath);
        if (!File.Exists(serverProfilePath))
            throw new InvalidOperationException("Local server-profile.json was not found for this KRot server.");
        if (string.IsNullOrWhiteSpace(sshHost) || string.IsNullOrWhiteSpace(sshUser) || string.IsNullOrWhiteSpace(sshPassword))
            NvpProfileStore.DisableCredential(serverProfilePath, credentialId);
        else
            await DisableRemoteCredentialAsync(serverProfilePath, credentialId, sshHost, sshUser, sshPassword, log);
        log("Disabled KRot user " + credentialId);
    }

    public static string ExportConnectionKey(string clientProfilePath, string credentialId)
    {
        var serverProfilePath = FindServerProfile(clientProfilePath);
        if (!File.Exists(serverProfilePath))
            throw new InvalidOperationException("Local server-profile.json was not found for this KRot server.");
        var keyPath = Path.Combine(Path.GetDirectoryName(serverProfilePath) ?? NoraAppState.DataRoot, credentialId + ".connection.key");
        if (File.Exists(keyPath))
            return File.ReadAllText(keyPath).Trim();

        var serverProfile = NvpConfig.Load(serverProfilePath);
        var credential = serverProfile.Credentials.FirstOrDefault(x => string.Equals(x.Id, credentialId, StringComparison.Ordinal) && x.Enabled);
        if (credential is null)
            throw new InvalidOperationException("Credential key was not found locally.");
        var clientConfig = NvpConfig.Load(clientProfilePath);
        var key = BuildConnectionKey(clientConfig, credential);
        File.WriteAllText(keyPath, key);
        return key;
    }

    public static async Task DownloadServerProfileAsync(string serverProfilePath, string sshHost, string sshUser, string sshPassword, Action<string> log)
    {
        if (string.IsNullOrWhiteSpace(serverProfilePath) ||
            string.IsNullOrWhiteSpace(sshHost) ||
            string.IsNullOrWhiteSpace(sshUser) ||
            string.IsNullOrWhiteSpace(sshPassword))
            return;

        var pscp = FindTool("pscp.exe");
        if (pscp.Length == 0)
            throw new InvalidOperationException("PuTTY pscp.exe was not found.");
        var target = $"{sshUser}@{sshHost}";
        await ExternalToolRunner.RunAsync(pscp, ["-batch", "-pw", sshPassword, $"{target}:/opt/nvp/server-profile.json", serverProfilePath], log);
        log("Downloaded fresh KRot server stats from " + sshHost);
    }

    public static async Task UninstallServerAsync(string sshHost, string sshUser, string sshPassword, int listenPort, Action<string> log)
    {
        if (string.IsNullOrWhiteSpace(sshHost) || string.IsNullOrWhiteSpace(sshUser) || string.IsNullOrWhiteSpace(sshPassword))
            throw new NoraAppException("NORA-VPS-5005", "Saved SSH credentials are required to uninstall KRot from a self-hosted VPS.");
        var plink = FindTool("plink.exe");
        if (plink.Length == 0)
            throw new InvalidOperationException("PuTTY plink.exe was not found.");

        var port = Math.Clamp(listenPort, 1, 65535);
        var target = $"{sshUser}@{sshHost}";
        log("Stopping KRot service on " + sshHost);
        var command = string.Join("; ", new[]
        {
            "systemctl stop nvp.service >/dev/null 2>&1 || true",
            "systemctl disable nvp.service >/dev/null 2>&1 || true",
            "rm -f /etc/systemd/system/nvp.service /tmp/nvp.service.new",
            "systemctl daemon-reload >/dev/null 2>&1 || true",
            "rm -rf /opt/nvp",
            $"ufw delete allow {port}/tcp >/dev/null 2>&1 || true",
            "ip link delete krot0 >/dev/null 2>&1 || true",
            "test ! -e /etc/systemd/system/nvp.service",
            "test ! -d /opt/nvp",
            "echo KRot_uninstalled"
        });
        await ExternalToolRunner.RunAsync(plink, ["-ssh", "-batch", "-pw", sshPassword, target, command], log);
        log("KRot uninstall completed on " + sshHost);
    }

    private static string BuildConnectionKey(NvpConfig clientConfig, NvpCredential credential)
    {
        var server = clientConfig.Servers.First();
        var clientIp = string.IsNullOrWhiteSpace(credential.ClientIp) ? clientConfig.Tunnel.ClientIp : credential.ClientIp.Trim();
        var payload = new
        {
            schema = "nora-connection-key-v1",
            profile_id = clientConfig.ProfileId + "-" + credential.Id,
            transport_profile = clientConfig.TransportProfile,
            server = new
            {
                host = server.Address,
                port = server.Port,
                tls_name = server.TlsName,
                cover_host = server.CoverHost
            },
            credentials = new
            {
                credential_id = credential.Id,
                credential_key = credential.Key
            },
            tunnel = new
            {
                client_ip = clientIp,
                server_ip = clientConfig.Tunnel.ServerIp,
                cidr = clientConfig.Tunnel.Cidr,
                dns = clientConfig.Tunnel.Dns
            }
        };
        var json = JsonSerializer.SerializeToUtf8Bytes(payload);
        return "nora1." + Convert.ToBase64String(json).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static string AllocateClientIp(NvpConfig serverProfile)
    {
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            serverProfile.Tunnel.ServerIp,
            serverProfile.Tunnel.ClientIp
        };
        foreach (var credential in serverProfile.Credentials)
        {
            if (!string.IsNullOrWhiteSpace(credential.ClientIp))
                used.Add(credential.ClientIp.Trim());
        }

        var cidr = serverProfile.Tunnel.Cidr;
        var slash = cidr.IndexOf('/');
        var baseIp = slash > 0 ? cidr[..slash] : "10.66.0.0";
        var octets = baseIp.Split('.');
        var prefix = octets.Length == 4 ? $"{octets[0]}.{octets[1]}.{octets[2]}." : "10.66.0.";
        for (var last = 10; last <= 250; last++)
        {
            var candidate = prefix + last;
            if (!used.Contains(candidate))
                return candidate;
        }

        throw new InvalidOperationException("No free KRot client IP addresses remain in " + serverProfile.Tunnel.Cidr);
    }

    private static async Task UploadServerProfileAsync(string serverProfilePath, string sshHost, string sshUser, string sshPassword, Action<string> log)
    {
        if (string.IsNullOrWhiteSpace(sshHost) || string.IsNullOrWhiteSpace(sshUser) || string.IsNullOrWhiteSpace(sshPassword))
        {
            log("Server-profile updated locally. SSH fields were empty, remote KRot service was not updated.");
            return;
        }

        var plink = FindTool("plink.exe");
        var pscp = FindTool("pscp.exe");
        if (plink.Length == 0 || pscp.Length == 0)
            throw new InvalidOperationException("PuTTY plink.exe/pscp.exe were not found.");
        var target = $"{sshUser}@{sshHost}";
        await ExternalToolRunner.RunAsync(pscp, ["-batch", "-pw", sshPassword, serverProfilePath, $"{target}:/opt/nvp/server-profile.json.new"], log);
        await ExternalToolRunner.RunAsync(plink, ["-ssh", "-batch", "-pw", sshPassword, target, "mv /opt/nvp/server-profile.json.new /opt/nvp/server-profile.json && test -s /opt/nvp/server-profile.json && systemctl is-active nvp.service"], log);
        log("KRot profile updated without restarting active tunnel sessions.");
    }

    private static async Task AddRemoteCredentialAsync(string serverProfilePath, NvpCredential credential, string sshHost, string sshUser, string sshPassword, Action<string> log)
    {
        var plink = FindTool("plink.exe");
        var pscp = FindTool("pscp.exe");
        if (plink.Length == 0 || pscp.Length == 0)
            throw new InvalidOperationException("PuTTY plink.exe/pscp.exe were not found.");
        var localMutation = Path.Combine(Path.GetTempPath(), "nora-user-" + Guid.NewGuid().ToString("N") + ".json");
        var remoteMutation = "/opt/nvp/.nora-user-" + Guid.NewGuid().ToString("N") + ".json";
        var target = $"{sshUser}@{sshHost}";
        try
        {
            File.WriteAllText(localMutation, JsonSerializer.Serialize(credential, NvpConfig.JsonOptions()));
            await ExternalToolRunner.RunAsync(pscp, ["-batch", "-pw", sshPassword, localMutation, $"{target}:{remoteMutation}"], log);
            await ExternalToolRunner.RunAsync(plink, ["-ssh", "-batch", "-pw", sshPassword, target,
                $"chmod 600 {remoteMutation} && /opt/nvp/nvp user-add /opt/nvp/server-profile.json {remoteMutation}"], log);
            await DownloadServerProfileAsync(serverProfilePath, sshHost, sshUser, sshPassword, log);
            var verified = NvpConfig.Load(serverProfilePath).Credentials.Any(x =>
                x.Enabled && string.Equals(x.Id, credential.Id, StringComparison.Ordinal) && string.Equals(x.Key, credential.Key, StringComparison.Ordinal));
            if (!verified)
                throw new InvalidOperationException("The VPS did not retain the new KRot credential.");
            log("Verified the new KRot credential on the VPS.");
        }
        finally
        {
            if (File.Exists(localMutation))
                File.Delete(localMutation);
        }
    }

    private static async Task DisableRemoteCredentialAsync(string serverProfilePath, string credentialId, string sshHost, string sshUser, string sshPassword, Action<string> log)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(credentialId, "^[A-Za-z0-9._-]{1,128}$"))
            throw new InvalidOperationException("Credential id contains unsupported characters.");
        var plink = FindTool("plink.exe");
        if (plink.Length == 0)
            throw new InvalidOperationException("PuTTY plink.exe was not found.");
        var target = $"{sshUser}@{sshHost}";
        await ExternalToolRunner.RunAsync(plink, ["-ssh", "-batch", "-pw", sshPassword, target,
            $"/opt/nvp/nvp user-disable /opt/nvp/server-profile.json {credentialId}"], log);
        await DownloadServerProfileAsync(serverProfilePath, sshHost, sshUser, sshPassword, log);
        var verified = NvpConfig.Load(serverProfilePath).Credentials.Any(x =>
            string.Equals(x.Id, credentialId, StringComparison.Ordinal) && !x.Enabled);
        if (!verified)
            throw new InvalidOperationException("The VPS did not retain the disabled KRot credential.");
    }

    private static string FindTool(string name)
    {
        foreach (var path in new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "PuTTY", name),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "PuTTY", name),
            name
        })
        {
            if (path == name || File.Exists(path))
                return path;
        }
        return "";
    }
}

internal sealed class NoraServerSetupDialog : Form
{
    private readonly TextBox _displayName = new() { Text = "My VPS" };
    private readonly TextBox _host = new();
    private readonly NumericUpDown _port = new() { Minimum = 1, Maximum = 65535, Value = 443 };
    private readonly TextBox _sshUser = new() { Text = "root" };
    private readonly TextBox _sshPassword = new() { UseSystemPasswordChar = true };
    private readonly TextBox _hostKey = new();
    private readonly TextBox _tlsName = new();
    private readonly TextBox _coverHost = new();
    private readonly CheckBox _showAdvanced = new() { Text = "Advanced options", AutoSize = true };
    private readonly CheckBox _provision = new() { Text = "Provision KRot core on this VPS", Checked = true, AutoSize = true };
    private readonly ToolTip _tips = new();
    private readonly Panel _advancedPanel = new();

    public NoraServerSettings Settings { get; private set; } = new();

    public NoraServerSetupDialog()
    {
        Text = "Add NORA VPN server";
        StartPosition = FormStartPosition.CenterParent;
        AutoScaleMode = AutoScaleMode.Dpi;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(620, 640);
        BackColor = NoraTheme.Background;
        Font = new Font("Segoe UI", 10f);
        DoubleBuffered = true;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(18),
            BackColor = BackColor
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 104));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));
        Controls.Add(root);

        var header = new NoraCard
        {
            Dock = DockStyle.Fill,
            FillColor = NoraTheme.Surface,
            BorderColor = NoraTheme.Border,
            Radius = 22,
            Padding = new Padding(20, 14, 20, 12),
            Margin = new Padding(0, 0, 0, 12)
        };
        var headerStack = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = Color.Transparent
        };
        headerStack.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        headerStack.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        headerStack.Controls.Add(new Label
        {
            Text = "Add server",
            Dock = DockStyle.Fill,
            Font = NoraTheme.Title,
            ForeColor = NoraTheme.TextPrimary,
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, 0);
        headerStack.Controls.Add(new Label
        {
            Text = "Required: VPS address, SSH user, SSH password. Advanced options are optional.",
            Dock = DockStyle.Fill,
            Font = NoraTheme.BodySmall,
            ForeColor = NoraTheme.TextSecondary,
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, 1);
        header.Controls.Add(headerStack);
        root.Controls.Add(header, 0, 0);
        root.Controls.Add(BuildForm(), 0, 1);
        root.Controls.Add(BuildActions(), 0, 2);

        _host.TextChanged += (_, _) => SyncHostDefaults();
        _showAdvanced.CheckedChanged += (_, _) => ToggleAdvanced();
        _showAdvanced.Checked = false;
        ToggleAdvanced();
    }

    private Control BuildForm()
    {
        var card = new NoraCard
        {
            Dock = DockStyle.Fill,
            FillColor = NoraTheme.Surface,
            BorderColor = NoraTheme.Border,
            Radius = 22,
            Padding = new Padding(20),
            Margin = new Padding(0, 0, 0, 12)
        };
        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 8,
            BackColor = Color.Transparent
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (var i = 0; i < 6; i++)
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 126));
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        card.Controls.Add(grid);

        AddRow(grid, 0, "Name", _displayName, "Only the label shown in the app.");
        AddRow(grid, 1, "Server IP", _host, "Public IP or hostname of your VPS.");
        AddRow(grid, 2, "Port", _port, "Usually 443.");
        AddRow(grid, 3, "SSH user", _sshUser, "The login used to reach the VPS, often root.");
        AddRow(grid, 4, "SSH password", _sshPassword, "Used once to upload and start the core.");

        grid.Controls.Add(_showAdvanced, 1, 5);
        _showAdvanced.Margin = new Padding(0, 8, 0, 0);
        _showAdvanced.ForeColor = NoraTheme.TextPrimary;
        _showAdvanced.BackColor = Color.Transparent;
        _showAdvanced.FlatStyle = FlatStyle.Flat;

        _advancedPanel.Controls.Clear();
        _advancedPanel.Dock = DockStyle.Fill;
        _advancedPanel.BackColor = Color.Transparent;
        _advancedPanel.Margin = new Padding(0, 8, 0, 0);
        _advancedPanel.Padding = new Padding(0);

        var advanced = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 3,
            BackColor = Color.Transparent,
            Padding = new Padding(14, 10, 14, 8),
            Margin = new Padding(0)
        };
        advanced.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        advanced.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        advanced.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        advanced.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        advanced.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));

        AddRow(advanced, 0, "SSH host key", _hostKey, "Optional fingerprint pin, e.g. ssh-ed25519 SHA256:...");
        AddRow(advanced, 1, "TLS name", _tlsName, "Domain for the certificate and SNI; blank uses ip.sslip.io.");
        AddRow(advanced, 2, "Cover host", _coverHost, "Visible host for the outer cover; usually the same as TLS name.");
        var advancedCard = new NoraCard
        {
            Dock = DockStyle.Fill,
            FillColor = NoraTheme.SurfaceAlt,
            BorderColor = NoraTheme.BorderSoft,
            Radius = 18,
            Padding = new Padding(0),
            Margin = new Padding(0)
        };
        advancedCard.Controls.Add(advanced);
        _advancedPanel.Controls.Add(advancedCard);
        grid.Controls.Add(_advancedPanel, 0, 6);
        grid.SetColumnSpan(_advancedPanel, 2);

        grid.Controls.Add(_provision, 1, 7);
        _provision.Margin = new Padding(0, 10, 0, 0);
        _provision.ForeColor = NoraTheme.TextPrimary;
        _provision.BackColor = Color.Transparent;
        _provision.FlatStyle = FlatStyle.Flat;

        return card;
    }

    private Control BuildActions()
    {
        var panel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(0, 10, 0, 0),
            BackColor = BackColor
        };

        var save = new NoraButton { Text = "Provision", Width = 132, Height = 42 };
        StylePrimaryButton(save, success: false);
        save.Click += (_, _) =>
        {
            Settings = new NoraServerSettings
            {
                DisplayName = string.IsNullOrWhiteSpace(_displayName.Text) ? _host.Text.Trim() : _displayName.Text.Trim(),
                Host = _host.Text.Trim(),
                Port = (int)_port.Value,
                SshUser = _sshUser.Text.Trim(),
                SshPassword = _sshPassword.Text,
                SshHostKey = _hostKey.Text.Trim(),
                TlsName = _tlsName.Text.Trim(),
                CoverHost = _coverHost.Text.Trim(),
                ProvisionRemote = _provision.Checked
            };
            DialogResult = DialogResult.OK;
            Close();
        };

        var cancel = new NoraButton { Text = "Cancel", Width = 112, Height = 42 };
        StyleSecondaryButton(cancel);
        cancel.Click += (_, _) =>
        {
            DialogResult = DialogResult.Cancel;
            Close();
        };

        panel.Controls.Add(save);
        panel.Controls.Add(cancel);
        return panel;
    }

    private void ToggleAdvanced()
    {
        _advancedPanel.Visible = _showAdvanced.Checked;
        if (_advancedPanel.Parent is TableLayoutPanel grid && grid.RowStyles.Count > 6)
            grid.RowStyles[6].Height = _showAdvanced.Checked ? 126 : 0;
    }

    private void SyncHostDefaults()
    {
        var host = _host.Text.Trim();
        if (host.Length == 0)
            return;
        var defaultName = $"{host}.sslip.io";
        if (string.IsNullOrWhiteSpace(_tlsName.Text))
            _tlsName.Text = defaultName;
        if (string.IsNullOrWhiteSpace(_coverHost.Text))
            _coverHost.Text = defaultName;
    }

    private void AddRow(TableLayoutPanel grid, int row, string label, Control control, string hint)
    {
        control.Dock = DockStyle.Fill;
        control.Margin = new Padding(0, 4, 0, 4);
        if (control is TextBox tb)
        {
            tb.BackColor = NoraTheme.Background;
            tb.ForeColor = NoraTheme.TextPrimary;
            tb.BorderStyle = BorderStyle.FixedSingle;
            tb.Font = NoraTheme.Body;
        }
        if (control is NumericUpDown nud)
        {
            nud.BackColor = NoraTheme.Background;
            nud.ForeColor = NoraTheme.TextPrimary;
            nud.Font = NoraTheme.Body;
        }
        grid.Controls.Add(new Label
        {
            Text = label,
            Dock = DockStyle.Fill,
            Font = NoraTheme.BodySmall,
            ForeColor = NoraTheme.TextSecondary,
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, row);
        grid.Controls.Add(control, 1, row);
        _tips.SetToolTip(control, hint);
    }

    private static void StyleButtonCore(Button button, Color normal, Color hover, Color pressed, Color foreColor, float fontSize, int borderSize)
    {
        if (button is NoraButton nora)
        {
            nora.NormalColor = normal;
            nora.HoverColor = hover;
            nora.PressedColor = pressed;
            nora.BorderColor = borderSize > 0 ? NoraTheme.Border : Color.Transparent;
            nora.ForeColor = foreColor;
            nora.Font = new Font("Segoe UI Semibold", fontSize);
            nora.Cursor = Cursors.Hand;
            nora.Margin = new Padding(0, 0, 10, 0);
            nora.Height = Math.Max(nora.Height, 38);
            nora.Invalidate();
            return;
        }

        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = borderSize;
        button.FlatAppearance.BorderColor = NoraTheme.Border;
        button.FlatAppearance.MouseOverBackColor = hover;
        button.FlatAppearance.MouseDownBackColor = pressed;
        button.UseVisualStyleBackColor = false;
        button.BackColor = normal;
        button.ForeColor = foreColor;
        button.Font = new Font("Segoe UI Semibold", fontSize);
        button.Cursor = Cursors.Hand;
        button.Margin = new Padding(0, 0, 10, 0);
    }

    private static void StylePrimaryButton(Button button, bool success = false)
    {
        var normal = success ? NoraTheme.Success : NoraTheme.Accent;
        var hover = success ? NoraTheme.SuccessHover : NoraTheme.AccentHover;
        var pressed = success ? NoraTheme.SuccessPressed : NoraTheme.AccentPressed;
        StyleButtonCore(button, normal, hover, pressed, Color.White, 10f, 0);
    }

    private static void StyleSecondaryButton(Button button)
    {
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderColor = NoraTheme.Border;
        button.FlatAppearance.BorderSize = 1;
        button.BackColor = NoraTheme.SurfaceAlt;
        button.ForeColor = NoraTheme.TextPrimary;
        button.Font = new Font("Segoe UI Semibold", 10f);
        button.Margin = new Padding(8, 0, 0, 0);
        button.Cursor = Cursors.Hand;
        button.UseVisualStyleBackColor = false;
    }
}
