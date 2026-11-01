namespace AlToque.Core;

/// <summary>
/// Ubicación de los datos locales. En modo portable (existe <c>portable.flag</c> junto al
/// ejecutable) todo se guarda en una subcarpeta del propio ejecutable.
/// </summary>
public sealed class AppPaths
{
    public const string PortableFlagName = "portable.flag";
    public const string HomeOverrideVariable = "ALTOQUE_HOME";

    public AppPaths(string configDir, bool isPortable)
    {
        ConfigDir = Path.GetFullPath(configDir);
        IsPortable = isPortable;
    }

    public string ConfigDir { get; }
    public bool IsPortable { get; }

    public string SettingsFile => Path.Combine(ConfigDir, "settings.json");
    public string TrustFile => Path.Combine(ConfigDir, "trust.json");
    public string IdentityFile => Path.Combine(ConfigDir, "identity.bin");
    public string SealKeyFile => Path.Combine(ConfigDir, "seal.key");
    public string HistoryFile => Path.Combine(ConfigDir, "history.jsonl");
    public string PeerCacheFile => Path.Combine(ConfigDir, "peers-cache.json");
    public string LogDir => Path.Combine(ConfigDir, "logs");
    public string ChatDir => Path.Combine(ConfigDir, "chats");
    public string ChatKeyFile => Path.Combine(ConfigDir, "chat.key");

    /// <summary>Imágenes pegadas en un chat (Ctrl+V): se guardan aquí para enviarlas y verlas después.</summary>
    public string PastedDir => Path.Combine(ConfigDir, "pasted");

    public static AppPaths Resolve()
    {
        var overrideDir = Environment.GetEnvironmentVariable(HomeOverrideVariable);
        if (!string.IsNullOrWhiteSpace(overrideDir))
            return Create(overrideDir, isPortable: false);

        var exeDir = AppContext.BaseDirectory;
        if (File.Exists(Path.Combine(exeDir, PortableFlagName)))
            return Create(Path.Combine(exeDir, AppInfo.Id + ".data"), isPortable: true);

        string baseDir;
        if (OperatingSystem.IsWindows())
            baseDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppInfo.Id);
        else if (OperatingSystem.IsMacOS())
            baseDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support", AppInfo.Id);
        else
        {
            var xdg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
            if (string.IsNullOrWhiteSpace(xdg))
                xdg = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
            baseDir = Path.Combine(xdg, "altoque");
        }
        return Create(baseDir, isPortable: false);
    }

    public static AppPaths Create(string dir, bool isPortable)
    {
        var paths = new AppPaths(dir, isPortable);
        SecureFile.EnsurePrivateDirectory(paths.ConfigDir);
        return paths;
    }
}
