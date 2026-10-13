using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MurdocsToy.Core;

namespace MurdocsToy.Desktop;

/// <summary>The proxy used for Betfair (HTTP or SOCKS5), saved in the profile. The proxy password is encrypted for this Windows user.</summary>
sealed record ProxySettings(bool Enabled = false, string Scheme = "http", string Host = "", int Port = 8080, string Username = "", string PasswordProtected = "")
{
    static readonly byte[] Entropy = Encoding.UTF8.GetBytes("MurdocsToy.proxy.v1");

    [System.Text.Json.Serialization.JsonIgnore] public bool Usable => Host.Trim() != "" && Port is > 0 and < 65536;
    [System.Text.Json.Serialization.JsonIgnore]
    public string PlainPassword
    {
        get { try { return PasswordProtected == "" ? "" : Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(PasswordProtected), Entropy, DataProtectionScope.CurrentUser)); } catch { return ""; } }
    }
    public static string Protect(string password) => password == "" ? "" : Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(password), Entropy, DataProtectionScope.CurrentUser));

    /// <summary>The value for Chromium's --proxy-server (the video panel).</summary>
    [System.Text.Json.Serialization.JsonIgnore] public string BrowserArgument => $"{(Scheme == "socks5" ? "socks5" : "http")}://{Host.Trim()}:{Port}";

    /// <summary>Null when the proxy is off or not filled in.</summary>
    public IWebProxy? ToWebProxy()
    {
        if (!Enabled || !Usable) return null;
        var p = new WebProxy(new Uri($"{(Scheme == "socks5" ? "socks5" : "http")}://{Host.Trim()}:{Port}")) { BypassProxyOnLocal = false };
        if (Username != "") p.Credentials = new NetworkCredential(Username, PlainPassword);
        return p;
    }
}

static class ProxyStore
{
    static string PathFile => Profile.File("proxy.json");

    public static ProxySettings Load()
    {
        try { return JsonSerializer.Deserialize<ProxySettings>(File.ReadAllText(PathFile)) ?? new(); } catch { return new(); }
    }

    public static void Save(ProxySettings s)
    {
        try { Directory.CreateDirectory(Path.GetDirectoryName(PathFile)!); File.WriteAllText(PathFile, JsonSerializer.Serialize(s)); } catch { }
    }
}

/// <summary>The proxy dialog opened from the login window.</summary>
sealed class ProxyForm : Form
{
    static readonly Color Back = Color.FromArgb(20, 23, 28), FieldBack = Color.FromArgb(28, 33, 41), Muted = Color.FromArgb(140, 148, 160);
    readonly ComboBox _type = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Top, FlatStyle = FlatStyle.Flat, BackColor = FieldBack, ForeColor = Color.White, Font = new Font("Segoe UI", 10.5f) };
    readonly TextBox _host = Field(), _port = Field(), _user = Field(), _pass = Field(true);
    readonly Label _result = new() { Dock = DockStyle.Top, Height = 38, ForeColor = Muted, Font = new Font("Segoe UI", 9f) };
    public ProxySettings Result { get; private set; }

    static TextBox Field(bool password = false) => new() { Dock = DockStyle.Top, Font = new Font("Segoe UI", 10.5f), BackColor = FieldBack, ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle, UseSystemPasswordChar = password };
    static Label Caption(string t) => new() { Text = t, Dock = DockStyle.Top, Height = 22, ForeColor = Muted, Font = new Font("Segoe UI", 8.5f), Padding = new Padding(0, 6, 0, 0) };

    public ProxyForm(ProxySettings current)
    {
        Result = current;
        Text = "Proxy settings"; FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = MinimizeBox = false; StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(360, 490); BackColor = Back; ForeColor = Color.White; Font = new Font("Segoe UI", 9f);
        Theme.TitleBar(this, Back, Color.White);
        _type.Items.AddRange(new object[] { "HTTP / HTTPS", "SOCKS5" });
        _type.SelectedIndex = current.Scheme == "socks5" ? 1 : 0;
        _host.Text = current.Host; _port.Text = current.Port.ToString(); _user.Text = current.Username; _pass.Text = current.PlainPassword;

        Button Btn(string text, Color bg) { var b = new Button { Text = text, Height = 34, Dock = DockStyle.Top, FlatStyle = FlatStyle.Flat, ForeColor = Color.White, BackColor = bg, Font = new Font("Segoe UI", 10f), Cursor = Cursors.Hand }; b.FlatAppearance.BorderSize = 0; return b; }
        var test = Btn("Test connection", Color.FromArgb(52, 60, 74)); var save = Btn("Save", Color.FromArgb(44, 120, 84));
        Panel Gap(int h) => new() { Dock = DockStyle.Top, Height = h, BackColor = Back };
        var body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(28, 20, 28, 14), BackColor = Back };
        body.Controls.Add(save); body.Controls.Add(Gap(8)); body.Controls.Add(test); body.Controls.Add(Gap(6)); body.Controls.Add(_result);
        body.Controls.Add(_pass); body.Controls.Add(Caption("Password (optional)"));
        body.Controls.Add(_user); body.Controls.Add(Caption("Username (optional)"));
        body.Controls.Add(_port); body.Controls.Add(Caption("Port"));
        body.Controls.Add(_host); body.Controls.Add(Caption("Host or IP"));
        body.Controls.Add(_type); body.Controls.Add(Caption("Type"));
        body.Controls.Add(new Label { Text = "Used for every connection to Betfair (and the video panel).", Dock = DockStyle.Top, Height = 36, ForeColor = Muted });
        Controls.Add(body);
        AcceptButton = save;

        test.Click += async (_, _) =>
        {
            var s = Read();
            if (!s.Usable) { _result.ForeColor = Color.FromArgb(235, 110, 110); _result.Text = "Fill in the host and a port first."; return; }
            _result.ForeColor = Muted; _result.Text = "Testing..."; test.Enabled = false;
            try
            {
                using var http = new HttpClient(new HttpClientHandler { Proxy = (s with { Enabled = true }).ToWebProxy(), UseProxy = true }) { Timeout = TimeSpan.FromSeconds(15) };
                using var r = await http.GetAsync("https://api.betfair.com/");
                _result.ForeColor = Color.FromArgb(110, 200, 140); _result.Text = "Reached Betfair through the proxy.";
            }
            catch (Exception ex) { _result.ForeColor = Color.FromArgb(235, 110, 110); _result.Text = "Failed: " + (ex.InnerException?.Message ?? ex.Message); }
            test.Enabled = true;
        };
        save.Click += (_, _) => { Result = Read(); DialogResult = DialogResult.OK; Close(); };
    }

    ProxySettings Read() => new(Result.Enabled, _type.SelectedIndex == 1 ? "socks5" : "http", _host.Text.Trim(), int.TryParse(_port.Text, out var p) ? p : 0, _user.Text.Trim(), ProxySettings.Protect(_pass.Text));
}
