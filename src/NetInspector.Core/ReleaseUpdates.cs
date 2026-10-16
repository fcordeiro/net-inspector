using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace NetInspector.Core;

public sealed record ReleaseUpdate(Version Version, string Page, string AssetUrl, string AssetName);
public static class ReleaseUpdates
{
    public const string Repository = "fcordeiro/net-inspector";
    public const string ReleasesPage = "https://github.com/" + Repository + "/releases";
    public static string AssetName => System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture == System.Runtime.InteropServices.Architecture.Arm64 ? "NetInspector-win-arm64.exe" : "NetInspector-win-x64.exe";
    public static Version Current => typeof(ReleaseUpdates).Assembly.GetName().Version ?? new Version(1, 5, 0);
    public static ReleaseUpdate? Parse(string json, string assetName)
    {
        using var doc = JsonDocument.Parse(json); var root = doc.RootElement;
        if (root.GetProperty("draft").GetBoolean() || root.GetProperty("prerelease").GetBoolean()) return null;
        var tag = root.GetProperty("tag_name").GetString()?.TrimStart('v');
        if (!Version.TryParse(tag, out var version)) throw new InvalidDataException("Versão publicada inválida.");
        var page = root.GetProperty("html_url").GetString();
        if (!IsReleaseUrl(page)) throw new InvalidDataException("Endereço da release inválido.");
        foreach (var asset in root.GetProperty("assets").EnumerateArray())
        {
            if (asset.GetProperty("name").GetString() != assetName || asset.GetProperty("size").GetInt64() <= 0 || asset.GetProperty("state").GetString() != "uploaded") continue;
            var url = asset.GetProperty("browser_download_url").GetString();
            if (!IsReleaseUrl(url) || !url!.Contains("/releases/download/", StringComparison.Ordinal)) continue;
            return new(version, page!, url, assetName);
        }
        throw new InvalidDataException("A release ainda não contém o executável compatível. Tente novamente mais tarde.");
    }
    private static bool IsReleaseUrl(string? value) => Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == "https" && uri.Host == "github.com" && uri.AbsolutePath.StartsWith("/" + Repository + "/releases/", StringComparison.Ordinal);
    public static bool IsNewer(Version candidate, Version current) => new Version(candidate.Major, candidate.Minor, Math.Max(0, candidate.Build)) > new Version(current.Major, current.Minor, Math.Max(0, current.Build));
    public static async Task<ReleaseUpdate?> Check(HttpClient client, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromSeconds(15));
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/repos/" + Repository + "/releases/latest");
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("NetInspector", Current.ToString(3)));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        if (response.StatusCode == HttpStatusCode.NotFound) throw new IOException("Nenhuma release pública disponível ainda.");
        if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests) throw new IOException("Limite de consultas do GitHub atingido. Tente novamente mais tarde.");
        response.EnsureSuccessStatusCode();
        using var stream = await response.Content.ReadAsStreamAsync(timeout.Token); using var buffer = new MemoryStream();
        var chunk = new byte[8192]; int count;
        while ((count = await stream.ReadAsync(chunk, timeout.Token)) > 0)
        {
            if (buffer.Length + count > 2_000_000) throw new InvalidDataException("Resposta de atualização excedeu o limite.");
            buffer.Write(chunk, 0, count);
        }
        return Parse(System.Text.Encoding.UTF8.GetString(buffer.ToArray()), AssetName);
    }
}
