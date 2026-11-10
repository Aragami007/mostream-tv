using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Reflection;
using System.Runtime.InteropServices;

namespace MoStreamTV.Host.UI;

internal static class BrandAssets
{
    private const string IconResource = "MoStreamTV.Host.Assets.mostream.ico";
    private const string Logo32Resource = "MoStreamTV.Host.Assets.mostream-32.png";
    private const string Logo16Resource = "MoStreamTV.Host.Assets.mostream-16.png";

    private static Icon? _cachedIcon;
    private static Image? _cachedLogo32;
    private static Image? _cachedLogo16;

    public static Icon? AppIcon => _cachedIcon ??= LoadIcon(IconResource);
    public static Image? Logo32 => _cachedLogo32 ??= LoadImage(Logo32Resource);
    public static Image? Logo16 => _cachedLogo16 ??= LoadImage(Logo16Resource);

    private static Icon? LoadIcon(string resourceName)
    {
        try
        {
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName);
            return stream is null ? null : new Icon(stream);
        }
        catch
        {
            return null;
        }
    }

    private static Image? LoadImage(string resourceName)
    {
        try
        {
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName);
            if (stream is null)
                return null;

            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            memory.Position = 0;
            using var decoded = Image.FromStream(memory);
            return new Bitmap(decoded);
        }
        catch
        {
            return null;
        }
    }
}

internal sealed record ThemePalette(
    bool IsDark,
    Color WindowBack,
    Color SurfaceBack,
    Color InputBack,
    Color HeaderBack,
    Color ButtonBack,
    Color ButtonHover,
    Color SelectionBack,
    Color Border,
    Color TextPrimary,
    Color TextMuted,
    Color AccentBrand,
    Color AccentUser,
    Color ErrorText,
    Color ReadyBack,
    Color ReadyFore);

internal static class AppTheme
{
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaUseImmersiveDarkModeLegacy = 19;

    public static ThemePalette Light { get; } = new(
        IsDark: false,
        WindowBack: SystemColors.Control,
        SurfaceBack: SystemColors.Window,
        InputBack: SystemColors.Window,
        HeaderBack: SystemColors.Control,
        ButtonBack: SystemColors.Control,
        ButtonHover: Color.FromArgb(228, 232, 238),
        SelectionBack: SystemColors.Highlight,
        Border: Color.FromArgb(204, 208, 214),
        TextPrimary: SystemColors.ControlText,
        TextMuted: SystemColors.GrayText,
        AccentBrand: Color.FromArgb(184, 22, 46),
        AccentUser: Color.FromArgb(24, 90, 157),
        ErrorText: Color.Firebrick,
        ReadyBack: Color.FromArgb(223, 245, 228),
        ReadyFore: Color.FromArgb(20, 90, 38));

    public static ThemePalette Dark { get; } = new(
        IsDark: true,
        WindowBack: Color.FromArgb(24, 26, 30),
        SurfaceBack: Color.FromArgb(32, 35, 42),
        InputBack: Color.FromArgb(18, 20, 24),
        HeaderBack: Color.FromArgb(30, 33, 39),
        ButtonBack: Color.FromArgb(46, 51, 60),
        ButtonHover: Color.FromArgb(62, 68, 80),
        SelectionBack: Color.FromArgb(56, 86, 128),
        Border: Color.FromArgb(64, 70, 82),
        TextPrimary: Color.FromArgb(236, 240, 245),
        TextMuted: Color.FromArgb(152, 160, 174),
        AccentBrand: Color.FromArgb(224, 58, 78),
        AccentUser: Color.FromArgb(116, 179, 255),
        ErrorText: Color.FromArgb(255, 118, 128),
        ReadyBack: Color.FromArgb(34, 84, 52),
        ReadyFore: Color.FromArgb(220, 248, 228));

    private static string SettingsFilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MoStreamTV",
        "theme.txt");

    public static bool LoadDarkPreference()
    {
        try
        {
            if (File.Exists(SettingsFilePath))
                return string.Equals(File.ReadAllText(SettingsFilePath).Trim(), "dark", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
        }

        return false;
    }

    public static void SaveDarkPreference(bool isDark)
    {
        try
        {
            var directory = Path.GetDirectoryName(SettingsFilePath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);
            File.WriteAllText(SettingsFilePath, isDark ? "dark" : "light");
        }
        catch
        {
        }
    }

    public static ThemePalette GetPalette(bool isDark) => isDark ? Dark : Light;

    public static void ApplyTitleBar(Form form, bool isDark)
    {
        if (!form.IsHandleCreated)
            return;

        var enabled = isDark ? 1 : 0;
        if (DwmSetWindowAttribute(form.Handle, DwmwaUseImmersiveDarkMode, ref enabled, sizeof(int)) != 0)
            DwmSetWindowAttribute(form.Handle, DwmwaUseImmersiveDarkModeLegacy, ref enabled, sizeof(int));
    }

    public static void ApplyToForm(Form form, ThemePalette palette)
    {
        form.SuspendLayout();
        try
        {
            form.BackColor = palette.WindowBack;
            form.ForeColor = palette.TextPrimary;
            ApplyTitleBar(form, palette.IsDark);
            ApplyChildren(form, palette);
        }
        finally
        {
            form.ResumeLayout(true);
            form.Invalidate(true);
        }
    }

    public static void ApplyChildren(Control parent, ThemePalette palette)
    {
        foreach (Control control in parent.Controls)
        {
            ApplySingleControl(control, palette);
            if (control.HasChildren)
                ApplyChildren(control, palette);
        }
    }

    private static void ApplySingleControl(Control control, ThemePalette palette)
    {
        switch (control)
        {
            case ThemeToggleButton toggle:
                toggle.ApplyTheme(palette);
                return;

            case ThemedGroupBox group:
                group.BackColor = palette.WindowBack;
                group.ForeColor = palette.TextPrimary;
                group.BorderColor = palette.Border;
                return;

            case GroupBox group:
                group.BackColor = palette.WindowBack;
                group.ForeColor = palette.TextPrimary;
                return;

            case TextBox textBox:
                textBox.BackColor = textBox.ReadOnly ? palette.SurfaceBack : palette.InputBack;
                textBox.ForeColor = textBox.ReadOnly && !textBox.TabStop ? palette.TextMuted : palette.TextPrimary;
                textBox.BorderStyle = palette.IsDark ? BorderStyle.FixedSingle : BorderStyle.Fixed3D;
                return;

            case RichTextBox richText:
                richText.BackColor = palette.InputBack;
                richText.ForeColor = palette.TextPrimary;
                richText.BorderStyle = BorderStyle.FixedSingle;
                ApplyNativeDarkTheme(richText, palette.IsDark ? "DarkMode_Explorer" : null);
                return;

            case NumericUpDown numeric:
                numeric.BackColor = palette.InputBack;
                numeric.ForeColor = palette.TextPrimary;
                numeric.BorderStyle = palette.IsDark ? BorderStyle.FixedSingle : BorderStyle.Fixed3D;
                return;

            case ComboBox combo:
                combo.FlatStyle = palette.IsDark ? FlatStyle.Flat : FlatStyle.Standard;
                combo.BackColor = palette.InputBack;
                combo.ForeColor = palette.TextPrimary;
                ApplyNativeDarkTheme(combo, palette.IsDark ? "DarkMode_CFD" : null);
                return;

            case ListView listView:
                listView.BackColor = palette.InputBack;
                listView.ForeColor = palette.TextPrimary;
                listView.OwnerDraw = palette.IsDark && listView.HeaderStyle != ColumnHeaderStyle.None;
                ApplyNativeDarkTheme(listView, palette.IsDark ? "DarkMode_Explorer" : "Explorer");
                return;

            case CheckBox { Appearance: Appearance.Button } readyButton:
                StyleButton(readyButton, palette);
                return;

            case CheckBox or RadioButton:
                control.BackColor = Color.Transparent;
                control.ForeColor = palette.TextPrimary;
                return;

            case ThemedButton themedButton:
                themedButton.ApplyTheme(palette);
                return;

            case Button button:
                StyleButton(button, palette);
                return;

            case Label label:
                if (Equals(label.Tag, "muted"))
                    label.ForeColor = palette.TextMuted;
                else
                    label.ForeColor = palette.TextPrimary;
                return;
        }
    }

    public static void StyleButton(ButtonBase button, ThemePalette palette)
    {
        if (palette.IsDark)
        {
            button.FlatStyle = FlatStyle.Flat;
            button.UseVisualStyleBackColor = false;
            button.BackColor = palette.ButtonBack;
            button.ForeColor = palette.TextPrimary;
            button.FlatAppearance.BorderSize = 1;
            button.FlatAppearance.BorderColor = palette.Border;
            button.FlatAppearance.MouseOverBackColor = palette.ButtonHover;
            button.FlatAppearance.MouseDownBackColor = palette.SelectionBack;
        }
        else
        {
            button.FlatStyle = FlatStyle.Standard;
            button.UseVisualStyleBackColor = true;
            button.BackColor = SystemColors.Control;
            button.ForeColor = SystemColors.ControlText;
        }
    }

    private static void ApplyNativeDarkTheme(Control control, string? themeName)
    {
        if (control.IsHandleCreated)
            TrySetWindowTheme(control.Handle, themeName);
        else if (!Equals(control.Tag, "dark-hooked") && themeName is not null)
        {
            control.HandleCreated += (_, _) =>
            {
                if (control.FindForm()?.BackColor == Dark.WindowBack)
                    TrySetWindowTheme(control.Handle, themeName);
            };
        }
    }

    private static void TrySetWindowTheme(IntPtr handle, string? appName)
    {
        try
        {
            SetWindowTheme(handle, appName ?? string.Empty, null);
        }
        catch
        {
        }
    }

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    private static extern int SetWindowTheme(IntPtr hwnd, string pszSubAppName, string? pszSubIdList);
}

internal sealed class ThemedButton : Button
{
    private ThemePalette _palette = AppTheme.Light;

    public void ApplyTheme(ThemePalette palette)
    {
        _palette = palette;
        AppTheme.StyleButton(this, palette);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs pevent)
    {
        base.OnPaint(pevent);
        if (Enabled || !_palette.IsDark)
            return;

        // WinForms FlatStyle.Flat paints disabled button captions with low-contrast system shadow.
        var surface = Color.FromArgb(31, 34, 40);
        using var backBrush = new SolidBrush(surface);
        using var borderPen = new Pen(Color.FromArgb(50, 54, 64));
        var inner = new Rectangle(0, 0, Math.Max(1, Width - 1), Math.Max(1, Height - 1));
        pevent.Graphics.FillRectangle(backBrush, ClientRectangle);
        pevent.Graphics.DrawRectangle(borderPen, inner);
        TextRenderer.DrawText(
            pevent.Graphics,
            Text,
            Font,
            ClientRectangle,
            Color.FromArgb(118, 125, 138),
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
    }
}

internal sealed class ThemedGroupBox : GroupBox
{
    public Color BorderColor { get; set; } = SystemColors.ControlDark;

    public ThemedGroupBox()
    {
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        using var backBrush = new SolidBrush(BackColor);
        g.FillRectangle(backBrush, ClientRectangle);

        var flags = TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;
        var textSize = TextRenderer.MeasureText(g, Text, Font, new Size(int.MaxValue, int.MaxValue), flags);
        var topOffset = Math.Max(6, textSize.Height / 2);
        var borderRect = new Rectangle(0, topOffset, Math.Max(1, Width - 1), Math.Max(1, Height - topOffset - 1));

        using (var borderPen = new Pen(BorderColor))
        {
            g.DrawRectangle(borderPen, borderRect);
        }

        if (!string.IsNullOrEmpty(Text))
        {
            var textRect = new Rectangle(8, 0, Math.Min(Width - 16, textSize.Width + 4), textSize.Height);
            g.FillRectangle(backBrush, textRect);
            TextRenderer.DrawText(g, Text, Font, new Point(10, 0), ForeColor, BackColor, flags);
        }
    }
}

internal sealed class ThemeToggleButton : Control
{
    private ThemePalette _palette = AppTheme.Light;
    private bool _hovered;

    public ThemeToggleButton()
    {
        Size = new Size(30, 30);
        Cursor = Cursors.Hand;
        TabStop = true;
        AccessibleName = "Toggle theme";
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor, true);
    }

    public void ApplyTheme(ThemePalette palette)
    {
        _palette = palette;
        BackColor = Color.Transparent;
        Invalidate();
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        _hovered = true;
        Invalidate();
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hovered = false;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode is Keys.Enter or Keys.Space)
        {
            e.SuppressKeyPress = true;
            OnClick(EventArgs.Empty);
            return;
        }
        base.OnKeyDown(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        if (_hovered || Focused)
        {
            using var hoverBrush = new SolidBrush(_palette.ButtonHover);
            var pill = new Rectangle(1, 1, Width - 3, Height - 3);
            using var path = CreateRoundedRect(pill, 7);
            g.FillPath(hoverBrush, path);
        }

        var iconColor = _palette.IsDark ? _palette.TextPrimary : _palette.TextMuted;
        if (_hovered)
            iconColor = _palette.TextPrimary;

        if (_palette.IsDark)
            DrawSunIcon(g, iconColor);
        else
            DrawMoonIcon(g, iconColor);
    }

    private void DrawMoonIcon(Graphics g, Color color)
    {
        var cx = Width / 2f;
        var cy = Height / 2f;
        using var outer = new GraphicsPath();
        outer.AddEllipse(cx - 7f, cy - 7f, 14f, 14f);
        using var cut = new GraphicsPath();
        cut.AddEllipse(cx - 2.5f, cy - 8.5f, 11f, 11f);
        using var region = new Region(outer);
        region.Exclude(cut);
        using var brush = new SolidBrush(color);
        g.FillRegion(brush, region);
    }

    private void DrawSunIcon(Graphics g, Color color)
    {
        var cx = Width / 2f;
        var cy = Height / 2f;
        using var brush = new SolidBrush(color);
        using var pen = new Pen(color, 1.7f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.FillEllipse(brush, cx - 3.6f, cy - 3.6f, 7.2f, 7.2f);
        for (var i = 0; i < 8; i++)
        {
            var angle = i * (Math.PI / 4.0);
            var cos = (float)Math.Cos(angle);
            var sin = (float)Math.Sin(angle);
            g.DrawLine(pen, cx + cos * 5.6f, cy + sin * 5.6f, cx + cos * 7.8f, cy + sin * 7.8f);
        }
    }

    private static GraphicsPath CreateRoundedRect(Rectangle bounds, int radius)
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
