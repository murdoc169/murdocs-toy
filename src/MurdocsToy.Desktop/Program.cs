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
        using var login = new LoginForm(auto, ui, args.Contains("--show-login"));
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

    static string PathFile => Profile.File("settings.json");
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
