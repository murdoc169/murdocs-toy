using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using MurdocsToy.Core;

namespace MurdocsToy.Desktop;

/// <summary>
/// The small TV: an embedded browser (Edge WebView2) showing this market's page on betfair.com. Betfair's own player appears on that
/// page for the events your account is eligible for. The app never touches the video itself, and you log in to the website in the panel
/// yourself (the login is kept in this app's own browser profile, separate from your normal browser).
/// </summary>
sealed partial class ClassicLadderForm
{
    ClassicSection? _tv;
    WebView2? _web;
    bool _tvStarting;

    ClassicSection BuildTv()
    {
        _tv = new ClassicSection("Live video (betfair.com)", 230, closable: true) { Visible = false };
        _tv.Header.CloseClicked += () => _tv.Visible = false;
        _tv.Header.HelpClicked += () => MessageBox.Show(
            "This panel is a small browser showing this market on betfair.com." + Environment.NewLine + Environment.NewLine +
            "Log in to the Betfair website here once (it is remembered for this app). Betfair's own video player then appears on the page " +
            "for the matches your account is eligible for (usually a funded account or a recent bet)." + Environment.NewLine + Environment.NewLine +
            "Betfair does not offer the video through its API, so the app never handles the stream itself.", "Live video");
        return _tv;
    }

    /// <summary>Show or hide the TV panel, starting the browser the first time.</summary>
    async Task ToggleTv()
    {
        if (_tv == null) return;
        if (_tv.Visible) { _tv.Visible = false; return; }
        _tv.Visible = true;
        if (_web != null || _tvStarting) return;
        _tvStarting = true;
        var url = BetfairLinks.MarketPage(_market.MarketId, _market.EventType?.Name);
        try
        {
            var env = await CoreWebView2Environment.CreateAsync(null, Profile.File("webview"));
            _web = new WebView2 { Dock = DockStyle.Fill };
            _tv.Body.Controls.Add(_web);
            await _web.EnsureCoreWebView2Async(env);
            _web.CoreWebView2.Settings.AreDevToolsEnabled = false;
            _web.CoreWebView2.NewWindowRequested += (_, e) => { e.Handled = true; _web.CoreWebView2.Navigate(e.Uri); };   // stay inside the panel
            _web.ZoomFactor = 0.55;                                                                                        // fit the top of the page (where the player sits) in a small panel
            _web.CoreWebView2.Navigate(url);
            SetMsg("Live video panel: log in to betfair.com in it once. Shift-click the screen icon to use your normal browser instead.");
        }
        catch (Exception ex)       // WebView2 runtime missing, or no network: fall back to the normal browser
        {
            _tv.Visible = false;
            SetMsg("The in-app browser is not available (" + ex.Message.Split('\n')[0] + "). Opening the page in your browser.");
            OpenOnBetfair();
        }
        finally { _tvStarting = false; }
    }

    /// <summary>The page title the panel currently shows (used by tests).</summary>
    public string? TvTitle => _web?.CoreWebView2?.DocumentTitle;
}
