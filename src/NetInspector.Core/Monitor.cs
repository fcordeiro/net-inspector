using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.Json;

namespace NetInspector.Core;

public sealed class Monitor : IDisposable
{
    public static TimeSpan SpeedInterval(int requestedMinutes) => TimeSpan.FromSeconds(Math.Clamp(requestedMinutes * 60.0 / 5, 30, 900));
    public Session Data { get; }
    private readonly object gate = new();
    private readonly HttpClient http = new(new SocketsHttpHandler { AllowAutoRedirect = false, ConnectTimeout = TimeSpan.FromSeconds(5), AutomaticDecompression = DecompressionMethods.None }) { Timeout = Timeout.InfiniteTimeSpan };
    private int underLoad;
    private int loadEpoch;
    private bool budgetReported;
    public Monitor(int minutes, bool speed, long budget, IEnumerable<GameTarget>? gameTargets = null)
    {
        Data = new() { RequestedMinutes = Math.Clamp(minutes, TestPolicy.MinimumMinutes, TestPolicy.MaximumMinutes), SpeedEnabled = speed, SpeedBudgetBytes = budget };
        Data.ConfiguredGameServers = (gameTargets ?? GameCatalog.Defaults).ToList();
        if (Data.ConfiguredGameServers.Count > 10) throw new InvalidDataException("Máximo de 10 servidores por coleta");
        foreach (var target in Data.ConfiguredGameServers) GameCatalog.Validate(target);
        http.DefaultRequestHeaders.UserAgent.ParseAdd("NetInspector/1.0");
    }
    public Session Snapshot()
    {
        // Samples are immutable records. Copy collection containers instead of serializing the full history on every UI tick.
        lock (gate) return new Session
        {
            SchemaVersion = Data.SchemaVersion, AppVersion = Data.AppVersion, Copyright = Data.Copyright,
            Id = Data.Id, StartedAt = Data.StartedAt, EndedAt = Data.EndedAt, RequestedMinutes = Data.RequestedMinutes,
            State = Data.State, SpeedEnabled = Data.SpeedEnabled, SpeedBudgetBytes = Data.SpeedBudgetBytes,
            Machine = new Inventory { Os = Data.Machine.Os, DefaultBrowser = Data.Machine.DefaultBrowser,
                PrimaryAdapterId = Data.Machine.PrimaryAdapterId, Gateway = Data.Machine.Gateway,
                Adapters = Data.Machine.Adapters.Select(a => a with { Addresses = [.. a.Addresses], Gateways = [.. a.Gateways], Dns = [.. a.Dns] }).ToList(), Extensions = [.. Data.Machine.Extensions],
                Details = new(Data.Machine.Details), Limitations = [.. Data.Machine.Limitations] },
            Pings = [.. Data.Pings], Services = [.. Data.Services], Speeds = [.. Data.Speeds], Events = [.. Data.Events],
            Network = [.. Data.Network], Resources = [.. Data.Resources], ProviderStatus = Data.ProviderStatus.Select(p => p with { Components = [.. p.Components], Incidents = [.. p.Incidents] }).ToList(),
            GameServers = [.. Data.GameServers], ConfiguredGameServers = [.. Data.ConfiguredGameServers]
        };
    }
    public void Log(string level, string message) { lock (gate) Data.Events.Add(new(DateTimeOffset.UtcNow, level, message)); }
    public async Task Run(string checkpoint, CancellationToken stop)
    {
        using var duration = CancellationTokenSource.CreateLinkedTokenSource(stop);
        duration.CancelAfter(TimeSpan.FromMinutes(Data.RequestedMinutes));
        var ct = duration.Token;
        Log("info", $"Coleta iniciada. ICMP a cada 2s, serviços a cada 60s, velocidade a cada {SpeedInterval(Data.RequestedMinutes).TotalSeconds:0}s (4 fluxos, 8s/direção). O Windows pode suspender a execução durante suspensão do PC.");
        var resources = ResourceLoop(ct);
        try
        {
            var inventory = await WindowsInventory.Collect(ct);
            lock (gate) Data.Machine = inventory;
            async Task Guard(Func<Task> work)
            {
                try { await work(); }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
                catch (Exception ex) { Log("error", ex.Message); lock (gate) Data.State = "error"; await duration.CancelAsync(); }
            }
            await Task.WhenAll(resources, Guard(() => PingLoop(ct)), Guard(() => ServiceLoop(ct)), Guard(() => SpeedLoop(ct)), Guard(() => GameLoop(ct)), Guard(() => StatusLoop(ct)), Guard(() => NetworkLoop(ct)), Guard(() => CheckpointLoop(checkpoint, ct)));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception ex) { Log("error", ex.Message); lock (gate) Data.State = "error"; }
        finally
        {
            await duration.CancelAsync();
            await resources;
            lock (gate) { Data.EndedAt = DateTimeOffset.UtcNow; if (Data.State != "error") Data.State = stop.IsCancellationRequested ? "stopped" : "completed"; }
            Log("info", "Recursos do computador — " + ResourceSummary.Describe(Snapshot()));
            await SaveCheckpoint(checkpoint);
        }
    }
    private async Task Repeat(Func<Task> action, TimeSpan interval, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var watch = Stopwatch.StartNew();
            await action();
            var remaining = interval - watch.Elapsed;
            if (remaining > TimeSpan.Zero) await Task.Delay(remaining, ct);
        }
    }
    private async Task ResourceLoop(CancellationToken ct)
    {
        var reader = new SystemResources();
        try
        {
            await Repeat(() =>
            {
                var sample = reader.Read();
                lock (gate) Data.Resources.Add(sample);
                var cpu = sample.CpuPercent is { } cpuValue ? $"{cpuValue:F1}%" : "aguardando medição";
                var memory = sample.MemoryPercent is { } memoryValue ? $"{memoryValue:F1}%" : "não medida";
                Log(sample.Error == null ? "info" : "warning", $"Recursos do computador: CPU {cpu}; memória física {memory}" + (sample.Error == null ? "" : "; " + sample.Error));
                return Task.CompletedTask;
            }, TimeSpan.FromSeconds(2), ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception ex) { Log("warning", "Monitoramento de CPU/memória indisponível: " + ex.Message); }
    }
    private Task PingLoop(CancellationToken ct) => Repeat(async () =>
    {
        var targets = new List<(string, string)> { ("Cloudflare", "1.1.1.1"), ("Google", "8.8.8.8"), ("Quad9", "9.9.9.9") };
        if (Data.Machine.Gateway is { } gateway) targets.Insert(0, ("Gateway", gateway));
        await Task.WhenAll(targets.Select(async target =>
        {
            using var ping = new Ping();
            var loaded = Volatile.Read(ref underLoad) != 0;
            var epoch = Volatile.Read(ref loadEpoch);
            try
            {
                var reply = await ping.SendPingAsync(target.Item2, TimeSpan.FromMilliseconds(1500), new byte[32], null, ct);
                loaded |= Volatile.Read(ref underLoad) != 0 || Volatile.Read(ref loadEpoch) != epoch;
                lock (gate) Data.Pings.Add(new(DateTimeOffset.UtcNow, target.Item1, target.Item2, reply.Status == IPStatus.Success, reply.Status == IPStatus.Success ? reply.RoundtripTime : null, reply.Status.ToString(), loaded));
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
            catch (Exception ex) { loaded |= Volatile.Read(ref underLoad) != 0 || Volatile.Read(ref loadEpoch) != epoch; lock (gate) Data.Pings.Add(new(DateTimeOffset.UtcNow, target.Item1, target.Item2, false, null, ex.Message, loaded)); }
        }));
    }, TimeSpan.FromSeconds(2), ct);

    private Task ServiceLoop(CancellationToken ct) => Repeat(async () =>
    {
        using var slots = new SemaphoreSlim(4);
        await Task.WhenAll(Catalog.Services.Select(async target =>
        {
            await slots.WaitAsync(ct);
            try { var sample = await Probe(target, ct); lock (gate) Data.Services.Add(sample); }
            finally { slots.Release(); }
        }));
    }, TimeSpan.FromMinutes(1), ct);

    public async Task<ServiceSample> Probe(Target target, CancellationToken ct)
    {
        double? dns = null, tcp = null, https = null;
        var at = DateTimeOffset.UtcNow;
        var phase = "DNS";
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(12));
        try
        {
            var timer = Stopwatch.StartNew();
            var addresses = await Dns.GetHostAddressesAsync(target.Host, timeout.Token);
            dns = timer.Elapsed.TotalMilliseconds;
            if (addresses.Length == 0) throw new IOException("DNS sem endereços");
            phase = "TCP";
            timer.Restart();
            using var client = new TcpClient();
            await client.ConnectAsync(addresses, 443, timeout.Token);
            tcp = timer.Elapsed.TotalMilliseconds;
            phase = "HTTPS";
            timer.Restart();
            using var request = new HttpRequestMessage(HttpMethod.Get, "https://" + target.Host + "/");
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            https = timer.Elapsed.TotalMilliseconds;
            var status = (int)response.StatusCode;
            var outcome = status is >= 200 and < 400 ? "reachable" : status is 401 or 403 or 429 ? "restricted" : "http_error";
            return new(at, target.Name, target.Host, target.Category, dns, tcp, https, status, outcome, null);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) { return new(at, target.Name, target.Host, target.Category, dns, tcp, https, null, phase + "_failed", ex is OperationCanceledException ? "Tempo limite de 12s" : ex.Message); }
    }
    private Task SpeedLoop(CancellationToken ct) => Repeat(async () =>
    {
        if (!Data.SpeedEnabled) return;
        foreach (var direction in new[] { "download", "upload" })
        {
            long consumed;
            lock (gate) consumed = Data.Speeds.Sum(s => s.BudgetBytes > 0 ? s.BudgetBytes : s.Bytes);
            var available = Data.SpeedBudgetBytes - consumed;
            var allowance = direction == "download" ? available / 2 : available;
            if (allowance < 256_000)
            {
                if (!budgetReported) { Log("warning", "Limite de dados dos testes de velocidade atingido; as demais sondagens continuam."); budgetReported = true; }
                return;
            }
            Interlocked.Exchange(ref underLoad, 1);
            Interlocked.Increment(ref loadEpoch);
            try
            {
                var sample = await new SpeedTester(http).Measure(direction, allowance, ct);
                lock (gate) Data.Speeds.Add(sample);
            }
            finally { Interlocked.Exchange(ref underLoad, 0); }
            ct.ThrowIfCancellationRequested();
        }
    }, SpeedInterval(Data.RequestedMinutes), ct);
    private Task CheckpointLoop(string path, CancellationToken ct) => Repeat(() => SaveCheckpoint(path), TimeSpan.FromSeconds(15), ct);
    private Task GameLoop(CancellationToken ct) => Repeat(async () =>
    {
        foreach (var target in Data.ConfiguredGameServers)
        {
            var sample = await GameProbe.Check(target, ct);
            lock (gate) Data.GameServers.Add(sample);
        }
    }, TimeSpan.FromMinutes(1), ct);
    private Task StatusLoop(CancellationToken ct) => Repeat(async () =>
    {
        foreach (var provider in GameCatalog.Providers)
        {
            if (provider.Api == null && Data.ProviderStatus.Any(p => p.Provider == provider.Name)) continue;
            var sample = await new ProviderProbe(http).Check(provider, ct);
            lock (gate) Data.ProviderStatus.Add(sample);
        }
    }, TimeSpan.FromMinutes(5), ct);
    private Task NetworkLoop(CancellationToken ct) => Repeat(() =>
    {
        var primary = WindowsInventory.ProbeRouteAdapter();
        foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (adapter.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
            try
            {
                var stats = adapter.GetIPStatistics();
                lock (gate) Data.Network.Add(new(DateTimeOffset.UtcNow, adapter.Name, adapter.NetworkInterfaceType.ToString(), adapter.OperationalStatus.ToString(), adapter.Speed, stats.BytesReceived, stats.BytesSent, stats.IncomingPacketsWithErrors, stats.OutgoingPacketsWithErrors, primary == null ? null : adapter.Id == primary));
            }
            catch (NetworkInformationException ex) { Log("warning", "Contadores de rede: " + ex.Message); }
        }
        return Task.CompletedTask;
    }, TimeSpan.FromSeconds(10), ct);
    public async Task SaveCheckpoint(string path)
    {
        var json = JsonSerializer.Serialize(Snapshot(), Session.Json);
        await File.WriteAllTextAsync(path + ".tmp", json);
        File.Move(path + ".tmp", path, true);
    }
    public void Dispose() => http.Dispose();
}
