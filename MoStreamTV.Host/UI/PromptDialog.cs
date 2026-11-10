namespace MoStreamTV.Host.UI;

internal static class PromptDialog
{
    public static string? ShowTextInput(
        IWin32Window owner,
        string title,
        string prompt,
        bool multiline = false,
        ThemePalette? palette = null)
    {
        using var dialog = new Form
        {
            Text = title,
            Icon = BrandAssets.AppIcon,
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MinimizeBox = false,
            MaximizeBox = false,
            ShowInTaskbar = false,
            Font = new Font("Segoe UI", 10F),
            ClientSize = multiline ? new Size(520, 250) : new Size(500, 172),
        };

        var activePalette = palette ?? AppTheme.GetPalette(AppTheme.LoadDarkPreference());
        var dpiScale = dialog.DeviceDpi > 0 ? dialog.DeviceDpi / 96f : 1f;
        var scaledWidth = Math.Max(multiline ? 520 : 500, (int)Math.Round((multiline ? 520 : 500) * dpiScale));
        dialog.ClientSize = new Size(scaledWidth, multiline ? (int)Math.Round(250 * dpiScale) : (int)Math.Round(172 * dpiScale));
        dialog.AutoSize = true;
        dialog.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        dialog.MinimumSize = new Size(scaledWidth, multiline ? (int)Math.Round(240 * dpiScale) : (int)Math.Round(175 * dpiScale));

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(14),
            ColumnCount = 1,
            RowCount = 3,
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(multiline ? new RowStyle(SizeType.Percent, 100) : new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var label = new Label
        {
            Text = prompt,
            UseMnemonic = false,
            AutoSize = true,
            MaximumSize = new Size(scaledWidth - 36, 0),
            Margin = new Padding(0, 0, 0, 8),
        };

        var input = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = multiline,
            AcceptsReturn = multiline,
            MinimumSize = new Size(0, multiline ? (int)Math.Round(96 * dpiScale) : 0),
            ScrollBars = multiline ? ScrollBars.Vertical : ScrollBars.None,
            Margin = new Padding(0, 0, 0, 12),
        };

        var buttonHeight = AppTheme.RecommendedInputHeight(dialog, 32);
        var buttonWidth = Math.Max(AppTheme.Scale(dialog, 92), AppTheme.MeasureButtonWidth(dialog, "OK", "Cancel"));

        var buttonRow = new FlowLayoutPanel
        {
            Dock = DockStyle.Right,
            AutoSize = true,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Margin = new Padding(0),
        };

        var cancelButton = new ThemedButton
        {
            Text = "Cancel",
            DialogResult = DialogResult.Cancel,
            Width = buttonWidth,
            Height = buttonHeight,
        };
        var okButton = new ThemedButton
        {
            Text = "OK",
            DialogResult = DialogResult.OK,
            Width = buttonWidth,
            Height = buttonHeight,
            Margin = new Padding(0, 0, 8, 0),
        };

        buttonRow.Controls.Add(cancelButton);
        buttonRow.Controls.Add(okButton);

        layout.Controls.Add(label, 0, 0);
        layout.Controls.Add(input, 0, 1);
        layout.Controls.Add(buttonRow, 0, 2);

        dialog.Controls.Add(layout);
        if (!multiline)
            dialog.AcceptButton = okButton;
        dialog.CancelButton = cancelButton;
        dialog.HandleCreated += (_, _) => AppTheme.ApplyTitleBar(dialog, activePalette.IsDark);
        AppTheme.ApplyToForm(dialog, activePalette);

        return dialog.ShowDialog(owner) == DialogResult.OK
            ? input.Text.Trim()
            : null;
    }
}
