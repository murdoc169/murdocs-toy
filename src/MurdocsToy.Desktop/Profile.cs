namespace MurdocsToy.Desktop;

/// <summary>Where the app keeps its settings. MT_PROFILE_DIR overrides it (used for tests and clean screenshots).</summary>
static class Profile
{
    public static string Dir =>
        Environment.GetEnvironmentVariable("MT_PROFILE_DIR") is { Length: > 0 } d ? d
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MurdocsToy");

    public static string File(string name) => Path.Combine(Dir, name);
}
