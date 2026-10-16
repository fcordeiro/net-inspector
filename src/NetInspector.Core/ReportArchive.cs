using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace NetInspector.Core;

public sealed record Manifest(string Format, int Version, Dictionary<string, string> Sha256);
public sealed record Report(Session Session, Dictionary<string, string> WindowsLogs);
public static class ReportArchive
{
    public static void Write(string path, Session session, Dictionary<string, string> windowsLogs)
    {
        var entries = new Dictionary<string, byte[]> { ["session.json"] = JsonSerializer.SerializeToUtf8Bytes(session, Session.Json) };
        foreach (var (name, value) in windowsLogs)
        {
            if (Path.GetFileName(name) != name) throw new InvalidDataException("Nome de log inválido");
            entries["windows/" + name] = System.Text.Encoding.UTF8.GetBytes(value);
        }
        var manifest = new Manifest("netinspect", 1, entries.ToDictionary(e => e.Key, e => Convert.ToHexString(SHA256.HashData(e.Value))));
        entries["manifest.json"] = JsonSerializer.SerializeToUtf8Bytes(manifest, Session.Json);
        using (var file = File.Create(path + ".tmp"))
        using (var zip = new ZipArchive(file, ZipArchiveMode.Create))
            foreach (var (name, value) in entries)
            {
                using var stream = zip.CreateEntry(name, CompressionLevel.Optimal).Open();
                stream.Write(value);
            }
        File.Move(path + ".tmp", path, true);
    }
    public static Report Read(string path)
    {
        if (new FileInfo(path).Length > 64L * 1024 * 1024) throw new InvalidDataException("Arquivo excede 64 MB");
        using var zip = ZipFile.OpenRead(path);
        if (zip.Entries.Count > 32 || zip.Entries.Sum(e => e.Length) > 128L * 1024 * 1024) throw new InvalidDataException("Pacote excede os limites de leitura");
        var entries = new Dictionary<string, byte[]>();
        foreach (var entry in zip.Entries)
        {
            if (entry.FullName != "manifest.json" && entry.FullName != "session.json" && !(entry.FullName.StartsWith("windows/") && entry.FullName.Split('/').Length == 2 && !entry.FullName.Contains("..") && !entry.FullName.Contains('\\')))
                throw new InvalidDataException("Entrada não permitida");
            using var stream = entry.Open();
            using var memory = new MemoryStream();
            var buffer = new byte[65536];
            int read;
            while ((read = stream.Read(buffer)) > 0)
            {
                if (memory.Length + read > entry.Length || memory.Length + read > 128L * 1024 * 1024) throw new InvalidDataException("Entrada excedeu o tamanho declarado");
                memory.Write(buffer, 0, read);
            }
            if (!entries.TryAdd(entry.FullName, memory.ToArray())) throw new InvalidDataException("Entrada duplicada");
        }
        if (!entries.TryGetValue("manifest.json", out var manifestBytes)) throw new InvalidDataException("Manifesto ausente");
        var manifest = JsonSerializer.Deserialize<Manifest>(manifestBytes, Session.Json) ?? throw new InvalidDataException("Manifesto inválido");
        if (manifest.Format != "netinspect" || manifest.Version != 1 || manifest.Sha256 == null) throw new InvalidDataException("Formato não suportado");
        if (manifest.Sha256.Count != entries.Count - 1) throw new InvalidDataException("Manifesto incompleto");
        foreach (var (name, hash) in manifest.Sha256)
            if (name == "manifest.json" || !entries.TryGetValue(name, out var bytes) || !string.Equals(Convert.ToHexString(SHA256.HashData(bytes)), hash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Falha de integridade: " + name);
        if (!entries.TryGetValue("session.json", out var sessionBytes)) throw new InvalidDataException("Sessão ausente");
        var session = JsonSerializer.Deserialize<Session>(sessionBytes, Session.Json) ?? throw new InvalidDataException("Sessão inválida");
        ValidateSession(session);
        return new(session, entries.Where(e => e.Key.StartsWith("windows/")).ToDictionary(e => e.Key, e => System.Text.Encoding.UTF8.GetString(e.Value)));
    }
    public static void ValidateSession(Session session)
    {
        if (session.Resources == null || session.SchemaVersion != 1 || session.Machine == null || session.Pings == null || session.Services == null || session.Speeds == null || session.Events == null || session.Network == null || session.Machine.Adapters == null || session.Machine.Extensions == null || session.Machine.Limitations == null || session.Machine.Details == null || session.Machine.DefaultBrowser == null || session.ProviderStatus == null || session.GameServers == null || session.ConfiguredGameServers == null)
            throw new InvalidDataException("Esquema de sessão inválido");
        if (session.Pings.Any(p => p == null || p.Target == null || p.Ms is < 0 || (p.Ms.HasValue && !double.IsFinite(p.Ms.Value))) || session.Services.Any(p => p == null || p.Name == null || p.Outcome == null) || session.Speeds.Any(p => p == null || p.Direction == null || p.Bytes < 0 || p.Mbps is < 0 || (p.Mbps.HasValue && !double.IsFinite(p.Mbps.Value))) || session.Network.Any(p => p == null || p.Adapter == null) || session.Events.Any(p => p == null))
            throw new InvalidDataException("Medições inválidas");
        if (session.Resources.Any(r => r == null || (r.Power?.Percent is { } battery && (battery < 0 || battery > 100)) || (r.Power?.RemainingSeconds is < 0) || (r.CpuPercent is { } cpu && (!double.IsFinite(cpu) || cpu < 0 || cpu > 100)) || (r.MemoryPercent is { } memory && (!double.IsFinite(memory) || memory < 0 || memory > 100)) || (r.UsedMemoryBytes.HasValue && r.TotalMemoryBytes.HasValue && r.UsedMemoryBytes > r.TotalMemoryBytes))) throw new InvalidDataException("Medições de recursos inválidas");
        if (session.ProviderStatus.Any(p => p == null || p.Provider == null || p.Components == null || p.Incidents == null) || session.GameServers.Any(p => p == null || p.Name == null || p.Host == null || (p.Ms.HasValue && (!double.IsFinite(p.Ms.Value) || p.Ms < 0)))) throw new InvalidDataException("Medições de jogos inválidas");
        if (session.Machine.Adapters.Any(a => a == null || a.Addresses == null || a.Dns == null || a.Gateways == null || a.Type == null) || session.Machine.Extensions.Any(e => e == null || e.Name == null)) throw new InvalidDataException("Inventário inválido");
    }
}
