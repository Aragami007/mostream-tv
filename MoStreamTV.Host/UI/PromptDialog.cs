namespace MoStreamTV.Host.UI;

internal static class PromptDialog
{
    public static string? ShowTextInput(IWin32Window owner, string title, string prompt, bool multiline = false)
    {
        using var dialog = new Form
        {
            Text = title,
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MinimizeBox = false,
            MaximizeBox = false,
            ShowInTaskbar = false,
            Font = new Font("Segoe UI", 10F),
            ClientSize = multiline ? new Size(480, 230) : new Size(460, 140),
        };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
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
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 6),
        };

        var input = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = multiline,
            AcceptsReturn = multiline,
            ScrollBars = multiline ? ScrollBars.Vertical : ScrollBars.None,
            Margin = new Padding(0, 0, 0, 10),
        };

        var buttonRow = new FlowLayoutPanel
        {
            Dock = DockStyle.Right,
            AutoSize = true,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Margin = new Padding(0),
        };

        var cancelButton = new Button
        {
            Text = "Cancel",
            DialogResult = DialogResult.Cancel,
            Width = 88,
            Height = 30,
        };
        var okButton = new Button
        {
            Text = "OK",
            DialogResult = DialogResult.OK,
            Width = 88,
            Height = 30,
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

        return dialog.ShowDialog(owner) == DialogResult.OK
            ? input.Text.Trim()
            : null;
    }
}
