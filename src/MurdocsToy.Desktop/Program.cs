using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MurdocsToy.Core;

namespace MurdocsToy.Desktop;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        if (args.Contains("--forget-login")) LoginStore.Forget();
        bool auto = args.Contains("--smoke") || args.Contains("--autologin");
        string ui = (args.FirstOrDefault(a => a.StartsWith("--ui="))?[5..]) switch
        {
            "classic" => Ui.Classic, "light" => Ui.ModernLight, "dark" => Ui.ModernDark, _ => LoginStore.Load().Ui ?? Ui.Classic,
        };
        Theme.Dark = ui == Ui.ModernDark;
        using var login = new LoginForm(auto, ui);
        if (login.ShowDialog() != DialogResult.OK || login.Client == null) return;
        ui = login.Ui;
        Theme.Dark = ui == Ui.ModernDark;
        bool smoke = args.Contains("--smoke"), privacy = args.Contains("--privacy"), openLadder = args.Contains("--open-ladder");
        string? search = args.FirstOrDefault(a => a.StartsWith("--search="))?[9..];
        Form main = ui == Ui.Classic ? new ClassicMainForm(login.Client, smoke, privacy, openLadder, search)
                                     : new MainForm(login.Client, ui, smoke, privacy, openLadder, search);
        Application.Run(main);
    }
}

/// <summary>
/// Remembers the login between runs. Username and app key are stored as plain settings; the password is
/// encrypted with Windows DPAPI (current user), so it can only be decrypted by this Windows account.
/// </summary>
static class LoginStore
{
    public record Saved(string? Username, string? AppKey, string? PasswordProtected, bool Remember = true, string? Ui = null);

    static readonly string PathFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MurdocsToy", "settings.json");
    static readonly byte[] Entropy = Encoding.UTF8.GetBytes("MurdocsToy.login.v1");

    public static Saved Load()
    {
        try { return JsonSerializer.Deserialize<Saved>(File.ReadAllText(PathFile)) ?? new(null, null, null); }
        catch { return new(null, null, null); }
    }

    public static string? Password(Saved s)
    {
        if (string.IsNullOrEmpty(s.PasswordProtected)) return null;
        try { return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(s.PasswordProtected), Entropy, DataProtectionScope.CurrentUser)); }
        catch { return null; }   // different Windows user or corrupted: ask again
    }

    public static void Save(string username, string appKey, string password, bool rememberPassword, string ui)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(PathFile)!);
            string? protectedPw = rememberPassword
                ? Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(password), Entropy, DataProtectionScope.CurrentUser))
                : null;
            File.WriteAllText(PathFile, JsonSerializer.Serialize(new Saved(username, appKey, protectedPw, rememberPassword, ui)));
        }
        catch { /* remembering is optional */ }
    }

    /// <summary>Removes the saved password (username and app key stay so the form is pre-filled).</summary>
    public static void Forget()
    {
        var s = Load();
        try { File.WriteAllText(PathFile, JsonSerializer.Serialize(new Saved(s.Username, s.AppKey, null, false, s.Ui))); } catch { }
    }
}

/// <summary>Login dialog. Fields are pre-filled from BETFAIR_* environment variables, else from the remembered login.</summary>
sealed class LoginForm : Form
{
    readonly LoginStore.Saved _saved = LoginStore.Load();
    readonly TextBox _user = new();
    readonly TextBox _pass = new() { UseSystemPasswordChar = true };
    readonly TextBox _key = new();
    readonly ComboBox _ui = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    readonly CheckBox _remember = new() { Text = "Remember me (password stored encrypted for this Windows user)", AutoSize = true };
    readonly Label _status = new() { AutoSize = true, ForeColor = Color.Firebrick };
    readonly Button _ok = new() { Text = "Log in", DialogResult = DialogResult.None };
    readonly bool _autoLogin;
    public BetfairClient? Client { get; private set; }
    public string Ui => MurdocsToy.Desktop.Ui.FromLabel((string)_ui.SelectedItem!);

    public LoginForm(bool forceAutoLogin, string ui)
    {
        foreach (var u in MurdocsToy.Desktop.Ui.All) _ui.Items.Add(MurdocsToy.Desktop.Ui.Label(u));
        _ui.SelectedItem = MurdocsToy.Desktop.Ui.Label(ui);
        _user.Text = Environment.GetEnvironmentVariable("BETFAIR_USERNAME") ?? _saved.Username ?? "";
        _pass.Text = Environment.GetEnvironmentVariable("BETFAIR_PASSWORD") ?? LoginStore.Password(_saved) ?? "";
        _key.Text = Environment.GetEnvironmentVariable("BETFAIR_APP_KEY") ?? _saved.AppKey ?? "";
        _remember.Checked = _saved.Remember || _saved.Username == null;
        // start straight away when everything needed is already known
        _autoLogin = forceAutoLogin || (_remember.Checked && _user.Text != "" && _pass.Text != "" && _key.Text != "");

        Text = "Murdoc's Toy - Betfair login";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(380, 250);
        var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(12) };
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        void Row(string l, Control c) { t.Controls.Add(new Label { Text = l, AutoSize = true, Anchor = AnchorStyles.Left }); c.Dock = DockStyle.Fill; t.Controls.Add(c); }
        Row("Username", _user); Row("Password", _pass); Row("App key", _key); Row("Interface", _ui);
        t.Controls.Add(_remember, 0, 4); t.SetColumnSpan(_remember, 2);
        t.Controls.Add(_status, 0, 5); t.SetColumnSpan(_status, 2);
        t.Controls.Add(_ok, 1, 6);
        Controls.Add(t);
        AcceptButton = _ok;
        _ok.Click += async (_, _) => await DoLogin();
        if (_autoLogin) Shown += async (_, _) => await DoLogin();
        Theme.Apply(this);
    }

    async Task DoLogin()
    {
        _ok.Enabled = false; _status.Text = "Logging in...";
        try
        {
            var c = new BetfairClient(_key.Text.Trim());
            await c.LoginAsync(_user.Text.Trim(), _pass.Text);
            LoginStore.Save(_user.Text.Trim(), _key.Text.Trim(), _pass.Text, _remember.Checked, Ui);
            Client = c; DialogResult = DialogResult.OK; Close();
        }
        catch (Exception ex) { _status.Text = ex.Message; _ok.Enabled = true; }   // saved login failed: show the form
    }
}
