using System.Text.Json;

namespace NetInspector.Core;

public static class Branding
{
    public static string Copyright => $"Copyright © {DateTime.Now.Year} Fernando J. Cordeiro @ CONNECT10 INTERNET";
}

public sealed record Target(string Name, string Host, string Category);
public static class Catalog
{
    public static readonly Target[] Services = [
        new("Facebook", "www.facebook.com", "Social"), new("Instagram", "www.instagram.com", "Social"),
        new("TikTok", "www.tiktok.com", "Social"), new("YouTube", "www.youtube.com", "Vídeo"),
        new("X", "x.com", "Social"), new("Globoplay", "globoplay.globo.com", "Streaming"),
        new("Netflix", "www.netflix.com", "Streaming"), new("HBO / Max", "www.hbomax.com", "Streaming"),
        new("Amazon Prime Video", "www.primevideo.com", "Streaming")];
}
public sealed record AdapterInfo(string Id, string Name, string Type, string Status, long LinkBps, string[] Addresses, string[] Gateways, string[] Dns);
public sealed record ExtensionInfo(string Browser, string Profile, string Id, string Name, string Version, string State);
public sealed class Inventory
{
    public string Os { get; set; } = Environment.OSVersion.ToString();
    public string DefaultBrowser { get; set; } = "Não identificado";
    public List<AdapterInfo> Adapters { get; set; } = [];
    public List<ExtensionInfo> Extensions { get; set; } = [];
    public Dictionary<string, string> Details { get; set; } = [];
    public List<string> Limitations { get; set; } = [];
    public string? PrimaryAdapterId { get; set; }
    public string? Gateway { get; set; }
}
public sealed record PingSample(DateTimeOffset At, string Target, string Address, bool Success, double? Ms, string Status, bool UnderLoad);
public sealed record ServiceSample(DateTimeOffset At, string Name, string Host, string Category, double? DnsMs, double? TcpMs, double? HttpsMs, int? HttpStatus, string Outcome, string? Error);
public sealed record SpeedSample(DateTimeOffset At, string Direction, double? Mbps, long Bytes, double Seconds, string? Error, long BudgetBytes = 0, int Streams = 1, double MeasurementSeconds = 0);
public sealed record AppEvent(DateTimeOffset At, string Level, string Message);
public sealed record ResourceSample(DateTimeOffset At, double? CpuPercent, double? MemoryPercent, ulong? UsedMemoryBytes, ulong? TotalMemoryBytes, string? Error = null, PowerSample? Power = null);
public sealed record NetworkSample(DateTimeOffset At, string Adapter, string Type, string Status, long LinkBps, long ReceivedBytes, long SentBytes, long ReceiveErrors, long SendErrors, bool? IsPrimary = null);
public sealed class Session
{
    public int SchemaVersion { get; set; } = 1;
    public string AppVersion { get; set; } = typeof(Session).Assembly.GetName().Version?.ToString(3) ?? "1.5.0";
    public string Copyright { get; set; } = Branding.Copyright;
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? EndedAt { get; set; }
    public int RequestedMinutes { get; set; }
    public string State { get; set; } = "running";
    public bool SpeedEnabled { get; set; }
    public long SpeedBudgetBytes { get; set; }
    public Inventory Machine { get; set; } = new();
    public List<PingSample> Pings { get; set; } = [];
    public List<ServiceSample> Services { get; set; } = [];
    public List<SpeedSample> Speeds { get; set; } = [];
    public List<AppEvent> Events { get; set; } = [];
    public List<NetworkSample> Network { get; set; } = [];
    public List<ResourceSample> Resources { get; set; } = [];
    public List<ProviderSample> ProviderStatus { get; set; } = [];
    public List<GameSample> GameServers { get; set; } = [];
    public List<GameTarget> ConfiguredGameServers { get; set; } = [];
    public static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true };
}

public sealed record PowerSample(bool? OnAcPower, bool? HasBattery, int? Percent, bool? Charging, int? RemainingSeconds);
