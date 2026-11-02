using System.Net.Http.Headers;
using System.Text.Json;
using AlToque.Core;

namespace AlToque;

/// <summary>Última versión publicada en GitHub Releases: de ahí se actualizan el escritorio y Android.</summary>
public static class ReleaseCheck
{
    public const string RepositoryUrl = "https://github.com/Krost22/Al-Toque";
    public const string ReleasesUrl = RepositoryUrl + "/releases/latest";
    private const string LatestApi = "https://api.github.com/repos/Krost22/Al-Toque/releases/latest";

    /// <param name="Version">"0.2.0", sin la "v" de la etiqueta.</param>
    /// <param name="ApkUrl">Descarga directa del APK de Android, si la versión lo trae.</param>
    /// <param name="Assets">Cada archivo de la versión (nombre → descarga directa), para actualizar las copias portables.</param>
    public sealed record Release(string Version, string? ApkUrl, IReadOnlyDictionary<string, string>? Assets = null);

    /// <summary>La versión publicada más reciente; null si aún no hay ninguna. Lanza si GitHub no responde.</summary>
    public static async Task<Release?> LatestAsync(CancellationToken ct = default)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("AlToque", AppInfo.Version));
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        using var response = await http.GetAsync(LatestApi, ct).ConfigureAwait(false);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);
        var root = doc.RootElement;
        if (!root.TryGetProperty("tag_name", out var tag) || tag.GetString() is not { Length: > 0 } name)
            return null;
        string? apk = null;
        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
        {
            foreach (var asset in assets.EnumerateArray())
            {
                if (asset.TryGetProperty("name", out var n) && n.GetString() is { } file
                    && asset.TryGetProperty("browser_download_url", out var url) && url.GetString() is { } link)
                {
                    files[file] = link;
                    if (apk is null && file.EndsWith(".apk", StringComparison.OrdinalIgnoreCase))
                        apk = link;
                }
            }
        }
        return new Release(name.TrimStart('v', 'V'), apk, files);
    }

    /// <summary>¿<paramref name="candidate"/> es posterior a <paramref name="current"/>? Ignora sufijos como "-beta".</summary>
    public static bool IsNewer(string candidate, string current) =>
        Version.TryParse(candidate.Split('-', '+')[0], out var a) && Version.TryParse(current.Split('-', '+')[0], out var b) && a > b;
}
