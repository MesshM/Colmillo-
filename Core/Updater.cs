using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;

namespace Colmillo.Core;

public sealed record UpdateInfo(Version Version, string Tag, string Notes, string SetupUrl, string? HashUrl, string PageUrl);

/// <summary>
/// Actualizaciones desde GitHub Releases. Es el unico codigo de red de Colmillo y solo
/// corre cuando el usuario aprieta "Buscar actualizaciones": no hay chequeo automatico.
/// </summary>
public static class Updater
{
    const string Repo = "MesshM/Colmillo-";
    const string SetupAsset = "Colmillo-Setup.exe";

    public static Version Current => typeof(Updater).Assembly.GetName().Version ?? new Version(0, 0);

    static HttpClient NewClient()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        // GitHub rechaza pedidos a su API sin User-Agent.
        http.DefaultRequestHeaders.UserAgent.ParseAdd($"Colmillo/{Current.ToString(3)}");
        return http;
    }

    /// <summary>Ultima version publicada si es mas nueva que esta; null si ya esta al dia.</summary>
    public static async Task<UpdateInfo?> CheckAsync()
    {
        using var http = NewClient();
        using var doc = JsonDocument.Parse(await http.GetStringAsync($"https://api.github.com/repos/{Repo}/releases/latest"));
        var root = doc.RootElement;
        var tag = root.GetProperty("tag_name").GetString() ?? "";
        if (!Version.TryParse(tag.TrimStart('v', 'V'), out var version)) return null;
        if (version <= new Version(Current.Major, Current.Minor, Math.Max(Current.Build, 0))) return null;

        string? setup = null, hash = null;
        foreach (var asset in root.GetProperty("assets").EnumerateArray())
        {
            var name = asset.GetProperty("name").GetString();
            var url = asset.GetProperty("browser_download_url").GetString();
            if (name == SetupAsset) setup = url;
            else if (name == SetupAsset + ".sha256") hash = url;
        }
        if (setup is null) return null;
        return new UpdateInfo(version, tag, root.GetProperty("body").GetString() ?? "", setup, hash,
            root.GetProperty("html_url").GetString() ?? $"https://github.com/{Repo}/releases");
    }

    /// <summary>
    /// Baja el instalador a una carpeta temporal y verifica su SHA-256 contra el publicado
    /// junto al release. Devuelve la ruta del instalador listo para ejecutar.
    /// </summary>
    public static async Task<string> DownloadAsync(UpdateInfo info, IProgress<double>? progress = null)
    {
        using var http = NewClient();
        http.Timeout = TimeSpan.FromMinutes(5);
        var dir = Path.Combine(Path.GetTempPath(), "Colmillo-actualizacion");
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, SetupAsset);

        using (var response = await http.GetAsync(info.SetupUrl, HttpCompletionOption.ResponseHeadersRead))
        {
            response.EnsureSuccessStatusCode();
            var total = response.Content.Headers.ContentLength ?? 0;
            await using var input = await response.Content.ReadAsStreamAsync();
            await using var output = File.Create(file);
            var buffer = new byte[81920];
            long read = 0;
            int n;
            while ((n = await input.ReadAsync(buffer)) > 0)
            {
                await output.WriteAsync(buffer.AsMemory(0, n));
                read += n;
                if (total > 0) progress?.Report((double)read / total);
            }
        }

        if (info.HashUrl is not null)
        {
            var expected = (await http.GetStringAsync(info.HashUrl)).Trim().Split(' ', '\t')[0];
            await using var stream = File.OpenRead(file);
            var actual = Convert.ToHexString(await SHA256.HashDataAsync(stream));
            if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(file);
                throw new InvalidDataException("El instalador descargado no coincide con el publicado. No se instalo nada.");
            }
        }
        return file;
    }
}
