using MurdocsToy.Core;

namespace MurdocsToy.Desktop;

/// <summary>A plain three-way switch for the interface: Classic | Modern dark | Modern light.</summary>
sealed class InterfacePicker : Control
{
    public string Value { get; set; } = Ui.ModernDark;
    static readonly (string Ui, string Label)[] Items = { (Ui.ModernDark, "Modern dark"), (Ui.ModernLight, "Modern light"), (Ui.Classic, "Classic (deprecated)") };
    static readonly int[] Weight = { 3, 3, 4 };   // the last label is longer

    public InterfacePicker()
    {
        Height = 32; DoubleBuffered = true; Cursor = Cursors.Hand; TabStop = true;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
    }

    Rectangle Segment(int i) { int total = Weight.Sum(), x = Width * Weight.Take(i).Sum() / total; return new Rectangle(x, 0, Width * Weight[i] / total, Height - 1); }

    protected override void OnMouseDown(MouseEventArgs e) { for (int i = 0; i < Items.Length; i++) if (Segment(i).Contains(e.Location)) { Value = Items[i].Ui; Focus(); Invalidate(); } }

    protected override bool IsInputKey(Keys k) => k is Keys.Left or Keys.Right || base.IsInputKey(k);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        int i = Array.FindIndex(Items, x => x.Ui == Value);
        if (e.KeyCode == Keys.Right) Value = Items[Math.Min(Items.Length - 1, i + 1)].Ui;
        else if (e.KeyCode == Keys.Left) Value = Items[Math.Max(0, i - 1)].Ui;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; g.Clear(Parent?.BackColor ?? Color.Black);
        for (int i = 0; i < Items.Length; i++)
        {
            var r = Segment(i); bool on = Items[i].Ui == Value;
            using (var b = new SolidBrush(on ? Color.FromArgb(52, 60, 74) : Color.FromArgb(28, 33, 41))) g.FillRectangle(b, r);
            using (var p = new Pen(Color.FromArgb(52, 60, 74))) g.DrawRectangle(p, r);
            TextRenderer.DrawText(g, Items[i].Label, new Font("Segoe UI", 8.5f), r, on ? Color.White : Color.FromArgb(150, 158, 170),
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
    }
}

/// <summary>Login window. Fields are pre-filled from BETFAIR_* environment variables, else from the remembered login.</summary>
sealed class LoginForm : Form
{
    static readonly Color Back = Color.FromArgb(20, 23, 28), FieldBack = Color.FromArgb(28, 33, 41), Muted = Color.FromArgb(140, 148, 160);

    readonly LoginStore.Saved _saved = LoginStore.Load();
    readonly TextBox _user = Field(), _pass = Field(true), _key = Field();
    readonly InterfacePicker _ui = new() { Dock = DockStyle.Top };
    readonly CheckBox _remember = new() { Text = "Remember me", AutoSize = false, Height = 28, Dock = DockStyle.Top, ForeColor = Muted, Font = new Font("Segoe UI", 9f) };
    readonly Label _status = new() { AutoSize = false, Height = 22, ForeColor = Muted, Font = new Font("Segoe UI", 9f), Dock = DockStyle.Top };
    readonly Button _ok = new() { Text = "Log in", Height = 38, Dock = DockStyle.Top, FlatStyle = FlatStyle.Flat, Font = new Font("Segoe UI", 10.5f), ForeColor = Color.White, BackColor = Color.FromArgb(44, 120, 84), Cursor = Cursors.Hand };
    readonly bool _autoLogin;
    public BetfairClient? Client { get; private set; }
    public string Ui => _ui.Value;

    static TextBox Field(bool password = false) => new()
    {
        Dock = DockStyle.Top, Font = new Font("Segoe UI", 10.5f), BackColor = FieldBack, ForeColor = Color.White,
        BorderStyle = BorderStyle.FixedSingle, UseSystemPasswordChar = password,
    };

    static Label Caption(string text) => new() { Text = text, Dock = DockStyle.Top, Height = 22, ForeColor = Muted, Font = new Font("Segoe UI", 8.5f), Padding = new Padding(0, 8, 0, 0) };

    public LoginForm(bool forceAutoLogin, string ui, bool stayOpen = false)
    {
        _ui.Value = ui;
        _user.Text = Environment.GetEnvironmentVariable("BETFAIR_USERNAME") ?? _saved.Username ?? "";
        _pass.Text = Environment.GetEnvironmentVariable("BETFAIR_PASSWORD") ?? LoginStore.Password(_saved) ?? "";
        _key.Text = Environment.GetEnvironmentVariable("BETFAIR_APP_KEY") ?? _saved.AppKey ?? "";
        _remember.Checked = _saved.Remember || _saved.Username == null;
        // start straight away when everything needed is already known
        _autoLogin = !stayOpen && (forceAutoLogin || (_remember.Checked && _user.Text != "" && _pass.Text != "" && _key.Text != ""));

        Text = "Murdoc's Toy";
        FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(380, 456);
        BackColor = Back; ForeColor = Color.White; Font = new Font("Segoe UI", 9f);
        Theme.TitleBar(this, Back, Color.White);

        _ok.FlatAppearance.BorderSize = 0; _ok.FlatAppearance.MouseOverBackColor = Color.FromArgb(54, 140, 98);
        var title = new Label { Text = "Murdoc's Toy", Dock = DockStyle.Top, Height = 40, Font = new Font("Segoe UI Semibold", 18f), ForeColor = Color.White };
        var sub = new Label { Text = "Betfair Exchange", Dock = DockStyle.Top, Height = 22, ForeColor = Muted, Font = new Font("Segoe UI", 9.5f) };
        Panel Gap(int h) => new() { Dock = DockStyle.Top, Height = h, BackColor = Back };

        // docked Top: the last control added sits at the top
        var body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(32, 28, 32, 16), BackColor = Back };
        body.Controls.Add(_ok); body.Controls.Add(Gap(6)); body.Controls.Add(_status); body.Controls.Add(_remember);
        body.Controls.Add(Gap(8)); body.Controls.Add(_ui); body.Controls.Add(Caption("Interface"));
        body.Controls.Add(_key); body.Controls.Add(Caption("App key"));
        body.Controls.Add(_pass); body.Controls.Add(Caption("Password"));
        body.Controls.Add(_user); body.Controls.Add(Caption("Username"));
        body.Controls.Add(Gap(14)); body.Controls.Add(sub); body.Controls.Add(title);
        Controls.Add(body);
        AcceptButton = _ok;
        _ok.Click += async (_, _) => await DoLogin();
        if (_autoLogin) Shown += async (_, _) => await DoLogin();
    }

    async Task DoLogin()
    {
        _ok.Enabled = false; _status.Text = "Logging in..."; _status.ForeColor = Muted;
        try
        {
            var c = new BetfairClient(_key.Text.Trim());
            await c.LoginAsync(_user.Text.Trim(), _pass.Text);
            LoginStore.Save(_user.Text.Trim(), _key.Text.Trim(), _pass.Text, _remember.Checked, Ui);
            Client = c; DialogResult = DialogResult.OK; Close();
        }
        catch (Exception ex) { _status.ForeColor = Color.FromArgb(235, 110, 110); _status.Text = ex.Message; _ok.Enabled = true; }   // saved login failed: show the form
    }
}
