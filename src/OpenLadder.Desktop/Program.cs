using OpenLadder.Core;

namespace OpenLadder.Desktop;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        using var login = new LoginForm(args.Contains("--smoke"));
        if (login.ShowDialog() != DialogResult.OK || login.Client == null) return;
        Application.Run(new MainForm(login.Client, args.Contains("--smoke")));
    }
}

/// <summary>Credentials come from the dialog; fields are pre-filled from BETFAIR_* environment variables.</summary>
sealed class LoginForm : Form
{
    readonly TextBox _user = new() { Text = Environment.GetEnvironmentVariable("BETFAIR_USERNAME") ?? "" };
    readonly TextBox _pass = new() { UseSystemPasswordChar = true, Text = Environment.GetEnvironmentVariable("BETFAIR_PASSWORD") ?? "" };
    readonly TextBox _key = new() { Text = Environment.GetEnvironmentVariable("BETFAIR_APP_KEY") ?? "" };
    readonly Label _status = new() { AutoSize = true, ForeColor = Color.Firebrick };
    readonly Button _ok = new() { Text = "Log in", DialogResult = DialogResult.None };
    public BetfairClient? Client { get; private set; }

    public LoginForm(bool autoLogin = false)
    {
        Text = "OpenLadder - Betfair login";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(340, 190);
        var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(12) };
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        void Row(string l, Control c) { t.Controls.Add(new Label { Text = l, AutoSize = true, Anchor = AnchorStyles.Left }); c.Dock = DockStyle.Fill; t.Controls.Add(c); }
        Row("Username", _user); Row("Password", _pass); Row("App key", _key);
        t.Controls.Add(_status, 0, 3); t.SetColumnSpan(_status, 2);
        t.Controls.Add(_ok, 1, 4);
        Controls.Add(t);
        AcceptButton = _ok;
        _ok.Click += async (_, _) => await DoLogin();
        if (autoLogin) Shown += async (_, _) => await DoLogin();
    }

    async Task DoLogin()
    {
        _ok.Enabled = false; _status.Text = "Logging in...";
        try
        {
            var c = new BetfairClient(_key.Text.Trim());
            await c.LoginAsync(_user.Text.Trim(), _pass.Text);
            Client = c; DialogResult = DialogResult.OK; Close();
        }
        catch (Exception ex) { _status.Text = ex.Message; _ok.Enabled = true; }
    }
}
