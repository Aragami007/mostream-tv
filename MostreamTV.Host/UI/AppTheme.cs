using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Reflection;
using System.Runtime.InteropServices;

namespace MostreamTV.Host.UI;

internal static class BrandAssets
{
    private const string IconResource = "MostreamTV.Host.Assets.mostream.ico";
    private const string Logo32Resource = "MostreamTV.Host.Assets.mostream-32.png";
    private const string Logo16Resource = "MostreamTV.Host.Assets.mostream-16.png";

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

    public static int Scale(Control control, int logicalPixels)
    {
        var dpi = control.DeviceDpi > 0 ? control.DeviceDpi : 96;
        return Math.Max(logicalPixels, (int)Math.Round(logicalPixels * (dpi / 96f)));
    }

    public static int RecommendedInputHeight(Control control, int logicalMinHeight = 32)
    {
        var measured = TextRenderer.MeasureText("Ag", control.Font).Height;
        return Math.Max(Scale(control, logicalMinHeight), measured + Scale(control, 12));
    }

    public static int MeasureButtonWidth(Control control, params string[] candidateTexts)
    {
        var maxTextWidth = 0;
        foreach (var text in candidateTexts)
        {
            if (!string.IsNullOrEmpty(text))
                maxTextWidth = Math.Max(maxTextWidth, TextRenderer.MeasureText(text, control.Font, Size.Empty, TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine).Width);
        }

        return maxTextWidth + Scale(control, 28);
    }

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
            if (control.HasChildren && control is not ThemedNumericUpDown)
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
                StyleTextBox(textBox, palette);
                return;

            case RichTextBox richText:
                richText.BackColor = palette.InputBack;
                richText.ForeColor = palette.TextPrimary;
                richText.BorderStyle = BorderStyle.FixedSingle;
                ApplyNativeDarkTheme(richText, palette.IsDark ? "DarkMode_Explorer" : null);
                return;

            case ThemedNumericUpDown themedNumeric:
                themedNumeric.ApplyTheme(palette);
                return;

            case NumericUpDown numeric:
                numeric.BackColor = palette.InputBack;
                numeric.ForeColor = palette.TextPrimary;
                numeric.BorderStyle = palette.IsDark ? BorderStyle.FixedSingle : BorderStyle.Fixed3D;
                return;

            case ThemedComboBox themedCombo:
                themedCombo.ApplyTheme(palette);
                return;

            case ComboBox combo:
                combo.FlatStyle = palette.IsDark ? FlatStyle.Flat : FlatStyle.Standard;
                combo.BackColor = palette.InputBack;
                combo.ForeColor = palette.TextPrimary;
                ApplyNativeDarkTheme(combo, palette.IsDark ? "DarkMode_CFD" : null);
                return;

            case ThemedListView themedListView:
                themedListView.ApplyTheme(palette);
                return;

            case ListView listView:
                listView.BackColor = palette.InputBack;
                listView.ForeColor = palette.TextPrimary;
                listView.OwnerDraw = palette.IsDark && listView.HeaderStyle != ColumnHeaderStyle.None;
                ApplyNativeDarkTheme(listView, palette.IsDark ? "DarkMode_Explorer" : "Explorer");
                return;

            case ThemedCheckButton themedCheckButton:
                themedCheckButton.ApplyTheme(palette);
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
                label.ForeColor = Equals(label.Tag, "muted") ? palette.TextMuted : palette.TextPrimary;
                return;
        }
    }

    public static void StyleTextBox(TextBox textBox, ThemePalette palette)
    {
        var inactive = !textBox.Enabled || (textBox.ReadOnly && !textBox.TabStop);
        textBox.BackColor = inactive || textBox.ReadOnly ? palette.SurfaceBack : palette.InputBack;
        textBox.ForeColor = inactive ? palette.TextMuted : palette.TextPrimary;
        textBox.BorderStyle = palette.IsDark ? BorderStyle.FixedSingle : BorderStyle.Fixed3D;
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

    internal static void DrawButtonFace(
        Graphics graphics,
        Rectangle clientRectangle,
        string text,
        Font font,
        Color backColor,
        Color borderColor,
        Color foreColor,
        bool focused)
    {
        graphics.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        using var backBrush = new SolidBrush(backColor);
        using var borderPen = new Pen(borderColor);
        graphics.FillRectangle(backBrush, clientRectangle);

        var outerRect = new Rectangle(0, 0, Math.Max(1, clientRectangle.Width - 1), Math.Max(1, clientRectangle.Height - 1));
        graphics.DrawRectangle(borderPen, outerRect);

        if (focused && clientRectangle.Width > 8 && clientRectangle.Height > 8)
        {
            using var focusPen = new Pen(Color.FromArgb(110, 155, 220)) { DashStyle = DashStyle.Dot };
            graphics.DrawRectangle(focusPen, new Rectangle(2, 2, clientRectangle.Width - 5, clientRectangle.Height - 5));
        }

        var textBounds = new Rectangle(4, 0, Math.Max(0, clientRectangle.Width - 8), clientRectangle.Height);
        TextRenderer.DrawText(
            graphics,
            text,
            font,
            textBounds,
            foreColor,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
    }

    internal static Color DarkDisabledBack => Color.FromArgb(31, 34, 40);
    internal static Color DarkDisabledBorder => Color.FromArgb(50, 54, 64);
    internal static Color DarkDisabledFore => Color.FromArgb(120, 128, 142);

    internal static void ApplyNativeDarkTheme(Control control, string? themeName)
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

    internal static void UpdateBrush(ref IntPtr brushHandle, Color color)
    {
        if (brushHandle != IntPtr.Zero)
            NativeMethods.DeleteObject(brushHandle);
        brushHandle = NativeMethods.CreateSolidBrush(ColorTranslator.ToWin32(color));
    }

    internal static void ReleaseBrush(ref IntPtr brushHandle)
    {
        if (brushHandle == IntPtr.Zero)
            return;
        NativeMethods.DeleteObject(brushHandle);
        brushHandle = IntPtr.Zero;
    }

    internal static bool TryHandleDarkCtlColor(ref Message m, ThemePalette palette, bool enabled, IntPtr brushHandle)
    {
        if (!palette.IsDark || (m.Msg != NativeMethods.WmCtlColorEdit && m.Msg != NativeMethods.WmCtlColorListBox && m.Msg != NativeMethods.WmCtlColorStatic))
            return false;

        var back = enabled ? palette.InputBack : palette.SurfaceBack;
        var fore = enabled ? palette.TextPrimary : palette.TextMuted;
        NativeMethods.SetBkColor(m.WParam, ColorTranslator.ToWin32(back));
        NativeMethods.SetTextColor(m.WParam, ColorTranslator.ToWin32(fore));
        m.Result = brushHandle;
        return true;
    }

    internal static void HandlePaintOrPrint(Control control, ref Message m, Action<Graphics> paint)
    {
        if (m.Msg == NativeMethods.WmPaint && control.IsHandleCreated)
        {
            using var graphics = control.CreateGraphics();
            paint(graphics);
        }
        else if (m.Msg == NativeMethods.WmPrint && m.WParam != IntPtr.Zero)
        {
            using var graphics = Graphics.FromHdc(m.WParam);
            paint(graphics);
        }
    }

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    private static extern int SetWindowTheme(IntPtr hwnd, string pszSubAppName, string? pszSubIdList);
}

internal static class NativeMethods
{
    public const int WmPaint = 0x000F;
    public const int WmEraseBkgnd = 0x0014;
    public const int WmCtlColorEdit = 0x0133;
    public const int WmCtlColorListBox = 0x0134;
    public const int WmCtlColorStatic = 0x0138;
    public const int WmReflect = 0x2000;
    public const int WmPrint = 0x0317;
    public const int WmPrintClient = 0x0318;
    public const int LvmGetHeader = 0x101F;

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct COMBOBOXINFO
    {
        public int cbSize;
        public RECT rcItem;
        public RECT rcButton;
        public int stateButton;
        public IntPtr hwndCombo;
        public IntPtr hwndItem;
        public IntPtr hwndList;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PAINTSTRUCT
    {
        public IntPtr hdc;
        [MarshalAs(UnmanagedType.Bool)]
        public bool fErase;
        public RECT rcPaint;
        [MarshalAs(UnmanagedType.Bool)]
        public bool fRestore;
        [MarshalAs(UnmanagedType.Bool)]
        public bool fIncUpdate;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
        public byte[] rgbReserved;
    }

    [DllImport("gdi32.dll")]
    public static extern IntPtr CreateSolidBrush(int crColor);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DeleteObject(IntPtr hObject);

    [DllImport("gdi32.dll")]
    public static extern int SetBkColor(IntPtr hdc, int crColor);

    [DllImport("gdi32.dll")]
    public static extern int SetTextColor(IntPtr hdc, int crColor);

    [DllImport("user32.dll")]
    public static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetComboBoxInfo(IntPtr hwndCombo, ref COMBOBOXINFO pcbi);

    [DllImport("user32.dll")]
    public static extern IntPtr BeginPaint(IntPtr hWnd, out PAINTSTRUCT lpPaint);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool EndPaint(IntPtr hWnd, [In] ref PAINTSTRUCT lpPaint);
}

internal sealed class ThemedButton : Button
{
    private ThemePalette _palette = AppTheme.Light;
    private bool _hovered;
    private bool _pressed;

    public void ApplyTheme(ThemePalette palette)
    {
        _palette = palette;
        AppTheme.StyleButton(this, palette);
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
        _pressed = false;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs mevent)
    {
        if (mevent.Button == MouseButtons.Left)
        {
            _pressed = true;
            Invalidate();
        }
        base.OnMouseDown(mevent);
    }

    protected override void OnMouseUp(MouseEventArgs mevent)
    {
        _pressed = false;
        Invalidate();
        base.OnMouseUp(mevent);
    }

    protected override void OnPaint(PaintEventArgs pevent)
    {
        if (!_palette.IsDark)
        {
            base.OnPaint(pevent);
            return;
        }

        // Paint dark flat buttons directly so WinForms FlatStyle padding does not clip or warp text.
        var back = !Enabled
            ? AppTheme.DarkDisabledBack
            : _pressed
                ? _palette.SelectionBack
                : _hovered
                    ? _palette.ButtonHover
                    : _palette.ButtonBack;
        var border = !Enabled
            ? AppTheme.DarkDisabledBorder
            : _hovered
                ? Color.FromArgb(92, 102, 120)
                : _palette.Border;
        var fore = Enabled ? _palette.TextPrimary : AppTheme.DarkDisabledFore;

        AppTheme.DrawButtonFace(pevent.Graphics, ClientRectangle, Text, Font, back, border, fore, Focused && ShowFocusCues && Enabled);
    }
}

internal sealed class ThemedCheckButton : CheckBox
{
    private ThemePalette _palette = AppTheme.Light;
    private bool _hovered;
    private bool _pressed;

    public ThemedCheckButton()
    {
        Appearance = Appearance.Button;
        TextAlign = ContentAlignment.MiddleCenter;
    }

    public void ApplyTheme(ThemePalette palette)
    {
        _palette = palette;
        if (Checked)
        {
            FlatStyle = palette.IsDark ? FlatStyle.Flat : FlatStyle.Standard;
            UseVisualStyleBackColor = false;
            BackColor = palette.ReadyBack;
            ForeColor = palette.ReadyFore;
        }
        else
        {
            FlatAppearance.CheckedBackColor = palette.ButtonBack;
            AppTheme.StyleButton(this, palette);
        }

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
        _pressed = false;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs mevent)
    {
        if (mevent.Button == MouseButtons.Left)
        {
            _pressed = true;
            Invalidate();
        }
        base.OnMouseDown(mevent);
    }

    protected override void OnMouseUp(MouseEventArgs mevent)
    {
        _pressed = false;
        Invalidate();
        base.OnMouseUp(mevent);
    }

    protected override void OnPaint(PaintEventArgs pevent)
    {
        if (!_palette.IsDark)
        {
            base.OnPaint(pevent);
            return;
        }

        Color back;
        Color border;
        Color fore;
        if (!Enabled)
        {
            back = AppTheme.DarkDisabledBack;
            border = AppTheme.DarkDisabledBorder;
            fore = AppTheme.DarkDisabledFore;
        }
        else if (Checked)
        {
            back = _pressed ? Color.FromArgb(48, 114, 72) : _hovered ? Color.FromArgb(44, 104, 66) : _palette.ReadyBack;
            border = Color.FromArgb(62, 148, 96);
            fore = _palette.ReadyFore;
        }
        else
        {
            back = _pressed ? _palette.SelectionBack : _hovered ? _palette.ButtonHover : _palette.ButtonBack;
            border = _hovered ? Color.FromArgb(92, 102, 120) : _palette.Border;
            fore = _palette.TextPrimary;
        }

        AppTheme.DrawButtonFace(pevent.Graphics, ClientRectangle, Text, Font, back, border, fore, Focused && ShowFocusCues && Enabled);
    }
}

internal sealed class ThemedComboBox : ComboBox
{
    private ThemePalette _palette = AppTheme.Light;
    private IntPtr _editBrush;

    public string PlaceholderText { get; set; } = string.Empty;

    public ThemedComboBox()
    {
        DrawMode = DrawMode.OwnerDrawFixed;
    }

    public void ApplyTheme(ThemePalette palette)
    {
        _palette = palette;
        FlatStyle = palette.IsDark ? FlatStyle.Flat : FlatStyle.Standard;
        ItemHeight = Math.Max(AppTheme.Scale(this, 22), TextRenderer.MeasureText("Ag", Font).Height + 4);
        SyncEditColors();
        AppTheme.ApplyNativeDarkTheme(this, palette.IsDark ? "DarkMode_CFD" : null);
        Invalidate(true);
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        base.OnEnabledChanged(e);
        SyncEditColors();
        Invalidate(true);
    }

    protected override void Dispose(bool disposing)
    {
        AppTheme.ReleaseBrush(ref _editBrush);
        base.Dispose(disposing);
    }

    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        var isComboEdit = (e.State & DrawItemState.ComboBoxEdit) != 0;
        if (e.Index < 0 || e.Index >= Items.Count)
        {
            if (!isComboEdit)
                return;

            var emptyBack = Enabled
                ? (_palette.IsDark ? _palette.InputBack : SystemColors.Window)
                : (_palette.IsDark ? _palette.SurfaceBack : SystemColors.Control);
            using var emptyBrush = new SolidBrush(emptyBack);
            e.Graphics.FillRectangle(emptyBrush, e.Bounds);

            if (!string.IsNullOrEmpty(PlaceholderText))
            {
                var emptyBounds = new Rectangle(e.Bounds.X + 4, e.Bounds.Y, Math.Max(0, e.Bounds.Width - 8), e.Bounds.Height);
                TextRenderer.DrawText(
                    e.Graphics,
                    PlaceholderText,
                    Font,
                    emptyBounds,
                    _palette.TextMuted,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            }

            return;
        }

        var selected = (e.State & DrawItemState.Selected) != 0;
        var back = !Enabled
            ? (_palette.IsDark ? _palette.SurfaceBack : SystemColors.Control)
            : selected
                ? _palette.SelectionBack
                : (_palette.IsDark ? _palette.InputBack : SystemColors.Window);
        var fore = !Enabled
            ? _palette.TextMuted
            : selected
                ? Color.White
                : _palette.TextPrimary;

        using var backBrush = new SolidBrush(back);
        e.Graphics.FillRectangle(backBrush, e.Bounds);

        var text = GetItemText(Items[e.Index]) ?? string.Empty;
        var textBounds = new Rectangle(e.Bounds.X + 4, e.Bounds.Y, Math.Max(0, e.Bounds.Width - 8), e.Bounds.Height);
        TextRenderer.DrawText(
            e.Graphics,
            text,
            Font,
            textBounds,
            fore,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }

    protected override void WndProc(ref Message m)
    {
        if (AppTheme.TryHandleDarkCtlColor(ref m, _palette, Enabled, _editBrush))
            return;

        base.WndProc(ref m);

        if (_palette.IsDark)
            AppTheme.HandlePaintOrPrint(this, ref m, PaintDarkFrame);
    }

    private void SyncEditColors()
    {
        var back = _palette.IsDark ? (Enabled ? _palette.InputBack : _palette.SurfaceBack) : SystemColors.Window;
        var fore = Enabled ? _palette.TextPrimary : _palette.TextMuted;
        BackColor = back;
        ForeColor = fore;
        AppTheme.UpdateBrush(ref _editBrush, back);
    }

    private void PaintDarkFrame(Graphics graphics)
    {
        var back = Enabled ? _palette.InputBack : _palette.SurfaceBack;
        var buttonWidth = Math.Max(20, SystemInformation.VerticalScrollBarWidth);
        var itemRect = new Rectangle(3, 3, Math.Max(0, Width - buttonWidth - 6), Math.Max(0, Height - 6));
        var buttonRect = new Rectangle(Math.Max(0, Width - buttonWidth - 1), 1, buttonWidth, Math.Max(1, Height - 2));

        var info = new NativeMethods.COMBOBOXINFO { cbSize = Marshal.SizeOf<NativeMethods.COMBOBOXINFO>() };
        if (NativeMethods.GetComboBoxInfo(Handle, ref info))
        {
            if (info.hwndItem != IntPtr.Zero && NativeMethods.GetWindowRect(info.hwndItem, out var editScreen))
            {
                var topLeft = PointToClient(new Point(editScreen.Left, editScreen.Top));
                itemRect = new Rectangle(
                    topLeft.X,
                    topLeft.Y,
                    Math.Max(0, editScreen.Right - editScreen.Left),
                    Math.Max(0, editScreen.Bottom - editScreen.Top));
            }
            else
            {
                itemRect = new Rectangle(
                    info.rcItem.Left,
                    info.rcItem.Top,
                    Math.Max(0, info.rcItem.Right - info.rcItem.Left),
                    Math.Max(0, info.rcItem.Bottom - info.rcItem.Top));
            }

            if (info.rcButton.Right > info.rcButton.Left)
            {
                buttonRect = new Rectangle(
                    info.rcButton.Left,
                    1,
                    Math.Max(16, Width - info.rcButton.Left - 1),
                    Math.Max(1, Height - 2));
            }
        }

        using var frameRegion = new Region(ClientRectangle);
        if (Enabled && itemRect.Width > 0 && itemRect.Height > 0)
            frameRegion.Exclude(itemRect);

        using (var backBrush = new SolidBrush(back))
            graphics.FillRegion(backBrush, frameRegion);

        var disabledText = !string.IsNullOrEmpty(Text) ? Text : PlaceholderText;
        if (!Enabled && !string.IsNullOrEmpty(disabledText) && itemRect.Width > 0 && itemRect.Height > 0)
        {
            var disabledTextRect = DropDownStyle == ComboBoxStyle.DropDownList
                ? new Rectangle(itemRect.X + 4, itemRect.Y, Math.Max(0, itemRect.Width - 8), itemRect.Height)
                : itemRect;
            TextRenderer.DrawText(
                graphics,
                disabledText,
                Font,
                disabledTextRect,
                _palette.TextMuted,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }

        var buttonBack = Enabled ? _palette.ButtonBack : _palette.SurfaceBack;
        using (var buttonBrush = new SolidBrush(buttonBack))
            graphics.FillRectangle(buttonBrush, buttonRect);

        using var borderPen = new Pen(_palette.Border);
        graphics.DrawLine(borderPen, buttonRect.Left, 1, buttonRect.Left, Height - 2);
        graphics.DrawRectangle(borderPen, 0, 0, Math.Max(1, Width - 1), Math.Max(1, Height - 1));

        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var cx = buttonRect.Left + buttonRect.Width / 2f;
        var cy = Height / 2f;
        using var arrowPen = new Pen(Enabled ? _palette.TextPrimary : _palette.TextMuted, 1.6f)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
        };
        graphics.DrawLines(arrowPen, new[]
        {
            new PointF(cx - 3.5f, cy - 1.5f),
            new PointF(cx, cy + 2f),
            new PointF(cx + 3.5f, cy - 1.5f),
        });
    }
}

internal sealed class ThemedNumericUpDown : NumericUpDown
{
    private ThemePalette _palette = AppTheme.Light;
    private IntPtr _editBrush;
    private SpinButtonWindow? _spinWindow;
    private bool _spinHookSubscribed;

    public void ApplyTheme(ThemePalette palette)
    {
        _palette = palette;
        BorderStyle = palette.IsDark ? BorderStyle.FixedSingle : BorderStyle.Fixed3D;
        SyncColors();
        EnsureSpinHook();
        Invalidate(true);
        if (Controls.Count > 0)
            Controls[0].Invalidate();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        EnsureSpinHook();
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        base.OnEnabledChanged(e);
        SyncColors();
        Invalidate(true);
        if (Controls.Count > 0)
            Controls[0].Invalidate();
    }

    protected override void Dispose(bool disposing)
    {
        _spinWindow?.ReleaseHandle();
        _spinWindow = null;
        AppTheme.ReleaseBrush(ref _editBrush);
        base.Dispose(disposing);
    }

    protected override void WndProc(ref Message m)
    {
        if (AppTheme.TryHandleDarkCtlColor(ref m, _palette, Enabled, _editBrush))
            return;

        base.WndProc(ref m);

        if (_palette.IsDark)
        {
            AppTheme.HandlePaintOrPrint(this, ref m, graphics =>
            {
                using var borderPen = new Pen(_palette.Border);
                graphics.DrawRectangle(borderPen, 0, 0, Math.Max(1, Width - 1), Math.Max(1, Height - 1));
            });
        }
    }

    private void SyncColors()
    {
        var back = _palette.IsDark ? (Enabled ? _palette.InputBack : _palette.SurfaceBack) : SystemColors.Window;
        var fore = Enabled ? _palette.TextPrimary : _palette.TextMuted;
        BackColor = back;
        ForeColor = fore;
        AppTheme.UpdateBrush(ref _editBrush, back);
    }

    private void EnsureSpinHook()
    {
        if (Controls.Count == 0)
            return;

        if (!_spinHookSubscribed)
        {
            _spinHookSubscribed = true;
            Controls[0].HandleCreated += (_, _) => EnsureSpinHook();
        }

        if (!Controls[0].IsHandleCreated)
            return;

        _spinWindow ??= new SpinButtonWindow(this);
        if (_spinWindow.Handle != Controls[0].Handle)
        {
            _spinWindow.ReleaseHandle();
            _spinWindow.AssignHandle(Controls[0].Handle);
        }
    }

    private sealed class SpinButtonWindow : NativeWindow
    {
        private readonly ThemedNumericUpDown _owner;

        public SpinButtonWindow(ThemedNumericUpDown owner)
        {
            _owner = owner;
        }

        protected override void WndProc(ref Message m)
        {
            if (!_owner._palette.IsDark)
            {
                base.WndProc(ref m);
                return;
            }

            if (m.Msg == NativeMethods.WmEraseBkgnd)
            {
                m.Result = (IntPtr)1;
                return;
            }

            if (m.Msg is NativeMethods.WmPrint or NativeMethods.WmPrintClient && m.WParam != IntPtr.Zero)
            {
                using var graphics = Graphics.FromHdc(m.WParam);
                PaintSpinButtons(graphics);
                m.Result = (IntPtr)1;
                return;
            }

            if (m.Msg == NativeMethods.WmPaint)
            {
                var hdc = NativeMethods.BeginPaint(m.HWnd, out var ps);
                try
                {
                    using var graphics = Graphics.FromHdc(hdc);
                    PaintSpinButtons(graphics);
                }
                finally
                {
                    NativeMethods.EndPaint(m.HWnd, ref ps);
                }

                m.Result = IntPtr.Zero;
                return;
            }

            base.WndProc(ref m);
        }

        private void PaintSpinButtons(Graphics graphics)
        {
            var size = _owner.Controls[0].ClientSize;
            var palette = _owner._palette;
            var back = _owner.Enabled ? palette.ButtonBack : palette.SurfaceBack;
            var arrowColor = _owner.Enabled ? palette.TextPrimary : palette.TextMuted;
            var midY = size.Height / 2;

            using (var backBrush = new SolidBrush(back))
                graphics.FillRectangle(backBrush, 0, 0, size.Width, size.Height);

            using (var borderPen = new Pen(palette.Border))
            {
                graphics.DrawLine(borderPen, 0, 0, 0, size.Height);
                graphics.DrawLine(borderPen, 0, midY, size.Width, midY);
            }

            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var arrowBrush = new SolidBrush(arrowColor);
            var cx = size.Width / 2f;
            var upCy = midY / 2f;
            var downCy = midY + (size.Height - midY) / 2f;

            graphics.FillPolygon(arrowBrush, new[]
            {
                new PointF(cx, upCy - 2.2f),
                new PointF(cx - 3.3f, upCy + 1.8f),
                new PointF(cx + 3.3f, upCy + 1.8f),
            });
            graphics.FillPolygon(arrowBrush, new[]
            {
                new PointF(cx, downCy + 2.2f),
                new PointF(cx - 3.3f, downCy - 1.8f),
                new PointF(cx + 3.3f, downCy - 1.8f),
            });
        }
    }
}

internal sealed class ThemedListView : ListView
{
    private ThemePalette _palette = AppTheme.Light;

    public void ApplyTheme(ThemePalette palette)
    {
        _palette = palette;
        BackColor = EffectiveBackColor;
        ForeColor = palette.TextPrimary;
        OwnerDraw = palette.IsDark && HeaderStyle != ColumnHeaderStyle.None;
        AppTheme.ApplyNativeDarkTheme(this, palette.IsDark ? "DarkMode_Explorer" : "Explorer");
        Invalidate(true);
    }

    private Color EffectiveBackColor => _palette.IsDark && !Enabled ? _palette.SurfaceBack : _palette.InputBack;

    protected override void OnEnabledChanged(EventArgs e)
    {
        base.OnEnabledChanged(e);
        BackColor = EffectiveBackColor;
        Invalidate(true);
    }

    protected override void WndProc(ref Message m)
    {
        if (_palette.IsDark && m.Msg == NativeMethods.WmEraseBkgnd)
        {
            using var graphics = Graphics.FromHdc(m.WParam);
            using var backBrush = new SolidBrush(EffectiveBackColor);
            graphics.FillRectangle(backBrush, ClientRectangle);
            m.Result = (IntPtr)1;
            return;
        }

        base.WndProc(ref m);

        if (_palette.IsDark && !Enabled)
            AppTheme.HandlePaintOrPrint(this, ref m, PaintDisabledBody);
    }

    private void PaintDisabledBody(Graphics graphics)
    {
        var topY = GetHeaderHeight();
        if (ClientSize.Height > topY && ClientSize.Width > 0)
        {
            using var backBrush = new SolidBrush(_palette.SurfaceBack);
            graphics.FillRectangle(backBrush, 0, topY, ClientSize.Width, ClientSize.Height - topY);
        }
    }

    private int GetHeaderHeight()
    {
        if (View != View.Details || HeaderStyle == ColumnHeaderStyle.None || !IsHandleCreated)
            return 0;

        var headerHandle = NativeMethods.SendMessage(Handle, NativeMethods.LvmGetHeader, IntPtr.Zero, IntPtr.Zero);
        if (headerHandle != IntPtr.Zero && NativeMethods.GetWindowRect(headerHandle, out var rect))
            return Math.Max(0, rect.Bottom - rect.Top);

        return 0;
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
            g.DrawRectangle(borderPen, borderRect);

        if (!string.IsNullOrEmpty(Text))
        {
            var textRect = new Rectangle(8, 0, Math.Min(Width - 16, textSize.Width + 6), textSize.Height);
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
