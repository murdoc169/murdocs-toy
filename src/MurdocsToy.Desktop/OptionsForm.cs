using System.Globalization;

namespace MurdocsToy.Desktop;

/// <summary>Edit stake presets, ladder size, refresh rate, colours and one-click behaviour.</summary>
sealed class OptionsForm : Form
{
    readonly Options _o;
    readonly TextBox _stakes = new();
    readonly NumericUpDown _default = new() { DecimalPlaces = 2, Minimum = 1, Maximum = 100000 };
    readonly NumericUpDown _row = new() { Minimum = 16, Maximum = 40 };
    readonly NumericUpDown _refresh = new() { Minimum = 250, Maximum = 10000, Increment = 250 };
    readonly NumericUpDown _font = new() { Minimum = 7, Maximum = 16 };
    readonly ComboBox _persist = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    readonly CheckBox _confirm = new() { Text = "Ask for confirmation before every bet (off = one-click)", AutoSize = true };
    readonly CheckBox _top = new() { Text = "Keep ladder windows on top", AutoSize = true };
    readonly CheckBox _centre = new() { Text = "Auto-centre ladder on last traded price", AutoSize = true };
    readonly Button _back = new() { Width = 90 }, _lay = new() { Width = 90 };

    public OptionsForm(Options o)
    {
        _o = o;
        Text = "Options";
        FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(420, 400);
        var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(12) };
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        void Row(string label, Control c) { t.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Padding = new Padding(0, 4, 0, 0) }); t.Controls.Add(c); }
        void Wide(Control c) { t.Controls.Add(c); t.SetColumnSpan(c, 2); }

        _persist.Items.AddRange(new object[] { "LAPSE", "PERSIST", "MARKET_ON_CLOSE" });
        _stakes.Text = string.Join(", ", o.StakePresets.Select(s => s.ToString("0.##", CultureInfo.InvariantCulture)));
        _default.Value = (decimal)o.DefaultStake; _row.Value = o.RowHeight; _refresh.Value = o.RefreshMs; _font.Value = (decimal)o.FontSize;
        _persist.SelectedItem = o.Persistence; _confirm.Checked = o.ConfirmBets; _top.Checked = o.AlwaysOnTop; _centre.Checked = o.AutoCentre;
        Swatch(_back, o.Back); Swatch(_lay, o.Lay);
        _back.Click += (_, _) => Pick(_back); _lay.Click += (_, _) => Pick(_lay);

        Row("Stake buttons", _stakes); _stakes.Dock = DockStyle.Fill;
        Row("Default stake", _default); Row("Row height (px)", _row); Row("Font size", _font);
        Row("Refresh (ms)", _refresh); Row("Unmatched bets", _persist);
        Row("Back colour", _back); Row("Lay colour", _lay);
        Wide(_confirm); Wide(_top); Wide(_centre);
        var ok = new Button { Text = "Save", DialogResult = DialogResult.None, Width = 80 };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Width = 80 };
        ok.Click += (_, _) => Save();
        var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill };
        buttons.Controls.AddRange(new Control[] { cancel, ok });
        Wide(buttons);
        Controls.Add(t); AcceptButton = ok; CancelButton = cancel;
    }

    static void Swatch(Button b, Color c) { b.BackColor = c; b.Text = ColorTranslator.ToHtml(c); }
    static void Pick(Button b) { using var d = new ColorDialog { Color = b.BackColor }; if (d.ShowDialog() == DialogResult.OK) Swatch(b, d.Color); }

    void Save()
    {
        var stakes = new List<double>();
        foreach (var part in _stakes.Text.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries))
        {
            if (!double.TryParse(part.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) || v < 1)
            { MessageBox.Show($"'{part}' is not a valid stake (minimum 1).", "Options"); return; }
            stakes.Add(v);
        }
        if (stakes.Count == 0) { MessageBox.Show("Enter at least one stake.", "Options"); return; }
        _o.StakePresets = stakes.Distinct().OrderBy(s => s).ToList();
        _o.DefaultStake = (double)_default.Value; _o.RowHeight = (int)_row.Value; _o.RefreshMs = (int)_refresh.Value; _o.FontSize = (float)_font.Value;
        _o.Persistence = (string)_persist.SelectedItem!; _o.ConfirmBets = _confirm.Checked; _o.AlwaysOnTop = _top.Checked; _o.AutoCentre = _centre.Checked;
        _o.BackColour = ColorTranslator.ToHtml(_back.BackColor); _o.LayColour = ColorTranslator.ToHtml(_lay.BackColor);
        DialogResult = DialogResult.OK; Close();
    }
}
